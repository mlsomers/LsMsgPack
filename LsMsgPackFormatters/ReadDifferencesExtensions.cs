using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Http;

namespace LsMsgPackFormatters
{
  public static class ReadDifferencesExtensions
  {
    /// <summary>
    /// What did not match between the request body and the classes it was read into (with <see cref="LtMsgPackHttpOptions.ReportDifferences"/>), null when it matched or was not read by the msgpack formatter.
    /// <para>Also when reading failed: the differences found until the error (without paths).</para>
    /// </summary>
    public static ReadDifferences GetReadDifferences(this HttpContext context)
    {
      return context?.Features.Get<ReadDifferences>();
    }
  }
}
