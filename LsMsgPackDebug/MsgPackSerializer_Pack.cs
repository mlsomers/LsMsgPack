using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using LsMsgPack.Converters;
using LsMsgPack.References;
using LsMsgPackDebug.Types.Extensions;

namespace LsMsgPackDebug
{
  public static partial class MsgPackSerializer
  {

    public static MsgPackItem SerializeObject(object item, MsgPackSettings settings, FullPropertyInfo assignedTo = null)
    {
      CallbackReplay outer = _replay;
      if (outer is null)
        return SerializeRoot(item, settings, assignedTo);
      _replay = null; // a call of its own (e.g. from a callback): not a part of the caller's write that may be repeated
      try
      {
        return SerializeRoot(item, settings, assignedTo);
      }
      finally
      {
        _replay = outer;
      }
    }

    /// <summary>
    /// The serialization callbacks of the write with a cached schema on this thread (see SerializeWithSchemaReference), which may be repeated: called once per object. Null otherwise.
    /// </summary>
    [ThreadStatic]
    private static CallbackReplay _replay;

    private static MsgPackItem SerializeRoot(object item, MsgPackSettings settings, FullPropertyInfo assignedTo)
    {
      if (settings.PreservesReferences)
        return SerializePreserving(item, settings, assignedTo);
      if (!settings.IgnoresCycles)
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
    /// <see cref="ReferenceHandling.IgnoreCycles"/>: the objects with properties being written on this thread, from the root of the call (<see cref="Start"/>) to the current one.
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
    /// <see cref="ReferenceHandling.Preserve"/>: the objects and collections written by the call on this thread (a call of its own, e.g. from a callback, has its own; a repeated write starts again).
    /// </summary>
    [ThreadStatic]
    private static ReferenceTable _references;

    private sealed class ReferenceTable
    {
      internal readonly Dictionary<object, ReferenceTarget> Targets = new Dictionary<object, ReferenceTarget>(IdentityComparer.Instance);
      internal List<MpReference> References;

      /// <summary>
      /// Above 0 while writing the keys of a map: a key is always written in full (a path step stands for an entry's value, it cannot point to a key).
      /// </summary>
      internal int InKeys;
    }

    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/>: the value, with a reference wherever an object or collection is met again (LsMsgPack: PreservingHandler, Serializer.WriteBoxed); the paths of the references
    /// are known once the whole value is (<see cref="SetPaths"/>), the item tree has every value as it is written.
    /// </summary>
    private static MsgPackItem SerializePreserving(object item, MsgPackSettings settings, FullPropertyInfo assignedTo)
    {
      ReferenceCode.ThrowIfUsed(settings);
      ICustomExt[] custom = settings._customExtentionTypes;
      for (int t = 0; custom != null && t < custom.Length; t++)
        if (custom[t] != null && custom[t].TypeSpecifier == MsgPackOptions.ReferenceExtensionType)
          throw ReferenceCode.Used(custom[t].GetType().Name);
      ReferenceTable outer = _references;
      ReferenceTable table = _references = new ReferenceTable();
      try
      {
        MsgPackItem root = SerializeObject(item, settings, assignedTo, 0, false);
        if (table.References != null)
          SetPaths(root, table.References);
        return root;
      }
      finally
      {
        _references = outer;
      }
    }

    private static void SetPaths(MsgPackItem root, List<MpReference> references)
    {
      Dictionary<object, ReferenceTarget> targets = new Dictionary<object, ReferenceTarget>(IdentityComparer.Instance);
      for (int t = 0; t < references.Count; t++)
        targets[references[t].Target.Item] = references[t].Target;
      FindPaths(root, targets, new List<int>());
      for (int t = 0; t < references.Count; t++)
      {
        int[] path = references[t].Target.Path;
        if (path is null)
          throw new MsgPackException("A reference points to a value that is not in the written tree."); // a bug: every target is written before its references
        references[t].SetPath(path);
      }
    }

    private static void FindPaths(object value, Dictionary<object, ReferenceTarget> targets, List<int> path)
    {
      if (!(value is MsgPackItem item))
        return; // a key or type id written as it is
      if (targets.TryGetValue(item, out ReferenceTarget target) && target.Path is null)
        target.Path = path.ToArray();
      if (item is MpArray)
      {
        Array items = item.Value as Array;
        for (int t = 0; items != null && t < items.Length; t++)
        {
          path.Add(t);
          FindPaths(items.GetValue(t), targets, path);
          path.RemoveAt(path.Count - 1);
        }
      }
      else if (item is MpMap && item.Value is KeyValuePair<object, object>[] entries)
      {
        for (int t = 0; t < entries.Length; t++)
        {
          path.Add(t);
          FindPaths(entries[t].Value, targets, path);
          path.RemoveAt(path.Count - 1);
        }
      }
    }

    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/>: an object or collection (<see cref="SerializationRules.TracksReferences"/>, or a converted value) written in full the first time, as a reference after that:
    /// wrapped with the type id where its declared type differs, by the rules of the value itself.
    /// </summary>
    private static MsgPackItem SerializeReferenced(object item, Type tType, MsgPackConverter converter, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames, ReferenceTable table)
    {
      if (table.Targets.TryGetValue(item, out ReferenceTarget target))
      {
        MpReference reference = new MpReference(settings, target);
        (table.References ?? (table.References = new List<MpReference>())).Add(reference);
        if (!ReferenceNeedsTypeId(tType, converter, assignedTo, settings))
          return reference;
        return new MpMap(new KeyValuePair<object, object>[]
        {
          new KeyValuePair<object, object>(TypeIdKey, SerializationRules.GetTypeIdentifier(tType, settings, assignedTo)),
          new KeyValuePair<object, object>(ContentKey, reference)
        }, settings);
      }
      target = new ReferenceTarget();
      table.Targets.Add(item, target);
      target.Item = SerializeValue(item, tType, converter, settings, assignedTo, depth, byNames);
      return target.Item;
    }

    /// <summary>
    /// The type id rule of the value the reference stands for (LsMsgPack: Serializer.ReferenceNeedsTypeId): a converted value as SerializeConverted wraps it, the others as objects and collections are.
    /// </summary>
    private static bool ReferenceNeedsTypeId(Type tType, MsgPackConverter converter, FullPropertyInfo assignedTo, MsgPackSettings settings)
    {
      if (converter != null)
        return !(assignedTo?.AssignedToType is null || !SerializationRules.NeedsTypeId(tType, assignedTo, settings));
      return SerializationRules.NeedsTypeId(tType, assignedTo, settings);
    }

    /// <summary>
    /// <see cref="ReferenceHandling.IgnoreCycles"/>: an object that contains itself is nil where it is met again (LsMsgPack: CycleHandler, Serializer.WriteBoxed).
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

      MsgPackConverter converter = settings.ConverterFor(tType, assignedTo); // the member's own converter first
      if (settings.PreservesReferences && !tType.IsValueType)
      {
        ReferenceTable table = _references;
        if (table != null && table.InKeys == 0 && (converter != null || SerializationRules.TracksReferences(tType, settings)))
          return SerializeReferenced(item, tType, converter, settings, assignedTo, depth, byNames, table);
      }
      return SerializeValue(item, tType, converter, settings, assignedTo, depth, byNames);
    }

