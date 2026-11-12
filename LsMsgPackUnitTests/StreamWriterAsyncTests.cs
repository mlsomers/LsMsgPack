using LsMsgPack;
using LsMsgPack.Streaming;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SwOrder = LsMsgPackUnitTests.StreamWriterReaderTests.SwOrder;
using SwLine = LsMsgPackUnitTests.StreamWriterReaderTests.SwLine;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The asynchronous MsgPackStreamWriter: it writes to the stream only in its async methods (an ASP.NET Core response), and writes the same bytes as the synchronous one.
  /// </summary>
  [TestClass]
  public class StreamWriterAsyncTests
  {
    /// <summary>
    /// Refuses synchronous writes and flushes, as an ASP.NET Core response does.
    /// </summary>
    private sealed class AsyncOnlyStream : MemoryStream
    {
      internal int Flushes;

      public override void Write(byte[] buffer, int offset, int count) { throw new InvalidOperationException("Synchronous write."); }
      public override void Write(ReadOnlySpan<byte> buffer) { throw new InvalidOperationException("Synchronous write."); }
      public override void WriteByte(byte value) { throw new InvalidOperationException("Synchronous write."); }
      public override void Flush() { throw new InvalidOperationException("Synchronous flush."); }

      public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
        base.Write(buffer, offset, count);
        return Task.CompletedTask;
      }

      public override Task FlushAsync(CancellationToken cancellationToken)
      {
        Flushes++;
        return Task.CompletedTask;
      }
    }

    private static SwOrder Order(int n)
    {
      SwOrder order = new SwOrder { Customer = "Customer " + n, Date = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n) };
      for (int t = 0; t < n % 4; t++)
        order.Lines.Add(new SwLine { Product = "P" + t, Price = 1.25m * t, Quantity = t });
      return order;
    }

    private static LsMsgPackSerializer Indexed()
    {
      return new LsMsgPackSerializer(new LsMsgPackOptions());
    }

    private static MsgPackStreamIndexOptions Index()
    {
      return new MsgPackStreamIndexOptions { CheckpointInterval = 2, PointInterval = 3 };
    }

    /// <summary>
    /// A table (a head and an array of unknown length) and objects with their schema, with an index and checkpoints.
    /// </summary>
    private static void WriteValues(MsgPackStreamWriter writer, int first, int last)
    {
      for (int n = first; n <= last; n++)
        writer.WriteObject(Order(n));
    }

    private static byte[] WriteSynchronously()
    {
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, Indexed(), MsgPackStreamSchemas.PerObject, true, Index()))
      {
        writer.FlushThreshold = 100;
        writer.BeginEntry("table");
        writer.WriteHead<List<SwOrder>>();
        writer.WriteArrayStart();
        WriteValues(writer, 1, 10);
        writer.WriteEnd();
        for (int n = 11; n <= 14; n++)
          writer.WriteObject(Order(n));
        writer.Write("end");
      }
      return ms.ToArray();
    }

    [TestMethod]
    public async Task SameBytesAsTheSynchronousWriter()
    {
      AsyncOnlyStream stream = new AsyncOnlyStream();
      MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, Indexed(), MsgPackStreamSchemas.PerObject, true, Index(), true);
      Assert.IsTrue(writer.IsAsynchronous);
      writer.FlushThreshold = 100;
      writer.BeginEntry("table");
      writer.WriteHead<List<SwOrder>>();
      writer.WriteArrayStart();
      for (int n = 1; n <= 10; n++)
        await writer.WriteObjectAsync(Order(n));
      writer.WriteEnd();
      await writer.FlushIfFullAsync();
      for (int n = 11; n <= 14; n++)
        await writer.WriteObjectAsync((object)Order(n), typeof(SwOrder));
      writer.Write("end");
      await writer.DisposeAsync();

      CollectionAssert.AreEqual(WriteSynchronously(), stream.ToArray());
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(stream.ToArray()), Indexed()))
      {
        IReadOnlyList<MsgPackStreamIndex> indexes = reader.ReadIndexes();
        Assert.AreEqual(MsgPackStreamIndexStatus.Checkpoint, indexes[0].Status);
        Assert.AreEqual(MsgPackStreamIndexStatus.Complete, indexes[indexes.Count - 1].Status);
      }
    }

    [TestMethod]
    public async Task TheBufferGoesOutOnlyInTheAsyncMethods()
    {
      AsyncOnlyStream stream = new AsyncOnlyStream();
      MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, Indexed(), MsgPackStreamSchemas.PerObject, true, null, true);
      writer.FlushThreshold = 10;
      writer.WriteHead<List<SwOrder>>();
      writer.WriteArrayStart();
      writer.WriteObject(Order(3));
      writer.WriteObject(Order(2));
      long position = writer.Position;
      Assert.AreEqual(0, stream.Length, "Past the threshold, nothing written synchronously");

      await writer.FlushIfFullAsync();
      Assert.AreEqual(position, stream.Length);
      Assert.AreEqual(0, stream.Flushes, "Written, the stream not flushed");

      writer.Write(1);
      await writer.FlushIfFullAsync();
      Assert.AreEqual(position, stream.Length, "Below the threshold");
      await writer.FlushAsync();
      Assert.AreEqual(position + 1, stream.Length);
      Assert.AreEqual(1, stream.Flushes);

      Assert.ThrowsExactly<InvalidOperationException>(() => writer.Flush());
    }

    [TestMethod]
    public async Task ACheckpointIsFlushedByFlushIfFullAsync()
    {
      AsyncOnlyStream stream = new AsyncOnlyStream();
      MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, Indexed(), MsgPackStreamSchemas.PerObject, true, new MsgPackStreamIndexOptions { CheckpointInterval = 1 }, true);
      writer.WriteObject(Order(1)); // an entry, then the checkpoint
      Assert.AreEqual(0, stream.Length);
      await writer.FlushIfFullAsync();
      Assert.AreEqual(writer.Position, stream.Length);
      Assert.AreEqual(1, stream.Flushes);
      await writer.FlushIfFullAsync();
      Assert.AreEqual(1, stream.Flushes, "Once");
    }

    /// <summary>
    /// Dispose writes nothing (the stream may not allow it): an aborted HTTP response is not completed with what was buffered.
    /// </summary>
    [TestMethod]
    public void DisposeOnlyClosesTheStream()
    {
      AsyncOnlyStream stream = new AsyncOnlyStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, Indexed(), MsgPackStreamSchemas.PerObject, false, new MsgPackStreamIndexOptions(), true))
      {
        writer.WriteObject(Order(1));
      }
      Assert.IsEmpty(stream.ToArray(), "Neither the buffer nor the index");
      Assert.IsFalse(stream.CanWrite, "Closed");
    }

    [TestMethod]
    public async Task DisposeAsyncOfTheSynchronousWriter()
    {
      MemoryStream ms = new MemoryStream();
      MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, Indexed(), MsgPackStreamSchemas.PerObject, true, Index());
      writer.FlushThreshold = 100;
      writer.BeginEntry("table");
      writer.WriteHead<List<SwOrder>>();
      writer.WriteArrayStart();
      WriteValues(writer, 1, 10);
      writer.WriteEnd();
      for (int n = 11; n <= 14; n++)
        writer.WriteObject(Order(n));
      writer.Write("end");
      await writer.DisposeAsync();
      CollectionAssert.AreEqual(WriteSynchronously(), ms.ToArray());
    }
  }
}
