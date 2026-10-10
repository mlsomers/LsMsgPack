using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using System;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Writes responses for Accept application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack, with LtMsgPack (see <see cref="LtMsgPackHttpOptions"/>).
  /// <para>application/x-lsmsgpack refers to the schema instead of sending it when the request says the client holds it (<see cref="LtMsgPackHttpSerializer"/>).</para>
  /// </summary>
  public class LsMsgPackOutputFormatter : OutputFormatter
  {
    private readonly LtMsgPackHttpSerializer Serializer;

    public LsMsgPackOutputFormatter() : this(new LtMsgPackHttpOptions()) { }

    /// <param name="options">Read once, later changes have no effect.</param>
    public LsMsgPackOutputFormatter(LtMsgPackHttpOptions options) : this(new LtMsgPackHttpSerializer(options ?? new LtMsgPackHttpOptions())) { }

    /// <param name="serializer">Share it with the input formatter (one schema store).</param>
    public LsMsgPackOutputFormatter(LtMsgPackHttpSerializer serializer)
    {
      Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
      for (int t = 0; t < MsgPackMediaTypes.All.Count; t++) // the first one is the default
        SupportedMediaTypes.Add(MsgPackMediaTypes.All[t]);
    }

    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
    {
      string mediaType = context.ContentType.Value;
      bool negotiated = Serializer.NegotiatesSchemas && MsgPackMediaTypes.IsLsMsgPack(mediaType);
      string clientSchemas = negotiated ? context.HttpContext.Request.Headers[LtMsgPackHttpSerializer.SchemasHeader].ToString() : null;

      // Serialize to a buffer and write it asynchronously, ASP.NET Core does not allow synchronous writes to the response stream.
      MsgPackPayload payload = Serializer.Serialize(context.Object, context.ObjectType, mediaType, clientSchemas);

      HttpResponse response = context.HttpContext.Response;
      if (negotiated)
      {
        response.Headers.Append("Vary", LtMsgPackHttpSerializer.SchemasHeader);
        if (!(payload.SchemaId is null))
          response.Headers[LtMsgPackHttpSerializer.SchemaHeader] = payload.SchemaId;
      }
      response.ContentLength = payload.Length;
#if NETCOREAPP3_0_OR_GREATER
      System.IO.Pipelines.PipeWriter writer = response.BodyWriter; // copied into the buffers of the response, one flush
      if (payload.Schema.Count > 0)
        System.Buffers.BuffersExtensions.Write(writer, new ReadOnlySpan<byte>(payload.Schema.Array, payload.Schema.Offset, payload.Schema.Count));
      System.Buffers.BuffersExtensions.Write(writer, new ReadOnlySpan<byte>(payload.Body.Array, payload.Body.Offset, payload.Body.Count));
      await writer.FlushAsync(context.HttpContext.RequestAborted);
#else
      await payload.WriteToAsync(response.Body, context.HttpContext.RequestAborted);
#endif
    }
  }
}
