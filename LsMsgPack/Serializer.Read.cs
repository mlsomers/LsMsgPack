using LsMsgPack;
using LsMsgPack.Compression;
using LsMsgPack.Converters;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using LsMsgPack.Extensions;
using LsMsgPack.Generated;
using LsMsgPack.IO;
using LsMsgPack.Reading;
using LsMsgPack.References;
using LsMsgPack.Writing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace LsMsgPack
{
  internal sealed partial class Serializer
  {
    private readonly ConcurrentDictionary<Type, ValueReader> _readers = new ConcurrentDictionary<Type, ValueReader>();

    /// <summary>
    /// The readers of the members' own converters ([MsgPackConverter] on a property or field), by converter.
    /// </summary>
    private readonly ConcurrentDictionary<MsgPackConverter, ValueReader> _memberReaders = new ConcurrentDictionary<MsgPackConverter, ValueReader>();
    private readonly ConcurrentDictionary<Type, ReadPlan> _readPlans = new ConcurrentDictionary<Type, ReadPlan>();
    private readonly ConditionalWeakTable<SchemaSession, SessionShared> _sessions = new ConditionalWeakTable<SchemaSession, SessionShared>();

    /// <summary>
    /// Objects are read as LsMsgPack does (not by the typed readers): custom property id resolvers decide the keys, custom type resolvers may pick a type by the properties.
    /// </summary>
    internal bool SlowObjects;

    /// <summary>
    /// Caches the inline schemas that are read, when the options have no <see cref="SchemaStore"/>.
    /// </summary>
    private SchemaStore _inlineStore;

    /// <summary>
    /// The schema read last (inline or a reference, cached in the store): the next payload usually starts with the same bytes, which are compared instead of measured, copied and looked up.
    /// </summary>
    private volatile SchemaHit _lastSchema;

    /// <summary>
    /// Every schema read that the store keeps (so as many as the store's entries), by its <see cref="SchemaKey"/>: behind <see cref="_lastSchema"/> and the recent ones (<see cref="RecentReads.Schemas"/>),
    /// a reader of any number of root types finds a schema without copying it (a reference) or with a hash of its bytes instead of a copy (inline), and allocates nothing.
    /// </summary>
    private readonly ConcurrentDictionary<SchemaKey, SchemaHit> _knownSchemas = new ConcurrentDictionary<SchemaKey, SchemaHit>();

    /// <summary>
    /// A reference: its schema id; an inline schema: a hash of its bytes and their length. The bytes of the hit are compared before it is used, so a hash that two schemas share only costs the lookups of the store.
    /// </summary>
    private struct SchemaKey : IEquatable<SchemaKey>
    {
      private readonly long _a, _b;
      private readonly int _length;

      private SchemaKey(long a, long b, int length)
      {
        _a = a;
        _b = b;
        _length = length;
      }

      /// <param name="length">The schema's bytes, or the reference (<see cref="SchemaStore.ReferenceLength"/> bytes, starting with fixext 16)</param>
      internal static SchemaKey Of(byte[] buffer, int offset, int length)
      {
        ReadOnlySpan<byte> bytes = new ReadOnlySpan<byte>(buffer, offset, length);
        if (length == SchemaStore.ReferenceLength && bytes[0] == (byte)MsgPackTypeId.MpFExt16) // an inline schema starts with a map
          return new SchemaKey(MemoryMarshal.Read<long>(bytes.Slice(2)), MemoryMarshal.Read<long>(bytes.Slice(2 + 8)), -1);

        ulong hash = 0xCBF29CE484222325UL;
        int t = 0;
        for (; t + 8 <= length; t += 8)
          hash = RotateLeft(hash ^ MemoryMarshal.Read<ulong>(bytes.Slice(t)), 29) * 0x9E3779B97F4A7C15UL;
        for (; t < length; t++)
          hash = (hash ^ bytes[t]) * 0x100000001B3UL;
        return new SchemaKey(unchecked((long)hash), 0, length);
      }

      private static ulong RotateLeft(ulong value, int bits)
      {
        return (value << bits) | (value >> (64 - bits));
      }

      public bool Equals(SchemaKey other)
      {
        return _a == other._a && _b == other._b && _length == other._length;
      }

      public override bool Equals(object obj)
      {
        return obj is SchemaKey other && Equals(other);
      }

      public override int GetHashCode()
      {
        return unchecked((int)_a ^ (int)(_a >> 32) ^ (int)_b ^ _length);
      }
    }

    /// <returns>The known schema whose bytes (or reference) are these, null when there is none</returns>
    private SchemaHit KnownSchema(byte[] buffer, int offset, int length)
    {
      if (_knownSchemas.IsEmpty) // one schema so far: the last one, compared already
        return null;
      if (!_knownSchemas.TryGetValue(SchemaKey.Of(buffer, offset, length), out SchemaHit hit) || hit.Bytes.Length != length || !new ReadOnlySpan<byte>(buffer, offset, length).SequenceEqual(hit.Bytes))
        return null;
      AddRecentSchema(hit);
      return hit;
    }

    private sealed class SchemaHit
    {
      /// <summary>
      /// The schema as it was read, or the reference to it.
      /// </summary>
      internal readonly byte[] Bytes;
      internal readonly SchemaStore.Entry Entry;
      internal readonly MsgPackOptions LengthSettings;

      /// <summary>
      /// The sessions of the schema for the options of this serializer (they do not change).
      /// </summary>
      internal readonly SchemaStore.SessionState State;

      /// <summary>
      /// The shared settings and binding of the session it was read with last (checked before it is used: a grown session replaces it).
      /// </summary>
      internal SessionShared Shared;

      internal SchemaHit(byte[] bytes, SchemaStore.Entry entry, MsgPackOptions lengthSettings, SchemaStore.SessionState state)
      {
        Bytes = bytes;
        Entry = entry;
        LengthSettings = lengthSettings;
        State = state;
      }
    }

    /// <summary>
    /// What the calls with a frozen (published) session share: the settings using the session, and for reading the binding of the schema to the local types.
    /// Only read, by any number of threads: LsMsgPack does not change settings while it uses them.
    /// </summary>
    internal sealed class SessionShared
    {
      internal readonly SchemaSession Session;
      private BoundSchema _bound;
      private MsgPackOptions _settings;

      internal SessionShared(SchemaSession session)
      {
        Session = session;
      }

      internal BoundSchema Bound
      {
        get { return _bound ?? (_bound = new BoundSchema(Session.Resolver, true)); }
      }

      /// <param name="options">The options of the serializer</param>
      internal MsgPackOptions Settings(MsgPackOptions options)
      {
        return _settings ?? (_settings = Session.Apply(options));
      }
    }

    private volatile SessionShared _lastSession;

    /// <summary>
    /// The caches for reading several root types in turn, made when a second root type, schema or session shows up: a reader of one root type allocates nothing more than before
    /// (the arrays made with the serializer, and a root entry before the plans and readers were made, moved those in the heap: reading game snapshots varied 2-5% between processes).
    /// </summary>
    private volatile RecentReads _recent;

    private RecentReads Recent()
    {
      return _recent ?? (_recent = new RecentReads()); // a race may make two, one is lost: it is a cache
    }

    private sealed class RecentReads
    {
      /// <summary>
      /// The number of entries of each cache, a power of 2.
      /// </summary>
      internal const int Size = 8;

      /// <summary>
      /// The schemas read before, compared when the payload does not start with <see cref="_lastSchema"/>: a reader of several root types in turn (a server receiving several kinds of request bodies)
      /// finds its schema without measuring, copying and looking it up. A ring that replaces its oldest entry (a grown schema has a new id, the old one stops coming); entries are only replaced, never changed.
      /// </summary>
      internal readonly SchemaHit[] Schemas = new SchemaHit[Size];
      private int _nextSchema;

      /// <summary>
      /// The shared sessions used before <see cref="_lastSession"/>, a ring as <see cref="Schemas"/> (a grown session replaces the old one).
      /// </summary>
      internal readonly SessionShared[] Sessions = new SessionShared[Size];
      private int _nextSession;

      /// <summary>
      /// The first <see cref="Size"/> root types read since there were two (added and never removed: types do not go stale): their assemblies are cached, and they keep their reader, so a switch of
      /// the root type is a comparison of references instead of lookups by the type (the hash of a Type is a call into the runtime). The others are in <see cref="Others"/>.
      /// </summary>
      internal RootEntry[] Roots = new RootEntry[0];

      /// <summary>
      /// All root types read since there were two, for the ones after <see cref="Roots"/>: one lookup instead of those of the cached assemblies and the reader.
      /// </summary>
      internal readonly ConcurrentDictionary<Type, RootEntry> Others = new ConcurrentDictionary<Type, RootEntry>();

      internal void AddSchema(SchemaHit hit)
      {
        int next = _nextSchema; // a race may skip or overwrite a slot: it is a cache
        _nextSchema = next + 1;
        Schemas[next & (Size - 1)] = hit;
      }

      internal void AddSession(SessionShared shared)
      {
        int next = _nextSession;
        _nextSession = next + 1;
        Sessions[next & (Size - 1)] = shared;
      }

      internal void AddRoot(Type type)
      {
        RootEntry entry = Others.GetOrAdd(type, t => new RootEntry(t));
        RootEntry[] roots = Roots;
        if (roots.Length < Size) // a copy, other threads read the array (one that adds at the same time may get lost, it comes back)
        {
          RootEntry[] more = new RootEntry[roots.Length + 1];
          Array.Copy(roots, more, roots.Length);
          more[roots.Length] = entry;
          Roots = more;
        }
      }
    }

    /// <summary>
    /// The type whose assembly was cached last (see <see cref="DeserializeWithSchema"/>).
    /// </summary>
    private volatile Type _cachedRoot;

    private sealed class RootEntry
    {
      internal readonly Type Type;

      /// <summary>
      /// Set when the type is first read as the root (not when its assemblies are cached: the reader is made where it was before).
      /// </summary>
      internal ValueReader Reader;

      internal RootEntry(Type type)
      {
        Type = type;
      }
    }

    /// <summary>
    /// The type of a value in a body whose assembly was cached last (see <see cref="DeserializeBody"/>).
    /// </summary>
    private volatile Type _cachedBodyType;

    internal bool CustomTypeResolvers { get { return _customTypeResolvers; } }

    /// <summary>
    /// The read plans compile their readers after this many objects (<see cref="LsMsgPackOptions.CompileReadersAfter"/>), -1 when they do not:
    /// no compiled code (CompiledWriter.Supported), or the plans do not read the objects (<see cref="SlowObjects"/>).
    /// </summary>
    internal int CompileReadersAfter;

    private void InitReading()
    {
      SlowObjects = CustomPropertyIds || _customTypeResolvers;
      CompileReadersAfter = Options._compileReadersAfter >= 0 && CompiledWriter.Supported && !SlowObjects ? Options._compileReadersAfter : -1;
      _inlineStore = new SchemaStore();
    }

    #region Readers and plans

    internal ValueReader<T> Reader<T>()
    {
      return Typed<T>(Reader(typeof(T)));
    }

    /// <summary>
    /// The reader as generic code needs it: a <see cref="ConvertingReader"/> (no dynamic code, no instantiation for the type) is replaced by a <see cref="BoxedReader{T}"/>,
    /// which exists here, since this code was compiled for T.
    /// </summary>
    private ValueReader<T> Typed<T>(ValueReader reader)
    {
      ValueReader<T> typed = reader as ValueReader<T>;
      if (typed != null)
        return typed;
      if (!(reader is ConvertingReader))
        return (ValueReader<T>)reader; // the cast's error, as before
      typed = new BoxedReader<T>();
      _readers.TryUpdate(typeof(T), typed, reader);
      return typed;
    }

    /// <summary>
    /// The reader of a property's or field's value: of its own converter when it has one ([MsgPackConverter] on the member), otherwise of its type.
    /// </summary>
    internal ValueReader<T> ReaderFor<T>(FullPropertyInfo declared)
    {
      ValueReader reader = ReaderFor(typeof(T), declared);
      return reader is ConvertingReader && declared?.MemberConverter != null ? new BoxedReader<T>() : Typed<T>(reader); // a member's own converter: Core applies it (assignedTo)
    }

    internal ValueReader ReaderFor(Type type, FullPropertyInfo declared)
    {
      MsgPackConverter member = declared?.MemberConverter;
      if (member is null)
        return Reader(type);
      ValueReader exact = _memberReaders.TryGetValue(member, out ValueReader found) ? found : _memberReaders.GetOrAdd(member, m => Preserving(m.ConvertedType, (ValueReader)m.Accept(new ConverterReaders(this))));
      if (type == member.ConvertedType)
        return exact;
      return (ValueReader)Instantiate(type, typeof(NullableReader<>), member.ConvertedType, exact) ?? new ConvertingReader(type); // no code for it (NativeAOT): Core applies the converter (assignedTo)
    }

    internal ValueReader Reader(Type type)
    {
      if (_readers.TryGetValue(type, out ValueReader reader))
        return reader;
      return _readers.GetOrAdd(type, t => CreateReader(t));
    }

    private ValueReader CreateReader(Type type)
    {
      return Preserving(type, ReaderOf(type));
    }

    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/>: the reader of a class resolves references and registers what it read (<see cref="PreservingReader{T}"/>; objects also when they are created, ReadPlan.Create).
    /// </summary>
    private ValueReader Preserving(Type type, ValueReader reader)
    {
      if (!PreserveReferences || type.IsValueType || type == typeof(string) || type == typeof(byte[]))
        return reader;
      return ((IPreservableReader)reader).Preserving();
    }

    private ValueReader ReaderOf(Type type)
    {
      Type underlying = Nullable.GetUnderlyingType(type);
      if (underlying != null)
      {
        ValueReader inner = Reader(underlying);
        if (inner is ConvertingReader) // no dynamic code and no generic reader of the type (NativeAOT): nor of its nullable
          return new ConvertingReader(type);
        return (ValueReader)Instantiate(type, typeof(NullableReader<>), underlying, inner) ?? new ConvertingReader(type);
      }

      WriteTypeInfo info = GetTypeInfo(type);
      switch (info.Kind)
      {
        case TypeKind.Bool: return new BoolReader();
        case TypeKind.SByte: return new IntegerReader<sbyte>();
        case TypeKind.Int16: return new IntegerReader<short>();
        case TypeKind.Int32: return new IntegerReader<int>();
        case TypeKind.Int64: return new IntegerReader<long>();
        case TypeKind.Byte: return new IntegerReader<byte>();
        case TypeKind.UInt16: return new IntegerReader<ushort>();
        case TypeKind.UInt32: return new IntegerReader<uint>();
        case TypeKind.UInt64: return new IntegerReader<ulong>();
        case TypeKind.Char: return new IntegerReader<char>();
        case TypeKind.Single: return new SingleReader();
        case TypeKind.Double: return new DoubleReader();
        case TypeKind.String: return new StringReader();
        case TypeKind.Guid: return new GuidReader();
        case TypeKind.GuidString: return new GuidStringReader();
        case TypeKind.DecimalString: return new DecimalStringReader(info.Extension);
        case TypeKind.DateTime:
        case TypeKind.DateTimeTimestamp: return new DateTimeReader(); // both formats
        case TypeKind.DateTimeOffset:
        case TypeKind.DateTimeOffsetMoment: return new DateTimeOffsetReader();
        case TypeKind.DateTimeOffsetArray: return new DateTimeOffsetArrayReader();
        case TypeKind.TimeSpan: return new TimeSpanReader();
        case TypeKind.Bin:
          if (type == typeof(byte[]))
            return new BinReader();
          break;
        case TypeKind.Extension:
          if (type == typeof(decimal) && info.Extension is DecimalExtension builtIn && builtIn.TypeCode != -1) // -1 is the timestamp, read as such (as LsMsgPack)
            return new DecimalReader(builtIn);
          ValueReader extension = ExtensionReader(type, info.Extension);
          if (extension != null)
            return extension;
          break;
        case TypeKind.Converted:
          return (ValueReader)info.Converter.Accept(new ConverterReaders(this));
        case TypeKind.Enum:
        case TypeKind.EnumName: // numbers and names, whatever is written
          ValueReader enumReader = (ValueReader)Instantiate(type, typeof(EnumReader<>), type, null);
          if (enumReader != null)
            return enumReader;
          break; // no code for it (NativeAOT): boxed
        case TypeKind.Array:
          ValueReader numbers = NumberReader(type);
          if (numbers != null)
            return numbers;
          ValueReader list = null; // null without code for it (NativeAOT): boxed
          if (IsVector(type))
            list = (ValueReader)Instantiate(type, typeof(ArrayReader<>), type.GetElementType(), this);
          else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && CollectionInfo.Get(type).Attribute is null)
            list = (ValueReader)Instantiate(type, typeof(ListReader<>), type.GenericTypeArguments[0], this);
          if (list != null)
            return list;
          if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
          {
            ValueReader bulk = InBulk(type.GenericTypeArguments[0]) ? (ValueReader)CreateInBulk(type, type.GenericTypeArguments[0], BulkCollection.Set, new BulkReaders(this)) : null;
            if (bulk != null)
              return bulk;
            ValueReader set = (ValueReader)InstantiateCollection(type, typeof(HashSetReader<>), type.GenericTypeArguments, this);
            if (set != null)
              return set;
          }
          ValueReader items = InterfaceReader(type) ?? OtherCollectionReader(type);
          if (items != null)
            return items;
          break;
        case TypeKind.Map:
          if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
          {
            ValueReader dictionary = (ValueReader)InstantiateCollection(type, typeof(DictionaryReader<,>), type.GenericTypeArguments, this);
            if (dictionary != null)
              return dictionary;
          }
          ValueReader entries = InterfaceReader(type) ?? OtherDictionaryReader(type);
          if (entries != null)
            return entries;
          break;
        case TypeKind.Complex:
        case TypeKind.Extended: // its plan reads it until a member the class does not have (ReadPlan.Extended), Core keeps those
          if (type == typeof(object)) // an object with a type id is read by the plan of its type (ObjectReader.Derived), anything else as LsMsgPack does (Slow)
            return new ObjectReader<object>(this);
          if (type.IsValueType ? StructPlans(type) : !type.ContainsGenericParameters)
          {
            ValueReader objects = (ValueReader)Instantiate(type, typeof(ObjectReader<>), type, this);
            if (objects != null)
              return objects;
          }
          break;
      }
      return (ValueReader)Instantiate(type, typeof(BoxedReader<>), type, null) ?? new ConvertingReader(type); // no code for it (NativeAOT, a value type): Core converts it
    }

    /// <returns>The reader of an array or list that reads its values in bulk (see <see cref="Bulk"/>); null for other types</returns>
    private ValueReader NumberReader(Type type)
    {
      Type element = Bulk(type, out bool list);
      return element is null ? null : (ValueReader)CreateInBulk(type, element, list ? BulkCollection.List : BulkCollection.Array, new BulkReaders(this));
    }

    private struct BulkReaders : IBulkCreator
    {
      private readonly Serializer _serializer;

      internal BulkReaders(Serializer serializer)
      {
        _serializer = serializer;
      }

      public object Array<T>() where T : unmanaged
      {
        return new NumberArrayReader<T>(_serializer.Reader<T>(), _serializer.DecimalCode(typeof(T)));
      }

      public object List<T>() where T : unmanaged
      {
#if NET8_0_OR_GREATER
        return new NumberListReader<T>(_serializer.Reader<T>(), _serializer.DecimalCode(typeof(T)));
#else
        return null;
#endif
      }

      public object Set<T>() where T : unmanaged
      {
        return new NumberSetReader<T>(_serializer.Reader<T>(), _serializer.DecimalCode(typeof(T)));
      }
    }

    /// <returns>The reader of a generic interface of a collection (IList&lt;T&gt;, IEnumerable&lt;T&gt;, IDictionary&lt;TKey, TValue&gt;...) whose concrete type has a typed reader (see <see cref="CollectionInterfaceReader{T}"/>), otherwise null</returns>
    private ValueReader InterfaceReader(Type type)
    {
      if (!type.IsInterface || !type.IsGenericType || type.ContainsGenericParameters)
        return null;
      if (!(Generated(type) is IGeneratedCollectionInterface)) // the source generator looked at it (its elements may have no other instantiations, see CollectionInfo)
      {
        if (!CanBindDelegates)
          return null;
        CollectionInfo info = CollectionInfo.Get(type);
        if (info.Attribute != null || info.ConcreteType == type || !(Reader(info.ConcreteType) is ICollectionContent))
          return null;
      }
      return (ValueReader)InstantiateCollection(type, typeof(CollectionInterfaceReader<>), new[] { type }, this);
    }

    /// <returns>The reader of another generic collection type that Core fills from an array of its elements (<see cref="CollectionReader{TCollection, TItem}"/>), null for others</returns>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only when the runtime supports dynamic code (CanBindDelegates).")]
    private ValueReader OtherCollectionReader(Type type)
    {
      if (!CanBindDelegates || PreserveReferences || type.IsArray || type.IsInterface || type.IsAbstract || type.ContainsGenericParameters)
        return null;
      CollectionInfo info = CollectionInfo.Get(type);
      if (info.Attribute != null || info.IsDictionary || info.ConcreteType != type || info.ElementType is null
        || !typeof(IEnumerable<>).MakeGenericType(info.ElementType).IsAssignableFrom(type))
        return null;
      return (ValueReader)CreateInstantiation(typeof(CollectionReader<,>), new[] { type, info.ElementType }, new object[] { this });
    }

    /// <returns>The reader of another dictionary type that Core fills from the entries of a map (<see cref="PairsReader{TCollection, TKey, TValue}"/>), null for others</returns>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only when the runtime supports dynamic code (CanBindDelegates).")]
    private ValueReader OtherDictionaryReader(Type type)
    {
      if (!CanBindDelegates || PreserveReferences || type.IsArray || type.IsInterface || type.IsAbstract || type.IsValueType || type.ContainsGenericParameters)
        return null;
      CollectionInfo info = CollectionInfo.Get(type);
      if (info.Attribute != null || !info.IsDictionary || info.PairKey is null || info.ConcreteType != type
        || !(info.FillsPairs || info.AddMethod != null || typeof(System.Collections.IDictionary).IsAssignableFrom(type)))
        return null;
      return (ValueReader)CreateInstantiation(typeof(PairsReader<,,>), new[] { type, info.KeyType, info.ValueType }, new object[] { this });
    }

    /// <returns>The reader of a custom extension of the type (an LsExtension&lt;T&gt; of the type), null for others</returns>
    private ValueReader ExtensionReader(Type type, LsExtension extension)
    {
      if (Generated(type) is IGeneratedInstantiations generated)
        return (ValueReader)generated.Instantiate(typeof(ExtensionReader<>), extension);

      if (!IsTypedExtension(extension, type))
        return null;
      return (ValueReader)CreateInstantiation(typeof(ExtensionReader<>), new[] { type }, new object[] { extension }); // null without code for it (NativeAOT): read boxed
    }

    /// <summary>
    /// The extension derives from LsExtension&lt;T&gt; of the type: its base classes compared by their definition, no MakeGenericType (NativeAOT may have no LsExtension of a value type).
    /// </summary>
    private static bool IsTypedExtension(LsExtension extension, Type type)
    {
      for (Type t = extension.GetType(); t != null; t = t.BaseType)
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(LsExtension<>))
          return t.GenericTypeArguments[0] == type;
      return false;
    }

    /// <summary>
    /// Creates the reader of a converter with its type arguments (see ConverterHandlers).
    /// </summary>
    private sealed class ConverterReaders : IMsgPackConverterVisitor<object>
    {
      private readonly Serializer _serializer;

      internal ConverterReaders(Serializer serializer)
      {
        _serializer = serializer;
      }

      public object Surrogate<T, TSurrogate>(MsgPackConverter<T, TSurrogate> converter)
      {
        return new ConverterReader<T, TSurrogate>(_serializer, converter);
      }

      public object Binary<T>(MsgPackBinaryConverter<T> converter)
      {
        return new BinaryConverterReader<T>(_serializer, converter);
      }
    }

    /// <returns>null when the type is not an object with properties that can be created</returns>
    internal ReadPlan GetReadPlan(Type type)
    {
      if (type == typeof(object)) // a map or array without a type id stays plain (Core: ValueConverter.ConvertMap keeps the raw map), never an instance of object
        return null;
      if (_readPlans.TryGetValue(type, out ReadPlan plan))
        return plan;

      TypeKind kind = GetTypeInfo(type).Kind;
      if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || (kind != TypeKind.Complex && kind != TypeKind.Extended) || (type.IsValueType && !StructPlans(type)))
        return null;

      // The properties LsMsgPack sets: names mode FullPropertyInfo.GetSerializedProps (their ids are the names without custom resolvers), the schema binds the same ones by name
      return _readPlans.GetOrAdd(type, t => new ReadPlan(this, t, FullPropertyInfo.GetSerializedProps(t, Options), Generated(t)?.Creator));
    }

    /// <param name="orAssignedTo">Return <paramref name="assignedTo"/> when the name is not found, otherwise null</param>
    internal Type ResolveTypeName(string name, Type assignedTo, bool orAssignedTo = true)
    {
      return TypeResolver.ResolveInternal(name, assignedTo, Options._typeResolvers, orAssignedTo);
    }

    private BoundSchema BoundFor(SchemaSession session, bool shared)
    {
      if (!shared)
        return new BoundSchema(session.Resolver, false);
      return Shared(session).Bound;
    }

    /// <param name="session">Frozen</param>
    private SessionShared Shared(SchemaSession session)
    {
      SessionShared last = _lastSession;
      if (last != null && ReferenceEquals(last.Session, session))
        return last;
      return SharedOfAnother(session);
    }

    /// <summary>
    /// <see cref="Shared"/> of another session than the last one: the recent ones first, then the table.
    /// </summary>
    private SessionShared SharedOfAnother(SchemaSession session)
    {
      SessionShared previous = _lastSession;
      RecentReads recent = _recent;
      if (recent != null)
      {
        SessionShared[] sessions = recent.Sessions;
        for (int t = sessions.Length - 1; t >= 0; t--) // the order does not matter, each session is in it once (or twice after a race, the same answer)
        {
          SessionShared candidate = sessions[t]; // read once: another thread may replace it
          if (candidate != null && ReferenceEquals(candidate.Session, session))
          {
            _lastSession = candidate;
            return candidate;
          }
        }
      }
      SessionShared shared = _sessions.GetValue(session, s => new SessionShared(s));
      if (previous != null) // another session than the one so far: from now on they are kept
      {
        if (recent is null)
        {
          recent = Recent();
          recent.AddSession(previous);
        }
        recent.AddSession(shared);
      }
      _lastSession = shared;
      return shared;
    }

    /// <summary>
    /// Caches the assemblies the names in the data may refer to (once per root type when there are several, see <see cref="RecentReads.Roots"/>), and makes it <see cref="_cachedRoot"/>.
    /// </summary>
    private void CacheRoot(Type type)
    {
      Type previous = _cachedRoot;
      RecentReads recent = _recent;
      if (recent != null)
      {
        RootEntry[] roots = recent.Roots;
        for (int t = roots.Length - 1; t >= 0; t--)
        {
          if (roots[t].Type == type)
          {
            _cachedRoot = type;
            return;
          }
        }
        if (recent.Others.ContainsKey(type)) // its assemblies are cached
        {
          _cachedRoot = type;
          return;
        }
      }
      // The names in the data (type ids, the schema) are resolved in the cached assemblies: the ones of the types the root type reaches (the T of List<T>...)
      TypeResolver.CacheAssembly(type.Assembly, type.Name); // as MsgPackSerializer.CacheAssemblyTypes(type)
      TypeResolver.CacheReachableAssemblies(type);
      if (previous != null) // a second root type: from now on the root types are kept
      {
        if (recent is null)
        {
          recent = Recent();
          recent.AddRoot(previous); // its assemblies are cached
        }
        recent.AddRoot(type);
      }
      _cachedRoot = type;
    }

    /// <summary>
    /// The reader of the root type, kept in its <see cref="RootEntry"/> when it has one.
    /// </summary>
    private ValueReader RootReader(Type type)
    {
      RecentReads recent = _recent;
      if (recent != null)
      {
        RootEntry[] roots = recent.Roots;
        for (int t = roots.Length - 1; t >= 0; t--)
        {
          RootEntry entry = roots[t];
          if (entry.Type == type)
            return entry.Reader ?? (entry.Reader = Reader(type)); // a race makes it twice: Reader returns the same one
        }
        if (recent.Others.TryGetValue(type, out RootEntry other))
          return other.Reader ?? (other.Reader = Reader(type));
      }
      return Reader(type); // one root type, or a type of a body (the stream reader)
    }

    #endregion

    #region Deserialize

    /// <summary>
    /// Reads one value (names mode) or one payload (indexed schema: the schema or a reference to it, then the value).
    /// </summary>
    /// <param name="end">The end of the data in the buffer</param>
    /// <param name="consumed">The position after the payload</param>
    /// <param name="differences">Collects the differences between the data and the classes, null when not asked for</param>
    /// <param name="intoSlot">A struct root value of <paramref name="type"/> is read into the context's slot, which is returned instead of a box (<see cref="ReadContext.IntoSlot"/>, read it with <see cref="RootSlot{T}.Take"/>: the paths that collect the differences or errors return the boxed value)</param>
    /// <param name="referencePrefix">A value of a head's body without the indexed schema (the stream reader): see <see cref="ReadContext.ReferencePrefix"/></param>
    /// <param name="into">An existing object the root value is read into (Populate, see <see cref="ReadContext.Into"/>), null to create it</param>
    internal object Deserialize(Type type, byte[] buffer, int offset, int end, out int consumed, ReadDifferences differences = null, bool intoSlot = false, Func<int[]> referencePrefix = null, object into = null)
    {
      if (differences is null && Options._readErrors != ReadErrorHandling.FailFast) // the errors are skipped and collected (with the differences)
        return DeserializeCollecting(type, buffer, offset, end, out consumed, intoSlot, referencePrefix, into);

      if (type != _cachedRoot)
        CacheRoot(type);

      ReadContext c = RentContext(buffer, offset, end);
      try
      {
        if (differences != null) // the fields of a reused context mostly keep their values: a comparison is cheaper than storing a reference (a write barrier)
          c.Differences = differences;
        c.IntoSlot = intoSlot;
        c.Unusual = SlowObjects || differences != null;
        if (referencePrefix != null)
          c.ReferencePrefix = referencePrefix;
        if (into != null)
          c.Into = into;
        object result;
        if (!Options._useIndexedSchema)
        {
          if (!ReferenceEquals(c.SlowSettings, Options))
            c.SlowSettings = Options;
          if (Options._compression != null && StartsCompressed(c.R))
            Decompress(c);
          result = ReadRoot(c, type);
        }
        else
          result = DeserializeWithSchema(c, type);

        consumed = c.Decompressed is null ? c.R.Pos : c.CompressedEnd;
        return result;
      }
      catch (ReadAgainPreserving) // the data has references and the options do not preserve them: read again with the option (a handler here costs nothing on the usual path)
      {
      }
      finally
      {
        ReturnContext(c);
      }
      differences?.Reset();
      return PreservingReader().Deserialize(type, buffer, offset, end, out consumed, differences, intoSlot, referencePrefix, into);
    }

    private Serializer _preservingReader;

    /// <summary>
    /// Reads data with references when the options leave <see cref="ReferenceHandling.Preserve"/> off (maintainer's decision 2026-11-19, gaps checklist item 9): the same options with it,
    /// so each value a reference points to is read once and shared. Reading each reference again as a copy grew exponentially (1.2 KB of data made 1.4 GB). Made at the first such call.
    /// <para>The objects of the first attempt are dropped: they had their OnDeserializing callbacks, as a read repeated after a schema grew.</para>
    /// </summary>
    private Serializer PreservingReader()
    {
      Serializer reader = _preservingReader;
      if (reader != null)
        return reader;
      LsMsgPackOptions options = Options.Clone();
      options._referenceHandling = ReferenceHandling.Preserve;
      try
      {
        reader = new Serializer(options) { SlowObjects = SlowObjects, CompileReadersAfter = CompileReadersAfter };
      }
      catch (InvalidOperationException ex) // a converter or custom extension of type code 5, which reads references as its values without the option
      {
        throw new MsgPackException($"The data has references (extension type {MsgPackOptions.ReferenceExtensionType}, written with ReferenceHandling.Preserve), which these options cannot read: {ex.Message}");
      }
      return Interlocked.CompareExchange(ref _preservingReader, reader, null) ?? reader;
    }

    // The lambdas are in methods of their own: a lambda that captures parameters allocates its closure where the method starts, on every call
    private object DeserializeCollecting(Type type, byte[] buffer, int offset, int end, out int consumed, bool intoSlot = false, Func<int[]> referencePrefix = null, object into = null)
    {
      ReadDifferences found = new ReadDifferences(Options);
      int used = 0;
      object read = ReadDifferences.Collect(found, () => Deserialize(type, buffer, offset, end, out used, found, intoSlot, referencePrefix, into));
      consumed = used;
      return read;
    }

    [ThreadStatic]
    private static ReadContext _threadContext;

    /// <summary>
    /// The context and reader of the calls on one thread, reused (taken while a call runs, so a nested call gets its own): two allocations less per call.
    /// </summary>
    private ReadContext RentContext(byte[] buffer, int offset, int end)
    {
      ReadContext c = _threadContext;
      if (c is null || !ReferenceEquals(c.Serializer, this))
      {
        c = new ReadContext(this) { R = new MsgPackReader(buffer, offset, end, Options) };
        c.R.Context = c;
        return c;
      }
      _threadContext = null;
      c.R.Reset(buffer, offset, end);
      c.Depth = 0;
      c.Schema = false;
      return c;
    }

    private static void ReturnContext(ReadContext c)
    {
      c.R.Release(); // the data is not kept alive
      if (c.Decompressed != null)
        ReturnDecompressed(c);
      if (c.References != null)
      {
        if (ReferenceEquals(c.References, c.PreservingReferences))
          c.References.Release();
        c.References = null;
      }
      if (c.ReferencePrefix != null)
        c.ReferencePrefix = null;
      if (c.Differences != null)
        c.EndDifferences();
      if (c.Into != null)
      {
        c.Into = null;
        c.IntoAt = -1;
      }
      _threadContext = c; // Bound and SlowSettings are kept: usually the next call has the same
    }

    /// <summary>
    /// The decompressed body goes back to the pool (apart, so ReturnContext stays small without compression).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReturnDecompressed(ReadContext c)
    {
      new Span<byte>(c.Decompressed, 0, c.DecompressedLength).Clear(); // a pooled array is shared with other code
      System.Buffers.ArrayPool<byte>.Shared.Return(c.Decompressed);
      c.Decompressed = null;
    }

    private object DeserializeWithSchema(ReadContext c, Type type)
    {
      MsgPackReader r = c.R;
      if (!ReadSchema(r, out SchemaStore.Entry schema, out MsgPackOptions lengthSettings, out SchemaStore.SessionState state, out byte[] raw, out SchemaHit hit))
        return null; // null is serialized without a schema
      if (Options._compression != null && StartsCompressed(r))
        Decompress(c);
      return ReadWithSchema(c, type, type, c.R.Pos, schema, lengthSettings, state, raw, hit);
    }

    private static bool StartsCompressed(MsgPackReader r)
    {
      return CompressedBody.StartsCompressed(new ReadOnlySpan<byte>(r.Buf, r.Pos, r.End - r.Pos));
    }

    /// <summary>
    /// The body at the reader's position is compressed (<see cref="MsgPackOptions.Compression"/>): from here on the reader reads the decompressed body (a rented array, returned by <see cref="ReturnContext"/>).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Decompress(ReadContext c)
    {
      MsgPackReader r = c.R;
      byte[] body = CompressedBody.Read(Options._compression, new ReadOnlySpan<byte>(r.Buf, r.Pos, r.End - r.Pos), Options, out int length, out int compressed);
      c.Decompressed = body;
      c.DecompressedLength = length;
      c.CompressedEnd = r.Pos + compressed;
      r.Reset(body, 0, length);
    }

    /// <summary>
    /// Reads the schema at the start of a payload (inline or a reference) and finds it in the store.
    /// </summary>
    /// <param name="schema">null when it is not cached (the store is full, or does not cache inline schemas): then <paramref name="raw"/> has its bytes</param>
    /// <param name="state">The reader sessions of the schema when it is a known one, otherwise null</param>
    /// <param name="hit">The known schema (<see cref="SchemaHit"/>), otherwise null</param>
    /// <returns>false for nil (null is serialized without a schema), the reader is then after it</returns>
    private bool ReadSchema(MsgPackReader r, out SchemaStore.Entry schema, out MsgPackOptions lengthSettings, out SchemaStore.SessionState state, out byte[] raw, out SchemaHit hit)
    {
      if (r.Pos >= r.End)
        throw MsgPackReader.EndOfData();

      state = null;
      raw = null;
      hit = null;
      int first = r.Buf[r.Pos];
      if (first == 0xC0)
      {
        r.Pos++;
        schema = null;
        lengthSettings = null;
        return false;
      }

      SchemaHit last = _lastSchema;
      if (last is null || !StartsWith(r, last.Bytes)) // a MsgPack value ends where its bytes end, so a payload starting with the bytes of the schema (or reference) starts with that schema
      {
        last = RecentSchema(r);
        if (last != null)
          _lastSchema = last; // the next payload of its root type finds it first
      }
      if (last != null)
      {
        hit = last;
        schema = last.Entry;
        lengthSettings = last.LengthSettings;
        state = last.State;
        r.Pos += last.Bytes.Length;
      }
      else if (first == (int)MsgPackTypeId.MpFExt16)
      {
        if (r.End - r.Pos < SchemaStore.ReferenceLength)
          throw MsgPackReader.EndOfData();
        sbyte extType = unchecked((sbyte)r.Buf[r.Pos + 1]);
        if (extType != SchemaStore.ReferenceExtensionType)
          throw new MsgPackException($"Expected the data to start with an indexed schema (a map) or a reference to one (extension type {SchemaStore.ReferenceExtensionType}) but found extension type {extType}. Was it serialized with MsgPackSettings.UseIndexedSchema = false?", 0, MsgPackTypeId.MpFExt16);

        SchemaHit known = KnownSchema(r.Buf, r.Pos, SchemaStore.ReferenceLength);
        if (known != null)
        {
          hit = known;
          schema = known.Entry;
          lengthSettings = known.LengthSettings;
          state = known.State;
          r.Pos += SchemaStore.ReferenceLength;
          return true;
        }

        SchemaId id = new SchemaId(Slice(r.Buf, r.Pos + 2, SchemaId.Length));
        SchemaStore store = Options._schemaStore;
        if (store is null)
          throw new MsgPackException($"The data refers to the cached schema {id}, reading it needs a MsgPackSettings.SchemaStore that holds the schema.");
        schema = store.GetById(id);
        lengthSettings = null;
        raw = Slice(r.Buf, r.Pos, SchemaStore.ReferenceLength);
        r.Pos += SchemaStore.ReferenceLength;
      }
      else if (IsMap(first))
      {
        int start = r.Pos;
        r.Skip(0);
        SchemaHit known = KnownSchema(r.Buf, start, r.Pos - start);
        if (known != null)
        {
          hit = known;
          schema = known.Entry;
          lengthSettings = known.LengthSettings;
          state = known.State;
          return true;
        }
        raw = Slice(r.Buf, start, r.Pos - start);
        SchemaStore store = Options._schemaStore ?? _inlineStore;
        if (!store.TryGetInline(raw, out schema))
        {
          IndexedSchemaTypeResolver.FromBytes(raw, Options, Options); // refuses anything that is not a schema (as LsMsgPack when it reads it), before it is cached
          schema = store.GetInline(raw);
        }
        lengthSettings = Options;
      }
      else
        throw NotASchema(first);
      return true;
    }

    /// <summary>
    /// Reads the body (from <paramref name="bodyStart"/>) with the schema <see cref="ReadSchema"/> found.
    /// </summary>
    /// <param name="root">The root type of the payload, which pairs the reader's classes with the schema's (<see cref="SerializationRules.BindReaderTypes"/>)</param>
    /// <param name="type">The type of the value that is read: <paramref name="root"/>, or a part of its body (<see cref="Streaming.MsgPackStreamReader.ReadHead{TRoot}"/>)</param>
    /// <param name="hit">The known schema, null when it is not (yet) one: it keeps the shared session it was read with last</param>
    private object ReadWithSchema(ReadContext c, Type root, Type type, int bodyStart, SchemaStore.Entry schema, MsgPackOptions lengthSettings, SchemaStore.SessionState state, byte[] raw, SchemaHit hit)
    {
      if (schema is null) // not cached (the store is full, or does not cache inline schemas): a session for this call
      {
        SchemaSession session = new SchemaSession(IndexedSchemaTypeResolver.FromBytes(raw, Options, Options), Options);
        return ReadBody(c, root, type, bodyStart, session, session.Apply(Options), false);
      }

      if (state is null)
      {
        state = schema.GetReader(Options);
        if (raw != null)
          AddSchemaHit(hit = new SchemaHit(raw, schema, lengthSettings, state)); // the store keeps its entries
      }

      // The published session, shared with other calls (as Entry.RunReader, without its allocations)
      SchemaSession current = state.Current;
      if (current != null)
      {
        SessionShared reading = hit?.Shared;
        if (reading is null || !ReferenceEquals(reading.Session, current)) // the first read of the schema, or its session grew (threads may set it in turn: each checks it)
        {
          reading = Shared(current);
          if (hit != null)
            hit.Shared = reading;
        }
        try
        {
          return ReadBody(c, root, type, bodyStart, reading.Bound, reading.Settings(Options));
        }
        catch (SchemaGrowthException) // something the session does not have yet, RunReader grows it
        {
        }
      }

      return ReadGrowing(c, root, type, bodyStart, schema, lengthSettings);
    }

    private object ReadGrowing(ReadContext c, Type root, Type type, int bodyStart, SchemaStore.Entry schema, MsgPackOptions lengthSettings)
    {
      return schema.RunReader(Options, lengthSettings, (session, sessionSettings) => ReadBody(c, root, type, bodyStart, session, sessionSettings, true));
    }

    private object ReadBody(ReadContext c, Type root, Type type, int bodyStart, SchemaSession session, MsgPackOptions sessionSettings, bool shared)
    {
      return ReadBody(c, root, type, bodyStart, BoundFor(session, shared && session.IsFrozen), sessionSettings);
    }

    private object ReadBody(ReadContext c, Type root, Type type, int bodyStart, BoundSchema bound, MsgPackOptions sessionSettings)
    {
      SerializationRules.BindReaderTypes(root, sessionSettings); // once per root type and schema
      c.R.Pos = bodyStart;
      c.R.ResetCounts(); // the limits count the body (read again when the shared session has to grow), not the schema
      c.Depth = 0;
      c.Schema = true;
      if (!ReferenceEquals(c.SlowSettings, sessionSettings))
        c.SlowSettings = sessionSettings;
      if (!ReferenceEquals(c.Bound, bound))
        c.Bound = bound;
      if (c.Differences != null)
        c.Differences.Reset(); // the body is read again when the shared session has to grow (ConversionSettings copies the new session's settings)
      return ReadRoot(c, type);
    }

    #endregion

    #region Schema heads (the stream reader)

    /// <summary>
    /// A schema read on its own, for the body that follows it in a stream (<see cref="Streaming.MsgPackStreamReader.ReadHead{TRoot}"/>): read in parts, each with this schema.
    /// </summary>
    internal sealed class SchemaHead
    {
      internal readonly SchemaStore.Entry Schema;
      internal readonly MsgPackOptions LengthSettings;
      internal readonly SchemaStore.SessionState State;
      internal readonly byte[] Raw;

      internal SchemaHead(SchemaStore.Entry schema, MsgPackOptions lengthSettings, SchemaStore.SessionState state, byte[] raw)
      {
        Schema = schema;
        LengthSettings = lengthSettings;
        State = state;
        Raw = raw;
      }
    }

    /// <summary>
    /// Reads the schema (inline or a reference) that starts at <paramref name="offset"/>.
    /// </summary>
    /// <param name="end">Where the schema ends</param>
    internal SchemaHead ReadHead(byte[] buffer, int offset, int end)
    {
      ReadContext c = RentContext(buffer, offset, end);
      try
      {
        MsgPackReader r = c.R;
        if (!ReadSchema(r, out SchemaStore.Entry schema, out MsgPackOptions lengthSettings, out SchemaStore.SessionState state, out byte[] raw, out SchemaHit unused))
          throw NotASchema(0xC0);
        if (r.Pos != end)
          throw new MsgPackException("The schema is followed by more data.", r.Pos, MsgPackTypeId.NeverUsed);
        if (schema != null && state is null)
          state = schema.GetReader(Options);
        return new SchemaHead(schema, lengthSettings, state, raw);
      }
      finally
      {
        ReturnContext(c);
      }
    }

    /// <summary>
    /// Reads one value of the body of a schema head, as <see cref="Deserialize"/> reads a payload of <paramref name="root"/> with that schema.
    /// </summary>
    /// <param name="root">The root type of the head</param>
    /// <param name="type">The declared type of the value</param>
    /// <param name="intoSlot">As in <see cref="Deserialize"/></param>
    /// <param name="referencePrefix">Where the value is in the root value of the body (the start of its reference paths, <see cref="ReadContext.ReferencePrefix"/>), null at the root</param>
    /// <param name="into">As in <see cref="Deserialize"/> (the stream reader's PopulateObject)</param>
    internal object DeserializeBody(SchemaHead head, Type root, Type type, byte[] buffer, int offset, int end, out int consumed, ReadDifferences differences = null, bool intoSlot = false, Func<int[]> referencePrefix = null, object into = null)
    {
      if (differences is null && Options._readErrors != ReadErrorHandling.FailFast) // the errors are skipped and collected (with the differences)
        return DeserializeBodyCollecting(head, root, type, buffer, offset, end, out consumed, referencePrefix, into);

      if (root != _cachedRoot)
        CacheRoot(root);
      if (type != root && type != _cachedBodyType) // the elements of a body are usually of one type: one lookup
      {
        TypeResolver.CacheAssembly(type.Assembly, type.Name);
        _cachedBodyType = type;
      }

      ReadContext c = RentContext(buffer, offset, end);
      try
      {
        if (differences != null)
          c.Differences = differences;
        c.IntoSlot = intoSlot;
        c.Unusual = SlowObjects || differences != null;
        c.ReferencePrefix = referencePrefix;
        if (into != null)
          c.Into = into;
        object result = ReadWithSchema(c, root, type, offset, head.Schema, head.LengthSettings, head.State, head.Raw, null);
        consumed = c.R.Pos;
        return result;
      }
      catch (ReadAgainPreserving) // as in Deserialize
      {
      }
      finally
      {
        ReturnContext(c);
      }
      differences?.Reset();
      return PreservingReader().DeserializeBody(head, root, type, buffer, offset, end, out consumed, differences, intoSlot, referencePrefix, into);
    }

    private object DeserializeBodyCollecting(SchemaHead head, Type root, Type type, byte[] buffer, int offset, int end, out int consumed, Func<int[]> referencePrefix, object into)
    {
      ReadDifferences found = new ReadDifferences(Options);
      int used = 0;
      object read = ReadDifferences.Collect(found, () => DeserializeBody(head, root, type, buffer, offset, end, out used, found, false, referencePrefix, into));
      consumed = used;
      return read;
    }

    #endregion

    #region Helpers

    private object ReadRoot(ReadContext c, Type type)
    {
      c.BodyStart = c.R.Pos;
      if (c.Into != null) // Populate: the root value is read into it
        c.IntoAt = c.R.Pos;
      if (PreserveReferences)
        c.References = c.PreservingReferences?.Restart(c.R.Pos) ?? (c.PreservingReferences = new ByteReferences(c, c.R.Pos, true));
      try
      {
        if (c.RootType != type) // calls on a thread usually read the same root type: no lookup (the hash of a Type is a call into the runtime)
        {
          c.RootReader = RootReader(type);
          c.RootType = type;
        }
        if (c.IntoSlot) // a struct of a typed call, read without a box
          return c.RootReader.ReadIntoSlot(c);
        return c.RootReader.ReadBoxed(c, null);
      }
      catch (RootValue unconverted)
      {
        return unconverted.Value;
      }
    }

    /// <returns>The recent schema the payload starts with (see <see cref="RecentReads.Schemas"/>), null when it starts with none of them</returns>
    private SchemaHit RecentSchema(MsgPackReader r)
    {
      RecentReads recent = _recent;
      if (recent is null)
        return null;
      SchemaHit[] schemas = recent.Schemas;
      for (int t = schemas.Length - 1; t >= 0; t--) // the order does not matter: at most one starts the payload (the last one is compared again, it failed already)
      {
        SchemaHit hit = schemas[t]; // read once: another thread may replace it
        if (hit != null && StartsWith(r, hit.Bytes))
          return hit;
      }
      return null;
    }

    /// <summary>
    /// A schema the store keeps, read for the first time: known from now on (<see cref="_knownSchemas"/>), and the last one.
    /// </summary>
    private void AddSchemaHit(SchemaHit hit)
    {
      SchemaHit previous = _lastSchema;
      if (previous != null) // a second schema: from now on they are known (the first one too)
      {
        _knownSchemas.TryAdd(SchemaKey.Of(previous.Bytes, 0, previous.Bytes.Length), previous);
        _knownSchemas.TryAdd(SchemaKey.Of(hit.Bytes, 0, hit.Bytes.Length), hit); // a hash that is taken already: this one is looked up in the store each time
      }
      AddRecentSchema(hit);
    }

    /// <summary>
    /// Makes <paramref name="hit"/> the last schema, and keeps it in the recent ones once there are two.
    /// </summary>
    private void AddRecentSchema(SchemaHit hit)
    {
      SchemaHit previous = _lastSchema;
      _lastSchema = hit;
      RecentReads recent = _recent;
      if (recent is null)
      {
        if (previous is null) // the first schema
          return;
        recent = Recent(); // another schema than the one so far: from now on they are kept
        recent.AddSchema(previous);
      }
      recent.AddSchema(hit);
    }

    private static bool StartsWith(MsgPackReader r, byte[] bytes)
    {
      return r.End - r.Pos >= bytes.Length && new ReadOnlySpan<byte>(r.Buf, r.Pos, bytes.Length).SequenceEqual(bytes);
    }

    private static bool IsMap(int first)
    {
      return (first & 0xF0) == 0x80 || first == 0xDE || first == 0xDF;
    }

    private static MsgPackException NotASchema(int first)
    {
      MsgPackTypeId typeId;
      if (first <= 0x7F) typeId = MsgPackTypeId.MpBytePart;
      else if (first >= 0xE0) typeId = MsgPackTypeId.MpSBytePart;
      else if (first <= 0x9F) typeId = MsgPackTypeId.MpArray4;
      else if (first <= 0xBF) typeId = MsgPackTypeId.MpStr5;
      else typeId = (MsgPackTypeId)first;
      return new MsgPackException($"Expected the data to start with an indexed schema (a map) but found {typeId}. Was it serialized with MsgPackSettings.UseIndexedSchema = false?", 0, typeId);
    }

    private static byte[] Slice(byte[] buffer, int offset, int count)
    {
      byte[] result = new byte[count];
      Buffer.BlockCopy(buffer, offset, result, 0, count);
      return result;
    }

    #endregion
  }
}
