using LsMsgPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormatters
{
  /// <summary>
  /// A response with a status code outside 200-299, with what its body says. ASP.NET Core answers errors with problem details (RFC 9457, application/problem+json, also when the request asks for MsgPack):
  /// <c>NotFound()</c>, <c>Problem()</c>, the automatic 400 of <c>[ApiController]</c> (<see cref="Errors"/>), and exceptions with <c>AddProblemDetails()</c>. Other servers and pages send a text,
  /// an action can also send a value of its own (<c>NotFound(error)</c>, MsgPack when the request asks for it: <see cref="ReadBody{T}"/>).
  /// Thrown by <see cref="MsgPackHttpClientExtensions.EnsureSuccessStatusCodeAsync"/>, <see cref="MsgPackHttpClientExtensions.GetFromMsgPackAsync{T}(HttpClient, string, LsMsgPackMediaTypeFormatter, CancellationToken)"/>
  /// and the stream methods (<see cref="MsgPackStreamExtensions"/>).
  /// </summary>
  public class MsgPackHttpException : HttpRequestException
  {
    /// <summary>
    /// The most of the body that is read: error bodies are small, this keeps a large error page from filling the memory.
    /// </summary>
    public const int MaxBodyLength = 64 * 1024;

    private const int MaxMessageText = 200;

    /// <param name="statusCode">The status code of the response</param>
    /// <param name="reasonPhrase">The reason phrase of the response (null for HTTP/2, which has none)</param>
    /// <param name="contentType">The Content-Type of the body, null without one</param>
    /// <param name="body">The body (the first <see cref="MaxBodyLength"/> bytes), null or empty without one</param>
    public MsgPackHttpException(HttpStatusCode statusCode, string reasonPhrase, string contentType, byte[] body)
      : this(statusCode, reasonPhrase, contentType, body ?? Array.Empty<byte>(), Describe(contentType, body ?? Array.Empty<byte>()))
    {
    }

    private MsgPackHttpException(HttpStatusCode statusCode, string reasonPhrase, string contentType, byte[] body, BodyInfo info)
#if NET5_0_OR_GREATER
      : base(MessageOf(statusCode, reasonPhrase, info), null, statusCode)
#else
      : base(MessageOf(statusCode, reasonPhrase, info))
#endif
    {
#if !NET5_0_OR_GREATER
      StatusCode = statusCode;
#endif
      ReasonPhrase = reasonPhrase;
      ContentType = contentType;
      Body = body;
      Text = info.Text;
      ProblemType = info.ProblemType;
      Title = info.Title;
      Detail = info.Detail;
      Instance = info.Instance;
      Errors = info.Errors;
    }

#if !NET5_0_OR_GREATER
    /// <summary>
    /// The status code of the response (on .NET 5 and later the one of <see cref="HttpRequestException"/>).
    /// </summary>
    public HttpStatusCode? StatusCode { get; }
#endif

    /// <summary>
    /// The reason phrase of the response, null for HTTP/2 (which has none).
    /// </summary>
    public string ReasonPhrase { get; }

    /// <summary>
    /// The Content-Type of the body (with its parameters), null without one.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// The body, at most its first <see cref="MaxBodyLength"/> bytes. Empty without one.
    /// </summary>
    public byte[] Body { get; }

    /// <summary>
    /// The body as text, for a text, JSON or XML Content-Type (or none), in its charset. Null for MsgPack and other binary bodies.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The <c>type</c> of problem details (a JSON body): a URI that identifies the kind of problem.
    /// </summary>
    public string ProblemType { get; }

    /// <summary>
    /// The <c>title</c> of problem details (a JSON body): a short summary of the kind of problem, e.g. "One or more validation errors occurred.".
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// The <c>detail</c> of problem details (a JSON body): what went wrong with this request.
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// The <c>instance</c> of problem details (a JSON body): a URI of this occurrence.
    /// </summary>
    public string Instance { get; }

    /// <summary>
    /// The <c>errors</c> of validation problem details (ASP.NET Core's automatic 400): the messages per property (the path in the request body). Empty when the body has none.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Reads a MsgPack body (e.g. <c>return NotFound(new MyError(...))</c> of an ASP.NET Core action, which the formatters write as MsgPack when the request asks for it) as <typeparamref name="T"/>.
    /// </summary>
    /// <param name="formatter">Its serializer reads the body (the one the client's <see cref="LsMsgPackSchemaHandler"/> was created with, which knows the schemas of the server)</param>
    /// <returns>The value, default for an empty body</returns>
    /// <exception cref="NotSupportedException">The body is not MsgPack</exception>
    /// <exception cref="MsgPackException">The body is not valid (or longer than <see cref="MaxBodyLength"/>), or not a <typeparamref name="T"/></exception>
    public T ReadBody<T>(LsMsgPackMediaTypeFormatter formatter)
    {
      if (formatter is null)
        throw new ArgumentNullException(nameof(formatter));
      if (Body.Length == 0)
        return default(T);
      string mediaType = MediaType(ContentType);
      if (!MsgPackMediaTypes.IsSupported(mediaType))
        throw new NotSupportedException($"The body is {mediaType ?? "without a Content-Type"}, not MsgPack.");
      object model = formatter.HttpSerializer.DeserializeBody(typeof(T), Body, 0, Body.Length, mediaType, out _);
      if (!(model is null) && !(model is T))
        throw new MsgPackException("The data could not be deserialized as " + typeof(T).Name + ", it contains a " + model.GetType().Name + ".");
      return model is T typed ? typed : default(T);
    }

    /// <summary>
    /// Reads the body of a response that is not successful (at most <see cref="MaxBodyLength"/> bytes) and creates the exception.
    /// </summary>
    internal static async Task<MsgPackHttpException> FromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
      HttpContent content = response.Content;
      byte[] body = Array.Empty<byte>();
      string contentType = null;
      if (!(content is null))
      {
        contentType = content.Headers.ContentType?.ToString();
        using (Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
          body = await ReadAtMostAsync(stream, MaxBodyLength, cancellationToken).ConfigureAwait(false);
      }
      return new MsgPackHttpException(response.StatusCode, response.ReasonPhrase, contentType, body);
    }

    /// <summary>
    /// The start of a text body (for the message of an exception), null for a binary or empty one.
    /// </summary>
    internal static async Task<string> ReadExcerptAsync(HttpContent content, CancellationToken cancellationToken)
    {
      string contentType = content.Headers.ContentType?.ToString();
      if (!IsText(MediaType(contentType)))
        return null;
      byte[] body;
      using (Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
        body = await ReadAtMostAsync(stream, MaxMessageText * 4, cancellationToken).ConfigureAwait(false); // UTF-8: up to 4 bytes a character
      return Excerpt(Decode(body, contentType));
    }

    private static string Excerpt(string text)
    {
      if (string.IsNullOrWhiteSpace(text))
        return null;
      text = text.Trim();
      return text.Length > MaxMessageText ? text.Substring(0, MaxMessageText) + "..." : text;
    }

    private static async Task<byte[]> ReadAtMostAsync(Stream stream, int max, CancellationToken cancellationToken)
    {
      byte[] buffer = new byte[4096];
      int length = 0;
      while (length < max)
      {
        if (length == buffer.Length)
          Array.Resize(ref buffer, Math.Min(buffer.Length * 2, max));
        int read = await stream.ReadAsync(buffer, length, buffer.Length - length, cancellationToken).ConfigureAwait(false);
        if (read == 0)
          break;
        length += read;
      }
      if (length < buffer.Length)
        Array.Resize(ref buffer, length);
      return buffer;
    }

    private sealed class BodyInfo
    {
      internal string Text;
      internal string ProblemType;
      internal string Title;
      internal string Detail;
      internal string Instance;
      internal IReadOnlyDictionary<string, string[]> Errors = NoErrors;
    }

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private static BodyInfo Describe(string contentType, byte[] body)
    {
      BodyInfo info = new BodyInfo();
      string mediaType = MediaType(contentType);
      if (body.Length == 0 || !IsText(mediaType))
        return info;
      info.Text = Decode(body, contentType);
      if (!(mediaType is null) && mediaType.EndsWith("json", StringComparison.Ordinal))
        ReadProblem(info);
      return info;
    }

    private static string MediaType(string contentType)
    {
      if (contentType is null)
        return null;
      int semicolon = contentType.IndexOf(';');
      return (semicolon < 0 ? contentType : contentType.Substring(0, semicolon)).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Text, JSON or XML, or a body without a Content-Type (HTTP lets the client guess, error pages are text).
    /// </summary>
    private static bool IsText(string mediaType)
    {
      return mediaType is null || mediaType.StartsWith("text/", StringComparison.Ordinal) || mediaType.EndsWith("json", StringComparison.Ordinal) || mediaType.EndsWith("xml", StringComparison.Ordinal);
    }

    private static string Decode(byte[] body, string contentType)
    {
      Encoding encoding = Encoding.UTF8;
      string charSet = null;
      try
      {
        charSet = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType ?? "text/plain").CharSet?.Trim('"');
      }
      catch (FormatException)
      {
      }
      if (!string.IsNullOrEmpty(charSet))
      {
        try
        {
          encoding = Encoding.GetEncoding(charSet);
        }
        catch (ArgumentException)
        {
        }
      }
      string text = encoding.GetString(body);
      return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
    }

    /// <summary>
    /// The members of problem details (RFC 9457) and ASP.NET Core's validation problem details, when the body is a JSON object.
    /// </summary>
    private static void ReadProblem(BodyInfo info)
    {
      JObject problem;
      try
      {
        using (JsonTextReader reader = new JsonTextReader(new StringReader(info.Text)) { DateParseHandling = DateParseHandling.None })
          problem = JToken.ReadFrom(reader) as JObject;
      }
      catch (JsonException)
      {
        return; // not JSON after all (or cut off at MaxBodyLength): the text stays
      }
      if (problem is null)
        return;
      info.ProblemType = StringOf(problem["type"]);
      info.Title = StringOf(problem["title"]);
      info.Detail = StringOf(problem["detail"]);
      info.Instance = StringOf(problem["instance"]);
      if (problem["errors"] is JObject errors)
      {
        Dictionary<string, string[]> byName = new Dictionary<string, string[]>();
        foreach (JProperty property in errors.Properties())
        {
          if (property.Value is JArray messages)
          {
            List<string> texts = new List<string>(messages.Count);
            for (int t = 0; t < messages.Count; t++)
            {
              string text = StringOf(messages[t]);
              if (!(text is null))
                texts.Add(text);
            }
            byName[property.Name] = texts.ToArray();
          }
          else if (StringOf(property.Value) is string single)
            byName[property.Name] = new[] { single };
        }
        info.Errors = byName;
      }
    }

    private static string StringOf(JToken token)
    {
      return token is JValue value && value.Type == JTokenType.String ? (string)value.Value : null;
    }

    private static string MessageOf(HttpStatusCode statusCode, string reasonPhrase, BodyInfo info)
    {
      // As EnsureSuccessStatusCode, then what the body says
      StringBuilder message = new StringBuilder("Response status code does not indicate success: ").Append((int)statusCode);
      if (!string.IsNullOrEmpty(reasonPhrase))
        message.Append(" (").Append(reasonPhrase).Append(')');
      message.Append('.');
      if (!(info.Title is null) || !(info.Detail is null) || info.Errors.Count > 0)
      {
        if (!(info.Title is null) && !string.Equals(info.Title, reasonPhrase, StringComparison.Ordinal))
          message.Append(' ').Append(info.Title);
        if (!(info.Detail is null))
          message.Append(' ').Append(info.Detail);
        foreach (KeyValuePair<string, string[]> error in info.Errors)
          message.Append(' ').Append(error.Key).Append(": ").Append(string.Join(" ", error.Value));
      }
      else if (Excerpt(info.Text) is string text)
        message.Append(' ').Append(text);
      return message.ToString();
    }
  }
}
