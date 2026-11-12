using LsMsgPack;
using LsMsgPack.Http;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormatters
{
  /// <summary>
  /// Client side schema negotiation for application/x-lsmsgpack (see <see cref="LsMsgPackHttpSerializer"/>): tells the server which schemas the client holds (request header MsgPack-Schemas),
  /// so it can refer to them instead of sending them, and registers the schemas the server sends inline (response header MsgPack-Schema).
  /// <para>Create it with <see cref="LsMsgPackMediaTypeFormatter.CreateHandler(HttpMessageHandler)"/>, so it shares the formatter's schema store: <c>new HttpClient(formatter.CreateHandler())</c>.
  /// With IHttpClientFactory, which sets the inner handler itself: <c>services.AddHttpClient("api").AddHttpMessageHandler(() => formatter.CreateDelegatingHandler())</c>
  /// (<see cref="LsMsgPackMediaTypeFormatter.CreateDelegatingHandler"/>).</para>
  /// </summary>
  public class LsMsgPackSchemaHandler : DelegatingHandler
  {
    private readonly LsMsgPackHttpSerializer _serializer;

    public LsMsgPackSchemaHandler(LsMsgPackHttpSerializer serializer, HttpMessageHandler innerHandler) : base(innerHandler)
    {
      _serializer = serializer ?? throw new System.ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Without an inner handler, for a pipeline that sets <see cref="DelegatingHandler.InnerHandler"/> itself (IHttpClientFactory refuses handlers that have one).
    /// </summary>
    public LsMsgPackSchemaHandler(LsMsgPackHttpSerializer serializer)
    {
      _serializer = serializer ?? throw new System.ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Ask for version 2 of the schema (<see cref="MsgPackMediaTypes.DescribedSchemaParameter"/>, see docs/WebFormatters.md), false by default: it describes the type of each property,
    /// so a client whose classes differ from the server's (other names, other namespaces) still pairs them with the server's (see docs/schema.md). The body is the same.
    /// <para>A request that accepts application/x-lsmsgpack gets <c>application/x-lsmsgpack; schema=described</c> with the same quality, a request without an Accept header
    /// <c>application/x-lsmsgpack; schema=described, application/x-lsmsgpack; q=0.9</c> (as the TypeScript client asks; the plain type for servers that don't know the parameter). Other Accept headers are left as they are.</para>
    /// </summary>
    public bool RequestDescribedSchemas { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (RequestDescribedSchemas)
        AskForDescribedSchema(request.Headers.Accept);

      if (_serializer.NegotiatesSchemas && !request.Headers.Contains(LsMsgPackHttpSerializer.SchemasHeader))
      {
        string schemas = _serializer.GetSchemasHeader(request.RequestUri);
        if (!(schemas is null))
          request.Headers.TryAddWithoutValidation(LsMsgPackHttpSerializer.SchemasHeader, schemas);
      }

      HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

      HttpContent content = response.Content;
      if (_serializer.NegotiatesSchemas && !(content is null) && MsgPackMediaTypes.IsLsMsgPack(content.Headers.ContentType?.MediaType) && !MsgPackMediaTypes.IsStream(content.Headers.ContentType.ToString()))
      { // a stream is read as it arrives, the schema of its head is learned by its reader (LsMsgPackHttpSerializer.CreateStreamReader)
        await content.LoadIntoBufferAsync().ConfigureAwait(false); // also runs a formatter that sets the header while writing (Web API in memory)
        string schemaId = HeaderValue(response, LsMsgPackHttpSerializer.SchemaHeader);
        if (!(schemaId is null))
        {
          byte[] body = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
          _serializer.LearnSchema(request.RequestUri, schemaId, body, 0, body.Length, content.Headers.ContentType.MediaType);
        }
      }
      return response;
    }

    private static void AskForDescribedSchema(HttpHeaderValueCollection<MediaTypeWithQualityHeaderValue> accept)
    {
      if (accept.Count == 0)
      {
        accept.Add(Described(null));
        accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack, 0.9));
        return;
      }
      if (MsgPackMediaTypes.AcceptsDescribedSchema(accept.ToString()))
        return;

      MediaTypeWithQualityHeaderValue plain = null;
      foreach (MediaTypeWithQualityHeaderValue range in accept)
      {
        if (MsgPackMediaTypes.IsLsMsgPack(range.MediaType) && range.Quality != 0)
        {
          plain = range;
          break;
        }
      }
      if (!(plain is null))
        accept.Add(Described(plain));
    }

    /// <param name="like">The range whose parameters (e.g. <see cref="MsgPackMediaTypes.StreamParameter"/>) and quality it gets, null: none</param>
    private static MediaTypeWithQualityHeaderValue Described(MediaTypeWithQualityHeaderValue like)
    {
      MediaTypeWithQualityHeaderValue described = new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack);
      described.Parameters.Add(new NameValueHeaderValue("schema", "described"));
      if (!(like is null))
      {
        foreach (NameValueHeaderValue parameter in like.Parameters)
        {
          if (!string.Equals(parameter.Name, "q", System.StringComparison.OrdinalIgnoreCase))
            described.Parameters.Add(new NameValueHeaderValue(parameter.Name, parameter.Value));
        }
        described.Quality = like.Quality;
      }
      return described;
    }

    internal static string HeaderValue(HttpResponseMessage response, string name)
    {
      IEnumerable<string> values;
      if (response.Headers.TryGetValues(name, out values) || response.Content.Headers.TryGetValues(name, out values))
      {
        foreach (string value in values)
          return value;
      }
      return null;
    }
  }
}
