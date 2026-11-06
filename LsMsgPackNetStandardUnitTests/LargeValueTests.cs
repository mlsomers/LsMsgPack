using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
#if KEEPTRACK
using System.ComponentModel;
using ObjectDebugger;
#endif

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Values above what a .NET array can hold (the specification allows 4 GB), read from a stream that makes up its content instead of holding it.
  /// </summary>
  [TestClass]
  public class LargeValueTests
  {
    private const long ThreeGb = 3000000000;

    /// <summary>
    /// A header, content of any length that follows from the position (<see cref="ContentByte"/>) and a trailer: nothing of the content is in memory.
    /// </summary>
    private sealed class MadeUpStream : Stream
    {
      private readonly byte[] _head;
      private readonly byte[] _tail;
      private readonly long _content;
      private readonly bool _seeks;
      private readonly Func<long, byte> _contentByte;
      private long _position;

      public MadeUpStream(byte[] head, long content, byte[] tail, bool seeks = true, Func<long, byte> contentByte = null)
      {
        _head = head;
        _content = content;
        _tail = tail ?? new byte[0];
        _seeks = seeks;
        _contentByte = contentByte ?? ContentByte;
      }

      public override bool CanRead => true;
      public override bool CanSeek => _seeks;
      public override bool CanWrite => false;
      public override long Length => _seeks ? Total : throw new NotSupportedException();
      private long Total => _head.Length + _content + _tail.Length;

      public override long Position
      {
        get => _position; // the explorers need it, also from streams that do not seek
        set
        {
          if (!_seeks)
            throw new NotSupportedException();
          _position = value;
        }
      }

      public override long Seek(long offset, SeekOrigin origin)
      {
        if (!_seeks)
          throw new NotSupportedException();
        _position = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : Total + offset;
        return _position;
      }

      public override int Read(byte[] buffer, int offset, int count)
      {
        int read = 0;
        while (read < count && _position < Total)
        {
          if (_position < _head.Length)
            buffer[offset + read] = _head[_position];
          else if (_position < _head.Length + _content)
            buffer[offset + read] = _contentByte(_position - _head.Length);
          else
            buffer[offset + read] = _tail[_position - _head.Length - _content];
          read++;
          _position++;
        }
        return read;
      }

      public override void Flush() { }
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static byte ContentByte(long at)
    {
      return (byte)(at % 251);
    }

    private static byte[] Header(byte code, uint length, params byte[] after)
    {
      return new[] { code, (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length }.Concat(after).ToArray();
    }

    private static string ErrorOf(Action read)
    {
      try
      {
        read();
      }
      catch (Exception ex)
      {
        return ex.InnerException?.Message ?? ex.Message;
      }
      return null;
    }

#if !KEEPTRACK
    [TestMethod]
    public void TooLongForAnArrayIsAClearError()
    {
      foreach (long length in new[] { ThreeGb, 2147483600L })
      {
        string error = ErrorOf(() => MsgPackItem.Unpack(new MadeUpStream(Header(0xC6, (uint)length), length, null)));
        Assert.AreEqual($"The data claims a value of {length} bytes, longer than a .NET array can hold.", error);
      }
    }

    [TestMethod]
    public void AHeaderDoesNotAllocateWhatItClaims()
    {
      long before = GC.GetAllocatedBytesForCurrentThread();
      string error = ErrorOf(() => MsgPackItem.Unpack(new MadeUpStream(Header(0xC6, 2000000000), 0, null, false)));
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      Assert.AreEqual("Unexpected end of data.", error);
      Assert.IsLessThan(4L * 1024 * 1024, allocated, "the memory follows the data that arrives");
    }

    [TestMethod]
    public void ReadInStepsWhenTheLengthIsNotKnown()
    {
      const int length = 5 * 1024 * 1024 + 3; // over several steps
      MpBin bin = (MpBin)MsgPackItem.Unpack(new MadeUpStream(Header(0xC6, length), length, null, false));
      byte[] value = (byte[])bin.Value;
      Assert.HasCount(length, value);
      for (int t = 0; t < length; t += 4099)
        Assert.AreEqual(ContentByte(t), value[t]);
      Assert.AreEqual(ContentByte(length - 1), value[length - 1]);
    }

    [TestMethod]
    public void AChunkTooLongForAnArrayIsAClearError()
    {
      byte[] head = new byte[] { 0xC1, 0xC4 }.Concat(Header(0xC6, (uint)ThreeGb)).ToArray();
      string error = ErrorOf(() => MsgPackItem.Unpack(new MadeUpStream(head, ThreeGb, null), new MsgPackSettings() { ReadStreams = true }));
      Assert.AreEqual($"The data claims a value of {ThreeGb} bytes, longer than a .NET array can hold.", error);
    }
#endif

#if KEEPTRACK
    private const long Limit = 1000;

    private static MsgPackSettings Settings(long limit = Limit)
    {
      return new MsgPackSettings() { PreservePackages = true, ReadStreams = true, TruncateValuesLargerThan = limit };
    }

    [TestMethod]
    public void WithoutTheLimitTooLongIsAnErrorItem()
    {
      MsgPackItem item = MsgPackItem.Unpack(new MadeUpStream(Header(0xC6, (uint)ThreeGb), ThreeGb, null), new MsgPackSettings());
      MpError error = item as MpError;
      Assert.IsNotNull(error);
      StringAssert.Contains(error.ToString() + error.Value, "longer than a .NET array can hold");
    }

    [TestMethod]
    public void ABinKeepsItsEndsAndTheNextValueIsRead()
    {
      MsgPackSettings settings = Settings();
      Stream data = new MadeUpStream(Header(0xC6, (uint)ThreeGb), ThreeGb, new byte[] { 42 });
      long before = GC.GetAllocatedBytesForCurrentThread();
      MpRoot root = MsgPackItem.UnpackMultiple(data, settings);
      Assert.IsLessThan(1024L * 1024, GC.GetAllocatedBytesForCurrentThread() - before);

      Assert.AreEqual(2, root.Count);
      MpBin bin = (MpBin)root[0];
      Assert.IsTrue(bin.IsTruncated);
      Assert.AreEqual(ThreeGb, bin.OriginalLength);
      Assert.AreEqual(MsgPackTypeId.MpBin32, bin.TypeId);
      Assert.AreEqual(0L, bin.StoredOffset);
      Assert.AreEqual(5 + ThreeGb, bin.StoredLength);
      StringAssert.Contains(bin.ToString(), "3000000000 bytes");
      StringAssert.Contains(bin.ToString(), MsgPackVarLen.TooLargeText);

      byte[] kept = (byte[])bin.Value;
      Assert.HasCount(200, kept);
      for (int t = 0; t < 100; t++)
      {
        Assert.AreEqual(ContentByte(t), kept[t]);
        Assert.AreEqual(ContentByte(ThreeGb - 100 + t), kept[100 + t]);
      }

      Assert.AreEqual(42L, Convert.ToInt64(root[1].Value));
      Assert.AreEqual(5 + ThreeGb, root[1].StoredOffset);
      Assert.AreEqual(new SkippedRange(5 + 100, ThreeGb - 200), settings.SkippedRanges.Single());
    }

    [TestMethod]
    public void AStreamThatDoesNotSeekPassesTheMiddle()
    {
      const long length = 3 * 1024 * 1024;
      Stream data = new MadeUpStream(Header(0xC6, (uint)length), length, null, false);
      MpBin bin = (MpBin)MsgPackItem.Unpack(data, Settings());
      Assert.AreEqual(length, bin.OriginalLength);
      Assert.AreEqual(ContentByte(length - 1), ((byte[])bin.Value)[199]);
    }

    [TestMethod]
    public void AClaimBeyondTheDataIsTheSameErrorAsWithoutTheLimit()
    {
      MsgPackItem item = MsgPackItem.Unpack(new MemoryStream(Header(0xC6, (uint)ThreeGb, 1, 2, 3)), Settings());
      Assert.IsInstanceOfType<MpError>(item);
      StringAssert.Contains(item.ToString() + item.Value, "Unexpected end");
    }

    [TestMethod]
    public void AtTheLimitTheValueIsWhole()
    {
      byte[] bytes = new byte[] { 0xC5, 1000 >> 8, 1000 & 0xFF }.Concat(new byte[Limit]).ToArray();
      MsgPackSettings settings = Settings();
      MpBin bin = (MpBin)MsgPackItem.Unpack(new MemoryStream(bytes), settings);
      Assert.IsFalse(bin.IsTruncated);
      Assert.HasCount((int)Limit, (byte[])bin.Value);
      Assert.AreEqual(Limit, bin.OriginalLength);
      Assert.IsEmpty(settings.SkippedRanges);
      CollectionAssert.AreEqual(bytes, bin.ToBytes());
    }

    private static byte[] Utf8Run(string text, int count)
    {
      StringBuilder sb = new StringBuilder();
      for (int t = 0; t < count; t++)
        sb.Append(text);
      return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [TestMethod]
    public void AStrKeepsItsFirstAndLastCharacters()
    {
      const long length = 5000;
      MsgPackSettings settings = Settings();
      Stream data = new MadeUpStream(Header(0xDB, (uint)length), length, null, true, at => (byte)'a');
      MpString str = (MpString)MsgPackItem.Unpack(data, settings);

      Assert.IsTrue(str.IsTruncated);
      Assert.AreEqual(length, str.OriginalLength);
      Assert.AreEqual(MsgPackTypeId.MpStr16, str.TypeId);
      string a100 = new string('a', 100);
      Assert.AreEqual($"{a100} ... {MsgPackVarLen.TooLargeText} ... {a100}", str.Value);
      // The hex views show the bytes of those characters
      Assert.AreEqual(new SkippedRange(5 + 100, length - 200), settings.SkippedRanges.Single());
    }

    [TestMethod]
    public void AStrIsNotCutInsideACharacter()
    {
      byte[] content = Utf8Run("€😀", 1000); // 3 and 4 bytes, 1 and 2 UTF-16 units
      byte[] bytes = Header(0xDB, (uint)content.Length).Concat(content).ToArray();
      MsgPackSettings settings = Settings();
      MpString str = (MpString)MsgPackItem.Unpack(new MemoryStream(bytes), settings);

      string value = (string)str.Value;
      Assert.IsFalse(value.Contains('�'), value);
      string[] ends = value.Split(new[] { $" ... {MsgPackVarLen.TooLargeText} ... " }, StringSplitOptions.None);
      Assert.HasCount(2, ends);
      StringAssert.StartsWith(ends[0], "€😀€😀", StringComparison.Ordinal);
      StringAssert.EndsWith(ends[1], "€😀€😀", StringComparison.Ordinal);
      Assert.IsLessThanOrEqualTo(100, ends[0].Length);
      Assert.IsLessThanOrEqualTo(100, ends[1].Length);
      for (int t = 0; t < 2; t++) // whole surrogate pairs
        Assert.AreEqual(ends[t], Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(ends[t])));

      SkippedRange skipped = settings.SkippedRanges.Single();
      Assert.AreEqual(5 + Encoding.UTF8.GetByteCount(ends[0]), skipped.Offset);
      Assert.AreEqual(content.Length - Encoding.UTF8.GetByteCount(ends[0]) - Encoding.UTF8.GetByteCount(ends[1]), skipped.Length);
    }

    [TestMethod]
    public void ATextWithLinesStaysOnOneLineInItsItem()
    {
      byte[] content = Utf8Run("line\r\n", 1000);
      MpString str = (MpString)MsgPackItem.Unpack(new MemoryStream(Header(0xDB, (uint)content.Length).Concat(content).ToArray()), Settings());
      StringAssert.Contains(str.ToString(), MsgPackVarLen.TooLargeText);
      Assert.IsFalse(str.ToString().Contains('\n'));
      Assert.IsTrue(((string)str.Value).Contains('\n'), "the value itself is not changed");
    }

    [TestMethod]
    public void AnExtStaysAnExtWithItsTypeSpecifier()
    {
      foreach (sbyte type in new sbyte[] { 7, -1, 1, 4 }) // also a timestamp, a decimal and a sidecar: nothing reads a type from its ends
      {
        byte[] head = Header(0xC9, (uint)ThreeGb, (byte)type);
        MpExt ext = (MpExt)MsgPackItem.Unpack(new MadeUpStream(head, ThreeGb, null), Settings());
        Assert.AreEqual(typeof(MpExt), ext.GetType());
        Assert.AreEqual(type, ext.TypeSpecifier);
        Assert.AreEqual(MsgPackTypeId.MpExt32, ext.TypeId);
        Assert.AreEqual(ThreeGb, ext.OriginalLength);
        StringAssert.Contains(ext.ToString(), MsgPackVarLen.TooLargeText);
      }
    }

    [TestMethod]
    public void TheIndexOfAStreamIsReadWhole()
    {
      byte[] content = new byte[2000];
      MsgPackSettings settings = Settings();
      MsgPackItem item = MsgPackItem.Unpack(new MemoryStream(Header(0xC9, (uint)content.Length, 3).Concat(content).ToArray()), settings);
      Assert.IsInstanceOfType<MpStreamIndex>(item);
      Assert.IsEmpty(settings.SkippedRanges);
    }

    /// <summary>
    /// A bin or str of unknown length: 0xC1 + code, chunks, 0xC1 0xC0.
    /// </summary>
    private static byte[] Chunked(byte start, byte chunkCode, params byte[][] chunks)
    {
      List<byte> bytes = new List<byte>() { 0xC1, start };
      foreach (byte[] chunk in chunks)
      {
        bytes.AddRange(Header(chunkCode, (uint)chunk.Length));
        bytes.AddRange(chunk);
      }
      bytes.Add(0xC1);
      bytes.Add(0xC0);
      return bytes.ToArray();
    }

    private static byte[] Counting(int length, int from)
    {
      byte[] bytes = new byte[length];
      for (int t = 0; t < length; t++)
        bytes[t] = ContentByte(from + t);
      return bytes;
    }

    [TestMethod]
    public void ChunksAboveTheLimitKeepTheEndsOfTheWhole()
    {
      // 3 chunks that fit together, then small ones and a large one over the limit
      int[] sizes = { 300, 300, 300, 150, 60, 2000, 40, 50 };
      List<byte[]> chunks = new List<byte[]>();
      int total = 0;
      foreach (int size in sizes)
      {
        chunks.Add(Counting(size, total));
        total += size;
      }
      MsgPackSettings settings = Settings();
      byte[] bytes = Chunked(0xC4, 0xC6, chunks.ToArray());
      MpBin bin = (MpBin)MsgPackItem.Unpack(new MemoryStream(bytes), settings);

      Assert.IsTrue(bin.LengthUnknown);
      Assert.IsTrue(bin.IsTruncated);
      Assert.AreEqual((long)total, bin.OriginalLength);
      CollectionAssert.AreEqual(Counting(100, 0).Concat(Counting(100, total - 100)).ToArray(), (byte[])bin.Value);
      Assert.HasCount(sizes.Length, bin.Chunks);
      Assert.AreEqual(bytes.Length, bin.StoredLength);

      // Only the large chunk after the limit left something out
      MsgPackVarLen large = (MsgPackVarLen)bin.Chunks[5];
      Assert.IsTrue(large.IsTruncated);
      Assert.AreEqual(2000L, large.OriginalLength);
      Assert.IsFalse(((MsgPackVarLen)bin.Chunks[4]).IsTruncated);
      Assert.IsFalse(((MsgPackVarLen)bin.Chunks[6]).IsTruncated);
      Assert.AreEqual(1, settings.SkippedRanges.Count);
      Assert.AreEqual(1800L, settings.SkippedRanges[0].Length);
    }

    [TestMethod]
    public void ChunksBelowTheLimitStayWhole()
    {
      byte[] bytes = Chunked(0xC4, 0xC6, Counting(400, 0), Counting(600, 400));
      MpBin bin = (MpBin)MsgPackItem.Unpack(new MemoryStream(bytes), Settings());
      Assert.IsFalse(bin.IsTruncated);
      CollectionAssert.AreEqual(Counting(1000, 0), (byte[])bin.Value);
    }

    [TestMethod]
    public void ChunksOfAStrKeepTheEndsAsCharacters()
    {
      byte[] part = Utf8Run("é", 500);
      MpString str = (MpString)MsgPackItem.Unpack(new MemoryStream(Chunked(0xA0, 0xDB, part, part, part)), Settings());
      Assert.IsTrue(str.IsTruncated);
      Assert.AreEqual(3000L, str.OriginalLength);
      string e100 = new string('é', 100);
      Assert.AreEqual($"{e100} ... {MsgPackVarLen.TooLargeText} ... {e100}", str.Value);
      StringAssert.Contains(str.ToString(), MsgPackVarLen.TooLargeText);
    }

    [TestMethod]
    public void ACutOffChunkedValueKeepsWhatItHas()
    {
      byte[] bytes = Chunked(0xC4, 0xC6, Counting(1200, 0), Counting(300, 1200));
      Array.Resize(ref bytes, bytes.Length - 2); // no end
      MsgPackItem item = MsgPackItem.Unpack(new MemoryStream(bytes), Settings());
      Assert.IsInstanceOfType<MpError>(item);
      MpBin bin = ((MpError)item).PartialItem as MpBin;
      if (bin != null)
        Assert.AreEqual(1500L, bin.OriginalLength);
    }

    [TestMethod]
    public void WritingATruncatedValueIsRefused()
    {
      foreach (byte[] head in new[] { Header(0xC6, 5000), Header(0xDB, 5000), Header(0xC9, 5000, 7) })
      {
        MsgPackItem item = MsgPackItem.Unpack(new MadeUpStream(head, 5000, null), Settings());
        Assert.IsTrue(((MsgPackVarLen)item).IsTruncated);
        MsgPackException error = Assert.ThrowsExactly<MsgPackException>(() => item.ToBytes());
        StringAssert.Contains(error.Message, MsgPackVarLen.TooLargeText);
      }
    }

    [TestMethod]
    public void AnArrayWithATruncatedValueCannotBeWrittenEither()
    {
      byte[] bytes = new byte[] { 0x91 }.Concat(Header(0xC6, 5000)).Concat(new byte[5000]).ToArray();
      MsgPackItem item = MsgPackItem.Unpack(new MemoryStream(bytes), Settings());
      MsgPackException error = Assert.ThrowsExactly<MsgPackException>(() => item.ToBytes());
      StringAssert.Contains(error.Message, MsgPackVarLen.TooLargeText);
    }

    [TestMethod]
    public void NoValidationWarningsOnWhatIsNotThere()
    {
      MsgPackItem item = MsgPackItem.Unpack(new MadeUpStream(Header(0xC6, 5000), 5000, null), Settings());
      Assert.IsEmpty(MsgPackValidation.ValidateItem(item, 100));
    }

    [TestMethod]
    public void TheOriginalLengthIsInThePropertyGrid()
    {
      MsgPackItem bin = MsgPackItem.Unpack(new MadeUpStream(Header(0xC6, (uint)ThreeGb), ThreeGb, null), Settings());
      PropertyDescriptor original = TypeDescriptor.GetProperties(bin, new Attribute[] { BrowsableAttribute.Yes }).Cast<PropertyDescriptor>().Single(p => p.DisplayName == "Original length");
      Assert.AreEqual(ThreeGb, original.GetValue(bin));
      Assert.AreEqual("MetaData", original.Category);
      Assert.IsFalse(string.IsNullOrEmpty(original.Description));

      foreach (MsgPackItem shown in new MsgPackItem[] { new MpString(new MsgPackSettings()) { Value = "é" }, new MpExt(new MsgPackSettings()) { Value = new byte[3] } })
        Assert.IsTrue(TypeDescriptor.GetProperties(shown, new Attribute[] { BrowsableAttribute.Yes }).Cast<PropertyDescriptor>().Any(p => p.DisplayName == "Original length"), shown.GetType().Name);
      Assert.AreEqual(2L, new MpString(new MsgPackSettings()) { Value = "é" }.OriginalLength);

      foreach (MsgPackItem hidden in new MsgPackItem[] { new MpArray(new MsgPackSettings()), new MpMap(new MsgPackSettings()) })
        Assert.IsFalse(TypeDescriptor.GetProperties(hidden, new Attribute[] { BrowsableAttribute.Yes }).Cast<PropertyDescriptor>().Any(p => p.DisplayName == "Original length"), hidden.GetType().Name);
    }

    [TestMethod]
    public void TheObjectsShowThatItWasTruncated()
    {
      // { "Name": "x", "Blob": bin }
      byte[] bytes = new byte[] { 0x82, 0xA4, (byte)'N', (byte)'a', (byte)'m', (byte)'e', 0xA1, (byte)'x', 0xA4, (byte)'B', (byte)'l', (byte)'o', (byte)'b' }
        .Concat(Header(0xC6, 5000)).Concat(new byte[5000]).ToArray();
      MsgPackSettings settings = Settings();
      MpRoot root = MsgPackItem.UnpackMultiple(bytes, settings);
      RootObject objects = new RootObject();
      objects.Reconstruct(root, null, TrimmedData.FromBytes(bytes, settings.SkippedRanges));
      PrimitiveObject blob = objects.Members.Single(m => m.Name == "Blob");
      StringAssert.Contains(blob.ValueText, MsgPackVarLen.TooLargeText);
      StringAssert.Contains(blob.ValueText, "5000 bytes");
    }

    [TestMethod]
    public void TrimmedDataConverts()
    {
      byte[] data = Counting(1000, 0);
      TrimmedData trimmed = TrimmedData.FromBytes(data, new[] { new SkippedRange(600, 100), new SkippedRange(100, 200) });
      Assert.AreEqual(1000L, trimmed.Length);
      Assert.HasCount(700, trimmed.Bytes);
      Assert.AreEqual(100L, trimmed.Skipped[0].Offset, "in the order of the data");

      Assert.AreEqual(0, trimmed.ToIndex(0));
      Assert.AreEqual(99, trimmed.ToIndex(99));
      Assert.AreEqual(100, trimmed.ToIndex(100), "a skipped byte: the next that was kept");
      Assert.AreEqual(100, trimmed.ToIndex(299));
      Assert.AreEqual(100, trimmed.ToIndex(300));
      Assert.AreEqual(399, trimmed.ToIndex(599));
      Assert.AreEqual(400, trimmed.ToIndex(600));
      Assert.AreEqual(400, trimmed.ToIndex(700));
      Assert.AreEqual(699, trimmed.ToIndex(999));
      Assert.AreEqual(700, trimmed.ToIndex(1000));
      Assert.AreEqual(700, trimmed.ToIndex(5000));

      for (int offset = 0; offset < 1000; offset++)
      {
        bool kept = !(offset >= 100 && offset < 300 || offset >= 600 && offset < 700);
        Assert.AreEqual(kept ? data[offset] : -1, trimmed.At(offset), offset.ToString());
      }
      Assert.AreEqual(-1, trimmed.At(-1));
      Assert.AreEqual(-1, trimmed.At(1000));

      CollectionAssert.AreEqual(Counting(50, 300), trimmed.Copy(300, 50));
      Assert.IsNull(trimmed.Copy(90, 20), "crosses a part that was left out");
      Assert.IsNull(trimmed.Copy(900, 200), "after the end");
    }

    [TestMethod]
    public void TrimmedDataWithoutSkippedPartsIsTheSameArray()
    {
      byte[] data = Counting(100, 0);
      Assert.AreSame(data, TrimmedData.FromBytes(data, null).Bytes);
      Assert.AreSame(data, TrimmedData.FromBytes(data, new List<SkippedRange>()).Bytes);
    }

    [TestMethod]
    public void TrimmedDataMergesOverlaps()
    {
      TrimmedData trimmed = TrimmedData.FromBytes(Counting(1000, 0), new[] { new SkippedRange(100, 200), new SkippedRange(150, 100), new SkippedRange(300, 10), new SkippedRange(900, 5000) });
      Assert.HasCount(2, trimmed.Skipped);
      Assert.AreEqual(new SkippedRange(100, 210), trimmed.Skipped[0]);
      Assert.AreEqual(new SkippedRange(900, 100), trimmed.Skipped[1], "not beyond the data");
      Assert.HasCount(690, trimmed.Bytes);
    }

    [TestMethod]
    public void TrimmedDataKeepsAtMostTheMaximum()
    {
      // What does not fit becomes the last part left out, also when the file holds no truncated value
      TrimmedData plain = TrimmedData.Read(new MadeUpStream(new byte[0], ThreeGb, null), null, 1000);
      Assert.HasCount(1000, plain.Bytes);
      Assert.AreEqual(new SkippedRange(1000, ThreeGb - 1000), plain.Skipped.Single());
      Assert.AreEqual(ContentByte(999), plain.Bytes[999]);
      Assert.AreEqual(1000, plain.ToIndex(ThreeGb - 1));

      TrimmedData mixed = TrimmedData.Read(new MadeUpStream(new byte[0], ThreeGb, null), new[] { new SkippedRange(100, 200), new SkippedRange(2000, 10) }, 1000);
      Assert.HasCount(1000, mixed.Bytes);
      Assert.HasCount(2, mixed.Skipped);
      Assert.AreEqual(new SkippedRange(1200, ThreeGb - 1200), mixed.Skipped[1]);
      Assert.AreEqual(ContentByte(1199), mixed.Bytes[999]);
    }

    [TestMethod]
    public void TrimmedDataOfAHugeStream()
    {
      MsgPackSettings settings = Settings();
      Stream data = new MadeUpStream(Header(0xC6, (uint)ThreeGb), ThreeGb, new byte[] { 42 });
      MsgPackItem.UnpackMultiple(data, settings);
      TrimmedData trimmed = TrimmedData.Read(data, settings.SkippedRanges);
      Assert.AreEqual(5 + ThreeGb + 1, trimmed.Length);
      Assert.HasCount(5 + 200 + 1, trimmed.Bytes);
      Assert.AreEqual(0xC6, trimmed.At(0));
      Assert.AreEqual(ContentByte(99), trimmed.At(5 + 99));
      Assert.AreEqual(-1, trimmed.At(5 + 100));
      Assert.AreEqual(ContentByte(ThreeGb - 1), trimmed.At(5 + ThreeGb - 1));
      Assert.AreEqual(42, trimmed.At(5 + ThreeGb));
    }
#endif
  }
}
