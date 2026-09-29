using LsMsgPack;
using LsMsgPackFormatters;
using LsMsgPackWebApiFormatters;
using LtMsgPack.Http;
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
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormattersTests
{
  [ApiController]
  [Route("negotiation")]
  public class NegotiationCoreController : ControllerBase
  {
    [HttpGet("animal")]
    public Animal GetAnimal() => new Dog { Name = "Rex", Barks = 3 };

    [HttpGet("order")]
    public Order GetOrder() => new Order { Id = 42, Customer = "Infotopie", Amounts = new[] { 1.5, 2.25 } };

    [HttpPost("echo")]
    public Order Echo(Order order) => order;
  }

  /// <summary>
  /// Keeps the bodies as they went over the wire (the first bytes of the responses, the requests).
  /// </summary>
  public class Recorder : DelegatingHandler
  {
    public readonly List<byte[]> Requests = new List<byte[]>();
    public readonly List<byte[]> Responses = new List<byte[]>();
    public readonly List<string> SchemasHeaders = new List<string>();

    public Recorder(HttpMessageHandler inner) : base(inner) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      SchemasHeaders.Add(request.Headers.TryGetValues(LtMsgPackHttpSerializer.SchemasHeader, out IEnumerable<string> values) ? string.Join(",", values) : null);
      if (!(request.Content is null))
        Requests.Add(await request.Content.ReadAsByteArrayAsync());
      HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
      await response.Content.LoadIntoBufferAsync();
      Responses.Add(await response.Content.ReadAsByteArrayAsync());
      return response;
    }
  }

  /// <summary>
  /// HttpClient with the handler of the Web API formatter (LsMsgPackSchemaHandler): the first response carries the schema, the next ones refer to it.
  /// </summary>
  [TestFixture]
  public class NegotiationTests
  {
    private static readonly Order SampleOrder = new Order { Id = 42, Customer = "Infotopie", Amounts = new[] { 1.5, 2.25 } };

    private static bool IsReference(byte[] body)
    {
      return body.Length >= SchemaStore.ReferenceLength && body[0] == 0xD8 && body[1] == (byte)SchemaStore.ReferenceExtensionType;
    }

    private static async Task<Animal> GetAnimal(HttpClient client, LsMsgPackMediaTypeFormatter formatter, string path)
    {
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
      HttpResponseMessage response = await client.SendAsync(request);
      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
      return await response.Content.ReadAsAsync<Animal>(new MediaTypeFormatter[] { formatter });
    }

    private static async Task<IHost> StartAspNetCore()
    {
      return await new HostBuilder()
        .ConfigureWebHost(web => web
          .UseTestServer()
          .ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(NegotiationCoreController).Assembly).AddLsMsgPackSerializerFormatters())
          .Configure(app => app.UseRouting().UseEndpoints(endpoints => endpoints.MapControllers())))
        .StartAsync();
    }

    private static async Task<Order> GetOrder(HttpClient client, LsMsgPackMediaTypeFormatter formatter)
    {
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "negotiation/order");
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
      HttpResponseMessage response = await client.SendAsync(request);
      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
      return await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter });
    }

    [Test]
    public async Task AspNetCoreSendsTheSchemaOnce()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          for (int t = 0; t < 3; t++)
            Assert.That((await GetOrder(client, formatter)).Customer, Is.EqualTo(SampleOrder.Customer));

          Assert.That(recorder.SchemasHeaders[0], Is.Null);
          Assert.That(recorder.SchemasHeaders[1], Has.Length.EqualTo(32));
          Assert.That(IsReference(recorder.Responses[0]), Is.False);
          Assert.That(IsReference(recorder.Responses[1]), Is.True);
          Assert.That(IsReference(recorder.Responses[2]), Is.True);
          Assert.That(recorder.Responses[1].Length, Is.LessThan(recorder.Responses[0].Length));
        }
      }
    }

    /// <summary>
    /// A schema of at most 18 bytes (the length of a reference) is always sent inline.
    /// </summary>
    [Test]
    public async Task SmallSchemasStayInline()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          for (int t = 0; t < 2; t++)
            Assert.That(((Dog)await GetAnimal(client, formatter, "negotiation/animal")).Barks, Is.EqualTo(3)); // { "Dog": ["Barks", "Name"] } is 17 bytes

          Assert.That(recorder.SchemasHeaders[1], Has.Length.EqualTo(32));
          Assert.That(IsReference(recorder.Responses[1]), Is.False);
        }
      }
    }

    [Test]
    public async Task RequestsCarryTheirSchemaInline()
    {
      using (IHost host = await StartAspNetCore())
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          for (int t = 0; t < 2; t++)
          {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "negotiation/echo") { Content = new ObjectContent<Order>(SampleOrder, formatter, MsgPackMediaTypes.XLsMsgPack) };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
            HttpResponseMessage response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That((await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter })).Customer, Is.EqualTo(SampleOrder.Customer));
          }

          Assert.That(recorder.Requests[1], Is.EqualTo(MsgPackSerializer.Serialize(SampleOrder)), "The same bytes as LsMsgPack, with the schema");
          Assert.That(IsReference(recorder.Responses[1]), Is.True);
        }
      }
    }

    [Test]
    public async Task FullClientStoreKeepsWorking()
    {
      using (IHost host = await StartAspNetCore())
      {
        LtMsgPackHttpOptions options = new LtMsgPackHttpOptions();
        options.XLsMsgPack.SchemaStore = new SchemaStore() { MaxSchemas = 0 };
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter(options);
        Recorder recorder = new Recorder(host.GetTestServer().CreateHandler());
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder)) { BaseAddress = new Uri("http://localhost/") })
        {
          for (int t = 0; t < 2; t++)
            Assert.That((await GetOrder(client, formatter)).Customer, Is.EqualTo(SampleOrder.Customer));
          Assert.That(recorder.SchemasHeaders, Is.EqualTo(new string[] { null, null }));
          Assert.That(IsReference(recorder.Responses[1]), Is.False);
        }
      }
    }

    [Test]
    public async Task WebApiServerSendsTheSchemaOnce()
    {
      System.Web.Http.HttpConfiguration config = new System.Web.Http.HttpConfiguration();
      System.Web.Http.HttpConfigurationExtensions.MapHttpAttributeRoutes(config);
      config.Formatters.Add(new LsMsgPackMediaTypeFormatter());
      using (System.Web.Http.HttpServer server = new System.Web.Http.HttpServer(config))
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        Recorder recorder = new Recorder(server);
        using (HttpClient client = new HttpClient(formatter.CreateHandler(recorder), false) { BaseAddress = new Uri("http://localhost/") }) // the server is used again below
        {
          for (int t = 0; t < 2; t++)
          {
            HttpRequestMessage order = new HttpRequestMessage(HttpMethod.Get, "webapi/order");
            order.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
            HttpResponseMessage response = await client.SendAsync(order);
            Assert.That((await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter })).Customer, Is.EqualTo(SampleOrder.Customer));
          }

          Assert.That(IsReference(recorder.Responses[0]), Is.False);
          Assert.That(IsReference(recorder.Responses[1]), Is.True);
        }

        // Without the handler: always the schema inline
        using (HttpClient plain = new HttpClient(new Recorder(server)) { BaseAddress = new Uri("http://localhost/") })
        {
          HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "webapi/animal");
          request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
          HttpResponseMessage response = await plain.SendAsync(request);
          byte[] body = await response.Content.ReadAsByteArrayAsync();
          Assert.That(IsReference(body), Is.False);
          Assert.That(response.Content.Headers.Expires, Is.Null);
          Assert.That(((Dog)MsgPackSerializer.Deserialize<Animal>(body)).Barks, Is.EqualTo(3));
        }
      }
    }

    [Test]
    public async Task WebApiReferenceExpires()
    {
      System.Web.Http.HttpConfiguration config = new System.Web.Http.HttpConfiguration();
      System.Web.Http.HttpConfigurationExtensions.MapHttpAttributeRoutes(config);
      config.Formatters.Add(new LsMsgPackMediaTypeFormatter());
      using (System.Web.Http.HttpServer server = new System.Web.Http.HttpServer(config))
      {
        LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
        using (HttpClient client = new HttpClient(formatter.CreateHandler(server)) { BaseAddress = new Uri("http://localhost/") })
        {
          HttpResponseMessage last = null;
          for (int t = 0; t < 2; t++)
          {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "webapi/order");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
            last = await client.SendAsync(request);
            await last.Content.LoadIntoBufferAsync();
          }
          Assert.That(last.Content.Headers.Expires, Is.LessThan(DateTimeOffset.UtcNow));
        }
      }
    }
  }
}
