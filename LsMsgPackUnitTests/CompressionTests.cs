using LsMsgPack;
using LsMsgPack.Compression;
using LsMsgPack.Http;
using LsMsgPack.Streaming;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// LZ4 compression of the body (MsgPackOptions.Compression, the package LsMsgPack.Lz4; maintainer's decisions 2026-11-21: MessagePack-CSharp's ext 98/99, the body only, the serializers without a package
  /// dependency). The bytes against MessagePack-CSharp: the interop project's CompressionInteropTests.
  /// </summary>
  public abstract class CompressionTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class CmpLine
    {
      public string Product { get; set; }
      public int Quantity { get; set; }
      public decimal Price { get; set; }
    }

    public class CmpOrder
    {
      public string Customer { get; set; }
      public List<CmpLine> Lines { get; set; }
      public byte[] Data { get; set; }
    }

    protected static CmpOrder Order(int lines)
    {
      CmpOrder order = new CmpOrder() { Customer = "Customer " + lines, Lines = new List<CmpLine>() };
      for (int t = 0; t < lines; t++)
        order.Lines.Add(new CmpLine() { Product = "Product " + (t % 17), Quantity = t % 5, Price = (t % 9) * 1.25m });
      return order;
    }

    protected static void AreEqual(CmpOrder expected, CmpOrder actual)
    {
      Assert.IsNotNull(actual);
      Assert.AreEqual(expected.Customer, actual.Customer);
      Assert.AreEqual(expected.Lines.Count, actual.Lines.Count);
      for (int t = 0; t < expected.Lines.Count; t++)
      {
        Assert.AreEqual(expected.Lines[t].Product, actual.Lines[t].Product);
        Assert.AreEqual(expected.Lines[t].Quantity, actual.Lines[t].Quantity);
        Assert.AreEqual(expected.Lines[t].Price, actual.Lines[t].Price);
      }
      CollectionAssert.AreEqual(expected.Data, actual.Data);
    }

    /// <param name="mode">0 without the schema, 1 the schema inline, 2 a reference to it</param>
    protected static MsgPackSettings Settings(MsgPackCompression compression, int mode, SchemaStore store = null, EndianAction endian = EndianAction.SwapIfCurrentSystemIsLittleEndian)
    {
      return new MsgPackSettings()
      {
        Compression = compression,
        UseInexedSchema = mode != 0,
        WriteSchemaReference = mode == 2,
        SchemaStore = mode == 2 ? store ?? new SchemaStore() : null,
        EndianAction = endian,
      };
    }

    private static bool KeepTrack
    {
      get { return typeof(MsgPackSettings).GetProperty("PreservePackages") != null; }
    }

    /// <summary>
    /// The data is refused: a MsgPackException with the message (LsMsgPackDebug under KEEPTRACK: the error is an MpError item, which the serializer cannot convert).
    /// </summary>
    private void Refused(byte[] bytes, MsgPackSettings settings, string message)
    {
      if (KeepTrack && Serializer is DebugSerializer)
      {
        Assert.Throws<Exception>(() => Serializer.Deserialize<CmpOrder>(bytes, settings));
        return;
      }
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<CmpOrder>(bytes, settings));
      StringAssert.Contains(ex.Message, message);
    }

    private static readonly MsgPackCompression[] Layouts = new MsgPackCompression[] { Lz4Compression.Block, Lz4Compression.BlockArray };

    /// <summary>
    /// Where the body starts: where the payload differs from the one written without compression (the head is the same).
    /// </summary>
    private static int BodyStart(byte[] plain, byte[] compressed)
    {
      int t = 0;
      while (t < plain.Length && t < compressed.Length && plain[t] == compressed[t])
        t++;
      return t;
    }

    [TestMethod]
    public void EveryLayoutAndSchemaMode_RoundTrip()
    {
      CmpOrder order = Order(200);
      foreach (MsgPackCompression compression in Layouts)
      {
        for (int mode = 0; mode < 3; mode++)
        {
          SchemaStore store = new SchemaStore();
          MsgPackSettings plainSettings = Settings(null, mode, store);
          MsgPackSettings settings = Settings(compression, mode, store);
          byte[] plain = Serializer.Serialize(order, plainSettings);
          byte[] bytes = Serializer.Serialize(order, settings);
          string what = $"{compression.Layout}, mode {mode}";
          Assert.IsLessThan(plain.Length / 3, bytes.Length, what);

          int body = BodyStart(plain, bytes);
          Assert.IsTrue(CompressedBody.StartsCompressed(new ReadOnlySpan<byte>(bytes, body, bytes.Length - body)), what);
          if (compression.Layout == MsgPackCompressionLayout.Block)
            Assert.AreEqual((sbyte)99, (sbyte)bytes[body + (bytes[body] == 0xC7 ? 2 : bytes[body] == 0xC8 ? 3 : 5)], what);
          else
            Assert.AreEqual(0x92, bytes[body], what); // [ext 98, one block]

          AreEqual(order, Serializer.Deserialize<CmpOrder>(bytes, settings));
          AreEqual(order, Serializer.Deserialize<CmpOrder>(new MemoryStream(bytes), settings));
          AreEqual(order, (CmpOrder)Serializer.Deserialize(typeof(CmpOrder), bytes, settings, out ReadDifferences differences));
          Assert.IsNull(differences);
        }
      }
    }

    /// <summary>
    /// LsMsgPack and the generated code write what LsMsgPackDebug writes, in every byte order.
    /// </summary>
    [TestMethod]
    public void SameBytesAsDebug()
    {
      CmpOrder order = Order(100);
      foreach (MsgPackCompression compression in Layouts.Concat(new[] { new Lz4Compression(MsgPackCompressionLayout.BlockArray, blockSize: 1024) }))
      {
        foreach (EndianAction endian in new[] { EndianAction.SwapIfCurrentSystemIsLittleEndian, EndianAction.NeverSwap })
        {
          for (int mode = 0; mode < 3; mode++)
          {
            SchemaStore store = new SchemaStore();
            byte[] debug = Serializers.Debug.Serialize(order, Settings(compression, mode, store, endian));
            byte[] bytes = Serializer.Serialize(order, Settings(compression, mode, store, endian));
            CollectionAssert.AreEqual(debug, bytes, $"{compression.Layout}, {endian}, mode {mode}");
            AreEqual(order, Serializer.Deserialize<CmpOrder>(debug, Settings(compression, mode, store, endian)));
          }
        }
      }
    }

    [TestMethod]
    public void ShortBody_WrittenAsItIs()
    {
      CmpOrder order = Order(1);
      for (int mode = 0; mode < 3; mode++)
      {
        SchemaStore store = new SchemaStore();
        byte[] plain = Serializer.Serialize(order, Settings(null, mode, store));
        byte[] bytes = Serializer.Serialize(order, Settings(Lz4Compression.Block, mode, store));
        CollectionAssert.AreEqual(plain, bytes, $"mode {mode}");
        AreEqual(order, Serializer.Deserialize<CmpOrder>(bytes, Settings(Lz4Compression.Block, mode, store)));
      }
      Assert.IsNull(Serializer.Deserialize<CmpOrder>(Serializer.Serialize<CmpOrder>(null, Settings(Lz4Compression.Block, 1)), Settings(Lz4Compression.Block, 1)));
    }

    /// <summary>
    /// A body compression would not make shorter (random bytes) is written as it is.
    /// </summary>
    [TestMethod]
    public void Incompressible_WrittenAsItIs()
    {
      byte[] random = new byte[5000];
      new Random(5).NextBytes(random);
      CmpOrder order = new CmpOrder() { Customer = "x", Lines = new List<CmpLine>(), Data = random };
      foreach (MsgPackCompression compression in Layouts)
      {
        byte[] plain = Serializer.Serialize(order, Settings(null, 0));
        byte[] bytes = Serializer.Serialize(order, Settings(compression, 0));
        CollectionAssert.AreEqual(plain, bytes, compression.Layout.ToString());
        AreEqual(order, Serializer.Deserialize<CmpOrder>(bytes, Settings(compression, 0)));
      }
    }

    /// <summary>
    /// A reader with the option takes either layout and uncompressed bodies, whatever its own layout says.
    /// </summary>
    [TestMethod]
    public void Readers_TakeBothLayoutsAndUncompressed()
    {
      CmpOrder order = Order(300);
      byte[] block = Serializer.Serialize(order, Settings(Lz4Compression.Block, 1));
      byte[] array = Serializer.Serialize(order, Settings(Lz4Compression.BlockArray, 1));
      byte[] plain = Serializer.Serialize(order, Settings(null, 1));
      foreach (MsgPackCompression reader in Layouts)
      {
        AreEqual(order, Serializer.Deserialize<CmpOrder>(block, Settings(reader, 1)));
        AreEqual(order, Serializer.Deserialize<CmpOrder>(array, Settings(reader, 1)));
        AreEqual(order, Serializer.Deserialize<CmpOrder>(plain, Settings(reader, 1)));
      }
    }

    /// <summary>
    /// Without the option a compressed body is an extension value (as MessagePack-CSharp's readers without compression): not an order.
    /// </summary>
    [TestMethod]
    public void WithoutTheOption_NotDecompressed()
    {
      byte[] bytes = Serializer.Serialize(Order(200), Settings(Lz4Compression.Block, 0));
      Assert.Throws<Exception>(() => Serializer.Deserialize<CmpOrder>(bytes, Settings(null, 0)));
    }

    /// <summary>
    /// Bodies of several blocks: the default 64 KiB blocks and small ones (each block compressed on its own).
    /// </summary>
    [TestMethod]
    public void ManyBlocks_RoundTrip()
    {
      CmpOrder order = Order(8000); // about 150 KB
      foreach (MsgPackCompression compression in new[] { Lz4Compression.BlockArray, new Lz4Compression(MsgPackCompressionLayout.BlockArray, blockSize: 1024) })
      {
        for (int mode = 0; mode < 3; mode++)
        {
          SchemaStore store = new SchemaStore();
          byte[] plain = Serializer.Serialize(order, Settings(null, mode, store));
          byte[] bytes = Serializer.Serialize(order, Settings(compression, mode, store));
          int body = BodyStart(plain, bytes);
          int blocks = (plain.Length - body + compression.BlockSize - 1) / compression.BlockSize;
          Assert.IsGreaterThan(1, blocks);
          int count = bytes[body] == 0xDC ? (bytes[body + 1] << 8) | bytes[body + 2] : bytes[body] & 0x0F;
          Assert.AreEqual(blocks + 1, count, $"block size {compression.BlockSize}, mode {mode}");
          AreEqual(order, Serializer.Deserialize<CmpOrder>(bytes, Settings(Lz4Compression.Block, mode, store)));
        }
      }
    }

    [TestMethod]
    public void HighCompressionLevel_ReadByAnyReader()
    {
      CmpOrder order = Order(2000);
      Lz4Compression high = new Lz4Compression(MsgPackCompressionLayout.Block, K4os.Compression.LZ4.LZ4Level.L09_HC);
      byte[] fast = Serializer.Serialize(order, Settings(Lz4Compression.Block, 1));
      byte[] bytes = Serializer.Serialize(order, Settings(high, 1));
      Assert.IsLessThanOrEqualTo(fast.Length, bytes.Length);
      AreEqual(order, Serializer.Deserialize<CmpOrder>(bytes, Settings(Lz4Compression.Block, 1)));
    }

    [TestMethod]
    public void MaxDecompressedLength_Refused()
    {
      CmpOrder order = Order(500);
      foreach (MsgPackCompression compression in Layouts)
      {
        for (int mode = 0; mode < 2; mode++)
        {
          byte[] plain = Serializer.Serialize(order, Settings(null, mode));
          byte[] bytes = Serializer.Serialize(order, Settings(compression, mode));
          int body = plain.Length - BodyStart(plain, bytes);
          MsgPackSettings settings = Settings(compression, mode);
          settings.MaxDecompressedLength = body;
          AreEqual(order, Serializer.Deserialize<CmpOrder>(bytes, settings));
          settings = Settings(compression, mode);
          settings.MaxDecompressedLength = body - 1;
          Refused(bytes, settings, "MaxDecompressedLength");
        }
      }
    }

    /// <summary>
    /// The ext 99 payload of <paramref name="content"/> (an ext 32 header).
    /// </summary>
    private static byte[] Ext99(byte[] content)
    {
      byte[] bytes = new byte[6 + content.Length];
      bytes[0] = 0xC9;
      bytes[1] = (byte)(content.Length >> 24);
      bytes[2] = (byte)(content.Length >> 16);
      bytes[3] = (byte)(content.Length >> 8);
      bytes[4] = (byte)content.Length;
      bytes[5] = 99;
      content.CopyTo(bytes, 6);
      return bytes;
    }

    private static byte[] Int32Then(int value, byte[] rest)
    {
      return new byte[] { 0xD2, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value }.Concat(rest).ToArray();
    }

    private static byte[] Lz4(byte[] data)
    {
      byte[] target = new byte[Lz4Compression.Block.MaximumCompressedLength(data.Length)];
      int length = Lz4Compression.Block.Compress(data, target);
      return target.Take(length).ToArray();
    }

    /// <summary>
    /// Data from outside: lengths that LZ4 cannot produce from the block are refused before anything is allocated, a block that does not decompress to the claimed length or is not LZ4 is refused.
    /// </summary>
    [TestMethod]
    public void InvalidCompressedBodies_Refused()
    {
      MsgPackSettings settings = Settings(Lz4Compression.Block, 0);
      byte[] body = Serializer.Serialize(Order(100), Settings(null, 0));
      byte[] block = Lz4(body);
      AreEqual(Order(100), Serializer.Deserialize<CmpOrder>(Ext99(Int32Then(body.Length, block)), settings)); // the crafted form is valid

      Refused(Ext99(Int32Then(1000000000, block)), settings, "more than LZ4 can produce");
      Refused(Ext99(Int32Then(body.Length + 1, block)), settings, "not valid");
      Refused(Ext99(Int32Then(body.Length - 1, block)), settings, "not valid");
      Refused(Ext99(Int32Then(body.Length, block.Take(block.Length / 2).ToArray())), settings, "not valid");
      Refused(Ext99(Int32Then(-5, block)), settings, "negative");
      Refused(Ext99(new byte[] { 0xA1, 0x41 }.Concat(block).ToArray()), settings, "no integer");

      // An array of blocks: more blocks than lengths, a length LZ4 cannot produce, a block that is no bin
      byte[] lengths = new byte[] { 0xCD, (byte)(body.Length >> 8), (byte)body.Length };
      byte[] array = new byte[] { 0x92, 0xC7, (byte)lengths.Length, 98 }.Concat(lengths).Concat(new byte[] { 0xC5, (byte)(block.Length >> 8), (byte)block.Length }).Concat(block).ToArray();
      AreEqual(Order(100), Serializer.Deserialize<CmpOrder>(array, settings)); // valid, with a bin 16
      byte[] twoBlocks = new byte[] { 0x93 }.Concat(array.Skip(1)).Concat(new byte[] { 0xC4, 0 }).ToArray();
      Refused(twoBlocks, settings, "not valid");
      byte[] huge = new byte[] { 0x92, 0xC7, 5, 98, 0xCE, 0x7F, 0xFF, 0xFF, 0xFF, 0xC4, 3, 1, 2, 3 }; // 2 GB claimed by 3 bytes: refused before the body is allocated
      Refused(huge, settings, "more than LZ4 can produce");
    }

    /// <summary>
    /// The highest ratio LZ4 reaches (long runs of zeros) is within the check of what a block can produce.
    /// </summary>
    [TestMethod]
    public void Zeros_WithinTheExpansionCheck()
    {
      foreach (int length in new[] { 64, 1000, 65536, 1 << 20, 10 << 20 })
      {
        CmpOrder order = new CmpOrder() { Customer = "", Lines = new List<CmpLine>(), Data = new byte[length] };
        foreach (MsgPackCompression compression in Layouts)
        {
          byte[] bytes = Serializer.Serialize(order, Settings(compression, 0));
          Assert.IsLessThan(length / 100 + 100, bytes.Length);
          AreEqual(order, Serializer.Deserialize<CmpOrder>(bytes, Settings(compression, 0)));
        }
      }
    }
  }

  [TestClass]
  public class DebugCompressionTests : CompressionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsCompressionTests : CompressionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }

    private static LsMsgPackSerializer Ls(MsgPackCompression compression, bool schema = true, bool references = false)
    {
      return new LsMsgPackSerializer(new LsMsgPackOptions() { Compression = compression, UseInexedSchema = schema, WriteSchemaReference = references, SchemaStore = references ? new SchemaStore() : null });
    }

    /// <summary>
    /// The stream overloads, buffer writers and sequences of several segments, async calls.
    /// </summary>
    [TestMethod]
    public void OtherOverloads_RoundTrip()
    {
      CmpOrder order = Order(400);
      foreach (MsgPackCompression compression in new[] { Lz4Compression.Block, Lz4Compression.BlockArray })
      {
        LsMsgPackSerializer ls = Ls(compression);
        byte[] bytes = ls.Serialize(order);

        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
        ls.Serialize(order, buffer);
        CollectionAssert.AreEqual(bytes, buffer.WrittenSpan.ToArray());

        MemoryStream ms = new MemoryStream();
        ls.SerializeAsync(order, ms).GetAwaiter().GetResult();
        CollectionAssert.AreEqual(bytes, ms.ToArray());

        AreEqual(order, ls.Deserialize<CmpOrder>(new ReadOnlyMemory<byte>(bytes)));
        AreEqual(order, ls.Deserialize<CmpOrder>(Segments(bytes, 7)));
        AreEqual(order, ls.Deserialize<CmpOrder>(new NonSeekingStream(bytes)));
        AreEqual(order, ls.DeserializeAsync<CmpOrder>(new NonSeekingStream(bytes)).GetAwaiter().GetResult());
      }
    }

    /// <summary>
    /// Values one after the other in a stream: each body compressed on its own, read back from where the previous one ended.
    /// </summary>
    [TestMethod]
    public void StreamWriterAndReader_EachValueCompressed()
    {
      foreach ((bool schema, MsgPackStreamSchemas schemas) in new[] { (false, MsgPackStreamSchemas.PerObject), (true, MsgPackStreamSchemas.PerObject), (true, MsgPackStreamSchemas.PerStream) })
      {
        LsMsgPackSerializer ls = Ls(Lz4Compression.Block, schema, schemas == MsgPackStreamSchemas.PerStream);
        MemoryStream ms = new MemoryStream();
        using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, ls, schemas, true))
        {
          for (int n = 0; n < 5; n++)
            writer.WriteObject(Order(50 + n * 40));
        }
        byte[] bytes = ms.ToArray();
        int plain = 0;
        for (int n = 0; n < 5; n++)
          plain += Ls(null, schema).Serialize(Order(50 + n * 40)).Length; // the schema inline each time: PerStream writes it once
        Assert.IsLessThan(plain / 3, bytes.Length);

        MsgPackStreamReader reader = new MsgPackStreamReader(new NonSeekingStream(bytes), ls);
        for (int n = 0; n < 5; n++)
          AreEqual(Order(50 + n * 40), reader.ReadObject<CmpOrder>());
        Assert.IsTrue(reader.AtEnd);

        if (schemas == MsgPackStreamSchemas.PerStream)
          continue; // the later values refer to the schema the stream reader registered
        // Deserialize of a stream leaves it after each payload
        NonSeekingStream stream = new NonSeekingStream(bytes);
        for (int n = 0; n < 5; n++)
          AreEqual(Order(50 + n * 40), ls.Deserialize<CmpOrder>(stream));
      }
    }

    /// <summary>
    /// The HTTP serializer limits what a compressed request body may become (64 MiB) when compression is on.
    /// </summary>
    [TestMethod]
    public void HttpSerializer_LimitsDecompressedLength()
    {
      LsMsgPackHttpOptions options = new LsMsgPackHttpOptions();
      Assert.AreEqual(64 * 1024 * 1024, options.MaxDecompressedLength);
      Assert.AreEqual(64 * 1024 * 1024, new LsMsgPackHttpSerializer(options).For(MsgPackMediaTypes.XLsMsgPack).Options.MaxDecompressedLength);
    }

    private static ReadOnlySequence<byte> Segments(byte[] bytes, int size)
    {
      Segment first = null, last = null;
      for (int at = 0; at < bytes.Length; at += size)
      {
        Segment segment = new Segment(new ReadOnlyMemory<byte>(bytes, at, Math.Min(size, bytes.Length - at)), at);
        if (first is null)
          first = segment;
        else
          last.Next = segment;
        last = segment;
      }
      return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
      internal Segment(ReadOnlyMemory<byte> memory, long index)
      {
        Memory = memory;
        RunningIndex = index;
      }

      internal new Segment Next { set { base.Next = value; } }
    }

    /// <summary>
    /// A network stream: no seeking, at most 100 bytes per read.
    /// </summary>
    private sealed class NonSeekingStream : Stream
    {
      private readonly byte[] _data;
      private int _pos;

      internal NonSeekingStream(byte[] data) { _data = data; }

      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return false; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { throw new NotSupportedException(); } }
      public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override void Flush() { }
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

      public override int Read(byte[] buffer, int offset, int count)
      {
        int n = Math.Min(Math.Min(count, 100), _data.Length - _pos);
        Array.Copy(_data, _pos, buffer, offset, n);
        _pos += n;
        return n;
      }
    }
  }

  [TestClass]
  public class GenCompressionTests : CompressionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
