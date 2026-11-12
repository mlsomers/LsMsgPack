using LsMsgPack;
using LsMsgPack.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Writes responses for Accept application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack, with LsMsgPack (see <see cref="LsMsgPackHttpOptions"/>).
  /// <para>application/x-lsmsgpack refers to the schema instead of sending it when the request says the client holds it (<see cref="LsMsgPackHttpSerializer"/>).</para>
  /// </summary>
  public class LsMsgPackOutputFormatter : OutputFormatter
  {
    internal readonly LsMsgPackHttpSerializer Serializer;

#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode(LsMsgPackFormatter.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackFormatter.RequiresDynamicCodeMessage)]
#endif
    public LsMsgPackOutputFormatter() : this(new LsMsgPackHttpOptions()) { }

    /// <param name="options">Read once, later changes have no effect.</param>
#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode(LsMsgPackFormatter.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackFormatter.RequiresDynamicCodeMessage)]
#endif
    public LsMsgPackOutputFormatter(LsMsgPackHttpOptions options) : this(new LsMsgPackHttpSerializer(options ?? new LsMsgPackHttpOptions())) { }

    /// <param name="serializer">Share it with the input formatter (one schema store).</param>
    public LsMsgPackOutputFormatter(LsMsgPackHttpSerializer serializer)
    {
      Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
      for (int t = 0; t < MsgPackMediaTypes.All.Count; t++) // the first one is the default
        SupportedMediaTypes.Add(MsgPackMediaTypes.All[t]);
    }

    public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
    {
      return MsgPackResponse.WriteAsync(context.HttpContext, Serializer, context.Object, context.ObjectType, context.ContentType.Value, false);
    }
  }
}
