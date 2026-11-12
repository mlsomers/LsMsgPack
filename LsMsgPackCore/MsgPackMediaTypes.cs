using System;
using System.Collections.Generic;
using System.Globalization;

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
    /// MsgPack using the configured settings (by default an indexed schema followed by the body, with type id's where ambiguous). Only LsMsgPack and LsMsgPack are expected to understand this.
    /// </summary>
    public const string XLsMsgPack = "application/x-lsmsgpack";

    /// <summary>
    /// The parameter of <see cref="XLsMsgPack"/> in an Accept header that asks for version 2 of the schema (<see cref="SchemaVersion.Described"/>): the kind and default of each property and the type of the root,
    /// for clients without the classes (e.g. JavaScript). E.g. <c>Accept: application/x-lsmsgpack; schema=described</c>. The body is the same, .NET readers read either version.
    /// </summary>
    public const string DescribedSchemaParameter = "schema=described";

    /// <summary>
    /// The parameter of a MsgPack media type in an Accept header that says the client reads streams (docs/Streaming.md): values of unknown length (an array whose items are written as they come),
    /// several root values, a head followed by its body. E.g. <c>Accept: application/x-lsmsgpack; stream=unknown-length</c>: the ASP.NET Core formatters then write an <c>IAsyncEnumerable&lt;T&gt;</c>
    /// as an array of unknown length instead of reading it to the end first. A response written so has the parameter in its Content-Type. Readers of single payloads (the serializers) refuse such data.
    /// </summary>
    public const string StreamParameter = "stream=unknown-length";

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
      for (int t = All.Count - 1; t >= 0; t--)
        if (string.Equals(mediaType, All[t], StringComparison.OrdinalIgnoreCase))
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
    /// True if the Accept header lists <see cref="XLsMsgPack"/> with <see cref="DescribedSchemaParameter"/> (and not with q=0).
    /// </summary>
    /// <param name="accept">The Accept header of the request (several values joined by commas)</param>
    public static bool AcceptsDescribedSchema(string accept)
    {
      return Accepts(accept, XLsMsgPack, "schema", "described");
    }

    /// <summary>
    /// True if the Accept header lists the media type with <see cref="StreamParameter"/> (and not with q=0): the client reads a stream in a response of that media type.
    /// </summary>
    /// <param name="accept">The Accept header of the request (several values joined by commas)</param>
    /// <param name="mediaType">The media type of the response (parameters are ignored)</param>
    public static bool AcceptsStream(string accept, string mediaType)
    {
      return Accepts(accept, WithoutParameters(mediaType), "stream", "unknown-length");
    }

    /// <summary>
    /// True if the Content-Type has <see cref="StreamParameter"/>: the body is a stream (docs/Streaming.md), with values of unknown length, read it with MsgPackStreamReader (LsMsgPack) as it arrives.
    /// </summary>
    public static bool IsStream(string contentType)
    {
      return Accepts(contentType, WithoutParameters(contentType), "stream", "unknown-length");
    }

    private static bool Accepts(string accept, string mediaType, string parameter, string value)
    {
      if (accept is null || mediaType is null || accept.IndexOf(value, StringComparison.OrdinalIgnoreCase) < 0) // the usual case: one scan, no allocations
        return false;

      string[] ranges = accept.Split(',');
      for (int r = 0; r < ranges.Length; r++)
      {
        string[] parts = ranges[r].Split(';');
        if (!string.Equals(parts[0].Trim(), mediaType, StringComparison.OrdinalIgnoreCase))
          continue;

        bool found = false;
        bool refused = false;
        for (int p = 1; p < parts.Length; p++)
        {
          int equals = parts[p].IndexOf('=');
          if (equals < 0)
            continue;
          string name = parts[p].Substring(0, equals).Trim();
          string text = parts[p].Substring(equals + 1).Trim().Trim('"');
          if (string.Equals(name, parameter, StringComparison.OrdinalIgnoreCase))
            found = string.Equals(text, value, StringComparison.OrdinalIgnoreCase);
          else if (string.Equals(name, "q", StringComparison.OrdinalIgnoreCase))
            refused = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double q) && q <= 0;
        }
        if (found && !refused)
          return true;
      }
      return false;
    }

    /// <summary>
    /// Returns a copy of the settings that produces plain MsgPack (no indexed schema, no type id's, objects as maps keyed by property names), so the result is not tied to LsMsgPack.
    /// </summary>
    /// <param name="settings">MsgPackSettings (LsMsgPack) or LsMsgPackOptions (LsMsgPack)</param>
    public static TOptions ToPlain<TOptions>(TOptions settings) where TOptions : MsgPackOptions
    {
      if (settings is null)
        throw new ArgumentNullException(nameof(settings));
      TOptions plain = (TOptions)settings.CloneOptions();
      plain.UseInexedSchema = false;
      plain.WriteSchemaReference = false;
      plain.AddTypeIdOptions = AddTypeIdOption.Never;
      plain.ObjectLayout = ObjectLayout.Map; // without the schema an array can only be read in the writer's order
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
