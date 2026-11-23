using LsMsgPack;
using LsMsgPack.Streaming;
using LsMsgPack.IO;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace LsMsgPack.Http
{
  /// <summary>
  /// What the web formatters share: a serializer per media type (<see cref="LsMsgPackHttpOptions"/>) and the negotiation of schema references for application/x-lsmsgpack. Thread-safe, one per application (the formatters of one registration share it).
  /// <para><b>Negotiation</b> (responses only): the client lists the schema ids it holds in the request header <see cref="SchemasHeader"/>. The server writes a reference when the client holds the schema of the response and the schema inline otherwise,
  /// names the schema in the response header <see cref="SchemaHeader"/> and adds <c>Vary: MsgPack-Schemas</c>. The client registers the schemas it receives inline, and lists them in its next requests to that server.
  /// Without the request header the response is ordinary application/x-lsmsgpack (the schema inline).</para>
  /// <para><b>Version 2 of the schema</b> (<see cref="LsMsgPackHttpOptions.DescribeSchemas"/>): a client without the classes asks for it with <c>Accept: application/x-lsmsgpack; schema=described</c>
  /// (<see cref="MsgPackMediaTypes.AcceptsDescribedSchema"/>). A second serializer writes it (<see cref="SchemaVersion.Described"/>), with the same store: its schemas have their own ids and are negotiated alike.</para>
  /// <para>Trimmed and NativeAOT apps: create it with the code of LsMsgPack's source generator (<see cref="LsMsgPackHttpSerializer(LsMsgPackContext, LsMsgPackHttpOptions)"/>), as <see cref="LsMsgPackSerializer"/>.</para>
  /// </summary>
  public sealed class LsMsgPackHttpSerializer
  {
    /// <summary>
    /// Request header: the ids of the schemas the client holds (comma separated, <see cref="SchemaId.ToString"/>).
    /// </summary>
    public const string SchemasHeader = "MsgPack-Schemas";

    /// <summary>
    /// Response header: the id of the schema of the body (inline or referred to).
    /// </summary>
    public const string SchemaHeader = "MsgPack-Schema";

    /// <summary>
    /// A longer <see cref="SchemasHeader"/> is ignored (the schemas are then sent inline).
    /// </summary>
    private const int MaxSchemasHeaderLength = 8192;

    private readonly LsMsgPackSerializer _plain;
    private readonly LsMsgPackSerializer _lsMsgPack;
    private readonly LsMsgPackSerializer _described; // version 2 of the schema, null without DescribeSchemas
    private readonly LsMsgPackOptions _lsOptions;
    private readonly SchemaStore _store;
    private readonly bool _negotiate;
    private readonly int _maxAdvertised;
    private readonly bool _byRoute;
    private readonly bool _writeStreams;
    private readonly bool _reportDifferences;
    private readonly bool _logDifferencesAsWarning;
    private readonly ConcurrentDictionary<SchemaId, string> _hexIds = new ConcurrentDictionary<SchemaId, string>();
    private readonly ConcurrentDictionary<SchemaId, Written> _written = new ConcurrentDictionary<SchemaId, Written>(); // server side: per schema the header value and the inline schema
    private readonly ConcurrentDictionary<string, Advertised> _received = new ConcurrentDictionary<string, Advertised>(StringComparer.OrdinalIgnoreCase); // client side, per origin
    private volatile Server _lastServer; // client side: the origin of the last request and its entry of _received

    [RequiresUnreferencedCode(LsMsgPackSerializer.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackSerializer.RequiresDynamicCodeMessage)]
    [DynamicDependency(nameof(Serializer.ReflectionInstantiations), typeof(Serializer))] // NativeAOT: the generic code for the classes (see there)
    public LsMsgPackHttpSerializer() : this(new LsMsgPackHttpOptions(), null) { }

    /// <param name="options">Read once, later changes do not apply</param>
    [RequiresUnreferencedCode(LsMsgPackSerializer.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackSerializer.RequiresDynamicCodeMessage)]
    [DynamicDependency(nameof(Serializer.ReflectionInstantiations), typeof(Serializer))] // NativeAOT: the generic code for the classes (see there)
    public LsMsgPackHttpSerializer(LsMsgPackHttpOptions options) : this(options, null) { }

    /// <summary>
    /// With the default options and the code of LsMsgPack's source generator (see <see cref="LsMsgPackHttpSerializer(LsMsgPackContext, LsMsgPackHttpOptions)"/>).
    /// </summary>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    public LsMsgPackHttpSerializer(LsMsgPackContext context) : this(new LsMsgPackHttpOptions(), NotNull(context)) { }

    /// <summary>
    /// With the code of LsMsgPack's source generator: the serializers of both media types ask <paramref name="context"/> first (see <see cref="LsMsgPackSerializer(LsMsgPackContext, LsMsgPackOptions)"/>).
    /// </summary>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    /// <param name="options">Read once, later changes do not apply</param>
    public LsMsgPackHttpSerializer(LsMsgPackContext context, LsMsgPackHttpOptions options) : this(options, NotNull(context)) { }

    /// <param name="context">null for none: the public constructors without a context require unreferenced and dynamic code, callers of this one (the formatters) do the same</param>
    internal LsMsgPackHttpSerializer(LsMsgPackHttpOptions options, LsMsgPackContext context)
    {
      if (options is null)
        throw new ArgumentNullException(nameof(options));

      LsMsgPackOptions plain = options.Plain?.Clone() ?? LsMsgPackPresets.Generic();
      plain.PropertyNameCaseInsensitive |= options.PropertyNameCaseInsensitive;
      plain.CompileWritersAfter = options.CompileWritersAfter;
      plain.CompileReadersAfter = options.CompileReadersAfter;
      Limit(plain, options);
      _plain = new LsMsgPackSerializer(plain, context);

      LsMsgPackOptions ls = (options.XLsMsgPack ?? LsMsgPackPresets.LsMsgPack()).Clone();
      ls.PropertyNameCaseInsensitive |= options.PropertyNameCaseInsensitive;
      ls.CompileWritersAfter = options.CompileWritersAfter;
      ls.CompileReadersAfter = options.CompileReadersAfter;
      Limit(ls, options);
      _negotiate = options.NegotiateSchemas && ls.UseInexedSchema;
      if (_negotiate)
      {
        if (ls.SchemaStore is null)
          ls.SchemaStore = new SchemaStore();
        ls.WriteSchemaReference = true; // written with a reference, replaced by the schema when the client does not hold it
        ls.CompleteSchemas = options.CompleteSchemas;
      }
      _lsOptions = ls;
      _store = ls.SchemaStore;
      _lsMsgPack = new LsMsgPackSerializer(ls, context);
      if (options.DescribeSchemas && ls.UseInexedSchema)
      {
        LsMsgPackOptions described = ls.Clone(); // the same store: the writer sessions are kept per version
        described.SchemaVersion = SchemaVersion.Described;
        _described = new LsMsgPackSerializer(described, context);
      }
      _maxAdvertised = Math.Max(0, options.MaxAdvertisedSchemas);
      _byRoute = options.AdvertiseByRoute;
      _writeStreams = options.WriteStreams;
      _reportDifferences = options.ReportDifferences;
      _logDifferencesAsWarning = options.ReportDifferences && options.LogDifferencesAsWarning;
    }

    private static LsMsgPackContext NotNull(LsMsgPackContext context)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));
      return context;
    }

    /// <summary>
    /// The limits for request bodies (untrusted data, docs/security.md): those of the HTTP options, on the options of both media types.
    /// </summary>
    private static void Limit(LsMsgPackOptions target, LsMsgPackHttpOptions options)
    {
      target.MaxObjects = options.MaxObjects;
      target.MaxItems = options.MaxItems;
      target.MaxStringLength = options.MaxStringLength;
      target.MaxBinLength = options.MaxBinLength;
      target.MaxDecompressedLength = options.MaxDecompressedLength;
    }

    /// <summary>
    /// The formatters collect the differences between the bodies and the classes (<see cref="LsMsgPackHttpOptions.ReportDifferences"/>).
    /// </summary>
    public bool ReportsDifferences
    {
      get { return _reportDifferences; }
    }

    /// <summary>
    /// ASP.NET Core logs the differences as a warning instead of at the Debug level (<see cref="LsMsgPackHttpOptions.LogDifferencesAsWarning"/>, only with <see cref="ReportsDifferences"/>).
    /// </summary>
    public bool LogsDifferencesAsWarning
    {
      get { return _logDifferencesAsWarning; }
    }

    /// <summary>
    /// The store of the schemas of application/x-lsmsgpack (null when the schema is not cached).
    /// </summary>
    public SchemaStore SchemaStore
    {
      get { return _store; }
    }

    /// <summary>
    /// Schema references are negotiated (<see cref="LsMsgPackHttpOptions.NegotiateSchemas"/> with the indexed schema).
    /// </summary>
    public bool NegotiatesSchemas
    {
      get { return _negotiate; }
    }

    /// <summary>
    /// Responses of application/x-lsmsgpack have version 2 of the schema when the client asks for it (<see cref="LsMsgPackHttpOptions.DescribeSchemas"/>, with the indexed schema).
    /// They then depend on the Accept header (<c>Vary: Accept</c>).
    /// </summary>
    public bool DescribesSchemas
    {
      get { return !(_described is null); }
    }

    /// <summary>
    /// Responses of an <c>IAsyncEnumerable&lt;T&gt;</c> are written as a stream when the client asks for it (<see cref="LsMsgPackHttpOptions.WriteStreams"/>).
    /// </summary>
    public bool WritesStreams
    {
      get { return _writeStreams; }
    }

    /// <summary>
    /// The serializer of the media type (the Content-Type, parameters are ignored): application/x-lsmsgpack or plain MsgPack.
    /// </summary>
    public LsMsgPackSerializer For(string mediaType)
    {
      return MsgPackMediaTypes.IsLsMsgPack(mediaType) ? _lsMsgPack : _plain;
    }

    #region Server

    /// <summary>
    /// Makes the schema of responses of the declared type before the first one is written (with <see cref="LsMsgPackHttpOptions.CompleteSchemas"/>: every class its values can hold), and returns its id.
    /// Does nothing when such responses were written already.
    /// </summary>
    /// <param name="declaredType">The type the responses are declared as (as passed to <see cref="Serialize(object, Type, string, string, bool)"/>)</param>
    /// <param name="describedSchema">The schema of clients that ask for version 2 (<see cref="DescribesSchemas"/>), otherwise version 1</param>
    /// <exception cref="InvalidOperationException">Without <see cref="NegotiatesSchemas"/> (no schema is referred to), or version 2 without <see cref="DescribesSchemas"/></exception>
    public SchemaId PrepareSchema(Type declaredType, bool describedSchema)
    {
      if (!_negotiate)
        throw new InvalidOperationException($"Schemas are only kept with {nameof(LsMsgPackHttpOptions.NegotiateSchemas)} and the indexed schema.");
      if (describedSchema && _described is null)
        throw new InvalidOperationException($"Version 2 of the schema needs {nameof(LsMsgPackHttpOptions.DescribeSchemas)}.");
      return (describedSchema ? _described : _lsMsgPack).PrepareSchema(declaredType);
    }

    /// <summary>
    /// Serializes a response body.
    /// </summary>
    /// <param name="declaredType">The type the value is declared as (a type id is added when the value's type differs), null: the value's type</param>
    /// <param name="mediaType">The Content-Type of the response</param>
    /// <param name="clientSchemas">The request header <see cref="SchemasHeader"/> (null when there is none): the body refers to its schema when the client holds it</param>
    public MsgPackPayload Serialize(object value, Type declaredType, string mediaType, string clientSchemas)
    {
      return Serialize(value, declaredType, mediaType, clientSchemas, false);
    }

    /// <summary>
    /// Serializes a response body, with version 2 of the schema when the client asks for it.
    /// </summary>
    /// <param name="declaredType">The type the value is declared as (a type id is added when the value's type differs), null: the value's type</param>
    /// <param name="mediaType">The Content-Type of the response</param>
    /// <param name="clientSchemas">The request header <see cref="SchemasHeader"/> (null when there is none): the body refers to its schema when the client holds it</param>
    /// <param name="describedSchema">The client asked for version 2 of the schema (<see cref="MsgPackMediaTypes.AcceptsDescribedSchema"/> of the request's Accept header), ignored unless <see cref="DescribesSchemas"/></param>
    public MsgPackPayload Serialize(object value, Type declaredType, string mediaType, string clientSchemas, bool describedSchema)
    {
      if (!MsgPackMediaTypes.IsLsMsgPack(mediaType))
        return Whole(_plain.Serialize(value, declaredType));

      byte[] data = (describedSchema && !(_described is null) ? _described : _lsMsgPack).Serialize(value, declaredType);
      if (!_negotiate || data.Length < SchemaStore.ReferenceLength || data[0] != (byte)MsgPackTypeId.MpFExt16 || data[1] != unchecked((byte)SchemaStore.ReferenceExtensionType))
        return Whole(data); // null (nil, no schema)

      Written written = WrittenSchema(new SchemaId(data, 2));
      if (written.Schema.Length > SchemaStore.ReferenceLength && Holds(clientSchemas, written.Hex)) // a small schema is sent anyway: it is shorter than the reference
        return new MsgPackPayload(new ArraySegment<byte>(data, 0, SchemaStore.ReferenceLength), new ArraySegment<byte>(data, SchemaStore.ReferenceLength, data.Length - SchemaStore.ReferenceLength), written.Hex, true);

      return new MsgPackPayload(new ArraySegment<byte>(written.Schema), new ArraySegment<byte>(data, SchemaStore.ReferenceLength, data.Length - SchemaStore.ReferenceLength), written.Hex, false);
    }

    /// <summary>
    /// Serializes a request body (client side): the schema is always inline, the server may not hold it.
    /// </summary>
    public MsgPackPayload Serialize(object value, Type declaredType, string mediaType)
    {
      return Serialize(value, declaredType, mediaType, null);
    }

    /// <summary>
    /// Prepares a response body that is a stream (docs/Streaming.md) of one root value with a head: the schema of <paramref name="rootType"/> once (with application/x-lsmsgpack and the indexed schema; a reference when
    /// the client holds it, as <see cref="Serialize(object, Type, string, string, bool)"/> decides), then the value, whose objects are written without it at any depth, e.g. the items of a list of unknown length
    /// (<see cref="MsgPackStreamWriter.WriteArrayStart"/>, <see cref="MsgPackStreamWriter.WriteObject{T}(T)"/> per item, <see cref="MsgPackStreamWriter.WriteEnd"/>).
    /// Set the response headers (<see cref="MsgPackStreamHead.SchemaId"/>), then write the body with <see cref="MsgPackStreamHead.CreateWriter"/>.
    /// </summary>
    /// <param name="rootType">The declared type of the root value (e.g. <c>List&lt;T&gt;</c> for the items of an <c>IAsyncEnumerable&lt;T&gt;</c>)</param>
    /// <param name="mediaType">The Content-Type of the response</param>
    /// <param name="clientSchemas">The request header <see cref="SchemasHeader"/> (null when there is none)</param>
    /// <param name="describedSchema">The client asked for version 2 of the schema, ignored unless <see cref="DescribesSchemas"/></param>
    public MsgPackStreamHead PrepareStreamHead(Type rootType, string mediaType, string clientSchemas, bool describedSchema)
    {
      if (rootType is null)
        throw new ArgumentNullException(nameof(rootType));
      if (!MsgPackMediaTypes.IsLsMsgPack(mediaType))
        return new MsgPackStreamHead(_plain, rootType, null, MsgPackStreamSchemas.PerObject, null, false);

      LsMsgPackSerializer serializer = describedSchema && !(_described is null) ? _described : _lsMsgPack;
      if (!serializer.Implementation.Options._useInexedSchema)
        return new MsgPackStreamHead(serializer, rootType, null, MsgPackStreamSchemas.PerObject, null, false);

      Serializer.WriterHead head = serializer.Implementation.PrepareHead(rootType); // the id written is the one named in the header
      if (!_negotiate)
        return new MsgPackStreamHead(serializer, rootType, head, MsgPackStreamSchemas.PerObject, null, false);

      Written written = WrittenSchema(head.Id);
      bool reference = written.Schema.Length > SchemaStore.ReferenceLength && Holds(clientSchemas, written.Hex);
      return new MsgPackStreamHead(serializer, rootType, head, reference ? MsgPackStreamSchemas.External : MsgPackStreamSchemas.PerObject, written.Hex, reference);
    }

    /// <summary>
    /// A writer of a response body that is a stream (docs/Streaming.md), with the serializer of the media type: application/x-lsmsgpack with schema references writes each schema inline the first time
    /// and refers to it after that (<see cref="MsgPackStreamSchemas.PerStream"/>), otherwise every object has its schema (<see cref="MsgPackStreamSchemas.PerObject"/>). The stream is left open.
    /// </summary>
    /// <param name="body">The response body</param>
    /// <param name="mediaType">The Content-Type of the response</param>
    /// <param name="index">Keep an index of the root values and write it at the end (see <see cref="MsgPackStreamIndexOptions"/>), null: none</param>
    /// <param name="asynchronous">The writer writes to the body only in its async methods (ASP.NET Core does not allow synchronous writes to a response)</param>
    public MsgPackStreamWriter CreateStreamWriter(Stream body, string mediaType, MsgPackStreamIndexOptions index = null, bool asynchronous = true)
    {
      LsMsgPackSerializer serializer = For(mediaType);
      LsMsgPackOptions options = serializer.Implementation.Options;
      bool references = options._useInexedSchema && options._writeSchemaReference && !(options._schemaStore is null);
      return new MsgPackStreamWriter(body, serializer, references ? MsgPackStreamSchemas.PerStream : MsgPackStreamSchemas.PerObject, true, index, asynchronous);
    }

    /// <summary>
    /// Serializes a response body as <see cref="Serialize(object, Type, string, string, bool)"/> does, without a byte[] of it: <paramref name="write"/> gets the payload in the serializer's buffers of this thread,
    /// valid only until it returns. Set the headers there (<see cref="MsgPackPayload.SchemaId"/>, <see cref="MsgPackPayload.Length"/>), then copy the payload, e.g. into <c>HttpResponse.BodyWriter</c>.
    /// <para><paramref name="write"/> is called once, and not at all when serializing fails. Pass what it needs as <paramref name="state"/> (a static lambda: no closure per response).</para>
    /// </summary>
    /// <param name="declaredType">The type the value is declared as (a type id is added when the value's type differs), null: the value's type</param>
    /// <param name="mediaType">The Content-Type of the response</param>
    /// <param name="clientSchemas">The request header <see cref="SchemasHeader"/> (null when there is none): the body refers to its schema when the client holds it</param>
    /// <param name="describedSchema">The client asked for version 2 of the schema, ignored unless <see cref="DescribesSchemas"/></param>
    public void Serialize<TState>(object value, Type declaredType, string mediaType, string clientSchemas, bool describedSchema, TState state, Action<TState, MsgPackPayload> write)
    {
      if (write is null)
        throw new ArgumentNullException(nameof(write));
      bool lsMsgPack = MsgPackMediaTypes.IsLsMsgPack(mediaType);
      LsMsgPackSerializer serializer = !lsMsgPack ? _plain : describedSchema && !(_described is null) ? _described : _lsMsgPack;
      PayloadSink<TState> sink = PayloadSink<TState>.Rent(this, lsMsgPack, clientSchemas, state, write);
      try
      {
        serializer.Implementation.Serialize(declaredType ?? value?.GetType() ?? typeof(object), value, LsMsgPackSerializer.BoxedWriter, null, out byte[] unused, sink);
      }
      finally
      {
        sink.Return();
      }
    }

    /// <summary>
    /// The payload of <see cref="Serialize{TState}"/> from the parts the serializer hands its sink, negotiated as <see cref="Serialize(object, Type, string, string, bool)"/> does with the whole payload (the same bytes).
    /// </summary>
    /// <param name="head">The schema, the reference to it (<paramref name="reference"/>), or nothing (<paramref name="headLength"/> 0)</param>
    private MsgPackPayload Payload(byte[] head, int headLength, bool reference, MsgPackWriter body, bool lsMsgPack, string clientSchemas)
    {
      ArraySegment<byte> rest = new ArraySegment<byte>(body.Buf, 0, body.Pos);
      ArraySegment<byte> written = headLength == 0 ? default(ArraySegment<byte>) : new ArraySegment<byte>(head, 0, headLength);
      if (!reference || !lsMsgPack || !_negotiate)
        return new MsgPackPayload(written, rest, null, false);

      Written schema = WrittenSchema(new SchemaId(head, 2));
      if (schema.Schema.Length > SchemaStore.ReferenceLength && Holds(clientSchemas, schema.Hex))
        return new MsgPackPayload(written, rest, schema.Hex, true);
      return new MsgPackPayload(new ArraySegment<byte>(schema.Schema), rest, schema.Hex, false);
    }

    /// <summary>
    /// Hands the parts of a payload to the callback of <see cref="Serialize{TState}"/>. One per thread and type of state, taken while it is used (a nested call gets a new one): no sink per response.
    /// </summary>
    private sealed class PayloadSink<TState> : IPayloadSink
    {
      [ThreadStatic]
      private static PayloadSink<TState> _free;

      private LsMsgPackHttpSerializer _owner;
      private bool _lsMsgPack;
      private string _clientSchemas;
      private TState _state;
      private Action<TState, MsgPackPayload> _write;

      internal static PayloadSink<TState> Rent(LsMsgPackHttpSerializer owner, bool lsMsgPack, string clientSchemas, TState state, Action<TState, MsgPackPayload> write)
      {
        PayloadSink<TState> sink = _free ?? new PayloadSink<TState>();
        _free = null;
        sink._owner = owner;
        sink._lsMsgPack = lsMsgPack;
        sink._clientSchemas = clientSchemas;
        sink._state = state;
        sink._write = write;
        return sink;
      }

      /// <summary>
      /// Drops the references (the state may hold the request) and keeps the sink for the next call on this thread.
      /// </summary>
      internal void Return()
      {
        _owner = null;
        _clientSchemas = null;
        _state = default(TState);
        _write = null;
        _free = this;
      }

      public void Write(byte[] head, int headLength, bool reference, MsgPackWriter body)
      {
        _write(_state, _owner.Payload(head, headLength, reference, body, _lsMsgPack, _clientSchemas));
      }
    }

    private static MsgPackPayload Whole(byte[] data)
    {
      return new MsgPackPayload(default(ArraySegment<byte>), new ArraySegment<byte>(data), null, false);
    }

    private string Hex(SchemaId id)
    {
      if (_hexIds.TryGetValue(id, out string hex))
        return hex;
      return _hexIds.GetOrAdd(id, i => i.ToString());
    }

    /// <summary>
    /// What a response needs of a schema the server wrote, looked up once per response: the store has every schema it wrote, and keeps them.
    /// </summary>
    private sealed class Written
    {
      internal readonly string Hex;
      internal readonly byte[] Schema; // inline, in the byte order of the options

      internal Written(string hex, byte[] schema)
      {
        Hex = hex;
        Schema = schema;
      }
    }

    private Written WrittenSchema(SchemaId id)
    {
      if (_written.TryGetValue(id, out Written written))
        return written;
      return _written.GetOrAdd(id, new Written(Hex(id), _store.GetInlineSchema(id, _lsOptions)));
    }

    private static bool Holds(string clientSchemas, string hex)
    {
      if (clientSchemas is null || clientSchemas.Length > MaxSchemasHeaderLength)
        return false;
#if NET8_0_OR_GREATER
      // the ids are written in lower case (SchemaId.ToString) and clients send them back as they are: an ordinal search, unless the header has upper case letters
      // (OrdinalIgnoreCase is a scalar loop with InvariantGlobalization, 6% of a request with 32 ids)
      if (clientSchemas.AsSpan().IndexOfAnyInRange('A', 'Z') < 0)
        return clientSchemas.IndexOf(hex, StringComparison.Ordinal) >= 0;
#endif
      return clientSchemas.IndexOf(hex, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion

    /// <summary>
    /// Deserializes a request or response body.
    /// </summary>
    /// <param name="mediaType">The Content-Type of the body</param>
    /// <exception cref="MissingSchemaException">The body refers to a schema this side does not hold</exception>
    public object Deserialize(Type type, byte[] data, int offset, int count, string mediaType)
    {
      return For(mediaType).Deserialize(type, data, offset, count);
    }

    /// <summary>
    /// Deserializes a request or response body, and reports what did not match between the data and the classes (see <see cref="LsMsgPackSerializer.Deserialize(Type, byte[], int, int, out ReadDifferences)"/>).
    /// </summary>
    /// <param name="mediaType">The Content-Type of the body</param>
    /// <param name="differences">Null when the data matched the classes</param>
    /// <exception cref="MissingSchemaException">The body refers to a schema this side does not hold</exception>
    public object Deserialize(Type type, byte[] data, int offset, int count, string mediaType, out ReadDifferences differences)
    {
      return For(mediaType).Deserialize(type, data, offset, count, out differences);
    }

    /// <summary>
    /// Deserializes a body as the formatters do: with the differences when <see cref="ReportsDifferences"/>, otherwise without (<paramref name="differences"/> is then null).
    /// When reading throws, the differences found until then are in the exception's <see cref="Exception.Data"/> (<see cref="ReadDifferences.ExceptionDataKey"/>).
    /// </summary>
    public object DeserializeBody(Type type, byte[] data, int offset, int count, string mediaType, out ReadDifferences differences)
    {
      if (_reportDifferences)
        return Deserialize(type, data, offset, count, mediaType, out differences);
      differences = null;
      return Deserialize(type, data, offset, count, mediaType);
    }

    /// <summary>
    /// <see cref="DeserializeBody(Type, byte[], int, int, string, out ReadDifferences)"/> of a body in buffers (e.g. what the request's <c>PipeReader</c> holds): read in place when it is one segment of an array,
    /// otherwise from a pooled copy that is cleared afterwards (see <see cref="LsMsgPackSerializer.Deserialize{T}(in ReadOnlySequence{byte})"/>).
    /// </summary>
    public object DeserializeBody(Type type, in ReadOnlySequence<byte> data, string mediaType, out ReadDifferences differences)
    {
      LsMsgPackSerializer serializer = For(mediaType);
      if (_reportDifferences)
        return serializer.Deserialize(type, data, out differences);
      differences = null;
      return serializer.Deserialize(type, data);
    }

    /// <summary>
    /// Reads a body into an existing object instead of creating one (<see cref="LsMsgPackSerializer.Populate{T}(byte[], int, int, T, out ReadDifferences)"/>, docs/Populate.md),
    /// with the differences as <see cref="DeserializeBody(Type, byte[], int, int, string, out ReadDifferences)"/>: when <see cref="ReportsDifferences"/>, otherwise <paramref name="differences"/> is null.
    /// </summary>
    /// <param name="mediaType">The Content-Type of the body</param>
    /// <exception cref="MissingSchemaException">The body refers to a schema this side does not hold</exception>
    public void PopulateBody<T>(byte[] data, int offset, int count, T target, string mediaType, out ReadDifferences differences) where T : class
    {
      LsMsgPackSerializer serializer = For(mediaType);
      if (_reportDifferences)
      {
        serializer.Populate(data, offset, count, target, out differences);
        return;
      }
      differences = null;
      serializer.Populate(data, offset, count, target);
    }

    /// <summary>
    /// The differences an exception of <see cref="DeserializeBody"/> carries (null when there are none).
    /// </summary>
    public static ReadDifferences DifferencesOf(Exception ex)
    {
      for (Exception e = ex; e != null; e = e.InnerException)
      {
        if (e.Data.Contains(ReadDifferences.ExceptionDataKey))
          return e.Data[ReadDifferences.ExceptionDataKey] as ReadDifferences;
      }
      return null;
    }

    #region Client

    /// <summary>
    /// The value of the request header <see cref="SchemasHeader"/> for a request to the server, null when there are none: the schemas received for the route of the uri
    /// (<see cref="LsMsgPackHttpOptions.AdvertiseByRoute"/>, most recent first), for a route not called yet (or without AdvertiseByRoute) the ones received from the server most recently.
    /// </summary>
    /// <param name="requestUri">An absolute uri, its scheme, host and port identify the server, its path the route</param>
    public string GetSchemasHeader(Uri requestUri)
    {
      if (!_negotiate || _maxAdvertised == 0 || requestUri is null || !requestUri.IsAbsoluteUri)
        return null;
      Advertised advertised = AdvertisedFor(requestUri, false);
      if (advertised is null)
        return null;

      if (_byRoute && advertised.Routes.TryGetValue(RouteKey(requestUri, advertised), out Route route))
        return route.GetHeader(this);

      string value = advertised.Header; // the same until the list changes (the store never drops a schema)
      if (value is null)
      {
        LinkedList<SchemaId> ids = advertised.Ids;
        lock (ids)
        {
          StringBuilder header = null;
          foreach (SchemaId id in ids)
          {
            if (!_store.Contains(id))
              continue;
            if (header is null)
              header = new StringBuilder(33 * ids.Count);
            else
              header.Append(',');
            header.Append(Hex(id));
          }
          value = header is null ? string.Empty : header.ToString();
          advertised.Header = value;
        }
      }
      return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// Learns the schema of a response (client side): registers it when it came inline and remembers that the server (and the route of the uri) uses it, so the next requests advertise it.
    /// </summary>
    /// <param name="requestUri">The uri of the request</param>
    /// <param name="schemaHeader">The response header <see cref="SchemaHeader"/></param>
    /// <param name="mediaType">The Content-Type of the response</param>
    /// <param name="data">The body; null when the client may hold the schema already (the result is then false when it does not, call again with the body)</param>
    /// <returns>true when the client holds the schema (it may be referred to from now on)</returns>
    public bool LearnSchema(Uri requestUri, string schemaHeader, byte[] data, int offset, int count, string mediaType)
    {
      if (!_negotiate || _maxAdvertised == 0 || requestUri is null || !requestUri.IsAbsoluteUri || string.IsNullOrEmpty(schemaHeader) || !MsgPackMediaTypes.IsLsMsgPack(mediaType))
        return false;

      SchemaId id;
      try
      {
        id = SchemaId.Parse(schemaHeader.Trim());
      }
      catch (FormatException)
      {
        return false;
      }

      if (!_store.Contains(id))
      {
        if (data is null || count <= 0 || data[offset] == (byte)MsgPackTypeId.MpFExt16) // a reference to a schema this side does not have
          return false;
        try
        {
          if (_store.RegisterInline(data, offset, count, _lsOptions) != id)
            return false; // not the schema the header claims
        }
        catch (MsgPackException) // not a schema, or the store is full: the schemas keep coming inline
        {
          return false;
        }
      }

      Advertised advertised = AdvertisedFor(requestUri, true);
      if (_byRoute)
      {
        ConcurrentDictionary<ulong, Route> routes = advertised.Routes;
        ulong key = RouteKey(requestUri, advertised);
        if (!routes.TryGetValue(key, out Route route))
        {
          if (routes.Count >= MaxRoutes)
            routes.Clear(); // e.g. paths with names in them: start again
          route = routes.GetOrAdd(key, k => new Route());
        }
        if (!route.Learn(id))
          return true; // known to the route: the list of recent ids is for routes without ids, it changes only for new ones
      }
      object first = advertised.First;
      if (!(first is null) && (SchemaId)first == id) // the most recent one already (one endpoint called again and again): no lock
        return true;
      LinkedList<SchemaId> ids = advertised.Ids;
      lock (ids)
      {
        if (ids.First != null && ids.First.Value == id)
          return true;
        ids.Remove(id);
        ids.AddFirst(id);
        while (ids.Count > _maxAdvertised)
          ids.RemoveLast();
        advertised.First = id;
        advertised.Header = null;
      }
      return true;
    }

    /// <summary>
    /// A reader of a body that is a stream (docs/Streaming.md; <see cref="MsgPackMediaTypes.IsStream"/>), with the serializer of the media type. Read it with the async methods (<see cref="MsgPackStreamReader.ReadHeadAsync{TRoot}"/>, ...).
    /// <para>Client side, with the request's uri and the response header <see cref="SchemaHeader"/>: the schema of a head is learned as <see cref="LearnSchema"/> learns the schema of a body,
    /// so the next requests to that server advertise it. Server side (a request body): leave both null.</para>
    /// </summary>
    /// <param name="body">The response (or request) body</param>
    /// <param name="mediaType">Its Content-Type</param>
    /// <param name="requestUri">Client side: the uri of the request</param>
    /// <param name="schemaHeader">Client side: the response header <see cref="SchemaHeader"/></param>
    /// <param name="leaveOpen">Do not close the body when the reader is disposed</param>
    public MsgPackStreamReader CreateStreamReader(Stream body, string mediaType, Uri requestUri = null, string schemaHeader = null, bool leaveOpen = false)
    {
      MsgPackStreamReader reader = new MsgPackStreamReader(body, For(mediaType), leaveOpen);
      if (_negotiate && !(requestUri is null) && !string.IsNullOrEmpty(schemaHeader) && MsgPackMediaTypes.IsLsMsgPack(mediaType))
        reader.HeadSchemaRead = (data, offset, count) => LearnSchema(requestUri, schemaHeader, data, offset, count, mediaType);
      return reader;
    }

    /// <summary>
    /// The schemas received from one server (an origin), most recent first.
    /// </summary>
    private sealed class Advertised
    {
      internal readonly LinkedList<SchemaId> Ids = new LinkedList<SchemaId>(); // changed under its lock
      internal volatile string Header; // the value of SchemasHeader, "" for none, null: build it again (set under the lock of Ids)
      internal volatile object First; // the boxed first id, read without the lock
      internal readonly ConcurrentDictionary<ulong, Route> Routes = new ConcurrentDictionary<ulong, Route>();
      internal volatile string Origin; // of the entry, set with _lastServer
    }

    private const int MaxRoutes = 1024; // per origin, then they start again (e.g. paths with names in them)
    private const int MaxPerRoute = 4;

    /// <summary>
    /// The schemas received for one route (a path, see <see cref="RouteKey"/>), most recent first: what a request to it advertises.
    /// </summary>
    private sealed class Route
    {
      private readonly SchemaId[] _ids = new SchemaId[MaxPerRoute]; // changed under its lock
      private int _count;
      private volatile string _header; // null: build it again

      /// <returns>whether the id is new for the route</returns>
      internal bool Learn(SchemaId id)
      {
        lock (_ids)
        {
          if (_count > 0 && _ids[0] == id)
            return false;
          int at = Array.IndexOf(_ids, id, 0, _count);
          bool added = at < 0;
          if (added)
            at = Math.Min(_count++, MaxPerRoute - 1);
          if (_count > MaxPerRoute)
            _count = MaxPerRoute;
          Array.Copy(_ids, 0, _ids, 1, at);
          _ids[0] = id;
          _header = null;
          return added;
        }
      }

      internal string GetHeader(LsMsgPackHttpSerializer owner)
      {
        string header = _header;
        if (!(header is null))
          return header;
        lock (_ids)
        {
          int count = Math.Min(_count, owner._maxAdvertised);
          StringBuilder built = new StringBuilder(33 * count);
          for (int t = 0; t < count; t++)
          {
            if (t > 0)
              built.Append(',');
            built.Append(owner.Hex(_ids[t]));
          }
          return _header = built.ToString();
        }
      }
    }

    /// <summary>
    /// A hash of the path of the uri, in which every segment that looks like an id counts as the same segment: all digits, or 16 or more hexadecimal digits and dashes (Guids, hashes),
    /// so /orders/17 and /orders/18 are one route, /v2/orders another one than /v1/orders.
    /// Two routes with the same key only share their ids: a client advertises only schemas it holds.
    /// </summary>
    private static ulong RouteKey(Uri uri, Advertised advertised)
    {
      string text = uri.OriginalString;
      string origin = advertised.Origin;
      int start;
      if (!(origin is null) && SameOrigin(text, origin))
        start = origin.Length;
      else
      {
        text = uri.AbsolutePath;
        start = 0;
      }
      ulong hash = 14695981039346656037UL, segment = 14695981039346656037UL;
      bool digits = true, hex = true; // the segment so far
      int length = 0;
      for (int t = start; t <= text.Length; t++)
      {
        char c = t < text.Length ? text[t] : '/';
        if (c == '?' || c == '#')
          c = '/';
        if (c == '/')
        {
          bool id = length > 0 && (digits || (hex && length >= 16));
          hash = (hash ^ (id ? 0x23UL : segment)) * 1099511628211UL;
          segment = 14695981039346656037UL;
          digits = hex = true;
          length = 0;
          if (t < text.Length && (text[t] == '?' || text[t] == '#'))
            break;
          continue;
        }
        bool digit = c >= '0' && c <= '9';
        digits &= digit;
        hex &= digit || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') || c == '-';
        length++;
        segment = (segment ^ c) * 1099511628211UL;
      }
      return hash;
    }

    private sealed class Server
    {
      internal readonly string Origin;
      internal readonly Advertised Advertised;

      internal Server(string origin, Advertised advertised)
      {
        Origin = origin;
        Advertised = advertised;
      }
    }

    /// <param name="create">Create the entry of the origin when there is none, otherwise return null then</param>
    private Advertised AdvertisedFor(Uri uri, bool create)
    {
      // GetLeftPart allocates the origin on every call: a uri that starts with the last origin is that server (anything else, e.g. an explicit default port, gets its origin)
      Server last = _lastServer;
      if (!(last is null) && SameOrigin(uri.OriginalString, last.Origin))
        return last.Advertised;

      string origin = uri.GetLeftPart(UriPartial.Authority);
      Advertised advertised;
      if (create)
        advertised = _received.GetOrAdd(origin, o => new Advertised());
      else if (!_received.TryGetValue(origin, out advertised))
        return null;
      advertised.Origin = origin;
      _lastServer = new Server(origin, advertised);
      return advertised;
    }

    private static bool SameOrigin(string uri, string origin)
    {
      return uri.Length > origin.Length && uri.StartsWith(origin, StringComparison.OrdinalIgnoreCase) && uri[origin.Length] == '/';
    }

    #endregion
  }
}