    /// <summary>
    /// <see cref="SerializeObject"/> after the reference check (<see cref="ReferenceHandling.Preserve"/>).
    /// </summary>
    private static MsgPackItem SerializeValue(object item, Type tType, MsgPackConverter converter, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
    {
      if (converter != null)
        return SerializeConverted(item, tType, converter, settings, assignedTo, depth, byNames);

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
      if (settings.IgnoresCycles && !tType.IsValueType)
        return SerializeTracked(item, tType, settings, assignedTo, depth, byNames);
      return SerializeComplex(item, tType, settings, assignedTo, depth, byNames);
    }

    /// <summary>
    /// A value of a type with a converter (MsgPackOptions.Converters, [MsgPackConverter]): its surrogate, written as a value declared as the surrogate type. Where the declared type differs
    /// it is wrapped as a leaf value is: { "": typeId, "@": surrogate }, the type id first (the schema gets the converted type before the surrogate's types, as LsMsgPack writes it).
    /// </summary>
    private static MsgPackItem SerializeConverted(object item, Type tType, MsgPackConverter converter, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
    {
      bool wrap = !(assignedTo?.AssignedToType is null || tType.IsPrimitive || tType == typeof(string) || !SerializationRules.NeedsTypeId(tType, assignedTo, settings));
      object typeId = wrap ? SerializationRules.GetTypeIdentifier(tType, settings, assignedTo) : null;
      ReferenceCode.ThrowIfUsed(converter as MsgPackBinaryConverter, settings); // also one of [MsgPackConverter] on a type
      MsgPackItem content = converter is MsgPackBinaryConverter binary && binary.ExtensionTypeCode != null
        ? new MpExt(settings) { TypeSpecifier = binary.ExtensionTypeCode.Value, Value = binary.ToBytes(item) } // the bytes of a binary converter as its extension (otherwise bin, as any byte[])
        : SerializeSurrogate(converter.ToSurrogateBoxed(item), settings, converter.SurrogateInfo, depth, byNames);
      if (!wrap)
        return content;
      return new MpMap(new KeyValuePair<object, object>[]
      {
        new KeyValuePair<object, object>(TypeIdKey, typeId),
        new KeyValuePair<object, object>(ContentKey, content)
      }, settings);
    }

    /// <summary>
    /// The surrogate of a converted value: the converted value keeps the identity (<see cref="ReferenceHandling.Preserve"/>), the surrogate itself is not tracked (a converter may share them), its values are.
    /// </summary>
    private static MsgPackItem SerializeSurrogate(object surrogate, MsgPackSettings settings, FullPropertyInfo surrogateInfo, int depth, bool byNames)
    {
      if (surrogate is null || !settings.PreservesReferences)
        return SerializeObject(surrogate, settings, surrogateInfo, depth, byNames);
      Type tType = surrogate.GetType();
      tType = Nullable.GetUnderlyingType(tType) ?? tType;
      return SerializeValue(surrogate, tType, settings.ConverterFor(tType, surrogateInfo), settings, surrogateInfo, depth, byNames);
    }

    private static MsgPackItem SerializeComplex(object item, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
    {
      SerializationCallbacks callbacks = SerializationCallbacks.Of(tType); // IMsgPackOnSerializing... (docs/Callbacks.md), objects only
      if (callbacks is null || !callbacks.Serializes)
        return SerializeProperties(item, tType, settings, assignedTo, depth, byNames);
      CallbackReplay replay = _replay;
      if (replay is null)
        callbacks.OnSerializing(item); // a struct: its box, which is written
      else
        replay.OnSerializing(callbacks, item);
      MsgPackItem packed = SerializeProperties(item, tType, settings, assignedTo, depth, byNames);
      if (replay is null)
        callbacks.OnSerialized(item);
      else
        replay.OnSerialized(callbacks, item);
      return packed;
    }

    private static MsgPackItem SerializeProperties(object item, Type tType, MsgPackSettings settings, FullPropertyInfo assignedTo, int depth, bool byNames)
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

      object typeId = null;
      if (SerializationRules.NeedsTypeId(tType, assignedTo, settings))
        typeId = SerializationRules.GetTypeIdentifier(tType, settings, assignedTo);
      else
        SerializationRules.ThrowIfUnresolvableWithSchema(tType, assignedTo, settings);
      List<KeptEntry> kept = KeptToWrite(item, tType, props, false, settings); // after the type id, before the values (the order of the names in the schema)

      KeyValuePair<object, object>[] propVals = new KeyValuePair<object, object>[props.Length + 1 + (kept is null ? 0 : kept.Count)];
      int count = 0;
      if (typeId != null)
        propVals[count++] = new KeyValuePair<object, object>(TypeIdKey, typeId);

      count = AddProperties(item, props, propVals, count, settings, depth, byNames);
      if (kept != null)
      {
        for (int t = 0; t < kept.Count; t++)
          propVals[count++] = new KeyValuePair<object, object>(kept[t].Key, new MpRawValue(kept[t].Bytes, settings));
      }

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
      List<KeptEntry> kept = KeptToWrite(item, tType, props, true, settings); // after the type id, before the values (the order of the names in the schema)
      int length = props.Length;
      if (kept != null)
      {
        for (int t = 0; t < kept.Count; t++)
          length = Math.Max(length, kept[t].Position + 1);
      }

      MsgPackItem[] values = new MsgPackItem[length];
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

      if (kept != null) // after the properties, at the positions of their names in the schema's entry (or their own without the schema), nil in between
      {
        for (int t = props.Length; t < values.Length; t++)
          values[t] = new MpNull(settings);
        for (int t = 0; t < kept.Count; t++)
        {
          values[kept[t].Position] = new MpRawValue(kept[t].Bytes, settings);
          count = Math.Max(count, kept[t].Position + 1);
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
    /// The values the object keeps as extension data (see ExtensionDataMember.ToWrite), with the indexed schema at the positions of their names in the type's entry. Null for none.
    /// </summary>
    private static List<KeptEntry> KeptToWrite(object item, Type tType, FullPropertyInfo[] props, bool array, MsgPackSettings settings)
    {
      ExtensionDataMember extension = ExtensionDataMember.Of(tType);
      if (extension is null)
        return null;
      bool schema = SerializationRules.GetIndexedSchema(settings) != null;
      List<KeptEntry> kept = extension.ToWrite(item, props, array, schema, settings);
      return kept != null && schema ? ExtensionDataMember.WithSchemaIds(kept, tType, settings) : kept;
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
        ReferenceTable table = settings.PreservesReferences ? _references : null;
        for (int t = 0; t < pairs.Length; t++)
          packedPairs[t] = new KeyValuePair<object, object>(SerializeKey(pairs[t].Key, settings, keyInfo, depth + 1, byNames, table), SerializeObject(pairs[t].Value, settings, valueInfo, depth + 1, byNames));

        return new MpMap(packedPairs, settings);
      }

      Array items = (Array)packed.Value;
      FullPropertyInfo elementInfo = new FullPropertyInfo(elementType ?? info.ElementType);
      MsgPackItem[] packedItems = new MsgPackItem[items.Length];
      for (int t = 0; t < packedItems.Length; t++)
        packedItems[t] = SerializeObject(items.GetValue(t), settings, elementInfo, depth + 1, byNames);

      return new MpArray(settings) { Value = packedItems };
    }

    /// <summary>
    /// A key of a dictionary, written in full (<see cref="ReferenceHandling.Preserve"/>: never a reference, a path cannot point to a key).
    /// </summary>
    private static MsgPackItem SerializeKey(object key, MsgPackSettings settings, FullPropertyInfo keyInfo, int depth, bool byNames, ReferenceTable table)
    {
      if (table is null)
        return SerializeObject(key, settings, keyInfo, depth, byNames);
      table.InKeys++;
      try
      {
        return SerializeObject(key, settings, keyInfo, depth, byNames);
      }
      finally
      {
        table.InKeys--;
      }
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
