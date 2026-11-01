using System;
using System.Collections.Generic;

namespace LtMsgPack.DataTests
{
  public static class Data
  {
    public static readonly string[] AllTypesNames = { "Id", "Name", "Amount", "Created", "Key", "Active", "Score", "Big", "Data", "Small", "Ratio", "Tiny" };

    /// <summary>
    /// Rows with values, with defaults (0, empty, false: written as values, not left out) and with DBNull in every column.
    /// </summary>
    public static List<AllTypesRow> AllTypesRows()
    {
      return new List<AllTypesRow>()
      {
        new AllTypesRow() { Id = 1, Name = "First", Amount = 12.34m, Created = new DateTime(2026, 10, 23, 14, 15, 16, 789), Key = new Guid("6c1e4f6e-3b7a-4d1e-9f0a-2b8c5d7e9f01"),
          Active = true, Score = 1.5, Big = 1L << 40, Data = new byte[] { 1, 2, 3 }, Small = -5, Ratio = 0.25f, Tiny = 200 },
        new AllTypesRow() { Id = 0, Name = "", Amount = 0m, Created = new DateTime(1969, 12, 31, 23, 59, 59, DateTimeKind.Utc), Key = Guid.Empty, Active = false, Score = 0, Big = 0, Data = new byte[0], Small = 0, Ratio = 0, Tiny = 0 },
        new AllTypesRow(),
        new AllTypesRow() { Id = int.MinValue, Name = "Ünïcødé and a text longer than thirty-two characters", Amount = -79228162514264337593543950335m, Created = new DateTime(9999, 12, 30, 23, 0, 0),
          Key = Guid.NewGuid(), Active = true, Score = double.NaN, Big = long.MaxValue, Data = new byte[300], Small = short.MaxValue, Ratio = float.MinValue, Tiny = 255 },
      };
    }

    public static ColumnarReader AllTypes(List<AllTypesRow> rows)
    {
      int n = rows.Count;
      ColumnarReader.Column[] columns =
      {
        Of(rows, r => r.Id), new ColumnarReader.Column<string>(Map(rows, r => r.Name)), Of(rows, r => r.Amount), Of(rows, r => r.Created), Of(rows, r => r.Key),
        Of(rows, r => r.Active), Of(rows, r => r.Score), Of(rows, r => r.Big), new ColumnarReader.Column<byte[]>(Map(rows, r => r.Data)), Of(rows, r => r.Small),
        Of(rows, r => r.Ratio), Of(rows, r => r.Tiny),
      };
      return new ColumnarReader(AllTypesNames, columns, n);
    }

    private static ColumnarReader.Column<T> Of<T>(List<AllTypesRow> rows, Func<AllTypesRow, T?> get) where T : struct
    {
      T[] values = new T[rows.Count];
      bool[] nulls = new bool[rows.Count];
      for (int t = 0; t < rows.Count; t++)
      {
        T? value = get(rows[t]);
        values[t] = value.GetValueOrDefault();
        nulls[t] = !value.HasValue;
      }
      return new ColumnarReader.Column<T>(values, nulls);
    }

    private static T[] Map<T>(List<AllTypesRow> rows, Func<AllTypesRow, T> get)
    {
      T[] values = new T[rows.Count];
      for (int t = 0; t < rows.Count; t++)
        values[t] = get(rows[t]);
      return values;
    }

    public static readonly string[] InvoiceNames = { "Id", "Number", "Customer", "Date", "Amount", "Vat", "Paid", "Reference", "Lines", "Note" };

    public static List<InvoiceRow> Invoices(int count)
    {
      Random random = new Random(42);
      List<InvoiceRow> rows = new List<InvoiceRow>(count);
      DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
      for (int t = 0; t < count; t++)
      {
        decimal amount = Math.Round((decimal)(random.NextDouble() * 10000), 2);
        rows.Add(new InvoiceRow()
        {
          Id = t + 1,
          Number = "INV-2026-" + (t + 1).ToString("000000"),
          Customer = "Customer " + random.Next(1, 500),
          Date = start.AddMinutes(random.Next(0, 300 * 24 * 60)),
          Amount = amount,
          Vat = Math.Round(amount * 0.21m, 2),
          Paid = random.Next(2) == 0,
          Reference = new Guid(random.Next(), (short)random.Next(), (short)random.Next(), new byte[] { 1, 2, 3, 4, 5, 6, 7, (byte)t }),
          Lines = random.Next(1, 40),
          Note = random.Next(3) == 0 ? null : "Delivered to the back door, ring twice",
        });
      }
      return rows;
    }

    public static ColumnarReader InvoiceReader(List<InvoiceRow> rows)
    {
      int n = rows.Count;
      bool[] noteNulls = new bool[n];
      string[] notes = new string[n];
      int[] ids = new int[n], lines = new int[n];
      string[] numbers = new string[n], customers = new string[n];
      DateTime[] dates = new DateTime[n];
      decimal[] amounts = new decimal[n], vats = new decimal[n];
      bool[] paid = new bool[n];
      Guid[] references = new Guid[n];
      for (int t = 0; t < n; t++)
      {
        InvoiceRow r = rows[t];
        ids[t] = r.Id; numbers[t] = r.Number; customers[t] = r.Customer; dates[t] = r.Date; amounts[t] = r.Amount; vats[t] = r.Vat; paid[t] = r.Paid;
        references[t] = r.Reference; lines[t] = r.Lines; notes[t] = r.Note; noteNulls[t] = r.Note is null;
      }
      ColumnarReader.Column[] columns =
      {
        new ColumnarReader.Column<int>(ids), new ColumnarReader.Column<string>(numbers), new ColumnarReader.Column<string>(customers), new ColumnarReader.Column<DateTime>(dates),
        new ColumnarReader.Column<decimal>(amounts), new ColumnarReader.Column<decimal>(vats), new ColumnarReader.Column<bool>(paid), new ColumnarReader.Column<Guid>(references),
        new ColumnarReader.Column<int>(lines), new ColumnarReader.Column<string>(notes, noteNulls),
      };
      return new ColumnarReader(InvoiceNames, columns, n);
    }
  }
}
