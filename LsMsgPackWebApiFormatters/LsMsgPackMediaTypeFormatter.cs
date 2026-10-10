using LsMsgPack;
using LtMsgPack.Http;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormatters
{
  /// <summary>
  /// Reads and writes application/msgpack, application/x-msgpack (plain MsgPack) and application/x-lsmsgpack with LtMsgPack (see <see cref="LtMsgPackHttpOptions"/>).
  /// <para>Server side (ASP.NET Web API 2): <c>config.Formatters.Add(new LsMsgPackMediaTypeFormatter());</c></para>
  /// <para>Client side (HttpClient): <c>new ObjectContent&lt;T&gt;(value, formatter, MsgPackMediaTypes.XLsMsgPack)</c> and <c>response.Content.ReadAsAsync&lt;T&gt;(new[] { formatter })</c>.
  /// Create the HttpClient with <c>new HttpClient(formatter.CreateHandler())</c> so application/x-lsmsgpack responses refer to the schemas the client already received instead of repeating them.</para>
  /// </summary>
  public class LsMsgPackMediaTypeFormatter : MediaTypeFormatter
  {
    private readonly LtMsgPackHttpSerializer Serializer;

    /// <summary>
    /// Server side, per request: the request header <see cref="LtMsgPackHttpSerializer.SchemasHeader"/> (the schemas the client holds, may be null). Null for the shared instance and on the client.
    /// </summary>
    private readonly RequestSchemas ServerRequest;

    public LsMsgPackMediaTypeFormatter() : this(new LtMsgPackHttpOptions()) { }

    /// <param name="options">Read once, later changes have no effect.</param>
    public LsMsgPackMediaTypeFormatter(LtMsgPackHttpOptions options) : this(new LtMsgPackHttpSerializer(options ?? new LtMsgPackHttpOptions())) { }

    public LsMsgPackMediaTypeFormatter(LtMsgPackHttpSerializer serializer)
    {
      Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
      for (int t = 0; t < MsgPackMediaTypes.All.Count; t++) // the first one is the default
        SupportedMediaTypes.Add(new MediaTypeHeaderValue(MsgPackMediaTypes.All[t]));
    }

    private LsMsgPackMediaTypeFormatter(LsMsgPackMediaTypeFormatter formatter, RequestSchemas request) : base(formatter)
    {
      Serializer = formatter.Serializer;
      ServerRequest = request;
    }

    /// <summary>
    /// The serializer shared by this formatter and its handler (<see cref="CreateHandler"/>).
    /// </summary>
    public LtMsgPackHttpSerializer HttpSerializer
    {
      get { return Serializer; }
    }

    /// <summary>
    /// Client side: a handler for the HttpClient that tells the server which schemas this formatter holds, and learns the schemas of the responses. Without it the responses carry their schema inline.
    /// </summary>
    /// <param name="innerHandler">The next handler, null: a new HttpClientHandler</param>
    public LsMsgPackSchemaHandler CreateHandler(HttpMessageHandler innerHandler = null)
    {
      return new LsMsgPackSchemaHandler(Serializer, innerHandler ?? new HttpClientHandler());
    }

    /// <summary>
    /// Server side (Web API content negotiation): an instance that knows which schemas the client holds.
    /// </summary>
    public override MediaTypeFormatter GetPerRequestFormatterInstance(Type type, HttpRequestMessage request, MediaTypeHeaderValue mediaType)
    {
      if (request is null || !Serializer.NegotiatesSchemas || !MsgPackMediaTypes.IsLsMsgPack(mediaType?.MediaType))
        return this;
      string schemas = request.Headers.TryGetValues(LtMsgPackHttpSerializer.SchemasHeader, out System.Collections.Generic.IEnumerable<string> values) ? string.Join(",", values) : null;
      return new LsMsgPackMediaTypeFormatter(this, new RequestSchemas(schemas));
    }

    public override bool CanReadType(Type type)
    {
      if (type is null)
        throw new ArgumentNullException(nameof(type));
      return true;
    }

    public override bool CanWriteType(Type type)
    {
      if (type is null)
        throw new ArgumentNullException(nameof(type));
      return true;
    }

    /// <summary>
    /// The buffer is sized by the Content-Length up to this size, larger bodies grow it as they arrive (the header is not trusted with a large allocation).
    /// </summary>
    private const int MaxPresized = 1024 * 1024;

    public override Task<object> ReadFromStreamAsync(Type type, Stream readStream, HttpContent content, IFormatterLogger formatterLogger)
    {
      return ReadFromStreamAsync(type, readStream, content, formatterLogger, CancellationToken.None);
    }

    public override async Task<object> ReadFromStreamAsync(Type type, Stream readStream, HttpContent content, IFormatterLogger formatterLogger, CancellationToken cancellationToken)
    {
      // Buffer the body asynchronously, the deserializer reads synchronously.
      long? length = content?.Headers.ContentLength;
      MemoryStream body = length > 0 && length <= MaxPresized ? new MemoryStream((int)length) : new MemoryStream(); // sized, so it does not grow while copying
      await readStream.CopyToAsync(body, 81920, cancellationToken).ConfigureAwait(false);
      if (body.Length == 0)
        return GetDefaultValueForType(type);

      try
      {
        object model = Serializer.DeserializeBody(type, body.GetBuffer(), 0, (int)body.Length, content?.Headers.ContentType?.MediaType, out ReadDifferences differences);
        if (differences != null)
          ReadDifferencesExtensions.Set(content, differences);
        if (!(model is null) && !type.IsInstanceOfType(model)) // The deserializer passes through values it cannot convert (eg. a string where a map was expected)
          throw new MsgPackException("The data could not be deserialized as " + type.Name + ", it contains a " + model.GetType().Name + ".");
        return model;
      }
      catch (Exception ex) when (Serializer.ReportsDifferences && ReportFailure(content, ex)) // never true: keeps the differences found until the exception
      {
        throw;
      }
      catch (Exception ex) when (!(formatterLogger is null) && !(ex is OperationCanceledException))
      {
        // Server side: report it in the ModelState (like the JSON formatter does), the body is fully buffered so anything thrown here is caused by the content
        formatterLogger.LogError(string.Empty, ex);
        return GetDefaultValueForType(type);
      }
    }

    /// <returns>False (an exception filter)</returns>
    private static bool ReportFailure(HttpContent content, Exception ex)
    {
      ReadDifferences differences = LtMsgPackHttpSerializer.DifferencesOf(ex);
      if (differences != null)
        ReadDifferencesExtensions.Set(content, differences);
      return false;
    }

    public override Task WriteToStreamAsync(Type type, object value, Stream writeStream, HttpContent content, TransportContext transportContext)
    {
      return WriteToStreamAsync(type, value, writeStream, content, transportContext, CancellationToken.None);
    }

    public override Task WriteToStreamAsync(Type type, object value, Stream writeStream, HttpContent content, TransportContext transportContext, CancellationToken cancellationToken)
    {
      string mediaType = content?.Headers.ContentType?.MediaType;

      // Serialize to a buffer and write it asynchronously. A request (client side) always carries its schema inline.
      MsgPackPayload payload = Serializer.Serialize(value, type, mediaType, ServerRequest?.Schemas);
      if (!(ServerRequest is null) && !(content is null) && !(payload.SchemaId is null))
      {
        // Web API buffers ObjectContent before sending the headers, so they can still be set. A formatter cannot set response headers (Vary),
        // so a response that only this client can read expires at once, for caches.
        content.Headers.TryAddWithoutValidation(LtMsgPackHttpSerializer.SchemaHeader, payload.SchemaId);
        if (payload.IsReference)
          content.Headers.Expires = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
      }
      return payload.WriteToAsync(writeStream, cancellationToken);
    }

    private sealed class RequestSchemas
    {
      internal RequestSchemas(string schemas)
      {
        Schemas = schemas;
      }

      internal string Schemas { get; }
    }
  }
}
