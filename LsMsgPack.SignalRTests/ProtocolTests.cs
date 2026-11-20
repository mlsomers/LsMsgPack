using LsMsgPack.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LsMsgPack.SignalRTests
{
  [TestClass]
  public class ProtocolTests
  {
    private static readonly LsMsgPackHubProtocol Ls = new LsMsgPackHubProtocol();
    private static readonly LsMsgPackHubProtocol AsMessagePack = new LsMsgPackHubProtocol(new LsMsgPackHubProtocolOptions() { AsMessagePack = true });
    private static readonly MessagePackHubProtocol Microsoft = new MessagePackHubProtocol();

    private static TestBinder Binder()
    {
      TestBinder binder = new TestBinder();
      binder.Targets["Send"] = new[] { typeof(ChatMessage), typeof(int), typeof(string), typeof(Point) };
      binder.Targets["Count"] = new[] { typeof(int) };
      binder.Targets["None"] = new Type[0];
      binder.Results["1"] = typeof(ChatMessage);
      binder.Results["2"] = typeof(int);
      binder.Results["raw"] = typeof(RawResult);
      binder.StreamItems["s1"] = typeof(ChatMessage);
      return binder;
    }

    /// <summary>
    /// Every message type with the fields Microsoft's MessagePack protocol writes (headers, stream ids, the result kinds, close with and without an error).
    /// </summary>
    private static List<HubMessage> Messages()
    {
      Dictionary<string, string> headers = new Dictionary<string, string>() { { "trace", "abc" }, { "ünï", "cødé" } };
      return new List<HubMessage>()
      {
        new InvocationMessage("Send", new object[] { ChatMessage.Sample(), 42, "text", null }),
        new InvocationMessage("7", "Send", new object[] { ChatMessage.Sample(2), -1, "", new Point() { X = 300, Y = -70000 } }) { Headers = headers },
        new InvocationMessage("8", "Count", new object[] { 3 }, new[] { "s1", "s2" }),
        new InvocationMessage("None", new object[0]),
        new StreamInvocationMessage("9", "Count", new object[] { 100000 }),
        new StreamInvocationMessage("10", "Count", new object[] { 1 }, new[] { "s1" }) { Headers = headers },
        new StreamItemMessage("s1", ChatMessage.Sample(3)),
        new StreamItemMessage("s1", null) { Headers = headers },
        CompletionMessage.WithResult("1", ChatMessage.Sample(4)),
        CompletionMessage.WithResult("2", 123456789),
        CompletionMessage.WithError("3", "it failed"),
        CompletionMessage.Empty("4"),
        new CompletionMessage("1", null, null, true) { Headers = headers },
        new CancelInvocationMessage("5"),
        PingMessage.Instance,
        CloseMessage.Empty,
        new CloseMessage("bye", true),
        new CloseMessage(null, true),
        new AckMessage(1),
        new AckMessage(long.MaxValue),
        new SequenceMessage(300),
      };
    }

    [TestMethod]
    public void Names()
    {
      Assert.AreEqual("lsmsgpack", Ls.Name);
      Assert.AreEqual("messagepack", AsMessagePack.Name);
      Assert.AreEqual(2, Ls.Version);
      Assert.IsTrue(Ls.IsVersionSupported(1));
      Assert.IsTrue(Ls.IsVersionSupported(2));
      Assert.IsFalse(Ls.IsVersionSupported(3));
    }

    [TestMethod]
    public void AsMessagePack_WritesTheBytesOfMicrosoftsProtocol()
    {
      List<HubMessage> messages = Messages();
      for (int i = 0; i < messages.Count; i++)
      {
        byte[] expected = Microsoft.GetMessageBytes(messages[i]).ToArray();
        byte[] actual = AsMessagePack.GetMessageBytes(messages[i]).ToArray();
        CollectionAssert.AreEqual(expected, actual, $"message {i} ({messages[i].GetType().Name}): {Convert.ToHexString(expected)} vs {Convert.ToHexString(actual)}");
      }
    }

    [TestMethod]
    public void AsMessagePack_ReadsMicrosoftsBytes_AndMicrosoftReadsOurs()
    {
      List<HubMessage> messages = Messages();
      for (int i = 0; i < messages.Count; i++)
      {
        string expected = Describe(messages[i]);
        Assert.AreEqual(expected, Describe(Parse(AsMessagePack, Microsoft.GetMessageBytes(messages[i]).ToArray())), $"message {i}, ours reading Microsoft's");
        Assert.AreEqual(expected, Describe(Parse(Microsoft, AsMessagePack.GetMessageBytes(messages[i]).ToArray())), $"message {i}, Microsoft's reading ours");
      }
    }

    [TestMethod]
    public void LsMsgPack_RoundTrip()
    {
      List<HubMessage> messages = Messages();
      for (int i = 0; i < messages.Count; i++)
        Assert.AreEqual(Describe(messages[i]), Describe(Parse(Ls, Ls.GetMessageBytes(messages[i]).ToArray())), $"message {i}");
    }

    /// <summary>
    /// The envelope is Microsoft's whatever the name: the messages without values are the same bytes.
    /// </summary>
    [TestMethod]
    public void LsMsgPack_EnvelopeIsMicrosofts()
    {
      HubMessage[] messages = new HubMessage[]
      {
        new InvocationMessage("8", "Count", new object[] { 3 }, new[] { "s1" }),
        CompletionMessage.WithError("3", "it failed"),
        CompletionMessage.Empty("4"),
        new CancelInvocationMessage("5"),
        PingMessage.Instance,
        new CloseMessage("bye", true),
        new AckMessage(70000),
        new SequenceMessage(-5),
      };
      for (int i = 0; i < messages.Length; i++)
        CollectionAssert.AreEqual(Microsoft.GetMessageBytes(messages[i]).ToArray(), Ls.GetMessageBytes(messages[i]).ToArray(), $"message {i}");
    }

    /// <summary>
    /// LsMsgPack's values: decimals and DateTimes as its extensions, Guids as text, enums as numbers, every property by name.
    /// </summary>
    [TestMethod]
    public void LsMsgPack_ValuesDifferFromMessagePack()
    {
      InvocationMessage message = new InvocationMessage("Send", new object[] { ChatMessage.Sample(), 1, "x", null });
      byte[] ls = Ls.GetMessageBytes(message).ToArray();
      byte[] mp = AsMessagePack.GetMessageBytes(message).ToArray();
      CollectionAssert.AreNotEqual(mp, ls);
      LsMsgPackSerializer plain = new LsMsgPackSerializer(LsMsgPackHubProtocolOptions.DefaultSerializerOptions(false));
      byte[] value = plain.Serialize(ChatMessage.Sample());
      Assert.IsTrue(Contains(ls, value), "the argument is the serializer's payload");
    }

    [TestMethod]
    public void WriteMessage_IsGetMessageBytes()
    {
      List<HubMessage> messages = Messages();
      ArrayBufferWriter<byte> output = new ArrayBufferWriter<byte>();
      List<byte> expected = new List<byte>();
      for (int i = 0; i < messages.Count; i++)
      {
        Ls.WriteMessage(messages[i], output);
        expected.AddRange(Ls.GetMessageBytes(messages[i]).ToArray());
      }
      CollectionAssert.AreEqual(expected.ToArray(), output.WrittenSpan.ToArray());
    }

    /// <summary>
    /// The messages one after another, arriving a byte at a time and in segments of a few bytes: a message is read when it is complete, the input is left at the next one.
    /// </summary>
    [TestMethod]
    public void PartialInput()
    {
      List<HubMessage> messages = Messages();
      ArrayBufferWriter<byte> output = new ArrayBufferWriter<byte>();
      for (int i = 0; i < messages.Count; i++)
        Ls.WriteMessage(messages[i], output);
      byte[] all = output.WrittenSpan.ToArray();
      TestBinder binder = Binder();

      // A byte at a time
      List<string> read = new List<string>();
      int available = 0;
      int consumed = 0;
      while (available < all.Length)
      {
        available++;
        ReadOnlySequence<byte> input = new ReadOnlySequence<byte>(all, consumed, available - consumed);
        while (Ls.TryParseMessage(ref input, binder, out HubMessage message))
          read.Add(Describe(message));
        consumed = available - (int)input.Length;
      }
      Assert.AreEqual(all.Length, consumed);
      AssertSame(messages, read);

      // Segments of 3 bytes (the prefixes and the values cross them)
      ReadOnlySequence<byte> segmented = Segments(all, 3);
      read.Clear();
      while (Ls.TryParseMessage(ref segmented, binder, out HubMessage message))
        read.Add(Describe(message));
      Assert.AreEqual(0, segmented.Length);
      AssertSame(messages, read);
    }

    [TestMethod]
    public void LargeMessage_LongPrefix()
    {
      string text = new string('x', 300000);
      InvocationMessage message = new InvocationMessage("Send", new object[] { new ChatMessage() { Text = text }, 1, text, null });
      byte[] bytes = Ls.GetMessageBytes(message).ToArray();
      Assert.AreEqual(0x80, bytes[1] & 0x80, "a three byte prefix");
      Assert.AreEqual(0, bytes[2] & 0x80, "a three byte prefix");
      Assert.AreEqual(Describe(message), Describe(Parse(Ls, bytes)));
      CollectionAssert.AreEqual(Microsoft.GetMessageBytes(message).ToArray(), AsMessagePack.GetMessageBytes(message).ToArray());
    }

    [TestMethod]
    public void TooLong_Throws()
    {
      ReadOnlySequence<byte> input = new ReadOnlySequence<byte>(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x08, 0x90 });
      Assert.ThrowsExactly<FormatException>(() => Ls.TryParseMessage(ref input, Binder(), out HubMessage message));
    }

    [TestMethod]
    public void WrongArgumentCount_IsABindingFailure()
    {
      InvocationMessage message = new InvocationMessage("12", "Count", new object[] { 1, 2 });
      InvocationBindingFailureMessage failure = (InvocationBindingFailureMessage)Parse(Ls, Ls.GetMessageBytes(message).ToArray());
      Assert.AreEqual("12", failure.InvocationId);
      Assert.AreEqual("Count", failure.Target);
      Assert.AreEqual("Invocation provides 2 argument(s) but target expects 1.", failure.BindingFailure.SourceException.Message);
    }

    [TestMethod]
    public void WrongArgumentType_IsABindingFailure()
    {
      InvocationMessage message = new InvocationMessage("12", "Count", new object[] { "three" });
      InvocationBindingFailureMessage failure = (InvocationBindingFailureMessage)Parse(Ls, Ls.GetMessageBytes(message).ToArray());
      Assert.IsInstanceOfType<InvalidDataException>(failure.BindingFailure.SourceException);
      StringAssert.StartsWith(failure.BindingFailure.SourceException.Message, "Error binding arguments.");
      StringAssert.Contains(failure.BindingFailure.SourceException.InnerException.Message, "`Int32` type for 'argument'");
    }

    [TestMethod]
    public void UnknownTarget_IsABindingFailure()
    {
      InvocationMessage message = new InvocationMessage("Nope", new object[] { 1 });
      InvocationBindingFailureMessage failure = (InvocationBindingFailureMessage)Parse(Ls, Ls.GetMessageBytes(message).ToArray());
      Assert.IsNull(failure.InvocationId);
      Assert.AreEqual("Nope", failure.Target);
    }

    [TestMethod]
    public void UnknownStream_IsAStreamBindingFailure()
    {
      StreamBindingFailureMessage failure = (StreamBindingFailureMessage)Parse(Ls, Ls.GetMessageBytes(new StreamItemMessage("nope", 1)).ToArray());
      Assert.AreEqual("nope", failure.Id);
    }

    [TestMethod]
    public void ResultOfTheWrongType_IsACompletionWithError_UnknownInvocationIsSkipped()
    {
      CompletionMessage wrong = (CompletionMessage)Parse(Ls, Ls.GetMessageBytes(CompletionMessage.WithResult("2", "text")).ToArray());
      Assert.IsFalse(wrong.HasResult);
      StringAssert.StartsWith(wrong.Error, "Error trying to deserialize result to Int32.");

      CompletionMessage unknown = (CompletionMessage)Parse(Ls, Ls.GetMessageBytes(CompletionMessage.WithResult("unknown", ChatMessage.Sample())).ToArray());
      Assert.IsTrue(unknown.HasResult);
      Assert.IsNull(unknown.Result);
      Assert.IsNull(unknown.Error);
    }

    /// <summary>
    /// A client's result passed on by the server (scale-out): kept as its bytes, written back as they came.
    /// </summary>
    [TestMethod]
    public void RawResult_KeepsTheBytes()
    {
      byte[] bytes = Ls.GetMessageBytes(CompletionMessage.WithResult("raw", ChatMessage.Sample())).ToArray();
      CompletionMessage read = (CompletionMessage)Parse(Ls, bytes);
      RawResult raw = (RawResult)read.Result;
      CollectionAssert.AreEqual(bytes, Ls.GetMessageBytes(CompletionMessage.WithResult("raw", raw)).ToArray());
    }

    [TestMethod]
    public void UnknownMessageType_IsIgnored()
    {
      byte[] bytes = new byte[] { 0x02, 0x91, 0x63 }; // [99]
      ReadOnlySequence<byte> input = new ReadOnlySequence<byte>(bytes);
      Assert.IsFalse(Ls.TryParseMessage(ref input, Binder(), out HubMessage message));
      Assert.IsNull(message);
      Assert.AreEqual(0, input.Length, "the message is consumed");
    }

    [TestMethod]
    public void TruncatedEnvelope_Throws()
    {
      byte[] bytes = new byte[] { 0x03, 0x93, 0x05, 0x80 }; // a CancelInvocation without its id
      ReadOnlySequence<byte> input = new ReadOnlySequence<byte>(bytes);
      InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => Ls.TryParseMessage(ref input, Binder(), out HubMessage message));
      StringAssert.Contains(error.Message, "string");
    }

    /// <summary>
    /// The serializer's limits apply to each value (untrusted data): objects past MaxObjects are refused.
    /// </summary>
    [TestMethod]
    public void Limits_ApplyToTheValues()
    {
      LsMsgPackHubProtocol limited = new LsMsgPackHubProtocol(new LsMsgPackHubProtocolOptions() { MaxObjects = 2 });
      InvocationMessage message = new InvocationMessage("Send", new object[] { ChatMessage.Sample(), 1, "x", null });
      InvocationBindingFailureMessage failure = (InvocationBindingFailureMessage)Parse(limited, Ls.GetMessageBytes(message).ToArray());
      StringAssert.Contains(failure.BindingFailure.SourceException.InnerException.InnerException.Message, "MaxObjects");
    }

    /// <summary>
    /// A JavaScript client sends camelCase names: they are read ignoring case.
    /// </summary>
    [TestMethod]
    public void CamelCaseNames_AreRead()
    {
      // [1, {}, nil, "Send", [nil, 1, "s", {"x": 5, "y": 6}], []]
      byte[] body = Convert.FromHexString("9601" + "80" + "C0" + "A453656E64" + "94" + "C0" + "01" + "A173" + "82A17805A17906" + "90");
      byte[] bytes = new byte[body.Length + 1];
      bytes[0] = (byte)body.Length;
      body.CopyTo(bytes, 1);
      InvocationMessage read = (InvocationMessage)Parse(Ls, bytes);
      Point p = (Point)read.Arguments[3];
      Assert.AreEqual(5, p.X);
      Assert.AreEqual(6, p.Y);
    }

    #region Helpers

    internal static HubMessage Parse(IHubProtocol protocol, byte[] bytes)
    {
      ReadOnlySequence<byte> input = new ReadOnlySequence<byte>(bytes);
      Assert.IsTrue(protocol.TryParseMessage(ref input, Binder(), out HubMessage message), "a message");
      Assert.AreEqual(0, input.Length, "the whole message is read");
      return message;
    }

    private static void AssertSame(List<HubMessage> expected, List<string> read)
    {
      Assert.AreEqual(expected.Count, read.Count);
      for (int i = 0; i < expected.Count; i++)
        Assert.AreEqual(Describe(expected[i]), read[i], $"message {i}");
    }

    private static ReadOnlySequence<byte> Segments(byte[] bytes, int size)
    {
      Segment first = null;
      Segment last = null;
      for (int at = 0; at < bytes.Length; at += size)
      {
        Segment segment = new Segment(new ReadOnlyMemory<byte>(bytes, at, Math.Min(size, bytes.Length - at)), last);
        if (first is null)
          first = segment;
        last = segment;
      }
      return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
      public Segment(ReadOnlyMemory<byte> memory, Segment previous)
      {
        Memory = memory;
        if (previous != null)
        {
          RunningIndex = previous.RunningIndex + previous.Memory.Length;
          previous.Next = this;
        }
      }
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
      return haystack.AsSpan().IndexOf(needle) >= 0;
    }

    /// <summary>
    /// A message as text: its type and its public properties as JSON (the values compared by their content).
    /// </summary>
    internal static string Describe(HubMessage message)
    {
      object shown = message;
      if (message is CompletionMessage completion && completion.Result is RawResult)
        shown = new { completion.InvocationId, completion.Error, completion.HasResult, Raw = true, completion.Headers };
      return message.GetType().Name + " " + JsonSerializer.Serialize(shown, message.GetType() == shown.GetType() ? message.GetType() : shown.GetType());
    }

    #endregion
  }
}
