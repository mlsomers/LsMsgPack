using LsMsgPack;
using LsMsgPackFormatters;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
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
using System.Text.Json;
using System.Threading.Tasks;

namespace LsMsgPackFormattersTests
{
  /// <summary>
  /// Minimal APIs: MsgPackResults / Results.Extensions.MsgPack and MsgPackBody&lt;T&gt;, with the serializer of AddLsMsgPack.
  /// </summary>
  [TestFixture]
  public class MinimalApiTests
  {
    private static readonly Order SampleOrder = new Order { Id = 42, Customer = "Infotopie", Amounts = new[] { 1.5, 2.25 } };

    private static MsgPackSettings Plain => new MsgPackSettings { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Never, ObjectLayout = ObjectLayout.Map };

#nullable enable // as in a new project: ASP.NET Core takes a parameter without annotations as optional
    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
      endpoints.MapPost("/min/echo", (MsgPackBody<Order> order) => MsgPackResults.MsgPack(order.Value));
      endpoints.MapGet("/min/order", () => MsgPackResults.MsgPack(SampleOrder));
      endpoints.MapGet("/min/animal", () => MsgPackResults.MsgPack<Animal>(new Dog { Name = "Rex", Barks = 3 }));
      endpoints.MapGet("/min/object", () => MsgPackResults.MsgPack<object>(new Dog { Name = "Rex", Barks = 3 }));
      endpoints.MapPost("/min/created", (MsgPackBody<Order> order) => Results.Extensions.MsgPack(order.Value, StatusCodes.Status201Created));
      endpoints.MapPost("/min/optional", (MsgPackBody<Order>? order) => order is null ? "none" : order.Value.Customer);
      endpoints.MapPost("/min/differences", (MsgPackBody<Order> order, HttpContext context) => context.GetReadDifferences()?.GenerateReport() ?? "none");
      endpoints.MapPost("/min/double", (MsgPackBody<int> value) => MsgPackResults.MsgPack(value.Value * 2L));
    }
#nullable restore

