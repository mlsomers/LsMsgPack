using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace LsMsgPackMvcTests
{
  public class FakeRequest : HttpRequestBase
  {
    private readonly MemoryStream body;
    private readonly string contentType;
    private readonly string[] acceptTypes;

    public FakeRequest(byte[] body, string contentType, params string[] acceptTypes)
    {
      this.body = new MemoryStream(body ?? new byte[0]);
      this.contentType = contentType;
      this.acceptTypes = acceptTypes.Length == 0 ? null : acceptTypes;
    }

    public override Stream InputStream => body;
    public override int ContentLength => (int)body.Length;
    public override string ContentType { get => contentType; set { } }
    public override string[] AcceptTypes => acceptTypes;
  }

  public class FakeResponse : HttpResponseBase
  {
    public readonly MemoryStream Body = new MemoryStream();

    public override Stream OutputStream => Body;
    public override string ContentType { get; set; }
  }

  public class FakeHttpContext : HttpContextBase
  {
    private readonly FakeRequest request;
    private readonly FakeResponse response = new FakeResponse();
    private readonly IDictionary items = new Dictionary<object, object>();

    public FakeHttpContext(FakeRequest request)
    {
      this.request = request;
    }

    public override HttpRequestBase Request => request;
    public override HttpResponseBase Response => response;
    public override IDictionary Items => items;

    public byte[] ResponseBody => response.Body.ToArray();
    public string ResponseContentType => response.ContentType;
  }

  public class FakeController : Controller { }

  public static class Fake
  {
    public static ControllerContext Context(FakeRequest request)
    {
      return new ControllerContext(new FakeHttpContext(request), new RouteData(), new FakeController());
    }
  }
}
