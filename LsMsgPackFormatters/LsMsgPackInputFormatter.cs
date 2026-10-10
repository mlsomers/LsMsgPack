using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
#if NETCOREAPP3_0_OR_GREATER
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
#endif

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Reads request bodies with the Content-Type application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack, with LtMsgPack (see <see cref="LtMsgPackHttpOptions"/>).
  /// </summary>
  public class LsMsgPackInputFormatter : InputFormatter
  {
    private readonly LtMsgPackHttpSerializer Serializer;

    public LsMsgPackInputFormatter() : this(new LtMsgPackHttpOptions()) { }

    /// <param name="options">Read once, later changes have no effect.</param>
    public LsMsgPackInputFormatter(LtMsgPackHttpOptions options) : this(new LtMsgPackHttpSerializer(options ?? new LtMsgPackHttpOptions())) { }

    /// <param name="serializer">Share it with the output formatter (one schema store).</param>
    public LsMsgPackInputFormatter(LtMsgPackHttpSerializer serializer)
    {
      Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
      for (int t = 0; t < MsgPackMediaTypes.All.Count; t++) // the first one is the default
        SupportedMediaTypes.Add(MsgPackMediaTypes.All[t]);
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
#if NETCOREAPP3_0_OR_GREATER
      // Buffer the body asynchronously in the pipe of the request, the deserializer reads synchronously which ASP.NET Core does not allow on the request stream
      PipeReader reader = context.HttpContext.Request.BodyReader;
      ReadResult read = await reader.ReadAsync(context.HttpContext.RequestAborted);
      while (!read.IsCompleted && !read.IsCanceled)
      {
        reader.AdvanceTo(read.Buffer.Start, read.Buffer.End); // nothing consumed yet, wait for the rest
        read = await reader.ReadAsync(context.HttpContext.RequestAborted);
      }

      ReadOnlySequence<byte> body = read.Buffer;
      byte[] rented = null;
      try
      {
        if (body.Length == 0)
          return context.TreatEmptyInputAsDefaultValue ? InputFormatterResult.Success(GetDefaultValueForType(context.ModelType)) : InputFormatterResult.NoValue();

        // Read in place when the body is in one buffer (LtMsgPack copies what it keeps), otherwise from a pooled copy
        if (!body.IsSingleSegment || !MemoryMarshal.TryGetArray(body.First, out ArraySegment<byte> data))
        {
          rented = ArrayPool<byte>.Shared.Rent(checked((int)body.Length));
          body.CopyTo(rented);
          data = new ArraySegment<byte>(rented, 0, (int)body.Length);
        }
        return Deserialize(context, data.Array, data.Offset, data.Count);
      }
      finally
      {
        if (rented != null)
          ArrayPool<byte>.Shared.Return(rented);
        reader.AdvanceTo(body.End);
      }
#else
      // Buffer the body asynchronously, the deserializer reads synchronously which ASP.NET Core does not allow on the request stream.
      long? contentLength = context.HttpContext.Request.ContentLength;
      MemoryStream body = contentLength > 0 && contentLength <= MaxPresized ? new MemoryStream((int)contentLength) : new MemoryStream();
      await context.HttpContext.Request.Body.CopyToAsync(body, 81920, context.HttpContext.RequestAborted);
      if (body.Length == 0)
        return context.TreatEmptyInputAsDefaultValue ? InputFormatterResult.Success(GetDefaultValueForType(context.ModelType)) : InputFormatterResult.NoValue();

      return Deserialize(context, body.GetBuffer(), 0, (int)body.Length);
#endif
    }

#if !NETCOREAPP3_0_OR_GREATER
    /// <summary>
    /// The buffer is sized by the Content-Length up to this size, larger bodies grow it as they arrive (the header is not trusted with a large allocation).
    /// </summary>
    private const int MaxPresized = 1024 * 1024;
#endif

    private InputFormatterResult Deserialize(InputFormatterContext context, byte[] data, int offset, int count)
    {
      try
      {
        object model = Serializer.DeserializeBody(context.ModelType, data, offset, count, context.HttpContext.Request.ContentType, out ReadDifferences differences);
        if (differences != null)
          Report(context, differences);
        if (!(model is null) && !context.ModelType.IsInstanceOfType(model)) // The deserializer passes through values it cannot convert (eg. a string where a map was expected)
          return Fail(context, new InputFormatterException("The request body could not be deserialized as " + context.ModelType.Name + "."));
        return InputFormatterResult.Success(model);
      }
      catch (Exception ex) when (Serializer.ReportsDifferences && ReportFailure(context, ex)) // never true: reports the differences found until the exception
      {
        throw;
      }
      catch (MsgPackException ex)
      {
        return Fail(context, new InputFormatterException(ex.Message, ex));
      }
      catch (Exception ex) when (!(ex is OperationCanceledException))
      {
        // The body is fully buffered, so anything thrown here is caused by the content (truncated data, unexpected types etc.)
        return Fail(context, new InputFormatterException("The request body could not be deserialized as " + context.ModelType.Name + ".", ex));
      }
    }

    /// <summary>
    /// The differences between the body and the classes (<see cref="LtMsgPackHttpOptions.ReportDifferences"/>): in the features of the request (<see cref="ReadDifferencesExtensions.GetReadDifferences"/>)
    /// and logged, as a warning with <see cref="LtMsgPackHttpOptions.LogDifferencesAsWarning"/>, otherwise at the Debug level (clients sending extra properties would fill the logs).
    /// </summary>
    private void Report(InputFormatterContext context, ReadDifferences differences)
    {
      context.HttpContext.Features.Set(differences);
      ILogger logger = context.HttpContext.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger<LsMsgPackInputFormatter>();
      LogLevel level = Serializer.LogsDifferencesAsWarning ? LogLevel.Warning : LogLevel.Debug;
      if (logger != null && logger.IsEnabled(level)) // the report walks the objects that were read
      {
        string sanitizedMethod = (context.HttpContext.Request.Method ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        string sanitizedPath = context.HttpContext.Request.Path.ToString().Replace("\r", string.Empty).Replace("\n", string.Empty);
        logger.Log(level, "The request body of {Method} {Path} did not match {Type}: {Differences}", sanitizedMethod, sanitizedPath, context.ModelType.Name, differences.GenerateReport());
      }
    }

    /// <returns>False (an exception filter)</returns>
    private bool ReportFailure(InputFormatterContext context, Exception ex)
    {
      ReadDifferences differences = LtMsgPackHttpSerializer.DifferencesOf(ex);
      if (differences != null)
        Report(context, differences);
      return false;
    }

    private static InputFormatterResult Fail(InputFormatterContext context, InputFormatterException exception)
    {
      context.ModelState.TryAddModelError(context.ModelName, exception, context.Metadata);
      return InputFormatterResult.Failure();
    }
  }
}
