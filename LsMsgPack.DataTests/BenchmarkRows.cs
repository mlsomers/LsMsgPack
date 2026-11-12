using Dapper;
using LsMsgPack.Streaming;
using LsMsgPack.Data;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LsMsgPack.DataTests
{
  /// <summary>
  /// The mapper and the rows as MsgPack against Dapper (and Dapper followed by a serializer): rows of an in-memory reader (the mappers' own costs) and of an in-memory SQLite database (a provider).
  /// Results in the test output (<c>--logger "console;verbosity=detailed"</c>), not asserted. Run in Release.
  /// </summary>
  [TestClass]
  [TestCategory("Benchmark")]
  public class BenchmarkRows
  {
    public TestContext TestContext { get; set; }

    private const int RowCount = 10000;
    private const int Rounds = 10;
    private const int Passes = 5;

    private sealed class Case
    {
      internal string Name;
      internal Func<object> Run;
    }

    [TestMethod]
    public void InMemoryReader()
    {
      List<InvoiceRow> rows = Data.Invoices(RowCount);
      ColumnarReader reader = Data.InvoiceReader(rows);
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      Func<IDataReader> fresh = () => { reader.Reset(); return reader; };

      Report("In-memory reader (typed columns), " + RowCount + " invoice rows of 10 columns", new[]
      {
        new Case() { Name = "hand-written loop", Run = () => HandWritten(fresh()) },
        new Case() { Name = "Dapper Parse<T>", Run = () => fresh().Parse<InvoiceRow>().ToList() },
        new Case() { Name = "LsMsgPack ReadRows<T>", Run = () => lt.ReadRows<InvoiceRow>(fresh()) },
        new Case() { Name = "Dapper + LsMsgPack Serialize", Run = () => lt.Serialize(fresh().Parse<InvoiceRow>().ToList()) },
        new Case() { Name = "ReadRows + LsMsgPack Serialize", Run = () => lt.Serialize(lt.ReadRows<InvoiceRow>(fresh())) },
        new Case() { Name = "LsMsgPack SerializeRows", Run = () => lt.SerializeRows<InvoiceRow>(fresh()) },
        new Case() { Name = "SerializeRows to a stream", Run = () => lt.SerializeRows<InvoiceRow>(fresh(), Stream.Null) },
        new Case() { Name = "stream writer WriteTable", Run = () => WriteTable(lt, fresh()) },
        new Case() { Name = "Dapper + System.Text.Json", Run = () => JsonSerializer.SerializeToUtf8Bytes(fresh().Parse<InvoiceRow>().ToList()) },
      });
    }

    [TestMethod]
    public void Sqlite()
    {
      List<InvoiceRow> rows = Data.Invoices(RowCount);
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      using (SqliteConnection connection = SqliteInvoices.Create(rows))
      {
        string sql = SqliteInvoices.SelectWithoutGuid; // Dapper does not read SQLite's TEXT Guids
        Func<Func<IDataReader, object>, object> query = read =>
        {
          using (SqliteCommand command = new SqliteCommand(sql, connection))
          using (SqliteDataReader reader = command.ExecuteReader())
            return read(reader);
        };

        Report("SQLite in memory, " + RowCount + " invoice rows of 9 columns (the query included)", new[]
        {
          new Case() { Name = "reader only (Read loop)", Run = () => query(r => { int n = 0; while (r.Read()) n++; return n; }) },
          new Case() { Name = "Dapper Query<T>", Run = () => connection.Query<InvoiceRow>(sql).ToList() },
          new Case() { Name = "LsMsgPack ReadRows<T>", Run = () => query(r => lt.ReadRows<InvoiceRow>(r)) },
          new Case() { Name = "Dapper + LsMsgPack Serialize", Run = () => lt.Serialize(connection.Query<InvoiceRow>(sql).ToList()) },
          new Case() { Name = "LsMsgPack SerializeRows", Run = () => query(r => lt.SerializeRows<InvoiceRow>(r)) },
          new Case() { Name = "Dapper + System.Text.Json", Run = () => JsonSerializer.SerializeToUtf8Bytes(connection.Query<InvoiceRow>(sql).ToList()) },
        });
      }
    }

    /// <summary>
    /// The invoices in SQLite's own storage classes (INTEGER, REAL, TEXT) read into a class of those types: no conversions by the provider, the mappers' costs on a real provider.
    /// </summary>
    public class NativeInvoiceRow
    {
      public long Id { get; set; }
      public string Number { get; set; }
      public string Customer { get; set; }
      public long Date { get; set; }
      public double Amount { get; set; }
      public double Vat { get; set; }
      public long Paid { get; set; }
      public long Lines { get; set; }
      public string Note { get; set; }
    }

    [TestMethod]
    public void SqliteNativeTypes()
    {
      List<InvoiceRow> rows = Data.Invoices(RowCount);
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      using (SqliteConnection connection = new SqliteConnection("Data Source=:memory:"))
      {
        connection.Open();
        DataTests.Sqlite.Execute(connection, "CREATE TABLE Invoices (Id INTEGER PRIMARY KEY, Number TEXT, Customer TEXT, Date INTEGER, Amount REAL, Vat REAL, Paid INTEGER, Lines INTEGER, Note TEXT)");
        foreach (InvoiceRow r in rows)
          DataTests.Sqlite.Execute(connection, "INSERT INTO Invoices VALUES (@id, @number, @customer, @date, @amount, @vat, @paid, @lines, @note)",
            ("@id", r.Id), ("@number", r.Number), ("@customer", r.Customer), ("@date", r.Date.Ticks), ("@amount", (double)r.Amount), ("@vat", (double)r.Vat), ("@paid", r.Paid ? 1 : 0),
            ("@lines", r.Lines), ("@note", r.Note));

        string sql = "SELECT Id, Number, Customer, Date, Amount, Vat, Paid, Lines, Note FROM Invoices ORDER BY Id";
        Func<Func<IDataReader, object>, object> query = read =>
        {
          using (SqliteCommand command = new SqliteCommand(sql, connection))
          using (SqliteDataReader reader = command.ExecuteReader())
            return read(reader);
        };

        Report("SQLite in memory, native storage classes, " + RowCount + " rows of 9 columns (the query included)", new[]
        {
          new Case() { Name = "hand-written loop", Run = () => query(r => HandWrittenNative(r)) },
          new Case() { Name = "Dapper Query<T>", Run = () => connection.Query<NativeInvoiceRow>(sql).ToList() },
          new Case() { Name = "hand-written, IsDBNull everywhere", Run = () => query(r => HandWrittenNativeNulls(r)) },
          new Case() { Name = "LsMsgPack ReadRows<T>", Run = () => query(r => lt.ReadRows<NativeInvoiceRow>(r)) },
          new Case() { Name = "Dapper + LsMsgPack Serialize", Run = () => lt.Serialize(connection.Query<NativeInvoiceRow>(sql).ToList()) },
          new Case() { Name = "LsMsgPack SerializeRows", Run = () => query(r => lt.SerializeRows<NativeInvoiceRow>(r)) },
          new Case() { Name = "stream writer WriteTable", Run = () => query(r => WriteTable(lt, r)) },
          new Case() { Name = "Dapper + System.Text.Json", Run = () => JsonSerializer.SerializeToUtf8Bytes(connection.Query<NativeInvoiceRow>(sql).ToList()) },
        });
      }
    }

    private static List<NativeInvoiceRow> HandWrittenNative(IDataReader r)
    {
      List<NativeInvoiceRow> list = new List<NativeInvoiceRow>();
      while (r.Read())
      {
        list.Add(new NativeInvoiceRow()
        {
          Id = r.GetInt64(0), Number = r.GetString(1), Customer = r.GetString(2), Date = r.GetInt64(3), Amount = r.GetDouble(4), Vat = r.GetDouble(5),
          Paid = r.GetInt64(6), Lines = r.GetInt64(7), Note = r.IsDBNull(8) ? null : r.GetString(8),
        });
      }
      return list;
    }

    /// <summary>
    /// As the mapper reads: every column checked for DBNull (SQLite's IsDBNull is a native call, about 65 ns).
    /// </summary>
    private static List<NativeInvoiceRow> HandWrittenNativeNulls(IDataReader r)
    {
      List<NativeInvoiceRow> list = new List<NativeInvoiceRow>();
      while (r.Read())
      {
        NativeInvoiceRow row = new NativeInvoiceRow();
        if (!r.IsDBNull(0)) row.Id = r.GetInt64(0);
        if (!r.IsDBNull(1)) row.Number = r.GetString(1);
        if (!r.IsDBNull(2)) row.Customer = r.GetString(2);
        if (!r.IsDBNull(3)) row.Date = r.GetInt64(3);
        if (!r.IsDBNull(4)) row.Amount = r.GetDouble(4);
        if (!r.IsDBNull(5)) row.Vat = r.GetDouble(5);
        if (!r.IsDBNull(6)) row.Paid = r.GetInt64(6);
        if (!r.IsDBNull(7)) row.Lines = r.GetInt64(7);
        if (!r.IsDBNull(8)) row.Note = r.GetString(8);
        list.Add(row);
      }
      return list;
    }

    /// <summary>
    /// The rows written while they are read (no buffering), to a stream that keeps nothing.
    /// </summary>
    /// <summary>
    /// A dump of the invoices (in memory, with an index) read back with <see cref="MsgPackDataReader"/>: the reader's own costs.
    /// </summary>
    [TestMethod]
    public void DumpReader()
    {
      List<InvoiceRow> rows = Data.Invoices(RowCount);
      LsMsgPackSerializer lt = new LsMsgPackSerializer();
      MemoryStream dump = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(dump, lt, leaveOpen: true, index: new MsgPackStreamIndexOptions()))
        writer.WriteTable(Data.InvoiceReader(rows), "Invoices");
      byte[] bytes = dump.ToArray();
      MsgPackStreamEntry entry;
      using (MsgPackStreamReader reader = new MsgPackStreamReader(new MemoryStream(bytes), lt))
        entry = reader.ReadIndexes()[0].Entries[0];
      object[] values = new object[Data.InvoiceNames.Length];
      Func<MsgPackDataReader> sequential = () => new MsgPackDataReader(new MsgPackStreamReader(new MemoryStream(bytes), lt));
      Func<MsgPackDataReader> atEntry = () => new MsgPackDataReader(new MsgPackStreamReader(new MemoryStream(bytes), lt), entry);

      Report("MsgPackDataReader over a dump in memory, " + RowCount + " invoice rows of 10 columns", new[]
      {
        new Case() { Name = "Read only, at the entry", Run = () => { using (MsgPackDataReader r = atEntry()) { int n = 0; while (r.Read()) n++; return n; } } },
        new Case() { Name = "GetValues, sequentially", Run = () => { using (MsgPackDataReader r = sequential()) { int n = 0; while (r.Read()) n += r.GetValues(values); return n; } } },
        new Case() { Name = "GetValues, at the entry", Run = () => { using (MsgPackDataReader r = atEntry()) { int n = 0; while (r.Read()) n += r.GetValues(values); return n; } } },
        new Case() { Name = "typed getters, at the entry", Run = () => { using (MsgPackDataReader r = atEntry()) return HandWritten(r); } },
        new Case() { Name = "ReadRows<T>, at the entry", Run = () => { using (MsgPackDataReader r = atEntry()) return lt.ReadRows<InvoiceRow>(r); } },
        new Case() { Name = "ReadAsync + GetValues, entry", Run = () => ReadAllAsync(atEntry(), values).GetAwaiter().GetResult() },
      });
    }

    private static async System.Threading.Tasks.Task<int> ReadAllAsync(MsgPackDataReader reader, object[] values)
    {
      using (reader)
      {
        int n = 0;
        while (await reader.ReadAsync())
          n += reader.GetValues(values);
        return n;
      }
    }

    private static long WriteTable(LsMsgPackSerializer serializer, IDataReader reader)
    {
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(Stream.Null, serializer))
        return writer.WriteTable(reader, "Invoices");
    }

    private static List<InvoiceRow> HandWritten(IDataReader r)
    {
      List<InvoiceRow> list = new List<InvoiceRow>();
      while (r.Read())
      {
        list.Add(new InvoiceRow()
        {
          Id = r.GetInt32(0), Number = r.GetString(1), Customer = r.GetString(2), Date = r.GetDateTime(3), Amount = r.GetDecimal(4), Vat = r.GetDecimal(5),
          Paid = r.GetBoolean(6), Reference = r.GetGuid(7), Lines = r.GetInt32(8), Note = r.IsDBNull(9) ? null : r.GetString(9),
        });
      }
      return list;
    }

    private void Report(string title, Case[] cases)
    {
      for (int t = 0; t < cases.Length; t++) // warm up (plans, Dapper's IL, tiered compilation)
        for (int w = 0; w < 30; w++)
          cases[t].Run();

      double[] best = new double[cases.Length];
      long[] allocated = new long[cases.Length];
      for (int t = 0; t < best.Length; t++)
        best[t] = double.MaxValue;
      for (int p = 0; p < Passes; p++)
      {
        for (int t = 0; t < cases.Length; t++)
        {
          GC.Collect();
          GC.WaitForPendingFinalizers();
          long before = GC.GetAllocatedBytesForCurrentThread();
          Stopwatch sw = Stopwatch.StartNew();
          for (int r = 0; r < Rounds; r++)
            cases[t].Run();
          double ms = sw.Elapsed.TotalMilliseconds / Rounds;
          allocated[t] = (GC.GetAllocatedBytesForCurrentThread() - before) / Rounds;
          best[t] = Math.Min(best[t], ms);
        }
      }

      TestContext.WriteLine(title + " (best of " + Passes + " passes of " + Rounds + " calls):");
      double baseline = best[1];
      for (int t = 0; t < cases.Length; t++)
      {
        TestContext.WriteLine(string.Format("  {0,-32} {1,8:0.000} ms  {2,6:0} ns/row  {3,5:0.00}x of {4}  {5,8:0} B/row",
          cases[t].Name, best[t], best[t] * 1e6 / RowCount, baseline / best[t], cases[1].Name, (double)allocated[t] / RowCount));
      }
    }
  }
}
