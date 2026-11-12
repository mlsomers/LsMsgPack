using LsMsgPack;
using LsMsgPack.Streaming;
using LsMsgPack.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPack.DataTests
{
  /// <summary>
  /// The rows of MsgPackDataReader, read column by column as the column types: every setting, rows of unknown length and maps, asynchronous reads of a stream that trickles.
  /// </summary>
  [TestClass]
  public class DataReaderRowsTests
  {
    private static byte[] AllTypesDump(LsMsgPackSerializer serializer, List<AllTypesRow> rows)
    {
      MemoryStream stream = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true, index: new MsgPackStreamIndexOptions()))
        writer.WriteTable(Data.AllTypes(rows), "AllTypes");
      return stream.ToArray();
    }

    /// <summary>
    /// Every value of AllTypes as the source had it: at the index entry with the exact types (GetValue, the typed getters through ReadRows), sequentially converted from the schema's types.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(Variant.All), typeof(Variant))]
    public void AllTypes_EverySetting(Variant variant)
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(variant.Ls());
      List<AllTypesRow> rows = Data.AllTypesRows();
      byte[] dump = AllTypesDump(serializer, rows);
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(dump), serializer))
      {
        MsgPackStreamEntry entry = reader.ReadIndexes()[0].Entries[0];
        using (MsgPackDataReader read = new MsgPackDataReader(reader, entry, leaveOpen: true))
          AssertSameValues(Data.AllTypes(rows), read, true);
        if (variant.Indexed) // otherwise there are no columns to map
          using (MsgPackDataReader read = new MsgPackDataReader(reader, entry, leaveOpen: true))
            SerializeRowsTests.AssertEqual(rows, serializer.ReadRows<AllTypesRow>(read));
      }
      using (MsgPackDataReader read = new MsgPackDataReader(new MsgPackStreamReader(new MemoryStream(dump), serializer)))
        AssertSameValues(Data.AllTypes(rows), read, false);
    }

    /// <param name="exact">The same field types and values, otherwise the values of <paramref name="actual"/> read with the typed getter of <paramref name="expected"/>'s type</param>
    private static void AssertSameValues(ColumnarReader expected, MsgPackDataReader actual, bool exact)
    {
      if (actual.FieldCount == 0) // without the indexed schema the head has no schema: no columns
      {
        int count = 0;
        while (actual.Read())
          count++;
        Assert.AreEqual(Data.AllTypesRows().Count, count);
        return;
      }
      Assert.AreEqual(expected.FieldCount, actual.FieldCount);
      for (int t = 0; t < expected.FieldCount && exact; t++)
        Assert.AreEqual(expected.GetFieldType(t), actual.GetFieldType(t), expected.GetName(t));
      int row = 0;
      while (expected.Read())
      {
        Assert.IsTrue(actual.Read(), "row " + row);
        for (int t = 0; t < expected.FieldCount; t++)
        {
          object value = expected.GetValue(t);
          object read = actual.GetValue(t);
          string where = expected.GetName(t) + " row " + row;
          Assert.AreEqual(value is DBNull, actual.IsDBNull(t), where);
          if (value is DBNull)
            Assert.IsInstanceOfType(read, typeof(DBNull), where);
          else if (value is byte[] bytes)
          {
            CollectionAssert.AreEqual(bytes, (byte[])read, where);
            CollectionAssert.AreEqual(bytes, actual.GetFieldValue<byte[]>(t), where);
          }
          else
          {
            if (exact)
              Assert.AreEqual(value, read, where);
            Assert.AreEqual(value, Typed(actual, t, value), "typed getter, " + where);
          }
        }
        row++;
      }
      Assert.IsFalse(actual.Read());
    }

    /// <summary>
    /// The value of the column with the typed getter of <paramref name="like"/>'s type.
    /// </summary>
    private static object Typed(MsgPackDataReader reader, int ordinal, object like)
    {
      switch (like)
      {
        case int _: return reader.GetInt32(ordinal);
        case long _: return reader.GetInt64(ordinal);
        case short _: return reader.GetInt16(ordinal);
        case byte _: return reader.GetByte(ordinal);
        case bool _: return reader.GetBoolean(ordinal);
        case double _: return reader.GetDouble(ordinal);
        case float _: return reader.GetFloat(ordinal);
        case decimal _: return reader.GetDecimal(ordinal);
        case DateTime _: return reader.GetDateTime(ordinal);
        case Guid _: return reader.GetGuid(ordinal);
        case string _: return reader.GetString(ordinal);
        default: return reader.GetFieldValue<object>(ordinal);
      }
    }

    public class Line
    {
      public long Id { get; set; }
      public string Text { get; set; }
      public double? Amount { get; set; }
    }

    /// <summary>
    /// Rows the stream writer writes token by token: arrays and a str of unknown length, a map by schema positions (out of order, a position that is no column), nil, a trimmed array, an extra value.
    /// </summary>
    [TestMethod]
    [DataRow(SchemaVersion.Names)]
    [DataRow(SchemaVersion.Described)]
    public void RowsOfUnknownLength_Maps_Nil(SchemaVersion version)
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { SchemaVersion = version });
      MemoryStream stream = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true))
      {
        writer.WriteHead<List<Line>>();
        writer.WriteArrayStart();

        writer.WriteArrayStart();
        writer.Write(1L);
        writer.WriteStringStart();
        writer.WriteStringChunk("in ".AsSpan());
        writer.WriteStringChunk("chunks".AsSpan());
        writer.WriteEnd();
        writer.Write(1.5);
        writer.WriteEnd();

        writer.WriteMapStart();
        writer.Write(2);
        writer.Write(2.5);
        writer.Write(9); // no column
        writer.Write("ignored");
        writer.Write(0);
        writer.Write(2L);
        writer.WriteEnd();

        writer.WriteNil();

        writer.WriteArrayHeader(1); // trimmed
        writer.Write(4L);

        writer.WriteArrayHeader(4); // a value after the last column
        writer.Write(5L);
        writer.Write("five");
        writer.WriteNil();
        writer.Write(true);

        writer.WriteEnd();
      }

      stream.Position = 0;
      using (MsgPackDataReader read = new MsgPackDataReader(new MsgPackStreamReader(stream, serializer)))
      {
        CollectionAssert.AreEqual(new[] { "Id", "Text", "Amount" }, Enumerable.Range(0, read.FieldCount).Select(read.GetName).ToArray());
        Assert.AreEqual(version == SchemaVersion.Described ? typeof(long) : typeof(object), read.GetFieldType(0));

        Assert.IsTrue(read.Read());
        Assert.AreEqual(1L, read.GetInt64(0));
        Assert.AreEqual("in chunks", read.GetString(1));
        Assert.AreEqual(1.5, read.GetDouble(2));

        Assert.IsTrue(read.Read());
        Assert.AreEqual(2L, read.GetInt64(0));
        Assert.IsTrue(read.IsDBNull(1));
        Assert.AreEqual(2.5, read.GetFieldValue<double>(2));

        Assert.IsTrue(read.Read());
        for (int t = 0; t < 3; t++)
          Assert.IsTrue(read.IsDBNull(t), "nil: all NULL");

        Assert.IsTrue(read.Read());
        Assert.AreEqual(4, read.GetInt32(0), "converted to the getter's type");
        Assert.IsTrue(read.IsDBNull(1));
        Assert.AreEqual(DBNull.Value, read.GetValue(2));
        Assert.ThrowsExactly<InvalidCastException>(() => read.GetDouble(2));

        Assert.IsTrue(read.Read());
        Assert.AreEqual(5L, Convert.ToInt64(read.GetValue(0)));
        Assert.AreEqual("five", read.GetValue(1));
        Assert.IsTrue(read.IsDBNull(2));

        Assert.IsFalse(read.Read());
        Assert.IsFalse(read.NextResult());
      }
    }

    /// <summary>
    /// The async reads of a stream that gives a few bytes per read and refuses synchronous reads: the same values as the synchronous reads.
    /// </summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    [DataRow(4096)]
    public async Task Async_TrickleStream_SameAsSynchronous(int chunk)
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { SchemaVersion = SchemaVersion.Described });
      List<AllTypesRow> rows = Data.AllTypesRows();
      MemoryStream stream = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true))
      {
        writer.WriteTable(Data.AllTypes(rows), "First");
        writer.WriteTable(Data.AllTypes(rows), "Second");
      }
      byte[] dump = stream.ToArray();

      List<object[]> expected = new List<object[]>();
      using (MsgPackDataReader read = new MsgPackDataReader(new MsgPackStreamReader(new MemoryStream(dump), serializer)))
      {
        do
        {
          while (read.Read())
          {
            object[] values = new object[read.FieldCount];
            read.GetValues(values);
            expected.Add(values);
          }
        }
        while (read.NextResult());
      }

      List<object[]> actual = new List<object[]>();
      using (MsgPackDataReader read = await MsgPackDataReader.OpenAsync(new MsgPackStreamReader(new AsyncTrickleStream(dump, chunk), serializer)))
      {
        do
        {
          while (await read.ReadAsync())
          {
            object[] values = new object[read.FieldCount];
            read.GetValues(values);
            actual.Add(values);
          }
        }
        while (await read.NextResultAsync());
      }

      Assert.AreEqual(2 * rows.Count, expected.Count);
      Assert.AreEqual(expected.Count, actual.Count);
      for (int r = 0; r < expected.Count; r++)
        for (int t = 0; t < expected[r].Length; t++)
        {
          if (expected[r][t] is byte[] bytes)
            CollectionAssert.AreEqual(bytes, (byte[])actual[r][t]);
          else
            Assert.AreEqual(expected[r][t], actual[r][t], "row " + r + " column " + t);
        }
    }

    /// <summary>
    /// At most <c>chunk</c> bytes per read, asynchronous reads only.
    /// </summary>
    private sealed class AsyncTrickleStream : Stream
    {
      private readonly byte[] _data;
      private readonly int _chunk;
      private int _pos;

      public AsyncTrickleStream(byte[] data, int chunk)
      {
        _data = data;
        _chunk = chunk;
      }

      public override int Read(byte[] buffer, int offset, int count) { throw new InvalidOperationException("A synchronous read."); }

      public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
        await Task.Yield();
        int n = Math.Min(Math.Min(count, _chunk), _data.Length - _pos);
        Buffer.BlockCopy(_data, _pos, buffer, offset, n);
        _pos += n;
        return n;
      }

      public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
      {
        byte[] bytes = new byte[buffer.Length];
        return new ValueTask<int>(ReadAsync(bytes, 0, bytes.Length, cancellationToken).ContinueWith(t => { bytes.AsSpan(0, t.Result).CopyTo(buffer.Span); return t.Result; }, cancellationToken));
      }

      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return false; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { throw new NotSupportedException(); } }
      public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override void Flush() { }
      public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
  }
}
