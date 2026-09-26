using LsMsgPack;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Writes responses for Accept application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack (using the given settings).
  /// </summary>
  public class LsMsgPackOutputFormatter : OutputFormatter
  {
    private readonly MsgPackSettings Settings;
    private readonly MsgPackSettings PlainSettings;

    public LsMsgPackOutputFormatter() : this(new MsgPackSettings()) { }

    /// <param name="settings">Used for application/x-lsmsgpack, a copy is taken so later changes have no effect.</param>
    public LsMsgPackOutputFormatter(MsgPackSettings settings)
    {
      Settings = (settings ?? new MsgPackSettings()).Clone();
      PlainSettings = MsgPackMediaTypes.ToPlain(Settings);
      foreach (string mediaType in MsgPackMediaTypes.All)
        SupportedMediaTypes.Add(mediaType);
    }

    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
    {
      MsgPackSettings settings = MsgPackMediaTypes.IsLsMsgPack(context.ContentType.Value) ? Settings : PlainSettings;

      // Serialize to a buffer and write it asynchronously, ASP.NET Core does not allow synchronous writes to the response stream.
      byte[] buffer = MsgPackSerializer.Serialize(context.Object, context.ObjectType, settings);
      context.HttpContext.Response.ContentLength = buffer.Length;
      await context.HttpContext.Response.Body.WriteAsync(buffer, 0, buffer.Length, context.HttpContext.RequestAborted);
    }
  }
}
