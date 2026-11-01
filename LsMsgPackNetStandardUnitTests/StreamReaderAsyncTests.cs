using LsMsgPack;
using LtMsgPack;
using LtMsgPack.Streaming;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static LsMsgPackUnitTests.StreamWriterReaderTests;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The async methods of MsgPackStreamReader: the same tokens, values, objects and errors as the synchronous ones, over a stream that refuses synchronous reads and returns 1 to 3 bytes per read.
  /// </summary>
  [TestClass]
  public class StreamReaderAsyncTests
  {
    private static LtMsgPackSerializer Plain()
    {
      return new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map });
    }

    private static byte[] Write(Action<MsgPackStreamWriter> write, LtMsgPackSerializer serializer = null, MsgPackStreamSchemas schemas = MsgPackStreamSchemas.PerObject, MsgPackStreamIndexOptions index = null)
    {
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer ?? Plain(), schemas, true, index))
        write(writer);
      return ms.ToArray();
    }

    private static MsgPackStreamReader SyncReader(byte[] bytes, LtMsgPackSerializer serializer = null)
    {
      return new MsgPackStreamReader(new MemoryStream(bytes), serializer ?? Plain());
    }

    private static MsgPackStreamReader AsyncReader(byte[] bytes, LtMsgPackSerializer serializer = null)
    {
      return new MsgPackStreamReader(new AsyncOnlyStream(bytes), serializer ?? Plain());
    }

    private static byte[] Hex(string hex)
    {
      return hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => Convert.ToByte(p, 16)).ToArray();
    }

    private static SwOrder Order(int n)
    {
      SwOrder order = new SwOrder { Customer = "Customer " + n, Date = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n) };
      for (int t = 0; t < n; t++)
        order.Lines.Add(new SwLine { Product = "P" + t, Price = 1.25m * t, Quantity = t });
      return order;
    }

    private static void AreEqual(SwOrder expected, SwOrder actual)
    {
      Assert.AreEqual(expected.Customer, actual.Customer);
      Assert.AreEqual(expected.Date, actual.Date.ToUniversalTime());
      Assert.HasCount(expected.Lines.Count, actual.Lines);
      for (int t = 0; t < expected.Lines.Count; t++)
      {
        Assert.AreEqual(expected.Lines[t].Product, actual.Lines[t].Product);
        Assert.AreEqual(expected.Lines[t].Price, actual.Lines[t].Price);
      }
    }

    #region Tokens and values

    /// <summary>
    /// Every token with its position and value; at depth 2 and deeper the arrays and maps, and every str and bin of unknown length, are read whole (<paramref name="whole"/>), the others token by token.
    /// </summary>
    private static string SyncTokens(byte[] bytes, bool whole)
    {
      StringBuilder text = new StringBuilder();
      using (MsgPackStreamReader reader = SyncReader(bytes))
      {
        while (reader.Read())
        {
          text.Append(reader.TokenType).Append('@').Append(reader.TokenStart).Append(' ');
          switch (reader.TokenType)
          {
            case MsgPackTokenType.ArrayStart:
            case MsgPackTokenType.MapStart:
              text.Append('(').Append(reader.Length).Append(')');
              if (whole && reader.Depth >= 2)
                text.Append(Describe(reader.GetValue()));
              break;
            case MsgPackTokenType.StringStart: if (whole) text.Append(reader.GetString()); break;
            case MsgPackTokenType.BinaryStart: if (whole) text.Append(Describe(reader.GetBytes())); break;
            case MsgPackTokenType.String: text.Append(reader.GetString()); break;
            case MsgPackTokenType.Binary: text.Append(Describe(reader.GetBytes())); break;
            case MsgPackTokenType.Integer:
            case MsgPackTokenType.Float:
            case MsgPackTokenType.Boolean:
            case MsgPackTokenType.Nil:
            case MsgPackTokenType.Extension: text.Append(Describe(reader.GetValue())); break;
          }
          text.Append(" d").Append(reader.Depth).Append(" p").Append(reader.Position).AppendLine();
        }
      }
      return text.ToString();
    }

    private static async Task<string> AsyncTokens(byte[] bytes, bool whole)
    {
      StringBuilder text = new StringBuilder();
      using (MsgPackStreamReader reader = AsyncReader(bytes))
      {
        while (await reader.ReadAsync())
        {
          text.Append(reader.TokenType).Append('@').Append(reader.TokenStart).Append(' ');
          switch (reader.TokenType)
          {
            case MsgPackTokenType.ArrayStart:
            case MsgPackTokenType.MapStart:
              text.Append('(').Append(reader.Length).Append(')');
              if (whole && reader.Depth >= 2)
                text.Append(Describe(await reader.GetValueAsync()));
              break;
            case MsgPackTokenType.StringStart: if (whole) text.Append(await reader.GetStringAsync()); break;
            case MsgPackTokenType.BinaryStart: if (whole) text.Append(Describe(await reader.GetBytesAsync())); break;
            case MsgPackTokenType.String: text.Append(await reader.GetStringAsync()); break;
            case MsgPackTokenType.Binary: text.Append(Describe(await reader.GetBytesAsync())); break;
            case MsgPackTokenType.Integer:
            case MsgPackTokenType.Float:
            case MsgPackTokenType.Boolean:
            case MsgPackTokenType.Nil:
            case MsgPackTokenType.Extension: text.Append(Describe(await reader.GetValueAsync())); break;
          }
          text.Append(" d").Append(reader.Depth).Append(" p").Append(reader.Position).AppendLine();
        }
      }
      return text.ToString();
    }

    private static string Describe(object value)
    {
      switch (value)
      {
        case null: return "null";
        case string s: return "\"" + s + "\"";
        case byte[] b: return "bin" + b.Length + ":" + string.Concat(b.Take(8).Select(x => x.ToString("x2")));
        case KeyValuePair<object, object>[] map: return "{" + string.Join(",", map.Select(p => Describe(p.Key) + ":" + Describe(p.Value))) + "}";
        case IEnumerable items: return "[" + string.Join(",", items.Cast<object>().Select(Describe)) + "]";
        case DateTime d: return d.ToUniversalTime().ToString("O");
        default: return value.GetType().Name + ":" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
      }
    }

    /// <summary>
    /// Values of every format, of unknown length, nested, longer than the buffer, and an index between the root values.
    /// </summary>
    private static byte[] Corpus()
    {
      string longText = new string('x', 40000) + "é";
      byte[] longBin = Enumerable.Range(0, 70000).Select(i => (byte)i).ToArray();
      return Write(w =>
      {
        w.Write(1); w.Write(-1); w.Write(200); w.Write(-200); w.Write(70000); w.Write(-70000); w.Write(long.MaxValue); w.Write(long.MinValue); w.Write(ulong.MaxValue);
        w.Write(1.5f); w.Write(2.5); w.Write(true); w.Write(false); w.WriteNil();
        w.Write("short"); w.Write(new string('a', 40)); w.Write(new string('b', 300)); w.Write(longText);
        w.WriteBinary(new byte[] { 1, 2, 3 }); w.WriteBinary(longBin);
        w.Write(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)); w.Write(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef")); w.Write(12.345m);
        w.WriteExtension(5, new byte[] { 9 }); w.WriteExtension(6, new byte[300]);

        w.WriteArrayStart();
        for (int i = 0; i < 20; i++)
        {
          w.WriteArrayHeader(3);
          w.Write(i);
          w.WriteStringStart(); w.Write("chunk "); w.Write(i.ToString()); w.WriteEnd();
          w.WriteMapStart(); w.Write("k"); w.WriteArrayStart(); w.Write(i); w.WriteEnd(); w.Write("bin"); w.WriteBinaryStart(); w.WriteBinary(new byte[] { 1 }); w.WriteBinary(longBin); w.WriteEnd(); w.WriteEnd();
        }
        w.WriteEnd();

        w.WriteMapHeader(2);
        w.Write("rows"); w.WriteArrayStart(); w.Write(1); w.Write(2); w.WriteEnd();
        w.Write("total"); w.Write(2);

        w.WriteArrayHeader(0);
        w.WriteMapStart(); w.WriteEnd();
        w.WriteStringStart(); w.WriteEnd();
        w.WriteStringStart(); w.Write(longText); w.Write(longText); w.WriteEnd();
        w.WriteArrayStart(); w.WriteArrayStart(); w.WriteArrayStart(); w.Write(1); w.WriteEnd(); w.WriteEnd(); w.WriteEnd();
        w.Write("last");
      }, index: new MsgPackStreamIndexOptions { CheckpointInterval = 3, PointInterval = 4 });
    }

    [TestMethod]
    public async Task TokensAndValuesAreTheSynchronousOnes()
    {
      byte[] corpus = Corpus();
      foreach (bool whole in new[] { false, true })
        Assert.AreEqual(SyncTokens(corpus, whole), await AsyncTokens(corpus, whole), "whole: " + whole);
    }

    [TestMethod]
    public async Task ExamplesOfTheProposal()
    {
      foreach (string hex in new[] { "c1 90 01 a1 61 91 c3 c1 c0", "82 a4 72 6f 77 73 c1 90 01 02 c1 c0 a5 74 6f 74 61 6c 02", "c1 a0 a2 61 62 a1 63 c1 c0" })
      {
        foreach (bool whole in new[] { false, true })
          Assert.AreEqual(SyncTokens(Hex(hex), whole), await AsyncTokens(Hex(hex), whole), hex);
        using (MsgPackStreamReader reader = AsyncReader(Hex(hex)))
        {
          await reader.ReadAsync();
          Assert.AreEqual(Describe(SyncReader(Hex(hex)).ReadObject<object>()), Describe(await reader.GetValueAsync()), hex);
          Assert.IsFalse(await reader.ReadAsync());
        }
      }
    }

    [TestMethod]
    public async Task SkipPassesValuesWithoutBufferingThem()
    {
      byte[] corpus = Corpus();
      List<string> sync = new List<string>();
      using (MsgPackStreamReader reader = SyncReader(corpus))
      {
        while (reader.Read())
        {
          sync.Add(reader.TokenType + "@" + reader.TokenStart);
          reader.Skip();
        }
      }
      List<string> async = new List<string>();
      using (MsgPackStreamReader reader = AsyncReader(corpus))
      {
        while (await reader.ReadAsync())
        {
          async.Add(reader.TokenType + "@" + reader.TokenStart);
          await reader.SkipAsync();
        }
      }
      CollectionAssert.AreEqual(sync, async);
    }

    [TestMethod]
    public async Task AtEndOfRootValuesAndArrays()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteArrayStart(); w.Write(1); w.Write("two"); w.WriteEnd();
        w.Write("x");
      }, index: new MsgPackStreamIndexOptions());
      using (MsgPackStreamReader reader = AsyncReader(bytes))
      {
        Assert.IsFalse(await reader.AtEndAsync());
        await reader.ReadAsync();
        Assert.IsFalse(await reader.AtEndAsync());
        await reader.ReadAsync();
        Assert.IsFalse(await reader.AtEndAsync());
        await reader.ReadAsync(); // "two", its data not passed yet
        Assert.IsTrue(await reader.AtEndAsync());
        await reader.ReadAsync();
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
        Assert.IsFalse(await reader.AtEndAsync());
        await reader.ReadAsync(); // "x", then the index
        Assert.IsTrue(await reader.AtEndAsync());
        Assert.IsFalse(await reader.ReadAsync());
      }
    }

    /// <summary>
    /// A live stream: after a one byte element the source waits. AtEndAsync needs only that the next byte is not an escape.
    /// </summary>
    [TestMethod]
    public async Task AtEndDoesNotWaitForMoreThanItNeeds()
    {
      GatedStream source = new GatedStream(Hex("c1 90 01 02"));
      using (MsgPackStreamReader reader = new MsgPackStreamReader(source, Plain()))
      {
        Assert.IsTrue(await reader.ReadAsync());
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(1L, reader.GetInt64());
        Task<bool> atEnd = reader.AtEndAsync();
        Assert.IsTrue(atEnd.Wait(TimeSpan.FromSeconds(10)), "the 02 is enough");
        Assert.IsFalse(atEnd.Result);
        Assert.IsTrue(await reader.ReadAsync());

        atEnd = reader.AtEndAsync();
        await Task.Delay(50);
        Assert.IsFalse(atEnd.IsCompleted, "waits for the next byte");
        source.Release(Hex("c1 c0"));
        Assert.IsTrue(await atEnd);
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
      }
    }

    #endregion

    #region Objects

    [TestMethod]
    public async Task ObjectsWithTheirSchema()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions());
      byte[] bytes = Write(w =>
      {
        for (int n = 1; n <= 3; n++)
          w.WriteObject(Order(n));
        w.WriteObject(Order(1500)); // longer than the buffer
        w.WriteObject<SwOrder>(null);
      }, serializer, index: new MsgPackStreamIndexOptions());

      using (MsgPackStreamReader reader = AsyncReader(bytes, serializer))
      {
        for (int n = 1; n <= 3; n++)
          AreEqual(Order(n), await reader.ReadObjectAsync<SwOrder>());
        AreEqual(Order(1500), await reader.ReadObjectAsync<SwOrder>());
        Assert.IsNull(await reader.ReadObjectAsync<SwOrder>());
        Assert.IsTrue(await reader.AtEndAsync());
        await Assert.ThrowsExactlyAsync<MsgPackException>(() => reader.ReadObjectAsync<SwOrder>());
      }
    }

    [TestMethod]
    public async Task PerStreamRegistersTheInlineSchemas()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { WriteSchemaReference = true, SchemaStore = new SchemaStore() });
      byte[] bytes = Write(w =>
      {
        for (int n = 1; n <= 3; n++)
          w.WriteObject(Order(n));
      }, serializer, MsgPackStreamSchemas.PerStream);
      Assert.AreEqual(0xD8, bytes[(int)(Write(w => w.WriteObject(Order(1)), serializer, MsgPackStreamSchemas.PerStream).Length)], "the second object refers to the schema");

      using (MsgPackStreamReader reader = AsyncReader(bytes, new LtMsgPackSerializer(new LtMsgPackOptions { SchemaStore = new SchemaStore() })))
      {
        for (int n = 1; n <= 3; n++)
          AreEqual(Order(n), await reader.ReadObjectAsync<SwOrder>());
        Assert.IsTrue(await reader.AtEndAsync());
      }
    }

    [TestMethod]
    public async Task ObjectsWithValuesOfUnknownLengthWithoutTheIndexedSchema()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteArrayStart();
        for (int n = 1; n <= 3; n++)
        {
          w.WriteMapStart();
          w.Write("Customer"); w.WriteStringStart(); w.Write("Customer "); w.Write(n.ToString()); w.WriteEnd();
          w.Write("Lines"); w.WriteArrayStart(); w.WriteEnd();
          w.WriteEnd();
        }
        w.WriteEnd();
      });

      using (MsgPackStreamReader reader = AsyncReader(bytes))
      {
        List<SwOrder> read = new List<SwOrder>();
        await foreach (SwOrder order in reader.ReadElementsAsync<SwOrder>())
          read.Add(order);
        CollectionAssert.AreEqual(new[] { "Customer 1", "Customer 2", "Customer 3" }, read.Select(o => o.Customer).ToArray());
        Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
        Assert.IsFalse(await reader.ReadAsync());
      }
      using (MsgPackStreamReader reader = AsyncReader(bytes))
        Assert.HasCount(3, await reader.ReadObjectAsync<List<SwOrder>>());
    }

    [TestMethod]
    public async Task HeadAndArrayOfUnknownLength()
    {
      List<SwOrder> orders = Enumerable.Range(1, 4).Select(Order).ToList();
      orders.Add(Order(900));
      foreach (MsgPackStreamSchemas mode in new[] { MsgPackStreamSchemas.PerObject, MsgPackStreamSchemas.PerStream, MsgPackStreamSchemas.External })
      {
        SchemaStore store = new SchemaStore();
        byte[] bytes = Write(w =>
        {
          w.WriteHead<List<SwOrder>>();
          w.WriteArrayStart();
          foreach (SwOrder order in orders)
            w.WriteObject(order);
          w.WriteObject<SwOrder>(null);
          w.WriteEnd();
        }, new LtMsgPackSerializer(new LtMsgPackOptions { WriteSchemaReference = true, SchemaStore = store, CompleteSchemas = true }), mode, new MsgPackStreamIndexOptions { PointInterval = 2 });

        LtMsgPackSerializer reading = new LtMsgPackSerializer(new LtMsgPackOptions { SchemaStore = mode == MsgPackStreamSchemas.External ? store : new SchemaStore() });
        using (MsgPackStreamReader reader = AsyncReader(bytes, reading))
        {
          await reader.ReadHeadAsync<List<SwOrder>>();
          List<SwOrder> read = new List<SwOrder>();
          await foreach (SwOrder order in reader.ReadElementsAsync<SwOrder>())
            read.Add(order);
          Assert.HasCount(orders.Count + 1, read, mode.ToString());
          for (int t = 0; t < orders.Count; t++)
            AreEqual(orders[t], read[t]);
          Assert.IsNull(read[orders.Count]);
          Assert.IsFalse(await reader.ReadAsync());
        }

        using (MsgPackStreamReader reader = AsyncReader(bytes, reading)) // whole
          AreEqual(orders[4], (await reader.ReadObjectAsync<List<SwOrder>>())[4]);
      }
    }

    /// <summary>
    /// The payload of a null list is nil without a schema: ReadHead leaves it as the body.
    /// </summary>
    [TestMethod]
    public async Task HeadOfNil()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions());
      byte[] bytes = serializer.Serialize<List<SwOrder>>(null);
      CollectionAssert.AreEqual(new byte[] { 0xC0 }, bytes);
      using (MsgPackStreamReader reader = SyncReader(bytes, serializer))
      {
        reader.ReadHead<List<SwOrder>>();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(MsgPackTokenType.Nil, reader.TokenType);
        Assert.IsFalse(reader.Read());
      }
      using (MsgPackStreamReader reader = AsyncReader(bytes, serializer))
      {
        await reader.ReadHeadAsync<List<SwOrder>>();
        Assert.IsNull(await reader.ReadObjectAsync<List<SwOrder>>());
        Assert.IsTrue(await reader.AtEndAsync());
      }
    }

    [TestMethod]
    public async Task ElementsRefuseOtherValues()
    {
      using (MsgPackStreamReader reader = AsyncReader(Hex("81 01 02")))
      {
        InvalidOperationException error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => { await foreach (object unused in reader.ReadElementsAsync<object>()) { } });
        StringAssert.Contains(error.Message, "MapStart");
      }
    }

    #endregion

    #region Errors

    [TestMethod]
    public async Task ErrorsAreTheSynchronousOnes()
    {
      foreach (string hex in new[] { "c1 91", "c1 ff", "c1 90 01 c1", "c1 c0", "92 01 c1 c0", "c1 80 a1 61 c1 c0", "c1 a0 01 c1 c0", "c1 c4 a1 61 c1 c0", "c1 a0 c1 90 c1 c0 c1 c0",
        "c1 90 c1 90 c1 90 c1 c0 c1 c0 c1 c0", "c1 90 01", "db ff ff ff f0 61 62 63", "dd ff ff ff ff 01", "c1 90 dd ff ff ff ff 01 02 c6 10 00 00 00 01 02 03", "c7 05 03 01", "d8 01 02", "c7 05 05 01", "c1", "92 d4 01", "91 91 91 01", "92 01", "82 a1 61 91 91 01" })
      {
        LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, MaxDepth = 2 });

        Exception sync = Catch(() => { using (MsgPackStreamReader r = SyncReader(Hex(hex), serializer)) { while (r.Read()) { } } });
        Exception async = await CatchAsync(async () => { using (MsgPackStreamReader r = AsyncReader(Hex(hex), serializer)) { while (await r.ReadAsync()) { } } });
        AreSame(sync, async, "tokens " + hex);

        sync = Catch(() => { using (MsgPackStreamReader r = SyncReader(Hex(hex), serializer)) r.ReadObject<object>(); });
        async = await CatchAsync(async () => { using (MsgPackStreamReader r = AsyncReader(Hex(hex), serializer)) await r.ReadObjectAsync<object>(); });
        AreSame(sync, async, "object " + hex);

        sync = Catch(() => { using (MsgPackStreamReader r = SyncReader(Hex(hex), serializer)) { r.Read(); r.GetValue(); } });
        async = await CatchAsync(async () => { using (MsgPackStreamReader r = AsyncReader(Hex(hex), serializer)) { await r.ReadAsync(); await r.GetValueAsync(); } });
        AreSame(sync, async, "value " + hex);
      }
    }

    private static Exception Catch(Action action)
    {
      try
      {
        action();
        return null;
      }
      catch (Exception ex)
      {
        return ex;
      }
    }

    private static async Task<Exception> CatchAsync(Func<Task> action)
    {
      try
      {
        await action();
        return null;
      }
      catch (Exception ex)
      {
        return ex;
      }
    }

    private static void AreSame(Exception sync, Exception async, string what)
    {
      Assert.AreEqual(sync?.GetType(), async?.GetType(), what + ": " + async);
      Assert.AreEqual(sync?.Message, async?.Message, what);
    }

    #endregion

    /// <summary>
    /// Refuses synchronous reads, returns 1 to 3 bytes per read and completes every other read asynchronously.
    /// </summary>
    private sealed class AsyncOnlyStream : Stream
    {
      private readonly MemoryStream _inner;
      private int _reads;
      public AsyncOnlyStream(byte[] bytes) { _inner = new MemoryStream(bytes); }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return false; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { throw new NotSupportedException(); } }
      public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException("A synchronous read."); }
      public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
        if ((_reads++ & 1) == 0)
          await Task.Yield();
        return _inner.Read(buffer, offset, Math.Min(count, 1 + _reads % 3));
      }
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    /// <summary>
    /// Returns its bytes, then waits for more (<see cref="Release"/>), as a response whose source waits for the next item.
    /// </summary>
    private sealed class GatedStream : Stream
    {
      private readonly Queue<byte[]> _chunks = new Queue<byte[]>();
      private TaskCompletionSource<bool> _more = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      public GatedStream(byte[] bytes) { _chunks.Enqueue(bytes); }
      public void Release(byte[] bytes)
      {
        _chunks.Enqueue(bytes);
        _more.TrySetResult(true);
      }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return false; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { throw new NotSupportedException(); } }
      public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException("A synchronous read."); }
      public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
        while (_chunks.Count == 0)
        {
          await _more.Task;
          _more = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        byte[] chunk = _chunks.Dequeue();
        int n = Math.Min(count, chunk.Length);
        Buffer.BlockCopy(chunk, 0, buffer, offset, n);
        if (n < chunk.Length)
        {
          Queue<byte[]> rest = new Queue<byte[]>();
          rest.Enqueue(chunk.Skip(n).ToArray());
          while (_chunks.Count > 0)
            rest.Enqueue(_chunks.Dequeue());
          while (rest.Count > 0)
            _chunks.Enqueue(rest.Dequeue());
        }
        return n;
      }
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
  }
}
