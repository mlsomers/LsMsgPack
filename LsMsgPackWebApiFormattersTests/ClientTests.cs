using LsMsgPack;
using LsMsgPackWebApiFormatters;
using NUnit.Framework;
using System;
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
      Assert.ThrowsAsync<MsgPackException>(new Func<Task>(() => ReadOrder(Bytes(new byte[] { 0x83, 0xA2, 0x49 }, LsMsgPackMediaTypes.MsgPack))));
    }

    [Test]
    public void WrongTypeThrows()
    {
      Assert.ThrowsAsync<MsgPackException>(new Func<Task>(() => ReadOrder(Bytes(new byte[] { 0xA5, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, LsMsgPackMediaTypes.MsgPack))));
    }

    [Test]
    public async Task EmptyBodyGivesDefault()
    {
      Assert.That(await Bytes(new byte[0], LsMsgPackMediaTypes.MsgPack).ReadAsAsync<Order>(new MediaTypeFormatter[] { Formatter }), Is.Null);
      Assert.That(await Bytes(new byte[0], LsMsgPackMediaTypes.MsgPack).ReadAsAsync<int>(new MediaTypeFormatter[] { Formatter }), Is.EqualTo(0));
    }

    [Test]
    public async Task DefaultMediaTypeIsPlainMsgPack()
    {
      ObjectContent<Order> content = new ObjectContent<Order>(new Order { Id = 1 }, Formatter);

      Assert.That(content.Headers.ContentType.MediaType, Is.EqualTo(LsMsgPackMediaTypes.MsgPack));
      Assert.That(MsgPackItem.UnpackMultiple(await content.ReadAsByteArrayAsync()).Count, Is.EqualTo(1));
    }
  }
}
