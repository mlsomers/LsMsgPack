using LsMsgPack;
using System;
using System.Net.Http.Headers;

namespace LsMsgPackWebApiFormatters
{
  /// <summary>
  /// The media types handled by the <see cref="LsMsgPackMediaTypeFormatter"/> and the settings used for each of them.
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

    internal static bool IsLsMsgPack(MediaTypeHeaderValue contentType)
    {
      return string.Equals(contentType?.MediaType, XLsMsgPack, StringComparison.OrdinalIgnoreCase);
    }
  }
}
