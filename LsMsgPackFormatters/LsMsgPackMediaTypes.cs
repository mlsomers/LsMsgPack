using LsMsgPack;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// The media types handled by the LsMsgPack formatters and the settings used for each of them.
  /// </summary>
  public static class LsMsgPackMediaTypes
  {
    /// <summary>
    /// Plain MsgPack, readable by any MsgPack implementation (no indexed schema and no type id's).
    /// </summary>
    public const string MsgPack = "application/msgpack";

    /// <summary>
    /// Plain MsgPack, readable by any MsgPack implementation (no indexed schema and no type id's).
    /// </summary>
    public const string XMsgPack = "application/x-msgpack";

    /// <summary>
    /// MsgPack using the configured <see cref="MsgPackSettings"/> (by default an indexed schema followed by the body, with type id's where ambiguous). Only LsMsgPack is expected to understand this.
    /// </summary>
    public const string XLsMsgPack = "application/x-lsmsgpack";

    internal static readonly string[] All = { MsgPack, XMsgPack, XLsMsgPack };

    private static readonly MediaType LsMsgPackMediaType = new MediaType(XLsMsgPack);

    /// <summary>
    /// Returns a copy of the settings that produces plain MsgPack, so the result is not tied to LsMsgPack.
    /// </summary>
    internal static MsgPackSettings ToPlain(MsgPackSettings settings)
    {
      MsgPackSettings plain = settings.Clone();
      plain.UseInexedSchema = false;
      plain.AddTypeIdOptions = AddTypeIdOption.Never;
      return plain;
    }

    internal static bool IsLsMsgPack(string contentType)
    {
      return !string.IsNullOrEmpty(contentType) && new MediaType(contentType).IsSubsetOf(LsMsgPackMediaType);
    }

    internal static void AddTo(MediaTypeCollection supportedMediaTypes)
    {
      foreach (string mediaType in All)
        supportedMediaTypes.Add(MediaTypeHeaderValue.Parse(mediaType));
    }
  }
}
