using LsMsgPack;
using LtMsgPack.Http;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormatters
{
  /// <summary>
  /// Client side schema negotiation for application/x-lsmsgpack (see <see cref="LtMsgPackHttpSerializer"/>): tells the server which schemas the client holds (request header MsgPack-Schemas),
  /// so it can refer to them instead of sending them, and registers the schemas the server sends inline (response header MsgPack-Schema).
  /// <para>Create it with <see cref="LsMsgPackMediaTypeFormatter.CreateHandler"/>, so it shares the formatter's schema store: <c>new HttpClient(formatter.CreateHandler())</c>.</para>
  /// </summary>
  public class LsMsgPackSchemaHandler : DelegatingHandler
  {
    private readonly LtMsgPackHttpSerializer _serializer;

    public LsMsgPackSchemaHandler(LtMsgPackHttpSerializer serializer, HttpMessageHandler innerHandler) : base(innerHandler)
    {
      _serializer = serializer ?? throw new System.ArgumentNullException(nameof(serializer));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      if (_serializer.NegotiatesSchemas && !request.Headers.Contains(LtMsgPackHttpSerializer.SchemasHeader))
      {
        string schemas = _serializer.GetSchemasHeader(request.RequestUri);
        if (!(schemas is null))
          request.Headers.TryAddWithoutValidation(LtMsgPackHttpSerializer.SchemasHeader, schemas);
      }

      HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

      HttpContent content = response.Content;
      if (_serializer.NegotiatesSchemas && !(content is null) && MsgPackMediaTypes.IsLsMsgPack(content.Headers.ContentType?.MediaType))
      {
        await content.LoadIntoBufferAsync().ConfigureAwait(false); // also runs a formatter that sets the header while writing (Web API in memory)
        string schemaId = HeaderValue(response, LtMsgPackHttpSerializer.SchemaHeader);
        if (!(schemaId is null))
        {
          byte[] body = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
          _serializer.LearnSchema(request.RequestUri, schemaId, body, 0, body.Length, content.Headers.ContentType.MediaType);
        }
      }
      return response;
    }

    private static string HeaderValue(HttpResponseMessage response, string name)
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
