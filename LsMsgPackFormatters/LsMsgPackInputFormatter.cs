using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Reads request bodies with the Content-Type application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack, with LtMsgPack (see <see cref="LtMsgPackHttpOptions"/>).
  /// </summary>
  public class LsMsgPackInputFormatter : InputFormatter
  {
    private readonly LtMsgPackHttpSerializer Serializer;

#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode(LsMsgPackFormatter.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackFormatter.RequiresDynamicCodeMessage)]
#endif
    public LsMsgPackInputFormatter() : this(new LtMsgPackHttpOptions()) { }

    /// <param name="options">Read once, later changes have no effect.</param>
#if NET8_0_OR_GREATER
    [RequiresUnreferencedCode(LsMsgPackFormatter.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LsMsgPackFormatter.RequiresDynamicCodeMessage)]
#endif
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
      return await MsgPackRequest.ReadAsync(context.HttpContext, (formatter: this, context), static (s, data) => data.Count == 0
        ? (s.context.TreatEmptyInputAsDefaultValue ? InputFormatterResult.Success(s.formatter.GetDefaultValueForType(s.context.ModelType)) : InputFormatterResult.NoValue())
        : s.formatter.Deserialize(s.context, data.Array, data.Offset, data.Count));
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

    private void Report(InputFormatterContext context, ReadDifferences differences)
    {
      MsgPackRequest.Report(context.HttpContext, Serializer, context.ModelType, differences, LoggerCategory);
    }

    private bool ReportFailure(InputFormatterContext context, Exception ex)
    {
      return MsgPackRequest.ReportFailure(context.HttpContext, Serializer, context.ModelType, ex, LoggerCategory);
    }

    private static readonly string LoggerCategory = typeof(LsMsgPackInputFormatter).FullName;

    private static InputFormatterResult Fail(InputFormatterContext context, InputFormatterException exception)
    {
      context.ModelState.TryAddModelError(context.ModelName, exception, context.Metadata);
      return InputFormatterResult.Failure();
    }
  }
}
