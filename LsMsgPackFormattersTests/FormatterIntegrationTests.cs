using LsMsgPack;
using LsMsgPackFormatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace LsMsgPackFormattersTests
{
  public abstract class Animal
  {
    public string Name { get; set; }
  }

  public class Dog : Animal
  {
    public int Barks { get; set; }
  }

  public class Order
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public double[] Amounts { get; set; }
  }

  [ApiController]
  [Route("test")]
  public class TestController : ControllerBase
  {
    [HttpPost("echo")]
    public Order Echo(Order order) => order;

    [HttpPost("double")]
    public long? Double([FromBody] int? value) => value * 2L;

    [HttpGet("animal")]
    public Animal GetAnimal() => new Dog { Name = "Rex", Barks = 3 };
  }

  [TestFixture]
  public class FormatterIntegrationTests
  {
    private static readonly Order SampleOrder = new Order { Id = 42, Customer = "Infotopie", Amounts = new[] { 1.5, 2.25 } };

    private static MsgPackSettings Plain => new MsgPackSettings { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Never };

    private static async Task<(IHost host, HttpClient client)> StartAsync(Action<IMvcBuilder> addFormatters)
    {
      IHost host = await new HostBuilder()
        .ConfigureWebHost(web => web
          .UseTestServer() // AllowSynchronousIO is false by default, as in Kestrel
          .ConfigureServices(services => addFormatters(services.AddControllers().AddApplicationPart(typeof(TestController).Assembly)))
          .Configure(app => app.UseRouting().UseEndpoints(endpoints => endpoints.MapControllers())))
        .StartAsync();
      return (host, host.GetTestClient());
    }

    private static Task<(IHost host, HttpClient client)> StartAsync() => StartAsync(mvc => mvc.AddLsMsgPackSerializerFormatters());

    private static HttpRequestMessage Post(byte[] body, string contentType, string accept)
    {
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/test/echo") { Content = new ByteArrayContent(body) };
      request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
      return request;
    }

    private static void AssertSampleOrder(Order actual)
    {
      Assert.That(actual.Id, Is.EqualTo(SampleOrder.Id));
      Assert.That(actual.Customer, Is.EqualTo(SampleOrder.Customer));
      Assert.That(actual.Amounts, Is.EqualTo(SampleOrder.Amounts));
    }

    [TestCase(LsMsgPackMediaTypes.MsgPack)]
    [TestCase(LsMsgPackMediaTypes.XMsgPack)]
    public async Task PlainMsgPackRoundTrips(string mediaType)
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Post(MsgPackSerializer.Serialize(SampleOrder, Plain), mediaType, mediaType));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(mediaType));
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(MsgPackItem.UnpackMultiple(body).Count, Is.EqualTo(1), "Plain msgpack should be a single item, without a schema");
        AssertSampleOrder(MsgPackSerializer.Deserialize<Order>(body, Plain));
      }
    }

    [Test]
    public async Task LsMsgPackRoundTripsWithConfiguredSettings()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Post(MsgPackSerializer.Serialize(SampleOrder), LsMsgPackMediaTypes.XLsMsgPack, LsMsgPackMediaTypes.XLsMsgPack));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(LsMsgPackMediaTypes.XLsMsgPack));
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(body, Is.EqualTo(MsgPackSerializer.Serialize(SampleOrder, new MsgPackSettings())), "The root should not get a type id when it matches the declared type");
        AssertSampleOrder(MsgPackSerializer.Deserialize<Order>(body));
      }
    }

    [Test]
    public async Task LsMsgPackAddsTypeIdForPolymorphicRoot()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/test/animal");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(LsMsgPackMediaTypes.XLsMsgPack));
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Animal animal = MsgPackSerializer.Deserialize<Animal>(await response.Content.ReadAsByteArrayAsync());
        Assert.That(animal, Is.TypeOf<Dog>());
        Assert.That(((Dog)animal).Barks, Is.EqualTo(3));
      }
    }

    [Test]
    public async Task SetupActionIsApplied()
    {
      (IHost host, HttpClient client) = await StartAsync(mvc => mvc.AddLsMsgPackSerializerFormatters(settings => settings.UseInexedSchema = false));
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Post(MsgPackSerializer.Serialize(SampleOrder, Plain), LsMsgPackMediaTypes.XLsMsgPack, LsMsgPackMediaTypes.XLsMsgPack));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(MsgPackItem.UnpackMultiple(body).Count, Is.EqualTo(1), "UseInexedSchema = false from the setup action should be used");
      }
    }

    [Test]
    public async Task MvcOptionsRegistrationWorks()
    {
      (IHost host, HttpClient client) = await StartAsync(mvc => mvc.AddMvcOptions(options => options.AddLsMsgPackSerializerFormatters()));
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Post(MsgPackSerializer.Serialize(SampleOrder, Plain), LsMsgPackMediaTypes.MsgPack, LsMsgPackMediaTypes.MsgPack));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        AssertSampleOrder(MsgPackSerializer.Deserialize<Order>(await response.Content.ReadAsByteArrayAsync(), Plain));
      }
    }

    [Test]
    public async Task PrimitiveBodyRoundTrips()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpRequestMessage request = Post(MsgPackSerializer.Serialize(21, Plain), LsMsgPackMediaTypes.MsgPack, LsMsgPackMediaTypes.MsgPack);
        request.RequestUri = new Uri("/test/double", UriKind.Relative);
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(MsgPackSerializer.Deserialize<long>(await response.Content.ReadAsByteArrayAsync(), Plain), Is.EqualTo(42));
      }
    }

    [TestCase(new byte[] { 0xC1 }, TestName = "MalformedInputGives400(never used type)")]
    [TestCase(new byte[] { 0x83, 0xA2, 0x49 }, TestName = "MalformedInputGives400(truncated)")]
    [TestCase(new byte[] { 0xA5, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, TestName = "MalformedInputGives400(string instead of map)")]
    public async Task MalformedInputGives400(byte[] body)
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Post(body, LsMsgPackMediaTypes.MsgPack, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
      }
    }

    [Test]
    public async Task UnsupportedContentTypeGives415()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Post(new byte[] { 0x68, 0x69 }, "text/plain", LsMsgPackMediaTypes.MsgPack));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
      }
    }

    [Test]
    public async Task JsonIsStillServed()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/test/echo") { Content = new StringContent("{\"id\":42,\"customer\":\"Infotopie\",\"amounts\":[1.5,2.25]}") };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
      }
    }
  }
}
