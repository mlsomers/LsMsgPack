using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.Streaming;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SwOrder = LsMsgPackUnitTests.StreamWriterReaderTests.SwOrder;
using SwLine = LsMsgPackUnitTests.StreamWriterReaderTests.SwLine;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// LsMsgPack's item reader with MsgPackSettings.ReadStreams (what the explorers show): values of unknown length (the 0xC1 escapes of msgpack/msgpack#347), the index of a stream, its tail and sidecars.
  /// </summary>
  [TestClass]
  public class StreamItemTests
  {
    private static MsgPackSettings Streams()
    {
#if KEEPTRACK
      return new MsgPackSettings { ReadStreams = true, DynamicallyCompact = false, PreservePackages = true };
#else
      return new MsgPackSettings { ReadStreams = true, DynamicallyCompact = false };
#endif
    }

    private static byte[] Hex(string hex)
    {
      hex = hex.Replace(" ", "");
      byte[] bytes = new byte[hex.Length / 2];
      for (int t = 0; t < bytes.Length; t++)
        bytes[t] = Convert.ToByte(hex.Substring(t * 2, 2), 16);
      return bytes;
    }

    private static MsgPackItem Unpack(byte[] bytes, MsgPackSettings settings = null)
    {
      return MsgPackItem.Unpack(new MemoryStream(bytes), settings ?? Streams());
    }

    [TestMethod]
    public void ArrayOfUnknownLength()
    {
      MsgPackItem item = Unpack(Hex("c1 90 01 a1 61 91 c3 c1 c0"));
      MpArray array = (MpArray)item;
      object[] values = (object[])array.Value;
      Assert.HasCount(3, values);
      Assert.AreEqual(1, Convert.ToInt32(values[0]));
      Assert.AreEqual("a", values[1]);
      Assert.IsTrue((bool)((object[])values[2])[0]);
#if KEEPTRACK
      Assert.IsTrue(array.LengthUnknown);
      Assert.AreEqual(0L, array.StoredOffset);
      Assert.AreEqual(9L, array.StoredLength); // the end (0xC1 0xC0) included
      Assert.HasCount(3, array.PackedValues);
      Assert.AreEqual(5L, array.PackedValues[2].StoredOffset);
      Assert.IsFalse(((MpArray)array.PackedValues[2]).LengthUnknown);
      StringAssert.Contains(array.ToString(), "unknown length");
#endif
    }

    [TestMethod]
    public void MapsAndNesting()
    {
      // {"rows": [1, 2], "total": 2} with the rows of unknown length, inside a map of unknown length
      MpMap outer = (MpMap)Unpack(Hex("c1 80 a1 78 82 a4 72 6f 77 73 c1 90 01 02 c1 c0 a5 74 6f 74 61 6c 02 c1 c0"));
      KeyValuePair<object, object>[] entries = (KeyValuePair<object, object>[])outer.Value;
      Assert.HasCount(1, entries);
      Assert.AreEqual("x", entries[0].Key);
      KeyValuePair<object, object>[] inner = (KeyValuePair<object, object>[])entries[0].Value;
      Assert.AreEqual("rows", inner[0].Key);
      CollectionAssert.AreEqual(new object[] { (byte)1, (byte)2 }, ((object[])inner[0].Value).Select(v => (object)Convert.ToByte(v)).ToArray());
      Assert.AreEqual("total", inner[1].Key);
#if KEEPTRACK
      Assert.IsTrue(outer.LengthUnknown);
      MpMap known = (MpMap)outer.PackedValues[0].Value;
      Assert.IsFalse(known.LengthUnknown);
      MpArray rows = (MpArray)known.PackedValues[0].Value;
      Assert.IsTrue(rows.LengthUnknown);
      Assert.AreEqual(10L, rows.StoredOffset);
      Assert.AreEqual(6L, rows.StoredLength);
#endif
    }

    [TestMethod]
    public void StringsAndBinaryInChunks()
    {
      MpString abc = (MpString)Unpack(Hex("c1 a0 a2 61 62 a1 63 c1 c0"));
      Assert.AreEqual("abc", abc.Value);

      // "é" (c3 a9) split over two chunks: the chunks are joined before decoding
      MpString split = (MpString)Unpack(Hex("c1 a0 a1 c3 d9 01 a9 c1 c0"));
      Assert.AreEqual("é", split.Value);

      MpBin bin = (MpBin)Unpack(Hex("c1 c4 c4 02 01 02 c4 00 c4 01 03 c1 c0"));
      CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])bin.Value);