    /// <param name="kestrel">On a socket: how the server answers exceptions (TestServer passes them to the client)</param>
    private static async Task<(IHost host, HttpClient client)> StartAsync(Action<IServiceCollection> services = null, bool kestrel = false)
    {
      IHost host = await new HostBuilder()
        .ConfigureWebHost(web =>
        {
          if (kestrel)
            web.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
          else
            web.UseTestServer();
          web.ConfigureServices(s =>
            {
              s.AddRouting();
              (services ?? (x => x.AddLsMsgPack()))(s);
            })
            .Configure(app => app.UseRouting().UseEndpoints(MapEndpoints));
        })
        .StartAsync();
      if (!kestrel)
        return (host, host.GetTestClient());
      string address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
      return (host, new HttpClient { BaseAddress = new Uri(address) });
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string accept, byte[] body = null, string contentType = null)
    {
      HttpRequestMessage request = new HttpRequestMessage(method, path);
      if (body != null)
      {
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
      }
      if (accept != null)
        request.Headers.TryAddWithoutValidation("Accept", accept);
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
    [TestCase(MsgPackMediaTypes.XLsMsgPack)]
    public async Task BodyAndResultRoundTrip(string mediaType)
    {
      MsgPackSettings settings = MsgPackMediaTypes.IsLsMsgPack(mediaType) ? new MsgPackSettings() : Plain;
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Post, "/min/echo", mediaType, MsgPackSerializer.Serialize(SampleOrder, settings), mediaType));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(mediaType));
        Assert.That(response.Headers.Vary, Does.Contain("Accept"));
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(body, Is.EqualTo(MsgPackSerializer.Serialize(SampleOrder, settings)), "The same bytes as the output formatter");
        AssertSampleOrder(MsgPackSerializer.Deserialize<Order>(body, settings));
      }
    }

    /// <summary>
    /// The same as MVC with the formatters: JSON unless the Accept header prefers MsgPack (*/* makes MVC ignore the header).
    /// </summary>
    [TestCase(null, null)]
    [TestCase("application/json", null)]
    [TestCase("*/*", null)]
    [TestCase("application/msgpack, */*", null)]
    [TestCase("application/json, application/msgpack", null)]
    [TestCase("application/json;q=0.5, application/msgpack", MsgPackMediaTypes.MsgPack)]
    [TestCase("application/msgpack;q=0.5, application/*", null)]
    [TestCase("application/msgpack;q=0, application/x-msgpack;q=0.2", MsgPackMediaTypes.XMsgPack)]
    [TestCase("text/plain, application/x-msgpack", MsgPackMediaTypes.XMsgPack)]
    [TestCase("application/x-lsmsgpack; schema=described, application/x-lsmsgpack", MsgPackMediaTypes.XLsMsgPack)]
    public async Task ResultFormatFollowsAccept(string accept, string expected)
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Get, "/min/order", accept));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(expected ?? "application/json"));
        Assert.That(response.Headers.Vary, Does.Contain("Accept"));
        if (expected is null)
          AssertSampleOrder(JsonSerializer.Deserialize<Order>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
      }
    }

    [Test]
    public async Task JsonBodyIsRead()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        byte[] json = System.Text.Encoding.UTF8.GetBytes("{\"id\":42,\"customer\":\"Infotopie\",\"amounts\":[1.5,2.25]}");
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Post, "/min/echo", MsgPackMediaTypes.MsgPack, json, "application/json; charset=utf-8"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        AssertSampleOrder(MsgPackSerializer.Deserialize<Order>(await response.Content.ReadAsByteArrayAsync(), Plain));
      }
    }

    [Test]
    public async Task DeclaredTypeGetsATypeId()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Get, "/min/animal", MsgPackMediaTypes.XLsMsgPack));
        Animal animal = MsgPackSerializer.Deserialize<Animal>(await response.Content.ReadAsByteArrayAsync());
        Assert.That(animal, Is.TypeOf<Dog>());
        Assert.That(((Dog)animal).Barks, Is.EqualTo(3));

        // JSON: the value's class, as MVC's JSON formatter writes it
        HttpResponseMessage json = await client.SendAsync(Request(HttpMethod.Get, "/min/animal", "application/json"));
        Assert.That(await json.Content.ReadAsStringAsync(), Does.Contain("\"barks\":3"));
      }
    }

    /// <summary>
    /// As MVC's ObjectResult: a value declared as object is written as its class (no type id on the root).
    /// </summary>
    [Test]
    public async Task ObjectIsWrittenAsItsClass()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Get, "/min/object", MsgPackMediaTypes.XLsMsgPack));
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(body, Is.EqualTo(MsgPackSerializer.Serialize(new Dog { Name = "Rex", Barks = 3 })));
      }
    }

    [Test]
    public async Task ResultExtensionsWithStatusCode()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Post, "/min/created", MsgPackMediaTypes.MsgPack, MsgPackSerializer.Serialize(SampleOrder, Plain), MsgPackMediaTypes.MsgPack));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        AssertSampleOrder(MsgPackSerializer.Deserialize<Order>(await response.Content.ReadAsByteArrayAsync(), Plain));
      }
    }

    [Test]
    public async Task PrimitiveBodyRoundTrips()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Post, "/min/double", MsgPackMediaTypes.MsgPack, MsgPackSerializer.Serialize(21, Plain), MsgPackMediaTypes.MsgPack));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(MsgPackSerializer.Deserialize<long>(await response.Content.ReadAsByteArrayAsync(), Plain), Is.EqualTo(42));
      }
    }

    [Test]
    public async Task SchemasAreNegotiated()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage first = await client.SendAsync(Request(HttpMethod.Get, "/min/order", MsgPackMediaTypes.XLsMsgPack));
        string schemaId = first.Headers.GetValues(LtMsgPackHttpSerializer.SchemaHeader).Single();
        Assert.That(first.Headers.Vary, Does.Contain(LtMsgPackHttpSerializer.SchemasHeader).And.Contain("Accept"));

        HttpRequestMessage again = Request(HttpMethod.Get, "/min/order", MsgPackMediaTypes.XLsMsgPack);
        again.Headers.Add(LtMsgPackHttpSerializer.SchemasHeader, schemaId);
        byte[] reference = await (await client.SendAsync(again)).Content.ReadAsByteArrayAsync();
        Assert.That(reference[0], Is.EqualTo(0xD8), "The client holds the schema: a reference");
        Assert.That(reference.Length, Is.LessThan((await first.Content.ReadAsByteArrayAsync()).Length));
      }
    }

    [Test]
    public async Task DescribedSchemaWhenAskedFor()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Get, "/min/order", "application/x-lsmsgpack; schema=described, application/x-lsmsgpack"));
        Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(MsgPackMediaTypes.XLsMsgPack));
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        Dictionary<string, object> schema = MsgPackSerializer.Deserialize<Dictionary<string, object>>(body, Plain); // the first item
        Assert.That(schema, Does.ContainKey(string.Empty), "Version 2: the root entry");
        Assert.That(response.Headers.Vary, Does.Contain("Accept"));
        AssertSampleOrder(MsgPackSerializer.Deserialize<Order>(body));
      }
    }

    [Test]
    public async Task DifferencesAreReportedWhenAskedFor()
    {
      (IHost host, HttpClient client) = await StartAsync(services => services.AddLsMsgPack(o => o.ReportDifferences = true));
      using (host)
      {
        OrderWithExtra order = new OrderWithExtra() { Id = 42, Customer = "Infotopie", Extra = "not in Order" };
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Post, "/min/differences", "text/plain", MsgPackSerializer.Serialize(order, Plain), MsgPackMediaTypes.MsgPack));

        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Order.Extra: not a property of the class, skipped, 1 time"));
      }
    }

    [Test]
    public async Task EmptyBodyIsNoValue()
    {
      (IHost host, HttpClient client) = await StartAsync(kestrel: true);
      using (host)
      using (client)
      {
        HttpResponseMessage optional = await client.SendAsync(Request(HttpMethod.Post, "/min/optional", null, Array.Empty<byte>(), MsgPackMediaTypes.MsgPack));
        Assert.That(await optional.Content.ReadAsStringAsync(), Is.EqualTo("none"));
        HttpResponseMessage nil = await client.SendAsync(Request(HttpMethod.Post, "/min/optional", null, new byte[] { 0xC0 }, MsgPackMediaTypes.MsgPack));
        Assert.That(await nil.Content.ReadAsStringAsync(), Is.EqualTo("none"));
        HttpResponseMessage given = await client.SendAsync(Request(HttpMethod.Post, "/min/optional", null, MsgPackSerializer.Serialize(SampleOrder, Plain), MsgPackMediaTypes.MsgPack));
        Assert.That(await given.Content.ReadAsStringAsync(), Is.EqualTo(SampleOrder.Customer));

        HttpResponseMessage required = await client.SendAsync(Request(HttpMethod.Post, "/min/echo", MsgPackMediaTypes.MsgPack, Array.Empty<byte>(), MsgPackMediaTypes.MsgPack));
        Assert.That(required.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
      }
    }

    [TestCase(new byte[] { 0xC1 }, TestName = "MalformedBodyGives400(never used type)")]
    [TestCase(new byte[] { 0x83, 0xA2, 0x49 }, TestName = "MalformedBodyGives400(truncated)")]
    [TestCase(new byte[] { 0xA5, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, TestName = "MalformedBodyGives400(string instead of map)")]
    public async Task MalformedBodyGives400(byte[] body)
    {
      (IHost host, HttpClient client) = await StartAsync(kestrel: true);
      using (host)
      using (client)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Post, "/min/echo", MsgPackMediaTypes.MsgPack, body, MsgPackMediaTypes.MsgPack));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
      }
    }

    [Test]
    public async Task UnsupportedContentTypeGives415()
    {
      (IHost host, HttpClient client) = await StartAsync(kestrel: true);
      using (host)
      using (client)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Post, "/min/echo", MsgPackMediaTypes.MsgPack, new byte[] { 0x68, 0x69 }, "text/plain"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
      }
    }

    [Test]
    public async Task WithoutAddLsMsgPackTheResultSaysSo()
    {
      (IHost host, HttpClient client) = await StartAsync(services => { });
      using (host)
      {
        InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() => client.SendAsync(Request(HttpMethod.Get, "/min/order", MsgPackMediaTypes.MsgPack))));
        Assert.That(ex.Message, Does.Contain("AddLsMsgPack()"));
      }
    }

    [Test]
    public async Task EndpointMetadataForOpenApi()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        IEnumerable<Endpoint> endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        Endpoint echo = endpoints.Single(e => (e as RouteEndpoint)?.RoutePattern.RawText == "/min/echo");

        IProducesResponseTypeMetadata produces = echo.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Single(m => m.Type == typeof(Order));
        Assert.That(produces.StatusCode, Is.EqualTo(200));
        Assert.That(produces.ContentTypes, Is.EqualTo(new[] { "application/json", MsgPackMediaTypes.MsgPack, MsgPackMediaTypes.XMsgPack, MsgPackMediaTypes.XLsMsgPack }));

        IAcceptsMetadata accepts = echo.Metadata.GetMetadata<IAcceptsMetadata>();
        Assert.That(accepts.RequestType, Is.EqualTo(typeof(Order)));
        Assert.That(accepts.IsOptional, Is.False);
        Assert.That(accepts.ContentTypes, Does.Contain(MsgPackMediaTypes.XLsMsgPack));

        Endpoint optional = endpoints.Single(e => (e as RouteEndpoint)?.RoutePattern.RawText == "/min/optional");
        Assert.That(optional.Metadata.GetMetadata<IAcceptsMetadata>().IsOptional, Is.True);
      }
    }

    /// <summary>
    /// The options of the services: AddLsMsgPack and AddLsMsgPackSerializerFormatters configure the same LtMsgPackHttpOptions.
    /// </summary>
    [Test]
    public async Task SetupActionIsApplied()
    {
      (IHost host, HttpClient client) = await StartAsync(services => services.AddLsMsgPack(o => o.XLsMsgPack.UseInexedSchema = false));
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Request(HttpMethod.Get, "/min/order", MsgPackMediaTypes.XLsMsgPack));
        Assert.That(MsgPackItem.UnpackMultiple(await response.Content.ReadAsByteArrayAsync()).Count, Is.EqualTo(1), "UseInexedSchema = false from the setup action should be used");
      }
    }
  }
}
