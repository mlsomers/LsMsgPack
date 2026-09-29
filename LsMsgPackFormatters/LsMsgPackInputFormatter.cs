using LsMsgPack;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using System;
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

    public LsMsgPackInputFormatter() : this(new LtMsgPackHttpOptions()) { }

    /// <param name="options">Read once, later changes have no effect.</param>
    public LsMsgPackInputFormatter(LtMsgPackHttpOptions options) : this(new LtMsgPackHttpSerializer(options ?? new LtMsgPackHttpOptions())) { }

    /// <param name="serializer">Share it with the output formatter (one schema store).</param>
    public LsMsgPackInputFormatter(LtMsgPackHttpSerializer serializer)
    {
      Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
      foreach (string mediaType in MsgPackMediaTypes.All)
        SupportedMediaTypes.Add(mediaType);
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
      // Buffer the body asynchronously, the deserializer reads synchronously which ASP.NET Core does not allow on the request stream.
      MemoryStream body = new MemoryStream();
      await context.HttpContext.Request.Body.CopyToAsync(body, 81920, context.HttpContext.RequestAborted);
      if (body.Length == 0)
        return context.TreatEmptyInputAsDefaultValue ? InputFormatterResult.Success(GetDefaultValueForType(context.ModelType)) : InputFormatterResult.NoValue();

      try
      {
        object model = Serializer.Deserialize(context.ModelType, body.GetBuffer(), 0, (int)body.Length, context.HttpContext.Request.ContentType);
        if (!(model is null) && !context.ModelType.IsInstanceOfType(model)) // The deserializer passes through values it cannot convert (eg. a string where a map was expected)
          return Fail(context, new InputFormatterException("The request body could not be deserialized as " + context.ModelType.Name + "."));
        return InputFormatterResult.Success(model);
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

    private static InputFormatterResult Fail(InputFormatterContext context, InputFormatterException exception)
    {
      context.ModelState.TryAddModelError(context.ModelName, exception, context.Metadata);
      return InputFormatterResult.Failure();
    }
  }
}
