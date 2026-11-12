using LsMsgPack;
using LsMsgPackDebug;
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
using System.ComponentModel.DataAnnotations;
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

    [HttpGet("problem")]
    public IActionResult GetProblem() => Problem("Order 3 is locked.", statusCode: 409, title: "Locked");

    [HttpPost("validate")]
    public IActionResult Validate(ValidatedOrder order) => Ok(order);

    [HttpGet("notfoundorder")]
    public IActionResult GetNotFoundOrder() => NotFound(StreamingClientTests.MakeOrder(8));

    [HttpGet("down")]
    public IActionResult GetDown() => new ContentResult() { StatusCode = 503, Content = "Down for maintenance", ContentType = "text/plain; charset=utf-8" };

    private static Order Echo(Order order, string method)
    {
      order.Customer += " " + method;
      return order;
    }
  }

  public class ValidatedOrder
  {
    [Required]
    public string Customer { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 5)]
    public int Count { get; set; }
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
          MsgPackHttpException missing = Assert.ThrowsAsync<MsgPackHttpException>(new Func<Task>(() => client.GetFromMsgPackAsync<Order>("clientext/missing", formatter)));
          Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
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
    public async Task ErrorsHaveTheirProblemDetails()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        using (HttpClient client = new HttpClient(formatter.CreateHandler(host.GetTestServer().CreateHandler())) { BaseAddress = new Uri("http://localhost/") })
        {
          // NotFound() of an [ApiController]: problem details, JSON although the request asks for MsgPack
          MsgPackHttpException notFound = Assert.ThrowsAsync<MsgPackHttpException>(new Func<Task>(() => client.GetFromMsgPackAsync<Order>("clientext/missingorder", formatter)));
          Assert.That(notFound.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
          Assert.That(((HttpRequestException)notFound).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "The one of HttpRequestException (.NET 5+)");
          Assert.That(notFound.Body, Is.Empty, "No action: an empty 404");

          MsgPackHttpException problem = Assert.ThrowsAsync<MsgPackHttpException>(new Func<Task>(() => client.GetFromMsgPackAsync<Order>("clientext/problem", formatter)));
          Assert.That(problem.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
          Assert.That(problem.ContentType, Does.StartWith("application/problem+json"));
          Assert.That(problem.Title, Is.EqualTo("Locked"));
          Assert.That(problem.Detail, Is.EqualTo("Order 3 is locked."));
          Assert.That(problem.ProblemType, Does.StartWith("https://tools.ietf.org/html/rfc9110"));
          Assert.That(problem.Errors, Is.Empty);
          Assert.That(problem.Text, Does.Contain("\"traceId\""));
          Assert.That(problem.Message, Is.EqualTo("Response status code does not indicate success: 409 (Conflict). Locked Order 3 is locked."));
          Assert.Throws<NotSupportedException>(new Action(() => problem.ReadBody<Order>(formatter)));

          // The automatic 400 of [ApiController]: validation problem details
          using (HttpResponseMessage response = await client.PostAsMsgPackAsync("clientext/validate", new ValidatedOrder() { Count = 9 }, formatter))
          {
            MsgPackHttpException invalid = Assert.ThrowsAsync<MsgPackHttpException>(new Func<Task>(() => response.EnsureSuccessStatusCodeAsync()));
            Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(invalid.Title, Is.EqualTo("One or more validation errors occurred."));
            Assert.That(invalid.Errors.Keys, Is.EquivalentTo(new[] { "Customer", "Count" }));
            Assert.That(invalid.Errors["Customer"], Is.EqualTo(new[] { "The Customer field is required." }));
            Assert.That(invalid.Message, Does.Contain("One or more validation errors occurred. ").And.Contain("Customer: The Customer field is required."));
          }
          using (HttpResponseMessage response = await client.PostAsMsgPackAsync("clientext/validate", new ValidatedOrder() { Count = 9 }, formatter))
          {
            NotSupportedException notChecked = Assert.ThrowsAsync<NotSupportedException>(new Func<Task>(() => response.Content.ReadFromMsgPackAsync<ValidatedOrder>(formatter)));
            Assert.That(notChecked.Message, Does.StartWith("The response is application/problem+json, not MsgPack. It starts with: {").And.Contain("One or more validation errors occurred."));
          }
          using (HttpResponseMessage response = await client.PostAsMsgPackAsync("clientext/validate", new ValidatedOrder() { Customer = "c", Count = 2 }, formatter))
            Assert.That(await response.EnsureSuccessStatusCodeAsync(), Is.SameAs(response));

          // A value of the action: MsgPack, as the request asks
          MsgPackHttpException withOrder = Assert.ThrowsAsync<MsgPackHttpException>(new Func<Task>(() => client.GetFromMsgPackAsync<Order>("clientext/notfoundorder", formatter)));
          Assert.That(withOrder.ContentType, Does.StartWith(MsgPackMediaTypes.XLsMsgPack));
          Assert.That(withOrder.Text, Is.Null);
          Assert.That(withOrder.Title, Is.Null);
          Assert.That(withOrder.Message, Is.EqualTo("Response status code does not indicate success: 404 (Not Found)."));
          Assert.That(withOrder.ReadBody<Order>(formatter).Customer, Is.EqualTo("c8"));

          // A text
          MsgPackHttpException down = Assert.ThrowsAsync<MsgPackHttpException>(new Func<Task>(() => client.GetFromMsgPackAsync<Order>("clientext/down", formatter)));
          Assert.That(down.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
          Assert.That(down.Text, Is.EqualTo("Down for maintenance"));
          Assert.That(down.Message, Is.EqualTo("Response status code does not indicate success: 503 (Service Unavailable). Down for maintenance"));

          // Streams
          MsgPackHttpException streamProblem = Assert.ThrowsAsync<MsgPackHttpException>(new Func<Task>(() => client.GetMsgPackStreamAsync("clientext/problem", formatter)));
          Assert.That(streamProblem.Detail, Is.EqualTo("Order 3 is locked."));
        }
      }
    }

    [Test]
    public void ProblemDetailsOfOtherServers()
    {
      byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);
      MsgPackHttpException errorsAsStrings = new MsgPackHttpException(HttpStatusCode.BadRequest, null, "application/json", Utf8("{\"title\":\"Invalid\",\"errors\":{\"name\":\"required\",\"other\":5}}"));
      Assert.That(errorsAsStrings.Title, Is.EqualTo("Invalid"));
      Assert.That(errorsAsStrings.Errors, Has.Count.EqualTo(1));
      Assert.That(errorsAsStrings.Errors["name"], Is.EqualTo(new[] { "required" }));
      Assert.That(errorsAsStrings.Message, Is.EqualTo("Response status code does not indicate success: 400. Invalid name: required"));

      MsgPackHttpException notJson = new MsgPackHttpException(HttpStatusCode.BadGateway, "Bad Gateway", "application/json", Utf8("<html>proxy error</html>"));
      Assert.That(notJson.Title, Is.Null);
      Assert.That(notJson.Text, Is.EqualTo("<html>proxy error</html>"));
      Assert.That(notJson.Message, Does.EndWith("Bad Gateway). <html>proxy error</html>"));

      MsgPackHttpException latin1 = new MsgPackHttpException(HttpStatusCode.InternalServerError, "Internal Server Error", "text/plain; charset=iso-8859-1", new byte[] { 0x63, 0x61, 0x66, 0xE9 });
      Assert.That(latin1.Text, Is.EqualTo("caf\u00E9"));

      MsgPackHttpException empty = new MsgPackHttpException(HttpStatusCode.NotFound, "Not Found", null, null);
      Assert.That(empty.Body, Is.Empty);
      Assert.That(empty.Text, Is.Null);
      Assert.That(empty.ReadBody<Order>(new LsMsgPackMediaTypeFormatter()), Is.Null);

      MsgPackHttpException longText = new MsgPackHttpException(HttpStatusCode.InternalServerError, "Internal Server Error", "text/html", Utf8(new string('x', 1000)));
      Assert.That(longText.Message, Does.EndWith(new string('x', 200) + "..."));
    }

    [Test]
    public async Task ALargeErrorBodyIsCut()
    {
      using (HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new ByteArrayContent(new byte[MsgPackHttpException.MaxBodyLength * 2]) })
      {
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        MsgPackHttpException large = Assert.ThrowsAsync<MsgPackHttpException>(new Func<Task>(() => response.EnsureSuccessStatusCodeAsync()));
        Assert.That(large.Body, Has.Length.EqualTo(MsgPackHttpException.MaxBodyLength));
        Assert.That(large.Text, Is.Null);
      }
      await Task.CompletedTask;
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
