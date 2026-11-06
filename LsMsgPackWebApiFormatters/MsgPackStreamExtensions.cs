using LsMsgPack;
using LtMsgPack.Http;
using LtMsgPack.Streaming;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#endif

namespace LsMsgPackWebApiFormatters
{
  /// <summary>
  /// HttpClient: responses that are streams (docs/Streaming.md), read as they arrive. An ASP.NET Core action that returns an <c>IAsyncEnumerable&lt;T&gt;</c> sends its items as they come
  /// when the request asks for a stream (<see cref="StreamAccept"/>): <c>await foreach (Order order in client.GetFromMsgPackAsAsyncEnumerable&lt;Order&gt;("api/orders", formatter))</c>.
  /// Request bodies that are streams: <see cref="MsgPackStreamContent"/>.
  /// </summary>
  public static class MsgPackStreamExtensions
  {
    /// <summary>
    /// The Accept header of a request for a stream, when the request has none: application/x-lsmsgpack and application/msgpack with <see cref="MsgPackMediaTypes.StreamParameter"/>,
    /// and without it for servers (and content negotiation) that do not know the parameter. No <c>*/*</c>: ASP.NET Core MVC answers that with JSON.
    /// <see cref="LsMsgPackSchemaHandler.RequestDescribedSchemas"/> adds <c>schema=described</c>.
    /// </summary>
    public const string StreamAccept = MsgPackMediaTypes.XLsMsgPack + "; " + MsgPackMediaTypes.StreamParameter + ", " + MsgPackMediaTypes.XLsMsgPack + "; q=0.9, "
      + MsgPackMediaTypes.MsgPack + "; " + MsgPackMediaTypes.StreamParameter + "; q=0.8, " + MsgPackMediaTypes.MsgPack + "; q=0.7";

