using LsMsgPack;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  public class LsMsgPackInputFormatter : InputFormatter
  {
    private MsgPackSettings Settings;

    public LsMsgPackInputFormatter(MsgPackSettings settings)
    {
      Settings = settings;
    }

    public LsMsgPackInputFormatter() {
      SupportedMediaTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/msgpack"));
      SupportedMediaTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/x-msgpack"));
      SupportedMediaTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/x-lsmsgpack"));
    }

    public override bool CanRead(InputFormatterContext context)
    {
      return true; // Optimistic :-)
    }

    public override Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
      return Task.FromResult(MsgPackSerializer.Deserialize<InputFormatterResult>(context.HttpContext.Request.Body, Settings));
    }
  }
}
