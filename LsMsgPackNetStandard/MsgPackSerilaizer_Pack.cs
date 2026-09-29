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
          || !SerializationRules.NeedsTypeId(tType, assignedTo, settings))
          return packed;

        return new MpMap(new KeyValuePair<object, object>[]
        {
          new KeyValuePair<object, object>(TypeIdKey, SerializationRules.GetTypeIdentifier(tType, settings, assignedTo)),
          new KeyValuePair<object, object>(ContentKey, packed)
        }, settings);
      }

      // Any complex object with properties
      FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(tType, settings);
      KeyValuePair<object, object>[] propVals = new KeyValuePair<object, object>[props.Length + 1];
      int count = 0;

      if (SerializationRules.NeedsTypeId(tType, assignedTo, settings))
        propVals[count++] = new KeyValuePair<object, object>(TypeIdKey, SerializationRules.GetTypeIdentifier(tType, settings, assignedTo));
      else
        SerializationRules.ThrowIfUnresolvableWithSchema(tType, assignedTo, settings);

      count = AddProperties(item, props, propVals, count, settings);

      return ToMap(propVals, count, settings);
    }

    /// <summary>
    /// The elements are serialized as an array (or a map for dictionaries).
    /// <para>Only when a type identifier or properties (see <see cref="SerializeEnumerableAttribute.SerializeProperties"/>) are needed, they are wrapped in a map: { "": typeId, "@": elements, ...properties }</para>
    /// </summary>
    private static MsgPackItem SerializeCollection(object item, MsgPackItem packed, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo)
    {
      SerializeEnumerableAttribute handleItems = SerializationRules.GetEnumerableAttribute(tType, assignedTo);
      bool serializeElements = handleItems?.SerializeElements ?? true;
      bool serializeProperties = handleItems?.SerializeProperties ?? false;
      bool addTypeId = SerializationRules.NeedsTypeId(tType, assignedTo, settings);

      MsgPackItem elements = serializeElements ? SerializeElements(packed, tType, handleItems?.ElementType, settings) : null;
      if (!addTypeId && !serializeProperties && elements != null) // no need to wrap the elements in a map
        return elements;

      FullPropertyInfo[] props = serializeProperties ? FullPropertyInfo.GetSerializedProps(tType, settings) : new FullPropertyInfo[0];
      KeyValuePair<object, object>[] propVals = new KeyValuePair<object, object>[props.Length + 2];
      int count = 0;

      if (addTypeId)
        propVals[count++] = new KeyValuePair<object, object>(TypeIdKey, SerializationRules.GetTypeIdentifier(tType, settings, assignedTo));
      else if (props.Length > 0)
        SerializationRules.ThrowIfUnresolvableWithSchema(tType, assignedTo, settings);

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

  }
}
