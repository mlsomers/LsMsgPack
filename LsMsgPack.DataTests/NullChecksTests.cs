using LsMsgPack.Data;
using LsMsgPack.Streaming;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;

namespace LsMsgPack.DataTests
{
  /// <summary>
  /// After the first rows, columns without a NULL so far are read without IsDBNull when the reader's getters throw for a NULL: a NULL that comes later is still nil (found by the getter's exception).
  /// </summary>
  [TestClass]
  public class NullChecksTests
  {
    private const int RowCount = 200;

    /// <summary>
    /// Values in every column up to row 100 (past the rows that are always checked), then NULLs in single columns, and a row of NULLs at the end.
    /// </summary>
    private static List<AllTypesRow> LateNulls()
    {
      List<AllTypesRow> rows = new List<AllTypesRow>(RowCount);
      for (int t = 0; t < RowCount; t++)
        rows.Add(t == RowCount - 1 ? new AllTypesRow() : Data.AllTypesRows()[t % 2 == 0 ? 0 : 3]);
      rows[100].Id = null;
      rows[150].Name = null;
      rows[151].Data = null;
      rows[160].Amount = null;
      rows[170].Key = null;
      return rows;
    }

    /// <summary>
    /// A ThrowingColumnarReader sees a NULL in its first rows: the add-on learns that its getters throw.
    /// </summary>
    private static void Teach()
    {
      new LsMsgPackSerializer().SerializeRows<AllTypesRow>(Data.AllTypes(Data.AllTypesRows(), true));
    }

