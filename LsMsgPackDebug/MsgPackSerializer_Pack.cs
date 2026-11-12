using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections;
using System.Collections.Generic;

namespace LsMsgPackDebug
{
  public static partial class MsgPackSerializer
  {

    public static MsgPackItem SerializeObject(object item, MsgPackSettings settings, FullPropertyInfo assignedTo = null)
    {
      if (!settings._ignoreCycles)
        return SerializeObject(item, settings, assignedTo, 0, false);

      // A call of its own (also one inside another, e.g. from a custom extension): the objects of the caller are not on its path
      CyclePath path = _cyclePath ?? (_cyclePath = new CyclePath());
      int outer = path.Start;
      path.Start = path.Count;
      try
      {
        return SerializeObject(item, settings, assignedTo, 0, false);
      }
      finally
      {
        path.Start = outer;
      }
    }

    /// <summary>
    /// <see cref="MsgPackOptions.IgnoreCycles"/>: the objects with properties being written on this thread, from the root of the call (<see cref="Start"/>) to the current one.
    /// </summary>
    private sealed class CyclePath
    {
      private object[] _objects = new object[16];
      internal int Count;
      internal int Start;

      internal bool Contains(object item)
      {
        for (int t = Count - 1; t >= Start; t--)
          if (ReferenceEquals(_objects[t], item))
            return true;
        return false;
      }

      internal void Push(object item)
      {
        if (Count == _objects.Length)
          Array.Resize(ref _objects, Count * 2);
        _objects[Count++] = item;
      }

      internal void Pop()
      {
        _objects[--Count] = null;
      }
    }

    [ThreadStatic]
    private static CyclePath _cyclePath;

