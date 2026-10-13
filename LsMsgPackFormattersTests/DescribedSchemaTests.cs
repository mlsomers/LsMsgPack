using LsMsgPack;
using LsMsgPackFormatters;
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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace LsMsgPackFormattersTests
{
  public enum DescribedStatus { Draft, Sent }

  /// <summary>
  /// A property of every kind the described schema names.
  /// </summary>
  public class DescribedKinds
  {
    public int Count { get; set; }
    public long Big { get; set; }
    public ulong Huge { get; set; }
    public bool Flag { get; set; }
    public double Ratio { get; set; }
    public string Text { get; set; }
    public decimal Amount { get; set; }
    public Guid Id { get; set; }
    public DateTime When { get; set; }
    public DateTimeOffset At { get; set; }
    public TimeSpan Duration { get; set; }
    public DateOnly Day { get; set; }
    public char Letter { get; set; }
    public byte[] Data { get; set; }
    public DescribedStatus Status { get; set; }
    public int? Maybe { get; set; }
    [DefaultValue("Woof")] public string Sound { get; set; } = "Woof";
    [DefaultValue(7)] public int Seven { get; set; } = 7;
    public ShippingAddress Address { get; set; }
    public List<ShippingAddress> Addresses { get; set; }
    public IEnumerable<ShippingAddress> Others { get; set; } // a List: the collection gets a type id and an entry
    public Dictionary<string, int> Counts { get; set; }
    public Animal Pet { get; set; }
    public KeyValuePair<string, int> Pair { get; set; }
    public object Anything { get; set; }
    public DescribedOrigin Point { get; set; } // only its default (left out): no entry
    public DescribedPoint Corner { get; set; } // written: an entry
  }

  public struct DescribedOrigin
  {
    public int X { get; set; }
    public int Y { get; set; }
  }

  public struct DescribedPoint
  {
    public int X { get; set; }
    public int Y { get; set; }
  }

  [ApiController]
  [Route("described")]
  public class DescribedController : ControllerBase
  {
    internal static DescribedKinds Sample() => new DescribedKinds
    {
      Count = 3,
      Text = "text",
      Amount = 12.5m,
      Address = new ShippingAddress { Street = "Main Street", City = "Utrecht" },
      Addresses = new List<ShippingAddress> { new ShippingAddress { City = "Paris" } },
      Others = new List<ShippingAddress> { new ShippingAddress { City = "Rome" } },
      Counts = new Dictionary<string, int> { { "a", 1 } },
      Pet = new Dog { Name = "Rex", Barks = 2 },
      Corner = new DescribedPoint { X = 1 },
    };

    [HttpGet("kinds")]
    public DescribedKinds GetKinds() => Sample();

    [HttpGet("list")]
    public List<ShippingAddress> GetList() => new List<ShippingAddress> { new ShippingAddress { Street = "Main Street", City = "Utrecht" } };
  }

  [TestFixture]
  public class DescribedSchemaTests
  {
    private const string DescribedAccept = MsgPackMediaTypes.XLsMsgPack + "; " + MsgPackMediaTypes.DescribedSchemaParameter + ", " + MsgPackMediaTypes.XLsMsgPack + "; q=0.9"; // as the TypeScript client asks

    private static async Task<(IHost host, HttpClient client)> StartAsync(Action<LtMsgPackHttpOptions> configure = null)
    {
      IHost host = await new HostBuilder()
        .ConfigureWebHost(web => web
          .UseTestServer()
          .ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(DescribedController).Assembly).AddLsMsgPackSerializerFormatters(o => configure?.Invoke(o)))
          .Configure(app => app.UseRouting().UseEndpoints(endpoints => endpoints.MapControllers())))
        .StartAsync();
      return (host, host.GetTestClient());
    }

    private static async Task<Response> GetAsync(HttpClient client, string path, bool described, string schemas = null)
    {
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
      request.Headers.TryAddWithoutValidation("Accept", described ? DescribedAccept : MsgPackMediaTypes.XLsMsgPack);
      if (!(schemas is null))
        request.Headers.Add(LtMsgPackHttpSerializer.SchemasHeader, schemas);
      HttpResponseMessage response = await client.SendAsync(request);
      response.EnsureSuccessStatusCode();
      return new Response(response, await response.Content.ReadAsByteArrayAsync());
    }

    private sealed class Response
    {
      internal Response(HttpResponseMessage message, byte[] bytes)
      {
        Message = message;
        Bytes = bytes;
        SchemaId = message.Headers.TryGetValues(LtMsgPackHttpSerializer.SchemaHeader, out IEnumerable<string> values) ? values.Single() : null;
        IsReference = bytes.Length >= SchemaStore.ReferenceLength && bytes[0] == 0xD8 && bytes[1] == (byte)SchemaStore.ReferenceExtensionType;
        MemoryStream stream = new MemoryStream(bytes);
        Schema = IsReference ? null : MsgPackItem.Unpack(stream);
        int schemaLength = IsReference ? SchemaStore.ReferenceLength : (int)stream.Position;
        SchemaBytes = bytes.Take(schemaLength).ToArray();
        Body = bytes.Skip(schemaLength).ToArray();
      }

      internal HttpResponseMessage Message { get; }
      internal byte[] Bytes { get; }
      internal string SchemaId { get; }
      internal bool IsReference { get; }
      internal MsgPackItem Schema { get; }
      internal byte[] SchemaBytes { get; }
      internal byte[] Body { get; }

      /// <summary>
      /// The entries of the schema in order: name and items.
      /// </summary>
      internal List<KeyValuePair<string, object[]>> Entries
      {
        get { return ((KeyValuePair<object, object>[])Schema.Value).Select(e => new KeyValuePair<string, object[]>((string)e.Key, (object[])e.Value)).ToList(); }
      }

      internal int IndexOf(string typeName)
      {
        return Entries.FindIndex(e => e.Key == typeName);
      }

      /// <summary>
      /// The description of a property (or "@") of an entry: [name, type] or [name, type, default].
      /// </summary>
      internal object[] Property(string typeName, string property)
      {
        object[] items = Entries.Single(e => e.Key == typeName).Value;
        return items.OfType<object[]>().Single(p => (string)p[0] == property);
      }
    }

    private static string Hash(byte version, byte[] schema)
    {
      byte[] input = new byte[schema.Length + 1];
      input[0] = version;
      Buffer.BlockCopy(schema, 0, input, 1, schema.Length);
      return string.Concat(SHA256.HashData(input).Take(16).Select(b => b.ToString("x2")));
    }

    private static string Kind(object[] description) => description[1] as string;

    private static int Index(object type) => Convert.ToInt32(type);

    [Test]
    public async Task OnlyWhenAsked()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        Response plain = await GetAsync(client, "/described/kinds", false);
        Response described = await GetAsync(client, "/described/kinds", true);

        Assert.That(plain.SchemaId, Is.EqualTo(Hash(1, plain.SchemaBytes)), "The plain schema keeps its id");
        Assert.That(described.SchemaId, Is.EqualTo(Hash(2, described.SchemaBytes)), "The described schema hashes format version 2");
        Assert.That(described.SchemaId, Is.Not.EqualTo(plain.SchemaId));
        Assert.That(described.Body, Is.EqualTo(plain.Body), "The body does not change");
        Assert.That(plain.Entries.Select(e => e.Key), Is.EqualTo(described.Entries.Select(e => e.Key).Take(plain.Entries.Count)), "The same entries in the same order (type ids)");
        Assert.That(described.Entries.Last().Key, Is.EqualTo(string.Empty), "The root entry comes last");
        Assert.That(described.Message.Headers.Vary, Does.Contain("Accept"));
        Assert.That(plain.Message.Headers.Vary, Does.Contain("Accept"), "Both variants vary with Accept, for caches");

        DescribedKinds read = MsgPackSerializer.Deserialize<DescribedKinds>(plain.Bytes); // a .NET client still gets what it reads
        Assert.That(read.Address.City, Is.EqualTo("Utrecht"));
      }
    }

    [Test]
    public async Task KindsOfTheProperties()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        Response r = await GetAsync(client, "/described/kinds", true);
        const string T = nameof(DescribedKinds);

        Assert.That(Kind(r.Property(T, "Count")), Is.EqualTo("int"));
        Assert.That(Kind(r.Property(T, "Big")), Is.EqualTo("long"));
        Assert.That(Kind(r.Property(T, "Huge")), Is.EqualTo("ulong"));
        Assert.That(Kind(r.Property(T, "Flag")), Is.EqualTo("bool"));
        Assert.That(Kind(r.Property(T, "Ratio")), Is.EqualTo("float"));
        Assert.That(Kind(r.Property(T, "Text")), Is.EqualTo("string"));
        Assert.That(Kind(r.Property(T, "Amount")), Is.EqualTo("decimal"));
        Assert.That(Kind(r.Property(T, "Id")), Is.EqualTo("guid"));
        Assert.That(Kind(r.Property(T, "When")), Is.EqualTo("datetime"));
        Assert.That(Kind(r.Property(T, "At")), Is.EqualTo("dto"));
        Assert.That(Kind(r.Property(T, "Duration")), Is.EqualTo("timespan"));
        Assert.That(Kind(r.Property(T, "Day")), Is.EqualTo("dateonly"));
        Assert.That(Kind(r.Property(T, "Letter")), Is.EqualTo("char"));
        Assert.That(Kind(r.Property(T, "Data")), Is.EqualTo("bin"));
        Assert.That(Kind(r.Property(T, "Status")), Is.EqualTo("int"), "Enums are their underlying integer");
        Assert.That(r.Property(T, "Maybe")[1], Is.EqualTo(new object[] { "?", "int" }));
        Assert.That(Kind(r.Property(T, "Anything")), Is.EqualTo("any"));
        Assert.That(Kind(r.Property(T, "Pet")), Is.EqualTo("any"), "An abstract class: the value has a type id");
        Assert.That(r.Property(T, "Pair")[1], Is.EqualTo(new object[] { "pair", "string", "int" }));
        Assert.That(r.Property(T, "Counts")[1], Is.EqualTo(new object[] { "dict", "string", "int" }));

        int address = r.IndexOf(nameof(ShippingAddress));
        Assert.That(address, Is.GreaterThan(0));
        Assert.That(Index(r.Property(T, "Address")[1]), Is.EqualTo(address));
        object[] addresses = (object[])r.Property(T, "Addresses")[1];
        Assert.That(addresses[0], Is.EqualTo("list"));
        Assert.That(Index(addresses[1]), Is.EqualTo(address));

        Assert.That(r.Property(T, "Sound"), Has.Length.EqualTo(3));
        Assert.That(r.Property(T, "Sound")[2], Is.EqualTo("Woof"), "[DefaultValue]");
        Assert.That(Convert.ToInt32(r.Property(T, "Seven")[2]), Is.EqualTo(7));
        Assert.That(r.Property(T, "Count"), Has.Length.EqualTo(2), "No default beyond the type's");

        object[] point = (object[])r.Property(T, "Point")[1];
        Assert.That(point[0], Is.EqualTo("struct"), "A struct without an entry has its properties inline (for its default)");
        Assert.That(((object[])point[1]).Cast<object[]>().Select(p => (string)p[0]), Is.EqualTo(new[] { "X", "Y" }));
        object[] corner = (object[])r.Property(T, "Corner")[1];
        Assert.That(corner[0], Is.EqualTo("struct"));
        Assert.That(Index(corner[1]), Is.EqualTo(r.IndexOf(nameof(DescribedPoint))));

        Assert.That(Index(r.Property(string.Empty, "@")[1]), Is.EqualTo(r.IndexOf(T)), "The root");
        Assert.That(Kind(r.Property(nameof(Dog), "Barks")), Is.EqualTo("int"));

        object[] list = (object[])r.Property("List<ShippingAddress>", "@")[1];
        Assert.That(list[0], Is.EqualTo("list"), "A collection with a type id describes its content");
        Assert.That(Index(list[1]), Is.EqualTo(address));
      }
    }

    [Test]
    public async Task ListRoot()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        Response r = await GetAsync(client, "/described/list", true);

        object[] root = (object[])r.Property(string.Empty, "@")[1];
        Assert.That(root[0], Is.EqualTo("list"));
        Assert.That(Index(root[1]), Is.EqualTo(r.IndexOf(nameof(ShippingAddress))));
      }
    }

    [Test]
    public async Task ClientThatHoldsTheDescribedSchemaGetsAReference()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        Response first = await GetAsync(client, "/described/kinds", true);
        Response second = await GetAsync(client, "/described/kinds", true, first.SchemaId);

        Assert.That(second.IsReference, Is.True);
        Assert.That(second.SchemaId, Is.EqualTo(first.SchemaId));
        Assert.That(string.Concat(second.Bytes.Skip(2).Take(16).Select(b => b.ToString("x2"))), Is.EqualTo(first.SchemaId), "The reference names the described schema");
        Assert.That(second.Body, Is.EqualTo(first.Body));

        Response plain = await GetAsync(client, "/described/kinds", false, first.SchemaId);
        Assert.That(plain.IsReference, Is.False, "A described id does not stand for the plain schema");
      }
    }

    [Test]
    public async Task CanBeTurnedOff()
    {
      (IHost host, HttpClient client) = await StartAsync(o => o.DescribeSchemas = false);
      using (host)
      {
        Response r = await GetAsync(client, "/described/kinds", true);
        Assert.That(r.SchemaId, Is.EqualTo(Hash(1, r.SchemaBytes)));
        Assert.That(r.Entries.Last().Key, Is.Not.EqualTo(string.Empty));
        Assert.That(r.Message.Headers.Vary, Does.Not.Contain("Accept"));
      }
    }

    [TestCase("application/x-lsmsgpack; schema=described", true)]
    [TestCase("application/json, application/x-lsmsgpack;schema=\"described\";q=0.9", true)]
    [TestCase("APPLICATION/X-LSMSGPACK; Schema=Described", true)]
    [TestCase("application/x-lsmsgpack; schema=described; q=0", false)]
    [TestCase("application/x-lsmsgpack", false)]
    [TestCase("application/msgpack; schema=described", false)]
    [TestCase("application/x-lsmsgpack; schema=plain, text/described", false)]
    [TestCase(null, false)]
    public void AcceptHeader(string accept, bool expected)
    {
      Assert.That(MsgPackMediaTypes.AcceptsDescribedSchema(accept), Is.EqualTo(expected));
    }
  }
}
