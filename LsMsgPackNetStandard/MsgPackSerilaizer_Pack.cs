using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections;
using System.Collections.Generic;

namespace LsMsgPack
{
  public static partial class MsgPackSerializer
  {

    public static MsgPackItem SerializeObject(object item, MsgPackSettings settings, FullPropertyInfo assignedTo = null)
    {
      if (ReferenceEquals(item, null))
        return new MpNull(settings);

      Type tType = item.GetType();
      Type nullableType = Nullable.GetUnderlyingType(tType);
      if (!(nullableType is null))
        tType = nullableType;

      MsgPackItem packed = MsgPackItem.Pack(item, settings, tType);

      // Strings, byte[] and Guid (MpBin) are enumerable but not treated as a collection
      if (item is IEnumerable && (packed is MpArray || packed is MpMap))
        return SerializeCollection(item, packed, tType, settings, assignedTo);

      if (packed != null)
      {
        if (assignedTo?.AssignedToType is null
          || tType.IsPrimitive
          || tType == typeof(string)
          || !NeedsTypeId(tType, assignedTo, settings))
          return packed;

        return new MpMap(new KeyValuePair<object, object>[]
        {
          new KeyValuePair<object, object>(TypeIdKey, GetTypeIdentifier(tType, settings, assignedTo)),
          new KeyValuePair<object, object>(ContentKey, packed)
        }, settings);
      }

      // Any complex object with properties
      FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(tType, settings);
      KeyValuePair<object, object>[] propVals = new KeyValuePair<object, object>[props.Length + 1];
      int count = 0;

      if (NeedsTypeId(tType, assignedTo, settings))
        propVals[count++] = new KeyValuePair<object, object>(TypeIdKey, GetTypeIdentifier(tType, settings, assignedTo));
      else
        ThrowIfUnresolvableWithSchema(tType, assignedTo, settings);

      count = AddProperties(item, props, propVals, count, settings);

      return ToMap(propVals, count, settings);
    }

    /// <summary>
    /// The elements are serialized as an array (or a map for dictionaries).
    /// <para>Only when a type identifier or properties (see <see cref="SerializeEnumerableAttribute.SerializeProperties"/>) are needed, they are wrapped in a map: { "": typeId, "@": elements, ...properties }</para>
    /// </summary>
    private static MsgPackItem SerializeCollection(object item, MsgPackItem packed, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo)
    {
      SerializeEnumerableAttribute handleItems = GetEnumerableAttribute(tType, assignedTo);
      bool serializeElements = handleItems?.SerializeElements ?? true;
      bool serializeProperties = handleItems?.SerializeProperties ?? false;
      bool addTypeId = NeedsTypeId(tType, assignedTo, settings);

      MsgPackItem elements = serializeElements ? SerializeElements(packed, tType, handleItems?.ElementType, settings) : null;
      if (!addTypeId && !serializeProperties && elements != null) // no need to wrap the elements in a map
        return elements;

      FullPropertyInfo[] props = serializeProperties ? FullPropertyInfo.GetSerializedProps(tType, settings) : new FullPropertyInfo[0];
      KeyValuePair<object, object>[] propVals = new KeyValuePair<object, object>[props.Length + 2];
      int count = 0;

      if (addTypeId)
        propVals[count++] = new KeyValuePair<object, object>(TypeIdKey, GetTypeIdentifier(tType, settings, assignedTo));
      else if (props.Length > 0)
        ThrowIfUnresolvableWithSchema(tType, assignedTo, settings);

      if (elements != null)
        propVals[count++] = new KeyValuePair<object, object>(ContentKey, elements);

      count = AddProperties(item, props, propVals, count, settings);

      return ToMap(propVals, count, settings);
    }

    /// <summary>
    /// The keys do not need to be checked for duplicates (like a dictionary would), property ids are unique per type and differ from the reserved keys (see FullPropertyInfo.ThrowIfIdsNotUnique).
    /// </summary>
    private static MpMap ToMap(KeyValuePair<object, object>[] entries, int count, MsgPackSettings settings)
    {
      if (count != entries.Length) // skipped properties (e.g. default values), or no type id
        Array.Resize(ref entries, count);
      return new MpMap(entries, settings);
    }

