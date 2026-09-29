using System;
using System.Collections.Generic;

namespace LsMsgPack
{
  /// <summary>
  /// The media types (Content-Type / Accept) used by the web formatters, and the settings used for each of them.
  /// </summary>
  public static class MsgPackMediaTypes
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
    /// MsgPack using the configured settings (by default an indexed schema followed by the body, with type id's where ambiguous). Only LsMsgPack and LtMsgPack are expected to understand this.
    /// </summary>
    public const string XLsMsgPack = "application/x-lsmsgpack";

    /// <summary>
    /// All supported media types, the first one is the default.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[] { MsgPack, XMsgPack, XLsMsgPack };

    /// <summary>
    /// True if the Content-Type (parameters are ignored) is one of the supported media types.
    /// </summary>
    public static bool IsSupported(string contentType)
    {
      string mediaType = WithoutParameters(contentType);
      foreach (string supported in All)
        if (string.Equals(mediaType, supported, StringComparison.OrdinalIgnoreCase))
          return true;
      return false;
    }

    /// <summary>
    /// True if the Content-Type (parameters are ignored) is application/x-lsmsgpack, which uses the configured settings instead of plain MsgPack.
    /// </summary>
    public static bool IsLsMsgPack(string contentType)
    {
      return string.Equals(WithoutParameters(contentType), XLsMsgPack, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a copy of the settings that produces plain MsgPack (no indexed schema and no type id's), so the result is not tied to LsMsgPack.
    /// </summary>
    /// <param name="settings">MsgPackSettings (LsMsgPack) or LtMsgPackOptions (LtMsgPack)</param>
    public static TOptions ToPlain<TOptions>(TOptions settings) where TOptions : MsgPackOptions
    {
      if (settings is null)
        throw new ArgumentNullException(nameof(settings));
      TOptions plain = (TOptions)settings.CloneOptions();
      plain.UseInexedSchema = false;
      plain.WriteSchemaReference = false;
      plain.AddTypeIdOptions = AddTypeIdOption.Never;
      return plain;
    }

    private static string WithoutParameters(string contentType)
    {
      if (contentType is null)
        return null;
      int separator = contentType.IndexOf(';');
      return (separator < 0 ? contentType : contentType.Substring(0, separator)).Trim();
    }
  }
}
