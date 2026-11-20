using LsMsgPack;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ObjectDebugger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Captures of a SignalR connection (<see cref="HubCapture"/>, <see cref="MsgPackSettings.ReadHubMessages"/>): the handshake, length prefixes and messages, and plain MsgPack that is no capture.
  /// </summary>
  [TestClass]
  public class ObjectDebuggerHubTests
  {
    private static void Frame(MemoryStream target, byte[] message)
    {
      long length = message.Length;
      do
      {
        byte b = (byte)(length & 0x7F);
        length >>= 7;
        if (length > 0)
          b |= 0x80;
        target.WriteByte(b);
      }
      while (length > 0);
      target.Write(message, 0, message.Length);
    }

    private static byte[] Capture(bool handshake)
    {
      LsMsgPackSerializer values = new LsMsgPackSerializer(LsMsgPackPresets.Generic());
      MemoryStream ms = new MemoryStream();
      if (handshake)
        ms.Write(Encoding.UTF8.GetBytes("{\"protocol\":\"lsmsgpack\",\"version\":2}\u001e{}\u001e"));
      Dictionary<string, string> none = new Dictionary<string, string>();
      Frame(ms, values.Serialize(new object[] { 1, none, "1", "Send", new object[] { new string('x', 200), 7 }, new string[0] }));
      Frame(ms, values.Serialize(new object[] { 3, none, "1", 3, new Dictionary<string, object>() { { "Name", "Rex" } } }));
      Frame(ms, values.Serialize(new object[] { 2, none, "s", 42 }));
      Frame(ms, values.Serialize(new object[] { 7, null, true }));
      Frame(ms, values.Serialize(new object[] { 8, 70000L }));
      return ms.ToArray();
    }

    private static MpRoot Read(byte[] data, bool hub = true)
    {
#if KEEPTRACK
      return MsgPackItem.UnpackMultiple(data, new MsgPackSettings() { ReadHubMessages = hub, PreservePackages = true });
#else
      return MsgPackItem.UnpackMultiple(data, new MsgPackSettings() { ReadHubMessages = hub });
#endif
    }

    [TestMethod]
    public void Recognized_HandshakePrefixesMessages()
    {
      byte[] data = Capture(true);
      List<HubCapture.Frame> frames = HubCapture.Recognize(data);
      Assert.IsNotNull(frames);
      CollectionAssert.AreEqual(new[] { 0, 0, 1, 3, 2, 7, 8 }, frames.Select(f => f.Type).ToArray());
      Assert.AreEqual("{\"protocol\":\"lsmsgpack\",\"version\":2}", frames[0].Text);
      Assert.AreEqual(2, frames[2].PrefixLength, "200 characters: a prefix of 2 bytes");
      Assert.AreEqual("Send", frames[2].Target);
      Assert.AreEqual("1", frames[2].InvocationId);
      Assert.AreEqual(3, frames[3].ResultKind);

      MpRoot root = Read(data);
      Type[] kinds = root.Select(i => i.GetType()).ToArray();
      CollectionAssert.AreEqual(new[] { typeof(MpHubHandshake), typeof(MpHubHandshake), typeof(MpHubPrefix), typeof(MpHubMessage), typeof(MpHubPrefix), typeof(MpHubMessage),
        typeof(MpHubPrefix), typeof(MpHubMessage), typeof(MpHubPrefix), typeof(MpHubMessage), typeof(MpHubPrefix), typeof(MpHubMessage) }, kinds);
      MpHubMessage invocation = (MpHubMessage)root[3];
      Assert.AreEqual("SignalR Invocation \"Send\" (id 1).", invocation.ToString());
      Assert.AreEqual("target", invocation.FieldName(3));
      Assert.AreEqual("result", ((MpHubMessage)root[5]).FieldName(4));
      Assert.AreEqual(frames[2].Length, ((MpHubPrefix)root[2]).MessageLength);
#if KEEPTRACK
      Assert.AreEqual(frames[2].Offset, root[2].StoredOffset);
      Assert.AreEqual(2, root[2].StoredLength);
      Assert.AreEqual(frames[2].Offset + 2, root[3].StoredOffset);
      Assert.AreEqual(frames[2].Length, root[3].StoredLength);
      Assert.AreEqual(0, MsgPackValidation.ValidateItem(root[2], 0).Length, "a prefix is no MsgPack integer");
#endif
    }

    [TestMethod]
    public void Reconstructed_MessagesAsObjectsOfTheirFields()
    {
      RootObject objects = new RootObject();
      objects.Reconstruct(Read(Capture(false)));
      Assert.AreEqual(ObjectKind.Sequence, objects.Kind);
      Assert.AreEqual(5, objects.Members.Count, "the prefixes are no payloads");
      ComplexObject invocation = (ComplexObject)objects.Members[0];
      Assert.AreEqual("SignalR Invocation", invocation.Type);
      CollectionAssert.AreEqual(new[] { "type", "headers", "invocationId", "target", "arguments", "streamIds" }, invocation.Members.Select(m => m.Name).ToArray());
      Assert.AreEqual("Send", invocation.Members[3].Value);
      Assert.AreEqual("[0].target", invocation.Members[3].Path);
      ComplexObject arguments = (ComplexObject)invocation.Members[4];
      Assert.AreEqual(2, arguments.Members.Count);
      Assert.AreEqual("[0].arguments[1]", arguments.Members[1].Path);
      ComplexObject completion = (ComplexObject)objects.Members[1];
      Assert.AreEqual("result", completion.Members[4].Name);
      Assert.AreEqual("Rex", ((ComplexObject)completion.Members[4]).Members.Single(m => m.Name == "Name").Value);
    }

    /// <summary>
    /// Only data that is a capture as a whole: plain MsgPack, a prefix that does not match, data after the last message, a message of the wrong shape.
    /// </summary>
    [TestMethod]
    public void NotACapture_ReadAsUsual()
    {
      byte[] capture = Capture(false);
      List<byte[]> plain = new List<byte[]>()
      {
        new byte[] { 0x03, 0x92, 0x01, 0x02 },          // 3, [1, 2]: the length fits, an Invocation has 5 or 6 fields
        new byte[] { 0x02, 0x91, 0x01 },                // 2, [1]
        capture.Concat(new byte[] { 0x01 }).ToArray(),   // a byte after the last message
        capture.Take(capture.Length - 1).ToArray(),      // the last message cut off
        new LsMsgPackSerializer().Serialize(new List<int>() { 1, 2, 3 }),
        Encoding.UTF8.GetBytes("{\"protocol\":\"messagepack\""), // a handshake without its separator
      };
      for (int t = 0; t < plain.Count; t++)
      {
        Assert.IsNull(HubCapture.Recognize(plain[t]), $"data {t}");
        MpRoot read;
        try
        {
          read = Read(plain[t]);
        }
        catch (MsgPackException) // read as usual, and the rest of a capture is no valid MsgPack either (without KEEPTRACK an error throws)
        {
          continue;
        }
        Assert.IsFalse(read.Any(i => i is MpHubMessage || i is MpHubPrefix), $"data {t}");
      }
#if KEEPTRACK
      Assert.IsFalse(Read(capture, false).Any(i => i is MpHubMessage), "off by default"); // read as plain MsgPack: a prefix of 2 bytes sends it astray
#else
      Assert.ThrowsExactly<MsgPackException>(() => Read(capture, false), "off by default, read as plain MsgPack: a prefix of 2 bytes sends it astray");
#endif
    }
  }
}
