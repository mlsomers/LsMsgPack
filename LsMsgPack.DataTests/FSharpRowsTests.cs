using LsMsgPackDebug;
using LsMsgPack.Data;
using LsMsgPack.Streaming;
using LsMsgPackFSharpTestTypes;
using Microsoft.Data.Sqlite;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace LsMsgPack.DataTests
{
  /// <summary>
  /// F# records as rows (docs/FSharp.md): created by their constructor (or set, <c>[&lt;CLIMutable&gt;]</c>), a NULL is None, a value Some, unions from their columns
  /// (a fieldless case by its name, a single-case union by its field). F# records compare by value, so whole rows are compared.
  /// </summary>
  [TestClass]
  public class FSharpRowsTests
  {
    private static readonly string[] Names = { "Id", "Customer", "Note", "Discount", "Paid", "Total", "Colour", "Mail", "OrderId", "Created" };

    private static List<FsInvoiceRow> Rows()
    {
      List<FsInvoiceRow> rows = new List<FsInvoiceRow>();
      for (int t = 0; t < 100; t++)
      {
        bool nulls = t % 3 == 1;
        rows.Add(new FsInvoiceRow(t + 1, "Customer " + t,
          nulls ? null : FSharpOption<string>.Some("note " + t),
          nulls ? null : FSharpOption<decimal>.Some(t * 0.25m),
          nulls ? FSharpValueOption<bool>.ValueNone : FSharpValueOption<bool>.NewValueSome(t % 2 == 0),
          t * 1.5,
          t % 3 == 0 ? FsColour.Red : t % 3 == 1 ? FsColour.Green : FsColour.Blue,
          FsEmail.NewFsEmail("c" + t + "@example.com"),
          FsOrderId.NewFsOrderId(new Guid(t + 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)),
          new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(t)));
      }
      return rows;
    }

    /// <summary>
    /// The columns as a provider has them: options as NULL or their value, unions as their name or field.
    /// </summary>
    private static ColumnarReader Reader(List<FsInvoiceRow> rows, bool throwing, string[] names = null)
    {
      int count = rows.Count;
      int[] id = new int[count];
      string[] customer = new string[count], note = new string[count], colour = new string[count], mail = new string[count];
      decimal[] discount = new decimal[count];
      bool[] paid = new bool[count];
      bool[] noNote = new bool[count], noDiscount = new bool[count], noPaid = new bool[count];
      double[] total = new double[count];
      Guid[] orderId = new Guid[count];
      DateTime[] created = new DateTime[count];
      for (int t = 0; t < count; t++)
      {
        FsInvoiceRow row = rows[t];
        id[t] = row.Id;
        customer[t] = row.Customer;
        noNote[t] = row.Note is null;
        note[t] = row.Note?.Value;
        noDiscount[t] = row.Discount is null;
        discount[t] = row.Discount is null ? 0 : row.Discount.Value;
        noPaid[t] = row.Paid.IsValueNone;
        paid[t] = row.Paid.IsValueSome && row.Paid.Value;
        total[t] = row.Total;
        colour[t] = row.Colour.IsRed ? "Red" : row.Colour.IsGreen ? "Green" : "Blue";
        mail[t] = row.Mail.Item;
        orderId[t] = row.OrderId.Item;
        created[t] = row.Created;
      }
      ColumnarReader.Column[] columns =
      {
        new ColumnarReader.Column<int>(id),
        new ColumnarReader.Column<string>(customer),
        new ColumnarReader.Column<string>(note, noNote),
        new ColumnarReader.Column<decimal>(discount, noDiscount),
        new ColumnarReader.Column<bool>(paid, noPaid),
        new ColumnarReader.Column<double>(total),
        new ColumnarReader.Column<string>(colour),
        new ColumnarReader.Column<string>(mail),
        new ColumnarReader.Column<Guid>(orderId),
        new ColumnarReader.Column<DateTime>(created),
      };
      names = names ?? Names;
      return throwing ? new ThrowingColumnarReader(names, columns, count) : new ColumnarReader(names, columns, count);
    }

    private static List<FsMutableRow> Mutable(List<FsInvoiceRow> rows)
    {
      List<FsMutableRow> result = new List<FsMutableRow>(rows.Count);
      for (int t = 0; t < rows.Count; t++)
      {
        FsInvoiceRow r = rows[t];
        result.Add(new FsMutableRow(r.Id, r.Customer, r.Note, r.Discount, r.Paid, r.Total, r.Colour, r.Mail, r.OrderId, r.Created));
      }
      return result;
    }

    private static void AssertRows<T>(List<T> expected, List<T> actual, string message = null)
    {
      Assert.AreEqual(expected.Count, actual.Count, message);
      for (int t = 0; t < expected.Count; t++)
        Assert.AreEqual(expected[t], actual[t], message + " row " + t);
    }

    [TestMethod]
    public void ReadRows_Records()
    {
      List<FsInvoiceRow> rows = Rows();
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      AssertRows(rows, serializer.ReadRows<FsInvoiceRow>(Reader(rows, false)));
      AssertRows(rows, serializer.ReadRows<FsInvoiceRow>(Reader(rows, true)));
      AssertRows(Mutable(rows), serializer.ReadRows<FsMutableRow>(Reader(rows, false)));
      AssertRows(Mutable(rows), serializer.ReadRows<FsMutableRow>(Reader(rows, true)));
    }

    /// <summary>
    /// F# records with a naming policy: the columns named as the policy names the record's fields.
    /// </summary>
    [TestMethod]
    public void ReadRows_Records_NamingPolicy()
    {
      List<FsInvoiceRow> rows = Rows();
      string[] snake = { "id", "customer", "note", "discount", "paid", "total", "colour", "mail", "order_id", "created" };
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower });
      AssertRows(rows, serializer.ReadRows<FsInvoiceRow>(Reader(rows, false, snake)));
      AssertRows(Mutable(rows), serializer.ReadRows<FsMutableRow>(Reader(rows, true, snake)));
      AssertRows(rows, serializer.ReadRows<FsInvoiceRow>(Reader(rows, false)), "the .NET names");
    }

    /// <summary>
    /// SQLite's storage classes (INTEGER for int and bool, TEXT for decimal, Guid and DateTime, REAL): the options and unions converted from them.
    /// </summary>
    [TestMethod]
    public void ReadRows_Records_Sqlite()
    {
      List<FsInvoiceRow> rows = Rows();
      using (SqliteConnection connection = new SqliteConnection("Data Source=:memory:"))
      {
        connection.Open();
        Sqlite.Execute(connection, "CREATE TABLE Rows (Id INTEGER NOT NULL, Customer TEXT NOT NULL, Note TEXT, Discount TEXT, Paid INTEGER, Total REAL NOT NULL, Colour TEXT NOT NULL, Mail TEXT NOT NULL, OrderId TEXT NOT NULL, Created TEXT NOT NULL)");
        for (int t = 0; t < rows.Count; t++)
        {
          FsInvoiceRow row = rows[t];
          Sqlite.Execute(connection, "INSERT INTO Rows VALUES (@id, @customer, @note, @discount, @paid, @total, @colour, @mail, @orderId, @created)",
            ("@id", row.Id), ("@customer", row.Customer), ("@note", row.Note?.Value), ("@discount", row.Discount is null ? null : (object)row.Discount.Value),
            ("@paid", row.Paid.IsValueSome ? (object)row.Paid.Value : null), ("@total", row.Total), ("@colour", row.Colour.IsRed ? "Red" : row.Colour.IsGreen ? "Green" : "Blue"),
            ("@mail", row.Mail.Item), ("@orderId", row.OrderId.Item), ("@created", row.Created));
        }
        LsMsgPackSerializer serializer = new LsMsgPackSerializer();
        using (SqliteCommand command = new SqliteCommand("SELECT * FROM Rows ORDER BY Id", connection))
        {
          using (SqliteDataReader reader = command.ExecuteReader())
            AssertRows(rows, serializer.ReadRows<FsInvoiceRow>(reader));
          using (SqliteDataReader reader = command.ExecuteReader())
            AssertRows(Mutable(rows), serializer.ReadRows<FsMutableRow>(reader));
        }
      }
    }

    /// <summary>
    /// The rows written by SerializeRows (Nullable columns, nil for NULL) read as F# records by both serializers, and the stream of a table dump.
    /// </summary>
    [TestMethod]
    public void SerializeRows_ReadAsRecords()
    {
      List<FsInvoiceRow> rows = Rows();
      LsMsgPackSerializer serializer = new LsMsgPackSerializer();
      byte[] data = serializer.SerializeRows<FsInvoiceRow>(Reader(rows, true));
      AssertRows(rows, serializer.Deserialize<List<FsInvoiceRow>>(data));
      AssertRows(Mutable(rows), serializer.Deserialize<List<FsMutableRow>>(data));
      AssertRows(rows, MsgPackSerializer.Deserialize<List<FsInvoiceRow>>(data));

      using (MemoryStream stream = new MemoryStream())
      {
        using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, leaveOpen: true))
          writer.WriteTable(Reader(rows, true), "rows");
        stream.Position = 0;
        using (MsgPackDataReader dump = new MsgPackDataReader(new MsgPackStreamReader(stream, serializer)))
          AssertRows(rows, serializer.ReadRows<FsInvoiceRow>(dump));
      }
    }
  }
}
