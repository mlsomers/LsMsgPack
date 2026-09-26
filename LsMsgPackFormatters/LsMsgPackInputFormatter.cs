using LsMsgPack;
using Microsoft.AspNetCore.Mvc.Formatters;
using System;
using System.IO;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  /// <summary>
  /// Reads request bodies with the Content-Type application/msgpack, application/x-msgpack (plain MsgPack) or application/x-lsmsgpack (using the given settings).
  /// </summary>
  public class LsMsgPackInputFormatter : InputFormatter
  {
    private readonly MsgPackSettings Settings;
    private readonly MsgPackSettings PlainSettings;

    public LsMsgPackInputFormatter() : this(new MsgPackSettings()) { }

    /// <param name="settings">Used for application/x-lsmsgpack, a copy is taken so later changes have no effect.</param>
    public LsMsgPackInputFormatter(MsgPackSettings settings)
    {
      Settings = (settings ?? new MsgPackSettings()).Clone();
      PlainSettings = LsMsgPackMediaTypes.ToPlain(Settings);
      LsMsgPackMediaTypes.AddTo(SupportedMediaTypes);
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
      // Buffer the body asynchronously, the deserializer reads synchronously which ASP.NET Core does not allow on the request stream.
      MemoryStream body = new MemoryStream();
      await context.HttpContext.Request.Body.CopyToAsync(body, 81920, context.HttpContext.RequestAborted);
      if (body.Length == 0)
        return context.TreatEmptyInputAsDefaultValue ? InputFormatterResult.Success(GetDefaultValueForType(context.ModelType)) : InputFormatterResult.NoValue();
      body.Position = 0;

      // A copy per request, since deserializing may flag errors on the settings (KEEPTRACK builds)
      MsgPackSettings settings = (LsMsgPackMediaTypes.IsLsMsgPack(context.HttpContext.Request.ContentType) ? Settings : PlainSettings).Clone();
      try
      {
        object model = MsgPackSerializer.Deserialize(context.ModelType, body, settings);
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
