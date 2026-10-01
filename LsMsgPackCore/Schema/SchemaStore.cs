using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LsMsgPack
{
  /// <summary>
  /// Keeps indexed schemas between calls, so they are built, read and resolved once instead of for every call. Thread-safe, share one store (e.g. per application).
  /// <para>Writing with <see cref="MsgPackSettings.WriteSchemaReference"/>: the data starts with a reference to the schema (<see cref="SchemaId"/>, 18 bytes) instead of the schema itself.
  /// The reader needs the schema in its own store: exchange the schemas (<see cref="GetSchema"/> / <see cref="Register"/>, or <see cref="Export"/> / <see cref="Import"/>) or let the reader fetch them (<see cref="SchemaProvider"/>).
  /// A schema grows when new types are written (e.g. another implementation of an interface), the grown schema gets a new id, the earlier ones stay valid.</para>
  /// <para>Reading: data with the schema inline (the default format) is recognized by the bytes of its schema, so each distinct schema is read and resolved once (<see cref="CacheInlineSchemas"/>).</para>
  /// </summary>
  public sealed class SchemaStore
  {
    /// <summary>
    /// The extension type of a schema reference: fixext16 (0xD8) with this type and the 16 bytes of the <see cref="SchemaId"/>.
    /// <para>Only meaningful as the first item of data written with the indexed schema, where the schema (a map) or nil is expected, so it cannot be mistaken for a custom extension.</para>
    /// </summary>
    public const sbyte ReferenceExtensionType = 2;

    /// <summary>
    /// The number of bytes of a schema reference.
    /// </summary>
    public const int ReferenceLength = 2 + SchemaId.Length;

    private readonly ConcurrentDictionary<SchemaId, Entry> _schemas = new ConcurrentDictionary<SchemaId, Entry>();
    private readonly ConcurrentDictionary<byte[], Entry> _inline = new ConcurrentDictionary<byte[], Entry>(ByteArrayComparer.Instance);
    private readonly ConcurrentDictionary<SessionKey, SessionState> _writers = new ConcurrentDictionary<SessionKey, SessionState>();
    private int _received; // schemas added by Register, Import, the provider and inline data (the ones MaxSchemas limits)

    /// <summary>
    /// The maximum number of schemas received from others (<see cref="Register"/>, <see cref="Import"/>, <see cref="SchemaProvider"/> and inline schemas that are cached), 1024 by default.
    /// <para>A limit, because a store that caches what it receives would otherwise keep growing when others send it endless variations. The schemas this process writes are not limited.</para>
    /// </summary>
    public int MaxSchemas { get; set; } = 1024;

    /// <summary>
    /// Cache the schemas of data read with the schema inline (default true). Once <see cref="MaxSchemas"/> is reached new ones are read without caching them.
    /// </summary>
    public bool CacheInlineSchemas { get; set; } = true;

    /// <summary>
    /// Called when data refers to a schema that is not in the store, returns the schema bytes (as returned by <see cref="GetSchema"/> of the writer's store) or null.
    /// <para>The returned bytes must have the requested id (it is checked). Called by the reading thread, which waits for it; without a provider a <see cref="MissingSchemaException"/> is thrown.</para>
    /// </summary>
    public Func<SchemaId, byte[]> SchemaProvider { get; set; }

    /// <summary>
    /// The number of schemas that can be referred to by their id (written, registered or imported).
    /// </summary>
    public int Count { get { return _schemas.Count; } }

    public bool Contains(SchemaId id)
    {
      return _schemas.ContainsKey(id);
    }

    public SchemaId[] GetSchemaIds()
    {
      List<SchemaId> ids = new List<SchemaId>(_schemas.Count);
      foreach (KeyValuePair<SchemaId, Entry> entry in _schemas)
        ids.Add(entry.Key);
      return ids.ToArray();
    }

    /// <returns>A copy of the schema bytes, null when the store does not have it</returns>
    public byte[] GetSchema(SchemaId id)
    {
      if (!_schemas.TryGetValue(id, out Entry entry))
        return null;
      return (byte[])entry.Bytes.Clone();
    }

    /// <summary>
    /// Adds the schema of a writer (e.g. received from it, see <see cref="GetSchema"/>). The id is computed from the bytes, so a schema cannot be registered under the id of another one.
    /// </summary>
    /// <returns>The id of the schema</returns>
    /// <exception cref="MsgPackException">When the bytes are not a schema, or the store holds <see cref="MaxSchemas"/> received schemas</exception>
    public SchemaId Register(byte[] schema)
    {
      if (schema is null)
        throw new ArgumentNullException(nameof(schema));

      SchemaId id = SchemaId.Compute(schema);
      if (_schemas.ContainsKey(id))
        return id;

      SchemaBytes.Parse(schema, null); // refuse anything that is not a schema (the types are resolved when it is used)
      if (!TryReserve())
        throw new MsgPackException($"The {nameof(SchemaStore)} already holds {nameof(MaxSchemas)} ({MaxSchemas}) received schemas.");

      if (!_schemas.TryAdd(id, new Entry((byte[])schema.Clone())))
        Interlocked.Decrement(ref _received); // another thread was first
      return id;
    }

    /// <summary>
    /// Writes all schemas that can be referred to (e.g. to preload the store of a reader, or to keep them between runs): a map of ids (bin) to schemas (bin).
    /// </summary>
    public void Export(Stream target)
    {
      List<KeyValuePair<SchemaId, Entry>> all = new List<KeyValuePair<SchemaId, Entry>>(_schemas);
      ByteWriter bytes = new ByteWriter();
      bytes.WriteMapHeader(all.Count, SchemaBytes.Canonical);
      for (int t = 0; t < all.Count; t++)
      {
        SchemaBytes.WriteBin(bytes, all[t].Key.ToByteArray());
        SchemaBytes.WriteBin(bytes, all[t].Value.Bytes);
      }
      bytes.CopyTo(target);
    }

    /// <summary>
    /// Registers the schemas written by <see cref="Export"/>. Their ids are computed again, the exported ones are not trusted.
    /// </summary>
    /// <returns>The number of schemas read</returns>
    public int Import(Stream source)
    {
      List<byte[]> schemas = SchemaBytes.ReadExport(source);
      for (int t = 0; t < schemas.Count; t++)
        Register(schemas[t]);
      return schemas.Count;
    }

    /// <summary>
    /// Writes the reference to a schema, as <see cref="MsgPackSerializer"/> does in place of the schema.
    /// </summary>
    public static byte[] WriteReference(SchemaId id)
    {
      byte[] reference = new byte[ReferenceLength];
      reference[0] = (byte)MsgPackTypeId.MpFExt16;
      reference[1] = unchecked((byte)ReferenceExtensionType);
      id.WriteTo(reference, 2);
      return reference;
    }

    /// <summary>
    /// The schema as a writer with these settings writes it inline (its lengths in the byte order of the settings), to send it in place of a reference (e.g. to a reader that does not have it).
    /// <para>The bytes are shared, do not change them.</para>
    /// </summary>
    /// <returns>null when the store does not have it</returns>
    internal byte[] GetInlineSchema(SchemaId id, MsgPackOptions settings)
    {
      if (!_schemas.TryGetValue(id, out Entry entry))
        return null;
      if (MsgPackOptions.SwapEndianChoice(settings, 2) == MsgPackOptions.SwapEndianChoice(SchemaBytes.Canonical, 2))
        return entry.Bytes;
      return entry.OtherByteOrder ?? (entry.OtherByteOrder = Repack(entry.Bytes, SchemaBytes.Canonical, settings));
    }

    /// <summary>
    /// Registers the schema at the start of data written with the schema inline (e.g. received from a writer that will refer to it later), as <see cref="Register"/>.
    /// </summary>
    /// <param name="settings">The byte order the data was written with</param>
    /// <returns>The id of the schema</returns>
    /// <exception cref="MsgPackException">When the data does not start with a schema, or the store holds <see cref="MaxSchemas"/> received schemas</exception>
    internal SchemaId RegisterInline(byte[] data, int offset, int count, MsgPackOptions settings)
    {
      if (count <= 0)
        throw new MsgPackException("Unexpected end of data.", 0, MsgPackTypeId.NeverUsed);
      int first = data[offset];
      if (!SchemaBytes.IsMap(first))
        throw SchemaBytes.NotASchema(first);

      MemoryStream stream = new MemoryStream(data, offset + 1, count - 1, false);
      byte[] schema = SchemaBytes.ReadRaw(stream, first, settings);
      if (MsgPackOptions.SwapEndianChoice(settings, 2) != MsgPackOptions.SwapEndianChoice(SchemaBytes.Canonical, 2))
        schema = Repack(schema, settings, SchemaBytes.Canonical);
      return Register(schema);
    }

    private static byte[] Repack(byte[] schema, MsgPackOptions from, MsgPackOptions to)
    {
      IndexedSchemaTypeResolver resolver = new IndexedSchemaTypeResolver() { ByTypeId = SchemaBytes.Parse(schema, from) };
      return resolver.Pack(to);
    }

    private bool TryReserve()
    {
      if (Interlocked.Increment(ref _received) <= MaxSchemas)
        return true;
      Interlocked.Decrement(ref _received);
      return false;
    }

    #region Sessions

    /// <summary>
    /// The session used to write values of the given (declared) root type, each root type has its own schema.
    /// </summary>
    internal SessionState GetWriter(Type root, MsgPackOptions settings)
    {
      SessionKey key = new SessionKey(root, settings);
      if (_writers.TryGetValue(key, out SessionState state))
        return state;
      return _writers.GetOrAdd(key, k => new SessionState(null));
    }

    /// <summary>
    /// Runs <paramref name="work"/> (writing a value) with the shared session of the root type.
    /// <para>When the work needs something the (frozen) session does not have yet, it throws <see cref="SchemaGrowthException"/> and is repeated, under the lock of the session, with a grown copy that is published in its place (a new schema id).</para>
    /// </summary>
    /// <param name="work">Gets the session and the settings to use (a copy of <paramref name="settings"/> using the session), may run twice</param>
    /// <param name="used">The session the result was made with (its <see cref="SchemaSession.Reference"/> refers to the schema)</param>
    internal TResult RunWriter<TSettings, TResult>(Type root, TSettings settings, Func<SchemaSession, TSettings, TResult> work, out SchemaSession used) where TSettings : MsgPackOptions
    {
      SessionState state = GetWriter(root, settings);
      SchemaSession session = state.Current;
      TResult result;
      if (session != null && TryRun(session, settings, work, out result))
      {
        used = session;
        return result;
      }

      lock (state)
      {
        SchemaSession current = state.Current;
        if (current != null && current != session && TryRun(current, settings, work, out result)) // grown by another thread in the meantime
        {
          used = current;
          return result;
        }

        SchemaSession grown = current?.Thaw(settings) ?? new SchemaSession(new IndexedSchemaTypeResolver(), settings);
        result = work(grown, grown.Apply(settings));
        PublishWriter(state, grown);
        used = grown;
        return result;
      }
    }

    /// <returns>false when the (frozen) session needs to grow</returns>
    private static bool TryRun<TSettings, TResult>(SchemaSession session, TSettings settings, Func<SchemaSession, TSettings, TResult> work, out TResult result) where TSettings : MsgPackOptions
    {
      try
      {
        result = work(session, session.Apply(settings));
        return true;
      }
      catch (SchemaGrowthException)
      {
        result = default(TResult);
        return false;
      }
    }

    /// <summary>
    /// Freezes and publishes a session that was extended while writing: its schema gets an id and can be referred to.
    /// </summary>
    internal void PublishWriter(SessionState state, SchemaSession session)
    {
      session.Freeze();
      byte[] schema = session.Resolver.Pack(SchemaBytes.Canonical); // the byte order of the MsgPack specification, whatever the EndianAction of the writer
      session.Id = SchemaId.Compute(schema);
      session.Reference = WriteReference(session.Id);
      _schemas.TryAdd(session.Id, new Entry(schema));
      state.Current = session;
    }

    /// <summary>
    /// The schema the data refers to.
    /// </summary>
    /// <exception cref="MissingSchemaException">When the store does not have it (and the <see cref="SchemaProvider"/> did not provide it)</exception>
    internal Entry GetById(SchemaId id)
    {
      if (_schemas.TryGetValue(id, out Entry entry))
        return entry;

      Func<SchemaId, byte[]> provider = SchemaProvider;
      byte[] provided = provider?.Invoke(id);
      if (provided is null)
        throw new MissingSchemaException(id);

      SchemaId providedId = Register(provided);
      if (providedId != id)
        throw new MsgPackException($"The {nameof(SchemaProvider)} was asked for the schema {id} but returned the schema {providedId}.");

      return _schemas[id];
    }

    /// <summary>
    /// The cached entry of a schema read inline, null when it is not cached (<see cref="CacheInlineSchemas"/> is off or the store is full).
    /// </summary>
    /// <param name="raw">The schema as read (in the byte order of the reader's settings)</param>
    /// <summary>
    /// The cached entry of a schema read inline, without adding it.
    /// </summary>
    internal bool TryGetInline(byte[] raw, out Entry entry)
    {
      return _inline.TryGetValue(raw, out entry);
    }

    internal Entry GetInline(byte[] raw)
    {
      if (_inline.TryGetValue(raw, out Entry entry))
        return entry;

      if (!CacheInlineSchemas || !TryReserve())
        return null;

      entry = new Entry(raw);
      if (_inline.TryAdd(raw, entry))
        return entry;

      Interlocked.Decrement(ref _received); // another thread was first
      return _inline[raw];
    }

    /// <summary>
    /// A schema known by its bytes, with the sessions that read it (one per <see cref="SessionKey"/>, the types are resolved by the type resolvers of the settings).
    /// </summary>
    internal sealed class Entry
    {
      internal readonly byte[] Bytes;

      /// <summary>
      /// The bytes with the lengths in the other byte order (see <see cref="GetInlineSchema"/>), made when first needed.
      /// </summary>
      internal byte[] OtherByteOrder;
      private readonly ConcurrentDictionary<SessionKey, SessionState> _readers = new ConcurrentDictionary<SessionKey, SessionState>();

      /// <param name="bytes">Not copied</param>
      internal Entry(byte[] bytes)
      {
        Bytes = bytes;
      }

      internal SessionState GetReader(MsgPackOptions settings)
      {
        SessionKey key = new SessionKey(null, settings);
        if (_readers.TryGetValue(key, out SessionState state))
          return state;
        return _readers.GetOrAdd(key, k => new SessionState(this));
      }

      /// <summary>
      /// Runs <paramref name="work"/> (converting what was read) with the session of this schema, shared by all calls that read it with settings of the same <see cref="SessionKey"/>.
      /// <para>When the work needs something the session does not have yet (e.g. a type that was not read before), it is repeated with a grown copy that replaces the session.</para>
      /// </summary>
      /// <param name="lengthSettings">The byte order the schema bytes were read with, null for the one of the specification (see <see cref="IndexedSchemaTypeResolver.FromBytes"/>)</param>
      /// <param name="work">Gets the session and the settings to use (a copy of <paramref name="settings"/> using the session), may run twice</param>
      internal TResult RunReader<TSettings, TResult>(TSettings settings, MsgPackOptions lengthSettings, Func<SchemaSession, TSettings, TResult> work) where TSettings : MsgPackOptions
      {
        SessionState state = GetReader(settings);
        SchemaSession session = state.Current;
        TResult result;
        if (session != null && TryRun(session, settings, work, out result))
          return result;

        lock (state)
        {
          SchemaSession current = state.Current;
          if (current != null && current != session && TryRun(current, settings, work, out result)) // grown by another thread in the meantime
            return result;

          SchemaSession grown = current?.Thaw(settings) ?? new SchemaSession(IndexedSchemaTypeResolver.FromBytes(Bytes, lengthSettings, settings), settings);
          result = work(grown, grown.Apply(settings));
          grown.Freeze();
          state.Current = grown;
          return result;
        }
      }
    }

    /// <summary>
    /// The current (frozen) session of a writer or reader, replaced when it grows. The lock serializes the growth.
    /// </summary>
    internal sealed class SessionState
    {
      private volatile SchemaSession _current;

      internal SessionState(Entry schema)
      {
        Schema = schema;
      }

      /// <summary>
      /// Reading: the schema of the session. Null for a writer.
      /// </summary>
      internal Entry Schema { get; }

      internal SchemaSession Current
      {
        get { return _current; }
        set { _current = value; }
      }
    }

    /// <summary>
    /// The settings that decide what a session contains: the type names (type resolvers, <see cref="AddTypeIdOption.FullName"/>), the properties (static filters), their order (<see cref="PropertyOrder"/>, the schema lists them in that order) and their ids (property id resolvers).
    /// The arrays are compared by reference: settings that share them (e.g. the defaults) share the sessions.
    /// </summary>
    internal struct SessionKey : IEquatable<SessionKey>
    {
      private readonly Type _root;
      private readonly IMsgPackTypeResolver[] _typeResolvers;
      private readonly IMsgPackPropertyIncludeStatically[] _staticFilters;
      private readonly IMsgPackPropertyIdResolver[] _propertyIdResolvers;
      private readonly AddTypeIdOption _addTypeIdOptions;
      private readonly EndianAction _endianAction; // inline schemas are read in the byte order of the reader
      private readonly PropertyOrder _propertyOrder;

      internal SessionKey(Type root, MsgPackOptions settings)
      {
        _root = root;
        _typeResolvers = settings._typeResolvers;
        _staticFilters = settings._staticFilters;
        _propertyIdResolvers = settings._propertyNameResolvers;
        _addTypeIdOptions = settings._addTypeIdOptions;
        _endianAction = settings._endianAction;
        _propertyOrder = settings._propertyOrder;
      }

      public bool Equals(SessionKey other)
      {
        return _root == other._root
          && ReferenceEquals(_typeResolvers, other._typeResolvers)
          && ReferenceEquals(_staticFilters, other._staticFilters)
          && ReferenceEquals(_propertyIdResolvers, other._propertyIdResolvers)
          && _addTypeIdOptions == other._addTypeIdOptions
          && _endianAction == other._endianAction
          && _propertyOrder == other._propertyOrder;
      }

      public override bool Equals(object obj)
      {
        return obj is SessionKey other && Equals(other);
      }

      public override int GetHashCode()
      {
        unchecked
        {
          int hash = _root is null ? 0 : _root.GetHashCode();
          hash = hash * 31 + RuntimeHelpers.GetHashCode(_typeResolvers);
          hash = hash * 31 + RuntimeHelpers.GetHashCode(_staticFilters);
          hash = hash * 31 + RuntimeHelpers.GetHashCode(_propertyIdResolvers);
          hash = hash * 31 + (int)_addTypeIdOptions;
          hash = hash * 31 + (int)_endianAction;
          return hash * 31 + (int)_propertyOrder;
        }
      }
    }

    #endregion

    /// <summary>
    /// Compares the content (inline schemas are looked up by their bytes, which are compared in full: no collision is possible).
    /// </summary>
    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
      internal static readonly ByteArrayComparer Instance = new ByteArrayComparer();

      public bool Equals(byte[] x, byte[] y)
      {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Length != y.Length) return false;
        for (int t = 0; t < x.Length; t++)
          if (x[t] != y[t]) return false;
        return true;
      }

      public int GetHashCode(byte[] bytes)
      {
        unchecked
        {
          uint hash = 2166136261; // FNV-1a
          for (int t = 0; t < bytes.Length; t++)
            hash = (hash ^ bytes[t]) * 16777619;
          return (int)hash;
        }
      }
    }
  }
}
