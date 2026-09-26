using LsMsgPack;
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
  /// Reads and writes application/msgpack, application/x-msgpack (plain MsgPack) and application/x-lsmsgpack (using the given settings).
  /// <para>Server side (ASP.NET Web API 2): <c>config.Formatters.Add(new LsMsgPackMediaTypeFormatter());</c></para>
  /// <para>Client side (HttpClient): <c>new ObjectContent&lt;T&gt;(value, formatter, LsMsgPackMediaTypes.XLsMsgPack)</c> and <c>response.Content.ReadAsAsync&lt;T&gt;(new[] { formatter })</c></para>
  /// </summary>
  public class LsMsgPackMediaTypeFormatter : MediaTypeFormatter
  {
    private readonly MsgPackSettings Settings;
    private readonly MsgPackSettings PlainSettings;

    public LsMsgPackMediaTypeFormatter() : this(new MsgPackSettings()) { }

    /// <param name="settings">Used for application/x-lsmsgpack, a copy is taken so later changes have no effect.</param>
    public LsMsgPackMediaTypeFormatter(MsgPackSettings settings)
    {
      Settings = (settings ?? new MsgPackSettings()).Clone();
      PlainSettings = LsMsgPackMediaTypes.ToPlain(Settings);
      foreach (string mediaType in LsMsgPackMediaTypes.All)
        SupportedMediaTypes.Add(new MediaTypeHeaderValue(mediaType));
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

    public override Task<object> ReadFromStreamAsync(Type type, Stream readStream, HttpContent content, IFormatterLogger formatterLogger)
    {
      return ReadFromStreamAsync(type, readStream, content, formatterLogger, CancellationToken.None);
    }

    public override async Task<object> ReadFromStreamAsync(Type type, Stream readStream, HttpContent content, IFormatterLogger formatterLogger, CancellationToken cancellationToken)
    {
      // Buffer the body asynchronously, the deserializer reads synchronously.
      MemoryStream body = new MemoryStream();
      await readStream.CopyToAsync(body, 81920, cancellationToken).ConfigureAwait(false);
      if (body.Length == 0)
        return GetDefaultValueForType(type);
      body.Position = 0;

      // A copy per request, since deserializing may flag errors on the settings (KEEPTRACK builds)
      MsgPackSettings settings = (LsMsgPackMediaTypes.IsLsMsgPack(content?.Headers.ContentType) ? Settings : PlainSettings).Clone();
      try
      {
        object model = MsgPackSerializer.Deserialize(type, body, settings);
        if (!(model is null) && !type.IsInstanceOfType(model)) // The deserializer passes through values it cannot convert (eg. a string where a map was expected)
          throw new MsgPackException("The data could not be deserialized as " + type.Name + ", it contains a " + model.GetType().Name + ".");
        return model;
      }
      catch (Exception ex) when (!(formatterLogger is null) && !(ex is OperationCanceledException))
      {
        // Server side: report it in the ModelState (like the JSON formatter does), the body is fully buffered so anything thrown here is caused by the content
        formatterLogger.LogError(string.Empty, ex);
        return GetDefaultValueForType(type);
      }
    }

    public override Task WriteToStreamAsync(Type type, object value, Stream writeStream, HttpContent content, TransportContext transportContext)
    {
      return WriteToStreamAsync(type, value, writeStream, content, transportContext, CancellationToken.None);
    }

    public override Task WriteToStreamAsync(Type type, object value, Stream writeStream, HttpContent content, TransportContext transportContext, CancellationToken cancellationToken)
    {
      MsgPackSettings settings = LsMsgPackMediaTypes.IsLsMsgPack(content?.Headers.ContentType) ? Settings : PlainSettings;

      // Serialize to a buffer and write it asynchronously
      byte[] buffer = MsgPackSerializer.Serialize(value, type, settings);
      return writeStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
    }
  }
}