    /// <summary>
    /// <see cref="MsgPackOptions.IgnoreCycles"/>: an object that contains itself is nil where it is met again (LsMsgPack: CycleHandler, Serializer.WriteBoxed).
    /// </summary>
    private static MsgPackItem SerializeTracked(object item, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
    {
      CyclePath path = _cyclePath ?? (_cyclePath = new CyclePath()); // SerializeObject with the settings creates it, a nested call of the private one may not
      if (path.Contains(item))
        return new MpNull(settings);
      path.Push(item);
      try
      {
        return SerializeComplex(item, tType, settings, assignedTo, depth, byNames);
      }
      finally
      {
        path.Pop();
      }
    }

    /// <param name="depth">The nesting level of the object (0 for the root): objects and collections at <see cref="MsgPackOptions.MaxDepth"/> are refused (a cycle would otherwise end in a stack overflow)</param>
    /// <param name="byNames">Below an object with an anonymous type, or a value with one that is not written as its declared type: objects with anonymous types are written by names (see SerializationRules.WritesByNames)</param>
    private static MsgPackItem SerializeObject(object item, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
    {
      if (ReferenceEquals(item, null))
        return new MpNull(settings);

      Type tType = item.GetType();
      Type nullableType = Nullable.GetUnderlyingType(tType);
      if (!(nullableType is null))
        tType = nullableType;

      MsgPackItem packed = MsgPackItem.Pack(item, settings, tType);

      // Strings, byte[] and Guid (MpBin) are enumerable but not treated as a collection
      if (packed != null && (packed is MpArray || packed is MpMap) && item is IEnumerable) // objects have no packed item: no interface check for them
      {
        bool elementsByNames = SerializationRules.EntersByNames(tType, assignedTo, byNames, false);
        if (tType.IsArray && tType.GetArrayRank() > 1)
          return SerializeMultidimensional((Array)item, tType, settings, assignedTo, depth, elementsByNames);
        ThrowIfTooDeep(depth, settings);
        return SerializeCollection(item, packed, tType, settings, assignedTo, depth, elementsByNames);
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
        return SerializePair(item, tType, settings, assignedTo, depth, SerializationRules.EntersByNames(tType, assignedTo, byNames, false));

      if (FrameworkTypeInfo.IsTuple(tType))
        return SerializeTuple(item, tType, settings, assignedTo, depth, SerializationRules.EntersByNames(tType, assignedTo, byNames, false));

      // Any complex object with properties
      if (settings._ignoreCycles && !tType.IsValueType)
        return SerializeTracked(item, tType, settings, assignedTo, depth, byNames);
      return SerializeComplex(item, tType, settings, assignedTo, depth, byNames);
    }

    private static MsgPackItem SerializeComplex(object item, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
    {
      ThrowIfTooDeep(depth, settings);
      if (SerializationRules.WritesByNames(tType, assignedTo, byNames))
        return SerializeByNames(item, tType, settings, depth);
      FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(tType, settings);
      if (props.Length == 0) // a refused type has no settable properties (the check is cached per type, the properties are)
        SerializationRules.ThrowIfUnsupportedFrameworkType(tType);
      byNames = SerializationRules.EntersByNames(tType, assignedTo, byNames, true);
      if (settings._objectLayout == ObjectLayout.Array)
        return SerializeAsArray(item, tType, props, settings, assignedTo, depth, byNames);

      KeyValuePair<object, object>[] propVals = new KeyValuePair<object, object>[props.Length + 1];
      int count = 0;

      if (SerializationRules.NeedsTypeId(tType, assignedTo, settings))
        propVals[count++] = new KeyValuePair<object, object>(TypeIdKey, SerializationRules.GetTypeIdentifier(tType, settings, assignedTo));
      else
        SerializationRules.ThrowIfUnresolvableWithSchema(tType, assignedTo, settings);

      count = AddProperties(item, props, propVals, count, settings, depth, byNames);

      return ToMap(propVals, count, settings);
    }

    /// <summary>
    /// An object with an anonymous type where its type is not declared or below another one (see SerializationRules.WritesByNames): a map of its property names and values, without a type id and not in the schema,
    /// whatever the <see cref="ObjectLayout"/> (the dynamic filters leave values out as in a map). Its values are written by names as far as they have anonymous types.
    /// </summary>
    private static MsgPackItem SerializeByNames(object item, Type tType, MsgPackSettings settings, int depth)
    {
      FullPropertyInfo[] props = FullPropertyInfo.GetNamedProps(tType, settings);
      KeyValuePair<object, object>[] propVals = new KeyValuePair<object, object>[props.Length];
      int count = AddProperties(item, props, propVals, 0, settings, depth, true);
      return ToMap(propVals, count, settings);
    }

    /// <summary>
    /// A KeyValuePair is an array [key, value] (as MessagePack-CSharp writes it), its Key and Value have no setters. Wrapped like a collection when it needs a type id: the key and value come first (they may add types to the schema).
    /// </summary>
    private static MsgPackItem SerializePair(object item, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
    {
      ThrowIfTooDeep(depth, settings);
      FrameworkTypeInfo.PairInfo pair = FrameworkTypeInfo.GetPair(tType);
      MpArray array = new MpArray(settings)
      {
        Value = new MsgPackItem[]
        {
          SerializeObject(pair.Key.GetValue(item), settings, pair.KeyInfo, depth + 1, byNames),
          SerializeObject(pair.Value.GetValue(item), settings, pair.ValueInfo, depth + 1, byNames)
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
    private static MsgPackItem SerializeTuple(object item, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
    {
      ThrowIfTooDeep(depth, settings);
      FrameworkTypeInfo.TupleInfo tuple = FrameworkTypeInfo.GetTuple(tType);
      MsgPackItem[] items = new MsgPackItem[tuple.Items.Length];
      for (int t = 0; t < items.Length; t++)
        items[t] = SerializeObject(tuple.GetItem(item, t), settings, tuple.Items[t], depth + 1, byNames);
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
    private static MsgPackItem SerializeMultidimensional(Array array, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
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
        elements[at++] = SerializeObject(element, settings, elementInfo, depth + 1, byNames);
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
    private static MsgPackItem SerializeAsArray(object item, Type tType, FullPropertyInfo[] props, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
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
        if (value is null || !MsgPackOptions.IncludeDynamically(settings._dynamicFilters, prop, item, value))
          values[t] = new MpNull(settings);
        else
        {
          values[t] = SerializeObject(value, settings, prop, depth + 1, byNames);
          if (!(values[t] is MpNull)) // a cycle (IgnoreCycles) is nil too: trimmed like the others (LsMsgPack checks the byte written)
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

    /// <summary>
    /// The elements are serialized as an array (or a map for dictionaries).
    /// <para>Only when a type identifier or properties (see <see cref="SerializeEnumerableAttribute.SerializeProperties"/>) are needed, they are wrapped in a map: { "": typeId, "@": elements, ...properties }</para>
    /// </summary>
    private static MsgPackItem SerializeCollection(object item, MsgPackItem packed, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
    {
      SerializeEnumerableAttribute handleItems = SerializationRules.GetEnumerableAttribute(tType, assignedTo);
      bool serializeElements = handleItems?.SerializeElements ?? true;
      bool serializeProperties = handleItems?.SerializeProperties ?? false;
      bool addTypeId = SerializationRules.NeedsTypeId(tType, assignedTo, settings);

      MsgPackItem elements = serializeElements ? SerializeElements(packed, tType, handleItems?.ElementType, settings, depth, byNames) : null;
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

      count = AddProperties(item, props, propVals, count, settings, depth, byNames);

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
    private static MsgPackItem SerializeElements(MsgPackItem packed, Type tType, Type elementType, MsgPackSettings settings, int depth, bool byNames)
    {
      CollectionInfo info = CollectionInfo.Get(tType);

      if (packed is MpMap)
      {
        KeyValuePair<object, object>[] pairs = (KeyValuePair<object, object>[])packed.Value;
        FullPropertyInfo keyInfo = new FullPropertyInfo(info.KeyType ?? typeof(object));
        FullPropertyInfo valueInfo = new FullPropertyInfo(info.ValueType ?? typeof(object));

        KeyValuePair<object, object>[] packedPairs = new KeyValuePair<object, object>[pairs.Length];
        for (int t = 0; t < pairs.Length; t++)
          packedPairs[t] = new KeyValuePair<object, object>(SerializeObject(pairs[t].Key, settings, keyInfo, depth + 1, byNames), SerializeObject(pairs[t].Value, settings, valueInfo, depth + 1, byNames));

        return new MpMap(packedPairs, settings);
      }

      Array items = (Array)packed.Value;
      FullPropertyInfo elementInfo = new FullPropertyInfo(elementType ?? info.ElementType);
      MsgPackItem[] packedItems = new MsgPackItem[items.Length];
      for (int t = 0; t < packedItems.Length; t++)
        packedItems[t] = SerializeObject(items.GetValue(t), settings, elementInfo, depth + 1, byNames);

      return new MpArray(settings) { Value = packedItems };
    }

    /// <returns>The number of entries in <paramref name="propVals"/></returns>
    private static int AddProperties(object item, FullPropertyInfo[] props, KeyValuePair<object, object>[] propVals, int count, MsgPackSettings settings, int depth, bool byNames)
    {
      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo prop = props[t];
        object value = prop.GetValue(item);

        if (!MsgPackOptions.IncludeDynamically(settings._dynamicFilters, prop, item, value))
          continue;

        if (value is null)
        {
          propVals[count++] = new KeyValuePair<object, object>(prop.PropertyId, value);
          continue;
        }
        propVals[count++] = new KeyValuePair<object, object>(prop.PropertyId, SerializeObject(value, settings, prop, depth + 1, byNames));
      }
      return count;
    }

  }
}