    /// <summary>
    /// Sends a GET request for a stream (<see cref="SendForMsgPackStreamAsync"/>) and returns a reader of the response body. Dispose the reader: it closes the body.
    /// </summary>
    /// <param name="formatter">Its serializer reads the body (the one the client's <see cref="LsMsgPackSchemaHandler"/> was created with)</param>
    public static async Task<MsgPackStreamReader> GetMsgPackStreamAsync(this HttpClient client, string requestUri, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri))
        return await SendForMsgPackStreamAsync(client, request, formatter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <see cref="GetMsgPackStreamAsync(HttpClient, string, LsMsgPackMediaTypeFormatter, CancellationToken)"/>
    /// </summary>
    public static async Task<MsgPackStreamReader> GetMsgPackStreamAsync(this HttpClient client, Uri requestUri, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri))
        return await SendForMsgPackStreamAsync(client, request, formatter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the request, returning when the response headers are there (<see cref="HttpCompletionOption.ResponseHeadersRead"/>), and returns a reader of the body (<see cref="ReadAsMsgPackStreamAsync"/>).
    /// A request without an Accept header gets <see cref="StreamAccept"/>. Dispose the reader: it closes the body.
    /// </summary>
    /// <exception cref="MsgPackHttpException">The status code is not a success (with the problem details or text of the body)</exception>
    /// <exception cref="NotSupportedException">The response is not MsgPack</exception>
    public static async Task<MsgPackStreamReader> SendForMsgPackStreamAsync(this HttpClient client, HttpRequestMessage request, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      if (client is null)
        throw new ArgumentNullException(nameof(client));
      if (request is null)
        throw new ArgumentNullException(nameof(request));
      if (formatter is null)
        throw new ArgumentNullException(nameof(formatter));
      if (request.Headers.Accept.Count == 0)
        request.Headers.TryAddWithoutValidation("Accept", StreamAccept);
      HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      return await ReadAsMsgPackStreamAsync(response, formatter).ConfigureAwait(false);
    }

    /// <summary>
    /// A reader of the response body, as it arrives (send the request with <see cref="HttpCompletionOption.ResponseHeadersRead"/>, otherwise HttpClient buffers it first).
    /// It reads streams (<see cref="MsgPackMediaTypes.IsStream"/>) and ordinary MsgPack bodies alike: <see cref="MsgPackStreamReader.ReadHeadAsync{TRoot}"/> also reads the payload of a <c>List&lt;T&gt;</c> in parts.
    /// The schema of a head is learned (<see cref="LtMsgPackHttpSerializer.CreateStreamReader"/>), so the next requests advertise it. An empty body (204 No Content) is an empty stream.
    /// Dispose the reader: it closes the body. The response is disposed when this throws.
    /// </summary>
    /// <exception cref="MsgPackHttpException">The status code is not a success (with the problem details or text of the body)</exception>
    /// <exception cref="NotSupportedException">The response is not MsgPack</exception>
    public static async Task<MsgPackStreamReader> ReadAsMsgPackStreamAsync(this HttpResponseMessage response, LsMsgPackMediaTypeFormatter formatter)
    {
      if (response is null)
        throw new ArgumentNullException(nameof(response));
      if (formatter is null)
        throw new ArgumentNullException(nameof(formatter));
      try
      {
        await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        HttpContent content = response.Content;
        if (content is null || response.StatusCode == HttpStatusCode.NoContent || content.Headers.ContentLength == 0)
          return formatter.HttpSerializer.CreateStreamReader(Stream.Null, MsgPackMediaTypes.MsgPack);

        string mediaType = content.Headers.ContentType?.MediaType;
        if (!MsgPackMediaTypes.IsSupported(mediaType))
          throw await MsgPackHttpClientExtensions.NotMsgPackAsync(content, mediaType, CancellationToken.None).ConfigureAwait(false);
        Stream body = await content.ReadAsStreamAsync().ConfigureAwait(false);
        return formatter.HttpSerializer.CreateStreamReader(body, mediaType, response.RequestMessage?.RequestUri, LsMsgPackSchemaHandler.HeaderValue(response, LtMsgPackHttpSerializer.SchemaHeader));
      }
      catch
      {
        response.Dispose();
        throw;
      }
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// Sends a GET request for a stream and returns the items as they arrive: of an <c>IAsyncEnumerable&lt;T&gt;</c> that the server streams (a head of <c>List&lt;T&gt;</c>, then an array of unknown length),
    /// or of a <c>List&lt;T&gt;</c> or <c>T[]</c> it sends as one body (servers that don't stream). The request is sent when the enumeration starts. Nil and an empty body are no items.
    /// </summary>
    /// <param name="formatter">Its serializer reads the body (the one the client's <see cref="LsMsgPackSchemaHandler"/> was created with)</param>
    public static async IAsyncEnumerable<T> GetFromMsgPackAsAsyncEnumerable<T>(this HttpClient client, string requestUri, LsMsgPackMediaTypeFormatter formatter, [EnumeratorCancellation] CancellationToken cancellationToken = default(CancellationToken))
    {
      using (MsgPackStreamReader reader = await GetMsgPackStreamAsync(client, requestUri, formatter, cancellationToken).ConfigureAwait(false))
      {
        await foreach (T item in ReadItemsAsync<T>(reader, cancellationToken).ConfigureAwait(false))
          yield return item;
      }
    }

    /// <summary>
    /// <see cref="GetFromMsgPackAsAsyncEnumerable{T}(HttpClient, string, LsMsgPackMediaTypeFormatter, CancellationToken)"/>
    /// </summary>
    public static async IAsyncEnumerable<T> GetFromMsgPackAsAsyncEnumerable<T>(this HttpClient client, Uri requestUri, LsMsgPackMediaTypeFormatter formatter, [EnumeratorCancellation] CancellationToken cancellationToken = default(CancellationToken))
    {
      using (MsgPackStreamReader reader = await GetMsgPackStreamAsync(client, requestUri, formatter, cancellationToken).ConfigureAwait(false))
      {
        await foreach (T item in ReadItemsAsync<T>(reader, cancellationToken).ConfigureAwait(false))
          yield return item;
      }
    }

    /// <summary>
    /// The items of the response body as they arrive (see <see cref="GetFromMsgPackAsAsyncEnumerable{T}(HttpClient, string, LsMsgPackMediaTypeFormatter, CancellationToken)"/>; send the request with
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/>, or use <see cref="SendForMsgPackStreamAsync"/>). The response is disposed at the end.
    /// </summary>
    public static async IAsyncEnumerable<T> ReadFromMsgPackAsAsyncEnumerable<T>(this HttpResponseMessage response, LsMsgPackMediaTypeFormatter formatter, [EnumeratorCancellation] CancellationToken cancellationToken = default(CancellationToken))
    {
      using (response)
      using (MsgPackStreamReader reader = await ReadAsMsgPackStreamAsync(response, formatter).ConfigureAwait(false))
      {
        await foreach (T item in ReadItemsAsync<T>(reader, cancellationToken).ConfigureAwait(false))
          yield return item;
      }
    }

    /// <summary>
    /// The items of a <c>List&lt;T&gt;</c>: its head (or the schema of its payload), then its array of unknown or known length.
    /// </summary>
    private static async IAsyncEnumerable<T> ReadItemsAsync<T>(MsgPackStreamReader reader, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      if (await reader.AtEndAsync(cancellationToken).ConfigureAwait(false))
        yield break; // an empty body
      await reader.ReadHeadAsync<List<T>>(cancellationToken).ConfigureAwait(false);
      if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.TokenType == MsgPackTokenType.Nil)
        yield break;
      await foreach (T item in reader.ReadElementsAsync<T>(cancellationToken).ConfigureAwait(false))
        yield return item;
    }
#endif
  }
}
