using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPackWebApiFormatters;
using NUnit.Framework;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace LsMsgPackWebApiFormattersTests
{
  /// <summary>
  /// Client side there is no ModelState, so errors are thrown
  /// </summary>
  [TestFixture]
  public class ClientTests
  {
    private static readonly LsMsgPackMediaTypeFormatter Formatter = new LsMsgPackMediaTypeFormatter();

    private static HttpContent Bytes(byte[] body, string contentType)
    {
      ByteArrayContent content = new ByteArrayContent(body);
      content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
      return content;
    }

    private static Task ReadOrder(HttpContent content)
    {
      return content.ReadAsAsync<Order>(new MediaTypeFormatter[] { Formatter });
    }

    /// <summary>
    /// PopulateFromMsgPackAsync: into the target (members the body lacks keep their values), an empty body or nil leaves it, a collection is cleared and filled,
    /// another media type and a target that cannot be read into throw, the differences with ReportDifferences (also until an error).
    /// </summary>
    [Test]
    public async Task PopulateReadsIntoTheTarget()
    {
      Order order = new Order() { Id = 1, Customer = "kept", Amounts = new[] { 2.5 } };
      await new ObjectContent<Order>(new Order() { Id = 7, Customer = "new" }, Formatter).PopulateFromMsgPackAsync(order, Formatter);
      Assert.That(order.Id, Is.EqualTo(7));
      Assert.That(order.Customer, Is.EqualTo("new"));
      Assert.That(order.Amounts, Is.Null, "written as nil");

      byte[] onlyId = MsgPackSerializer.Serialize(new System.Collections.Generic.Dictionary<string, object>() { { "Id", 9 } }, new MsgPackSettings() { UseIndexedSchema = false });
      await Bytes(onlyId, MsgPackMediaTypes.MsgPack).PopulateFromMsgPackAsync(order, Formatter);
      Assert.That(order.Id, Is.EqualTo(9));
      Assert.That(order.Customer, Is.EqualTo("new"), "not in the body: kept");

      await Bytes(new byte[0], MsgPackMediaTypes.MsgPack).PopulateFromMsgPackAsync(order, Formatter);
      await new ByteArrayContent(new byte[0]).PopulateFromMsgPackAsync(order, Formatter); // 204: no Content-Type
      await Bytes(new byte[] { 0xC0 }, MsgPackMediaTypes.MsgPack).PopulateFromMsgPackAsync(order, Formatter);
      Assert.That(order.Id, Is.EqualTo(9), "left as it is");

      System.Collections.Generic.List<Order> orders = new System.Collections.Generic.List<Order>() { new Order() { Id = 1 } };
      await new ObjectContent<Order[]>(new[] { new Order() { Id = 2 }, new Order() { Id = 3 } }, Formatter).PopulateFromMsgPackAsync(orders, Formatter);
      Assert.That(orders.Select(o => o.Id), Is.EqualTo(new[] { 2, 3 }));

      await Assert.ThrowsAsync<NotSupportedException>(new Func<Task>(() => Bytes(new byte[] { 0x7B, 0x7D }, "application/json").PopulateFromMsgPackAsync(order, Formatter)));
      await Assert.ThrowsAsync<ArgumentException>(new Func<Task>(() => Bytes(onlyId, MsgPackMediaTypes.MsgPack).PopulateFromMsgPackAsync("a string", Formatter)));
      await Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(() => Bytes(onlyId, MsgPackMediaTypes.MsgPack).PopulateFromMsgPackAsync<Order>(null, Formatter)));

      LsMsgPackMediaTypeFormatter reporting = new LsMsgPackMediaTypeFormatter(new LsMsgPack.Http.LsMsgPackHttpOptions() { ReportDifferences = true });
      HttpContent extra = new ObjectContent<OrderWithExtra>(new OrderWithExtra() { Id = 4, Extra = "x" }, Formatter);
      await extra.PopulateFromMsgPackAsync(order, reporting);
      Assert.That(order.Id, Is.EqualTo(4));
      Assert.That(extra.GetReadDifferences().Differences.Single().Name, Is.EqualTo("Extra"));
      HttpContent matching = new ObjectContent<Order>(new Order() { Id = 5 }, Formatter);
      await matching.PopulateFromMsgPackAsync(order, reporting);
      Assert.That(matching.GetReadDifferences(), Is.Null);

      byte[] body = MsgPackSerializer.Serialize(new System.Collections.Generic.Dictionary<string, object>() { { "Extra", "x" }, { "Id", 300 } }, new MsgPackSettings() { UseIndexedSchema = false });
      HttpContent cut = Bytes(body.Take(body.Length - 1).ToArray(), MsgPackMediaTypes.MsgPack);
      await Assert.ThrowsAsync<MsgPackException>(new Func<Task>(() => cut.PopulateFromMsgPackAsync(new Order(), reporting)));
      Assert.That(cut.GetReadDifferences().Differences.Single().Name, Is.EqualTo("Extra"), "the differences until the error");
    }

    [Test]
    public async Task MalformedInputThrows()
    {
      await Assert.ThrowsAsync<MsgPackException>(new Func<Task>(() => ReadOrder(Bytes(new byte[] { 0x83, 0xA2, 0x49 }, MsgPackMediaTypes.MsgPack))));
    }

    [Test]
    public async Task WrongTypeThrows()
    {
      await Assert.ThrowsAsync<MsgPackException>(new Func<Task>(() => ReadOrder(Bytes(new byte[] { 0xA5, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, MsgPackMediaTypes.MsgPack))));
    }

    [Test]
    public async Task DifferencesOfTheResponse()
    {
      LsMsgPackMediaTypeFormatter reporting = new LsMsgPackMediaTypeFormatter(new LsMsgPack.Http.LsMsgPackHttpOptions() { ReportDifferences = true });
      HttpContent content = new ObjectContent<OrderWithExtra>(new OrderWithExtra() { Id = 1, Extra = "x" }, Formatter);
      Order order = await content.ReadAsAsync<Order>(new MediaTypeFormatter[] { reporting });

      Assert.That(order.Id, Is.EqualTo(1));
      Assert.That(content.GetReadDifferences().Differences.Single().Name, Is.EqualTo("Extra"));
      Assert.That(new HttpResponseMessage() { Content = content }.GetReadDifferences(), Is.SameAs(content.GetReadDifferences()));
      Assert.That(Bytes(new byte[] { 0x80 }, MsgPackMediaTypes.MsgPack).GetReadDifferences(), Is.Null);
    }

    [Test]
    public async Task DifferencesUntilTheError()
    {
      LsMsgPackMediaTypeFormatter reporting = new LsMsgPackMediaTypeFormatter(new LsMsgPack.Http.LsMsgPackHttpOptions() { ReportDifferences = true });
      byte[] body = MsgPackSerializer.Serialize(new System.Collections.Generic.Dictionary<string, object>() { { "Extra", "x" }, { "Id", 300 } }, new MsgPackSettings() { UseIndexedSchema = false });
      HttpContent content = Bytes(body.Take(body.Length - 1).ToArray(), MsgPackMediaTypes.MsgPack); // the Id is cut off

      await Assert.ThrowsAsync<MsgPackException>(new Func<Task>(() => content.ReadAsAsync<Order>(new MediaTypeFormatter[] { reporting })));
      Assert.That(content.GetReadDifferences().Differences.Single().Name, Is.EqualTo("Extra"));
    }

    [Test]
    public async Task EmptyBodyGivesDefault()
    {
      Assert.That(await Bytes(new byte[0], MsgPackMediaTypes.MsgPack).ReadAsAsync<Order>(new MediaTypeFormatter[] { Formatter }), Is.Null);
      Assert.That(await Bytes(new byte[0], MsgPackMediaTypes.MsgPack).ReadAsAsync<int>(new MediaTypeFormatter[] { Formatter }), Is.EqualTo(0));
    }

    [Test]
    public async Task DefaultMediaTypeIsPlainMsgPack()
    {
      ObjectContent<Order> content = new ObjectContent<Order>(new Order { Id = 1 }, Formatter);

      Assert.That(content.Headers.ContentType.MediaType, Is.EqualTo(MsgPackMediaTypes.MsgPack));
      Assert.That(MsgPackItem.UnpackMultiple(await content.ReadAsByteArrayAsync()).Count, Is.EqualTo(1));
    }
  }
}
