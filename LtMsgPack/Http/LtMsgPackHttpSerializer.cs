using LsMsgPack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace LtMsgPack.Http
{
  /// <summary>
  /// What the web formatters share: a serializer per media type (<see cref="LtMsgPackHttpOptions"/>) and the negotiation of schema references for application/x-lsmsgpack. Thread-safe, one per application (the formatters of one registration share it).
  /// <para><b>Negotiation</b> (responses only): the client lists the schema ids it holds in the request header <see cref="SchemasHeader"/>. The server writes a reference when the client holds the schema of the response and the schema inline otherwise,
  /// names the schema in the response header <see cref="SchemaHeader"/> and adds <c>Vary: MsgPack-Schemas</c>. The client registers the schemas it receives inline, and lists them in its next requests to that server.
  /// Without the request header the response is ordinary application/x-lsmsgpack (the schema inline).</para>
  /// <para><b>Version 2 of the schema</b> (<see cref="LtMsgPackHttpOptions.DescribeSchemas"/>): a client without the classes asks for it with <c>Accept: application/x-lsmsgpack; schema=described</c>
  /// (<see cref="MsgPackMediaTypes.AcceptsDescribedSchema"/>). A second serializer writes it (<see cref="SchemaVersion.Described"/>), with the same store: its schemas have their own ids and are negotiated alike.</para>
  /// <para>Trimmed and NativeAOT apps: create it with the code of LtMsgPack's source generator (<see cref="LtMsgPackHttpSerializer(LtMsgPackContext, LtMsgPackHttpOptions)"/>), as <see cref="LtMsgPackSerializer"/>.</para>
  /// </summary>
  public sealed class LtMsgPackHttpSerializer
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

    private readonly LtMsgPackSerializer _plain;
    private readonly LtMsgPackSerializer _lsMsgPack;
    private readonly LtMsgPackSerializer _described; // version 2 of the schema, null without DescribeSchemas
    private readonly LtMsgPackOptions _lsOptions;
    private readonly SchemaStore _store;
    private readonly bool _negotiate;
    private readonly int _maxAdvertised;
    private readonly bool _reportDifferences;
    private readonly bool _logDifferencesAsWarning;
    private readonly ConcurrentDictionary<SchemaId, string> _hexIds = new ConcurrentDictionary<SchemaId, string>();
    private readonly ConcurrentDictionary<string, LinkedList<SchemaId>> _received = new ConcurrentDictionary<string, LinkedList<SchemaId>>(StringComparer.OrdinalIgnoreCase);

    [RequiresUnreferencedCode(LtMsgPackSerializer.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LtMsgPackSerializer.RequiresDynamicCodeMessage)]
    public LtMsgPackHttpSerializer() : this(new LtMsgPackHttpOptions(), null) { }

    /// <param name="options">Read once, later changes do not apply</param>
    [RequiresUnreferencedCode(LtMsgPackSerializer.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LtMsgPackSerializer.RequiresDynamicCodeMessage)]
    public LtMsgPackHttpSerializer(LtMsgPackHttpOptions options) : this(options, null) { }

    /// <summary>
    /// With the default options and the code of LtMsgPack's source generator (see <see cref="LtMsgPackHttpSerializer(LtMsgPackContext, LtMsgPackHttpOptions)"/>).
    /// </summary>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    public LtMsgPackHttpSerializer(LtMsgPackContext context) : this(new LtMsgPackHttpOptions(), NotNull(context)) { }

    /// <summary>
    /// With the code of LtMsgPack's source generator: the serializers of both media types ask <paramref name="context"/> first (see <see cref="LtMsgPackSerializer(LtMsgPackContext, LtMsgPackOptions)"/>).
    /// </summary>
    /// <param name="context">E.g. <c>AppMsgPackContext.Default</c></param>
    /// <param name="options">Read once, later changes do not apply</param>
    public LtMsgPackHttpSerializer(LtMsgPackContext context, LtMsgPackHttpOptions options) : this(options, NotNull(context)) { }

    /// <param name="context">null for none: the public constructors without a context require unreferenced and dynamic code, callers of this one (the formatters) do the same</param>
    internal LtMsgPackHttpSerializer(LtMsgPackHttpOptions options, LtMsgPackContext context)
    {
      if (options is null)
        throw new ArgumentNullException(nameof(options));

      LtMsgPackOptions plain = options.Plain?.Clone() ?? MsgPackMediaTypes.ToPlain(new LtMsgPackOptions());
      plain.PropertyNameCaseInsensitive |= options.PropertyNameCaseInsensitive;
      _plain = new LtMsgPackSerializer(plain, context);

      LtMsgPackOptions ls = (options.XLsMsgPack ?? LtMsgPackPresets.LsMsgPack()).Clone();
      ls.PropertyNameCaseInsensitive |= options.PropertyNameCaseInsensitive;
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
      _lsMsgPack = new LtMsgPackSerializer(ls, context);
      if (options.DescribeSchemas && ls.UseInexedSchema)
      {
        LtMsgPackOptions described = ls.Clone(); // the same store: the writer sessions are kept per version
        described.SchemaVersion = SchemaVersion.Described;
        _described = new LtMsgPackSerializer(described, context);
      }
      _maxAdvertised = Math.Max(0, options.MaxAdvertisedSchemas);
      _reportDifferences = options.ReportDifferences;
      _logDifferencesAsWarning = options.ReportDifferences && options.LogDifferencesAsWarning;
    }

    private static LtMsgPackContext NotNull(LtMsgPackContext context)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));
      return context;
    }

    /// <summary>
    /// The formatters collect the differences between the bodies and the classes (<see cref="LtMsgPackHttpOptions.ReportDifferences"/>).
    /// </summary>
    public bool ReportsDifferences
    {
      get { return _reportDifferences; }
    }

    /// <summary>
    /// ASP.NET Core logs the differences as a warning instead of at the Debug level (<see cref="LtMsgPackHttpOptions.LogDifferencesAsWarning"/>, only with <see cref="ReportsDifferences"/>).
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
    /// Schema references are negotiated (<see cref="LtMsgPackHttpOptions.NegotiateSchemas"/> with the indexed schema).
    /// </summary>
    public bool NegotiatesSchemas
    {
      get { return _negotiate; }
    }

    /// <summary>
    /// Responses of application/x-lsmsgpack have version 2 of the schema when the client asks for it (<see cref="LtMsgPackHttpOptions.DescribeSchemas"/>, with the indexed schema).
    /// They then depend on the Accept header (<c>Vary: Accept</c>).
    /// </summary>
    public bool DescribesSchemas
    {
      get { return !(_described is null); }
    }

    /// <summary>
    /// The serializer of the media type (the Content-Type, parameters are ignored): application/x-lsmsgpack or plain MsgPack.
    /// </summary>
    public LtMsgPackSerializer For(string mediaType)
    {
      return MsgPackMediaTypes.IsLsMsgPack(mediaType) ? _lsMsgPack : _plain;
    }

    #region Server

    /// <summary>
    /// Makes the schema of responses of the declared type before the first one is written (with <see cref="LtMsgPackHttpOptions.CompleteSchemas"/>: every class its values can hold), and returns its id.
    /// Does nothing when such responses were written already.
    /// </summary>
    /// <param name="declaredType">The type the responses are declared as (as passed to <see cref="Serialize(object, Type, string, string, bool)"/>)</param>
    /// <param name="describedSchema">The schema of clients that ask for version 2 (<see cref="DescribesSchemas"/>), otherwise version 1</param>
    /// <exception cref="InvalidOperationException">Without <see cref="NegotiatesSchemas"/> (no schema is referred to), or version 2 without <see cref="DescribesSchemas"/></exception>
    public SchemaId PrepareSchema(Type declaredType, bool describedSchema)
    {
      if (!_negotiate)
        throw new InvalidOperationException($"Schemas are only kept with {nameof(LtMsgPackHttpOptions.NegotiateSchemas)} and the indexed schema.");
      if (describedSchema && _described is null)
        throw new InvalidOperationException($"Version 2 of the schema needs {nameof(LtMsgPackHttpOptions.DescribeSchemas)}.");
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

      SchemaId id = new SchemaId(data, 2);
      string hex = Hex(id);
      byte[] schema = _store.GetInlineSchema(id, _lsOptions); // the store has every schema it wrote
      if (schema.Length > SchemaStore.ReferenceLength && Holds(clientSchemas, hex)) // a small schema is sent anyway: it is shorter than the reference
        return new MsgPackPayload(default(ArraySegment<byte>), new ArraySegment<byte>(data), hex, true);

      return new MsgPackPayload(new ArraySegment<byte>(schema), new ArraySegment<byte>(data, SchemaStore.ReferenceLength, data.Length - SchemaStore.ReferenceLength), hex, false);
    }

    /// <summary>
    /// Serializes a request body (client side): the schema is always inline, the server may not hold it.
    /// </summary>
    public MsgPackPayload Serialize(object value, Type declaredType, string mediaType)
    {
      return Serialize(value, declaredType, mediaType, null);
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

    private static bool Holds(string clientSchemas, string hex)
    {
      return !(clientSchemas is null) && clientSchemas.Length <= MaxSchemasHeaderLength && clientSchemas.IndexOf(hex, StringComparison.OrdinalIgnoreCase) >= 0;
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
    /// Deserializes a request or response body, and reports what did not match between the data and the classes (see <see cref="LtMsgPackSerializer.Deserialize(Type, byte[], int, int, out ReadDifferences)"/>).
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
    /// The value of the request header <see cref="SchemasHeader"/> for a request to the server: the schemas received from it that the store still holds (most recent first), null when there are none.
    /// </summary>
    /// <param name="requestUri">An absolute uri, its scheme, host and port identify the server</param>
    public string GetSchemasHeader(Uri requestUri)
    {
      if (!_negotiate || _maxAdvertised == 0 || requestUri is null || !requestUri.IsAbsoluteUri)
        return null;
      if (!_received.TryGetValue(Origin(requestUri), out LinkedList<SchemaId> ids))
        return null;

      StringBuilder header = null;
      lock (ids)
      {
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
      }
      return header?.ToString();
    }

    /// <summary>
    /// Learns the schema of a response (client side): registers it when it came inline and remembers that the server uses it, so the next requests advertise it.
    /// </summary>
    /// <param name="requestUri">The uri of the request</param>
    /// <param name="schemaHeader">The response header <see cref="SchemaHeader"/></param>
    /// <param name="mediaType">The Content-Type of the response</param>
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

      LinkedList<SchemaId> ids = _received.GetOrAdd(Origin(requestUri), o => new LinkedList<SchemaId>());
      lock (ids)
      {
        if (ids.First != null && ids.First.Value == id)
          return true;
        ids.Remove(id);
        ids.AddFirst(id);
        while (ids.Count > _maxAdvertised)
          ids.RemoveLast();
      }
      return true;
    }

    private static string Origin(Uri uri)
    {
      return uri.GetLeftPart(UriPartial.Authority);
    }

    #endregion
  }
}
