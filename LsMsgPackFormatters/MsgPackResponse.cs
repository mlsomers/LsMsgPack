using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Writes a MsgPack response: what the output formatter and the minimal API result (<c>MsgPackResult&lt;T&gt;</c>) share.
  /// </summary>
  internal static class MsgPackResponse
  {
    /// <param name="mediaType">The Content-Type of the response (set by the caller), one of <see cref="MsgPackMediaTypes.All"/></param>
    /// <param name="varyAccept">The caller chose the format by the Accept header: <c>Vary: Accept</c> also without version 2 of the schema</param>
    internal static async Task WriteAsync(HttpContext context, LtMsgPackHttpSerializer serializer, object value, Type declaredType, string mediaType, bool varyAccept)
    {
      bool lsMsgPack = MsgPackMediaTypes.IsLsMsgPack(mediaType);
      bool negotiated = serializer.NegotiatesSchemas && lsMsgPack;
      bool describes = serializer.DescribesSchemas && lsMsgPack;
      HttpRequest request = context.Request;
      string clientSchemas = negotiated ? request.Headers[LtMsgPackHttpSerializer.SchemasHeader].ToString() : null;
      bool described = describes && MsgPackMediaTypes.AcceptsDescribedSchema(request.Headers["Accept"].ToString());

      // Serialize to a buffer and write it asynchronously, ASP.NET Core does not allow synchronous writes to the response stream.
      MsgPackPayload payload = serializer.Serialize(value, declaredType, mediaType, clientSchemas, described);

      HttpResponse response = context.Response;
      bool accept = describes || varyAccept; // version 2 of the schema depends on the Accept header
      if (negotiated || accept)
        response.Headers.Append("Vary", Vary(negotiated, accept));
      if (negotiated && !(payload.SchemaId is null))
        response.Headers[LtMsgPackHttpSerializer.SchemaHeader] = payload.SchemaId;
      response.ContentLength = payload.Length;
#if NETCOREAPP3_0_OR_GREATER
      System.IO.Pipelines.PipeWriter writer = response.BodyWriter; // copied into the buffers of the response, one flush
      if (payload.Schema.Count > 0)
        System.Buffers.BuffersExtensions.Write(writer, new ReadOnlySpan<byte>(payload.Schema.Array, payload.Schema.Offset, payload.Schema.Count));
      System.Buffers.BuffersExtensions.Write(writer, new ReadOnlySpan<byte>(payload.Body.Array, payload.Body.Offset, payload.Body.Count));
      await writer.FlushAsync(context.RequestAborted);
#else
      await payload.WriteToAsync(response.Body, context.RequestAborted);
#endif
    }

    private const string VarySchemasAndAccept = LtMsgPackHttpSerializer.SchemasHeader + ", Accept";

    private static string Vary(bool negotiated, bool accept)
    {
      if (!negotiated)
        return "Accept";
      return accept ? VarySchemasAndAccept : LtMsgPackHttpSerializer.SchemasHeader;
    }
  }
}
