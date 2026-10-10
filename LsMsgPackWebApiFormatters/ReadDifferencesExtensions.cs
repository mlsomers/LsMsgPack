using LsMsgPack;
using LtMsgPack.Http;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace LsMsgPackWebApiFormatters
{
  public static class ReadDifferencesExtensions
  {
    /// <summary>
    /// The differences per body that was read, kept as long as the content lives.
    /// </summary>
    private static readonly ConditionalWeakTable<HttpContent, ReadDifferences> Found = new ConditionalWeakTable<HttpContent, ReadDifferences>();

    internal static void Set(HttpContent content, ReadDifferences differences)
    {
      if (content is null)
        return;
      lock (Found) // no AddOrUpdate in .NET Standard 2.0
      {
        Found.Remove(content);
        Found.Add(content, differences);
      }
    }

    /// <summary>
    /// What did not match between the body and the classes it was read into by <see cref="LsMsgPackMediaTypeFormatter"/> (with <see cref="LtMsgPackHttpOptions.ReportDifferences"/>),
    /// null when it matched or was not read by it. Also when reading failed: the differences found until the error (without paths).
    /// </summary>
    public static ReadDifferences GetReadDifferences(this HttpContent content)
    {
      return content != null && Found.TryGetValue(content, out ReadDifferences differences) ? differences : null;
    }

    /// <summary>
    /// Server side: the differences of the request body (see <see cref="GetReadDifferences(HttpContent)"/>).
    /// </summary>
    public static ReadDifferences GetReadDifferences(this HttpRequestMessage request)
    {
      return request?.Content.GetReadDifferences();
    }

    /// <summary>
    /// Client side: the differences of the response body after <c>ReadAsAsync</c> (see <see cref="GetReadDifferences(HttpContent)"/>).
    /// </summary>
    public static ReadDifferences GetReadDifferences(this HttpResponseMessage response)
    {
      return response?.Content.GetReadDifferences();
    }
  }
}
