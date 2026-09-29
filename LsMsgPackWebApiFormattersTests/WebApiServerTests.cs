using LsMsgPack;
using LsMsgPackWebApiFormatters;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace LsMsgPackWebApiFormattersTests
{
  [RoutePrefix("webapi")]
  public class WebApiTestController : ApiController
  {
    [HttpPost, Route("echo")]
    public Order Echo([FromBody] Order order) => order;

    [HttpGet, Route("animal")]
    public Animal GetAnimal() => new Dog { Name = "Rex", Barks = 3 };

    [HttpGet, Route("order")]
    public Order GetOrder() => new Order { Id = 42, Customer = "Infotopie", Amounts = new[] { 1.5, 2.25 } };
  }

  /// <summary>
  /// The usual Web API 2 way of turning an invalid ModelState into a 400
  /// </summary>
  public class ValidateModelAttribute : ActionFilterAttribute
  {
    public override void OnActionExecuting(HttpActionContext actionContext)
    {
      if (!actionContext.ModelState.IsValid)
        actionContext.Response = actionContext.Request.CreateErrorResponse(HttpStatusCode.BadRequest, actionContext.ModelState);
    }
  }

  /// <summary>
  /// ASP.NET Web API 2 hosted in memory
  /// </summary>
  [TestFixture]
  public class WebApiServerTests
  {
    private static readonly Order SampleOrder = new Order { Id = 42, Customer = "Infotopie", Amounts = new[] { 1.5, 2.25 } };
    private static readonly LsMsgPackMediaTypeFormatter Formatter = new LsMsgPackMediaTypeFormatter();

    private HttpServer server;
    private HttpClient client;

    [OneTimeSetUp]
    public void StartServer()
    {
      HttpConfiguration config = new HttpConfiguration();
      config.MapHttpAttributeRoutes();
      config.Formatters.Add(new LsMsgPackMediaTypeFormatter());
      config.Filters.Add(new ValidateModelAttribute());
      config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;
      server = new HttpServer(config);
      client = new HttpClient(server) { BaseAddress = new Uri("http://localhost/") };
    }

    [OneTimeTearDown]
    public void StopServer()
    {
      client.Dispose();
      server.Dispose();
    }

    private static HttpRequestMessage Post(HttpContent content, string accept)
    {
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "webapi/echo") { Content = content };
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
      return request;
    }

    private static ByteArrayContent Bytes(byte[] body, string contentType)
    {
      ByteArrayContent content = new ByteArrayContent(body);
      content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
      return content;
    }

    [TestCase(MsgPackMediaTypes.MsgPack)]
    [TestCase(MsgPackMediaTypes.XMsgPack)]
    [TestCase(MsgPackMediaTypes.XLsMsgPack)]
    public async Task RoundTrips(string mediaType)
    {
      HttpResponseMessage response = await client.SendAsync(Post(new ObjectContent<Order>(SampleOrder, Formatter, mediaType), mediaType));

      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
      Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(mediaType));
      Order order = await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { Formatter });
      Assert.That(order.Id, Is.EqualTo(SampleOrder.Id));
      Assert.That(order.Customer, Is.EqualTo(SampleOrder.Customer));
      Assert.That(order.Amounts, Is.EqualTo(SampleOrder.Amounts));
    }

    [TestCase(MsgPackMediaTypes.MsgPack, false)]
    [TestCase(MsgPackMediaTypes.XLsMsgPack, true)]
    public async Task WireFormatFollowsMediaType(string mediaType, bool lsMsgPack)
    {
      HttpResponseMessage response = await client.SendAsync(Post(new ObjectContent<Order>(SampleOrder, Formatter, mediaType), mediaType));

      byte[] expected = lsMsgPack
        ? MsgPackSerializer.Serialize(SampleOrder, new MsgPackSettings())
        : MsgPackSerializer.Serialize(SampleOrder, new MsgPackSettings { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Never });
      Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(expected));
    }

    [Test]
    public async Task LsMsgPackAddsTypeIdForPolymorphicRoot()
    {
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "webapi/animal");
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
      HttpResponseMessage response = await client.SendAsync(request);

      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
      Animal animal = await response.Content.ReadAsAsync<Animal>(new MediaTypeFormatter[] { Formatter });
      Assert.That(animal, Is.TypeOf<Dog>());
      Assert.That(((Dog)animal).Barks, Is.EqualTo(3));
    }

    [TestCase(new byte[] { 0xC1 }, TestName = "MalformedInputGives400(never used type)")]
    [TestCase(new byte[] { 0x83, 0xA2, 0x49 }, TestName = "MalformedInputGives400(truncated)")]
    [TestCase(new byte[] { 0xA5, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, TestName = "MalformedInputGives400(string instead of map)")]
    public async Task MalformedInputGives400(byte[] body)
    {
      HttpResponseMessage response = await client.SendAsync(Post(Bytes(body, MsgPackMediaTypes.MsgPack), "application/json"));

      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task UnsupportedContentTypeGives415()
    {
      HttpResponseMessage response = await client.SendAsync(Post(Bytes(new byte[] { 0x68, 0x69 }, "text/plain"), MsgPackMediaTypes.MsgPack));

      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
    }

    [Test]
    public async Task JsonIsStillServed()
    {
      HttpResponseMessage response = await client.SendAsync(Post(new ObjectContent<Order>(SampleOrder, new JsonMediaTypeFormatter()), "application/json"));

      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
      Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
    }
  }
}
