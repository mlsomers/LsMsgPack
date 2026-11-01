#if NET8_0_OR_GREATER
using LsMsgPack;
using LtMsgPack.Http;
using LtMsgPack.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// A response body that is a stream (docs/Streaming.md), written by the action or endpoint with a <see cref="MsgPackStreamWriter"/> while it is sent: values of unknown length, several root values,
  /// heads, objects with their schemas, an index. For data too large to buffer, or produced while it is written (the rows of a query). For a controller action and a minimal API endpoint.
  /// <para>The media type is the MsgPack one the Accept header asks for, application/x-lsmsgpack when it asks for none (there is no JSON), with <see cref="MsgPackMediaTypes.StreamParameter"/> in the Content-Type.
  /// The writer has the serializer of that media type (the output formatter's, or the one of <see cref="LsMsgPackFormatter.AddLsMsgPack(IServiceCollection, Action{LtMsgPackHttpOptions})"/>): with application/x-lsmsgpack and schema references
  /// each schema is inline the first time and referred to after that (<see cref="MsgPackStreamSchemas.PerStream"/>).</para>
  /// <para>The writer is asynchronous (it never writes to the response synchronously, which ASP.NET Core does not allow): write the values, and await <see cref="MsgPackStreamWriter.FlushIfFullAsync"/>
  /// (or <see cref="MsgPackStreamWriter.WriteObjectAsync{T}(T, CancellationToken)"/>) after each one or each row, which sends the buffer at <see cref="MsgPackStreamWriter.FlushThreshold"/> and waits while the client is slow,
  /// and <see cref="MsgPackStreamWriter.FlushAsync"/> to send what is there now (e.g. before waiting for more data). Without them the whole body is kept until the end.
  /// The writer is disposed after the callback (<see cref="MsgPackStreamWriter.DisposeAsync"/>: the index and the rest). An exception of the callback after the first flush aborts the response, so the client finds the data cut off.</para>
  /// </summary>
  public sealed class MsgPackStreamResult : IActionResult, IResult, IStatusCodeHttpResult, IEndpointMetadataProvider
  {
    private readonly Func<MsgPackStreamWriter, CancellationToken, Task> _write;
    private readonly MsgPackStreamIndexOptions _index;

    /// <param name="write">Writes the body (the token is <c>HttpContext.RequestAborted</c>)</param>
    /// <param name="index">Keep an index of the root values and write it at the end of the body (see <see cref="MsgPackStreamIndexOptions"/>), null: none</param>
    /// <param name="statusCode">null: 200</param>
    public MsgPackStreamResult(Func<MsgPackStreamWriter, CancellationToken, Task> write, MsgPackStreamIndexOptions index = null, int? statusCode = null)
    {
      _write = write ?? throw new ArgumentNullException(nameof(write));
      _index = index;
      StatusCode = statusCode;
    }

    /// <summary>
    /// The status code of the response (null: 200, or what the action set).
    /// </summary>
    public int? StatusCode { get; }

    public Task ExecuteResultAsync(ActionContext context)
    {
      if (context is null)
        throw new ArgumentNullException(nameof(context));
      HttpContext httpContext = context.HttpContext;
      return WriteAsync(httpContext, FormatterSerializer(httpContext) ?? httpContext.RequestServices?.GetService<LtMsgPackHttpSerializer>());
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
      if (httpContext is null)
        throw new ArgumentNullException(nameof(httpContext));
      return WriteAsync(httpContext, httpContext.RequestServices?.GetService<LtMsgPackHttpSerializer>() ?? FormatterSerializer(httpContext));
    }

    private static LtMsgPackHttpSerializer FormatterSerializer(HttpContext httpContext)
    {
      IList<Microsoft.AspNetCore.Mvc.Formatters.IOutputFormatter> formatters = httpContext.RequestServices?.GetService<IOptions<MvcOptions>>()?.Value.OutputFormatters;
      if (formatters is null)
        return null;
      for (int f = 0; f < formatters.Count; f++)
      {
        if (formatters[f] is LsMsgPackOutputFormatter formatter)
          return formatter.Serializer;
      }
      return null;
    }

    private async Task WriteAsync(HttpContext httpContext, LtMsgPackHttpSerializer serializer)
    {
      if (serializer is null)
        throw new InvalidOperationException("No LtMsgPackHttpSerializer: MsgPackStreamResult needs the formatters (AddLsMsgPackSerializerFormatters) or builder.Services.AddLsMsgPack().");

      HttpResponse response = httpContext.Response;
      if (StatusCode.HasValue)
        response.StatusCode = StatusCode.Value;
      string mediaType = MsgPackNegotiation.Choose(httpContext.Request.Headers.Accept) ?? MsgPackMediaTypes.XLsMsgPack;
      response.ContentType = string.Concat(mediaType, "; ", MsgPackMediaTypes.StreamParameter);
      response.Headers.Append("Vary", "Accept");
      response.ContentLength = null;

      MsgPackStreamWriter writer = serializer.CreateStreamWriter(response.Body, mediaType, _index, true);
      await _write(writer, httpContext.RequestAborted);
      await writer.DisposeAsync(); // the index and the rest of the buffer; not after an exception: an error response is still possible when nothing was sent
    }

    static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
      if (builder is null)
        throw new ArgumentNullException(nameof(builder));
      builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, null, new[] { MsgPackMediaTypes.XLsMsgPack, MsgPackMediaTypes.MsgPack, MsgPackMediaTypes.XMsgPack }));
    }
  }
}
#endif
