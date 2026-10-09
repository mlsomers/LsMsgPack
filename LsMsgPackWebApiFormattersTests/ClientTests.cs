using LsMsgPack;
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

    [Test]
    public void MalformedInputThrows()
    {
      Assert.ThrowsAsync<MsgPackException>(new Func<Task>(() => ReadOrder(Bytes(new byte[] { 0x83, 0xA2, 0x49 }, MsgPackMediaTypes.MsgPack))));
    }

    [Test]
    public void WrongTypeThrows()
    {
      Assert.ThrowsAsync<MsgPackException>(new Func<Task>(() => ReadOrder(Bytes(new byte[] { 0xA5, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, MsgPackMediaTypes.MsgPack))));
    }

    [Test]
    public async Task DifferencesOfTheResponse()
    {
      LsMsgPackMediaTypeFormatter reporting = new LsMsgPackMediaTypeFormatter(new LtMsgPack.Http.LtMsgPackHttpOptions() { ReportDifferences = true });
      HttpContent content = new ObjectContent<OrderWithExtra>(new OrderWithExtra() { Id = 1, Extra = "x" }, Formatter);
      Order order = await content.ReadAsAsync<Order>(new MediaTypeFormatter[] { reporting });

      Assert.That(order.Id, Is.EqualTo(1));
      Assert.That(content.GetReadDifferences().Differences.Single().Name, Is.EqualTo("Extra"));
      Assert.That(new HttpResponseMessage() { Content = content }.GetReadDifferences(), Is.SameAs(content.GetReadDifferences()));
      Assert.That(Bytes(new byte[] { 0x80 }, MsgPackMediaTypes.MsgPack).GetReadDifferences(), Is.Null);
    }

    [Test]
    public void DifferencesUntilTheError()
    {
      LsMsgPackMediaTypeFormatter reporting = new LsMsgPackMediaTypeFormatter(new LtMsgPack.Http.LtMsgPackHttpOptions() { ReportDifferences = true });
      byte[] body = MsgPackSerializer.Serialize(new System.Collections.Generic.Dictionary<string, object>() { { "Extra", "x" }, { "Id", 300 } }, new MsgPackSettings() { UseInexedSchema = false });
      HttpContent content = Bytes(body.Take(body.Length - 1).ToArray(), MsgPackMediaTypes.MsgPack); // the Id is cut off

      Assert.ThrowsAsync<MsgPackException>(new Func<Task>(() => content.ReadAsAsync<Order>(new MediaTypeFormatter[] { reporting })));
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
