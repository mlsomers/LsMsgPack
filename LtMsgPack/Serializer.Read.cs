using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using LtMsgPack.Extensions;
using LtMsgPack.IO;
using LtMsgPack.Reading;
using LtMsgPack.Writing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LtMsgPack
{
  internal sealed partial class Serializer
  {
    private readonly ConcurrentDictionary<Type, ValueReader> _readers = new ConcurrentDictionary<Type, ValueReader>();
    private readonly ConcurrentDictionary<Type, ReadPlan> _readPlans = new ConcurrentDictionary<Type, ReadPlan>();
    private readonly ConditionalWeakTable<SchemaSession, BoundSchema> _bound = new ConditionalWeakTable<SchemaSession, BoundSchema>();

    /// <summary>
    /// Objects are read as LsMsgPack does (not by the typed readers): custom property id resolvers decide the keys, custom type resolvers may pick a type by the properties.
    /// </summary>
    internal bool SlowObjects;

    /// <summary>
    /// Caches the inline schemas that are read, when the options have no <see cref="SchemaStore"/>.
    /// </summary>
    private SchemaStore _inlineStore;

    internal bool CustomTypeResolvers { get { return _customTypeResolvers; } }

    private void InitReading()
    {
      SlowObjects = CustomPropertyIds || _customTypeResolvers;
      _inlineStore = new SchemaStore();
    }

    #region Readers and plans

    internal ValueReader<T> Reader<T>()
    {
      return (ValueReader<T>)Reader(typeof(T));
    }

    internal ValueReader Reader(Type type)
    {
      if (_readers.TryGetValue(type, out ValueReader reader))
        return reader;
      return _readers.GetOrAdd(type, t => CreateReader(t));
    }

    private ValueReader CreateReader(Type type)
    {
      Type underlying = Nullable.GetUnderlyingType(type);
      if (underlying != null)
        return (ValueReader)Activator.CreateInstance(typeof(NullableReader<>).MakeGenericType(underlying), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { Reader(underlying) }, null);

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
        case TypeKind.DecimalString: return new DecimalStringReader();
        case TypeKind.DateTime: return new DateTimeReader();
        case TypeKind.DateTimeOffset: return new DateTimeOffsetReader();
        case TypeKind.DateTimeOffsetArray: return new DateTimeOffsetArrayReader();
        case TypeKind.TimeSpan: return new TimeSpanReader();
        case TypeKind.Bin:
          if (type == typeof(byte[]))
            return new BinReader();
          break;
        case TypeKind.Extension:
          Type typedExtension = typeof(LtExtension<>).MakeGenericType(type);
          if (typedExtension.IsInstanceOfType(info.Extension))
            return (ValueReader)Activator.CreateInstance(typeof(ExtensionReader<>).MakeGenericType(type), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { info.Extension }, null);
          break;
        case TypeKind.Enum:
          return (ValueReader)Activator.CreateInstance(typeof(EnumReader<>).MakeGenericType(type), true);
        case TypeKind.Array:
          if (type.IsArray && type.GetArrayRank() == 1 && type == type.GetElementType().MakeArrayType())
            return (ValueReader)Activator.CreateInstance(typeof(ArrayReader<>).MakeGenericType(type.GetElementType()), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { this }, null);
          if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && CollectionInfo.Get(type).Attribute is null)
            return (ValueReader)Activator.CreateInstance(typeof(ListReader<>).MakeGenericType(type.GenericTypeArguments[0]), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { this }, null);
          break;
        case TypeKind.Complex:
          if (!type.IsValueType && type != typeof(object) && !type.ContainsGenericParameters)
            return (ValueReader)Activator.CreateInstance(typeof(ObjectReader<>).MakeGenericType(type), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { this }, null);
          break;
      }
      return (ValueReader)Activator.CreateInstance(typeof(BoxedReader<>).MakeGenericType(type), true);
    }

    /// <returns>null when the type is not an object with properties that can be created</returns>
    internal ReadPlan GetReadPlan(Type type)
    {
      if (_readPlans.TryGetValue(type, out ReadPlan plan))
        return plan;

      if (type.IsAbstract || type.IsInterface || type.IsValueType || type.ContainsGenericParameters || GetTypeInfo(type).Kind != TypeKind.Complex)
        return null;

      // The properties LsMsgPack sets: names mode FullPropertyInfo.GetSerializedProps (their ids are the names without custom resolvers), the schema binds the same ones by name
      return _readPlans.GetOrAdd(type, t => new ReadPlan(this, t, FullPropertyInfo.GetSerializedProps(t, Options)));
    }

    internal Type ResolveTypeName(string name, Type assignedTo)
    {
      return TypeResolver.ResolveInternal(name, assignedTo, Options._typeResolvers);
    }

    private BoundSchema BoundFor(SchemaSession session, bool shared)
    {
      if (!shared)
        return new BoundSchema(session.Resolver);
      return _bound.GetValue(session, s => new BoundSchema(s.Resolver));
    }

    #endregion

    #region Deserialize

    /// <summary>
    /// Reads one value (names mode) or one payload (indexed schema: the schema or a reference to it, then the value).
    /// </summary>
    /// <param name="end">The end of the data in the buffer</param>
    /// <param name="consumed">The position after the payload</param>
    internal object Deserialize(Type type, byte[] buffer, int offset, int end, out int consumed)
    {
      ReadContext c = new ReadContext(this) { R = new MsgPackReader(buffer, offset, end, Options) };
      object result;
      if (!Options._useInexedSchema)
      {
        c.SlowSettings = Options;
        result = ReadRoot(c, type);
      }
      else
        result = DeserializeWithSchema(c, type);

      consumed = c.R.Pos;
      return result;
    }

    private object DeserializeWithSchema(ReadContext c, Type type)
    {
      TypeResolver.CacheAssembly(type.Assembly, type.Name); // as MsgPackSerializer.CacheAssemblyTypes(type)

      MsgPackReader r = c.R;
      if (r.Pos >= r.End)
        throw MsgPackReader.EndOfData();

      int first = r.Buf[r.Pos];
      if (first == 0xC0) // null is serialized without a schema
      {
        r.Pos++;
        return null;
      }

      SchemaStore.Entry schema;
      MsgPackOptions lengthSettings;
      byte[] raw = null;
      if (first == (int)MsgPackTypeId.MpFExt16)
      {
        if (r.End - r.Pos < SchemaStore.ReferenceLength)
          throw MsgPackReader.EndOfData();
        sbyte extType = unchecked((sbyte)r.Buf[r.Pos + 1]);
        if (extType != SchemaStore.ReferenceExtensionType)
          throw new MsgPackException($"Expected the data to start with an indexed schema (a map) or a reference to one (extension type {SchemaStore.ReferenceExtensionType}) but found extension type {extType}. Was it serialized with MsgPackSettings.UseInexedSchema = false?", 0, MsgPackTypeId.MpFExt16);

        SchemaId id = new SchemaId(Slice(r.Buf, r.Pos + 2, SchemaId.Length));
        SchemaStore store = Options._schemaStore;
        if (store is null)
          throw new MsgPackException($"The data refers to the cached schema {id}, reading it needs a MsgPackSettings.SchemaStore that holds the schema.");
        schema = store.GetById(id);
        lengthSettings = null;
        r.Pos += SchemaStore.ReferenceLength;
      }
      else if (IsMap(first))
      {
        int start = r.Pos;
        r.Skip(0);
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

      int bodyStart = r.Pos;
      if (schema is null) // not cached (the store is full, or does not cache inline schemas): a session for this call
      {
        SchemaSession session = new SchemaSession(IndexedSchemaTypeResolver.FromBytes(raw, Options, Options), Options);
        return ReadBody(c, type, bodyStart, session, session.Apply(Options), false);
      }

      return schema.RunReader(Options, lengthSettings, (session, sessionSettings) => ReadBody(c, type, bodyStart, session, sessionSettings, true));
    }

    private object ReadBody(ReadContext c, Type type, int bodyStart, SchemaSession session, MsgPackOptions sessionSettings, bool shared)
    {
      c.R.Pos = bodyStart;
      c.Depth = 0;
      c.Schema = true;
      c.SlowSettings = sessionSettings;
      c.Bound = BoundFor(session, shared && session.IsFrozen);
      return ReadRoot(c, type);
    }

    private object ReadRoot(ReadContext c, Type type)
    {
      try
      {
        return Reader(type).ReadBoxed(c, null);
      }
      catch (RootValue unconverted)
      {
        return unconverted.Value;
      }
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
      return new MsgPackException($"Expected the data to start with an indexed schema (a map) but found {typeId}. Was it serialized with MsgPackSettings.UseInexedSchema = false?", 0, typeId);
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
