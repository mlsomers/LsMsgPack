using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using LsMsgPack.Extensions;
using LsMsgPack.IO;
using LsMsgPack.Writing;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LsMsgPack
{
  /// <summary>
  /// The plans and caches of one <see cref="LsMsgPackSerializer"/> (its options do not change).
  /// </summary>
  internal sealed partial class Serializer
  {
    internal readonly LsMsgPackOptions Options;
    internal readonly FilterMode Filters;

    /// <summary>
    /// The dynamic filters have a <see cref="FilterShouldSerialize"/>: the properties with a ShouldSerialize method take the boxed filters (<see cref="CallsShouldSerialize"/>).
    /// </summary>
    private readonly bool _shouldSerialize;

    /// <summary>
    /// The filter mode of <see cref="PropWriter{TTarget, TValue, TFilter}"/> that calls the ShouldSerialize methods typed (<see cref="ShouldSerializeFilter"/>,
    /// <see cref="ShouldSerializeThenDefaultsFilter"/>, <see cref="DefaultsThenShouldSerializeFilter"/>), null when the filters are not one of those sets (the boxed filters).
    /// </summary>
    private readonly Type _typedShouldSerialize;

    /// <summary>
    /// A dynamic filter takes the owner (<see cref="IMsgPackPropertyIncludeWithOwner"/>): a struct is boxed for it.
    /// </summary>
    internal readonly bool OwnerFilters;

    /// <summary>
    /// <see cref="MsgPackOptions.IgnoreCycles"/>: the object handlers of classes are <see cref="CycleHandler{T}"/>s, <see cref="WriteBoxed"/> tracks the objects too.
    /// </summary>
    internal readonly bool IgnoreCycles;
    internal readonly bool CanBindDelegates;

    /// <summary>
    /// The plans compile their writers after this many objects (<see cref="LsMsgPackOptions.CompileWritersAfter"/>), -1 when they do not:
    /// no compiled code (CompiledWriter.Supported) or the writers would not be used (as for the generated code: custom dynamic filters, a type id on every value).
    /// </summary>
    internal readonly int CompileWritersAfter;
    private readonly bool _alwaysTypeId;
    private readonly bool _customTypeResolvers;
    internal readonly bool CustomPropertyIds;

    private readonly ConcurrentDictionary<Type, object> _handlers = new ConcurrentDictionary<Type, object>();
    private readonly ConcurrentDictionary<Type, WriteTypeInfo> _typeInfos = new ConcurrentDictionary<Type, WriteTypeInfo>();
    private readonly ConcurrentDictionary<Type, ObjectPlan> _namePlans = new ConcurrentDictionary<Type, ObjectPlan>();
    private readonly ConcurrentDictionary<Type, ObjectPlan> _schemaPlans = new ConcurrentDictionary<Type, ObjectPlan>();
    private readonly ConcurrentDictionary<Type, NamesPlan> _byNamesPlans = new ConcurrentDictionary<Type, NamesPlan>();
    private readonly ConcurrentDictionary<Type, SchemaTypeInfo> _schemaInfos = new ConcurrentDictionary<Type, SchemaTypeInfo>();
    private readonly ConcurrentDictionary<Type, object> _nameTypeIds = new ConcurrentDictionary<Type, object>();
    private readonly ConditionalWeakTable<FullPropertyInfo[], byte[][]> _sessionKeys = new ConditionalWeakTable<FullPropertyInfo[], byte[][]>();
    private readonly MsgPackWriter _keyWriter;

    internal Serializer(LsMsgPackOptions options)
    {
      Options = options;
      IMsgPackPropertyIncludeDynamicallyArray(options, out Filters);
      _shouldSerialize = options.CallsShouldSerialize;
      _typedShouldSerialize = _shouldSerialize ? TypedShouldSerialize(options._dynamicFilters) : null;
      OwnerFilters = HasOwnerFilters(options._dynamicFilters);
      IgnoreCycles = options._ignoreCycles;
      CanBindDelegates = PropertyAccessor.CanCompile;
      _alwaysTypeId = (options._addTypeIdOptions & AddTypeIdOption.Always) != 0;
      _customTypeResolvers = options._typeResolvers.Length > 0;
      CustomPropertyIds = options._propertyNameResolvers.Length > 0;
      _keyWriter = new MsgPackWriter(16);
      CompileWritersAfter = options._compileWritersAfter >= 0 && CompiledWriter.Supported && Filters != FilterMode.Custom && !_alwaysTypeId ? options._compileWritersAfter : -1;
      InitGenerated();
      InitReading();
    }

    private static void IMsgPackPropertyIncludeDynamicallyArray(LsMsgPackOptions options, out FilterMode mode)
    {
      IMsgPackPropertyIncludeDynamically[] filters = options._dynamicFilters;
      mode = FilterMode.None;
      if (filters is null)
        return;
      for (int t = 0; t < filters.Length; t++)
      {
        Type type = filters[t]?.GetType();
        if (type == typeof(FilterShouldSerialize))
          continue; // only the properties with a ShouldSerialize method take the boxed filters (CallsShouldSerialize)
        if (type == typeof(FilterDefaultValues) && !((FilterDefaultValues)filters[t]).OmitEmptyStrings) // the typed handlers decide as the default filter (DefaultFilter.Instance)
          mode = FilterMode.DefaultValues;
        else
        {
          mode = FilterMode.Custom;
          return;
        }
      }
    }

    private static bool HasOwnerFilters(IMsgPackPropertyIncludeDynamically[] filters)
    {
      if (filters is null)
        return false;
      for (int t = filters.Length - 1; t >= 0; t--)
        if (filters[t] is IMsgPackPropertyIncludeWithOwner)
          return true;
      return false;
    }

    /// <summary>
    /// <see cref="FilterShouldSerialize"/> calls a method of the owner for this property: its writer takes the boxed filters with the owner, the generated and compiled code leave it to that writer.
    /// </summary>
    internal bool CallsShouldSerialize(FullPropertyInfo info)
    {
      return _shouldSerialize && info.Attributes?.ShouldSerialize != null;
    }

    /// <summary>
    /// The filter sets that a property writer can apply typed, in the order <see cref="MsgPackOptions.IncludeDynamically"/> asks them (the last filter first: the calls of the method stay the same).
    /// </summary>
    private static Type TypedShouldSerialize(IMsgPackPropertyIncludeDynamically[] filters)
    {
      if (filters.Length == 1 && IsShouldSerialize(filters[0]))
        return typeof(ShouldSerializeFilter);
      if (filters.Length == 2)
      {
        if (IsTypedDefaults(filters[0]) && IsShouldSerialize(filters[1]))
          return typeof(ShouldSerializeThenDefaultsFilter);
        if (IsShouldSerialize(filters[0]) && IsTypedDefaults(filters[1]))
          return typeof(DefaultsThenShouldSerializeFilter);
      }
      return null;
    }

    private static bool IsShouldSerialize(IMsgPackPropertyIncludeDynamically filter)
    {
      return filter != null && filter.GetType() == typeof(FilterShouldSerialize);
    }

    private static bool IsTypedDefaults(IMsgPackPropertyIncludeDynamically filter)
    {
      return filter != null && filter.GetType() == typeof(FilterDefaultValues) && !((FilterDefaultValues)filter).OmitEmptyStrings; // as FilterMode.DefaultValues
    }

    /// <summary>
    /// The typed mode asks FilterDefaultValues before the ShouldSerialize method (<see cref="DefaultsThenShouldSerializeFilter"/>).
    /// </summary>
    internal bool ShouldSerializeDefaultsFirst
    {
      get { return _typedShouldSerialize == typeof(DefaultsThenShouldSerializeFilter); }
    }

    /// <summary>
    /// The typed filter mode for a property with a ShouldSerialize method (<see cref="CallsShouldSerialize"/>) written by a <see cref="PropWriter{TTarget, TValue, TFilter}"/> of <paramref name="declaring"/>:
    /// the method has a typed delegate on that class (dynamic code, not a struct or a static method), the filters are one of the typed sets, and no [DefaultValue] asks for the boxed default check.
    /// </summary>
    /// <returns>null for the boxed filters</returns>
    internal Type TypedShouldSerializeFilter(FullPropertyInfo info, Type declaring)
    {
      Type mode = _typedShouldSerialize;
      if (mode is null || info.Attributes.ShouldSerialize.OwnerType != declaring)
        return null;
      if (mode != typeof(ShouldSerializeFilter) && info.CustomAttributes.ContainsKey(nameof(DefaultValueAttribute)))
        return null;
      return mode;
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
        object inner = _handlers.TryGetValue(underlying, out object found) ? found : _handlers.GetOrAdd(underlying, t => CreateHandler(t)); // Handler<T>(null)
        if (inner.GetType().IsGenericType && inner.GetType().GetGenericTypeDefinition() == typeof(BoxedHandler<>))
          return Boxed(type);
        return Instantiate(type, typeof(NullableHandler<>), underlying, inner);
      }

      WriteTypeInfo info = GetTypeInfo(type);
      if (info.HasAnonymousType) // WriteBoxed decides how it is written (SerializationRules.WritesByNames)
        return Boxed(type);
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
        case TypeKind.DateTimeOffsetMoment: return new DateTimeOffsetMomentHandler();
        case TypeKind.Char: return new CharHandler();
        case TypeKind.TimeSpan: return new TimeSpanHandler();
        case TypeKind.Bin:
          return type == typeof(byte[]) ? new BinHandler() : Boxed(type);
        case TypeKind.Extension:
          if (type == typeof(decimal) && info.Extension is LsExtension<decimal> dec)
            return new DecimalHandler(dec);
          return Instantiate(type, typeof(ExtensionHandler<>), type, info.Extension);
        case TypeKind.Enum:
          return Instantiate(type, typeof(EnumHandler<>), type, null);
        case TypeKind.Array:
          object numbers = NumberHandler(type);
          if (numbers != null)
            return numbers;
          if (IsVector(type))
            return Instantiate(type, typeof(ArrayHandler<>), type.GetElementType(), this);
          if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && CollectionInfo.Get(type).Attribute is null)
            return Instantiate(type, typeof(ListHandler<>), type.GenericTypeArguments[0], this);
          if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
          {
            object bulk = InBulk(type.GenericTypeArguments[0]) ? CreateInBulk(type, type.GenericTypeArguments[0], BulkCollection.Set, new BulkHandlers(this)) : null;
            if (bulk != null)
              return bulk;
            return InstantiateCollection(type, typeof(HashSetHandler<>), type.GenericTypeArguments, this) ?? Boxed(type);
          }
          return Boxed(type);
        case TypeKind.Map:
          if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            return InstantiateCollection(type, typeof(DictionaryHandler<,>), type.GenericTypeArguments, this) ?? Boxed(type);
          return Boxed(type);
        case TypeKind.Complex:
          if (type.IsValueType ? StructPlans(type) : type != typeof(object) && !type.IsInterface && !type.IsAbstract && !type.ContainsGenericParameters)
          {
            object handler = Instantiate(type, typeof(ObjectHandler<>), type, this);
            return IgnoreCycles && !type.IsValueType && CanContainObjects(type) ? ((ICycleTracking)handler).Tracked() : handler; // apart, so the handler without the option stays as it is
          }
          return Boxed(type);
        default:
          return Boxed(type);
      }
    }

    /// <summary>
    /// A struct with properties (<see cref="TypeKind.Complex"/>) gets the object plans: by reference or boxed once, its properties read and set by typed delegates (StructPropWriter, StructPropReader).
    /// Without dynamic code those are the source generator's (a generated struct, <see cref="LsMsgPack.Generated.IGeneratedStruct"/>), otherwise they would be reflection: the struct is written and read boxed then (BoxedHandler, BoxedReader).
    /// </summary>
    internal bool StructPlans(Type type)
    {
      return (CanBindDelegates || Generated(type) is LsMsgPack.Generated.IGeneratedStruct) && !type.ContainsGenericParameters && Nullable.GetUnderlyingType(type) is null;
    }

    /// <summary>
    /// A one-dimensional array with a lower bound of zero (T[]), not a T[*] (one dimension, any lower bound: what a multidimensional array of rank 1 is).
    /// </summary>
    /// <returns>The handler of an array or list that writes its values in bulk (see <see cref="Bulk"/>); null for other types</returns>
    private object NumberHandler(Type type)
    {
      Type element = Bulk(type, out bool list);
      return element is null ? null : CreateInBulk(type, element, list ? BulkCollection.List : BulkCollection.Array, new BulkHandlers(this));
    }

    internal enum BulkCollection { Array, List, Set }

    /// <summary>
    /// Creates the handler or reader of a collection of a <see cref="InBulk(Type)"/> element type, of one kind of collection each (so a generated description instantiates only its own).
    /// </summary>
    internal interface IBulkCreator
    {
      object Array<T>() where T : unmanaged;

      /// <returns>null before net8.0 (lists are not written in bulk there, see <see cref="Bulk"/>)</returns>
      object List<T>() where T : unmanaged;

      object Set<T>() where T : unmanaged;
    }

    /// <summary>
    /// The handler or reader in bulk of <paramref name="type"/>: created by its generated description (<see cref="LsMsgPack.Generated.GeneratedValues.BulkArray{T}"/>...), otherwise by one switch over the element types
    /// for every kind of collection, without dynamic code. Only where the runtime has dynamic code: the switch instantiates every element type for every kind, which NativeAOT would all compile
    /// into the app (197 KB), so there the test of <see cref="RuntimeFeature.IsDynamicCodeSupported"/> itself (a constant for ILC) leaves it out, and only the types the source generator saw are in bulk.
    /// </summary>
    /// <returns>null when it is not created: the collection is written and read per value (a context of an older generator, a type it did not describe)</returns>
    internal object CreateInBulk<TCreator>(Type type, Type element, BulkCollection collection, TCreator creator) where TCreator : struct, IBulkCreator
    {
      if (Generated(type) is LsMsgPack.Generated.IGeneratedBulk generated)
        return generated.Create(creator);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      if (!RuntimeFeature.IsDynamicCodeSupported)
        return null;
#endif
      return WithBulkElement(element, collection, creator);
    }

    private static object WithBulkElement<TCreator>(Type element, BulkCollection collection, TCreator creator) where TCreator : struct, IBulkCreator
    {
      if (element == typeof(int)) return Create<int, TCreator>(collection, creator);
      if (element == typeof(long)) return Create<long, TCreator>(collection, creator);
      if (element == typeof(float)) return Create<float, TCreator>(collection, creator);
      if (element == typeof(double)) return Create<double, TCreator>(collection, creator);
      if (element == typeof(sbyte)) return Create<sbyte, TCreator>(collection, creator);
      if (element == typeof(short)) return Create<short, TCreator>(collection, creator);
      if (element == typeof(ushort)) return Create<ushort, TCreator>(collection, creator);
      if (element == typeof(uint)) return Create<uint, TCreator>(collection, creator);
      if (element == typeof(ulong)) return Create<ulong, TCreator>(collection, creator);
      if (element == typeof(bool)) return Create<bool, TCreator>(collection, creator);
      if (element == typeof(Guid)) return Create<Guid, TCreator>(collection, creator);
      return Create<decimal, TCreator>(collection, creator);
    }

    private static object Create<T, TCreator>(BulkCollection collection, TCreator creator) where T : unmanaged where TCreator : struct, IBulkCreator
    {
      if (collection == BulkCollection.Set)
        return creator.Set<T>();
      if (collection == BulkCollection.List)
        return creator.List<T>();
      return creator.Array<T>();
    }

    private struct BulkHandlers : IBulkCreator
    {
      private readonly Serializer _serializer;

      internal BulkHandlers(Serializer serializer)
      {
        _serializer = serializer;
      }

      public object Array<T>() where T : unmanaged
      {
        return new NumberArrayHandler<T>(_serializer, _serializer.DecimalCode(typeof(T)));
      }

      public object List<T>() where T : unmanaged
      {
#if NET8_0_OR_GREATER
        return new NumberListHandler<T>(_serializer, _serializer.DecimalCode(typeof(T)));
#else
        return null;
#endif
      }

      public object Set<T>() where T : unmanaged
      {
        return new NumberSetHandler<T>(_serializer, _serializer.DecimalCode(typeof(T)));
      }
    }

    /// <summary>
    /// The arrays (T[]) and lists (List&lt;T&gt;, net8.0: <c>CollectionsMarshal</c>) whose values are written and read in bulk (see <see cref="Numbers"/>): of int, long, float or double (written the same with any options),
    /// and of sbyte, short, ushort, uint, ulong, bool, Guid or decimal when the options write and read the element type as such (its <see cref="TypeKind"/>: not with a custom extension for it, nor Guids
    /// and decimals in another <see cref="GuidFormat"/> or <see cref="DecimalFormat"/>; decimals with the built-in <see cref="DecimalExtension"/>). Not DateTime: a loop without the call per value
    /// wrote 1000 of them 5-8% faster and read them as fast, the timestamp is most of it. Sets (<see cref="HashSet{T}"/>) of these types too (<see cref="InBulk"/>).
    /// </summary>
    /// <returns>The element type, or null for other types</returns>
    internal Type Bulk(Type type, out bool list)
    {
      list = false;
      Type element;
      if (type.IsArray && IsVector(type))
        element = type.GetElementType();
#if NET8_0_OR_GREATER
      else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
      {
        element = type.GenericTypeArguments[0];
        list = true;
      }
#endif
      else
        return null;

      return InBulk(element) ? element : null;
    }

    /// <summary>
    /// Whether arrays, lists and sets (<see cref="HashSet{T}"/>) of the type are written and read in bulk (see <see cref="Bulk"/>).
    /// </summary>
    internal bool InBulk(Type element)
    {
      if (element == typeof(int) || element == typeof(long) || element == typeof(float) || element == typeof(double))
        return true;
      TypeKind kind;
      if (element == typeof(sbyte)) kind = TypeKind.SByte; // a List<sbyte> (an sbyte[] is bin)
      else if (element == typeof(short)) kind = TypeKind.Int16;
      else if (element == typeof(ushort)) kind = TypeKind.UInt16;
      else if (element == typeof(uint)) kind = TypeKind.UInt32;
      else if (element == typeof(ulong)) kind = TypeKind.UInt64;
      else if (element == typeof(bool)) kind = TypeKind.Bool;
      else if (element == typeof(Guid)) kind = TypeKind.Guid;
      else if (element == typeof(decimal)) return GetTypeInfo(element).Kind == TypeKind.Extension && GetTypeInfo(element).Extension is DecimalExtension builtIn && builtIn.TypeCode != -1; // the built-in extension (-1 is read as a timestamp)
      else return false;
      return GetTypeInfo(element).Kind == kind;
    }

    /// <returns>The type code of the built-in decimal extension for decimals in bulk (<see cref="InBulk"/>), 0 for the other types</returns>
    internal byte DecimalCode(Type element)
    {
      return element == typeof(decimal) ? unchecked((byte)GetTypeInfo(element).Extension.TypeCode) : (byte)0;
    }

    internal static bool IsVector(Type type)
    {
      return type.IsArray && type.GetArrayRank() == 1 && !type.Name.EndsWith("[*]", StringComparison.Ordinal);
    }

    private object Boxed(Type type)
    {
      return Instantiate(type, typeof(BoxedHandler<>), type, this);
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

    /// <summary>
    /// <see cref="Plan"/> kept on the info of the type (boxed values, e.g. of polymorphic properties, look the info up anyway).
    /// </summary>
    private ObjectPlan PlanOf(WriteTypeInfo info, bool names)
    {
      ObjectPlan plan = names ? info.NamesPlan : info.SchemaPlan;
      if (plan != null)
        return plan;
      plan = Plan(info.Type, names);
      if (names)
        info.NamesPlan = plan;
      else
        info.SchemaPlan = plan;
      return plan;
    }

    private NamesPlan ByNamesPlan(Type type)
    {
      if (_byNamesPlans.TryGetValue(type, out NamesPlan plan))
        return plan;
      return _byNamesPlans.GetOrAdd(type, t => new NamesPlan(this, t));
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
          names[p] = props[p].Name;
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
      WriteBoxedAs(c, value, type, GetTypeInfo(type), assignedTo);
    }

    /// <summary>
    /// The root value (<see cref="WriteContext.Root"/>) of an untyped Serialize call, as <see cref="WriteBoxed(WriteContext, object, FullPropertyInfo)"/>.
    /// </summary>
    internal void WriteRoot(WriteContext c, FullPropertyInfo root)
    {
      object value = c.Root;
      if (value is null)
      {
        c.W.Nil();
        return;
      }

      Type type = value.GetType();
      WriteTypeInfo info = c.RootTypeInfo;
      if (info is null || info.Type != type) // calls on a thread usually write the same root type: no lookup
        c.RootTypeInfo = info = GetTypeInfo(type);
      WriteBoxedAs(c, value, type, info, root);
    }

    internal void WriteBoxedAs(WriteContext c, object value, Type type, WriteTypeInfo info, FullPropertyInfo assignedTo)
    {
      if (info.HasAnonymousType)
      {
        WriteWithAnonymousType(c, value, type, info, assignedTo);
        return;
      }
      WriteBoxed(c, value, type, info, assignedTo);
    }

    /// <summary>
    /// A value with an anonymous type (LsMsgPack: SerializeObject with its byNames parameter): an object of such a type by names where SerializationRules.WritesByNames says so,
    /// otherwise as any value, with the values below it in the names context where SerializationRules.EntersByNames says so (never a type id, see SerializationRules.NeedsTypeId).
    /// </summary>
    private void WriteWithAnonymousType(WriteContext c, object value, Type type, WriteTypeInfo info, FullPropertyInfo assignedTo)
    {
      bool byNames = c.ByNames;
      bool isObject = info.Kind == TypeKind.Complex;
      if (isObject && SerializationRules.WritesByNames(type, assignedTo, byNames))
      {
        if (IgnoreCycles)
        {
          if (!c.EnterObject(value))
          {
            c.W.Nil();
            return;
          }
          ByNamesPlan(type).Write(c, value);
          c.LeaveObject();
          return;
        }
        ByNamesPlan(type).Write(c, value);
        return;
      }
      c.ByNames = SerializationRules.EntersByNames(type, assignedTo, byNames, isObject);
      WriteBoxed(c, value, type, info, assignedTo);
      c.ByNames = byNames;
    }

    /// <summary>
    /// <see cref="MsgPackOptions.IgnoreCycles"/>: whether an object of exactly this class can contain an object with properties (itself, in a cycle). Its handler tracks it only then:
    /// with properties of only numbers, strings and the like nothing below it can be it, so the check could never find it (the same bytes as tracking it, as LsMsgPack does);
    /// an object of a derived type goes through <see cref="WriteBoxed"/>, which tracks it.
    /// </summary>
    private bool CanContainObjects(Type type)
    {
      return !HoldNoObjects(FullPropertyInfo.GetSerializedProps(type, Options)) || !HoldNoObjects(FullPropertyInfo.GetStaticallyIncludedProps(type, Options)); // both lists: the plans by names and by the schema
    }

    private static bool HoldNoObjects(FullPropertyInfo[] infos)
    {
      for (int t = infos.Length - 1; t >= 0; t--)
      {
        Type type = infos[t].DeclaredType;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
          || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(byte[])))
          return false;
      }
      return true;
    }

    /// <summary>
    /// <see cref="MsgPackOptions.IgnoreCycles"/>: an object with properties of a type that is not declared there (LsMsgPack: SerializeTracked), nil when it is already being written.
    /// </summary>
    private void WriteTracked(WriteContext c, object value, Type type, WriteTypeInfo info, FullPropertyInfo assignedTo)
    {
      if (!c.EnterObject(value))
      {
        c.W.Nil();
        return;
      }
      PlanOf(info, c.Mode == IdMode.Names).Write(c, value, SerializationRules.NeedsTypeId(type, assignedTo, Options, info.NeverHasTypeId), assignedTo);
      c.LeaveObject();
    }

    private void WriteBoxed(WriteContext c, object value, Type type, WriteTypeInfo info, FullPropertyInfo assignedTo)
    {
      switch (info.Kind)
      {
        case TypeKind.Map:
        case TypeKind.Array:
          WriteCollection(c, value, type, info, assignedTo);
          return;

        case TypeKind.Complex:
          if (IgnoreCycles && !type.IsValueType)
          {
            WriteTracked(c, value, type, info, assignedTo);
            return;
          }
          PlanOf(info, c.Mode == IdMode.Names).Write(c, value, SerializationRules.NeedsTypeId(type, assignedTo, Options, info.NeverHasTypeId), assignedTo);
          return;

        case TypeKind.Pair:
          WritePair(c, value, type, assignedTo);
          return;

        case TypeKind.Tuple:
          WriteTuple(c, value, type, assignedTo);
          return;

        case TypeKind.MultiArray:
          WriteMultidimensional(c, (Array)value, type, assignedTo);
          return;
      }

      if (assignedTo is null || info.NeverWrapped || !SerializationRules.NeedsTypeId(type, assignedTo, Options, info.NeverHasTypeId))
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
        case TypeKind.GuidString: w.GuidString((Guid)value); return;
        case TypeKind.DecimalString: w.DecimalString((decimal)value); return;
        case TypeKind.DateTime: w.DateTime(DateTimeHandler.Utc((DateTime)value, Options._unspecifiedIsUtc)); return;
        case TypeKind.DateTimeOffset: w.DateTime(((DateTimeOffset)value).UtcDateTime); return;
        case TypeKind.DateTimeOffsetArray: DateTimeOffsetArrayHandler.WriteArray(w, (DateTimeOffset)value); return;
        case TypeKind.DateTimeOffsetMoment: DateTimeOffsetMomentHandler.WriteArray(w, (DateTimeOffset)value); return;
        case TypeKind.BigIntegerBinary: w.Bin(FrameworkTypeInfo.BigIntegerBinary(value)); return;
        case TypeKind.Extension: ExtensionHandler<object>.WriteExtensionBoxed(c, info.Extension, value); return;
        case TypeKind.Enum: WriteEnum(w, value, info.Type); return;
        case TypeKind.Char: w.UInt16((char)value); return;
        case TypeKind.TimeSpan: w.Int64(((TimeSpan)value).Ticks); return;
        case TypeKind.Uri: w.String(((Uri)value).OriginalString); return;
        case TypeKind.DateOnly: w.Int32((int)FrameworkTypeInfo.DayNumber.GetValue(value)); return;
        case TypeKind.TimeOnly: w.Int64((long)FrameworkTypeInfo.TimeOnlyTicks.GetValue(value)); return;
        case TypeKind.Plain: WritePlain(w, FrameworkTypeInfo.ToPlain(value, info.PlainForm)); return;
        case TypeKind.RawExtension:
          MsgPackExtension extension = (MsgPackExtension)value;
          w.Extension(extension.TypeCode, extension.Data);
          return;
      }
      throw new InvalidOperationException($"{info.Kind} is not a leaf value.");
    }

    /// <summary>
    /// LsMsgPack: FrameworkTypes.PackPlain.
    /// </summary>
    private static void WritePlain(MsgPackWriter w, object plain)
    {
      switch (plain)
      {
        case float single: w.Single(single); return;
        case string text: w.String(text); return;
        case byte[] bytes: w.Bin(bytes); return;
        case PlainExtension extension: w.Extension(extension.TypeCode, extension.Data); return;
        case int number: w.Int32(number); return;
        case long number: w.Int64(number); return;
        case ulong number: w.UInt64(number); return;
        case double[] doubles:
          w.ArrayHeader(doubles.Length);
          for (int t = 0; t < doubles.Length; t++)
            w.Double(doubles[t]);
          return;
      }
      throw new InvalidOperationException($"{plain.GetType()} is not a plain value.");
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
    /// <summary>
    /// LsMsgPack: SerializePair. [key, value], wrapped like a collection when it needs a type id (the key and value first, they may add types to the schema).
    /// </summary>
    private void WritePair(WriteContext c, object value, Type type, FullPropertyInfo assignedTo)
    {
      FrameworkTypeInfo.PairInfo pair = FrameworkTypeInfo.GetPair(type);
      bool addTypeId = SerializationRules.NeedsTypeId(type, assignedTo, Options);
      MsgPackWriter main = c.W;
      MsgPackWriter content = addTypeId ? c.RentWriter() : main;
      c.W = content;
      c.EnterContainer();
      try
      {
        content.ArrayHeader(2);
        WriteBoxed(c, pair.Key.GetValue(value), pair.KeyInfo);
        WriteBoxed(c, pair.Value.GetValue(value), pair.ValueInfo);
      }
      finally
      {
        c.LeaveContainer();
        c.W = main;
      }
      if (!addTypeId)
        return;

      main.MapHeader(2);
      main.String(MsgPackOptions.TypeIdKey);
      c.WriteTypeId(type, assignedTo);
      main.String(MsgPackOptions.ContentKey);
      main.Raw(content.Buf, 0, content.Pos);
      c.ReturnWriter(content);
    }

    /// <summary>
    /// LsMsgPack: SerializeTuple. The items as an array, wrapped like a pair when it needs a type id.
    /// </summary>
    private void WriteTuple(WriteContext c, object value, Type type, FullPropertyInfo assignedTo)
    {
      FrameworkTypeInfo.TupleInfo tuple = FrameworkTypeInfo.GetTuple(type);
      bool addTypeId = SerializationRules.NeedsTypeId(type, assignedTo, Options);
      MsgPackWriter main = c.W;
      MsgPackWriter content = addTypeId ? c.RentWriter() : main;
      c.W = content;
      c.EnterContainer();
      try
      {
        content.ArrayHeader(tuple.Items.Length);
        for (int t = 0; t < tuple.Items.Length; t++)
          WriteBoxed(c, tuple.GetItem(value, t), tuple.Items[t]);
      }
      finally
      {
        c.LeaveContainer();
        c.W = main;
      }
      if (!addTypeId)
        return;

      main.MapHeader(2);
      main.String(MsgPackOptions.TypeIdKey);
      c.WriteTypeId(type, assignedTo);
      main.String(MsgPackOptions.ContentKey);
      main.Raw(content.Buf, 0, content.Pos);
      c.ReturnWriter(content);
    }

    /// <summary>
    /// LsMsgPack: SerializeMultidimensional. [length0, length1, ..., [items]], wrapped like a collection when it needs a type id (the items first, they may add types to the schema).
    /// </summary>
    private void WriteMultidimensional(WriteContext c, Array array, Type type, FullPropertyInfo assignedTo)
    {
      bool addTypeId = SerializationRules.NeedsTypeId(type, assignedTo, Options);
      FullPropertyInfo elementInfo = GetTypeInfo(type).ElementInfo;
      MsgPackWriter main = c.W;
      MsgPackWriter content = addTypeId ? c.RentWriter() : main;
      c.W = content;
      c.EnterContainer();
      try
      {
        content.ArrayHeader(array.Rank + 1);
        for (int t = 0; t < array.Rank; t++)
          content.Int32(array.GetLength(t));
        content.ArrayHeader(array.Length);
        foreach (object element in array) // the last dimension changes first
          WriteBoxed(c, element, elementInfo);
      }
      finally
      {
        c.LeaveContainer();
        c.W = main;
      }
      if (!addTypeId)
        return;

      main.MapHeader(2);
      main.String(MsgPackOptions.TypeIdKey);
      c.WriteTypeId(type, assignedTo);
      main.String(MsgPackOptions.ContentKey);
      main.Raw(content.Buf, 0, content.Pos);
      c.ReturnWriter(content);
    }

    private void WriteCollection(WriteContext c, object value, Type type, WriteTypeInfo info, FullPropertyInfo assignedTo)
    {
      SerializeEnumerableAttribute handleItems = SerializationRules.GetEnumerableAttribute(info.Collection, assignedTo);
      bool serializeElements = handleItems?.SerializeElements ?? true;
      bool serializeProperties = handleItems?.SerializeProperties ?? false;
      bool addTypeId = SerializationRules.NeedsTypeId(type, assignedTo, Options, info.NeverHasTypeId);

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
      if (elementType is null && TypedElements(info) is ITypedElements typed)
      {
        typed.WriteElements(c, value);
        return;
      }

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
        else if (!(value is Array)) // other collections of KeyValuePair<TKey, TValue>
        {
          KeyValuePair<object, object>[] pairs = info.Collection.ToPairs((IEnumerable)value);
          c.W.MapHeader(pairs.Length);
          for (int t = 0; t < pairs.Length; t++)
          {
            WriteBoxed(c, pairs[t].Key, keyInfo);
            WriteBoxed(c, pairs[t].Value, valueInfo);
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
        else if (value is IList indexed && !(value is Array multi && multi.Rank > 1)) // the indexer of a multidimensional array throws
        {
          c.W.ArrayHeader(indexed.Count);
          for (int t = 0; t < indexed.Count; t++)
            WriteBoxed(c, indexed[t], elementInfo);
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

    /// <summary>
    /// The handler of the collection type when it writes the elements without boxing them (see <see cref="ITypedElements"/>), kept on the info.
    /// </summary>
    private ITypedElements TypedElements(WriteTypeInfo info)
    {
      if (info.ElementsKnown)
        return info.Elements;

      Type type = info.Type;
      Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
      if ((CanBindDelegates || Generated(type) != null) // without dynamic code only the instantiations of the generated code exist, not those of every runtime type
        && (IsVector(type) || definition == typeof(List<>) || definition == typeof(HashSet<>) || definition == typeof(Dictionary<,>)))
      {
        object handler = _handlers.TryGetValue(type, out object found) ? found : _handlers.GetOrAdd(type, t => CreateHandler(t));
        info.Elements = handler as ITypedElements;
      }
      info.ElementsKnown = true; // another thread that still sees false looks it up again
      return info.Elements;
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
    internal readonly LsExtension Extension;
    internal readonly bool NeverWrapped;
    internal readonly FullPropertyInfo ElementInfo;
    internal readonly CollectionInfo Collection;
    internal readonly FullPropertyInfo KeyInfo;
    internal readonly FullPropertyInfo ValueInfo;
    internal readonly PropertyInfo PairKey;
    internal readonly PropertyInfo PairValue;
    internal readonly PlainForm PlainForm;

    /// <summary>
    /// The type is anonymous, or has an anonymous type as a generic argument or element (see SerializationRules.HasAnonymousType): written through <see cref="Serializer.WriteBoxed(WriteContext, object, FullPropertyInfo)"/>.
    /// </summary>
    internal readonly bool HasAnonymousType;

    /// <summary>
    /// SerializationRules.NeverHasTypeId, asked per value.
    /// </summary>
    internal readonly bool NeverHasTypeId;

    /// <summary>
    /// The plans of an object (<see cref="TypeKind.Complex"/>), set when first used (see Serializer.PlanOf): the infos belong to one serializer.
    /// </summary>
    internal ObjectPlan NamesPlan;
    internal ObjectPlan SchemaPlan;

    /// <summary>
    /// The handler that writes the elements of this collection type, null when they are written boxed (see Serializer.TypedElements).
    /// </summary>
    internal ITypedElements Elements;
    internal bool ElementsKnown;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(KeyValuePair<,>))]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.KeptMembers)]
    internal WriteTypeInfo(Type type, LsMsgPackOptions options)
    {
      Type = type;
      Kind = TypeKinds.Classify(type, options._extensions, out Extension);
      HasAnonymousType = SerializationRules.HasAnonymousType(type);
      NeverHasTypeId = SerializationRules.NeverHasTypeId(type);
      if (Kind == TypeKind.Plain)
        PlainForm = FrameworkTypeInfo.GetPlainForm(type);
      if (Kind == TypeKind.Guid && options._guidFormat == GuidFormat.String)
        Kind = TypeKind.GuidString;
      else if (Kind == TypeKind.DateTimeOffset && options._dateTimeOffsetFormat == DateTimeOffsetFormat.ClockTimeAndOffset)
        Kind = TypeKind.DateTimeOffsetArray;
      else if (Kind == TypeKind.DateTimeOffset && options._dateTimeOffsetFormat == DateTimeOffsetFormat.TimestampAndOffset)
        Kind = TypeKind.DateTimeOffsetMoment;
      else if (Kind == TypeKind.Plain && FrameworkTypeInfo.IsBigInteger(type) && options._bigIntegerFormat == BigIntegerFormat.Binary)
        Kind = TypeKind.BigIntegerBinary;
      else if (type == typeof(decimal) && options._decimalFormat == DecimalFormat.String)
        Kind = TypeKind.DecimalString; // the extension (if any) still reads
      NeverWrapped = TypeKinds.NeverWrapped(type);
      if (Kind == TypeKind.MultiArray)
        ElementInfo = new FullPropertyInfo(type.GetElementType());
      if (Kind == TypeKind.Map || Kind == TypeKind.Array)
      {
        CollectionInfo collection = CollectionInfo.Get(type);
        Collection = collection;
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
