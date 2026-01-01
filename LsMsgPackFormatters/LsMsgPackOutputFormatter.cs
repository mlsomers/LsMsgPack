using LsMsgPack;
using Microsoft.AspNetCore.Mvc.Formatters;
using System;
using System.Threading.Tasks;

namespace LsMsgPackFormatters
{
  public class LsMsgPackOutputFormatter : OutputFormatter
  {
    private MsgPackSettings Settings;

    public LsMsgPackOutputFormatter(MsgPackSettings settings)
    {
      Settings = settings;
    }

    public LsMsgPackOutputFormatter() { 
      SupportedMediaTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/msgpack"));
      SupportedMediaTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/x-msgpack"));
      SupportedMediaTypes.Add(new Microsoft.Net.Http.Headers.MediaTypeHeaderValue("application/x-lsmsgpack"));
    }

    protected override bool CanWriteType(Type type)
    {
      return true; // Optimistic :-)
    }

    public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
    {
      MsgPackSerializer.Serialize(context.Object, context.HttpContext.Response.Body, Settings);
      return Task.CompletedTask;
    }
  }
}
