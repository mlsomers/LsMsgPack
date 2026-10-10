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
using Microsoft.Extensions.Logging;
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

  /// <summary>
  /// An Order of another version: with a property Order does not have.
  /// </summary>
  public class OrderWithExtra
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public string Extra { get; set; }
  }

  public class ShippingAddress
  {
    public string Street { get; set; }
    public string City { get; set; }
  }

  public class Shipment
  {
    public int Id { get; set; }
    public ShippingAddress Address { get; set; }
  }

  /// <summary>
  /// A Shipment of another version: Address is called Destination.
  /// </summary>
  public class ShipmentWithDestination
  {
    public int Id { get; set; }
    public ShippingAddress Destination { get; set; }
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

    [HttpPost("differences")]
    public string Differences(Order order) => HttpContext.GetReadDifferences()?.GenerateReport() ?? "none";

    [HttpPost("shipment")] // the example of docs/WebFormatters.md#reporting-differences
    public IActionResult PostShipment(Shipment shipment)
    {
      if (shipment.Address == null)
        return BadRequest("Validation failed: address is required\r\n" + HttpContext.GetReadDifferences());
      return Ok(shipment);
    }

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

    /// <summary>
    /// Keeps the warnings that are logged.
    /// </summary>
    private sealed class WarningsLogger : ILoggerProvider, ILogger
    {
      internal readonly List<string> Warnings = new List<string>();
      internal readonly List<string> DebugMessages = new List<string>();
      private readonly LogLevel _minimum;
      public WarningsLogger(LogLevel minimum = LogLevel.Warning) { _minimum = minimum; }
      public ILogger CreateLogger(string categoryName) => categoryName == typeof(LsMsgPackInputFormatter).FullName ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
      public void Dispose() { }
      public IDisposable BeginScope<TState>(TState state) => null;
      public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimum;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
      {
        List<string> messages = logLevel >= LogLevel.Warning ? Warnings : logLevel == LogLevel.Debug ? DebugMessages : null;
        if (messages != null)
          lock (messages)
            messages.Add(formatter(state, exception));
      }
    }

    /// <summary>
    /// An order with a property the class does not have.
    /// </summary>
    private static byte[] OrderWithExtra(string mediaType)
    {
      OrderWithExtra order = new OrderWithExtra() { Id = 42, Customer = "Infotopie", Extra = "not in Order" };
      return MsgPackSerializer.Serialize(order, MsgPackMediaTypes.IsLsMsgPack(mediaType) ? new MsgPackSettings() : Plain);
    }

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

    [TestCase(MsgPackMediaTypes.MsgPack)]
    [TestCase(MsgPackMediaTypes.XLsMsgPack)]
    public async Task LargeBodyRoundTrips(string mediaType)
    {
      // Larger than one buffer of the request pipe: the input formatter reads it from a copy
      Order large = new Order { Id = 7, Customer = new string('c', 5000), Amounts = Enumerable.Range(0, 50000).Select(i => i * 0.5).ToArray() };
      MsgPackSettings settings = mediaType == MsgPackMediaTypes.MsgPack ? Plain : new MsgPackSettings();
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Post(MsgPackSerializer.Serialize(large, settings), mediaType, mediaType));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Order echoed = MsgPackSerializer.Deserialize<Order>(await response.Content.ReadAsByteArrayAsync(), settings);
        Assert.That(echoed.Customer, Is.EqualTo(large.Customer));
        Assert.That(echoed.Amounts, Is.EqualTo(large.Amounts));
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

    [TestCase(MsgPackMediaTypes.MsgPack)]
    [TestCase(MsgPackMediaTypes.XLsMsgPack)]
    public async Task DifferencesAreReportedWhenAskedFor(string mediaType)
    {
      WarningsLogger logger = new WarningsLogger();
      (IHost host, HttpClient client) = await StartAsync(mvc =>
      {
        mvc.Services.AddLogging(logging => logging.AddProvider(logger));
        mvc.AddLsMsgPackSerializerFormatters(settings =>
        {
          settings.ReportDifferences = true;
          settings.LogDifferencesAsWarning = true;
        });
      });
      using (host)
      {
        HttpRequestMessage request = Post(OrderWithExtra(mediaType), mediaType, "text/plain");
        request.RequestUri = new Uri("/test/differences", UriKind.Relative);
        HttpResponseMessage response = await client.SendAsync(request);

        string report = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), report);
        Assert.That(report, Does.Contain("Order.Extra: not a property of the class, skipped, 1 time"));
        Assert.That(logger.Warnings.Single(), Does.Contain("The request body of POST /test/differences did not match Order:").And.Contain("Order.Extra"));
      }
    }

    [Test]
    public async Task DifferencesAreLoggedAtDebugLevelByDefault()
    {
      WarningsLogger logger = new WarningsLogger(LogLevel.Debug);
      (IHost host, HttpClient client) = await StartAsync(mvc =>
      {
        mvc.Services.AddLogging(logging => logging.AddProvider(logger).SetMinimumLevel(LogLevel.Debug));
        mvc.AddLsMsgPackSerializerFormatters(settings => settings.ReportDifferences = true);
      });
      using (host)
      {
        HttpRequestMessage request = Post(OrderWithExtra(MsgPackMediaTypes.MsgPack), MsgPackMediaTypes.MsgPack, "text/plain");
        request.RequestUri = new Uri("/test/differences", UriKind.Relative);
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Order.Extra"));
        Assert.That(logger.Warnings, Is.Empty);
        Assert.That(logger.DebugMessages.Single(), Does.Contain("The request body of POST /test/differences did not match Order:").And.Contain("Order.Extra"));
      }
    }

    [Test]
    public async Task LogDifferencesAsWarningNeedsReportDifferences()
    {
      WarningsLogger logger = new WarningsLogger(LogLevel.Debug);
      (IHost host, HttpClient client) = await StartAsync(mvc =>
      {
        mvc.Services.AddLogging(logging => logging.AddProvider(logger).SetMinimumLevel(LogLevel.Debug));
        mvc.AddLsMsgPackSerializerFormatters(settings => settings.LogDifferencesAsWarning = true);
      });
      using (host)
      {
        HttpRequestMessage request = Post(OrderWithExtra(MsgPackMediaTypes.MsgPack), MsgPackMediaTypes.MsgPack, "text/plain");
        request.RequestUri = new Uri("/test/differences", UriKind.Relative);
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("none"));
        Assert.That(logger.Warnings, Is.Empty);
        Assert.That(logger.DebugMessages, Is.Empty);
      }
    }

    [TestCase(MsgPackMediaTypes.MsgPack)]
    [TestCase(MsgPackMediaTypes.XLsMsgPack)]
    public async Task MissingValueAnsweredWithTheDifferences(string mediaType)
    {
      (IHost host, HttpClient client) = await StartAsync(mvc => mvc.AddLsMsgPackSerializerFormatters(settings => settings.ReportDifferences = true));
      using (host)
      {
        ShipmentWithDestination shipment = new ShipmentWithDestination() { Id = 7, Destination = new ShippingAddress() { Street = "Main street 1", City = "Utrecht" } };
        byte[] body = MsgPackSerializer.Serialize(shipment, MsgPackMediaTypes.IsLsMsgPack(mediaType) ? new MsgPackSettings() : Plain);
        HttpRequestMessage request = Post(body, mediaType, "text/plain");
        request.RequestUri = new Uri("/test/shipment", UriKind.Relative);
        HttpResponseMessage response = await client.SendAsync(request);

        string message = await response.Content.ReadAsStringAsync();
        TestContext.WriteLine(message);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), message);
        Assert.That(message, Does.StartWith("Validation failed: address is required\r\n").And.Contain("Shipment.Destination: not a property of the class"));
      }
    }

    [Test]
    public async Task DifferencesAreNotCollectedByDefault()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpRequestMessage request = Post(OrderWithExtra(MsgPackMediaTypes.MsgPack), MsgPackMediaTypes.MsgPack, "text/plain");
        request.RequestUri = new Uri("/test/differences", UriKind.Relative);
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("none"));
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