#if KEEPTRACK
      Assert.IsTrue(split.LengthUnknown);
      Assert.HasCount(2, split.Chunks);
      Assert.AreEqual(2L, split.Chunks[0].StoredOffset);
      Assert.AreEqual(2L, split.Chunks[0].StoredLength);
      Assert.AreEqual(4L, split.Chunks[1].StoredOffset);
      Assert.AreEqual(3L, split.Chunks[1].StoredLength);
      Assert.HasCount(3, bin.Chunks);
      Assert.IsNull(((MpString)Unpack(Hex("a1 61"))).Chunks);
#endif
    }

    /// <summary>
    /// An escape that does not fit where it is: under KEEPTRACK an error item (the explorers show the data around it), otherwise an exception.
    /// </summary>
    [TestMethod]
    [DataRow("c1 c0", "is not inside an array, map, str or bin of unknown length")]
    [DataRow("92 01 c1 c0", "is not inside an array, map, str or bin of unknown length")]
    [DataRow("c1 05", "0xC1 0x05 is reserved")]
    [DataRow("c1", "ends with the escape byte")]
    [DataRow("c1 80 a1 61 c1 c0", "follows a key of a map, where its value belongs")]
    [DataRow("c1 90 01", "has no end (0xC1 0xC0)")]
    [DataRow("c1 a0 a1 61 01", "Only str chunks may follow")]
    [DataRow("c1 c4 a1 61 c1 c0", "Only bin chunks may follow")]
    [DataRow("c1 a0 c1 90 c1 c0", "Only str chunks may follow the start of a str of unknown length, not the escape sequence 0xC1 0x90")]
    [DataRow("c1 a0 a1 61", "the str of unknown length has no end")]
    public void MisplacedEscapesAreErrors(string hex, string expected)
    {
      byte[] bytes = Hex(hex);
#if KEEPTRACK
      MsgPackItem item = Unpack(bytes);
      StringAssert.Contains(Errors(item), expected);
#else
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Unpack(bytes));
      StringAssert.Contains(Messages(ex), expected);
#endif
    }

    private static string Messages(Exception ex)
    {
      StringBuilder sb = new StringBuilder();
      for (Exception e = ex; e != null; e = e.InnerException)
        sb.AppendLine(e.Message);
      return sb.ToString();
    }

#if KEEPTRACK
    /// <summary>
    /// The messages of the error items in the tree.
    /// </summary>
    private static string Errors(MsgPackItem item)
    {
      StringBuilder sb = new StringBuilder();
      CollectErrors(item, sb);
      return sb.ToString();
    }

    private static void CollectErrors(MsgPackItem item, StringBuilder sb)
    {
      switch (item)
      {
        case null: return;
        case MpError error:
          sb.AppendLine(Messages((Exception)error.Value));
          CollectErrors(error.PartialItem, sb);
          return;
        case MpRoot root:
          foreach (MsgPackItem child in root)
            CollectErrors(child, sb);
          return;
        case MpArray array:
          foreach (MsgPackItem child in array.PackedValues)
            CollectErrors(child, sb);
          return;
        case MpMap map:
          foreach (KeyValuePair<MsgPackItem, MsgPackItem> pair in map.PackedValues)
          {
            CollectErrors(pair.Key, sb);
            CollectErrors(pair.Value, sb);
          }
          return;
      }
    }
