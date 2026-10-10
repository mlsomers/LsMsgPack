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
      return SerializeObject(item, settings, assignedTo, 0);
    }

    /// <param name="depth">The nesting level of the object (0 for the root): objects and collections at <see cref="MsgPackOptions.MaxDepth"/> are refused (a cycle would otherwise end in a stack overflow)</param>
    private static MsgPackItem SerializeObject(object item, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth)
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
      {
        if (tType.IsArray && tType.GetArrayRank() > 1)
          return SerializeMultidimensional((Array)item, tType, settings, assignedTo, depth);
        ThrowIfTooDeep(depth, settings);
        return SerializeCollection(item, packed, tType, settings, assignedTo, depth);
      }

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

      if (FrameworkTypeInfo.IsKeyValuePair(tType))
        return SerializePair(item, tType, settings, assignedTo, depth);

      if (FrameworkTypeInfo.IsTuple(tType))
        return SerializeTuple(item, tType, settings, assignedTo, depth);

      // Any complex object with properties
      ThrowIfTooDeep(depth, settings);
      FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(tType, settings);
      if (props.Length == 0) // a refused type has no settable properties (the check is cached per type, the properties are)
        SerializationRules.ThrowIfUnsupportedFrameworkType(tType);
      if (settings._objectLayout == ObjectLayout.Array)
        return SerializeAsArray(item, tType, props, settings, assignedTo, depth);

      KeyValuePair<object, object>[] propVals = new KeyValuePair<object, object>[props.Length + 1];
      int count = 0;

      if (SerializationRules.NeedsTypeId(tType, assignedTo, settings))
        propVals[count++] = new KeyValuePair<object, object>(TypeIdKey, SerializationRules.GetTypeIdentifier(tType, settings, assignedTo));
      else
        SerializationRules.ThrowIfUnresolvableWithSchema(tType, assignedTo, settings);

      count = AddProperties(item, props, propVals, count, settings, depth);

      return ToMap(propVals, count, settings);
    }

    /// <summary>
    /// A KeyValuePair is an array [key, value] (as MessagePack-CSharp writes it), its Key and Value have no setters. Wrapped like a collection when it needs a type id: the key and value come first (they may add types to the schema).
    /// </summary>
    private static MsgPackItem SerializePair(object item, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth)
    {
      ThrowIfTooDeep(depth, settings);
      FrameworkTypeInfo.PairInfo pair = FrameworkTypeInfo.GetPair(tType);
      MpArray array = new MpArray(settings)
      {
        Value = new MsgPackItem[]
        {
          SerializeObject(pair.Key.GetValue(item), settings, pair.KeyInfo, depth + 1),
          SerializeObject(pair.Value.GetValue(item), settings, pair.ValueInfo, depth + 1)
        }
      };
      if (!SerializationRules.NeedsTypeId(tType, assignedTo, settings))
        return array;

      return new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>(TypeIdKey, SerializationRules.GetTypeIdentifier(tType, settings, assignedTo)),
        new KeyValuePair<object, object>(ContentKey, array)
      }, settings);
    }

    /// <summary>
    /// A tuple is an array of its items (as MessagePack-CSharp and Nerdbank.MessagePack write it), wrapped like a pair when it needs a type id.
    /// </summary>
    private static MsgPackItem SerializeTuple(object item, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth)
    {
      ThrowIfTooDeep(depth, settings);
      FrameworkTypeInfo.TupleInfo tuple = FrameworkTypeInfo.GetTuple(tType);
      MsgPackItem[] items = new MsgPackItem[tuple.Items.Length];
      for (int t = 0; t < items.Length; t++)
        items[t] = SerializeObject(tuple.GetItem(item, t), settings, tuple.Items[t], depth + 1);
      MpArray array = new MpArray(settings) { Value = items };
      if (!SerializationRules.NeedsTypeId(tType, assignedTo, settings))
        return array;

      return new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>(TypeIdKey, SerializationRules.GetTypeIdentifier(tType, settings, assignedTo)),
        new KeyValuePair<object, object>(ContentKey, array)
      }, settings);
    }

    /// <summary>
    /// A multidimensional array is [length0, length1, ..., [items]] (as MessagePack-CSharp writes it), wrapped like a collection when it needs a type id.
    /// </summary>
    private static MsgPackItem SerializeMultidimensional(Array array, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth)
    {
      ThrowIfTooDeep(depth, settings);
      int rank = array.Rank;
      MsgPackItem[] items = new MsgPackItem[rank + 1];
      for (int t = 0; t < rank; t++)
        items[t] = new MpInt(settings) { Value = array.GetLength(t) };
      FullPropertyInfo elementInfo = new FullPropertyInfo(tType.GetElementType());
      MsgPackItem[] elements = new MsgPackItem[array.Length];
      int at = 0;
      foreach (object element in array) // the last dimension changes first
        elements[at++] = SerializeObject(element, settings, elementInfo, depth + 1);
      items[rank] = new MpArray(settings) { Value = elements };
      MpArray result = new MpArray(settings) { Value = items };
      if (!SerializationRules.NeedsTypeId(tType, assignedTo, settings))
        return result;

      return new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>(TypeIdKey, SerializationRules.GetTypeIdentifier(tType, settings, assignedTo)),
        new KeyValuePair<object, object>(ContentKey, result)
      }, settings);
    }

    private static void ThrowIfTooDeep(int depth, MsgPackSettings settings)
    {
      if (depth >= settings._maxDepth)
        throw new MsgPackException($"The object graph is nested deeper than {nameof(MsgPackSettings)}.{nameof(MsgPackSettings.MaxDepth)} ({settings._maxDepth}), it may contain a cycle.");
    }

    /// <summary>
    /// <see cref="ObjectLayout.Array"/>: the values in the order of the properties (nil when the dynamic filters leave one out), wrapped in a map when a type id is needed: { "": typeId, "@": [values] }.
    /// <para>The ids are resolved before the type id and the values (GetSerializedProps), as for a map, so the indexed schema lists the types in the same order.</para>
    /// </summary>
    private static MsgPackItem SerializeAsArray(object item, Type tType, FullPropertyInfo[] props, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth)
    {
      object typeId = null;
      bool addTypeId = SerializationRules.NeedsTypeId(tType, assignedTo, settings);
      if (addTypeId)
        typeId = SerializationRules.GetTypeIdentifier(tType, settings, assignedTo);
      else
        SerializationRules.ThrowIfUnresolvableAsArray(tType, assignedTo);

      MsgPackItem[] values = new MsgPackItem[props.Length];
      int count = 0; // after the last value that is not nil
      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo prop = props[t];
        object value = prop.GetValue(item);
        if (value is null || !IncludeDynamically(prop, value, settings))
          values[t] = new MpNull(settings);
        else
        {
          values[t] = SerializeObject(value, settings, prop, depth + 1);
          count = t + 1;
        }
      }

      if (settings._trimTrailingNulls && count != values.Length)
        Array.Resize(ref values, count);

      MpArray array = new MpArray(settings) { Value = values };
      if (!addTypeId)
        return array;

      return new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>(TypeIdKey, typeId),
        new KeyValuePair<object, object>(ContentKey, array)
      }, settings);
    }

    private static bool IncludeDynamically(FullPropertyInfo prop, object value, MsgPackSettings settings)
    {
      for (int i = settings._dynamicFilters.Length - 1; i >= 0; i--)
        if (!settings._dynamicFilters[i].IncludeProperty(prop, value))
          return false;
      return true;
    }

    /// <summary>
    /// The elements are serialized as an array (or a map for dictionaries).
    /// <para>Only when a type identifier or properties (see <see cref="SerializeEnumerableAttribute.SerializeProperties"/>) are needed, they are wrapped in a map: { "": typeId, "@": elements, ...properties }</para>
    /// </summary>
    private static MsgPackItem SerializeCollection(object item, MsgPackItem packed, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth)
    {
      SerializeEnumerableAttribute handleItems = SerializationRules.GetEnumerableAttribute(tType, assignedTo);
      bool serializeElements = handleItems?.SerializeElements ?? true;
      bool serializeProperties = handleItems?.SerializeProperties ?? false;
      bool addTypeId = SerializationRules.NeedsTypeId(tType, assignedTo, settings);

      MsgPackItem elements = serializeElements ? SerializeElements(packed, tType, handleItems?.ElementType, settings, depth) : null;
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

      count = AddProperties(item, props, propVals, count, settings, depth);

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
    private static MsgPackItem SerializeElements(MsgPackItem packed, Type tType, Type elementType, MsgPackSettings settings, int depth)
    {
      CollectionInfo info = CollectionInfo.Get(tType);

      if (packed is MpMap)
      {
        KeyValuePair<object, object>[] pairs = (KeyValuePair<object, object>[])packed.Value;
        FullPropertyInfo keyInfo = new FullPropertyInfo(info.KeyType ?? typeof(object));
        FullPropertyInfo valueInfo = new FullPropertyInfo(info.ValueType ?? typeof(object));

        KeyValuePair<object, object>[] packedPairs = new KeyValuePair<object, object>[pairs.Length];
        for (int t = 0; t < pairs.Length; t++)
          packedPairs[t] = new KeyValuePair<object, object>(SerializeObject(pairs[t].Key, settings, keyInfo, depth + 1), SerializeObject(pairs[t].Value, settings, valueInfo, depth + 1));

        return new MpMap(packedPairs, settings);
      }

      Array items = (Array)packed.Value;
      FullPropertyInfo elementInfo = new FullPropertyInfo(elementType ?? info.ElementType);
      MsgPackItem[] packedItems = new MsgPackItem[items.Length];
      for (int t = 0; t < packedItems.Length; t++)
        packedItems[t] = SerializeObject(items.GetValue(t), settings, elementInfo, depth + 1);

      return new MpArray(settings) { Value = packedItems };
    }

    /// <returns>The number of entries in <paramref name="propVals"/></returns>
    private static int AddProperties(object item, FullPropertyInfo[] props, KeyValuePair<object, object>[] propVals, int count, MsgPackSettings settings, int depth)
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
        propVals[count++] = new KeyValuePair<object, object>(prop.PropertyId, SerializeObject(value, settings, prop, depth + 1));
      }
      return count;
    }

  }
}
