using LsMsgPack;
using LtMsgPack.Data;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace LtMsgPack.DataTests
{
  [TestClass]
  public class SerializeRowsTests
  {
    [TestMethod]
    [DynamicData(nameof(Variant.All), typeof(Variant))]
    public void SameBytesAsTheList(Variant variant)
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(variant.Lt());
      List<AllTypesRow> rows = Data.AllTypesRows();
      byte[] actual = serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows));

      CollectionAssert.AreEqual(serializer.Serialize(rows), actual, "LtMsgPack " + variant);
      CollectionAssert.AreEqual(MsgPackSerializer.Serialize(rows, variant.Ls()), actual, "LsMsgPack " + variant);

      AssertEqual(rows, serializer.Deserialize<List<AllTypesRow>>(actual));
      AssertEqual(rows, MsgPackSerializer.Deserialize<List<AllTypesRow>>(actual, variant.Ls()));
    }

    [TestMethod]
    [DynamicData(nameof(Variant.All), typeof(Variant))]
    public void NoRows_SameBytesAsAnEmptyList(Variant variant)
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(variant.Lt());
      List<AllTypesRow> rows = new List<AllTypesRow>();
      CollectionAssert.AreEqual(serializer.Serialize(rows), serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows)), variant.ToString());
    }

    [TestMethod]
    public void Stream_SameBytes_ReturnsTheCount()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer();
      List<AllTypesRow> rows = Data.AllTypesRows();
      MemoryStream target = new MemoryStream();
      Assert.AreEqual(rows.Count, serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows), target));
      CollectionAssert.AreEqual(serializer.Serialize(rows), target.ToArray());

      target = new MemoryStream();
      Assert.AreEqual(rows.Count, serializer.SerializeRows(Data.AllTypes(rows), target, nameof(AllTypesRow)));
      CollectionAssert.AreEqual(serializer.Serialize(rows), target.ToArray());
    }

    /// <summary>
    /// More rows than one chunk of the body holds (64 KiB): the chunks and the rest of the body joined, to a byte array and to a stream; the second call reuses the pooled chunks.
    /// </summary>
    [TestMethod]
    public void ManyRows_Chunked_SameBytes()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer();
      List<AllTypesRow> rows = new List<AllTypesRow>();
      byte[] expected;
      do
      {
        for (int t = 0; t < 100; t++)
          rows.AddRange(Data.AllTypesRows());
        expected = serializer.Serialize(rows);
      }
      while (expected.Length <= 3 * 64 * 1024);
      for (int call = 0; call < 2; call++)
      {
        CollectionAssert.AreEqual(expected, serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows)), "call " + call);
        MemoryStream target = new MemoryStream();
        Assert.AreEqual(rows.Count, serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows), target));
        CollectionAssert.AreEqual(expected, target.ToArray(), "stream, call " + call);
      }
    }

    [TestMethod]
    public void SecondCall_SamePlan_SameBytes()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer();
      List<AllTypesRow> rows = Data.AllTypesRows();
      byte[] first = serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows));
      CollectionAssert.AreEqual(first, serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows)));
      byte[] other = serializer.SerializeRows(Data.AllTypes(rows), "Other");
      CollectionAssert.AreNotEqual(first, other);
      CollectionAssert.AreEqual(first, serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows)));
    }

    /// <summary>
    /// Readers pair the root's class with the rows' entry whatever its name: one that resolves to another class (here <see cref="Row"/>), or to none.
    /// </summary>
    [TestMethod]
    public void EntryName_AnyName_BindsTheReadersClass()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer();
      List<AllTypesRow> rows = Data.AllTypesRows();
      foreach (string name in new[] { MsgPackDataStreams.DefaultTypeName, "NoSuchClassAnywhere" })
      {
        byte[] data = serializer.SerializeRows(Data.AllTypes(rows), name);
        AssertEqual(rows, serializer.Deserialize<List<AllTypesRow>>(data));
        AssertEqual(rows, MsgPackSerializer.Deserialize<List<AllTypesRow>>(data));
        List<Row> lt = serializer.Deserialize<List<Row>>(data);
        for (int t = 0; t < rows.Count; t++)
        {
          Assert.AreEqual(rows[t].Name, lt[t].Name);
          Assert.AreEqual(rows[t].Big, lt[t].Big);
        }
      }
    }

    [TestMethod]
    public void DuplicateColumnNames_Throw()
    {
      ColumnarReader reader = new ColumnarReader(new[] { "Id", "Id" }, new ColumnarReader.Column[] { new ColumnarReader.Column<int>(new[] { 1 }), new ColumnarReader.Column<int>(new[] { 2 }) }, 1);
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => new LtMsgPackSerializer().SerializeRows(reader));
      StringAssert.Contains(ex.Message, "\"Id\"");
    }

    [TestMethod]
    public void UnnamedColumn_Throws()
    {
      ColumnarReader reader = new ColumnarReader(new[] { "" }, new ColumnarReader.Column[] { new ColumnarReader.Column<int>(new[] { 1 }) }, 1);
      Assert.ThrowsExactly<MsgPackException>(() => new LtMsgPackSerializer().SerializeRows(reader));
    }

    /// <summary>
    /// With <see cref="MsgPackOptions.WriteSchemaReference"/> the payload refers to the schema of the columns, which is added to the store.
    /// </summary>
    [TestMethod]
    public void SchemaReference_ReadWithTheStore()
    {
      SchemaStore store = new SchemaStore();
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions() { WriteSchemaReference = true, SchemaStore = store });
      List<AllTypesRow> rows = Data.AllTypesRows();
      byte[] data = serializer.SerializeRows<AllTypesRow>(Data.AllTypes(rows));
      Assert.AreEqual(0xD8, data[0], "a reference");
      Assert.AreEqual(1, store.Count);
      AssertEqual(rows, serializer.Deserialize<List<AllTypesRow>>(data));
    }

    /// <summary>
    /// SQLite gives the field types of its storage classes (INTEGER long, REAL double, TEXT string, BLOB byte[]).
    /// </summary>
    public class SqliteRow
    {
      public long? Id { get; set; }
      public string Name { get; set; }
      public double? Score { get; set; }
      public byte[] Data { get; set; }
    }

    [TestMethod]
    [DynamicData(nameof(Variant.All), typeof(Variant))]
    public void Sqlite_SameBytesAsTheList(Variant variant)
    {
      List<SqliteRow> rows = new List<SqliteRow>()
      {
        new SqliteRow() { Id = 1, Name = "One", Score = 1.25, Data = new byte[] { 9, 8 } },
        new SqliteRow() { Id = 0, Name = "", Score = 0, Data = new byte[0] },
        new SqliteRow() { Id = 3 },
      };
      using (SqliteConnection connection = new SqliteConnection("Data Source=:memory:"))
      {
        connection.Open();
        Sqlite.Execute(connection, "CREATE TABLE Rows (Id INTEGER, Name TEXT, Score REAL, Data BLOB)");
        foreach (SqliteRow row in rows)
          Sqlite.Execute(connection, "INSERT INTO Rows VALUES (@id, @name, @score, @data)", ("@id", row.Id), ("@name", row.Name), ("@score", row.Score), ("@data", row.Data));

        LtMsgPackSerializer serializer = new LtMsgPackSerializer(variant.Lt());
        using (SqliteCommand command = new SqliteCommand("SELECT Id, Name, Score, Data FROM Rows ORDER BY rowid", connection))
        using (SqliteDataReader reader = command.ExecuteReader())
          CollectionAssert.AreEqual(serializer.Serialize(rows), serializer.SerializeRows<SqliteRow>(reader), variant.ToString());
      }
    }

    /// <summary>
    /// What a DateTime stands for as the serializer writes it: a Local value its moment, Utc and Unspecified their clock time (read back as Utc).
    /// </summary>
    internal static DateTime? Moment(DateTime? value)
    {
      if (value is null)
        return null;
      return value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    internal static void AssertEqual(List<AllTypesRow> expected, List<AllTypesRow> actual)
    {
      Assert.AreEqual(expected.Count, actual.Count);
      for (int t = 0; t < expected.Count; t++)
      {
        AllTypesRow e = expected[t], a = actual[t];
        string row = "row " + t;
        Assert.AreEqual(e.Id, a.Id, row);
        Assert.AreEqual(e.Name, a.Name, row);
        Assert.AreEqual(e.Amount, a.Amount, row);
        Assert.AreEqual(Moment(e.Created), Moment(a.Created), row);
        Assert.AreEqual(e.Key, a.Key, row);
        Assert.AreEqual(e.Active, a.Active, row);
        Assert.AreEqual(e.Score, a.Score, row);
        Assert.AreEqual(e.Big, a.Big, row);
        CollectionAssert.AreEqual(e.Data, a.Data, row);
        Assert.AreEqual(e.Small, a.Small, row);
        Assert.AreEqual(e.Ratio, a.Ratio, row);
        Assert.AreEqual(e.Tiny, a.Tiny, row);
      }
    }
  }
}
