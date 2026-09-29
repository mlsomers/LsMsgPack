using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Types;
using LtMsgPack.Extensions;
using LtMsgPack.IO;
using LtMsgPack.Writing;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LtMsgPack
{
  /// <summary>
  /// The plans and caches of one <see cref="LtMsgPackSerializer"/> (its options do not change).
  /// </summary>
  internal sealed partial class Serializer
  {
    internal readonly LtMsgPackOptions Options;
    internal readonly FilterMode Filters;
    internal readonly bool CanBindDelegates;
    private readonly bool _alwaysTypeId;
    private readonly bool _customTypeResolvers;
    internal readonly bool CustomPropertyIds;

    private readonly ConcurrentDictionary<Type, object> _handlers = new ConcurrentDictionary<Type, object>();
    private readonly ConcurrentDictionary<Type, WriteTypeInfo> _typeInfos = new ConcurrentDictionary<Type, WriteTypeInfo>();
    private readonly ConcurrentDictionary<Type, ObjectPlan> _namePlans = new ConcurrentDictionary<Type, ObjectPlan>();
    private readonly ConcurrentDictionary<Type, ObjectPlan> _schemaPlans = new ConcurrentDictionary<Type, ObjectPlan>();
    private readonly ConcurrentDictionary<Type, SchemaTypeInfo> _schemaInfos = new ConcurrentDictionary<Type, SchemaTypeInfo>();
    private readonly ConcurrentDictionary<Type, object> _nameTypeIds = new ConcurrentDictionary<Type, object>();
    private readonly ConditionalWeakTable<FullPropertyInfo[], byte[][]> _sessionKeys = new ConditionalWeakTable<FullPropertyInfo[], byte[][]>();
    private readonly MsgPackWriter _keyWriter;

    internal Serializer(LtMsgPackOptions options)
    {
      Options = options;
      IMsgPackPropertyIncludeDynamicallyArray(options, out Filters);
      CanBindDelegates = PropertyAccessor.CanCompile;
      _alwaysTypeId = (options._addTypeIdOptions & AddTypeIdOption.Always) != 0;
      _customTypeResolvers = options._typeResolvers.Length > 0;
      CustomPropertyIds = options._propertyNameResolvers.Length > 0;
      _keyWriter = new MsgPackWriter(16);
      InitReading();
    }

    private static void IMsgPackPropertyIncludeDynamicallyArray(LtMsgPackOptions options, out FilterMode mode)
    {
      if (options._dynamicFilters is null || options._dynamicFilters.Length == 0)
        mode = FilterMode.None;
      else if (options._dynamicFilters.Length == 1 && options._dynamicFilters[0] != null && options._dynamicFilters[0].GetType() == typeof(FilterDefaultValues))
        mode = FilterMode.DefaultValues;
      else
        mode = FilterMode.Custom;
    }

    #region Handlers and plans

    /// <param name="declared">The property the values are assigned to (its [SerializeEnumerable] changes how collections are written), null for elements</param>
    internal ValueHandler<T> Handler<T>(FullPropertyInfo declared)
    {
      if (declared != null && declared.CustomAttributes.ContainsKey(nameof(SerializeEnumerableAttribute)))
        return new BoxedHandler<T>(this);

      if (_handlers.TryGetValue(typeof(T), out object handler))
        return (ValueHandler<T>)handler;
      return (ValueHandler<T>)_handlers.GetOrAdd(typeof(T), t => CreateHandler(t));
    }

    private object CreateHandler(Type type)
    {
      if (_alwaysTypeId) // every value decides about its type id (AddTypeIdOption.Always)
        return Boxed(type);

      Type underlying = Nullable.GetUnderlyingType(type);
      if (underlying != null)
      {
        object inner = GetType().GetMethod(nameof(Handler), BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(underlying).Invoke(this, new object[] { null });
        if (inner.GetType().IsGenericType && inner.GetType().GetGenericTypeDefinition() == typeof(BoxedHandler<>))
          return Boxed(type);
        return Activator.CreateInstance(typeof(NullableHandler<>).MakeGenericType(underlying), BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { inner }, null);
      }

      WriteTypeInfo info = GetTypeInfo(type);
      switch (info.Kind)
      {
        case TypeKind.Bool: return new BoolHandler();
        case TypeKind.SByte: return new SByteHandler();
        case TypeKind.Int16: return new Int16Handler();
        case TypeKind.Int32: return new Int32Handler();
        case TypeKind.Int64: return new Int64Handler();
        case TypeKind.Byte: return new ByteHandler();
        case TypeKind.UInt16: return new UInt16Handler();
        case TypeKind.UInt32: return new UInt32Handler();
        case TypeKind.UInt64: return new UInt64Handler();
        case TypeKind.Single: return new SingleHandler();
        case TypeKind.Double: return new DoubleHandler();
        case TypeKind.String: return new StringHandler();
        case TypeKind.Guid: return new GuidHandler();
        case TypeKind.GuidString: return new GuidStringHandler();
        case TypeKind.DecimalString: return new DecimalStringHandler();
        case TypeKind.DateTime: return new DateTimeHandler(Options._unspecifiedIsUtc);
        case TypeKind.DateTimeOffset: return new DateTimeOffsetHandler();
        case TypeKind.DateTimeOffsetArray: return new DateTimeOffsetArrayHandler();
        case TypeKind.Char: return new CharHandler();
        case TypeKind.TimeSpan: return new TimeSpanHandler();
        case TypeKind.Bin:
          return type == typeof(byte[]) ? new BinHandler() : Boxed(type);
        case TypeKind.Extension:
          if (type == typeof(decimal) && info.Extension is LtExtension<decimal> dec)
            return new DecimalHandler(dec);
          return Activator.CreateInstance(typeof(ExtensionHandler<>).MakeGenericType(type), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { info.Extension }, null);
        case TypeKind.Enum:
          return Activator.CreateInstance(typeof(EnumHandler<>).MakeGenericType(type), true);
        case TypeKind.Array:
          if (type.IsArray && type.GetArrayRank() == 1 && type == type.GetElementType().MakeArrayType())
            return Activator.CreateInstance(typeof(ArrayHandler<>).MakeGenericType(type.GetElementType()), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { this }, null);
          if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && CollectionInfo.Get(type).Attribute is null)
            return Activator.CreateInstance(typeof(ListHandler<>).MakeGenericType(type.GenericTypeArguments[0]), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { this }, null);
          return Boxed(type);
        case TypeKind.Complex:
          if (!type.IsValueType && type != typeof(object) && !type.IsInterface && !type.IsAbstract && !type.ContainsGenericParameters)
            return Activator.CreateInstance(typeof(ObjectHandler<>).MakeGenericType(type), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { this }, null);
          return Boxed(type);
        default:
          return Boxed(type);
      }
    }

    private object Boxed(Type type)
    {
      return Activator.CreateInstance(typeof(BoxedHandler<>).MakeGenericType(type), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { this }, null);
    }

    internal WriteTypeInfo GetTypeInfo(Type type)
    {
      if (_typeInfos.TryGetValue(type, out WriteTypeInfo info))
        return info;
      return _typeInfos.GetOrAdd(type, t => new WriteTypeInfo(t, Options));
    }

    internal ObjectPlan Plan(Type type, bool names)
    {
      ConcurrentDictionary<Type, ObjectPlan> plans = names ? _namePlans : _schemaPlans;
      if (plans.TryGetValue(type, out ObjectPlan plan))
        return plan;
      return plans.GetOrAdd(type, t => new ObjectPlan(this, t, names));
    }

    internal SchemaTypeInfo GetSchemaInfo(Type type)
    {
      if (_schemaInfos.TryGetValue(type, out SchemaTypeInfo info))
        return info;

      return _schemaInfos.GetOrAdd(type, t =>
      {
        ComplexTypeDef def = new ComplexTypeDef(0, t, Options); // the name as the schema has it (type resolvers, full or short name)
        FullPropertyInfo[] props = FullPropertyInfo.GetStaticallyIncludedProps(t, Options);
        string[] names = new string[props.Length];
        for (int p = 0; p < props.Length; p++)
          names[p] = props[p].PropertyInfo.Name;
        return new SchemaTypeInfo() { Type = t, Name = def.TypeName, IsCollection = t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t), PropertyNames = names };
      });
    }

    /// <summary>
    /// Names mode: the type id of LsMsgPack's GetTypeIdentifier (cached when no custom type resolvers decide it).
    /// </summary>
    internal object NameTypeId(Type type, FullPropertyInfo assignedTo)
    {
      if (_customTypeResolvers)
        return SerializationRules.GetTypeIdentifier(type, Options, assignedTo);

      if (_nameTypeIds.TryGetValue(type, out object id))
        return id;
      return _nameTypeIds.GetOrAdd(type, t => SerializationRules.GetTypeIdentifier(t, Options, null));
    }

    /// <summary>
    /// A map key (a property id) as LsMsgPack packs it.
    /// </summary>
    internal byte[] EncodeKey(object id)
    {
      lock (_keyWriter)
      {
        WriteContext c = new WriteContext(this) { W = _keyWriter, Mode = IdMode.Names, IdSettings = Options };
        _keyWriter.Reset(Options);
        WriteBoxed(c, id, null);
        return _keyWriter.ToArray();
      }
    }

    /// <summary>
    /// The encoded ids of the properties of a schema session (cached per array, which the session caches per type).
    /// </summary>
    internal byte[][] SessionKeys(FullPropertyInfo[] infos)
    {
      if (_sessionKeys.TryGetValue(infos, out byte[][] keys))
        return keys;

      keys = new byte[infos.Length][];
      for (int t = 0; t < infos.Length; t++)
        keys[t] = EncodeKey(infos[t].PropertyId);
      try
      {
        _sessionKeys.Add(infos, keys);
      }
      catch (ArgumentException) // added by another thread
      {
      }
      return keys;
    }

    #endregion

    #region Writing boxed values (LsMsgPack: SerializeObject)

    /// <summary>
    /// Writes any value the way LsMsgPack's SerializeObject does: leaf values (wrapped in a map with a type id when assigned to another type, except primitives and strings), collections and objects.
    /// </summary>
    /// <param name="assignedTo">The property or element the value is assigned to, null for map keys and type ids</param>
    internal void WriteBoxed(WriteContext c, object value, FullPropertyInfo assignedTo)
    {
      if (value is null)
      {
        c.W.Nil();
        return;
      }

      Type type = value.GetType();
      WriteTypeInfo info = GetTypeInfo(type);
      switch (info.Kind)
      {
        case TypeKind.Map:
        case TypeKind.Array:
          WriteCollection(c, value, type, info, assignedTo);
          return;

        case TypeKind.Complex:
          Plan(type, c.Mode == IdMode.Names).Write(c, value, SerializationRules.NeedsTypeId(type, assignedTo, Options), assignedTo);
          return;
      }

      if (assignedTo is null || info.NeverWrapped || !SerializationRules.NeedsTypeId(type, assignedTo, Options))
      {
        WriteLeaf(c, value, info);
        return;
      }

      c.W.MapHeader(2);
      c.W.String(MsgPackOptions.TypeIdKey);
      c.WriteTypeId(type, assignedTo);
      c.W.String(MsgPackOptions.ContentKey);
      WriteLeaf(c, value, info);
    }

    private void WriteLeaf(WriteContext c, object value, WriteTypeInfo info)
    {
      MsgPackWriter w = c.W;
      switch (info.Kind)
      {
        case TypeKind.Bool: w.Bool((bool)value); return;
        case TypeKind.SByte: w.SByte((sbyte)value); return;
        case TypeKind.Int16: w.Int16((short)value); return;
        case TypeKind.Int32: w.Int32((int)value); return;
        case TypeKind.Int64: w.Int64((long)value); return;
        case TypeKind.Byte: w.UInt8((byte)value); return;
        case TypeKind.UInt16: w.UInt16((ushort)value); return;
        case TypeKind.UInt32: w.UInt32((uint)value); return;
        case TypeKind.UInt64: w.UInt64((ulong)value); return;
        case TypeKind.Single: w.Single((float)value); return;
        case TypeKind.Double: w.Double((double)value); return;
        case TypeKind.String: w.String((string)value); return;
        case TypeKind.Bin:
          if (value is byte[] bytes)
            w.Bin(bytes);
          else
          {
            sbyte[] signed = (sbyte[])value;
            w.BinHeader(signed.Length);
            w.Ensure(signed.Length);
            Buffer.BlockCopy(signed, 0, w.Buf, w.Pos, signed.Length);
            w.Pos += signed.Length;
          }
          return;
        case TypeKind.Guid: w.Guid((Guid)value); return;
        case TypeKind.GuidString: w.String(((Guid)value).ToString("D")); return;
        case TypeKind.DecimalString: w.String(((decimal)value).ToString(CultureInfo.InvariantCulture)); return;
        case TypeKind.DateTime: w.DateTime(DateTimeHandler.Utc((DateTime)value, Options._unspecifiedIsUtc)); return;
        case TypeKind.DateTimeOffset: w.DateTime(((DateTimeOffset)value).UtcDateTime); return;
        case TypeKind.DateTimeOffsetArray: DateTimeOffsetArrayHandler.WriteArray(w, (DateTimeOffset)value); return;
        case TypeKind.Extension: ExtensionHandler<object>.WriteExtensionBoxed(c, info.Extension, value); return;
        case TypeKind.Enum: WriteEnum(w, value, info.Type); return;
        case TypeKind.Char: w.UInt16((char)value); return;
        case TypeKind.TimeSpan: w.Int64(((TimeSpan)value).Ticks); return;
        case TypeKind.Uri: w.String(((Uri)value).OriginalString); return;
        case TypeKind.DateOnly: w.Int32((int)FrameworkTypeInfo.DayNumber.GetValue(value)); return;
        case TypeKind.TimeOnly: w.Int64((long)FrameworkTypeInfo.TimeOnlyTicks.GetValue(value)); return;
        case TypeKind.RawExtension:
          MsgPackExtension extension = (MsgPackExtension)value;
          w.Extension(extension.TypeCode, extension.Data);
          return;
      }
      throw new InvalidOperationException($"{info.Kind} is not a leaf value.");
    }

    private static void WriteEnum(MsgPackWriter w, object value, Type type)
    {
      switch (Type.GetTypeCode(Enum.GetUnderlyingType(type)))
      {
        case TypeCode.SByte: w.SByte((sbyte)Convert.ToInt64(value)); return;
        case TypeCode.Int16: w.Int16((short)Convert.ToInt64(value)); return;
        case TypeCode.Int32: w.Int32((int)Convert.ToInt64(value)); return;
        case TypeCode.Int64: w.Int64(Convert.ToInt64(value)); return;
        case TypeCode.Byte: w.UInt8((byte)Convert.ToUInt64(value)); return;
        case TypeCode.UInt16: w.UInt16((ushort)Convert.ToUInt64(value)); return;
        case TypeCode.UInt32: w.UInt32((uint)Convert.ToUInt64(value)); return;
        case TypeCode.UInt64: w.UInt64(Convert.ToUInt64(value)); return;
      }
      throw new MsgPackException($"Unable to convert \"{value}\" to an integer type");
    }

    /// <summary>
    /// LsMsgPack: SerializeCollection. The elements as an array (a map for dictionaries), wrapped in a map when a type id or properties are needed.
    /// </summary>
    private void WriteCollection(WriteContext c, object value, Type type, WriteTypeInfo info, FullPropertyInfo assignedTo)
    {
      SerializeEnumerableAttribute handleItems = SerializationRules.GetEnumerableAttribute(type, assignedTo);
      bool serializeElements = handleItems?.SerializeElements ?? true;
      bool serializeProperties = handleItems?.SerializeProperties ?? false;
      bool addTypeId = SerializationRules.NeedsTypeId(type, assignedTo, Options);

      if (!addTypeId && !serializeProperties && serializeElements)
      {
        WriteElements(c, value, info, handleItems?.ElementType);
        return;
      }

      // LsMsgPack resolves the elements before the type id and properties (which may add types to the schema), so they are written first
      MsgPackWriter elements = null;
      if (serializeElements)
      {
        MsgPackWriter main = c.W;
        elements = c.RentWriter();
        c.W = elements;
        try
        {
          WriteElements(c, value, info, handleItems?.ElementType);
        }
        finally
        {
          c.W = main;
        }
      }

      ObjectPlan plan = null;
      byte[][] keys = null;
      FullPropertyInfo[] infos = null;
      int propCount = 0;
      if (serializeProperties)
      {
        plan = Plan(type, c.Mode == IdMode.Names);
        keys = c.BeginObject(plan, out infos);
        propCount = plan.Props.Length;
      }

      int max = propCount + 2;
      int at = c.W.ReserveMapHeader(max);
      int count = 0;
      if (addTypeId)
      {
        c.W.String(MsgPackOptions.TypeIdKey);
        c.WriteTypeId(type, assignedTo);
        count++;
      }
      else if (propCount > 0)
        SerializationRules.ThrowIfUnresolvableWithSchema(type, assignedTo, c.UsesSchema);

      if (elements != null)
      {
        c.W.String(MsgPackOptions.ContentKey);
        c.W.Raw(elements.Buf, 0, elements.Pos);
        c.ReturnWriter(elements);
        count++;
      }

      if (plan != null)
      {
        c.EnterContainer();
        count += plan.WriteProperties(c, value, keys, infos);
        c.LeaveContainer();
      }

      c.W.PatchMapHeader(at, max, count);
    }

    /// <summary>
    /// LsMsgPack: SerializeElements. The elements know the type they are assigned to, so type ids are only added for other types.
    /// </summary>
    private void WriteElements(WriteContext c, object value, WriteTypeInfo info, Type elementType)
    {
      c.EnterContainer();
      if (info.Kind == TypeKind.Map)
      {
        FullPropertyInfo keyInfo = info.KeyInfo;
        FullPropertyInfo valueInfo = info.ValueInfo;
        if (value is IDictionary dictionary)
        {
          c.W.MapHeader(dictionary.Count);
          IDictionaryEnumerator entries = dictionary.GetEnumerator();
          while (entries.MoveNext())
          {
            WriteBoxed(c, entries.Key, keyInfo);
            WriteBoxed(c, entries.Value, valueInfo);
          }
        }
        else // KeyValuePair<TKey, TValue>[]
        {
          Array pairs = (Array)value;
          c.W.MapHeader(pairs.Length);
          for (int t = 0; t < pairs.Length; t++)
          {
            object pair = pairs.GetValue(t);
            WriteBoxed(c, info.PairKey.GetValue(pair), keyInfo);
            WriteBoxed(c, info.PairValue.GetValue(pair), valueInfo);
          }
        }
      }
      else
      {
        FullPropertyInfo elementInfo = elementType is null ? info.ElementInfo : new FullPropertyInfo(elementType);
        if (value is object[] items)
        {
          c.W.ArrayHeader(items.Length);
          for (int t = 0; t < items.Length; t++)
            WriteBoxed(c, items[t], elementInfo);
        }
        else if (value is ICollection collection)
        {
          c.W.ArrayHeader(collection.Count);
          foreach (object item in collection)
            WriteBoxed(c, item, elementInfo);
        }
        else
        {
          List<object> list = new List<object>();
          foreach (object item in (IEnumerable)value)
            list.Add(item);
          c.W.ArrayHeader(list.Count);
          for (int t = 0; t < list.Count; t++)
            WriteBoxed(c, list[t], elementInfo);
        }
      }
      c.LeaveContainer();
    }

    #endregion
  }

  /// <summary>
  /// What the serializer knows about a runtime type.
  /// </summary>
  internal sealed class WriteTypeInfo
  {
    internal readonly Type Type;
    internal readonly TypeKind Kind;
    internal readonly LtExtension Extension;
    internal readonly bool NeverWrapped;
    internal readonly FullPropertyInfo ElementInfo;
    internal readonly FullPropertyInfo KeyInfo;
    internal readonly FullPropertyInfo ValueInfo;
    internal readonly PropertyInfo PairKey;
    internal readonly PropertyInfo PairValue;

    internal WriteTypeInfo(Type type, LtMsgPackOptions options)
    {
      Type = type;
      Kind = TypeKinds.Classify(type, options._extensions, out Extension);
      if (Kind == TypeKind.Guid && options._guidFormat == GuidFormat.String)
        Kind = TypeKind.GuidString;
      else if (Kind == TypeKind.DateTimeOffset && options._dateTimeOffsetFormat == DateTimeOffsetFormat.ClockTimeAndOffset)
        Kind = TypeKind.DateTimeOffsetArray;
      else if (type == typeof(decimal) && options._decimalFormat == DecimalFormat.String)
        Kind = TypeKind.DecimalString; // the extension (if any) still reads
      NeverWrapped = TypeKinds.NeverWrapped(type);
      if (Kind == TypeKind.Map || Kind == TypeKind.Array)
      {
        CollectionInfo collection = CollectionInfo.Get(type);
        ElementInfo = new FullPropertyInfo(collection.ElementType);
        KeyInfo = new FullPropertyInfo(collection.KeyType ?? typeof(object));
        ValueInfo = new FullPropertyInfo(collection.ValueType ?? typeof(object));
        if (Kind == TypeKind.Map && type.IsArray)
        {
          Type pair = type.GetElementType();
          PairKey = pair.GetProperty(nameof(KeyValuePair<object, object>.Key));
          PairValue = pair.GetProperty(nameof(KeyValuePair<object, object>.Value));
        }
      }
    }
  }
}
