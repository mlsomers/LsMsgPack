using LsMsgPack;
using LsMsgPack.Streaming;
using LtMsgPack;
using LtMsgPack.Streaming;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SwOrder = LsMsgPackUnitTests.StreamWriterReaderTests.SwOrder;
using SwLine = LsMsgPackUnitTests.StreamWriterReaderTests.SwLine;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The index of a stream (MsgPackStreamIndexOptions, MsgPackStreamReader.ReadIndexes, Seek): entries, hashes, sidecars, points, checkpoints, interruptions, appending, moved streams, validation.
  /// </summary>
  [TestClass]
  public class StreamIndexTests
  {
    private static LtMsgPackSerializer Plain()
    {
      return new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false });
    }

    private static SwOrder Order(int n)
    {
      SwOrder order = new SwOrder { Customer = "Customer " + n, Date = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n) };
      for (int t = 0; t < n % 4; t++)
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

    private static byte[] Write(Action<MsgPackStreamWriter> write, MsgPackStreamIndexOptions index, LtMsgPackSerializer serializer = null, MsgPackStreamSchemas schemas = MsgPackStreamSchemas.PerObject, int flushThreshold = 64 * 1024)
    {
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer ?? Plain(), schemas, true, index))
      {
        writer.FlushThreshold = flushThreshold;
        write(writer);
      }
      return ms.ToArray();
    }

    private static IReadOnlyList<MsgPackStreamIndex> Indexes(byte[] bytes, bool search = false, LtMsgPackSerializer serializer = null)
    {
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(bytes), serializer ?? Plain()))
        return reader.ReadIndexes(search);
    }

    private static List<MsgPackStreamEntry> Entries(IReadOnlyList<MsgPackStreamIndex> indexes)
    {
      return indexes.SelectMany(i => i.Entries).ToList();
    }

    private static byte[] Range(byte[] bytes, long offset, long length)
    {
      return bytes.Skip((int)offset).Take((int)length).ToArray();
    }

    private sealed class Sha256Hasher : IMsgPackStreamHasher
    {
      private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      public string Algorithm { get { return "sha2-256"; } }
      public int HashLength { get { return 32; } }
      public void Append(ReadOnlySpan<byte> data) { _hash.AppendData(data); }
      public int GetHashAndReset(Span<byte> destination) { return _hash.GetHashAndReset(destination); }
    }

    /// <summary>
    /// Keeps the bytes it sees per entry, names the entries and writes a sidecar for the ones with an even number.
    /// </summary>
    private sealed class RecordingObserver : IMsgPackStreamObserver
    {
      private readonly MemoryStream _current = new MemoryStream();
      public readonly List<byte[]> Seen = new List<byte[]>();
      public void Append(ReadOnlySpan<byte> data) { _current.Write(data); }
      public ReadOnlyMemory<byte> EntryEnded(MsgPackStreamEntry entry)
      {
        Seen.Add(_current.ToArray());
        _current.SetLength(0);
        entry.Metadata = new Dictionary<string, object> { { "n", Seen.Count } };
        return Seen.Count % 2 == 0 ? new byte[] { 0xDE, 0xAD, (byte)Seen.Count } : ReadOnlyMemory<byte>.Empty;
      }
    }

    [TestMethod]
    public void EveryRootValueIsAnEntry()
    {
      List<long> positions = new List<long>();
      byte[] bytes = Write(w =>
      {
        positions.Add(w.Position);
        w.Write(42);
        positions.Add(w.Position);
        w.BeginEntry("orders", "application/x-orders").Modified = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        w.WriteArrayStart();
        w.WriteObject(Order(1));
        w.WriteObject(Order(2));
        w.WriteEnd();
        positions.Add(w.Position);
        w.BeginEntry(7L);
        w.WriteMapHeader(1);
        w.Write("key");
        w.Write("value");
        positions.Add(w.Position);
        w.WriteObject(Order(3));
        positions.Add(w.Position);
      }, new MsgPackStreamIndexOptions { Metadata = new Dictionary<string, object> { { "source", "test" }, { "rows", new object[] { 1, "two" } } } });

      IReadOnlyList<MsgPackStreamIndex> indexes = Indexes(bytes);
      Assert.HasCount(1, indexes);
      MsgPackStreamIndex index = indexes[0];
      Assert.AreEqual(MsgPackStreamIndexStatus.Complete, index.Status);
      Assert.AreEqual(MsgPackStreamIndex.CurrentVersion, index.Version);
      StringAssert.StartsWith(index.Writer, "LtMsgPack ");
      Assert.IsNull(index.PreviousTail);
      Assert.AreEqual("test", index.Metadata["source"]);
      Assert.AreEqual(positions[4], index.Offset);
      Assert.AreEqual(bytes.Length, index.Offset + index.Length + MsgPackStreamIndex.TailLength);

      Assert.HasCount(4, index.Entries);
      for (int t = 0; t < 4; t++)
      {
        Assert.AreEqual(positions[t], index.Entries[t].Offset);
        Assert.AreEqual(positions[t + 1] - positions[t], index.Entries[t].Length);
        Assert.IsNull(index.Entries[t].Hash);
        Assert.IsFalse(index.Entries[t].Incomplete);
      }
      Assert.IsNull(index.Entries[0].Identifier);
      Assert.AreEqual("orders", index.Entries[1].Identifier);
      Assert.AreEqual("application/x-orders", index.Entries[1].ContentType);
      Assert.AreEqual(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), index.Entries[1].Modified);
      Assert.AreEqual(2L, index.Entries[1].ElementCount);
      Assert.AreEqual(7L, index.Entries[2].Identifier);
      Assert.IsNull(index.Entries[2].ElementCount); // a map

      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(bytes), Plain()))
      {
        reader.Seek(index.Entries[3]);
        AreEqual(Order(3), reader.ReadObject<SwOrder>());
        reader.Seek(index.Entries[1]);
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(MsgPackTokenType.ArrayStart, reader.TokenType);
        AreEqual(Order(1), reader.ReadObject<SwOrder>());
        reader.Seek(index.Entries[0]);
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(42L, reader.GetInt64());
      }
    }

    /// <summary>
    /// A reader that cannot seek reads the stream value by value: the index, its tail and the sidecars are no values, also through checkpoints.
    /// </summary>
    [TestMethod]
    public void ReadersSkipTheIndexTheTailsAndTheSidecars()
    {
      Action<MsgPackStreamWriter> write = w =>
      {
        for (int n = 1; n <= 7; n++)
          w.WriteObject(Order(n));
      };
      byte[] plain = Write(write, null);
      byte[] indexed = Write(write, new MsgPackStreamIndexOptions { CheckpointInterval = 3, Observer = new RecordingObserver() });
      Assert.IsGreaterThan(plain.Length, indexed.Length);

      foreach (byte[] bytes in new[] { plain, indexed })
      {
        using (MsgPackStreamReader reader = new MsgPackStreamReader(new TrickleStream(bytes), Plain()))
        {
          int n = 0;
          while (!reader.AtEnd)
            AreEqual(Order(++n), reader.ReadObject<SwOrder>());
          Assert.AreEqual(7, n);
          Assert.IsFalse(reader.Read());
        }

        int values = 0;
        using (MsgPackStreamReader reader = new MsgPackStreamReader(new TrickleStream(bytes), Plain()))
        {
          while (true)
          {
            int depth = reader.Depth;
            if (!reader.Read())
              break;
            if (depth == 0)
              values++;
            reader.Skip();
          }
        }
        Assert.AreEqual(7, values);
      }

      IReadOnlyList<MsgPackStreamIndex> indexes = Indexes(indexed);
      Assert.HasCount(3, indexes); // after 3 and 6 entries, then the last one
      Assert.AreEqual(MsgPackStreamIndexStatus.Checkpoint, indexes[0].Status);
      Assert.AreEqual(MsgPackStreamIndexStatus.Checkpoint, indexes[1].Status);
      Assert.AreEqual(MsgPackStreamIndexStatus.Complete, indexes[2].Status);
      CollectionAssert.AreEqual(new[] { 3, 3, 1 }, indexes.Select(i => i.Entries.Count).ToArray());
      Assert.AreEqual(indexes[0].Offset + indexes[0].Length, indexes[1].PreviousTail);
      Assert.AreEqual(indexes[1].Offset + indexes[1].Length, indexes[2].PreviousTail);
    }

    /// <summary>
    /// The hasher and the observer get exactly the bytes of each entry, also across flushes of the buffer (a threshold of a few bytes); the sidecars follow their entries.
    /// </summary>
    [TestMethod]
    public void HashesAndObserversSeeTheBytesOfEachEntry()
    {
      foreach (int threshold in new[] { 0, 5, 64 * 1024 })
      {
        RecordingObserver observer = new RecordingObserver();
        byte[] bytes = Write(w =>
        {
          for (int n = 1; n <= 4; n++)
            w.WriteObject(Order(n));
          w.WriteArrayStart();
          for (int n = 5; n <= 9; n++)
            w.WriteObject(Order(n));
          w.WriteEnd();
        }, new MsgPackStreamIndexOptions { Hasher = new Sha256Hasher(), Observer = observer }, flushThreshold: threshold);

        MsgPackStreamIndex index = Indexes(bytes).Single();
        Assert.AreEqual("sha2-256", index.HashAlgorithm);
        Assert.HasCount(5, index.Entries);
        Assert.HasCount(5, observer.Seen);
        for (int t = 0; t < 5; t++)
        {
          MsgPackStreamEntry entry = index.Entries[t];
          byte[] content = Range(bytes, entry.Offset, entry.Length);
          CollectionAssert.AreEqual(SHA256.HashData(content), entry.Hash, $"threshold {threshold}, entry {t}");
          CollectionAssert.AreEqual(content, observer.Seen[t]);
          Assert.AreEqual((byte)(t + 1), Convert.ToByte(entry.Metadata["n"]));
          if (t % 2 == 1)
          {
            Assert.AreEqual(entry.Offset + entry.Length + 3, entry.SidecarOffset); // after the ext 8 header of 3 bytes
            CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD, (byte)(t + 1) }, Range(bytes, entry.SidecarOffset.Value, entry.SidecarLength));
          }
          else
            Assert.IsNull(entry.SidecarOffset);
        }
      }
    }

    /// <summary>
    /// A table as a head and an array of unknown or known length: points every 10 rows, a manual point with a key, and reading from any row.
    /// </summary>
    [TestMethod]
    public void PointsLeadIntoTheRowsOfATable()
    {
      foreach (bool unknown in new[] { true, false })
      {
        SchemaStore store = new SchemaStore();
        LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { WriteSchemaReference = true, SchemaStore = store });
        byte[] bytes = Write(w =>
        {
          w.WriteObject(Order(1000)); // an entry before the table
          w.BeginEntry("table");
          w.WriteHead<List<SwOrder>>();
          if (unknown)
            w.WriteArrayStart();
          else
            w.WriteArrayHeader(100);
          for (int n = 0; n < 100; n++)
          {
            if (n == 55)
              w.AddPoint(new DateTime(2026, 10, 31, 0, 0, 55, DateTimeKind.Utc));
            w.WriteObject(Order(n));
          }
          if (unknown)
          {
            w.AddPoint("dangling"); // no element follows it: left out
            w.WriteEnd();
          }
        }, new MsgPackStreamIndexOptions { PointInterval = 10 }, serializer, MsgPackStreamSchemas.PerStream);

        MsgPackStreamEntry table = Indexes(bytes, serializer: serializer)[0].Entries[1];
        Assert.AreEqual("table", table.Identifier);
        Assert.AreEqual(100L, table.ElementCount);
        CollectionAssert.AreEqual(new long[] { 0, 10, 20, 30, 40, 50, 55, 60, 70, 80, 90 }, table.Points.Select(p => p.Element).ToArray());
        Assert.AreEqual(new DateTime(2026, 10, 31, 0, 0, 55, DateTimeKind.Utc), table.Points[6].Key);

        LtMsgPackSerializer reading = new LtMsgPackSerializer(new LtMsgPackOptions { SchemaStore = new SchemaStore() }); // learns the schemas from the stream through the index
        using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(bytes), reading))
        {
          IReadOnlyList<MsgPackStreamIndex> indexes = reader.ReadIndexes();
          reader.Seek(table);
          reader.ReadHead<List<SwOrder>>();
          Assert.IsTrue(reader.Read());
          Assert.AreEqual(MsgPackTokenType.ArrayStart, reader.TokenType);
          foreach (long row in new long[] { 57, 99, 3, 55, 56, 0 })
          {
            reader.SeekElement(table, row);
            AreEqual(Order((int)row), reader.ReadObject<SwOrder>());
          }
          reader.SeekElement(table, 95);
          int count = 0;
          while (!reader.AtEnd)
            AreEqual(Order(95 + count++), reader.ReadObject<SwOrder>());
          Assert.AreEqual(5, count);
          Assert.IsTrue(reader.Read());
          Assert.AreEqual(MsgPackTokenType.ArrayEnd, reader.TokenType);
          Assert.AreEqual(0, reader.Depth);
        }
      }
    }

    /// <summary>
    /// With PerStream the later objects refer to the schema of the first one: the index locates it, so a reader that starts at a later entry can read it.
    /// </summary>
    [TestMethod]
    public void TheIndexLocatesTheSchemasOfPerStream()
    {
      SchemaStore store = new SchemaStore();
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { WriteSchemaReference = true, SchemaStore = store });
      byte[] bytes = Write(w =>
      {
        for (int n = 1; n <= 3; n++)
          w.WriteObject(Order(n));
      }, new MsgPackStreamIndexOptions(), serializer, MsgPackStreamSchemas.PerStream);

      MsgPackStreamIndex index = Indexes(bytes).Single();
      Assert.IsNotEmpty(index.Schemas);
      Assert.AreEqual(index.Entries[0].Offset, index.Schemas[0].Offset); // inline in the first entry

      LtMsgPackSerializer reading = new LtMsgPackSerializer(new LtMsgPackOptions { SchemaStore = new SchemaStore() });
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(bytes), reading))
      {
        reader.Seek(index.Entries[2]);
        Assert.ThrowsExactly<MissingSchemaException>(() => reader.ReadObject<SwOrder>()); // a reference to a schema the store does not have yet
        index = reader.ReadIndexes().Single();
        reader.Seek(index.Entries[2]);
        AreEqual(Order(3), reader.ReadObject<SwOrder>());
      }
    }

    /// <summary>
    /// A writer that stops after a checkpoint (a crash): the stream does not end with a tail, the last checkpoint is found by searching backwards.
    /// </summary>
    [TestMethod]
    public void ACrashLeavesTheEntriesBeforeTheLastCheckpoint()
    {
      byte[] complete = Write(w =>
      {
        for (int n = 1; n <= 5; n++)
          w.WriteObject(Order(n));
      }, new MsgPackStreamIndexOptions { CheckpointInterval = 2, Hasher = new Sha256Hasher() });
      IReadOnlyList<MsgPackStreamIndex> all = Indexes(complete);
      long afterCheckpoint = all[1].Offset + all[1].Length + MsgPackStreamIndex.TailLength;
      byte[] crashed = complete.Take((int)afterCheckpoint + 7).ToArray(); // part of the fifth entry

      Assert.IsEmpty(Indexes(crashed));
      IReadOnlyList<MsgPackStreamIndex> found = Indexes(crashed, true);
      Assert.HasCount(2, found);
      Assert.AreEqual(MsgPackStreamIndexStatus.Checkpoint, found[1].Status);
      List<MsgPackStreamEntry> entries = Entries(found);
      Assert.HasCount(4, entries);
      for (int t = 0; t < 4; t++)
        CollectionAssert.AreEqual(SHA256.HashData(Range(crashed, entries[t].Offset, entries[t].Length)), entries[t].Hash);
      Assert.IsEmpty(Indexes(Write(w => w.WriteObject(Order(1)), null), true)); // no index at all

      Assert.ThrowsExactly<InvalidOperationException>(() => MsgPackStreamWriter.Append(new MemoryStream(crashed.ToArray()), Plain()));
    }

    [TestMethod]
    public void DisposingInTheMiddleOfAValueMarksTheIndexInterrupted()
    {
      byte[] bytes = Write(w =>
      {
        w.WriteObject(Order(1));
        w.WriteArrayStart();
        w.WriteObject(Order(2));
      }, new MsgPackStreamIndexOptions());
      MsgPackStreamIndex index = Indexes(bytes).Single();
      Assert.AreEqual(MsgPackStreamIndexStatus.Interrupted, index.Status);
      Assert.HasCount(2, index.Entries);
      Assert.IsFalse(index.Entries[0].Incomplete);
      Assert.IsTrue(index.Entries[1].Incomplete);
      Assert.AreEqual(1L, index.Entries[1].ElementCount);
      Assert.AreEqual(index.Offset, index.Entries[1].Offset + index.Entries[1].Length);
      Assert.ThrowsExactly<InvalidOperationException>(() => MsgPackStreamWriter.Append(new MemoryStream(bytes.ToArray()), Plain()));

      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions());
      bytes = Write(w =>
      {
        w.WriteObject(Order(1));
        w.WriteHead<List<SwOrder>>(); // a head without its body
      }, new MsgPackStreamIndexOptions(), serializer);
      index = Indexes(bytes).Single();
      Assert.AreEqual(MsgPackStreamIndexStatus.Interrupted, index.Status);
      Assert.IsTrue(index.Entries[1].Incomplete);
    }

    public class SiExploding
    {
      public int Value { get { throw new InvalidTimeZoneException("getter"); } }
    }

    [TestMethod]
    public void ARefusedRootValueIsNoInterruption()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions());
      byte[] bytes = Write(w =>
      {
        w.WriteObject(Order(1));
        w.WriteHead<List<SwOrder>>();
        w.WriteArrayStart();
        Assert.ThrowsExactly<InvalidOperationException>(() => w.WriteObject<object>(new StreamWriterReaderTests.SwBag { Name = "bag" })); // a class the head's schema does not have: nothing written
        w.WriteObject(Order(2));
        w.WriteEnd();
      }, new MsgPackStreamIndexOptions { PointInterval = 1 }, serializer);
      MsgPackStreamIndex index = Indexes(bytes).Single();
      Assert.AreEqual(MsgPackStreamIndexStatus.Complete, index.Status);
      Assert.AreEqual(1L, index.Entries[1].ElementCount);
      Assert.HasCount(1, index.Entries[1].Points);

      bytes = Write(w =>
      {
        w.WriteObject(Order(1));
        Assert.ThrowsExactly<InvalidTimeZoneException>(() => w.WriteObject(new SiExploding())); // the last root value, nothing written
      }, new MsgPackStreamIndexOptions());
      index = Indexes(bytes).Single();
      Assert.AreEqual(MsgPackStreamIndexStatus.Complete, index.Status);
      Assert.HasCount(1, index.Entries);
    }

    [TestMethod]
    public void AppendingAddsAnIndexThatPointsToTheOneBefore()
    {
      MemoryStream file = new MemoryStream();
      using (MsgPackStreamWriter writer = MsgPackStreamWriter.Append(file, Plain(), index: new MsgPackStreamIndexOptions { Hasher = new Sha256Hasher() }, leaveOpen: true)) // an empty stream
      {
        writer.WriteObject(Order(1));
        writer.WriteObject(Order(2));
      }
      for (int part = 0; part < 2; part++)
      {
        using (MsgPackStreamWriter writer = MsgPackStreamWriter.Append(file, Plain(), leaveOpen: true))
        {
          writer.BeginEntry("part " + part);
          writer.WriteObject(Order(3 + part));
        }
      }

      byte[] bytes = file.ToArray();
      IReadOnlyList<MsgPackStreamIndex> indexes = Indexes(bytes);
      Assert.HasCount(3, indexes);
      Assert.AreEqual("sha2-256", indexes[0].HashAlgorithm);
      Assert.IsNull(indexes[1].HashAlgorithm);
      List<MsgPackStreamEntry> entries = Entries(indexes);
      Assert.HasCount(4, entries);
      Assert.AreEqual("part 1", entries[3].Identifier);
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(bytes), Plain()))
      {
        for (int t = 3; t >= 0; t--)
        {
          reader.Seek(entries[t]);
          AreEqual(Order(t + 1), reader.ReadObject<SwOrder>());
        }
      }
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new TrickleStream(bytes), Plain()))
      {
        int n = 0;
        while (!reader.AtEnd)
          AreEqual(Order(++n), reader.ReadObject<SwOrder>());
        Assert.AreEqual(4, n);
      }

      MemoryStream unindexed = new MemoryStream(); // expandable
      unindexed.Write(Write(w => w.WriteObject(Order(1)), null));
      using (MsgPackStreamWriter writer = MsgPackStreamWriter.Append(unindexed, Plain(), leaveOpen: true))
        writer.WriteObject(Order(2));
      MsgPackStreamEntry appended = Indexes(unindexed.ToArray()).Single().Entries.Single();
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(unindexed.ToArray()), Plain()))
      {
        reader.Seek(appended);
        AreEqual(Order(2), reader.ReadObject<SwOrder>());
      }
    }

    /// <summary>
    /// A stream embedded after other data: the offsets move along with it.
    /// </summary>
    [TestMethod]
    public void AMovedStreamKeepsItsIndex()
    {
      byte[] bytes = Write(w =>
      {
        for (int n = 1; n <= 3; n++)
          w.WriteObject(Order(n));
      }, new MsgPackStreamIndexOptions { CheckpointInterval = 2 });
      byte[] moved = new byte[100].Concat(bytes).ToArray();

      List<MsgPackStreamEntry> entries = Entries(Indexes(moved));
      Assert.HasCount(3, entries);
      Assert.AreEqual(100L, entries[0].Offset);
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(moved), Plain()))
      {
        reader.Seek(entries[2]);
        AreEqual(Order(3), reader.ReadObject<SwOrder>());
      }
    }

    /// <summary>
    /// The index and its tail are in the byte order of the specification, the extension header in the stream's (for the readers that skip it).
    /// </summary>
    [TestMethod]
    public void TheIndexIgnoresTheByteOrderOfTheStream()
    {
      LtMsgPackSerializer swapped = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, EndianAction = EndianAction.NeverSwap });
      Action<MsgPackStreamWriter> write = w =>
      {
        w.BeginEntry(300);
        w.WriteArrayHeader(300); // lengths that differ by byte order
        for (int n = 0; n < 300; n++)
          w.Write(n * 1000);
      };
      byte[] bytes = Write(write, new MsgPackStreamIndexOptions { PointInterval = 2 }, swapped);
      MsgPackStreamEntry entry = Indexes(bytes).Single().Entries.Single();
      Assert.AreEqual(300L, entry.Identifier);
      Assert.HasCount(150, entry.Points);

      using (MsgPackStreamReader reader = new MsgPackStreamReader(new TrickleStream(bytes), swapped))
      {
        Assert.IsTrue(reader.Read());
        reader.Skip();
        Assert.IsTrue(reader.AtEnd);
        Assert.IsFalse(reader.Read());
      }
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(bytes), swapped))
      {
        reader.Seek(entry);
        Assert.IsTrue(reader.Read());
        reader.SeekElement(entry, 251);
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(251000L, reader.GetInt64());
      }
    }

    [TestMethod]
    public void MetadataMustBePlainValues()
    {
      Assert.ThrowsExactly<ArgumentException>(() => new MsgPackStreamWriter(new MemoryStream(), Plain(), index: new MsgPackStreamIndexOptions { Metadata = new Dictionary<string, object> { { "x", new Version(1, 0) } } }));
      Assert.ThrowsExactly<ArgumentException>(() => Write(w => w.BeginEntry(1.5), new MsgPackStreamIndexOptions()));
      Assert.ThrowsExactly<InvalidOperationException>(() => Write(w => w.BeginEntry(), null));
      Assert.ThrowsExactly<InvalidOperationException>(() => Write(w => { w.WriteArrayStart(); w.BeginEntry(); }, new MsgPackStreamIndexOptions()));
      Assert.ThrowsExactly<InvalidOperationException>(() => Write(w => w.AddPoint(), new MsgPackStreamIndexOptions()));
      Assert.ThrowsExactly<InvalidOperationException>(() => Write(w => { w.WriteMapStart(); w.AddPoint(); }, new MsgPackStreamIndexOptions()));

      byte[] bytes = Write(w =>
      {
        MsgPackStreamEntry entry = w.BeginEntry("e");
        entry.Metadata = new Dictionary<string, object> { { "nested", new Dictionary<string, object> { { "list", new List<object> { 1, "a", null, new byte[] { 1 } } } } } };
        w.Write(true);
      }, new MsgPackStreamIndexOptions());
      KeyValuePair<object, object>[] nested = (KeyValuePair<object, object>[])Indexes(bytes).Single().Entries.Single().Metadata["nested"];
      object[] list = (object[])nested.Single().Value;
      Assert.AreEqual("a", list[1]);
      Assert.IsNull(list[2]);
    }

    /// <summary>
    /// A changed byte breaks the CRC-32: no index (not a wrong one). An index that matches its CRC-32 but points outside of the stream is refused.
    /// </summary>
    [TestMethod]
    public void BrokenIndexesAreNotUsed()
    {
      byte[] bytes = Write(w => w.WriteObject(Order(1)), new MsgPackStreamIndexOptions());
      MsgPackStreamIndex index = Indexes(bytes).Single();
      byte[] changed = bytes.ToArray();
      changed[index.Offset + 3] ^= 1;
      Assert.IsEmpty(Indexes(changed));
      Assert.IsEmpty(Indexes(changed, true));

      // [1, 0, nil, nil, nil, [], [[1000, 5]]]: an entry after the index
      byte[] content = { 0x97, 0x01, 0x00, 0xC0, 0xC0, 0xC0, 0x90, 0x91, 0x92, 0xCD, 0x03, 0xE8, 0x05 };
      byte[] crafted = Crafted(new byte[] { 0xC0, 0xC0 }, content);
      Assert.ThrowsExactly<MsgPackException>(() => Indexes(crafted));

      // a newer version
      content = new byte[] { 0x97, 0x02, 0x00, 0xC0, 0xC0, 0xC0, 0x90, 0x90 };
      Assert.ThrowsExactly<MsgPackException>(() => Indexes(Crafted(new byte[] { 0xC0 }, content)));

      // points out of order: [1, 0, nil, nil, nil, [], [[0, 1, nil, nil, nil, nil, 3, [[1, 0], [0, 0]]]]]
      content = new byte[] { 0x97, 0x01, 0x00, 0xC0, 0xC0, 0xC0, 0x90, 0x91, 0x98, 0x00, 0x01, 0xC0, 0xC0, 0xC0, 0xC0, 0x03, 0x92, 0x92, 0x01, 0x00, 0x92, 0x00, 0x00 };
      Assert.ThrowsExactly<MsgPackException>(() => Indexes(Crafted(new byte[] { 0xC0 }, content)));

      using (MsgPackStreamReader reader = new MsgPackStreamReader(new TrickleStream(bytes), Plain()))
        Assert.ThrowsExactly<NotSupportedException>(() => reader.ReadIndexes());
    }

    /// <summary>
    /// <paramref name="data"/>, then an index with <paramref name="content"/> (ext 8 of type 3) and its tail.
    /// </summary>
    private static byte[] Crafted(byte[] data, byte[] content)
    {
      List<byte> bytes = new List<byte>(data);
      long offset = bytes.Count;
      byte[] index = new byte[] { 0xC7, (byte)content.Length, 0x03 }.Concat(content).ToArray();
      bytes.AddRange(index);
      uint crc = Crc32.Compute(index);
      bytes.Add(0x94);
      bytes.Add(0xCF);
      for (int shift = 56; shift >= 0; shift -= 8)
        bytes.Add((byte)(offset >> shift));
      bytes.Add(0xCE);
      bytes.AddRange(BitConverter.GetBytes((uint)index.Length).Reverse());
      bytes.Add(0xCE);
      bytes.AddRange(BitConverter.GetBytes(crc).Reverse());
      bytes.Add(0xA4);
      bytes.AddRange("LSIX"u8.ToArray());
      return bytes.ToArray();
    }

    /// <summary>
    /// Cannot seek and returns 1 to 3 bytes per read, as a network stream may.
    /// </summary>
    private sealed class TrickleStream : Stream
    {
      private readonly MemoryStream _inner;
      private int _reads;
      public TrickleStream(byte[] bytes) { _inner = new MemoryStream(bytes); }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return false; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { throw new NotSupportedException(); } }
      public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count) { return _inner.Read(buffer, offset, Math.Min(count, 1 + _reads++ % 3)); }
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
  }
}
