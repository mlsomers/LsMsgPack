using LsMsgPack.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;

namespace LsMsgPack.SignalRTests
{
  /// <summary>
  /// Writing and reading an invocation with a chat message (the server's work per message) with each protocol: ns and bytes allocated per message, and the message size.
  /// Run with <c>-c Release --filter TestCategory=Benchmark --logger "console;verbosity=detailed"</c>.
  /// </summary>
  [TestClass]
  public class ProtocolBenchmark
  {
    [TestMethod]
    [TestCategory("Benchmark")]
    public void WriteAndRead()
    {
      List<(string, IHubProtocol)> protocols = new List<(string, IHubProtocol)>()
      {
        ("lsmsgpack", new LsMsgPackHubProtocol()),
        ("lsmsgpack-indexed", new LsMsgPackHubProtocol(new LsMsgPackHubProtocolOptions() { IndexedSchema = true })),
        ("lsmsgpack-indexed, names schema", new LsMsgPackHubProtocol(new LsMsgPackHubProtocolOptions() { IndexedSchema = true, SerializerOptions = NamesSchema() })),
        ("LsMsgPack as messagepack", new LsMsgPackHubProtocol(new LsMsgPackHubProtocolOptions() { AsMessagePack = true })),
        ("Microsoft messagepack", new MessagePackHubProtocol()),
        ("Microsoft json", new JsonHubProtocol()),
      };
      TestBinder binder = new TestBinder();
      binder.Targets["Send"] = new[] { typeof(ChatMessage), typeof(int) };
      InvocationMessage message = new InvocationMessage("Send", new object[] { ChatMessage.Sample(), 7 });
      List<ChatMessage> list = new List<ChatMessage>();
      for (int i = 0; i < 50; i++)
        list.Add(ChatMessage.Sample(i));
      binder.Targets["List"] = new[] { typeof(List<ChatMessage>), typeof(int) };
      InvocationMessage large = new InvocationMessage("List", new object[] { list, 7 });

      foreach ((string name, IHubProtocol protocol) in protocols)
      {
        Measure(name, protocol, binder, message, 200000);
        Measure(name + ", 50 messages in a list", protocol, binder, large, 10000);
      }
    }

    /// <summary>
    /// The indexed defaults with schema version 1 (names only): for .NET clients with the classes.
    /// </summary>
    private static LsMsgPackOptions NamesSchema()
    {
      LsMsgPackOptions options = LsMsgPackHubProtocolOptions.DefaultSerializerOptions(false, true);
      options.SchemaVersion = SchemaVersion.Names;
      return options;
    }

    private static void Measure(string name, IHubProtocol protocol, IInvocationBinder binder, HubMessage message, int count)
    {
      ArrayBufferWriter<byte> output = new ArrayBufferWriter<byte>(64 * 1024);
      for (int i = 0; i < count / 10; i++) // warm up (tiered compilation)
      {
        output.ResetWrittenCount();
        protocol.WriteMessage(message, output);
      }
      byte[] bytes = output.WrittenSpan.ToArray();
      for (int i = 0; i < count / 10; i++)
      {
        ReadOnlySequence<byte> input = new ReadOnlySequence<byte>(bytes);
        protocol.TryParseMessage(ref input, binder, out HubMessage read);
      }

      double[] write = new double[3];
      double[] read2 = new double[3];
      long writeAlloc = 0;
      long readAlloc = 0;
      for (int run = 0; run < 3; run++)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch watch = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
          output.ResetWrittenCount();
          protocol.WriteMessage(message, output);
        }
        write[run] = watch.Elapsed.TotalMilliseconds * 1e6 / count;
        writeAlloc = (GC.GetAllocatedBytesForCurrentThread() - before) / count;

        before = GC.GetAllocatedBytesForCurrentThread();
        watch.Restart();
        for (int i = 0; i < count; i++)
        {
          ReadOnlySequence<byte> input = new ReadOnlySequence<byte>(bytes);
          protocol.TryParseMessage(ref input, binder, out HubMessage read);
        }
        read2[run] = watch.Elapsed.TotalMilliseconds * 1e6 / count;
        readAlloc = (GC.GetAllocatedBytesForCurrentThread() - before) / count;
      }
      Array.Sort(write);
      Array.Sort(read2);
      Console.WriteLine($"{name,-50} {bytes.Length,6} B   write {write[1],9:0} ns {writeAlloc,6} B   read {read2[1],9:0} ns {readAlloc,6} B");
    }
  }
}
