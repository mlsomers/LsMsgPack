using LsMsgPack;
using LtMsgPack.Http;
using System.Web;

namespace LsMsgPackMvc
{
  public static class ReadDifferencesExtensions
  {
    private const string ItemsKey = ReadDifferences.ExceptionDataKey;

    internal static void Set(HttpContextBase context, ReadDifferences differences)
    {
      if (context?.Items != null)
        context.Items[ItemsKey] = differences;
    }

    /// <summary>
    /// What did not match between the request body and the classes it was read into by <see cref="LsMsgPackModelBinder"/> (with <see cref="LtMsgPackHttpOptions.ReportDifferences"/>),
    /// null when it matched or was not read by it. Also when reading failed: the differences found until the error (without paths).
    /// </summary>
    public static ReadDifferences GetReadDifferences(this HttpContextBase context)
    {
      return context?.Items?[ItemsKey] as ReadDifferences;
    }
  }
}
