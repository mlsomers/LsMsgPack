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
        return ConvertArray(items, assignType, settings);

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

      if (propVals.Count > (hasTypeId ? 1 : 0))
        SerializationRules.GetIndexedSchema(settings)?.ThrowIfNoEntry(tType, settings);
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
      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo prop = props[t];
        if (propVals.TryGetValue(prop.PropertyId, out object propval))
        {
          prop.SetValue(instance, ConvertDeserializeValue(propval, prop.PropertyInfo.PropertyType, settings, prop));
        }
      }
    }

    /// <summary>
    /// Converts the items of a MsgPack array into an array or collection of the given type, or into an object written as an array (<see cref="ObjectLayout.Array"/>).
    /// </summary>
    private static object ConvertArray(object[] items, Type assignType, MsgPackOptions settings)
    {
      if (!IsCollection(assignType))
      {
        if (!assignType.IsInstanceOfType(items))
        {
          Type objectType = Nullable.GetUnderlyingType(assignType) ?? assignType;
          if (IsObjectType(objectType))
          {
            if (Array.Exists(items, i => i != null)) // before the ids are resolved, which would add an entry for the class
              SerializationRules.GetIndexedSchema(settings)?.ThrowIfNoEntry(objectType, settings);
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
      FullPropertyInfo[] byPosition = PropertiesByPosition(type, props, settings);
      object result = Instances.CreateObject(type, settings);
      int count = Math.Min(items.Length, byPosition.Length);
      for (int t = 0; t < count; t++)
      {
        FullPropertyInfo prop = byPosition[t];
        if (prop is null || items[t] is null)
          continue;
        prop.SetValue(result, ConvertDeserializeValue(items[t], prop.PropertyInfo.PropertyType, settings, prop));
      }
      return result;
    }

    /// <returns>The property of each position: the properties themselves, or with the indexed schema the ones named by the schema of the data (null when not a property here)</returns>
    private static FullPropertyInfo[] PropertiesByPosition(Type type, FullPropertyInfo[] props, MsgPackOptions settings)
    {
      IndexedSchemaTypeResolver schema = SerializationRules.GetIndexedSchema(settings);
      if (schema is null || !schema.TryGetDef(type, out ComplexTypeDef def) || def.IsCollection)
        return props;

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
          object key = ConvertDeserializeValue(pairs[t].Key, info.KeyType, settings, null);
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
          object key = ConvertDeserializeValue(pairs[t].Key, info.KeyType, settings, null);
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
        object key = ConvertDeserializeValue(pairs[t].Key, info.KeyType, settings, null);
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
        return new DateTimeOffset(dateTime); // the timestamp does not contain the offset, the value is local time

      if (targetType.IsEnum)
        return Enum.ToObject(targetType, val);

      if ((targetType.IsPrimitive || targetType == typeof(decimal)) && val is IConvertible)
        return Convert.ChangeType(val, targetType, CultureInfo.InvariantCulture);

      if (FrameworkTypeInfo.TryConvert(val, targetType, out object converted)) // TimeSpan, DateOnly, TimeOnly and Uri
        return converted;

      return val;
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
