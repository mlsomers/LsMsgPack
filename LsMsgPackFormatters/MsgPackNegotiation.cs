#if NET8_0_OR_GREATER
using LsMsgPack;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Chooses the format of a minimal API response as MVC chooses between the JSON formatter and the MsgPack output formatter (added after it).
  /// </summary>
  internal static class MsgPackNegotiation
  {
    /// <summary>
    /// The MsgPack media type the Accept header asks for (as listed in <see cref="MsgPackMediaTypes.All"/>), null for JSON: when the header does not list MsgPack,
    /// lists <c>*/*</c> (MVC then ignores it, unless RespectBrowserAcceptHeader) or prefers a type JSON matches (by quality, then by position).
    /// </summary>
    internal static string Choose(StringValues accept)
    {
      bool msgPack = false;
      for (int t = 0; t < accept.Count; t++)
        msgPack |= accept[t] != null && accept[t].IndexOf("msgpack", StringComparison.OrdinalIgnoreCase) >= 0;
      if (!msgPack || !MediaTypeHeaderValue.TryParseList(accept, out IList<MediaTypeHeaderValue> ranges))
        return null; // the usual JSON request: one scan, no parsing

      bool found = false;
      string best = null; // null: JSON
      double bestQuality = 0;
      for (int r = 0; r < ranges.Count; r++)
      {
        MediaTypeHeaderValue range = ranges[r];
        if (range.MatchesAllTypes)
          return null;
        double quality = range.Quality ?? 1;
        if (quality <= 0 || (found && quality <= bestQuality))
          continue; // an earlier range of the same quality wins
        string mediaType = MsgPackType(range.MediaType);
        if (mediaType != null || ServedByJson(range))
        {
          found = true;
          best = mediaType;
          bestQuality = quality;
        }
      }
      return best;
    }

    private static string MsgPackType(StringSegment mediaType)
    {
      for (int t = 0; t < MsgPackMediaTypes.All.Count; t++)
        if (mediaType.Equals(MsgPackMediaTypes.All[t], StringComparison.OrdinalIgnoreCase))
          return MsgPackMediaTypes.All[t];
      return null;
    }

    /// <summary>
    /// The media types of MVC's JSON formatter: application/json, text/json, application/*+json, and the ranges containing them (application/*, text/*).
    /// </summary>
    private static bool ServedByJson(MediaTypeHeaderValue range)
    {
      StringSegment type = range.Type;
      StringSegment subType = range.SubType;
      if (type.Equals("application", StringComparison.OrdinalIgnoreCase))
        return subType.Equals("*", StringComparison.Ordinal) || subType.Equals("json", StringComparison.OrdinalIgnoreCase) || range.Suffix.Equals("json", StringComparison.OrdinalIgnoreCase);
      if (type.Equals("text", StringComparison.OrdinalIgnoreCase))
        return subType.Equals("*", StringComparison.Ordinal) || subType.Equals("json", StringComparison.OrdinalIgnoreCase);
      return false;
    }
  }
}
#endif
