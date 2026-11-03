using LsMsgPack;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormatters
{
  /// <summary>
  /// HttpClient: MsgPack bodies in one call, as System.Net.Http.Json does for JSON. <c>Order order = await client.GetFromMsgPackAsync&lt;Order&gt;("api/orders/1", formatter)</c>,
  /// <c>HttpResponseMessage response = await client.PostAsMsgPackAsync("api/orders", order, formatter)</c>, <c>await response.Content.ReadFromMsgPackAsync&lt;Order&gt;(formatter)</c>.
  /// Streams: <see cref="MsgPackStreamExtensions"/>.
  /// </summary>
  public static class MsgPackHttpClientExtensions
  {
    /// <summary>
    /// The Accept header these methods send when neither the request nor the client's <see cref="HttpClient.DefaultRequestHeaders"/> have one: application/x-lsmsgpack, then application/msgpack.
    /// No <c>*/*</c>: ASP.NET Core MVC answers that with JSON. <see cref="LsMsgPackSchemaHandler.RequestDescribedSchemas"/> adds <c>schema=described</c>.
    /// </summary>
    public const string Accept = MsgPackMediaTypes.XLsMsgPack + ", " + MsgPackMediaTypes.MsgPack + "; q=0.9";

    private static readonly HttpMethod Patch = new HttpMethod("PATCH"); // HttpMethod.Patch is not in .NET Standard 2.0

    /// <summary>
    /// Sends a GET request and reads the response body as <typeparamref name="T"/> (<see cref="ReadFromMsgPackAsync{T}"/>).
    /// </summary>
    /// <param name="formatter">Its serializer reads the body (the one the client's <see cref="LsMsgPackSchemaHandler"/> was created with)</param>
    /// <returns>The value, default for an empty body (204 No Content)</returns>
    /// <exception cref="MsgPackHttpException">The status code is not a success (with the problem details or text of the body)</exception>
    /// <exception cref="NotSupportedException">The response is not MsgPack, or a stream</exception>
    /// <exception cref="MsgPackException">The body is not valid or not a <typeparamref name="T"/></exception>
    public static Task<T> GetFromMsgPackAsync<T>(this HttpClient client, string requestUri, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      return GetAsync<T>(client, new HttpRequestMessage(HttpMethod.Get, requestUri), formatter, cancellationToken);
    }

    /// <summary>
    /// <see cref="GetFromMsgPackAsync{T}(HttpClient, string, LsMsgPackMediaTypeFormatter, CancellationToken)"/>
    /// </summary>
    public static Task<T> GetFromMsgPackAsync<T>(this HttpClient client, Uri requestUri, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      return GetAsync<T>(client, new HttpRequestMessage(HttpMethod.Get, requestUri), formatter, cancellationToken);
    }

    /// <summary>
    /// Sends a POST request with the value as an application/x-lsmsgpack body (declared type <typeparamref name="T"/>, the schema inline), asking for MsgPack (<see cref="Accept"/>).
    /// The status code is not checked: <see cref="EnsureSuccessStatusCodeAsync"/>. Read the response with <see cref="ReadFromMsgPackAsync{T}"/>. For another media type: <c>client.PostAsync(uri, new ObjectContent&lt;T&gt;(value, formatter, mediaType))</c>.
    /// </summary>
    /// <param name="formatter">Its serializer writes the body</param>
    public static Task<HttpResponseMessage> PostAsMsgPackAsync<T>(this HttpClient client, string requestUri, T value, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      return SendAsync(client, new HttpRequestMessage(HttpMethod.Post, requestUri), value, formatter, cancellationToken);
    }

    /// <summary>
    /// <see cref="PostAsMsgPackAsync{T}(HttpClient, string, T, LsMsgPackMediaTypeFormatter, CancellationToken)"/>
    /// </summary>
    public static Task<HttpResponseMessage> PostAsMsgPackAsync<T>(this HttpClient client, Uri requestUri, T value, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      return SendAsync(client, new HttpRequestMessage(HttpMethod.Post, requestUri), value, formatter, cancellationToken);
    }

    /// <summary>
    /// A PUT request, as <see cref="PostAsMsgPackAsync{T}(HttpClient, string, T, LsMsgPackMediaTypeFormatter, CancellationToken)"/>.
    /// </summary>
    public static Task<HttpResponseMessage> PutAsMsgPackAsync<T>(this HttpClient client, string requestUri, T value, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      return SendAsync(client, new HttpRequestMessage(HttpMethod.Put, requestUri), value, formatter, cancellationToken);
    }

    /// <summary>
    /// <see cref="PutAsMsgPackAsync{T}(HttpClient, string, T, LsMsgPackMediaTypeFormatter, CancellationToken)"/>
    /// </summary>
    public static Task<HttpResponseMessage> PutAsMsgPackAsync<T>(this HttpClient client, Uri requestUri, T value, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      return SendAsync(client, new HttpRequestMessage(HttpMethod.Put, requestUri), value, formatter, cancellationToken);
    }

    /// <summary>
    /// A PATCH request, as <see cref="PostAsMsgPackAsync{T}(HttpClient, string, T, LsMsgPackMediaTypeFormatter, CancellationToken)"/>.
    /// </summary>
    public static Task<HttpResponseMessage> PatchAsMsgPackAsync<T>(this HttpClient client, string requestUri, T value, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      return SendAsync(client, new HttpRequestMessage(Patch, requestUri), value, formatter, cancellationToken);
    }

    /// <summary>
    /// <see cref="PatchAsMsgPackAsync{T}(HttpClient, string, T, LsMsgPackMediaTypeFormatter, CancellationToken)"/>
    /// </summary>
    public static Task<HttpResponseMessage> PatchAsMsgPackAsync<T>(this HttpClient client, Uri requestUri, T value, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      return SendAsync(client, new HttpRequestMessage(Patch, requestUri), value, formatter, cancellationToken);
    }

    /// <summary>
    /// Throws a <see cref="MsgPackHttpException"/> for a status code outside 200-299, as <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>, but with what the body says
    /// (problem details or a text, the first <see cref="MsgPackHttpException.MaxBodyLength"/> bytes): <c>await response.EnsureSuccessStatusCodeAsync()</c> after <c>PostAsMsgPackAsync</c>.
    /// The body has been read then.
    /// </summary>
    /// <returns>The response</returns>
    /// <exception cref="MsgPackHttpException">The status code is not a success</exception>
    public static async Task<HttpResponseMessage> EnsureSuccessStatusCodeAsync(this HttpResponseMessage response, CancellationToken cancellationToken = default(CancellationToken))
    {
      if (response is null)
        throw new ArgumentNullException(nameof(response));
      if (!response.IsSuccessStatusCode)
        throw await MsgPackHttpException.FromResponseAsync(response, cancellationToken).ConfigureAwait(false);
      return response;
    }

    /// <summary>
    /// Reads the body as <typeparamref name="T"/> with the formatter's serializer, as <c>ReadAsAsync&lt;T&gt;</c> with only this formatter, but a body that is not MsgPack (e.g. JSON) throws a <see cref="NotSupportedException"/>.
    /// With <see cref="LtMsgPack.Http.LtMsgPackHttpOptions.ReportDifferences"/> the content has the differences afterwards (<see cref="ReadDifferencesExtensions.GetReadDifferences(HttpContent)"/>).
    /// </summary>
    /// <param name="formatter">Its serializer reads the body (the one the client's <see cref="LsMsgPackSchemaHandler"/> was created with)</param>
    /// <returns>The value, default for an empty body</returns>
    /// <exception cref="NotSupportedException">The body is not MsgPack (the message has the start of a text body, e.g. the problem details of an error: check the status code first,
    /// <see cref="EnsureSuccessStatusCodeAsync"/>), or a stream (read it with <see cref="MsgPackStreamExtensions.ReadAsMsgPackStreamAsync"/>)</exception>
    /// <exception cref="MsgPackException">The body is not valid or not a <typeparamref name="T"/></exception>
    public static async Task<T> ReadFromMsgPackAsync<T>(this HttpContent content, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken = default(CancellationToken))
    {
      if (content is null)
        throw new ArgumentNullException(nameof(content));
      if (formatter is null)
        throw new ArgumentNullException(nameof(formatter));
      if (content.Headers.ContentLength == 0)
        return default(T);

      string mediaType = content.Headers.ContentType?.MediaType;
      if (mediaType is null && (await content.ReadAsByteArrayAsync().ConfigureAwait(false)).Length == 0)
        return default(T); // 204 No Content has no Content-Type, and may have no Content-Length either
      if (!MsgPackMediaTypes.IsSupported(mediaType))
        throw await NotMsgPackAsync(content, mediaType, cancellationToken).ConfigureAwait(false);
      if (MsgPackMediaTypes.IsStream(content.Headers.ContentType.ToString()))
        throw new NotSupportedException("The response is a stream (" + MsgPackMediaTypes.StreamParameter + "), read it with ReadAsMsgPackStreamAsync or ReadFromMsgPackAsAsyncEnumerable.");

      Stream body = await content.ReadAsStreamAsync().ConfigureAwait(false);
      object value = await formatter.ReadFromStreamAsync(typeof(T), body, content, null, cancellationToken).ConfigureAwait(false);
      return value is T typed ? typed : default(T); // the formatter throws for a value of another type, this is null
    }

    private static async Task<T> GetAsync<T>(HttpClient client, HttpRequestMessage request, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken)
    {
      using (request)
      {
        if (client is null)
          throw new ArgumentNullException(nameof(client));
        if (formatter is null)
          throw new ArgumentNullException(nameof(formatter));
        AddAccept(client, request);
        // Headers only: the formatter copies the body once, instead of HttpClient buffering it first (the schema handler buffers application/x-lsmsgpack to learn its schema)
        using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
          await response.EnsureSuccessStatusCodeAsync(cancellationToken).ConfigureAwait(false);
          if (response.Content is null) // .NET Framework, without a body
            return default(T);
          return await ReadFromMsgPackAsync<T>(response.Content, formatter, cancellationToken).ConfigureAwait(false);
        }
      }
    }

    /// <summary>
    /// A body that is not MsgPack, with the start of its text: usually an error (problem details) whose status code was not checked.
    /// </summary>
    internal static async Task<NotSupportedException> NotMsgPackAsync(HttpContent content, string mediaType, CancellationToken cancellationToken)
    {
      string message = $"The response is {mediaType ?? "without a Content-Type"}, not MsgPack.";
      string text = await MsgPackHttpException.ReadExcerptAsync(content, cancellationToken).ConfigureAwait(false);
      return new NotSupportedException(text is null ? message : message + " It starts with: " + text);
    }

    private static async Task<HttpResponseMessage> SendAsync<T>(HttpClient client, HttpRequestMessage request, T value, LsMsgPackMediaTypeFormatter formatter, CancellationToken cancellationToken)
    {
      using (request)
      {
        if (client is null)
          throw new ArgumentNullException(nameof(client));
        if (formatter is null)
          throw new ArgumentNullException(nameof(formatter));
        request.Content = new ObjectContent<T>(value, formatter, MsgPackMediaTypes.XLsMsgPack);
        AddAccept(client, request);
        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
      }
    }

    private static void AddAccept(HttpClient client, HttpRequestMessage request)
    {
      if (request.Headers.Accept.Count == 0 && client.DefaultRequestHeaders.Accept.Count == 0)
        request.Headers.TryAddWithoutValidation("Accept", Accept);
    }
  }
}
