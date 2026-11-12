using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.Streaming;
using LsMsgPackFormatters;
using LtMsgPack;
using LtMsgPack.Http;
using LtMsgPack.Streaming;
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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackFormattersTests
{
  [ApiController]
  [Route("streaming")]
  public class StreamingController : ControllerBase
  {
    [HttpGet("orders")]
    public List<Order> GetOrders() => Enumerable.Range(1, 3).Select(StreamingResponseTests.MakeOrder).ToList();

    [HttpGet("orders-async")]
    public IAsyncEnumerable<Order> GetOrdersAsync() => StreamingResponseTests.OrdersAsync(3);

    [HttpGet("orders-ok")]
    public IActionResult GetOrdersOk() => Ok(StreamingResponseTests.OrdersAsync(3));

    [HttpGet("numbers-async")]
    public async IAsyncEnumerable<int> GetNumbersAsync()
    {
      for (int i = 1; i <= 3; i++)
      {
        await Task.Yield();
        yield return i;
      }
    }

    [HttpGet("pairs-async")]
    public async IAsyncEnumerable<KeyValuePair<string, int>> GetPairsAsync()
    {
      await Task.Yield();
      yield return new KeyValuePair<string, int>("a", 1);
    }

    [HttpGet("gated")]
    public IAsyncEnumerable<Order> GetGated() => StreamingResponseTests.GatedAsync();

    [HttpGet("failing")]
    public IAsyncEnumerable<Order> GetFailing([FromQuery] int after) => StreamingResponseTests.FailingAsync(after);

    [HttpGet("result")]
    public IActionResult GetResult() => StreamingResponseTests.TwoTables();
  }

  /// <summary>
  /// Responses that are streams: an IAsyncEnumerable&lt;T&gt; for a client that asks for it (Accept with <see cref="MsgPackMediaTypes.StreamParameter"/>), and <see cref="MsgPackStreamResult"/>.
  /// </summary>
  [TestFixture]
  public class StreamingResponseTests
  {
    private const string LsStream = "application/x-lsmsgpack; stream=unknown-length, application/x-lsmsgpack; q=0.9";
    private const string PlainStream = "application/msgpack; stream=unknown-length, application/msgpack; q=0.9";
    private const string DescribedStream = "application/x-lsmsgpack; schema=described; stream=unknown-length, application/x-lsmsgpack; q=0.9";

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

    /// <summary>
    /// The first order, then the second when the test releases <see cref="_gate"/>: the first must reach the client before that.
    /// </summary>
    internal static async IAsyncEnumerable<Order> GatedAsync()
    {
      yield return MakeOrder(1);
      await _gate.WaitAsync(TimeSpan.FromSeconds(20));
      yield return MakeOrder(2);
    }

    internal static async IAsyncEnumerable<Order> FailingAsync(int after)
    {
      for (int i = 1; i <= after; i++)
      {
        await Task.Delay(1);
        yield return MakeOrder(i);
      }
      await Task.Delay(1);
      throw new InvalidOperationException("The query failed.");
    }

    /// <summary>
    /// Two root values of two entries (one a head and its body) and an index.
    /// </summary>
    internal static MsgPackStreamResult TwoTables()
    {
      return new MsgPackStreamResult(async (writer, cancellationToken) =>
      {
        writer.BeginEntry("orders");
        writer.WriteHead<List<Order>>();
        writer.WriteArrayStart();
        await foreach (Order order in OrdersAsync(3, cancellationToken))
          await writer.WriteObjectAsync(order, cancellationToken);
        writer.WriteEnd();
        writer.BeginEntry("last");
        writer.WriteObject(MakeOrder(9));
      }, new MsgPackStreamIndexOptions());
    }

    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
      endpoints.MapGet("/min/orders-async", () => MsgPackResults.MsgPack(OrdersAsync(3)));
      endpoints.MapGet("/min/result", () => TwoTables());
      endpoints.MapGet("/min/result-extension", () => Results.Extensions.MsgPackStream(async (writer, cancellationToken) =>
      {
        writer.Write("text");
        await writer.FlushAsync(cancellationToken);
      }));
    }

    private static async Task<(IHost host, HttpClient client)> StartAsync(Action<LtMsgPackHttpOptions> options = null)
    {
      IHost host = await new HostBuilder()
        .ConfigureWebHost(web => web
          .UseTestServer() // AllowSynchronousIO is false by default, as in Kestrel
          .ConfigureServices(services =>
          {
            services.AddControllers().AddApplicationPart(typeof(StreamingController).Assembly).AddLsMsgPackSerializerFormatters(options ?? (o => { }));
            services.AddLsMsgPack();
          })
          .Configure(app => app.UseRouting().UseEndpoints(endpoints =>
          {
            endpoints.MapControllers();
            MapEndpoints(endpoints);
          })))
        .StartAsync();
      return (host, host.GetTestClient());
    }

    private static HttpRequestMessage Get(string path, string accept)
    {
      HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
      if (accept != null)
        request.Headers.TryAddWithoutValidation("Accept", accept);
      return request;
    }

    private static async Task<byte[]> BodyAsync(HttpClient client, HttpRequestMessage request)
    {
      HttpResponseMessage response = await client.SendAsync(request);
      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
      return await response.Content.ReadAsByteArrayAsync();
    }

    private static LtMsgPackSerializer LsReader => new LtMsgPackSerializer(LtMsgPackPresets.LsMsgPack());

    private static LtMsgPackSerializer PlainReader => new LtMsgPackSerializer(MsgPackMediaTypes.ToPlain(new LtMsgPackOptions()));

    /// <summary>
    /// Reads the stream of an IAsyncEnumerable&lt;Order&gt;: a head, then an array of unknown length.
    /// </summary>
    private static List<Order> ReadOrders(byte[] body, LtMsgPackSerializer serializer)
    {
      List<Order> orders = new List<Order>();
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(body), serializer))
      {
        reader.ReadHead<List<Order>>();
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.TokenType, Is.EqualTo(MsgPackTokenType.ArrayStart));
        Assert.That(reader.IsUnknownLength, Is.True);
        while (!reader.AtEnd)
          orders.Add(reader.ReadObject<Order>());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.TokenType, Is.EqualTo(MsgPackTokenType.ArrayEnd));
        Assert.That(reader.Read(), Is.False, "One root value");
      }
      return orders;
    }

    /// <summary>
    /// The stream is the payload of the List&lt;T&gt; with an array of unknown length: C1 90 ... C1 C0 instead of the array header.
    /// </summary>
    private static void AssertListWithUnknownLength(byte[] stream, byte[] list)
    {
      int header = 0;
      while (header < list.Length && stream[header] == list[header])
        header++;
      Assert.That(list[header] & 0xF0, Is.EqualTo(0x90), "The fixarray header of the list");
      Assert.That(stream.Skip(header).Take(2), Is.EqualTo(new byte[] { 0xC1, 0x90 }), "The start of an array of unknown length");
      Assert.That(stream.Skip(header + 2).Take(stream.Length - header - 4), Is.EqualTo(list.Skip(header + 1)), "The same items");
      Assert.That(stream.Skip(stream.Length - 2), Is.EqualTo(new byte[] { 0xC1, 0xC0 }), "The end sequence");
    }

    [TestCase("/streaming/orders-async")]
    [TestCase("/streaming/orders-ok")]
    [TestCase("/min/orders-async")]
    public async Task AsyncEnumerableIsStreamedWhenAskedFor(string path)
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Get(path, LsStream));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(response.Content.Headers.ContentType.ToString(), Is.EqualTo("application/x-lsmsgpack; stream=unknown-length"));
        Assert.That(response.Headers.Vary, Does.Contain("Accept"));
        Assert.That(response.Headers.GetValues(LtMsgPackHttpSerializer.SchemaHeader).Single(), Has.Length.EqualTo(32), "The schema of the head");
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        List<Order> orders = ReadOrders(body, LsReader);
        Assert.That(orders.Select(o => o.Customer), Is.EqualTo(new[] { "c1", "c2", "c3" }));
        Assert.That(orders[2].Amounts, Is.EqualTo(new[] { 4.5 }));
        AssertListWithUnknownLength(body, await BodyAsync(client, Get("/streaming/orders", MsgPackMediaTypes.XLsMsgPack)));
      }
    }

    [Test]
    public async Task PlainMsgPackIsStreamed()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Get("/streaming/orders-async", PlainStream));
        Assert.That(response.Content.Headers.ContentType.ToString(), Is.EqualTo("application/msgpack; stream=unknown-length"));
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        Assert.That(body.Take(2), Is.EqualTo(new byte[] { 0xC1, 0x90 }), "No head without the indexed schema");
        Assert.That(ReadOrders(body, PlainReader).Select(o => o.Id), Is.EqualTo(new[] { 1, 2, 3 }));
        AssertListWithUnknownLength(body, await BodyAsync(client, Get("/streaming/orders", MsgPackMediaTypes.MsgPack)));
      }
    }

    [Test]
    public async Task ValueTypesAreStreamed()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        byte[] body = await BodyAsync(client, Get("/streaming/numbers-async", PlainStream));
        Assert.That(body, Is.EqualTo(new byte[] { 0xC1, 0x90, 1, 2, 3, 0xC1, 0xC0 }));
      }
    }

    /// <summary>
    /// A List&lt;KeyValuePair&lt;K, V&gt;&gt; is a map: not streamed.
    /// </summary>
    [Test]
    public async Task PairsAreNotStreamed()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Get("/streaming/pairs-async", PlainStream));
        Assert.That(response.Content.Headers.ContentType.ToString(), Is.EqualTo(MsgPackMediaTypes.MsgPack));
        Assert.That(MsgPackSerializer.Deserialize<Dictionary<string, int>>(await response.Content.ReadAsByteArrayAsync(), new MsgPackSettings { UseInexedSchema = false }), Is.EqualTo(new Dictionary<string, int> { { "a", 1 } }));
      }
    }

    /// <summary>
    /// Without the parameter (or with WriteStreams off) the response is the List&lt;T&gt; as before, which varies with the Accept header (also because of the described schema).
    /// </summary>
    [TestCase(MsgPackMediaTypes.XLsMsgPack, true)]
    [TestCase(LsStream, false)]
    public async Task NotStreamedOtherwise(string accept, bool writeStreams)
    {
      (IHost host, HttpClient client) = await StartAsync(o => o.WriteStreams = writeStreams);
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Get("/streaming/orders-async", accept));
        Assert.That(response.Content.Headers.ContentType.ToString(), Is.EqualTo(MsgPackMediaTypes.XLsMsgPack));
        Assert.That(response.Headers.Vary, Does.Contain("Accept"));
        Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(await BodyAsync(client, Get("/streaming/orders", MsgPackMediaTypes.XLsMsgPack))));
      }
    }

    [Test]
    public async Task HeadRefersToASchemaTheClientHolds()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage list = await client.SendAsync(Get("/streaming/orders", MsgPackMediaTypes.XLsMsgPack));
        string schemaId = list.Headers.GetValues(LtMsgPackHttpSerializer.SchemaHeader).Single();

        HttpRequestMessage request = Get("/streaming/orders-async", LsStream);
        request.Headers.Add(LtMsgPackHttpSerializer.SchemasHeader, schemaId);
        HttpResponseMessage response = await client.SendAsync(request);
        Assert.That(response.Headers.GetValues(LtMsgPackHttpSerializer.SchemaHeader).Single(), Is.EqualTo(schemaId), "The schema of List<Order>");
        Assert.That(response.Headers.Vary, Does.Contain(LtMsgPackHttpSerializer.SchemasHeader).And.Contain("Accept"));
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.That(body[0], Is.EqualTo(0xD8), "A reference");
        Assert.That(body.Skip(SchemaStore.ReferenceLength).Take(2), Is.EqualTo(new byte[] { 0xC1, 0x90 }));

        LtMsgPackHttpSerializer clientSide = new LtMsgPackHttpSerializer(); // what a client learned from the first response
        byte[] listBody = await list.Content.ReadAsByteArrayAsync();
        Assert.That(clientSide.LearnSchema(new Uri("http://localhost/streaming/orders"), schemaId, listBody, 0, listBody.Length, MsgPackMediaTypes.XLsMsgPack), Is.True);
        Assert.That(ReadOrders(body, clientSide.For(MsgPackMediaTypes.XLsMsgPack)).Select(o => o.Id), Is.EqualTo(new[] { 1, 2, 3 }));
      }
    }

    [Test]
    public async Task DescribedSchemaInTheHead()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        byte[] body = await BodyAsync(client, Get("/streaming/orders-async", DescribedStream));
        Dictionary<string, object> schema = MsgPackSerializer.Deserialize<Dictionary<string, object>>(body, new MsgPackSettings { UseInexedSchema = false }); // the first value
        Assert.That(schema, Does.ContainKey(string.Empty), "Version 2: the root entry");
        Assert.That(ReadOrders(body, LsReader), Has.Count.EqualTo(3));
      }
    }

    /// <summary>
    /// The items that are there go out when the source waits for the next one.
    /// </summary>
    [Test]
    public async Task ItemsArriveWhileTheSourceWaits()
    {
      _gate = new SemaphoreSlim(0);
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Get("/streaming/gated", LsStream), HttpCompletionOption.ResponseHeadersRead);
        Stream body = await response.Content.ReadAsStreamAsync();
        using (MsgPackStreamReader reader = new MsgPackStreamReader(body, LsReader))
        {
          Task<Order> first = Task.Run(() =>
          {
            reader.ReadHead<List<Order>>();
            reader.Read();
            return reader.ReadObject<Order>();
          });
          Assert.That(await Task.WhenAny(first, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(first), "The first order before the second is made");
          Assert.That(first.Result.Id, Is.EqualTo(1));

          _gate.Release();
          Order second = await Task.Run(() => reader.ReadObject<Order>());
          Assert.That(second.Id, Is.EqualTo(2));
          Assert.That(await Task.Run(() => reader.AtEnd), Is.True);
        }
      }
    }

    /// <summary>
    /// An exception before the first item: nothing was sent, the server can still answer with an error (TestServer passes the exception to the client).
    /// After it, the response is aborted: the client does not get a complete stream.
    /// </summary>
    [Test]
    public async Task ExceptionsOfTheSource()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        InvalidOperationException before = Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(() => client.SendAsync(Get("/streaming/failing?after=0", LsStream))));
        Assert.That(before.Message, Is.EqualTo("The query failed."));

        Assert.CatchAsync<Exception>(new Func<Task>(async () =>
        {
          HttpResponseMessage response = await client.SendAsync(Get("/streaming/failing?after=2", LsStream));
          ReadOrders(await response.Content.ReadAsByteArrayAsync(), LsReader);
        }));
      }
    }

    [TestCase("/streaming/result", null, MsgPackMediaTypes.XLsMsgPack)]
    [TestCase("/min/result", null, MsgPackMediaTypes.XLsMsgPack)]
    [TestCase("/streaming/result", "*/*", MsgPackMediaTypes.XLsMsgPack)]
    [TestCase("/min/result", "application/msgpack", MsgPackMediaTypes.MsgPack)]
    public async Task StreamResult(string path, string accept, string mediaType)
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        HttpResponseMessage response = await client.SendAsync(Get(path, accept));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(response.Content.Headers.ContentType.ToString(), Is.EqualTo(mediaType + "; stream=unknown-length"));
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        LtMsgPackSerializer serializer = MsgPackMediaTypes.IsLsMsgPack(mediaType) ? LsReader : PlainReader;
        if (MsgPackMediaTypes.IsLsMsgPack(mediaType))
        {
          LtMsgPackOptions options = LtMsgPackPresets.LsMsgPack();
          options.SchemaStore = new SchemaStore(); // PerStream: the reader registers the inline schemas for the references after them
          serializer = new LtMsgPackSerializer(options);
        }
        using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(body), serializer))
        {
          IReadOnlyList<MsgPackStreamIndex> indexes = reader.ReadIndexes();
          Assert.That(indexes.Single().Status, Is.EqualTo(MsgPackStreamIndexStatus.Complete));
          Assert.That(indexes[0].Entries.Select(e => e.Identifier), Is.EqualTo(new object[] { "orders", "last" }));

          reader.ReadHead<List<Order>>();
          reader.Read();
          List<Order> orders = new List<Order>();
          while (!reader.AtEnd)
            orders.Add(reader.ReadObject<Order>());
          reader.Read();
          Assert.That(orders.Select(o => o.Id), Is.EqualTo(new[] { 1, 2, 3 }));
          Assert.That(reader.ReadObject<Order>().Customer, Is.EqualTo("c9"));
          Assert.That(reader.AtEnd, Is.True);
        }
      }
    }

    [Test]
    public async Task StreamResultOfResultsExtensions()
    {
      (IHost host, HttpClient client) = await StartAsync();
      using (host)
      {
        byte[] body = await BodyAsync(client, Get("/min/result-extension", null));
        Assert.That(body, Is.EqualTo(new byte[] { 0xA4, (byte)'t', (byte)'e', (byte)'x', (byte)'t' }));
      }
    }
  }
}
