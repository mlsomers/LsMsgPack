using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Converts unpacked values (null, a primitive or extension value, object[] for arrays, KeyValuePair&lt;object, object&gt;[] for maps) into the types they are assigned to.
  /// <para>Shared by the serializers: LsMsgPack converts every value this way, LtMsgPack the ones its typed readers leave to it (polymorphism, dictionaries, unexpected formats).</para>
  /// </summary>
  internal static class ValueConverter
  {
    /// <summary>
    /// Single entry point for converting an unpacked value into the type it will be assigned to.
    /// <para>Unpacked values are either null, a primitive (or extension) value, an object[] (MsgPack array) or a KeyValuePair&lt;object, object&gt;[] (MsgPack map).</para>
    /// </summary>
    /// <param name="val">The unpacked value (LsMsgPack: MsgPackItem.UnpackedValue)</param>
    /// <param name="assignType">The type the result will be assigned to</param>
    /// <param name="settings">Settings used for unpacking</param>
    /// <param name="prop">The property the result will be assigned to (null for the root object, collection elements and dictionary entries)</param>
    internal static object ConvertDeserializeValue(object val, Type assignType, MsgPackOptions settings, FullPropertyInfo prop)
    {
      if (val is null)
        return null;

      if (val is KeyValuePair<object, object>[] map)
        return ConvertMap(map, assignType, settings, prop);

      if (val is object[] items)
        return ConvertArray(items, assignType, settings, prop);

      if (val is IMsgPackExtension extension) // only extensions without a registered type (LsMsgPack: MpExt, see MsgPackItem.UnpackedValue)
        return ConvertExtension(extension, assignType);

      return ConvertScalar(val, assignType);
    }

    /// <summary>
    /// An extension without a registered type (see <see cref="MsgPackSettings.CustomExtentionTypes"/>) is only assigned as itself (e.g. to object), or as its bytes to a byte[].
    /// <para>Converting its bytes to anything else would silently misread data, another library may use the type for something else (e.g. a Guid in a different byte order).</para>
    /// </summary>
    private static object ConvertExtension(IMsgPackExtension extension, Type assignType)
    {
      if (assignType.IsInstanceOfType(extension))
        return extension;

      if (assignType == typeof(byte[]))
        return extension.Data;

      throw new MsgPackException($"Unable to convert extension type {extension.TypeSpecifier} ({extension.Count} bytes) to {assignType.FullName}. To read it, add a custom extension for type {extension.TypeSpecifier} to MsgPackSettings.CustomExtentionTypes.", 0, extension.TypeId);
    }

    /// <summary>
    /// A map can be:
    /// <list type="bullet">
    /// <item>An object with properties (optionally including a type identifier).</item>
    /// <item>A wrapper around a value that needed a type identifier or (optionally) other properties: { "": typeId, "@": content }</item>
    /// <item>The entries of a dictionary (when serialized without a wrapper).</item>
    /// </list>
    /// </summary>
    private static object ConvertMap(KeyValuePair<object, object>[] map, Type assignType, MsgPackOptions settings, FullPropertyInfo prop)
    {
      if (assignType == typeof(KeyValuePair<object, object>[]))
        return map;

      Dictionary<object, object> propVals = new Dictionary<object, object>(map.Length, MapConversionEqualityComparer.Instance);
      for (int t = map.Length - 1; t >= 0; t--)
      {
        if (!(map[t].Key is null))
          propVals[map[t].Key] = map[t].Value;
      }

      bool hasTypeId = propVals.TryGetValue(MsgPackOptions.TypeIdKey, out object typeId);
      Type tType = TypeResolver.Resolve(typeId, assignType, prop, settings, propVals);

      object result;
      if (propVals.TryGetValue(MsgPackOptions.ContentKey, out object content))
      {
        result = content is KeyValuePair<object, object>[] pairs && IsDictionary(tType)
          ? ConvertPairs(pairs, tType, settings) // the content of a dictionary is always a map of entries, never an object
          : ConvertDeserializeValue(content, tType, settings, null);

        int reservedKeys = hasTypeId ? 2 : 1;
        if (propVals.Count > reservedKeys && !(result is null)) // properties serialized along with the content (see SerializeEnumerableAttribute.SerializeProperties)
          SetProperties(result, result.GetType(), propVals, settings);

        return result;
      }

      if (IsDictionary(tType) && !ElementsOmitted(tType, prop))
        return ConvertPairs(map, tType, settings);

      if (tType.IsInstanceOfType(map)) // object, IEnumerable, ... there is nothing more specific known, keep the raw map
        return map;

      if (tType.IsAbstract || tType.IsInterface)
        throw new Exception(
          $"Cannot create an instance of an interface or abstract type:\r\n  {tType.FullName}\r\nEither use MsgPackSettings.AddTypeIdOptions when serializing (easiest but adds payload) or add a custom IMsgPackTypeResolver to MsgPackSettings._typeResolvers.");

      if (propVals.Count > (hasTypeId ? 1 : 0) && SerializationRules.GetIndexedSchema(settings)?.SkipIfNoEntry(tType, settings, prop) == true)
        return null;
      result = Instances.CreateObject(tType, settings);
      SetProperties(result, tType, propVals, settings);
      return result;
    }

    /// <summary>
    /// A collection serialized with <see cref="SerializeEnumerableAttribute.SerializeElements"/> = false is a map of properties (not of dictionary entries).
    /// </summary>
    internal static bool ElementsOmitted(Type tType, FullPropertyInfo prop)
    {
      SerializeEnumerableAttribute att = null;
      if (prop?.CustomAttributes != null && prop.CustomAttributes.TryGetValue(nameof(SerializeEnumerableAttribute), out object propAtt))
        att = (SerializeEnumerableAttribute)propAtt;
      else
        att = CollectionInfo.Get(tType).Attribute;

      return att != null && !att.SerializeElements;
    }

    private static void SetProperties(object instance, Type tType, Dictionary<object, object> propVals, MsgPackOptions settings)
    {
      FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(tType, settings);
      ReadDifferences differences = settings._differences;
      if (differences != null)
      {
        ReportUnknownKeys(instance, tType, props, propVals, settings); // before the values (the objects in them), as LtMsgPack finds them
        differences.Push(instance);
      }
      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo prop = props[t];
        if (propVals.TryGetValue(prop.PropertyId, out object propval))
        {
          prop.SetValue(instance, ConvertDeserializeValue(propval, prop.PropertyInfo.PropertyType, settings, prop));
        }
      }
      differences?.Pop();
    }

    /// <summary>
    /// The keys of the map that are not properties of the class (only when the differences are collected).
    /// </summary>
    private static void ReportUnknownKeys(object instance, Type tType, FullPropertyInfo[] props, Dictionary<object, object> propVals, MsgPackOptions settings)
    {
      HashSet<object> known = new HashSet<object>(MapConversionEqualityComparer.Instance);
      for (int t = 0; t < props.Length; t++)
        known.Add(props[t].PropertyId);

      List<object> unknown = null;
      foreach (object key in propVals.Keys)
      {
        if (MsgPackOptions.TypeIdKey.Equals(key) || MsgPackOptions.ContentKey.Equals(key) || known.Contains(key))
          continue;
        (unknown ?? (unknown = new List<object>())).Add(key);
      }
      if (unknown is null)
        return;

      ComplexTypeDef def = null;
      SerializationRules.GetIndexedSchema(settings)?.ByType.TryGetValue(tType, out def); // the keys are indexes into the writer's names
      for (int t = unknown.Count - 1; t >= 0; t--) // propVals was filled from the last entry of the map to the first
        settings._differences.UnknownProperty(instance, NameOf(unknown[t], def));
    }

    /// <returns>The name of the property with this index in the schema entry, otherwise the key</returns>
    private static object NameOf(object key, ComplexTypeDef def)
    {
      if (def is null || key is null || !key.GetType().IsPrimitive || key is bool || key is char || key is float || key is double)
        return key;
      long index = Convert.ToInt64(key, CultureInfo.InvariantCulture);
      return index >= 0 && index < def.Props.Count && def.Props[(int)index] != null ? def.Props[(int)index] : key;
    }

    /// <summary>
    /// Converts the items of a MsgPack array into an array or collection of the given type, or into an object written as an array (<see cref="ObjectLayout.Array"/>).
    /// </summary>
    private static object ConvertArray(object[] items, Type assignType, MsgPackOptions settings, FullPropertyInfo prop)
    {
      if (IsDictionary(assignType) && !Array.TrueForAll(items, i => i is object[] pair && pair.Length == 2)) // only [key, value] pairs can be entries
        throw new MsgPackException($"An array cannot be read into {assignType}: the data is a list, or an object written as an array of its values (ObjectLayout.Array, the default), which has no property names. "
          + "Read it into a class with the properties, or write it with ObjectLayout.Map.");

      if (!IsCollection(assignType))
      {
        if (!assignType.IsInstanceOfType(items))
        {
          Type objectType = Nullable.GetUnderlyingType(assignType) ?? assignType;
          if (items.Length == 2 && FrameworkTypeInfo.IsKeyValuePair(objectType)) // [key, value]
          {
            FrameworkTypeInfo.PairInfo pair = FrameworkTypeInfo.GetPair(objectType);
            return pair.Create(ConvertDeserializeValue(items[0], pair.KeyInfo.AssignedToType, settings, null), ConvertDeserializeValue(items[1], pair.ValueInfo.AssignedToType, settings, null));
          }
          if (IsObjectType(objectType))
          {
            if (Array.Exists(items, i => i != null) && SerializationRules.GetIndexedSchema(settings)?.SkipIfNoEntry(objectType, settings, prop) == true) // before the ids are resolved, which would add an entry for the class
              return null;
            FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(objectType, settings);
            if (props.Length > 0 || items.Length == 0)
              return ConvertPositional(items, objectType, props, settings);
          }
          return items; // will probably fail when assigned, but not our call to make
        }
        assignType = typeof(object[]); // object, still convert the elements (they may contain type identifiers)
      }

      CollectionInfo info = CollectionInfo.Get(assignType);
      Array elements = Array.CreateInstance(info.ElementType, items.Length);
      for (int t = items.Length - 1; t >= 0; t--)
        elements.SetValue(ConvertDeserializeValue(items[t], info.ElementType, settings, null), t);

      return info.Create(elements, settings);
    }

    /// <summary>
    /// A type that is written as an object with properties (not a value the serializers write themselves, like a DateTime or a Guid, which have no settable properties).
    /// </summary>
    private static bool IsObjectType(Type type)
    {
      return !type.IsPrimitive && !type.IsEnum && type != typeof(string) && type != typeof(decimal) && !type.IsAbstract && !type.IsInterface && !type.ContainsGenericParameters;
    }

    /// <summary>
    /// The values of an object written as an array: by position in the order of the properties, or with the indexed schema by the names of the schema.
    /// <para>A nil or missing value leaves the property as the constructor made it (like a property that is not in a map), values after the known properties are skipped.</para>
    /// </summary>
    private static object ConvertPositional(object[] items, Type type, FullPropertyInfo[] props, MsgPackOptions settings)
    {
      FullPropertyInfo[] byPosition = PropertiesByPosition(type, props, settings, out ComplexTypeDef def);
      object result = Instances.CreateObject(type, settings);
      ReadDifferences differences = settings._differences;
      differences?.Push(result);
      int count = Math.Min(items.Length, byPosition.Length);
      for (int t = 0; t < count; t++)
      {
        FullPropertyInfo prop = byPosition[t];
        if (prop is null || items[t] is null)
        {
          if (differences != null && items[t] != null) // a nil is a value left out (as a key that is not in a map)
            differences.UnknownProperty(result, NameOf(t, def));
          continue;
        }
        prop.SetValue(result, ConvertDeserializeValue(items[t], prop.PropertyInfo.PropertyType, settings, prop));
      }
      if (differences != null)
      {
        differences.Pop();
        for (int t = count; t < items.Length; t++)
          if (items[t] != null)
            differences.ExtraValue(result, t);
      }
      return result;
    }

    /// <returns>The property of each position: the properties themselves, or with the indexed schema the ones named by the schema of the data (null when not a property here)</returns>
    /// <param name="def">The schema entry the positions are of (null without)</param>
    private static FullPropertyInfo[] PropertiesByPosition(Type type, FullPropertyInfo[] props, MsgPackOptions settings, out ComplexTypeDef def)
    {
      IndexedSchemaTypeResolver schema = SerializationRules.GetIndexedSchema(settings);
      if (schema is null || !schema.TryGetDef(type, out def) || def.IsCollection)
      {
        def = null;
        return props;
      }

      FullPropertyInfo[] byPosition = new FullPropertyInfo[def.Props.Count];
      int next = 0; // usually in the same order
      for (int t = 0; t < byPosition.Length; t++)
      {
        string name = def.Props[t];
        if (name is null)
          continue;

        if (next < props.Length && props[next].PropertyInfo.Name == name)
        {
          byPosition[t] = props[next++];
          continue;
        }

        for (int p = 0; p < props.Length; p++)
        {
          if (props[p].PropertyInfo.Name == name)
          {
            byPosition[t] = props[p];
            next = p + 1;
            break;
          }
        }
      }
      return byPosition;
    }

    /// <summary>
    /// Converts the entries of a MsgPack map into a dictionary (or KeyValuePair&lt;,&gt;[]) of the given type.
    /// </summary>
    private static object ConvertPairs(KeyValuePair<object, object>[] pairs, Type assignType, MsgPackOptions settings)
    {
      CollectionInfo info = CollectionInfo.Get(assignType);

      if (info.ConcreteType.IsArray)
      {
        Array typedArr = Array.CreateInstance(info.ElementType, pairs.Length);
        for (int t = pairs.Length - 1; t >= 0; t--)
        {
          object key = ConvertKey(pairs[t].Key, info.KeyType, settings);
          object value = ConvertDeserializeValue(pairs[t].Value, info.ValueType, settings, null);
          typedArr.SetValue(Activator.CreateInstance(info.ElementType, key, value), t);
        }
        return typedArr;
      }

      if (info.FillsPairs) // a collection of KeyValuePair elements that is not a dictionary
      {
        Array elements = Array.CreateInstance(info.ElementType, pairs.Length);
        for (int t = 0; t < pairs.Length; t++)
        {
          object key = ConvertKey(pairs[t].Key, info.KeyType, settings);
          object value = ConvertDeserializeValue(pairs[t].Value, info.ValueType, settings, null);
          elements.SetValue(Activator.CreateInstance(info.ElementType, key, value), t);
        }
        return info.Create(elements, settings);
      }

      object result = Instances.CreateCollection(info.ConcreteType, settings);
      IDictionary dictionary = result as IDictionary;
      object[] args = dictionary is null ? new object[2] : null;
      for (int t = 0; t < pairs.Length; t++) // keep the original order
      {
        object key = ConvertKey(pairs[t].Key, info.KeyType, settings);
        object value = ConvertDeserializeValue(pairs[t].Value, info.ValueType, settings, null);
        if (dictionary != null)
          dictionary.Add(key, value);
        else
        {
          args[0] = key;
          args[1] = value;
          info.AddMethod.Invoke(result, args);
        }
      }
      return result;
    }

    /// <summary>
    /// A key of a dictionary entry. Number keys are read into string keys (as JSON writes them), except with the indexed schema: the keys of an object are then the indexes of its property names,
    /// and which class they belong to is only known for typed reads (the map could also be a dictionary with number keys, there is no telling them apart).
    /// </summary>
    private static object ConvertKey(object key, Type keyType, MsgPackOptions settings)
    {
      if (keyType == typeof(string) && !(key is null) && key.GetType().IsPrimitive && SerializationRules.GetIndexedSchema(settings) != null)
        throw new MsgPackException($"The map has a number key ({key}) and is read into a dictionary with string keys. With the indexed schema the keys of an object are the indexes of its property names, "
          + "which are only known when the object is read into its class. Read it into a class with the properties, or write it without the indexed schema (UseInexedSchema = false) and with ObjectLayout.Map.");
      return ConvertDeserializeValue(key, keyType, settings, null);
    }

    private static object ConvertScalar(object val, Type assignType)
    {
      Type valType = val.GetType();
      // Fix ArgumentException like "System.Byte cannot be converted to System.Nullable`1[System.Int32]"
      Type targetType = Nullable.GetUnderlyingType(assignType) ?? assignType;
      if (targetType == valType)
        return val;

      if (val is byte[] bytes)
      {
        if (targetType == typeof(Guid))
          return new Guid(bytes);

        if (targetType == typeof(sbyte[])) // the CLR considers sbyte[] to be a byte[], so it is packed as MpBin
        {
          sbyte[] signed = new sbyte[bytes.Length];
          Buffer.BlockCopy(bytes, 0, signed, 0, bytes.Length);
          return signed;
        }
      }

      if (assignType.IsAssignableFrom(valType))
        return val;

      if (targetType == typeof(DateTimeOffset) && val is DateTime dateTime)
        return MsgPackOptions.OffsetOfTimestamp(dateTime); // the timestamp does not contain the offset: the local one, or zero for a UTC or Unspecified DateTime (ReadDateTimeKind)

      if (targetType == typeof(string))
      {
        string text = ToText(val);
        if (text != null)
          return text;
      }

      if (val is string str)
      {
        if (targetType == typeof(Guid) && Guid.TryParse(str, out Guid guid)) // as other libraries write Guids (e.g. MessagePack-CSharp)
          return guid;
        if (targetType.IsEnum)
          return Enum.Parse(targetType, str, true); // the name, as the JSON serializers write enums with a string converter
      }

      if (targetType.IsEnum)
        return Enum.ToObject(targetType, val);

      if ((targetType.IsPrimitive || targetType == typeof(decimal)) && val is IConvertible)
        return Convert.ChangeType(val, targetType, CultureInfo.InvariantCulture);

      if (FrameworkTypeInfo.TryConvert(val, targetType, out object converted)) // TimeSpan, DateOnly, TimeOnly and Uri
        return converted;

      return val;
    }

    /// <summary>
    /// A value read into a string, as the JSON serializers read it from the JSON text: numbers in the invariant culture, true/false, a timestamp in ISO 8601,
    /// a bin of 16 bytes as the Guid it most likely is (LsMsgPack writes Guids that way), other bins in base64. Null for anything else.
    /// </summary>
    private static string ToText(object val)
    {
      switch (val)
      {
        case bool b: return b ? "true" : "false";
        case byte[] bytes: return bytes.Length == 16 ? new Guid(bytes).ToString() : Convert.ToBase64String(bytes);
        case DateTime dateTime: return dateTime.ToString("O", CultureInfo.InvariantCulture);
        case float f: return f.ToString("R", CultureInfo.InvariantCulture);
        case double d: return d.ToString("R", CultureInfo.InvariantCulture);
        case IFormattable formattable when val.GetType().IsPrimitive || val is decimal: return formattable.ToString(null, CultureInfo.InvariantCulture);
      }
      return null;
    }

    internal static bool IsCollection(Type type)
    {
      return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }

    internal static bool IsDictionary(Type type)
    {
      return IsCollection(type) && CollectionInfo.Get(type).IsDictionary;
    }
  }
}
