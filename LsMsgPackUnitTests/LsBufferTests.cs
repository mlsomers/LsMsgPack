using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The overloads of LsMsgPackSerializer for buffers: writing to an <see cref="IBufferWriter{T}"/>, reading from <see cref="ReadOnlyMemory{T}"/> and <see cref="ReadOnlySequence{T}"/>.
  /// They give the bytes and values of the byte[] overloads.
  /// </summary>
  [TestClass]
  public class LsBufferTests
  {
    public interface IBufShape { }

    public class BufCircle : IBufShape
    {
      public double Radius { get; set; }
    }

    public class BufLine
    {
      public string Product { get; set; }
      public int Quantity { get; set; }
    }

    public class BufOrder
    {
      public string Name { get; set; }
      public byte[] Blob { get; set; }
      public List<BufLine> Lines { get; set; }
      public IBufShape Shape { get; set; }
    }

    /// <summary>
    /// <see cref="BufOrder"/> with a property more (written as BufOrder, see <see cref="Differences_AsTheByteArrayOverloads"/>).
    /// </summary>
    public class BufOrderNewer
    {
      public string Name { get; set; }
      public byte[] Blob { get; set; }
      public List<BufLine> Lines { get; set; }
      public IBufShape Shape { get; set; }
      public string Note { get; set; }
    }

    public struct BufPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public class BufNode
    {
      public string Name { get; set; }
      public BufNode Next { get; set; }
    }

    private static BufOrder NewOrder()
    {
      return new BufOrder
      {
        Name = "order ü", // not only ASCII
        Blob = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray(),
        Lines = new List<BufLine> { new BufLine { Product = "pen", Quantity = 2 }, new BufLine { Product = "ink", Quantity = 70000 } },
        Shape = new BufCircle { Radius = 1.5 }
      };
    }

    private static void AssertOrder(BufOrder expected, BufOrder actual, string setting)
    {
      Assert.IsNotNull(actual, setting);
      Assert.AreEqual(expected.Name, actual.Name, setting);
      CollectionAssert.AreEqual(expected.Blob, actual.Blob, setting);
      Assert.HasCount(expected.Lines.Count, actual.Lines, setting);
      for (int t = 0; t < expected.Lines.Count; t++)
      {
        Assert.AreEqual(expected.Lines[t].Product, actual.Lines[t].Product, setting);
        Assert.AreEqual(expected.Lines[t].Quantity, actual.Lines[t].Quantity, setting);
      }
      Assert.IsInstanceOfType<BufCircle>(actual.Shape, setting);
      Assert.AreEqual(((BufCircle)expected.Shape).Radius, ((BufCircle)actual.Shape).Radius, setting);
    }

    /// <summary>
    /// Without the schema, with the inline schema, and with references to a schema store (the reader is the same serializer, it has the store).
    /// </summary>
    private static IEnumerable<(string Name, LsMsgPackSerializer Serializer)> Serializers()
    {
      yield return ("names", new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false }));
      yield return ("names, map", new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }));
      yield return ("inline", new LsMsgPackSerializer(new LsMsgPackOptions()));
      yield return ("reference", new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = new SchemaStore(), WriteSchemaReference = true }));
    }

    [TestMethod]
    public void BufferWriter_SameBytesAsByteArray()
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        BufOrder order = NewOrder();
        byte[] expected = lt.Serialize(order);

        ArrayBufferWriter<byte> target = new ArrayBufferWriter<byte>();
        lt.Serialize(order, target);
        CollectionAssert.AreEqual(expected, target.WrittenSpan.ToArray(), name);

        target = new ArrayBufferWriter<byte>();
        lt.Serialize((object)order, typeof(BufOrder), target);
        CollectionAssert.AreEqual(expected, target.WrittenSpan.ToArray(), name + ", boxed");

        target = new ArrayBufferWriter<byte>();
        lt.Serialize<object>(order.Shape, target); // a type id at the root
        CollectionAssert.AreEqual(lt.Serialize<object>(order.Shape), target.WrittenSpan.ToArray(), name + ", object");

        target = new ArrayBufferWriter<byte>();
        lt.Serialize(new BufPoint { X = 3, Y = -4 }, target); // a struct root, in the slot
        CollectionAssert.AreEqual(lt.Serialize(new BufPoint { X = 3, Y = -4 }), target.WrittenSpan.ToArray(), name + ", struct");

        target = new ArrayBufferWriter<byte>();
        lt.Serialize((BufOrder)null, target);
        CollectionAssert.AreEqual(new byte[] { 0xC0 }, target.WrittenSpan.ToArray(), name + ", null");

        target = new ArrayBufferWriter<byte>();
        lt.Serialize(42, target);
        CollectionAssert.AreEqual(lt.Serialize(42), target.WrittenSpan.ToArray(), name + ", int");
      }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(100)]
    public void BufferWriter_InPieces(int piece)
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        BufOrder order = NewOrder();
        PieceWriter target = new PieceWriter(piece);
        lt.Serialize(order, target);
        CollectionAssert.AreEqual(lt.Serialize(order), target.Written, name);
      }
    }

    [TestMethod]
    public void BufferWriter_AppendsAfterWhatIsThere()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      ArrayBufferWriter<byte> target = new ArrayBufferWriter<byte>();
      target.Write(new byte[] { 1, 2, 3 });
      lt.Serialize(new BufPoint { X = 1 }, target);
      lt.Serialize(NewOrder(), target);
      byte[] expected = new byte[] { 1, 2, 3 }.Concat(lt.Serialize(new BufPoint { X = 1 })).Concat(lt.Serialize(NewOrder())).ToArray();
      CollectionAssert.AreEqual(expected, target.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void BufferWriter_NothingWrittenWhenSerializingFails()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      BufNode node = new BufNode { Name = "loop" };
      node.Next = node;
      ArrayBufferWriter<byte> target = new ArrayBufferWriter<byte>();
      Assert.ThrowsExactly<MsgPackException>(() => lt.Serialize(node, target));
      Assert.AreEqual(0, target.WrittenCount);

      node.Next = null;
      lt.Serialize(node, target); // the state of the thread works after the failure
      CollectionAssert.AreEqual(lt.Serialize(node), target.WrittenSpan.ToArray());
    }

    /// <summary>
    /// A target that serializes something itself when asked for its buffer: the nested call has its own state.
    /// </summary>
    [TestMethod]
    public void BufferWriter_NestedCall()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      ArrayBufferWriter<byte> inner = new ArrayBufferWriter<byte>();
      NestingWriter target = new NestingWriter(() => lt.Serialize(new BufPoint { X = 7 }, inner));
      lt.Serialize(NewOrder(), target);
      CollectionAssert.AreEqual(lt.Serialize(NewOrder()), target.Written);
      CollectionAssert.AreEqual(lt.Serialize(new BufPoint { X = 7 }), inner.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void ReadOnlyMemory_SameValues()
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        BufOrder order = NewOrder();
        byte[] payload = lt.Serialize(order);

        AssertOrder(order, lt.Deserialize<BufOrder>(new ReadOnlyMemory<byte>(payload)), name);
        AssertOrder(order, (BufOrder)lt.Deserialize(typeof(BufOrder), new ReadOnlyMemory<byte>(payload)), name + ", Type");

        // A slice of a larger array (in place), also as an ArraySegment
        byte[] padded = new byte[] { 0xFF, 0xFF }.Concat(payload).Concat(new byte[] { 0xFF }).ToArray();
        AssertOrder(order, lt.Deserialize<BufOrder>(new ReadOnlyMemory<byte>(padded, 2, payload.Length)), name + ", slice");
        AssertOrder(order, lt.Deserialize<BufOrder>(new ArraySegment<byte>(padded, 2, payload.Length)), name + ", segment");

        // Memory that is not an array's: read from a pooled copy
        BufOrder copied = lt.Deserialize<BufOrder>(new OpaqueMemory(padded.ToArray()).Memory.Slice(2, payload.Length));
        AssertOrder(order, copied, name + ", not an array");

        BufPoint point = lt.Deserialize<BufPoint>(new OpaqueMemory(lt.Serialize(new BufPoint { X = 5, Y = 6 })).Memory);
        Assert.AreEqual(5, point.X, name);
        Assert.AreEqual(6, point.Y, name);
        Assert.IsNull(lt.Deserialize<BufOrder>(new ReadOnlyMemory<byte>(new byte[] { 0xC0 })), name);
      }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    [DataRow(1000)] // one segment
    public void ReadOnlySequence_SameValues(int piece)
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        BufOrder order = NewOrder();
        byte[] payload = lt.Serialize(order);
        ReadOnlySequence<byte> sequence = Split(payload, piece);
        Assert.AreEqual(piece >= payload.Length, sequence.IsSingleSegment, name);

        AssertOrder(order, lt.Deserialize<BufOrder>(sequence), name);
        AssertOrder(order, (BufOrder)lt.Deserialize(typeof(BufOrder), sequence), name + ", Type");

        BufPoint point = lt.Deserialize<BufPoint>(Split(lt.Serialize(new BufPoint { X = 5, Y = 6 }), piece));
        Assert.AreEqual(5, point.X, name);
        Assert.AreEqual(6, point.Y, name);
        Assert.AreEqual(42, lt.Deserialize<int>(Split(lt.Serialize(42), piece)), name);
      }
    }

    /// <summary>
    /// The objects read do not refer to the data: the source is overwritten after reading, the pooled copy is cleared.
    /// </summary>
    [TestMethod]
    public void ValuesDoNotReferToTheData()
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        BufOrder order = NewOrder();
        byte[] payload = lt.Serialize(order);

        byte[] source = payload.ToArray();
        BufOrder inPlace = lt.Deserialize<BufOrder>(new ReadOnlyMemory<byte>(source));
        source.AsSpan().Fill(0xFF);
        AssertOrder(order, inPlace, name + ", in place");

        source = payload.ToArray();
        BufOrder fromSequence = lt.Deserialize<BufOrder>(Split(source, 5));
        source.AsSpan().Fill(0xFF);
        AssertOrder(order, fromSequence, name + ", sequence");
      }
    }

    [TestMethod]
    public void Errors_AsTheByteArrayOverloads()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false });
      byte[] truncated = new byte[] { 0x82, 0xA4, (byte)'N', (byte)'a' };
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<BufNode>(truncated));
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<BufNode>(new ReadOnlyMemory<byte>(truncated)));
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<BufNode>(new OpaqueMemory(truncated).Memory));
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<BufNode>(Split(truncated, 1)));
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<BufNode>(ReadOnlyMemory<byte>.Empty));
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<BufNode>(ReadOnlySequence<byte>.Empty));
    }

    [TestMethod]
    public void Differences_AsTheByteArrayOverloads()
    {
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        BufOrder order = NewOrder();
        BufOrderNewer newer = new BufOrderNewer { Name = order.Name, Blob = order.Blob, Lines = order.Lines, Shape = order.Shape, Note = "new" };
        byte[] payload = lt.Serialize(newer);
        string expected = Report(lt.Deserialize<BufOrder>(payload, out ReadDifferences differences), differences, order, name);

        Assert.AreEqual(expected, Report(lt.Deserialize<BufOrder>(new ReadOnlyMemory<byte>(payload), out differences), differences, order, name + ", memory"));
        Assert.AreEqual(expected, Report(lt.Deserialize<BufOrder>(new OpaqueMemory(payload).Memory, out differences), differences, order, name + ", not an array"));
        Assert.AreEqual(expected, Report(lt.Deserialize<BufOrder>(Split(payload, 3), out differences), differences, order, name + ", sequence"));
        Assert.AreEqual(expected, Report((BufOrder)lt.Deserialize(typeof(BufOrder), new ReadOnlyMemory<byte>(payload), out differences), differences, order, name + ", memory, Type"));
        Assert.AreEqual(expected, Report((BufOrder)lt.Deserialize(typeof(BufOrder), Split(payload, 3), out differences), differences, order, name + ", sequence, Type"));

        Assert.IsNull(Report(lt.Deserialize<BufOrder>(Split(lt.Serialize(order), 3), out differences), differences, order, name + ", matching"));
      }
    }

    private static string Report(BufOrder read, ReadDifferences differences, BufOrder expected, string setting)
    {
      AssertOrder(expected, read, setting);
      return differences?.GenerateReport();
    }

    /// <summary>
    /// Writing to a reused buffer allocates nothing (a class and a struct root). Only measured when the code is optimized, as <see cref="LsStructTests"/>.
    /// </summary>
    [TestMethod]
    public void BufferWriter_NoAllocation()
    {
      bool optimized = IsOptimized(typeof(LsMsgPackSerializer)) && IsOptimized(typeof(LsBufferTests));
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        BufLine line = new BufLine { Product = "pen", Quantity = 2 };
        BufPoint point = new BufPoint { X = 1, Y = 2 };
        ArrayBufferWriter<byte> target = new ArrayBufferWriter<byte>(1024);
        long lineBytes = Allocated(lt, line, target); // one root type at a time: with schema references the serializer keeps the writer of the last root type only
        CollectionAssert.AreEqual(lt.Serialize(line), target.WrittenSpan.ToArray(), name);
        long pointBytes = Allocated(lt, point, target);
        CollectionAssert.AreEqual(lt.Serialize(point), target.WrittenSpan.ToArray(), name);
        if (optimized)
        {
          Assert.AreEqual(0, lineBytes, name + ", class");
          Assert.AreEqual(0, pointBytes, name + ", struct");
        }
      }
    }

    /// <summary>
    /// A server writing several root types in turn allocates nothing either (with schema references the serializer keeps the session of each root type).
    /// </summary>
    [TestMethod]
    public void BufferWriter_AlternatingRootTypes_NoAllocation()
    {
      bool optimized = IsOptimized(typeof(LsMsgPackSerializer)) && IsOptimized(typeof(LsBufferTests));
      foreach ((string name, LsMsgPackSerializer lt) in Serializers())
      {
        BufLine line = new BufLine { Product = "pen", Quantity = 2 };
        BufPoint point = new BufPoint { X = 1, Y = 2 };
        BufNode node = new BufNode { Name = "n" };
        ArrayBufferWriter<byte> target = new ArrayBufferWriter<byte>(1024);
        for (int t = 0; t < 3; t++)
        {
          target.ResetWrittenCount();
          lt.Serialize(line, target);
          lt.Serialize(point, target);
          lt.Serialize(node, target);
        }
        target.ResetWrittenCount();
        long before = GC.GetAllocatedBytesForCurrentThread();
        lt.Serialize(line, target);
        lt.Serialize(point, target);
        lt.Serialize(node, target);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        CollectionAssert.AreEqual(lt.Serialize(line).Concat(lt.Serialize(point)).Concat(lt.Serialize(node)).ToArray(), target.WrittenSpan.ToArray(), name);
        if (optimized)
          Assert.AreEqual(0, allocated, name);
      }
    }

    /// <summary>
    /// The web formatters' path (LsMsgPackHttpSerializer.Serialize with a callback): the payload of the byte[] overload in every case of the negotiation, split the same way.
    /// </summary>
    [TestMethod]
    public void Http_CallbackPayload_SameAsByteArrayPayload()
    {
      LsMsgPack.Http.LsMsgPackHttpSerializer server = new LsMsgPack.Http.LsMsgPackHttpSerializer(new LsMsgPack.Http.LsMsgPackHttpOptions());
      BufOrder order = NewOrder();
      string held = server.Serialize(order, typeof(BufOrder), MsgPackMediaTypes.XLsMsgPack, null).SchemaId; // the id the client then holds
      string heldDescribed = server.Serialize(order, typeof(BufOrder), MsgPackMediaTypes.XLsMsgPack, null, true).SchemaId;
      Assert.IsNotNull(held);

      var cases = new (string Name, object Value, string MediaType, string ClientSchemas, bool Described)[]
      {
        ("inline (the client lacks the schema)", order, MsgPackMediaTypes.XLsMsgPack, null, false),
        ("reference (the client holds it)", order, MsgPackMediaTypes.XLsMsgPack, held, false),
        ("described, inline", order, MsgPackMediaTypes.XLsMsgPack, null, true),
        ("described, reference", order, MsgPackMediaTypes.XLsMsgPack, heldDescribed, true),
        ("plain", order, MsgPackMediaTypes.MsgPack, held, false),
        ("null", null, MsgPackMediaTypes.XLsMsgPack, held, false),
        ("small schema, sent inline anyway", 42, MsgPackMediaTypes.XLsMsgPack, null, false),
      };
      foreach (var c in cases)
      {
        LsMsgPack.Http.MsgPackPayload expected = server.Serialize(c.Value, c.Value is int ? typeof(int) : typeof(BufOrder), c.MediaType, c.ClientSchemas, c.Described);
        int calls = 0;
        byte[] schema = null, body = null;
        string id = null;
        bool reference = false;
        int length = -1;
        server.Serialize(c.Value, c.Value is int ? typeof(int) : typeof(BufOrder), c.MediaType, c.ClientSchemas, c.Described, 0, (state, payload) =>
        {
          calls++;
          schema = payload.Schema.AsSpan().ToArray(); // valid only during the call
          body = payload.Body.AsSpan().ToArray();
          id = payload.SchemaId;
          reference = payload.IsReference;
          length = payload.Length;
        });
        Assert.AreEqual(1, calls, c.Name);
        CollectionAssert.AreEqual(expected.Schema.AsSpan().ToArray(), schema, c.Name + ": head");
        CollectionAssert.AreEqual(expected.Body.AsSpan().ToArray(), body, c.Name + ": body");
        Assert.AreEqual(expected.SchemaId, id, c.Name);
        Assert.AreEqual(expected.IsReference, reference, c.Name);
        Assert.AreEqual(expected.Length, length, c.Name);
        if (c.Name.StartsWith("reference", StringComparison.Ordinal) || c.Name == "described, reference")
          Assert.IsTrue(reference && schema.Length == SchemaStore.ReferenceLength, c.Name + ": the head is the reference");
      }

      // Not called when serializing fails
      BufNode loop = new BufNode();
      loop.Next = loop;
      int failed = 0;
      Assert.ThrowsExactly<MsgPackException>(() => server.Serialize(loop, typeof(BufNode), MsgPackMediaTypes.XLsMsgPack, null, false, 0, (state, payload) => failed++));
      Assert.AreEqual(0, failed);
    }

    [TestMethod]
    public void Http_DeserializeBody_Sequence()
    {
      LsMsgPack.Http.LsMsgPackHttpOptions options = new LsMsgPack.Http.LsMsgPackHttpOptions { ReportDifferences = true };
      LsMsgPack.Http.LsMsgPackHttpSerializer http = new LsMsgPack.Http.LsMsgPackHttpSerializer(options);
      BufOrder order = NewOrder();
      foreach (string mediaType in new[] { MsgPackMediaTypes.XLsMsgPack, MsgPackMediaTypes.MsgPack })
      {
        // Without the IBufShape: plain MsgPack (the Generic() preset) has no type ids
        byte[] data = http.Serialize(new BufOrderNewer { Name = order.Name, Blob = order.Blob, Lines = order.Lines, Note = "x" }, typeof(BufOrderNewer), mediaType).ToArray();
        string expected = BodyReport((BufOrder)http.DeserializeBody(typeof(BufOrder), data, 0, data.Length, mediaType, out ReadDifferences differences), differences, order, mediaType);
        Assert.IsNotNull(expected, mediaType + ": the unknown property is reported");
        foreach (int piece in new[] { 1, 7, data.Length })
          Assert.AreEqual(expected, BodyReport((BufOrder)http.DeserializeBody(typeof(BufOrder), Split(data, piece), mediaType, out differences), differences, order, mediaType + ", " + piece));
      }
    }

    private static string BodyReport(BufOrder read, ReadDifferences differences, BufOrder expected, string setting)
    {
      Assert.AreEqual(expected.Name, read.Name, setting);
      CollectionAssert.AreEqual(expected.Blob, read.Blob, setting);
      Assert.AreEqual(expected.Lines[1].Quantity, read.Lines[1].Quantity, setting);
      return differences?.GenerateReport();
    }

    /// <returns>The bytes allocated by writing <paramref name="value"/> after a warm up, the target then holds that payload</returns>
    private static long Allocated<T>(LsMsgPackSerializer lt, T value, ArrayBufferWriter<byte> target)
    {
      for (int t = 0; t < 3; t++)
      {
        target.ResetWrittenCount();
        lt.Serialize(value, target);
      }
      target.ResetWrittenCount();
      long before = GC.GetAllocatedBytesForCurrentThread();
      lt.Serialize(value, target);
      return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static bool IsOptimized(Type type)
    {
      DebuggableAttribute debuggable = type.Assembly.GetCustomAttribute<DebuggableAttribute>();
      return debuggable == null || !debuggable.IsJITOptimizerDisabled;
    }

    #region Buffers

    /// <summary>
    /// A sequence of segments of <paramref name="piece"/> bytes.
    /// </summary>
    private static ReadOnlySequence<byte> Split(byte[] data, int piece)
    {
      if (data.Length <= piece)
        return new ReadOnlySequence<byte>(data);
      Segment first = new Segment(new ReadOnlyMemory<byte>(data, 0, piece), null);
      Segment last = first;
      for (int start = piece; start < data.Length; start += piece)
        last = new Segment(new ReadOnlyMemory<byte>(data, start, Math.Min(piece, data.Length - start)), last);
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

    /// <summary>
    /// Memory that is not an array's for <see cref="System.Runtime.InteropServices.MemoryMarshal.TryGetArray"/> (as native memory).
    /// </summary>
    private sealed class OpaqueMemory : MemoryManager<byte>
    {
      private readonly byte[] _data;

      public OpaqueMemory(byte[] data) { _data = data; }

      public override Span<byte> GetSpan() { return _data; }

      public override MemoryHandle Pin(int elementIndex = 0) { throw new NotSupportedException(); }

      public override void Unpin() { }

      protected override void Dispose(bool disposing) { }
    }

    /// <summary>
    /// Gives spans of at most <c>piece</c> bytes, whatever is asked.
    /// </summary>
    private class PieceWriter : IBufferWriter<byte>
    {
      private readonly int _piece;
      private byte[] _buffer = new byte[16];
      private int _count;

      public PieceWriter(int piece) { _piece = piece; }

      public byte[] Written { get { return _buffer.AsSpan(0, _count).ToArray(); } }

      public void Advance(int count)
      {
        if (count < 0 || count > _piece || _count + count > _buffer.Length)
          throw new InvalidOperationException($"Advance({count})");
        _count += count;
      }

      public virtual Span<byte> GetSpan(int sizeHint = 0)
      {
        if (_buffer.Length < _count + _piece)
          Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _count + _piece));
        return new Span<byte>(_buffer, _count, _piece);
      }

      public Memory<byte> GetMemory(int sizeHint = 0)
      {
        GetSpan(sizeHint);
        return new Memory<byte>(_buffer, _count, _piece);
      }
    }

    private sealed class NestingWriter : PieceWriter
    {
      private readonly Action _nested;

      public NestingWriter(Action nested) : base(1 << 20) { _nested = nested; }

      public override Span<byte> GetSpan(int sizeHint = 0)
      {
        _nested();
        return base.GetSpan(sizeHint);
      }
    }

    #endregion
  }
}