    [TestMethod]
    [DynamicData(nameof(Variant.All), typeof(Variant))]
    public void ThrowingGetters_LateNulls_SameBytesAsTheList(Variant variant)
    {
      Teach();
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(variant.Ls()); // its own plans: no column had a NULL yet
      List<AllTypesRow> rows = LateNulls();
      byte[] expected = serializer.Serialize(rows);

      ThrowingColumnarReader reader = (ThrowingColumnarReader)Data.AllTypes(rows, true);
      CollectionAssert.AreEqual(expected, serializer.SerializeRows<AllTypesRow>(reader), "first call");
      Assert.IsTrue(reader.IsDBNullCalls < 1100, "IsDBNull after the first rows only for the columns that had a NULL (11 columns, not byte[]: 2200 when every one is checked): " + reader.IsDBNullCalls);
      CollectionAssert.AreEqual(expected, serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows, true)), "second call: the columns with NULLs are checked");
    }

    [TestMethod]
    public void ThrowingGetters_LateNulls_ReadRows()
    {
      Teach();
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      List<AllTypesRow> rows = LateNulls();
      ThrowingColumnarReader reader = (ThrowingColumnarReader)Data.AllTypes(rows, true);
      SerializeRowsTests.AssertEqual(rows, serializer.ReadRows<AllTypesRow>(reader));
      Assert.IsTrue(reader.IsDBNullCalls < 1100, "IsDBNull calls (2200 when every column is checked): " + reader.IsDBNullCalls);
      SerializeRowsTests.AssertEqual(rows, serializer.ReadRows<AllTypesRow>(Data.AllTypes(rows, true)));
    }

    [TestMethod]
    public void ThrowingGetters_LateNulls_WriteTable()
    {
      Teach();
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      List<AllTypesRow> rows = LateNulls();
      MemoryStream stream = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true))
        writer.WriteTable(Data.AllTypes(rows, true), "AllTypes");

      stream.Position = 0;
      using (MsgPackStreamReader reader = new MsgPackStreamReader(stream, serializer))
        SerializeRowsTests.AssertEqual(rows, reader.ReadObject<List<AllTypesRow>>());
    }

    /// <summary>
    /// A reader whose getters give a value for a NULL (here 0 or null) is checked on every row.
    /// </summary>
    [TestMethod]
    public void SilentGetters_LateNulls_SameBytesAsTheList()
    {
      List<AllTypesRow> rows = LateNulls();
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      new LsMsgPackSerializer().SerializeRows<AllTypesRow>(Data.AllTypes(Data.AllTypesRows())); // a NULL in the first rows: learns that the getters do not throw
      CollectionAssert.AreEqual(serializer.Serialize(rows), serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows)));
      SerializeRowsTests.AssertEqual(rows, serializer.ReadRows<AllTypesRow>(Data.AllTypes(rows)));
    }

    public class CustomerOrder
    {
      public long Id { get; set; }
      public string Name { get; set; }
      public long? OrderId { get; set; }
      public double? Amount { get; set; }
      public string Note { get; set; }
    }

    /// <summary>
    /// SQLite's metadata says NOT NULL for the columns of the outer side of a join (the table's declaration), and they are NULL from row 100 on: still nil.
    /// </summary>
    [TestMethod]
    public void Sqlite_LeftJoin_NotNullColumnsWithNulls()
    {
      using (SqliteConnection connection = new SqliteConnection("Data Source=:memory:"))
      {
        connection.Open();
        Sqlite.Execute(connection, "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)");
        Sqlite.Execute(connection, "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Amount REAL NOT NULL, Note TEXT NOT NULL)");
        for (int t = 1; t <= RowCount; t++)
        {
          Sqlite.Execute(connection, "INSERT INTO Customers VALUES (@id, @name)", ("@id", t), ("@name", "Customer " + t));
          if (t <= 100)
            Sqlite.Execute(connection, "INSERT INTO Orders VALUES (@id, @customer, @amount, 'note')", ("@id", 1000 + t), ("@customer", t), ("@amount", t * 1.5));
        }
        const string sql = "SELECT c.Id, c.Name, o.Id AS OrderId, o.Amount, o.Note FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id ORDER BY c.Id";

        // Twice each, with a serializer of its own (its own plans): the first call may still learn the provider, the second reads without IsDBNull after the first rows
        for (int pass = 0; pass < 2; pass++)
        {
          LsMsgPackSerializer serializer = new LsMsgPackSerializer();
          using (SqliteCommand command = new SqliteCommand(sql, connection))
          using (SqliteDataReader reader = command.ExecuteReader())
            AssertOrders(serializer.ReadRows<CustomerOrder>(reader));

          serializer = new LsMsgPackSerializer();
          using (SqliteCommand command = new SqliteCommand(sql, connection))
          using (SqliteDataReader reader = command.ExecuteReader())
            AssertOrders(serializer.Deserialize<List<CustomerOrder>>(serializer.SerializeRows<CustomerOrder>(reader)));

          serializer = new LsMsgPackSerializer();
          MemoryStream stream = new MemoryStream();
          using (SqliteCommand command = new SqliteCommand(sql, connection))
          using (SqliteDataReader reader = command.ExecuteReader())
          using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true))
            writer.WriteTable(reader, "Orders");
          stream.Position = 0;
          using (MsgPackStreamReader reader = new MsgPackStreamReader(stream, serializer))
            AssertOrders(reader.ReadObject<List<CustomerOrder>>());
        }
      }
    }

    private static void AssertOrders(List<CustomerOrder> read)
    {
      Assert.AreEqual(RowCount, read.Count);
      for (int t = 0; t < RowCount; t++)
      {
        CustomerOrder row = read[t];
        Assert.AreEqual(t + 1, row.Id);
        Assert.AreEqual("Customer " + (t + 1), row.Name);
        if (t < 100)
        {
          Assert.AreEqual(1001 + t, row.OrderId);
          Assert.AreEqual((t + 1) * 1.5, row.Amount);
          Assert.AreEqual("note", row.Note);
        }
        else
        {
          Assert.IsNull(row.OrderId, "row " + t);
          Assert.IsNull(row.Amount, "row " + t);
          Assert.IsNull(row.Note, "row " + t);
        }
      }
    }
  }
}
