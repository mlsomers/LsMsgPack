using LsMsgPack;
using LsMsgPackFormatters;
using LtMsgPack;
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
using System.Linq;
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

  public class Cat : Animal
  {
    public bool Purrs { get; set; }
  }

  public class Order
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public double[] Amounts { get; set; }
  }

  public class Receipt
  {
    public Guid Id { get; set; }
    public decimal Total { get; set; }
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

    [HttpGet("order")]
    public Order GetOrder() => new Order { Id = 42, Customer = "Infotopie", Amounts = new[] { 1.5, 2.25 } };

    [HttpGet("cat")]
    public Animal GetCat() => new Cat { Name = "Tom", Purrs = true };

    [HttpGet("receipt")]
    public Receipt GetReceipt() => new Receipt { Id = FormatterIntegrationTests.ReceiptId, Total = 12.50m };
  }

  [TestFixture]
  public class FormatterIntegrationTests
  {
    private static readonly Order SampleOrder = new Order { Id = 42, Customer = "Infotopie", Amounts = new[] { 1.5, 2.25 } };
    internal static readonly Guid ReceiptId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static MsgPackSettings Plain => new MsgPackSettings { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Never, ObjectLayout = ObjectLayout.Map };

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

    [TestCase(MsgPackMediaTypes.MsgPack)]
    [TestCase(MsgPackMediaTypes.XMsgPack)]
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
        HttpResponseMessage response = await client.SendAsync(Post(MsgPackSerializer.Serialize(SampleOrder), MsgPackMediaTypes.XLsMsgPack, MsgPackMediaTypes.XLsMsgPack));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(MsgPackMediaTypes.XLsMsgPack));
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
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
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
      (IHost host, HttpClient client) = await StartAsync(mvc => mvc.AddLsMsgPackSerializerFormatters(settings => settings.XLsMsgPack.UseInexedSchema = false));
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Post(MsgPackSerializer.Serialize(SampleOrder, Plain), MsgPackMediaTypes.XLsMsgPack, MsgPackMediaTypes.XLsMsgPack));

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
        HttpResponseMessage response = await client.SendAsync(Post(MsgPackSerializer.Serialize(SampleOrder, Plain), MsgPackMediaTypes.MsgPack, MsgPackMediaTypes.MsgPack));

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
        HttpRequestMessage request = Post(MsgPackSerializer.Serialize(21, Plain), MsgPackMediaTypes.MsgPack, MsgPackMediaTypes.MsgPack);
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
        HttpResponseMessage response = await client.SendAsync(Post(body, MsgPackMediaTypes.MsgPack, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
      }
    }

    [Test]
    public async Task UnsupportedContentTypeGives415()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Post(new byte[] { 0x68, 0x69 }, "text/plain", MsgPackMediaTypes.MsgPack));

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
    private static HttpRequestMessage Get(string path, string schemas)
    {
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
      if (!(schemas is null))
        request.Headers.Add(LtMsgPackHttpSerializer.SchemasHeader, schemas);
      return request;
    }

    private static string SchemaHeader(HttpResponseMessage response)
    {
      return response.Headers.TryGetValues(LtMsgPackHttpSerializer.SchemaHeader, out IEnumerable<string> values) ? values.Single() : null;
    }

    private static bool IsReference(byte[] body)
    {
      return body.Length >= SchemaStore.ReferenceLength && body[0] == 0xD8 && body[1] == (byte)SchemaStore.ReferenceExtensionType;
    }

    [Test]
    public async Task WithoutSchemasHeaderTheSchemaIsInline()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Get("/test/animal", null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(IsReference(body), Is.False);
        Assert.That(response.Headers.Vary, Does.Contain(LtMsgPackHttpSerializer.SchemasHeader));
        Assert.That(SchemaHeader(response), Has.Length.EqualTo(32));
        Assert.That(((Dog)MsgPackSerializer.Deserialize<Animal>(body)).Barks, Is.EqualTo(3), "LsMsgPack reads it without a store");
      }
    }

    [Test]
    public async Task ClientThatHoldsTheSchemaGetsAReference()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        LtMsgPackHttpSerializer clientSide = new LtMsgPackHttpSerializer();
        Uri server = new Uri("http://localhost/");

        HttpResponseMessage first = await client.SendAsync(Get("/test/order", clientSide.GetSchemasHeader(server)));
        byte[] inline = await first.Content.ReadAsByteArrayAsync();
        Assert.That(IsReference(inline), Is.False);
        Assert.That(clientSide.LearnSchema(server, SchemaHeader(first), inline, 0, inline.Length, MsgPackMediaTypes.XLsMsgPack), Is.True);
        Assert.That(clientSide.GetSchemasHeader(server), Is.EqualTo(SchemaHeader(first)));

        HttpResponseMessage second = await client.SendAsync(Get("/test/order", clientSide.GetSchemasHeader(server)));
        byte[] reference = await second.Content.ReadAsByteArrayAsync();
        Assert.That(IsReference(reference), Is.True);
        Assert.That(SchemaHeader(second), Is.EqualTo(SchemaHeader(first)));
        Assert.That(second.Content.Headers.ContentLength, Is.EqualTo(reference.Length));
        byte[] schema = clientSide.SchemaStore.GetSchema(SchemaId.Parse(SchemaHeader(first)));
        Assert.That(reference.Skip(SchemaStore.ReferenceLength), Is.EqualTo(inline.Skip(schema.Length)), "The same body after the reference as after the schema");

        AssertSampleOrder((Order)clientSide.Deserialize(typeof(Order), reference, 0, reference.Length, MsgPackMediaTypes.XLsMsgPack));
        AssertSampleOrder(MsgPackSerializer.Deserialize<Order>(reference, new MsgPackSettings() { SchemaStore = clientSide.SchemaStore })); // LsMsgPack reads it with the store
      }
    }

    [Test]
    public async Task UnknownSchemaIdsGetTheSchemaInline()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Get("/test/animal", "00112233445566778899aabbccddeeff,0123456789abcdef0123456789abcdef"));

        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(IsReference(body), Is.False);
        Assert.That(((Dog)MsgPackSerializer.Deserialize<Animal>(body)).Barks, Is.EqualTo(3));
      }
    }

    [Test]
    public async Task GrownSchemaGetsANewId()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        LtMsgPackHttpSerializer clientSide = new LtMsgPackHttpSerializer();
        Uri server = new Uri("http://localhost/");

        HttpResponseMessage dog = await client.SendAsync(Get("/test/animal", null));
        byte[] dogBody = await dog.Content.ReadAsByteArrayAsync();
        clientSide.LearnSchema(server, SchemaHeader(dog), dogBody, 0, dogBody.Length, MsgPackMediaTypes.XLsMsgPack);

        HttpResponseMessage cat = await client.SendAsync(Get("/test/cat", clientSide.GetSchemasHeader(server))); // Animal gets the properties of Cat: the schema grows
        byte[] catBody = await cat.Content.ReadAsByteArrayAsync();
        Assert.That(SchemaHeader(cat), Is.Not.EqualTo(SchemaHeader(dog)));
        Assert.That(IsReference(catBody), Is.False);
        Assert.That(clientSide.LearnSchema(server, SchemaHeader(cat), catBody, 0, catBody.Length, MsgPackMediaTypes.XLsMsgPack), Is.True);
        Assert.That(((Cat)clientSide.Deserialize(typeof(Animal), catBody, 0, catBody.Length, MsgPackMediaTypes.XLsMsgPack)).Purrs, Is.True);

        HttpResponseMessage again = await client.SendAsync(Get("/test/animal", clientSide.GetSchemasHeader(server))); // the dog is written with the grown schema
        byte[] againBody = await again.Content.ReadAsByteArrayAsync();
        Assert.That(SchemaHeader(again), Is.EqualTo(SchemaHeader(cat)));
        Assert.That(IsReference(againBody), Is.True);
        Assert.That(((Dog)clientSide.Deserialize(typeof(Animal), againBody, 0, againBody.Length, MsgPackMediaTypes.XLsMsgPack)).Barks, Is.EqualTo(3));
      }
    }

    [Test]
    public async Task NegotiationCanBeTurnedOff()
    {
      (IHost host, HttpClient client) = await StartAsync(mvc => mvc.AddLsMsgPackSerializerFormatters(settings => settings.NegotiateSchemas = false));
      using (host)
      {
        HttpResponseMessage first = await client.SendAsync(Get("/test/animal", null));
        Assert.That(SchemaHeader(first), Is.Null);
        Assert.That(first.Headers.Vary, Is.Empty);
        byte[] body = await first.Content.ReadAsByteArrayAsync();
        Assert.That(body, Is.EqualTo(MsgPackSerializer.Serialize<Animal>(new Dog { Name = "Rex", Barks = 3 })));
      }
    }

    [Test]
    public async Task PlainMediaTypesDoNotNegotiate()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/test/animal");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.MsgPack));
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(SchemaHeader(response), Is.Null);
        Assert.That(response.Headers.Vary, Is.Empty);
      }
    }

    [Test]
    public async Task PresetForPlainMediaTypes()
    {
      (IHost host, HttpClient client) = await StartAsync(mvc => mvc.AddLsMsgPackSerializerFormatters(settings => settings.Plain = LtMsgPackPresets.MessagePackCSharp()));
      using (host)
      {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/test/receipt");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.MsgPack));
        HttpResponseMessage response = await client.SendAsync(request);

        Dictionary<string, object> receipt = MsgPackSerializer.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsByteArrayAsync(), Plain);
        Assert.That(receipt["Id"], Is.EqualTo(ReceiptId.ToString("D")));
        Assert.That(receipt["Total"], Is.EqualTo("12.50"));
      }
    }
  }
}