    /// <summary>
    /// Serialize the elements knowing the type they will be assigned to, so type identifiers are only added when the element type is ambiguous.
    /// </summary>
    private static MsgPackItem SerializeElements(MsgPackItem packed, Type tType, Type elementType, MsgPackSettings settings)
    {
      CollectionInfo info = CollectionInfo.Get(tType);

      if (packed is MpMap)
      {
        KeyValuePair<object, object>[] pairs = (KeyValuePair<object, object>[])packed.Value;
        FullPropertyInfo keyInfo = new FullPropertyInfo(info.KeyType ?? typeof(object));
        FullPropertyInfo valueInfo = new FullPropertyInfo(info.ValueType ?? typeof(object));

        KeyValuePair<object, object>[] packedPairs = new KeyValuePair<object, object>[pairs.Length];
        for (int t = 0; t < pairs.Length; t++)
          packedPairs[t] = new KeyValuePair<object, object>(SerializeObject(pairs[t].Key, settings, keyInfo), SerializeObject(pairs[t].Value, settings, valueInfo));

        return new MpMap(packedPairs, settings);
      }

      Array items = (Array)packed.Value;
      FullPropertyInfo elementInfo = new FullPropertyInfo(elementType ?? info.ElementType);
      MsgPackItem[] packedItems = new MsgPackItem[items.Length];
      for (int t = 0; t < packedItems.Length; t++)
        packedItems[t] = SerializeObject(items.GetValue(t), settings, elementInfo);

      return new MpArray(settings) { Value = packedItems };
    }

    /// <summary>
    /// An attribute on the property takes precedence over one on the collection type
    /// </summary>
    private static SerializeEnumerableAttribute GetEnumerableAttribute(Type tType, FullPropertyInfo assignedTo)
    {
      if (assignedTo?.CustomAttributes != null && assignedTo.CustomAttributes.TryGetValue(nameof(SerializeEnumerableAttribute), out object att))
        return (SerializeEnumerableAttribute)att;

      return CollectionInfo.Get(tType).Attribute;
    }

    private static bool NeedsTypeId(Type tType, FullPropertyInfo assignedTo, MsgPackSettings settings)
    {
      if ((settings._addTypeIdOptions & AddTypeIdOption.Always) != 0)
        return true;

      if ((settings._addTypeIdOptions & AddTypeIdOption.IfAmbiguious) != 0)
        return assignedTo?.AssignedToType != tType;

      return false;
    }

    /// <summary>
    /// With the indexed schema, property keys are indexes into the schema of the runtime type, so without a type id the reader cannot tell which type (and thus which property names) they belong to.
    /// Resolving by signature (see <see cref="IMsgPackTypeResolver.Resolve"/>) is therefore impossible and the data would be read as the wrong type.
    /// </summary>
    private static void ThrowIfUnresolvableWithSchema(Type tType, FullPropertyInfo assignedTo, MsgPackSettings settings)
    {
      if (assignedTo?.AssignedToType is null || assignedTo.AssignedToType == tType || !UsesIndexedSchema(settings))
        return;

      throw new MsgPackException($"Unable to serialize {tType.FullName} assigned to {assignedTo.AssignedToType.FullName} without a type id while using the indexed schema: the property keys are schema indexes of {tType.Name}, so the type cannot be resolved by its properties when deserializing. Use {nameof(AddTypeIdOption)}.{nameof(AddTypeIdOption.IfAmbiguious)} (with the schema a type id costs about 1 byte) or set {nameof(MsgPackSettings)}.{nameof(MsgPackSettings.UseInexedSchema)} = false.");
    }

    private static bool UsesIndexedSchema(MsgPackSettings settings)
    {
      for (int t = 0; t < settings._propertyNameResolvers.Length; t++)
        if (settings._propertyNameResolvers[t] is IndexedSchemaTypeResolver)
          return true;
      return false;
    }

    /// <returns>The number of entries in <paramref name="propVals"/></returns>
    private static int AddProperties(object item, FullPropertyInfo[] props, KeyValuePair<object, object>[] propVals, int count, MsgPackSettings settings)
    {
      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo prop = props[t];
        object value = prop.GetValue(item);

        bool exclude = false;
        for (int i = settings._dynamicFilters.Length - 1; i >= 0; i--)
          if (!settings._dynamicFilters[i].IncludeProperty(prop, value)) { exclude = true; break; }

        if (exclude)
          continue;

        if (value is null)
        {
          propVals[count++] = new KeyValuePair<object, object>(prop.PropertyId, value);
          continue;
        }
        propVals[count++] = new KeyValuePair<object, object>(prop.PropertyId, SerializeObject(value, settings, prop));
      }
      return count;
    }

    /// <summary>
    /// This can be overridden by implementing <see cref="IMsgPackTypeResolver">IMsgPackTypeResolver</see>.
    /// </summary>
    private static object GetTypeIdentifier(Type type, MsgPackSettings settings, FullPropertyInfo propertyInfo)
    {
      object typeId = null;

      for (int t = settings._typeResolvers.Length - 1; t >= 0; t--)
      {
        typeId = settings._typeResolvers[t].IdForType(type, propertyInfo, settings);
        if (typeId != null)
          break;
      }
      if (typeId is null && !((settings._addTypeIdOptions & AddTypeIdOption.NoDefaultFallBack) > 0))
      {
        bool fullname = (settings._addTypeIdOptions & AddTypeIdOption.FullName) > 0;
        typeId = TypeResolver.GetTypeName(type, fullname);
      }

      return typeId;
    }
  }
}
