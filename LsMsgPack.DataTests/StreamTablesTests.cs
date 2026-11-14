using LsMsgPack;
using LsMsgPack.Streaming;
using LsMsgPack.Data;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPack.DataTests
{
  /// <summary>
  /// Result sets and table dumps as streams (StreamWriterDataExtensions) and read back (MsgPackDataReader, ReadHead/ReadObject, the index).
  /// </summary>
  [TestClass]
  public class StreamTablesTests
  {
    private const string AllTables = "SELECT Id, Name, Score FROM Customers ORDER BY Id; SELECT Id, CustomerId, Amount, Note FROM Orders ORDER BY CustomerId, Id; SELECT Id, Name FROM Empty";

    public class OrderRow
    {
      public long Id { get; set; }
      public long CustomerId { get; set; }
      public double? Amount { get; set; }
      public string Note { get; set; }
    }

    private static SqliteConnection Database()
    {
      SqliteConnection connection = new SqliteConnection("Data Source=:memory:");
      connection.Open();
      DataTests.Sqlite.Execute(connection, "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT, Score REAL)");
      DataTests.Sqlite.Execute(connection, "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Amount REAL, Note TEXT)");
      DataTests.Sqlite.Execute(connection, "CREATE TABLE Empty (Id INTEGER, Name TEXT)");
      for (int t = 1; t <= 50; t++)
        DataTests.Sqlite.Execute(connection, "INSERT INTO Customers VALUES (@id, @name, @score)", ("@id", t), ("@name", "Customer " + t), ("@score", t % 7 == 0 ? null : (object)(t * 1.5)));
      for (int t = 1; t <= 2500; t++)
        DataTests.Sqlite.Execute(connection, "INSERT INTO Orders VALUES (@id, @customer, @amount, @note)", ("@id", t), ("@customer", 1 + (t * 7) % 50), ("@amount", t * 0.25), ("@note", t % 3 == 0 ? null : "Note " + t));
      return connection;
    }

    private static DbDataReader Query(SqliteConnection connection, string sql)
    {
      return new SqliteCommand(sql, connection).ExecuteReader();
    }

    /// <summary>
    /// The result sets of both readers have the same columns and values (the values of <paramref name="actual"/> as the type of <paramref name="expected"/>'s).
    /// </summary>
    private static void AssertSameResultSets(DbDataReader expected, DbDataReader actual, int resultSets, bool sameTypes)
    {
      for (int set = 0; set < resultSets; set++)
      {
        Assert.AreEqual(expected.FieldCount, actual.FieldCount, "result set " + set);
        for (int t = 0; t < expected.FieldCount; t++)
        {
          Assert.AreEqual(expected.GetName(t), actual.GetName(t));
          if (sameTypes)
            Assert.AreEqual(expected.GetFieldType(t), actual.GetFieldType(t), expected.GetName(t));
        }
        int rows = 0;
        while (expected.Read())
        {
          Assert.IsTrue(actual.Read(), "result set " + set + " row " + rows);
          for (int t = 0; t < expected.FieldCount; t++)
          {
            object value = expected.GetValue(t);
            object read = actual.GetValue(t);
            if (value is DBNull)
              Assert.IsInstanceOfType(read, typeof(DBNull), expected.GetName(t));
            else
              Assert.AreEqual(value, Convert.ChangeType(read, value.GetType()), expected.GetName(t) + " row " + rows);
          }
          rows++;
        }
        Assert.IsFalse(actual.Read());
        Assert.AreEqual(expected.NextResult(), actual.NextResult(), "after result set " + set);
      }
    }

    [TestMethod]
    [DataRow(SchemaVersion.Names)]
    [DataRow(SchemaVersion.Described)]
    public void ResultSets_ReadBackAsDataReader(SchemaVersion version)
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { SchemaVersion = version });
      using (SqliteConnection connection = Database())
      {
        MemoryStream stream = new MemoryStream();
        using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true))
        using (DbDataReader source = Query(connection, AllTables))
          Assert.AreEqual(3, writer.WriteResultSets(source));

        stream.Position = 0;
        using (MsgPackDataReader read = new MsgPackDataReader(new MsgPackStreamReader(stream, serializer)))
        using (DbDataReader expected = Query(connection, AllTables))
        {
          Assert.AreEqual(MsgPackDataStreams.DefaultTypeName, read.TableName);
          if (version == SchemaVersion.Described)
            Assert.AreEqual(typeof(long), read.GetFieldType(0), "the kinds of version 2");
          else
            Assert.AreEqual(typeof(object), read.GetFieldType(0), "version 1 has no types");
          AssertSameResultSets(expected, read, 3, version == SchemaVersion.Described);
        }
      }
    }

    [TestMethod]
    public void EmptyResultSet_KeepsItsColumns()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { SchemaVersion = SchemaVersion.Described });
      using (SqliteConnection connection = Database())
      {
        MemoryStream stream = new MemoryStream();
        using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true))
        using (DbDataReader source = Query(connection, "SELECT Id, Name FROM Empty"))
          Assert.AreEqual(0, writer.WriteTable(source, "Empty"));

        stream.Position = 0;
        using (MsgPackDataReader read = new MsgPackDataReader(new MsgPackStreamReader(stream, serializer)))
        {
          Assert.AreEqual("Empty", read.TableName);
          Assert.AreEqual(2, read.FieldCount);
          Assert.AreEqual("Name", read.GetName(1));
          Assert.IsFalse(read.HasRows);
          Assert.IsFalse(read.Read());
          Assert.IsFalse(read.NextResult());
        }
      }
    }

    private static MemoryStream Dump(SqliteConnection connection, LsMsgPackSerializer serializer, MsgPackStreamIndexOptions index)
    {
      MemoryStream stream = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true, index: index))
      {
        using (DbDataReader customers = Query(connection, "SELECT Id, Name, Score FROM Customers ORDER BY Id"))
          writer.WriteTable(customers, "Customers");
        using (DbDataReader orders = Query(connection, "SELECT Id, CustomerId, Amount, Note FROM Orders ORDER BY CustomerId, Id"))
          writer.WriteTable(orders, "Orders", new MsgPackTableOptions() { PointColumn = "CustomerId" });
        using (DbDataReader empty = Query(connection, "SELECT Id, Name FROM Empty"))
          writer.WriteTable(empty, "Empty");
      }
      stream.Position = 0;
      return stream;
    }

    [TestMethod]
    public void TableDump_Index_TablesColumnsAndPoints()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      using (SqliteConnection connection = Database())
      using (MsgPackStreamReader reader = new MsgPackStreamReader(Dump(connection, serializer, new MsgPackStreamIndexOptions() { PointInterval = 100 }), serializer))
      {
        List<MsgPackStreamEntry> entries = reader.ReadIndexes().SelectMany(i => i.Entries).ToList();
        CollectionAssert.AreEqual(new object[] { "Customers", "Orders", "Empty" }, entries.Select(e => e.Identifier).ToArray());
        Assert.IsTrue(entries.All(e => e.ContentType == MsgPackDataStreams.ContentType));
        CollectionAssert.AreEqual(new long?[] { 50, 2500, 0 }, entries.Select(e => e.ElementCount).ToArray());

        IList columns = (IList)entries[1].Metadata[MsgPackDataStreams.ColumnsKey];
        Assert.AreEqual(4, columns.Count);
        CollectionAssert.AreEqual(new object[] { "Amount", "System.Double", "REAL" }, ((IList)columns[2]).Cast<object>().ToArray());

        // Points: every 100 rows and where the customer changes (the key)
        List<MsgPackStreamIndexPoint> points = entries[1].Points;
        Assert.IsTrue(points.Count >= 50, points.Count.ToString());
        MsgPackStreamIndexPoint customer42 = points.First(p => p.Key != null && Convert.ToInt64(p.Key) == 42); // integers are read back in their smallest type
        reader.Seek(entries[1]);
        reader.ReadHead<List<OrderRow>>();
        reader.Read(); // ArrayStart
        reader.Seek(entries[1], customer42);
        OrderRow first42 = reader.ReadObject<OrderRow>();
        Assert.AreEqual(42, first42.CustomerId);

        List<OrderRow> all;
        using (DbDataReader orders = Query(connection, "SELECT Id, CustomerId, Amount, Note FROM Orders ORDER BY CustomerId, Id"))
          all = new LsMsgPackSerializer().ReadRows<OrderRow>(orders);
        Assert.AreEqual(all.First(o => o.CustomerId == 42).Id, first42.Id);

        reader.Seek(entries[1]);
        reader.ReadHead<List<OrderRow>>();
        reader.Read();
        reader.SeekElement(entries[1], 1234);
        OrderRow row = reader.ReadObject<OrderRow>();
        Assert.AreEqual(all[1234].Id, row.Id);
        Assert.AreEqual(all[1234].Note, row.Note);
      }
    }

    [TestMethod]
    public void TableDump_ReadAtEntry_ExactTypes_AndSequentially()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      using (SqliteConnection connection = Database())
      {
        MemoryStream stream = Dump(connection, serializer, new MsgPackStreamIndexOptions());
        using (MsgPackStreamReader reader = new MsgPackStreamReader(stream, serializer, leaveOpen: true))
        {
          MsgPackStreamEntry orders = reader.ReadIndexes().SelectMany(i => i.Entries).Single(e => "Orders".Equals(e.Identifier));
          using (MsgPackDataReader read = new MsgPackDataReader(reader, orders, leaveOpen: true))
          using (DbDataReader expected = Query(connection, "SELECT Id, CustomerId, Amount, Note FROM Orders ORDER BY CustomerId, Id"))
          {
            Assert.AreEqual("Orders", read.TableName);
            Assert.AreEqual("INTEGER", read.GetDataTypeName(0));
            AssertSameResultSets(expected, read, 1, true);
          }
        }

        stream.Position = 0; // the whole dump as result sets, the index at its end is passed
        using (MsgPackDataReader read = new MsgPackDataReader(new MsgPackStreamReader(stream, serializer)))
        using (DbDataReader expected = Query(connection, AllTables))
        {
          Assert.AreEqual("Customers", read.TableName);
          AssertSameResultSets(expected, read, 3, false);
        }
      }
    }

    /// <summary>
    /// <see cref="TableDump_ReadAtEntry_ExactTypes_AndSequentially"/> with the async methods (ReadIndexesAsync, OpenAsync at the entry, ReadAsync), from a stream that refuses synchronous reads.
    /// </summary>
    [TestMethod]
    public async Task Async_ReadAtEntry()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      using (SqliteConnection connection = Database())
      {
        byte[] dump = Dump(connection, serializer, new MsgPackStreamIndexOptions() { PointInterval = 100 }).ToArray();
        using (MsgPackStreamReader reader = new MsgPackStreamReader(new AsyncSeekStream(dump), serializer))
        {
          MsgPackStreamEntry orders = (await reader.ReadIndexesAsync()).SelectMany(i => i.Entries).Single(e => "Orders".Equals(e.Identifier));
          using (MsgPackDataReader read = await MsgPackDataReader.OpenAsync(reader, orders, leaveOpen: true))
          using (DbDataReader expected = Query(connection, "SELECT Id, CustomerId, Amount, Note FROM Orders ORDER BY CustomerId, Id"))
          {
            Assert.AreEqual("Orders", read.TableName);
            Assert.AreEqual("INTEGER", read.GetDataTypeName(0));
            int rows = 0;
            while (expected.Read())
            {
              Assert.IsTrue(await read.ReadAsync());
              for (int t = 0; t < expected.FieldCount; t++)
                Assert.AreEqual(expected.GetValue(t), read.GetValue(t), "row " + rows + " column " + t);
              rows++;
            }
            Assert.AreEqual(2500, rows);
            Assert.IsFalse(await read.ReadAsync());
            Assert.IsFalse(await read.NextResultAsync(), "one table");
          }

          // into the rows of the table
          reader.Seek(orders);
          await reader.ReadHeadAsync<List<OrderRow>>();
          await reader.ReadAsync(); // ArrayStart
          await reader.SeekElementAsync(orders, 1234);
          OrderRow row = await reader.ReadObjectAsync<OrderRow>();
          using (DbDataReader all = Query(connection, "SELECT Id, CustomerId, Amount, Note FROM Orders ORDER BY CustomerId, Id"))
            Assert.AreEqual(new LsMsgPackSerializer().ReadRows<OrderRow>(all)[1234].Id, row.Id);
        }
      }
    }

    /// <summary>
    /// Seeks, refuses synchronous reads, at most 7 bytes per read.
    /// </summary>
    private sealed class AsyncSeekStream : Stream
    {
      private readonly MemoryStream _inner;
      public AsyncSeekStream(byte[] bytes) { _inner = new MemoryStream(bytes); }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return true; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { return _inner.Length; } }
      public override long Position { get { return _inner.Position; } set { _inner.Position = value; } }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count) { throw new InvalidOperationException("A synchronous read."); }
      public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
      {
        await Task.Yield();
        return _inner.Read(buffer, offset, Math.Min(count, 7));
      }
      public override long Seek(long offset, SeekOrigin origin) { return _inner.Seek(offset, origin); }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    /// <summary>
    /// A dump read back as a data reader, inserted into another table (as SqlBulkCopy.WriteToServer(reader) would): the same rows.
    /// </summary>
    [TestMethod]
    public void Restore_IntoAnotherTable()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { SchemaVersion = SchemaVersion.Described });
      using (SqliteConnection connection = Database())
      {
        MemoryStream stream = Dump(connection, serializer, null);
        DataTests.Sqlite.Execute(connection, "CREATE TABLE Restored (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Amount REAL, Note TEXT)");
        using (MsgPackDataReader read = new MsgPackDataReader(new MsgPackStreamReader(stream, serializer)))
        {
          Assert.IsTrue(read.NextResult(), "Orders is the second table");
          Assert.AreEqual("Orders", read.TableName);
          using (SqliteTransaction transaction = connection.BeginTransaction())
          using (SqliteCommand insert = new SqliteCommand("INSERT INTO Restored VALUES (@p0, @p1, @p2, @p3)", connection, transaction))
          {
            for (int t = 0; t < read.FieldCount; t++)
              insert.Parameters.Add(new SqliteParameter("@p" + t, null));
            object[] values = new object[read.FieldCount];
            while (read.Read())
            {
              read.GetValues(values);
              for (int t = 0; t < values.Length; t++)
                insert.Parameters[t].Value = values[t];
              insert.ExecuteNonQuery();
            }
            transaction.Commit();
          }
        }

        using (DbDataReader expected = Query(connection, "SELECT Id, CustomerId, Amount, Note FROM Orders ORDER BY Id"))
        using (DbDataReader restored = Query(connection, "SELECT Id, CustomerId, Amount, Note FROM Restored ORDER BY Id"))
          AssertSameResultSets(expected, restored, 1, true);
      }
    }

    [TestMethod]
    public async Task Async_ResultSets()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { SchemaVersion = SchemaVersion.Described });
      using (SqliteConnection connection = Database())
      {
        MemoryStream stream = new MemoryStream();
        MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true, asynchronous: true);
        writer.FlushThreshold = 1024;
        using (DbDataReader source = Query(connection, AllTables))
          Assert.AreEqual(3, await writer.WriteResultSetsAsync(source, new[] { "Customers", "Orders" }));
        await writer.DisposeAsync();

        stream.Position = 0;
        using (MsgPackDataReader read = await MsgPackDataReader.OpenAsync(new MsgPackStreamReader(stream, serializer)))
        using (DbDataReader expected = Query(connection, AllTables))
        {
          Assert.AreEqual("Customers", read.TableName);
          int count = 0;
          while (await read.ReadAsync())
            count++;
          Assert.AreEqual(50, count);
          Assert.IsTrue(await read.NextResultAsync());
          Assert.AreEqual("Orders", read.TableName);
          Assert.IsTrue(await read.ReadAsync());
          Assert.IsTrue(await read.NextResultAsync(), "the rest of Orders is passed");
          Assert.AreEqual(MsgPackDataStreams.DefaultTypeName, read.TableName, "the third has no name");
          Assert.IsFalse(await read.ReadAsync());
          Assert.IsFalse(await read.NextResultAsync());
        }
      }
    }

    /// <summary>
    /// Every column type of AllTypes through a stream: read into its class (ReadHead + ReadObject, and the whole table with ReadObject), and as a data reader with the exact types of the index.
    /// </summary>
    [TestMethod]
    public void AllTypes_ThroughAStream()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      List<AllTypesRow> rows = Data.AllTypesRows();
      MemoryStream stream = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true, index: new MsgPackStreamIndexOptions()))
      {
        writer.WriteTable(Data.AllTypes(rows), "AllTypes");
        writer.WriteTable(Data.AllTypes(rows), "Again");
      }

      stream.Position = 0;
      using (MsgPackStreamReader reader = new MsgPackStreamReader(stream, serializer, leaveOpen: true))
      {
        reader.ReadHead<List<AllTypesRow>>();
        reader.Read();
        List<AllTypesRow> read = new List<AllTypesRow>();
        while (!reader.AtEnd)
          read.Add(reader.ReadObject<AllTypesRow>());
        reader.Read();
        SerializeRowsTests.AssertEqual(rows, read);
        SerializeRowsTests.AssertEqual(rows, reader.ReadObject<List<AllTypesRow>>()); // head and body at once

        MsgPackStreamEntry entry = reader.ReadIndexes().SelectMany(i => i.Entries).First();
        using (MsgPackDataReader data = new MsgPackDataReader(reader, entry, leaveOpen: true))
        {
          Type[] types = Enumerable.Range(0, data.FieldCount).Select(data.GetFieldType).ToArray();
          CollectionAssert.AreEqual(new[] { typeof(int), typeof(string), typeof(decimal), typeof(DateTime), typeof(Guid), typeof(bool), typeof(double), typeof(long), typeof(byte[]), typeof(short), typeof(float), typeof(byte) }, types);
          List<AllTypesRow> mapped = serializer.ReadRows<AllTypesRow>(data);
          SerializeRowsTests.AssertEqual(rows, mapped);
        }
      }
    }

    /// <summary>
    /// The source fails in the middle of a row of the second table: what was written of the row is taken back, the index marks the table incomplete, the first table is read through the index.
    /// </summary>
    [TestMethod]
    public void SourceFailsInARow_TableIncomplete_EarlierTablesRead()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      List<AllTypesRow> rows = Data.AllTypesRows();
      MemoryStream stream = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true, index: new MsgPackStreamIndexOptions()))
      {
        writer.WriteTable(Data.AllTypes(rows), "First");
        ColumnarReader failing = Data.AllTypes(rows);
        failing.FailAtRow = 3; // row 2 is all DBNull: no getter is called
        failing.FailFromColumn = 5;
        Assert.ThrowsExactly<IOException>(() => writer.WriteTable(failing, "Second"));
        Assert.AreEqual(1, writer.Depth, "the rows' array stays open");
      }

      stream.Position = 0;
      using (MsgPackStreamReader reader = new MsgPackStreamReader(stream, serializer))
      {
        IReadOnlyList<MsgPackStreamIndex> indexes = reader.ReadIndexes();
        Assert.AreEqual(MsgPackStreamIndexStatus.Interrupted, indexes[indexes.Count - 1].Status);
        List<MsgPackStreamEntry> entries = indexes.SelectMany(i => i.Entries).ToList();
        Assert.IsFalse(entries[0].Incomplete);
        Assert.IsTrue(entries[1].Incomplete);
        Assert.AreEqual(3L, entries[1].ElementCount, "the rows before the failing one");

        using (MsgPackDataReader data = new MsgPackDataReader(reader, entries[0], leaveOpen: true))
          SerializeRowsTests.AssertEqual(rows, serializer.ReadRows<AllTypesRow>(data));

        reader.Seek(entries[1]); // the complete rows of the incomplete table
        reader.ReadHead<List<AllTypesRow>>();
        reader.Read();
        List<AllTypesRow> before = new List<AllTypesRow> { reader.ReadObject<AllTypesRow>(), reader.ReadObject<AllTypesRow>(), reader.ReadObject<AllTypesRow>() };
        SerializeRowsTests.AssertEqual(rows.Take(3).ToList(), before);
      }
    }
  }
}
