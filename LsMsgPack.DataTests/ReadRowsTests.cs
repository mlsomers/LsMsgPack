using Dapper;
using LsMsgPack.Data;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace LsMsgPack.DataTests
{
  [TestClass]
  public class ReadRowsTests
  {
    [TestMethod]
    public void AllTypes_ReadBack()
    {
      List<AllTypesRow> rows = Data.AllTypesRows();
      List<AllTypesRow> read = new LsMsgPackSerializer().ReadRows<AllTypesRow>(Data.AllTypes(rows));
      SerializeRowsTests.AssertEqual(rows, read);
    }

    [TestMethod]
    public void Invoices_SameAsDapper()
    {
      List<InvoiceRow> rows = Data.Invoices(200);
      List<InvoiceRow> dapper = Data.InvoiceReader(rows).Parse<InvoiceRow>().ToList();
      List<InvoiceRow> read = new LsMsgPackSerializer().ReadRows<InvoiceRow>(Data.InvoiceReader(rows));
      AssertEqual(dapper, read);
      AssertEqual(rows, new LsMsgPackSerializer().EnumerateRows<InvoiceRow>(Data.InvoiceReader(rows)).ToList());
    }

    [TestMethod]
    public void Names_Attributes_Conversions()
    {
      string[] names = { "id", "customer_name", "first_name", "COUNT", "Comment", "Status", "Extra", "Total", "Unknown" };
      ColumnarReader.Column[] columns =
      {
        new ColumnarReader.Column<int>(new[] { 7, 8 }),
        new ColumnarReader.Column<string>(new[] { "Ann", "Bob" }),
        new ColumnarReader.Column<string>(new[] { "A", "B" }),
        new ColumnarReader.Column<int>(new[] { 0, 3 }, new[] { false, true }),
        new ColumnarReader.Column<string>(new[] { "set", null }),
        new ColumnarReader.Column<int>(new[] { 2, 1 }),
        new ColumnarReader.Column<string>(new[] { "x", "y" }),
        new ColumnarReader.Column<int>(new[] { 100, -1 }),
        new ColumnarReader.Column<string>(new[] { "?", "?" }),
      };
      List<MappedRow> read = new LsMsgPackSerializer().ReadRows<MappedRow>(new ColumnarReader(names, columns, 2));

      Assert.AreEqual(7, read[0].Id, "inherited, ignoring case");
      Assert.AreEqual("Ann", read[0].Customer, "[Column]");
      Assert.AreEqual("A", read[0].FirstName, "underscores");
      Assert.AreEqual(0, read[0].Count, "a 0 is a value");
      Assert.IsNull(read[1].Count, "DBNull");
      Assert.AreEqual("set", read[0].Comment);
      Assert.AreEqual("none", read[1].Comment, "DBNull leaves the constructor value");
      Assert.AreEqual(Status.Paid, read[0].Status, "an enum from an integer");
      Assert.IsNull(read[0].Extra, "[NotMapped]");
      Assert.AreEqual(100L, read[0].Total, "int into long");
      Assert.AreEqual(-1L, read[1].Total);
    }

    [TestMethod]
    public void ImmutableClass_CreatedByItsConstructor()
    {
      string[] names = { "name", "Id", "Score" };
      ColumnarReader.Column[] columns =
      {
        new ColumnarReader.Column<string>(new[] { "Ann", null }),
        new ColumnarReader.Column<int>(new[] { 7, 8 }),
        new ColumnarReader.Column<double>(new[] { 1.5, 0 }, new[] { false, true }),
      };
      List<ImmutableRow> read = new LsMsgPackSerializer().ReadRows<ImmutableRow>(new ColumnarReader(names, columns, 2));
      Assert.AreEqual(7, read[0].Id);
      Assert.AreEqual("Ann", read[0].Name);
      Assert.AreEqual(1.5, read[0].Score);
      Assert.AreEqual(8, read[1].Id);
      Assert.IsNull(read[1].Name);
      Assert.IsNull(read[1].Score);
    }

    /// <summary>
    /// The SQLite storage classes: INTEGER into int and bool, TEXT into DateTime, decimal and Guid (as Microsoft.Data.Sqlite stores them).
    /// Dapper does not read a Guid from TEXT (without a type handler), the mapper does (SQLite's GetGuid reads it): Dapper is compared without that column.
    /// </summary>
    [TestMethod]
    public void Sqlite_SameAsDapper()
    {
      List<InvoiceRow> rows = Data.Invoices(100);
      using (SqliteConnection connection = SqliteInvoices.Create(rows))
      {
        Assert.ThrowsExactly<DataException>(() => connection.Query<InvoiceRow>(SqliteInvoices.Select).ToList());
        List<InvoiceRow> dapper = connection.Query<InvoiceRow>(SqliteInvoices.SelectWithoutGuid).ToList();
        List<InvoiceRow> read;
        using (SqliteCommand command = new SqliteCommand(SqliteInvoices.Select, connection))
        using (SqliteDataReader reader = command.ExecuteReader())
          read = new LsMsgPackSerializer().ReadRows<InvoiceRow>(reader);
        AssertEqual(rows, read);
        for (int t = 0; t < read.Count; t++)
          read[t].Reference = Guid.Empty;
        AssertEqual(dapper, read);
      }
    }

    internal static void AssertEqual(List<InvoiceRow> expected, List<InvoiceRow> actual)
    {
      Assert.AreEqual(expected.Count, actual.Count);
      for (int t = 0; t < expected.Count; t++)
      {
        InvoiceRow e = expected[t], a = actual[t];
        string row = "row " + t;
        Assert.AreEqual(e.Id, a.Id, row);
        Assert.AreEqual(e.Number, a.Number, row);
        Assert.AreEqual(e.Customer, a.Customer, row);
        Assert.AreEqual(e.Date, a.Date, row);
        Assert.AreEqual(e.Amount, a.Amount, row);
        Assert.AreEqual(e.Vat, a.Vat, row);
        Assert.AreEqual(e.Paid, a.Paid, row);
        Assert.AreEqual(e.Reference, a.Reference, row);
        Assert.AreEqual(e.Lines, a.Lines, row);
        Assert.AreEqual(e.Note, a.Note, row);
      }
    }
  }

  internal static class SqliteInvoices
  {
    internal const string Select = "SELECT Id, Number, Customer, Date, Amount, Vat, Paid, Reference, Lines, Note FROM Invoices ORDER BY Id";
    internal const string SelectWithoutGuid = "SELECT Id, Number, Customer, Date, Amount, Vat, Paid, Lines, Note FROM Invoices ORDER BY Id";

    internal static SqliteConnection Create(List<InvoiceRow> rows)
    {
      SqliteConnection connection = new SqliteConnection("Data Source=:memory:");
      connection.Open();
      Sqlite.Execute(connection, "CREATE TABLE Invoices (Id INTEGER PRIMARY KEY, Number TEXT, Customer TEXT, Date TEXT, Amount TEXT, Vat TEXT, Paid INTEGER, Reference TEXT, Lines INTEGER, Note TEXT)");
      foreach (InvoiceRow r in rows) // in memory: no transaction needed
        Sqlite.Execute(connection, "INSERT INTO Invoices VALUES (@id, @number, @customer, @date, @amount, @vat, @paid, @reference, @lines, @note)",
          ("@id", r.Id), ("@number", r.Number), ("@customer", r.Customer), ("@date", r.Date), ("@amount", r.Amount), ("@vat", r.Vat), ("@paid", r.Paid),
          ("@reference", r.Reference.ToString()), ("@lines", r.Lines), ("@note", r.Note));
      return connection;
    }
  }
}
