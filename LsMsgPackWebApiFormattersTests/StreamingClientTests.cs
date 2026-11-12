using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPackFormatters;
using LsMsgPackWebApiFormatters;
using LsMsgPack.Http;
using LsMsgPack.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormattersTests
{
  [ApiController]
  [Route("streamclient")]
  public class StreamClientCoreController : ControllerBase
  {
    [HttpGet("orders")]
    public IAsyncEnumerable<Order> GetOrders() => StreamingClientTests.OrdersAsync(3);

    [HttpGet("list")]
    public List<Order> GetList() => Enumerable.Range(1, 3).Select(StreamingClientTests.MakeOrder).ToList();

    [HttpGet("gated")]
    public IAsyncEnumerable<Order> GetGated() => StreamingClientTests.GatedAsync();

    [HttpGet("none")]
    public IActionResult GetNone() => NoContent();

    [HttpGet("failing")]
    public IAsyncEnumerable<Order> GetFailing() => StreamingClientTests.FailingAsync();

    [HttpGet("json")]
    public IActionResult GetJson() => Content("[]", "application/json");
  }

  [System.Web.Http.RoutePrefix("webapistream")]
  public class StreamClientWebApiController : System.Web.Http.ApiController
  {
    [System.Web.Http.HttpGet, System.Web.Http.Route("list")]
    public List<Order> GetList() => Enumerable.Range(1, 3).Select(StreamingClientTests.MakeOrder).ToList();

    [System.Web.Http.HttpGet, System.Web.Http.Route("null")]
    public List<Order> GetNull() => null;
  }

  /// <summary>
  /// HttpClient reading streamed responses (MsgPackStreamExtensions) and sending streamed request bodies (MsgPackStreamContent), against the ASP.NET Core formatters and Web API 2.
  /// </summary>
  [TestFixture]
  public class StreamingClientTests
  {
    internal static Order MakeOrder(int id) => new Order { Id = id, Customer = "c" + id, Amounts = new[] { id * 1.5 } };

    internal static async IAsyncEnumerable<Order> OrdersAsync(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      for (int i = 1; i <= count; i++)
      {
        await Task.Yield();
        yield return MakeOrder(i);
      }
    }

    private static SemaphoreSlim _gate;

    internal static async IAsyncEnumerable<Order> GatedAsync()
    {
      yield return MakeOrder(1);
      await _gate.WaitAsync(TimeSpan.FromSeconds(20));
      yield return MakeOrder(2);
    }

    private static SemaphoreSlim _failGate;

    /// <summary>
    /// Two orders, then an exception when the test releases <see cref="_failGate"/> (TestServer drops what the client did not read yet when the response is aborted).
    /// </summary>
    internal static async IAsyncEnumerable<Order> FailingAsync()
    {
      yield return MakeOrder(1);
      await Task.Yield();
      yield return MakeOrder(2);
      await _failGate.WaitAsync(TimeSpan.FromSeconds(20));
      throw new InvalidOperationException("The query failed.");
    }

    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
      // a streamed request body, read asynchronously (TestServer, as Kestrel, refuses synchronous reads)
      endpoints.MapPost("/streamclient/upload", async (HttpContext context, LsMsgPackHttpSerializer serializer) =>
      {
        List<string> customers = new List<string>();
        using (MsgPackStreamReader reader = serializer.CreateStreamReader(context.Request.Body, context.Request.ContentType, leaveOpen: true))
        {
          await reader.ReadHeadAsync<List<Order>>(context.RequestAborted);
          await foreach (Order order in reader.ReadElementsAsync<Order>(context.RequestAborted))
            customers.Add(order.Customer);
          if (!await reader.AtEndAsync(context.RequestAborted))
            customers.Add(await reader.ReadObjectAsync<string>(context.RequestAborted));
        }
        return Results.Text(context.Request.ContentType + "|" + string.Join(",", customers));
      });
    }

    private static async Task<IHost> StartAspNetCore()
    {
      return await new HostBuilder()
        .ConfigureWebHost(web => web
          .UseTestServer()
          .ConfigureServices(services =>
          {
            services.AddControllers().AddApplicationPart(typeof(StreamClientCoreController).Assembly).AddLsMsgPackSerializerFormatters();
            services.AddLsMsgPack();
          })
          .Configure(app => app.UseRouting().UseEndpoints(endpoints =>
          {
            endpoints.MapControllers();
            MapEndpoints(endpoints);
          })))
        .StartAsync();
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> items)
    {
      List<T> list = new List<T>();
      await foreach (T item in items)
        list.Add(item);
      return list;
    }

    [Test]
    public async Task ItemsOfAnAsyncEnumerable()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          List<Order> orders = await ToListAsync(client.GetFromMsgPackAsAsyncEnumerable<Order>("streamclient/orders", formatter));
          Assert.That(orders.Select(o => o.Customer), Is.EqualTo(new[] { "c1", "c2", "c3" }));
          Assert.That(orders[2].Amounts, Is.EqualTo(new[] { 4.5 }));
          Assert.That(recorder.AcceptHeaders[0], Is.EqualTo(MsgPackStreamExtensions.StreamAccept));
          byte[] body = recorder.Responses[0];
          Assert.That(body[0] & 0xF0, Is.EqualTo(0x80), "The head: the schema inline");
          Assert.That(body.Skip(body.Length - 2), Is.EqualTo(new byte[] { 0xC1, 0xC0 }), "An array of unknown length");
        }
      }
    }

    /// <summary>
    /// The schema of the head is learned like the schema of a body: the next request advertises it, the server refers to it.
    /// </summary>
    [Test]
    public async Task TheSchemaOfTheHeadIsLearned()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          for (int t = 0; t < 3; t++)
            Assert.That((await ToListAsync(client.GetFromMsgPackAsAsyncEnumerable<Order>("streamclient/orders", formatter))).Select(o => o.Id), Is.EqualTo(new[] { 1, 2, 3 }));

          Assert.That(recorder.SchemasHeaders[0], Is.Null);
          Assert.That(recorder.SchemasHeaders[1], Has.Length.EqualTo(32));
          Assert.That(recorder.Responses[1][0], Is.EqualTo(0xD8), "A reference");
          Assert.That(recorder.Responses[2][0], Is.EqualTo(0xD8));
          Assert.That(recorder.Responses[1].Length, Is.LessThan(recorder.Responses[0].Length));

          // the List<Order> of a body has the same (complete) schema: it is referred to as well
          List<Order> list = await ToListAsync(client.GetFromMsgPackAsAsyncEnumerable<Order>("streamclient/list", formatter));
          Assert.That(list.Select(o => o.Id), Is.EqualTo(new[] { 1, 2, 3 }));
          Assert.That(recorder.Responses[3][0], Is.EqualTo(0xD8));
        }
      }
    }

    [Test]
    public async Task DescribedSchemaAndStream()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(true, recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          List<Order> orders = await ToListAsync(client.GetFromMsgPackAsAsyncEnumerable<Order>("streamclient/orders", formatter));
          Assert.That(orders.Select(o => o.Id), Is.EqualTo(new[] { 1, 2, 3 }));
          Assert.That(recorder.AcceptHeaders[0], Does.EndWith(", application/x-lsmsgpack; schema=described; stream=unknown-length"));
          Assert.That(MsgPackMediaTypes.AcceptsStream(recorder.AcceptHeaders[0], MsgPackMediaTypes.XLsMsgPack), Is.True);
          Dictionary<string, object> schema = MsgPackSerializer.Deserialize<Dictionary<string, object>>(recorder.Responses[0], new MsgPackSettings { UseInexedSchema = false });
          Assert.That(schema, Does.ContainKey(string.Empty), "Version 2: the root entry");
        }
      }
    }

    /// <summary>
    /// The items are read as they arrive: the first one before the server makes the second.
    /// </summary>
    [Test]
    public async Task ItemsArriveWhileTheSourceWaits()
    {
      _gate = new SemaphoreSlim(0);
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        using (HttpClient client = new HttpClient(formatter.CreateHandler(host.GetTestServer().CreateHandler())) { BaseAddress = new Uri("http://localhost/") })
        {
          IAsyncEnumerator<Order> items = client.GetFromMsgPackAsAsyncEnumerable<Order>("streamclient/gated", formatter).GetAsyncEnumerator();
          try
          {
            Task<bool> first = items.MoveNextAsync().AsTask();
            Assert.That(await Task.WhenAny(first, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(first), "The first order before the second is made");
            Assert.That(first.Result, Is.True);
            Assert.That(items.Current.Id, Is.EqualTo(1));

            _gate.Release();
            Assert.That(await items.MoveNextAsync(), Is.True);
            Assert.That(items.Current.Id, Is.EqualTo(2));
            Assert.That(await items.MoveNextAsync(), Is.False);
          }
          finally
          {
            await items.DisposeAsync();
          }
        }
      }
    }

    [Test]
    public async Task TokensOfAStream()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        using (HttpClient client = new HttpClient(formatter.CreateHandler(host.GetTestServer().CreateHandler())) { BaseAddress = new Uri("http://localhost/") })
        using (MsgPackStreamReader reader = await client.GetMsgPackStreamAsync("streamclient/orders", formatter))
        {
          await reader.ReadHeadAsync<List<Order>>();
          Assert.That(await reader.ReadAsync(), Is.True);
          Assert.That(reader.TokenType, Is.EqualTo(MsgPackTokenType.ArrayStart));
          Assert.That(reader.IsUnknownLength, Is.True);
          List<int> ids = new List<int>();
          while (!await reader.AtEndAsync())
            ids.Add((await reader.ReadObjectAsync<Order>()).Id);
          Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3 }));
          Assert.That(await reader.ReadAsync(), Is.True);
          Assert.That(reader.TokenType, Is.EqualTo(MsgPackTokenType.ArrayEnd));
          Assert.That(await reader.ReadAsync(), Is.False);
        }
      }
    }

    /// <summary>
    /// A server that doesn't stream (Web API 2) sends the List&lt;T&gt; as one body: the same items, nil is none.
    /// </summary>
    [Test]
    public async Task ListsOfServersThatDoNotStream()
    {
      System.Web.Http.HttpConfiguration config = new System.Web.Http.HttpConfiguration();
      System.Web.Http.HttpConfigurationExtensions.MapHttpAttributeRoutes(config);
      LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
      config.Formatters.Add(new LsMsgPackMediaTypeFormatter());
      using (System.Web.Http.HttpServer server = new System.Web.Http.HttpServer(config))
      using (HttpClient client = new HttpClient(formatter.CreateHandler(server)) { BaseAddress = new Uri("http://localhost/") })
      {
        Assert.That((await ToListAsync(client.GetFromMsgPackAsAsyncEnumerable<Order>("webapistream/list", formatter))).Select(o => o.Customer), Is.EqualTo(new[] { "c1", "c2", "c3" }));
        Assert.That(await ToListAsync(client.GetFromMsgPackAsAsyncEnumerable<Order>("webapistream/null", formatter)), Is.Empty);
      }
    }

    [Test]
    public async Task NoContentAndErrors()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        using (HttpClient client = new HttpClient(formatter.CreateHandler(host.GetTestServer().CreateHandler())) { BaseAddress = new Uri("http://localhost/") })
        {
          Assert.That(await ToListAsync(client.GetFromMsgPackAsAsyncEnumerable<Order>("streamclient/none", formatter)), Is.Empty);
          MsgPackHttpException missing = Assert.ThrowsAsync<MsgPackHttpException>(new Func<Task>(() => ToListAsync(client.GetFromMsgPackAsAsyncEnumerable<Order>("streamclient/missing", formatter))));
          Assert.That(missing.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
          NotSupportedException json = Assert.ThrowsAsync<NotSupportedException>(new Func<Task>(() => ToListAsync(client.GetFromMsgPackAsAsyncEnumerable<Order>("streamclient/json", formatter))));
          Assert.That(json.Message, Does.Contain("application/json"));
        }
      }
    }

    /// <summary>
    /// The server's source fails after two items: the response is aborted, the client gets the two items, then an exception.
    /// </summary>
    [Test]
    public async Task AbortedStream()
    {
      _failGate = new SemaphoreSlim(0);
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        using (HttpClient client = new HttpClient(formatter.CreateHandler(host.GetTestServer().CreateHandler())) { BaseAddress = new Uri("http://localhost/") })
        {
          List<int> ids = new List<int>();
          Exception error = Assert.CatchAsync<Exception>(new Func<Task>(async () =>
          {
            await foreach (Order order in client.GetFromMsgPackAsAsyncEnumerable<Order>("streamclient/failing", formatter))
            {
              ids.Add(order.Id);
              if (order.Id == 2)
                _failGate.Release();
            }
          }));
          Assert.That(ids, Is.EqualTo(new[] { 1, 2 }), error.ToString());
        }
      }
    }

    [TestCase(MsgPackMediaTypes.XLsMsgPack)]
    [TestCase(MsgPackMediaTypes.MsgPack)]
    public async Task StreamedRequestBodies(string mediaType)
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        using (HttpClient client = new HttpClient(formatter.CreateHandler(host.GetTestServer().CreateHandler())) { BaseAddress = new Uri("http://localhost/") })
        {
          HttpResponseMessage response = await client.PostAsync("streamclient/upload", MsgPackStreamContent.Create(formatter, OrdersAsync(3), mediaType));
          Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(mediaType + "; stream=unknown-length|c1,c2,c3"));

          MsgPackStreamContent content = new MsgPackStreamContent(formatter, async writer =>
          {
            writer.WriteHead<List<Order>>();
            writer.WriteArrayStart();
            await writer.WriteObjectAsync(MakeOrder(7));
            writer.WriteEnd();
            await writer.WriteObjectAsync("and a second root value");
          }, mediaType);
          Assert.That(content.Headers.ContentLength, Is.Null);
          response = await client.PostAsync("streamclient/upload", content);
          Assert.That(await response.Content.ReadAsStringAsync(), Does.EndWith("|c7,and a second root value"));
        }
      }
    }
  }
}
