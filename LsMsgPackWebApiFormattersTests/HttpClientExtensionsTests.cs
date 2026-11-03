using LsMsgPack;
using LsMsgPackFormatters;
using LsMsgPackWebApiFormatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormattersTests
{
  [ApiController]
  [Route("clientext")]
  public class ClientExtensionsCoreController : ControllerBase
  {
    [HttpGet("order/{id}")]
    public Order GetOrder(int id) => StreamingClientTests.MakeOrder(id);

    [HttpPost("echo")]
    public Order Post(Order order) => Echo(order, "post");

    [HttpPut("echo")]
    public Order Put(Order order) => Echo(order, "put");

    [HttpPatch("echo")]
    public Order Patch(Order order) => Echo(order, "patch");

    [HttpGet("none")]
    public IActionResult GetNone() => NoContent();

    [HttpGet("json")]
    public IActionResult GetJson() => Content("{}", "application/json");

    [HttpGet("stream")]
    public IAsyncEnumerable<Order> GetStream() => StreamingClientTests.OrdersAsync(2);

    private static Order Echo(Order order, string method)
    {
      order.Customer += " " + method;
      return order;
    }
  }

  [System.Web.Http.RoutePrefix("clientextwebapi")]
  public class ClientExtensionsWebApiController : System.Web.Http.ApiController
  {
    [System.Web.Http.HttpGet, System.Web.Http.Route("order/{id}")]
    public Order GetOrder(int id) => StreamingClientTests.MakeOrder(id);

    [System.Web.Http.HttpPost, System.Web.Http.Route("echo")]
    public Order Post(Order order)
    {
      order.Customer += " post";
      return order;
    }
  }

  /// <summary>
  /// MsgPackHttpClientExtensions (GetFromMsgPackAsync, PostAsMsgPackAsync...) against the ASP.NET Core formatters and Web API 2.
  /// </summary>
  [TestFixture]
  public class HttpClientExtensionsTests
  {
    private static Task<IHost> StartAspNetCore()
    {
      return new HostBuilder()
        .ConfigureWebHost(web => web
          .UseTestServer()
          .ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(ClientExtensionsCoreController).Assembly).AddLsMsgPackSerializerFormatters())
          .Configure(app => app.UseRouting().UseEndpoints(endpoints => endpoints.MapControllers())))
        .StartAsync();
    }

    [Test]
    public async Task GetReadsTheBodyAndTheSchemaIsReferred()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          for (int t = 1; t <= 3; t++)
          {
            Order order = await client.GetFromMsgPackAsync<Order>("clientext/order/" + t, formatter);
            Assert.That(order.Customer, Is.EqualTo("c" + t));
            Assert.That(order.Amounts, Is.EqualTo(new[] { t * 1.5 }));
          }
          Assert.That(await client.GetFromMsgPackAsync<Order>(new Uri("clientext/order/4", UriKind.Relative), formatter), Has.Property(nameof(Order.Id)).EqualTo(4));

          Assert.That(recorder.AcceptHeaders[0], Is.EqualTo(MsgPackHttpClientExtensions.Accept));
          Assert.That(recorder.SchemasHeaders[1], Has.Length.EqualTo(32));
          Assert.That(recorder.Responses[1][0], Is.EqualTo(0xD8), "A reference");
        }
      }
    }

    [Test]
    public async Task PostPutAndPatchSendTheValue()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          Order order = StreamingClientTests.MakeOrder(7);
          using (HttpResponseMessage response = await client.PostAsMsgPackAsync("clientext/echo", order, formatter))
          {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(MsgPackMediaTypes.XLsMsgPack));
            Assert.That((await response.Content.ReadFromMsgPackAsync<Order>(formatter)).Customer, Is.EqualTo("c7 post"));
          }
          using (HttpResponseMessage response = await client.PutAsMsgPackAsync(new Uri("http://localhost/clientext/echo"), order, formatter))
            Assert.That((await response.Content.ReadFromMsgPackAsync<Order>(formatter)).Customer, Is.EqualTo("c7 put"));
          using (HttpResponseMessage response = await client.PatchAsMsgPackAsync("clientext/echo", order, formatter))
          {
            Order patched = await response.Content.ReadFromMsgPackAsync<Order>(formatter);
            Assert.That(patched.Customer, Is.EqualTo("c7 patch"));
            Assert.That(patched.Amounts, Is.EqualTo(order.Amounts));
          }

          Assert.That(recorder.AcceptHeaders, Is.All.EqualTo(MsgPackHttpClientExtensions.Accept));
          Assert.That(recorder.Requests[0], Is.EqualTo(MsgPackSerializer.Serialize(order)), "The body is what LsMsgPack writes, with the schema inline");
        }
      }
    }

    [Test]
    public async Task TheAcceptHeaderOfTheClientIsKept()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.MsgPack));
          Assert.That((await client.GetFromMsgPackAsync<Order>("clientext/order/1", formatter)).Customer, Is.EqualTo("c1"));
          using (HttpResponseMessage response = await client.PostAsMsgPackAsync("clientext/echo", StreamingClientTests.MakeOrder(2), formatter))
          {
            Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(MsgPackMediaTypes.MsgPack));
            Assert.That((await response.Content.ReadFromMsgPackAsync<Order>(formatter)).Customer, Is.EqualTo("c2 post"));
          }
          Assert.That(recorder.AcceptHeaders, Is.All.EqualTo(MsgPackMediaTypes.MsgPack));
        }
      }
    }

    [Test]
    public async Task StatusAndMediaTypes()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        using (HttpClient client = new HttpClient(formatter.CreateHandler(host.GetTestServer().CreateHandler())) { BaseAddress = new Uri("http://localhost/") })
        {
          Assert.That(await client.GetFromMsgPackAsync<Order>("clientext/none", formatter), Is.Null);
          Assert.That(await client.GetFromMsgPackAsync<int>("clientext/none", formatter), Is.EqualTo(0));
          Assert.ThrowsAsync<HttpRequestException>(new Func<Task>(() => client.GetFromMsgPackAsync<Order>("clientext/missing", formatter)));
          Assert.ThrowsAsync<NotSupportedException>(new Func<Task>(() => client.GetFromMsgPackAsync<Order>("clientext/json", formatter)));

          HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "clientext/stream");
          request.Headers.TryAddWithoutValidation("Accept", MsgPackStreamExtensions.StreamAccept);
          using (HttpResponseMessage response = await client.SendAsync(request))
            Assert.ThrowsAsync<NotSupportedException>(new Func<Task>(() => response.Content.ReadFromMsgPackAsync<List<Order>>(formatter)));
          Assert.That(await client.GetFromMsgPackAsync<List<Order>>("clientext/stream", formatter), Has.Count.EqualTo(2), "Not a stream without the parameter");
        }
      }
    }

    [Test]
    public async Task WebApi()
    {
      System.Web.Http.HttpConfiguration config = new System.Web.Http.HttpConfiguration();
      System.Web.Http.HttpConfigurationExtensions.MapHttpAttributeRoutes(config);
      LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
      config.Formatters.Add(new LsMsgPackMediaTypeFormatter());
      using (System.Web.Http.HttpServer server = new System.Web.Http.HttpServer(config))
      using (HttpClient client = new HttpClient(formatter.CreateHandler(server)) { BaseAddress = new Uri("http://localhost/") })
      {
        Assert.That((await client.GetFromMsgPackAsync<Order>("clientextwebapi/order/3", formatter)).Customer, Is.EqualTo("c3"));
        using (HttpResponseMessage response = await client.PostAsMsgPackAsync("clientextwebapi/echo", StreamingClientTests.MakeOrder(5), formatter))
        {
          Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(MsgPackMediaTypes.XLsMsgPack));
          Assert.That((await response.Content.ReadFromMsgPackAsync<Order>(formatter)).Customer, Is.EqualTo("c5 post"));
        }
      }
    }
  }
}
