using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// LsMsgPackSerializer.SerializeAsync / DeserializeAsync of one value on a stream, and the synchronous Deserialize of a stream that shares their reading:
  /// the bytes and values of the byte[] overloads, no synchronous I/O, never a byte past the value.
  /// </summary>
  [TestClass]
  public class LsAsyncStreamTests
  {
    public interface IAsShape { }

    public class AsCircle : IAsShape
    {
      public double Radius { get; set; }
    }

    public class AsLine
    {
      public string Product { get; set; }
      public int Quantity { get; set; }
    }

    public class AsOrder
    {
      public string Name { get; set; }
      public byte[] Blob { get; set; }
      public List<AsLine> Lines { get; set; }
      public IAsShape Shape { get; set; }
      public Dictionary<string, int> Counts { get; set; }
    }

    public struct AsPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    /// <summary>
    /// A newer <see cref="AsLine"/>: read as AsLine, Note is an unknown property (a difference).
    /// </summary>
    public class AsLineNoted
    {
      public string Product { get; set; }
      public int Quantity { get; set; }
      public string Note { get; set; }
    }

    public class AsNode
    {
      public string Name { get; set; }
      public AsNode Next { get; set; }
    }

    private static AsOrder NewOrder(int lines = 2)
    {
      return new AsOrder
      {
        Name = "order ü " + new string('x', 300), // str 16
        Blob = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray(), // bin 16
        Lines = Enumerable.Range(0, lines).Select(i => new AsLine { Product = "p" + i, Quantity = i * 1000 }).ToList(),
        Shape = new AsCircle { Radius = 1.5 },
        Counts = Enumerable.Range(0, 20).ToDictionary(i => "k" + i, i => i), // map 16
      };
    }

    private static string Dump(AsOrder order)
    {
      if (order is null)
        return "null";
      return order.Name + "|" + Convert.ToBase64String(order.Blob) + "|" + string.Join(",", order.Lines.Select(l => l.Product + ":" + l.Quantity))
        + "|" + ((AsCircle)order.Shape).Radius + "|" + string.Join(",", order.Counts.Select(c => c.Key + "=" + c.Value));
    }

    /// <summary>
    /// Without the schema, inline, references (the reader is the same serializer, it has the store), and lengths in little-endian (the scan reads them).
    /// </summary>
    private static IEnumerable<(string Name, LsMsgPackSerializer Serializer)> Serializers()
    {
      yield return ("names", new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false }));
      yield return ("names, map", new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map }));
      yield return ("inline", new LsMsgPackSerializer(new LsMsgPackOptions()));
      yield return ("inline, map", new LsMsgPackSerializer(new LsMsgPackOptions { ObjectLayout = ObjectLayout.Map }));
      yield return ("reference", new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = new SchemaStore(), WriteSchemaReference = true }));
      yield return ("little-endian", new LsMsgPackSerializer(new LsMsgPackOptions { EndianAction = EndianAction.NeverSwap }));
    }

    [TestMethod]
    public async Task SerializeAsync_SameBytesAsSerialize()
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        AsOrder order = NewOrder();
        await AssertWritten(lt.Serialize(order), s => lt.SerializeAsync(order, s), name);
        await AssertWritten(lt.Serialize(order), s => lt.SerializeAsync((object)order, typeof(AsOrder), s), name + ", boxed");
        await AssertWritten(lt.Serialize(order), s => lt.SerializeAsync((object)order, null, s), name + ", boxed, no type");
        await AssertWritten(lt.Serialize<object>(order.Shape), s => lt.SerializeAsync<object>(order.Shape, s), name + ", object");
        await AssertWritten(lt.Serialize(new AsPoint { X = 3, Y = -4 }), s => lt.SerializeAsync(new AsPoint { X = 3, Y = -4 }, s), name + ", struct");
        await AssertWritten(new byte[] { 0xC0 }, s => lt.SerializeAsync((AsOrder)null, s), name + ", null");
        await AssertWritten(lt.Serialize(42), s => lt.SerializeAsync(42, s), name + ", int");
        AsOrder large = NewOrder(5000); // the pooled buffer larger than its first size
        await AssertWritten(lt.Serialize(large), s => lt.SerializeAsync(large, s), name + ", large");
      }
    }

    /// <summary>
    /// The same bytes to a stream that refuses synchronous writes, and to a MemoryStream (written directly, the task is complete when it returns).
    /// </summary>
    private static async Task AssertWritten(byte[] expected, Func<Stream, Task> write, string setting)
    {
      AsyncOnlyStream target = new AsyncOnlyStream(new byte[0]);
      await write(target);
      CollectionAssert.AreEqual(expected, target.Written.ToArray(), setting);
      Assert.AreEqual(1, target.Writes, setting + ": one WriteAsync");

      MemoryStream memory = new MemoryStream();
      memory.WriteByte(7);
      Task written = write(memory);
      Assert.IsTrue(written.IsCompleted, setting + ": a MemoryStream needs no await");
      await written;
      CollectionAssert.AreEqual(new byte[] { 7 }.Concat(expected).ToArray(), memory.ToArray(), setting + ": after what is there");
    }

    [TestMethod]
    public async Task SerializeAsync_NothingWrittenWhenSerializingFails()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      AsNode node = new AsNode { Name = "loop" };
      node.Next = node;
      AsyncOnlyStream target = new AsyncOnlyStream(new byte[0]);
      Task task = lt.SerializeAsync(node, target);
      Assert.IsTrue(task.IsFaulted, "a faulted task, not an exception from the call");
      await Assert.ThrowsExactlyAsync<MsgPackException>(() => task);
      await Assert.ThrowsExactlyAsync<MsgPackException>(() => lt.SerializeAsync((object)node, typeof(AsNode), target));
      Assert.AreEqual(0, target.Written.Length);

      node.Next = null;
      await lt.SerializeAsync(node, target); // the state of the thread works after the failure
      CollectionAssert.AreEqual(lt.Serialize(node), target.Written.ToArray());
    }

    [TestMethod]
    public async Task Async_Arguments()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      Assert.ThrowsExactly<ArgumentNullException>(() => lt.SerializeAsync(1, null));
      Assert.ThrowsExactly<ArgumentNullException>(() => lt.SerializeAsync((object)1, typeof(int), null));
      Assert.ThrowsExactly<ArgumentNullException>(() => lt.DeserializeAsync<int>(null));
      Assert.ThrowsExactly<ArgumentNullException>(() => lt.DeserializeAsync(typeof(int), null));

      CancellationToken canceled = new CancellationToken(true);
      AsyncOnlyStream stream = new AsyncOnlyStream(lt.Serialize(1));
      await Assert.ThrowsAsync<OperationCanceledException>(() => lt.SerializeAsync(1, stream, canceled));
      await Assert.ThrowsAsync<OperationCanceledException>(() => lt.SerializeAsync(1, new MemoryStream(), canceled));
      await Assert.ThrowsAsync<OperationCanceledException>(() => lt.SerializeAsync((object)1, null, stream, canceled));
      await Assert.ThrowsAsync<OperationCanceledException>(() => lt.DeserializeAsync<int>(stream, canceled));
      await Assert.ThrowsAsync<OperationCanceledException>(() => lt.DeserializeAsync<int>(new MemoryStream(lt.Serialize(1)), canceled));
      await Assert.ThrowsAsync<OperationCanceledException>(() => lt.DeserializeAsync(typeof(int), stream, canceled));
      Assert.AreEqual(0, stream.Written.Length);
      Assert.AreEqual(0, stream.Consumed);
    }

    /// <summary>
    /// Values one after another, then a byte that is not read: each call leaves the stream right after its value, the values are those of the byte[] overloads.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeserializeAsync_ValuesInARow(bool wholeReads)
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        AsOrder order = NewOrder();
        AsOrder large = NewOrder(3000);
        List<byte[]> payloads = new List<byte[]>
        {
          lt.Serialize(order), lt.Serialize(new AsPoint { X = 3, Y = -4 }), lt.Serialize((AsOrder)null), lt.Serialize(-70000),
          lt.Serialize<object>(order.Shape), lt.Serialize(large), lt.Serialize("text"), lt.Serialize(order)
        };
        byte[] data = payloads.SelectMany(p => p).Concat(new byte[] { 0x2A }).ToArray();
        long[] ends = payloads.Select(p => (long)p.Length).ToArray();
        for (int t = 1; t < ends.Length; t++)
          ends[t] += ends[t - 1];

        AsyncOnlyStream stream = new AsyncOnlyStream(data, wholeReads);
        string expected = Dump(order);
        Assert.AreEqual(expected, Dump(await lt.DeserializeAsync<AsOrder>(stream)), name);
        Assert.AreEqual(ends[0], stream.Consumed, name + ": after the order");
        AsPoint point = await lt.DeserializeAsync<AsPoint>(stream);
        Assert.AreEqual(3, point.X, name);
        Assert.AreEqual(-4, point.Y, name);
        Assert.AreEqual(ends[1], stream.Consumed, name + ": after the struct");
        Assert.IsNull(await lt.DeserializeAsync<AsOrder>(stream), name);
        Assert.AreEqual(ends[2], stream.Consumed, name + ": after null");
        Assert.AreEqual(-70000, await lt.DeserializeAsync<int>(stream), name);
        Assert.AreEqual(ends[3], stream.Consumed, name + ": after the int");
        Assert.AreEqual(1.5, ((AsCircle)await lt.DeserializeAsync<IAsShape>(stream)).Radius, name);
        Assert.AreEqual(ends[4], stream.Consumed, name + ": after the shape");
        Assert.AreEqual(Dump(large), Dump((AsOrder)await lt.DeserializeAsync(typeof(AsOrder), stream)), name + ", large");
        Assert.AreEqual(ends[5], stream.Consumed, name + ": after the large order");
        Assert.AreEqual("text", await lt.DeserializeAsync(typeof(string), stream), name);
        Assert.AreEqual(ends[6], stream.Consumed, name + ": after the string");
        Assert.AreEqual(expected, Dump(await lt.DeserializeAsync<AsOrder>(stream)), name + ", again");
        Assert.AreEqual(ends[7], stream.Consumed, name + ": after the last order");
        byte[] rest = new byte[2];
        Assert.AreEqual(1, await stream.ReadAsync(rest, 0, 2), name);
        Assert.AreEqual(0x2A, rest[0], name);

        // the synchronous Deserialize of a stream reads the same way
        AsyncOnlyStream sync = new AsyncOnlyStream(data, wholeReads, allowSync: true);
        Assert.AreEqual(expected, Dump(lt.Deserialize<AsOrder>(sync)), name + ", sync");
        Assert.AreEqual(ends[0], sync.Consumed, name + ", sync");
        Assert.AreEqual(-4, lt.Deserialize<AsPoint>(sync).Y, name + ", sync");
        Assert.IsNull(lt.Deserialize<AsOrder>(sync), name + ", sync");
        Assert.AreEqual(-70000, lt.Deserialize<int>(sync), name + ", sync");
        Assert.AreEqual(1.5, ((AsCircle)lt.Deserialize<IAsShape>(sync)).Radius, name + ", sync");
        Assert.AreEqual(Dump(large), Dump(lt.Deserialize<AsOrder>(sync)), name + ", sync");
        Assert.AreEqual("text", lt.Deserialize<string>(sync), name + ", sync");
        Assert.AreEqual(expected, Dump(lt.Deserialize<AsOrder>(sync, out ReadDifferences differences)), name + ", sync");
        Assert.IsNull(differences, name + ", sync");
        Assert.AreEqual(ends[7], sync.Consumed, name + ", sync");
      }
    }

    [TestMethod]
    public async Task DeserializeAsync_MemoryStream_InPlaceWithoutAwait()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      AsOrder order = NewOrder();
      byte[] one = lt.Serialize(order);
      MemoryStream memory = new MemoryStream(one.Concat(one).ToArray());
      Task<AsOrder> first = lt.DeserializeAsync<AsOrder>(memory);
      Assert.IsTrue(first.IsCompleted);
      Assert.AreEqual(Dump(order), Dump(await first));
      Assert.AreEqual(one.Length, memory.Position);
      Assert.AreEqual(Dump(order), Dump((AsOrder)await lt.DeserializeAsync(typeof(AsOrder), memory)));
      Assert.AreEqual(memory.Length, memory.Position);

      Task<AsOrder> failed = lt.DeserializeAsync<AsOrder>(memory); // at the end
      Assert.IsTrue(failed.IsFaulted, "a faulted task, not an exception from the call");
      await Assert.ThrowsExactlyAsync<MsgPackException>(() => failed);
    }

    /// <summary>
    /// Data that ends anywhere, data that is not valid, nesting too deep, a claimed length that never arrives: the errors of the synchronous Deserialize of a stream.
    /// </summary>
    [TestMethod]
    public async Task Errors_AsTheSynchronousRead()
    {
      LsMsgPackSerializer names = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false });
      LsMsgPackSerializer inline = new LsMsgPackSerializer(new LsMsgPackOptions());
      byte[] order = names.Serialize(NewOrder());
      byte[] withSchema = inline.Serialize(NewOrder());
      for (int cut = 0; cut < order.Length; cut += cut < 40 ? 1 : 37)
        await AssertSameError<AsOrder>(names, order.Take(cut).ToArray(), "cut at " + cut);
      for (int cut = 0; cut < withSchema.Length; cut += cut < 40 ? 1 : 37)
        await AssertSameError<AsOrder>(inline, withSchema.Take(cut).ToArray(), "inline, cut at " + cut);

      string escape = await AssertSameError<object>(names, new byte[] { 0x92, 0x01, 0xC1, 0x00 }, "0xC1");
      StringAssert.Contains(escape, "0xC1");

      LsMsgPackSerializer shallow = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, MaxDepth = 3 });
      Assert.IsNotNull(shallow.Deserialize<object>(new NonSeekableStream(new byte[] { 0x91, 0x91, 0x91, 0x01 }))); // 3 levels: fine
      Assert.IsNotNull(await shallow.DeserializeAsync<object>(new AsyncOnlyStream(new byte[] { 0x91, 0x91, 0x91, 0x01 })));
      string deep = await AssertSameError<object>(shallow, new byte[] { 0x91, 0x91, 0x91, 0x91, 0x01 }, "depth");
      StringAssert.Contains(deep, "MaxDepth");
      await AssertSameError<object>(shallow, new byte[] { 0x91, 0x91, 0x91, 0x90 }, "depth, empty"); // an empty array too (as the reader)

      // a bin 32 of 2 GB that sends 10 bytes: the buffer grows only with what arrives
      byte[] claimed = new byte[] { 0xC6, 0x7F, 0xFF, 0xFF, 0xFF }.Concat(new byte[10]).ToArray();
      await AssertSameError<byte[]>(names, claimed, "claimed length");
      long before = GC.GetAllocatedBytesForCurrentThread(); // the synchronous read (it shares the scan): an await may continue on another thread
      Assert.ThrowsExactly<MsgPackException>(() => names.Deserialize<byte[]>(new NonSeekableStream(claimed)));
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      Assert.IsLessThan(1_000_000, allocated, $"no buffer of the claimed length: {allocated} B");
    }

    private static async Task<string> AssertSameError<T>(LsMsgPackSerializer lt, byte[] data, string setting)
    {
      Exception sync = null;
      try
      {
        lt.Deserialize<T>(new NonSeekableStream(data));
      }
      catch (Exception ex)
      {
        sync = ex;
      }
      Assert.IsNotNull(sync, setting + ": the synchronous read throws");
      Assert.IsInstanceOfType<MsgPackException>(sync, setting + ": " + sync);

      Task<T> task = lt.DeserializeAsync<T>(new AsyncOnlyStream(data));
      Exception async = await Assert.ThrowsExactlyAsync<MsgPackException>(() => task, setting);
      Assert.AreEqual(sync.Message, async.Message, setting);
      Exception untyped = await Assert.ThrowsExactlyAsync<MsgPackException>(() => lt.DeserializeAsync(typeof(T), new AsyncOnlyStream(data, true)), setting);
      Assert.AreEqual(sync.Message, untyped.Message, setting);
      return sync.Message;
    }

    #region Differences and Populate

    /// <summary>
    /// DeserializeAsync with a callback: called once with what the synchronous out overload reports, not called for data that matches; from a stream that refuses synchronous reads and from a MemoryStream (without awaiting).
    /// </summary>
    [TestMethod]
    public async Task DeserializeAsync_Differences_AsTheSynchronousRead()
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        byte[] data = lt.Serialize(new AsLineNoted { Product = "p", Quantity = 3, Note = "newer" }).Concat(new byte[] { 0x2A }).ToArray();
        AsLine expected = lt.Deserialize<AsLine>(data, out ReadDifferences sync);
        Assert.IsNotNull(sync, name + ": the premise, a difference");
        string report = sync.GenerateReport();

        List<ReadDifferences> reported = new List<ReadDifferences>();
        AsyncOnlyStream stream = new AsyncOnlyStream(data);
        AsLine read = await lt.DeserializeAsync<AsLine>(stream, reported.Add);
        Assert.AreEqual(expected.Product + expected.Quantity, read.Product + read.Quantity, name);
        Assert.AreEqual(data.Length - 1, stream.Consumed, name + ": left after the value");
        Assert.HasCount(1, reported, name);
        Assert.AreEqual(report, reported[0].GenerateReport(), name);
        Assert.AreSame(read, reported[0].Root, name);

        object untyped = await lt.DeserializeAsync(typeof(AsLine), new AsyncOnlyStream(data, true), reported.Add);
        Assert.AreEqual("p3", ((AsLine)untyped).Product + ((AsLine)untyped).Quantity, name);
        Assert.HasCount(2, reported, name);
        Assert.AreEqual(report, reported[1].GenerateReport(), name);

        MemoryStream memory = new MemoryStream(data);
        Task<AsLine> inMemory = lt.DeserializeAsync<AsLine>(memory, reported.Add);
        Assert.IsTrue(inMemory.IsCompleted, name + ": a MemoryStream needs no await");
        Assert.AreEqual("p", (await inMemory).Product, name);
        Assert.AreEqual(data.Length - 1, memory.Position, name);
        Task<object> untypedInMemory = lt.DeserializeAsync(typeof(AsLine), new MemoryStream(data), reported.Add);
        Assert.IsTrue(untypedInMemory.IsCompleted, name);
        await untypedInMemory;
        Assert.HasCount(4, reported, name);
        Assert.AreEqual(report, reported[3].GenerateReport(), name);

        // matching data: not called; null: read without reporting
        AsOrder order = NewOrder();
        byte[] matching = lt.Serialize(order);
        Assert.AreEqual(Dump(order), Dump(await lt.DeserializeAsync<AsOrder>(new AsyncOnlyStream(matching), d => Assert.Fail(name + ": called for matching data"))));
        Assert.AreEqual(Dump(order), Dump(await lt.DeserializeAsync<AsOrder>(new MemoryStream(matching), d => Assert.Fail(name + ": called for matching data"))));
        Assert.AreEqual(Dump(order), Dump((AsOrder)await lt.DeserializeAsync(typeof(AsOrder), new AsyncOnlyStream(matching), null)));
        Assert.AreEqual(Dump(order), Dump(await lt.DeserializeAsync<AsOrder>(new AsyncOnlyStream(matching), null, CancellationToken.None)));
      }
    }

    /// <summary>
    /// A failed read does not call back: the exception is the synchronous read's, with the differences found until then in its Data when the synchronous one has them.
    /// </summary>
    [TestMethod]
    public async Task DeserializeAsync_Differences_NotCalledWhenReadingFails()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map });
      // an unknown property, then a value that is no number (ReadErrors.FailFast)
      byte[] data = lt.Serialize(new List<Dictionary<string, object>> { new Dictionary<string, object> { { "Product", "p" }, { "Note", "n" } }, new Dictionary<string, object> { { "Quantity", "many" } } });
      Exception sync = null;
      try
      {
        lt.Deserialize<List<AsLine>>(new NonSeekableStream(data), out ReadDifferences unused);
      }
      catch (Exception ex)
      {
        sync = ex;
      }
      Assert.IsNotNull(sync, "the premise: the read fails");
      Assert.IsTrue(sync.Data.Contains(ReadDifferences.ExceptionDataKey), "the premise: differences before the error");
      foreach (Stream source in new Stream[] { new AsyncOnlyStream(data), new MemoryStream(data) })
      {
        Exception async = null;
        try
        {
          await lt.DeserializeAsync<List<AsLine>>(source, d => Assert.Fail("called after a failure"));
        }
        catch (Exception ex)
        {
          async = ex;
        }
        Assert.IsNotNull(async, source.GetType().Name);
        Assert.AreEqual(sync.GetType(), async.GetType());
        Assert.AreEqual(sync.Message, async.Message);
        Assert.AreEqual(((ReadDifferences)sync.Data[ReadDifferences.ExceptionDataKey]).GenerateReport(), ((ReadDifferences)async.Data[ReadDifferences.ExceptionDataKey]).GenerateReport());
      }
    }

    /// <summary>
    /// PopulateAsync: the target gets the values (the same as the synchronous Populate), the stream is left after the value, a MemoryStream is read without awaiting,
    /// a collection target is cleared and filled, the differences as Populate's out overload reports them.
    /// </summary>
    [TestMethod]
    public async Task PopulateAsync_AsTheSynchronousPopulate()
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        AsOrder written = NewOrder();
        byte[] data = lt.Serialize(written).Concat(new byte[] { 0x2A }).ToArray();
        AsOrder target = new AsOrder();
        AsyncOnlyStream stream = new AsyncOnlyStream(data);
        await lt.PopulateAsync(stream, target);
        Assert.AreEqual(Dump(written), Dump(target), name);
        Assert.AreEqual(data.Length - 1, stream.Consumed, name + ": left after the value");

        MemoryStream memory = new MemoryStream(data);
        AsOrder inMemory = new AsOrder();
        Task populated = lt.PopulateAsync(memory, inMemory);
        Assert.IsTrue(populated.IsCompleted, name + ": a MemoryStream needs no await");
        await populated;
        Assert.AreEqual(Dump(written), Dump(inMemory), name);
        Assert.AreEqual(data.Length - 1, memory.Position, name);

        List<AsLine> lines = new List<AsLine> { new AsLine { Product = "old" } };
        await lt.PopulateAsync(new AsyncOnlyStream(lt.Serialize(written.Lines)), lines);
        Assert.AreEqual("p0:0,p1:1000", string.Join(",", lines.Select(l => l.Product + ":" + l.Quantity)), name + ": cleared and filled");

        AsOrder nil = new AsOrder { Name = "kept" };
        await lt.PopulateAsync(new AsyncOnlyStream(new byte[] { 0xC0 }), nil);
        Assert.AreEqual("kept", nil.Name, name + ": nil leaves the target");

        byte[] newer = lt.Serialize(new AsLineNoted { Product = "p", Quantity = 3, Note = "newer" });
        lt.Populate(newer, new AsLine(), out ReadDifferences sync);
        Assert.IsNotNull(sync, name + ": the premise, a difference");
        List<ReadDifferences> reported = new List<ReadDifferences>();
        AsLine line = new AsLine();
        await lt.PopulateAsync(new AsyncOnlyStream(newer), line, reported.Add);
        Assert.AreEqual("p3", line.Product + line.Quantity, name);
        await lt.PopulateAsync(new MemoryStream(newer), new AsLine(), reported.Add);
        Assert.HasCount(2, reported, name);
        Assert.AreEqual(sync.GenerateReport(), reported[0].GenerateReport(), name);
        Assert.AreEqual(sync.GenerateReport(), reported[1].GenerateReport(), name);
        await lt.PopulateAsync(new AsyncOnlyStream(data), new AsOrder(), d => Assert.Fail(name + ": called for matching data"));
      }
    }

    /// <summary>
    /// The members the data lacks keep their values (a map without them); a target that cannot be read into throws before the stream is read; errors as the synchronous Populate.
    /// </summary>
    [TestMethod]
    public async Task PopulateAsync_KeepsMissingMembers_RefusesBeforeReading()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map });
      AsLine line = new AsLine { Product = "old", Quantity = 5 };
      await lt.PopulateAsync(new AsyncOnlyStream(lt.Serialize(new Dictionary<string, object> { { "Product", "new" } })), line);
      Assert.AreEqual("new5", line.Product + line.Quantity);

      AsyncOnlyStream untouched = new AsyncOnlyStream(lt.Serialize(NewOrder()));
      Assert.ThrowsExactly<ArgumentException>(() => lt.PopulateAsync(untouched, "a string"));
      Assert.ThrowsExactly<ArgumentNullException>(() => lt.PopulateAsync<AsOrder>(untouched, null));
      Assert.AreEqual(0, untouched.Consumed, "nothing read");

      byte[] cut = lt.Serialize(NewOrder()).Take(40).ToArray();
      Exception sync = Assert.ThrowsExactly<MsgPackException>(() => lt.Populate(new NonSeekableStream(cut), new AsOrder()));
      Exception async = await Assert.ThrowsExactlyAsync<MsgPackException>(() => lt.PopulateAsync(new AsyncOnlyStream(cut), new AsOrder()));
      Assert.AreEqual(sync.Message, async.Message);
      Task failed = lt.PopulateAsync(new MemoryStream(cut), new AsOrder());
      Assert.IsTrue(failed.IsFaulted, "a faulted task, not an exception from the call");

      using (CancellationTokenSource cancel = new CancellationTokenSource())
      {
        cancel.Cancel();
        Task cancelled = lt.PopulateAsync(new AsyncOnlyStream(cut), new AsOrder(), cancel.Token);
        Assert.IsTrue(cancelled.IsCanceled);
      }
    }

    #endregion

    /// <summary>
    /// The stream cannot be peeked: each read asks for what the value needs at least, so a list of 1000 objects takes a few dozen reads, not one per byte.
    /// </summary>
    [TestMethod]
    public async Task Reads_GrowWithWhatTheHeadersTell()
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        AsOrder order = NewOrder(1000);
        byte[] data = lt.Serialize(order);
        AsyncOnlyStream stream = new AsyncOnlyStream(data, wholeReads: true);
        Assert.AreEqual(Dump(order), Dump(await lt.DeserializeAsync<AsOrder>(stream)), name);
        Assert.AreEqual(data.Length, stream.Consumed, name);
        Console.WriteLine($"{name}: {stream.Reads} reads for {data.Length} bytes");
        Assert.IsLessThan(data.Length / 50, stream.Reads, $"{name}: {stream.Reads} reads for {data.Length} bytes");
      }
    }

    /// <summary>
    /// A connection that sends a value in parts and then waits: the call waits too (without a thread), and cancelling it ends the wait.
    /// </summary>
    [TestMethod]
    public async Task DeserializeAsync_WaitsForTheRestAndCanBeCancelled()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      byte[] data = lt.Serialize(NewOrder());
      GatedStream stream = new GatedStream(data.Take(10).ToArray());
      Task<AsOrder> reading = lt.DeserializeAsync<AsOrder>(stream);
      await Task.Delay(50);
      Assert.IsFalse(reading.IsCompleted);
      stream.Release(data.Skip(10).ToArray());
      Assert.AreEqual(Dump(NewOrder()), Dump(await reading));

      GatedStream waiting = new GatedStream(data.Take(10).ToArray());
      using (CancellationTokenSource cancel = new CancellationTokenSource())
      {
        Task<AsOrder> cancelled = lt.DeserializeAsync<AsOrder>(waiting, cancel.Token);
        await Task.Delay(50);
        Assert.IsFalse(cancelled.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
      }
    }

    /// <summary>
    /// Refuses synchronous reads and writes (unless <c>allowSync</c>), returns 1 to 3 bytes per read (or all that is asked, <c>wholeReads</c>) and completes every other call asynchronously.
    /// </summary>
    private sealed class AsyncOnlyStream : Stream
    {
      private readonly MemoryStream _inner;
      private readonly bool _wholeReads;
      private readonly bool _allowSync;
      public readonly MemoryStream Written = new MemoryStream();
      public int Reads;
      public int Writes;
      public AsyncOnlyStream(byte[] bytes, bool wholeReads = false, bool allowSync = false) { _inner = new MemoryStream(bytes); _wholeReads = wholeReads; _allowSync = allowSync; }
      public long Consumed { get { return _inner.Position; } }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return false; } }
      public override bool CanWrite { get { return true; } }
      public override long Length { get { throw new NotSupportedException(); } }
      public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count)
      {
        if (!_allowSync)
          throw new NotSupportedException("A synchronous read.");
        return _inner.Read(buffer, offset, _wholeReads ? count : Math.Min(count, 1 + ++Reads % 3));
      }
      public override int ReadByte() { throw new NotSupportedException("Read byte by byte."); }
      public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if ((Reads++ & 1) == 0)
          await Task.Yield();
        return _inner.Read(buffer, offset, _wholeReads ? count : Math.Min(count, 1 + Reads % 3));
      }
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException("A synchronous write."); }
      public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        Writes++;
        Written.Write(buffer, offset, count);
      }
    }

    /// <summary>
    /// Synchronous reads of 1 to 3 bytes.
    /// </summary>
    private sealed class NonSeekableStream : Stream
    {
      private readonly MemoryStream _inner;
      private int _reads;
      public NonSeekableStream(byte[] bytes) { _inner = new MemoryStream(bytes); }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return false; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { throw new NotSupportedException(); } }
      public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count) { return _inner.Read(buffer, offset, Math.Min(count, 1 + ++_reads % 3)); }
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    /// <summary>
    /// Returns its bytes, then waits for more (<see cref="Release"/>) or the cancellation of the read.
    /// </summary>
    private sealed class GatedStream : Stream
    {
      private readonly Queue<byte> _bytes;
      private TaskCompletionSource<bool> _more = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      public GatedStream(byte[] bytes) { _bytes = new Queue<byte>(bytes); }
      public void Release(byte[] bytes)
      {
        foreach (byte b in bytes)
          _bytes.Enqueue(b);
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
        while (_bytes.Count == 0)
        {
          Task waited = await Task.WhenAny(_more.Task, Task.Delay(Timeout.Infinite, cancellationToken));
          cancellationToken.ThrowIfCancellationRequested();
          _more = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        int n = 0;
        while (n < count && _bytes.Count > 0)
          buffer[offset + n++] = _bytes.Dequeue();
        return n;
      }
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
  }
}