#endif

    [TestMethod]
    public void WithoutTheSettingEscapesAreRefused()
    {
      MsgPackSettings settings = new MsgPackSettings();
      Assert.IsFalse(settings.ReadStreams);
      byte[] bytes = Hex("c1 90 01 c1 c0");
#if KEEPTRACK
      StringAssert.Contains(Errors(MsgPackItem.Unpack(new MemoryStream(bytes), settings)), "set MsgPackSettings.ReadStreams");
#else
      MsgPackException ex = Assert.Throws<MsgPackException>(() => MsgPackItem.Unpack(new MemoryStream(bytes), settings));
      StringAssert.Contains(Messages(ex), "set MsgPackSettings.ReadStreams");
#endif
      Assert.IsTrue(settings.Clone() is MsgPackSettings);
      Assert.IsTrue(new MsgPackSettings { ReadStreams = true }.Clone().ReadStreams);
    }

    private static LsMsgPackSerializer Plain()
    {
      return new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false });
    }

    private static SwOrder Order(int n)
    {
      SwOrder order = new SwOrder { Customer = "Customer " + n, Date = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n) };
      for (int t = 0; t < n % 4; t++)
        order.Lines.Add(new SwLine { Product = "P" + t, Price = 1.25m * t, Quantity = t });
      return order;
    }

    private static byte[] Write(Action<MsgPackStreamWriter> write, MsgPackStreamIndexOptions index, LsMsgPackSerializer serializer = null, MsgPackStreamSchemas schemas = MsgPackStreamSchemas.PerObject)
    {
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer ?? Plain(), schemas, true, index))
        write(writer);
      return ms.ToArray();
    }

    /// <summary>
    /// LsMsgPack's Deserialize reads values of unknown length too with the setting.
    /// </summary>
    [TestMethod]
    public void DeserializeReadsValuesOfUnknownLength()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteArrayStart();
        for (int n = 1; n <= 3; n++)
          w.WriteObject(Order(n));
        w.WriteEnd();
      }, null);
      Assert.AreEqual(0xC1, bytes[0]);

      MsgPackSettings settings = new MsgPackSettings { UseInexedSchema = false, ReadStreams = true };
      List<SwOrder> orders = MsgPackSerializer.Deserialize<List<SwOrder>>(bytes, settings);
      Assert.HasCount(3, orders);
      for (int n = 1; n <= 3; n++)
      {
        Assert.AreEqual(Order(n).Customer, orders[n - 1].Customer);
        Assert.HasCount(Order(n).Lines.Count, orders[n - 1].Lines);
      }
    }

    private sealed class SidecarObserver : IMsgPackStreamObserver
    {
      private int _entries;
      public void Append(ReadOnlySpan<byte> data) { }
      public ReadOnlyMemory<byte> EntryEnded(MsgPackStreamEntry entry)
      {
        _entries++;
        return _entries % 2 == 0 ? new byte[] { 0xDE, 0xAD, (byte)_entries } : ReadOnlyMemory<byte>.Empty;
      }
    }

    private static List<MsgPackStreamIndex> LsIndexes(byte[] bytes)
    {
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(bytes), Plain()))
        return reader.ReadIndexes().ToList();
    }

    private static void AreEqual(MsgPackStreamIndex expected, MsgPackStreamIndex actual)
    {
      Assert.AreEqual(expected.Offset, actual.Offset);
      Assert.AreEqual(expected.Length, actual.Length);
      Assert.AreEqual(expected.Status, actual.Status);
      Assert.AreEqual(expected.Writer, actual.Writer);
      Assert.AreEqual(expected.PreviousTail, actual.PreviousTail);
      Assert.AreEqual(expected.HashAlgorithm, actual.HashAlgorithm);
      Assert.HasCount(expected.Entries.Count, actual.Entries);
      for (int t = 0; t < expected.Entries.Count; t++)
      {
        MsgPackStreamEntry e = expected.Entries[t];
        MsgPackStreamEntry a = actual.Entries[t];
        Assert.AreEqual(e.Offset, a.Offset);
        Assert.AreEqual(e.Length, a.Length);
        Assert.AreEqual(e.Identifier, a.Identifier);
        Assert.AreEqual(e.ContentType, a.ContentType);
        Assert.AreEqual(e.ElementCount, a.ElementCount);
        Assert.AreEqual(e.SidecarOffset, a.SidecarOffset);
        Assert.AreEqual(e.SidecarLength, a.SidecarLength);
        Assert.AreEqual(e.Incomplete, a.Incomplete);
        Assert.AreEqual(e.Points?.Count, a.Points?.Count);
      }
    }

    private static Action<MsgPackStreamWriter> Entries(int count, bool unknownLength = true)
    {
      return w =>
      {
        for (int n = 1; n <= count; n++)
        {
          w.BeginEntry(n % 2 == 0 ? (object)("orders " + n) : n, "application/x-orders");
          if (unknownLength)
            w.WriteArrayStart();
          else
            w.WriteArrayHeader(5);
          for (int r = 0; r < 5; r++)
            w.WriteObject(Order(n + r));
          if (unknownLength)
            w.WriteEnd();
        }
      };
    }

    [TestMethod]
    public void TheIndexItsTailAndTheSidecars()
    {
      byte[] bytes = Write(Entries(5), new MsgPackStreamIndexOptions { CheckpointInterval = 2, PointInterval = 2, Observer = new SidecarObserver() });
      List<MsgPackStreamIndex> expected = LsIndexes(bytes);
      Assert.HasCount(3, expected);

      MpRoot root = MsgPackItem.UnpackMultiple(bytes, Streams());
      List<MpStreamIndex> indexes = root.OfType<MpStreamIndex>().ToList();
      List<MpStreamTail> tails = root.OfType<MpStreamTail>().ToList();
      Assert.HasCount(3, indexes);
      Assert.HasCount(3, tails);
      Assert.HasCount(2, root.OfType<MpStreamSidecar>().ToList()); // after the 2nd and the 4th entry
      Assert.HasCount(5, root.OfType<MpArray>().Where(a => !(a is MpStreamTail)).ToList());
      for (int t = 0; t < 3; t++)
      {
        Assert.IsNull(indexes[t].IndexError, indexes[t].IndexError);
        Assert.AreSame(tails[t], indexes[t].Tail);
        Assert.AreSame(indexes[t], tails[t].Index);
        Assert.IsTrue(tails[t].CrcMatches);
        Assert.AreEqual(0L, indexes[t].Delta);
        AreEqual(expected[t], indexes[t].Index);
        Assert.AreEqual(indexes[t].Index.Offset, tails[t].IndexOffset);
        int next = root.IndexOf(indexes[t]) + 1;
        Assert.AreSame(tails[t], root[next]);
      }
      StringAssert.Contains(indexes[2].ToString(), "Complete");
#if KEEPTRACK
      Assert.IsTrue(indexes[0].CrcMatches);
      Assert.AreEqual(2, indexes[0].EntryCount);
      MpArray content = (MpArray)indexes[0].Content;
      Assert.AreEqual("version", indexes[0].NameOf(content.PackedValues[0]));
      Assert.AreEqual("entries", indexes[0].NameOf(content.PackedValues[6]));
      MpArray entry = (MpArray)((MpArray)content.PackedValues[6]).PackedValues[1];
      Assert.AreEqual("identifier", indexes[0].NameOf(entry.PackedValues[2]));
      Assert.AreEqual("orders 2", entry.PackedValues[2].Value);
      Assert.AreEqual("points", indexes[0].NameOf(entry.PackedValues[7]));
      MpArray point = (MpArray)((MpArray)entry.PackedValues[7]).PackedValues[0];
      Assert.AreEqual("element", indexes[0].NameOf(point.PackedValues[0]));
      Assert.IsNull(indexes[0].NameOf(entry)); // an element of a list
      // The content is read from the data: its items have offsets in it
      Assert.IsGreaterThan(indexes[0].StoredOffset, content.StoredOffset);
      Assert.AreEqual(indexes[0].StoredOffset + indexes[0].StoredLength, content.StoredOffset + content.StoredLength);
#endif
    }

    [TestMethod]
    public void AMovedStreamKeepsItsIndex()
    {
      byte[] bytes = Write(Entries(3), new MsgPackStreamIndexOptions { CheckpointInterval = 2 });
      byte[] moved = Enumerable.Repeat((byte)0xC0, 100).Concat(bytes).ToArray(); // nils before it
      List<MsgPackStreamIndex> expected = LsIndexes(moved);
      Assert.AreEqual(100L, expected[0].Entries[0].Offset);

      List<MpStreamIndex> indexes = MsgPackItem.UnpackMultiple(moved, Streams()).OfType<MpStreamIndex>().ToList();
      Assert.HasCount(2, indexes);
      for (int t = 0; t < 2; t++)
      {
        Assert.AreEqual(100L, indexes[t].Delta);
        Assert.IsTrue(indexes[t].Tail.CrcMatches);
        AreEqual(expected[t], indexes[t].Index);
      }
    }

    [TestMethod]
    public void ADamagedIndexDoesNotMatchItsCrc()
    {
      byte[] bytes = Write(Entries(2), new MsgPackStreamIndexOptions());
      int writer = IndexOf(bytes, Encoding.ASCII.GetBytes("LsMsgPack"));
      Assert.IsGreaterThan(0, writer);
      bytes[writer] = (byte)'X'; // still a valid index, but not the one the tail describes

      MpStreamIndex index = MsgPackItem.UnpackMultiple(bytes, Streams()).OfType<MpStreamIndex>().Single();
      Assert.IsNotNull(index.Index);
      StringAssert.StartsWith(index.Index.Writer, "XsMsgPack");
      Assert.IsFalse(index.Tail.CrcMatches);
      StringAssert.Contains(index.ToString(), "does not match");
      Assert.IsEmpty(LsIndexes(bytes)); // LsMsgPack does not use it
#if KEEPTRACK
      MsgPackValidation.ValidationItem[] issues = MsgPackValidation.ValidateItem(index, 1000);
      Assert.IsTrue(issues.Any(i => i.Severity == MsgPackValidation.ValidationSeverity.Error && i.Message.Contains("does not match the CRC-32")), string.Join("; ", issues.Select(i => i.Message)));
#endif
    }

    [TestMethod]
    public void AnIndexWithoutItsTail()
    {
      byte[] bytes = Write(Entries(2), new MsgPackStreamIndexOptions());
      byte[] cut = bytes.Take(bytes.Length - MsgPackStreamIndex.TailLength).ToArray();
      MpStreamIndex index = MsgPackItem.UnpackMultiple(cut, Streams()).OfType<MpStreamIndex>().Single();
      Assert.IsNull(index.Tail);
      Assert.IsNotNull(index.Index);
      Assert.HasCount(2, index.Index.Entries);
      StringAssert.Contains(index.ToString(), "no tail follows it");
#if KEEPTRACK
      Assert.IsTrue(MsgPackValidation.ValidateItem(index, 1000).Any(i => i.Severity == MsgPackValidation.ValidationSeverity.Warning && i.Message.Contains("No tail follows")));
      MpArray array = MsgPackItem.UnpackMultiple(cut, Streams()).OfType<MpArray>().First();
      Assert.IsTrue(MsgPackValidation.ValidateItem(array, 1000).Any(i => i.Message.Contains("msgpack/msgpack#347")));
#endif
    }

    [TestMethod]
    public void WithoutTheSettingTheIndexIsAnExtension()
    {
      byte[] bytes = Write(Entries(2, false), new MsgPackStreamIndexOptions());
      MsgPackSettings settings = new MsgPackSettings { DynamicallyCompact = false };
      MpRoot root = MsgPackItem.UnpackMultiple(bytes, settings);
      Assert.IsFalse(root.OfType<MpStreamIndex>().Any());
      Assert.IsFalse(root.OfType<MpStreamTail>().Any());
      Assert.AreEqual(typeof(MpExt), root[root.Count - 2].GetType());
      Assert.AreEqual(typeof(MpArray), root[root.Count - 1].GetType());
    }

    /// <summary>
    /// Extension types 3 and 4 only mean the index and a sidecar at the root, and a registered custom extension with that type wins.
    /// </summary>
    [TestMethod]
    public void OnlyAtTheRoot()
    {
      MpArray nested = (MpArray)Unpack(Hex("92 d4 03 00 d4 04 00"));
      object[] values = (object[])nested.Value;
      Assert.AreEqual(typeof(MpExt), values[0].GetType()); // an unknown extension stays an MpExt in the unpacked values
      Assert.AreEqual(typeof(MpExt), values[1].GetType());
      Assert.IsInstanceOfType<MpStreamSidecar>(Unpack(Hex("d4 04 00")));
      MpStreamIndex notAnIndex = (MpStreamIndex)Unpack(Hex("d4 03 00"));
      Assert.IsNull(notAnIndex.Index);
      Assert.IsNotNull(notAnIndex.IndexError);
    }

    private static int IndexOf(byte[] bytes, byte[] part)
    {
      for (int t = 0; t <= bytes.Length - part.Length; t++)
      {
        int p = 0;
        while (p < part.Length && bytes[t + p] == part[p])
          p++;
        if (p == part.Length)
          return t;
      }
      return -1;
    }
  }
}
