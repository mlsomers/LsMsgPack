using LsMsgPack;
using LsMsgPack.Data;
using LsMsgPackFSharpTestTypes;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace LsMsgPackAotFSharpTest
{
  /// <summary>
  /// F# types under NativeAOT (docs/FSharp.md): "write" (JIT) writes the bytes of each value in each option set, "check" (NativeAOT) writes the same bytes and reads them back into equal values
  /// (F# types compare by value), then maps rows into F# records. F# projects have no generated code: everything goes through the runtime plans and the F# converters.
  /// </summary>
  public static class Program
  {
    private sealed class Case
    {
      internal string Name;
      internal object Value;
      internal Func<LsMsgPackSerializer, byte[]> Write;
      internal Func<LsMsgPackSerializer, byte[], object> Read;
    }

    private static Case Of<T>(string name, T value)
    {
      return new Case() { Name = name, Value = value, Write = s => s.Serialize(value), Read = (s, data) => s.Deserialize<T>(data) };
    }

    private static FSharpOption<T> Some<T>(T value) { return FSharpOption<T>.Some(value); }

    private static FSharpList<T> List<T>(params T[] items) { return ListModule.OfSeq(items); }

    /// <summary>
    /// The values of the unit tests (LsMsgPackUnitTests.FSharpTests) that are F# only.
    /// </summary>
    private static IEnumerable<Case> Values()
    {
      yield return Of("address", new FsAddress("Main", 5));
      yield return Of("struct-record", new FsPoint(1.5, -2));
      yield return Of("climutable", new FsMutable(1, "b"));
      yield return Of("person-all", new FsPerson("Ann", Some("annie"), FSharpValueOption<int>.NewValueSome(0), Some(new FsAddress("Main", 5)), Some(1.5)));
      yield return Of("person-none", new FsPerson("Bob", null, FSharpValueOption<int>.ValueNone, null, null));
      yield return Of("holder", new FsHolder(
        List(FsShape.NewCircle(2.0), FsShape.NewRect(1.0, 3.5), FsShape.Empty),
        SetModule.OfSeq(new[] { "b", "a" }),
        MapModule.OfSeq(new[] { Tuple.Create("one", 1), Tuple.Create("two", 2) }),
        Tuple.Create(7, "seven"),
        FsColour.Blue,
        FsEmail.NewFsEmail("a@b.nl"),
        Some(FsShape.Empty),
        FSharpChoice<int, string>.NewChoice2Of2("second"),
        new[] { FsBig.A, FsBig.NewE(3), FsBig.F }));
      yield return Of("outcome", new FsWithOutcome(
        FsOutcome.NewOk(200),
        FsAnswer.NewOk("yes"),
        FsGeneric<string>.NewValue("v"),
        List(FsUnnamed.NewOne(1), FsUnnamed.NewTwo(2, "two"), FsUnnamed.Nothing),
        FsTree.NewNode(List(FsTree.NewLeaf(1), FsTree.NewNode(List(FsTree.NewLeaf(2))))),
        FsQualified.NewSecond(2),
        FsOrderId.NewFsOrderId(new Guid("0b0e9b8e-1b3c-4d6e-8f00-112233445566")),
        FsStructColour.Down,
        Some(Some(3))));
      yield return Of("shape", FsShape.NewCircle(2.0));
      yield return Of("fieldless-case", FsShape.Empty);
      yield return Of("colour", FsColour.Red);
      yield return Of("single-case", FsEmail.NewFsEmail("x@y.nl"));
      yield return Of("pair", FsPair.NewFsPair(1, "one"));
      yield return Of("generic-list", List(FsGeneric<int>.NewValue(5), FsGeneric<int>.NoValue));
      yield return Of("shape-list", List(FsShape.NewCircle(1), FsShape.Empty));
      yield return Of("option-int", Some(42));
      yield return Of("option-shape", Some(FsShape.NewRect(1, 2)));
      yield return Of("int-list", List(1, 2, 3)); // List.ofSeq of a value type: the application calls it (List), so NativeAOT has its code
      yield return Of("value-option", FSharpValueOption<int>.NewValueSome(3));
      yield return Of("map-by-colour", MapModule.OfSeq(new[] { Tuple.Create(FsColour.Red, Some(1)), Tuple.Create(FsColour.Blue, (FSharpOption<int>)null) }));
    }

    private static IEnumerable<(string Name, Func<LsMsgPackOptions> Options)> OptionSets()
    {
      yield return ("default", () => new LsMsgPackOptions());
      yield return ("names-map", () => new LsMsgPackOptions() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map });
      yield return ("names-array", () => new LsMsgPackOptions() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Array });
      yield return ("naming-policy", () => new LsMsgPackOptions() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower });
      yield return ("schema-v2", () => new LsMsgPackOptions() { SchemaVersion = SchemaVersion.Described }); // describes the surrogates: an option of a value type as a nullable
      yield return ("type-ids-always", () => new LsMsgPackOptions() { AddTypeIdOptions = AddTypeIdOption.Always }); // every converted value written boxed
      yield return ("preserve", () => new LsMsgPackOptions() { ReferenceHandling = ReferenceHandling.Preserve }); // converted classes read by Core's conversion, which resolves the references
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "F# types have no generated code: deliberately the runtime plans, the model kept by Model.xml.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "F# types have no generated code: deliberately the runtime plans, the model kept by Model.xml.")]
    private static LsMsgPackSerializer Runtime(LsMsgPackOptions options)
    {
      return new LsMsgPackSerializer(options);
    }

    public static int Main(string[] args)
    {
      string mode = args.Length > 0 ? args[0] : "write";
      string path = args.Length > 1 ? args[1] : "fsharp.txt";
      Console.WriteLine($"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, IsDynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported}, F# types");
      try
      {
        return mode == "check" ? Check(path) : Write(path);
      }
      catch (Exception ex)
      {
        Console.WriteLine("FAILED: " + ex);
        return 1;
      }
    }

    /// <summary>
    /// With a JIT: the expected bytes, and the values read back equal.
    /// </summary>
    private static int Write(string path)
    {
      StringBuilder golden = new StringBuilder();
      foreach ((string set, Func<LsMsgPackOptions> options) in OptionSets())
      {
        foreach (Case value in Values())
        {
          byte[] written = value.Write(Runtime(options()));
          object read = value.Read(Runtime(options()), written);
          if (!Equals(value.Value, read))
            throw new Exception($"{set} {value.Name}: read back as {read}, not {value.Value}");
          golden.Append(set).Append('|').Append(value.Name).Append('|').Append(Convert.ToHexString(written)).Append('\n');
        }
      }
      byte[] rows = Runtime(new LsMsgPackOptions()).SerializeRows<FsInvoiceRow>(Rows.Reader());
      golden.Append("rows|serialize-rows|").Append(Convert.ToHexString(rows)).Append('\n');
      string problem = Rows.Problem(Runtime);
      if (problem != null)
        throw new Exception(problem);
      File.WriteAllText(path, golden.ToString());
      Console.WriteLine("Written " + path);
      return 0;
    }

    /// <summary>
    /// NativeAOT: the same bytes, the values read back equal, the rows mapped.
    /// </summary>
    private static int Check(string path)
    {
      Dictionary<string, Case> values = Values().ToDictionary(v => v.Name);
      Dictionary<string, Func<LsMsgPackOptions>> sets = OptionSets().ToDictionary(o => o.Name, o => o.Options);
      int failures = 0;
      foreach (string line in File.ReadAllLines(path))
      {
        string[] parts = line.Split('|');
        byte[] expected = Convert.FromHexString(parts[2]);
        string problem;
        try
        {
          problem = parts[0] == "rows" ? RowsProblem(expected) : ValueProblem(values[parts[1]], sets[parts[0]], expected);
        }
        catch (Exception ex)
        {
          problem = ex.ToString();
        }
        Console.WriteLine($"{parts[0]} {parts[1]}: {problem ?? "OK"}");
        if (problem != null)
          failures++;
      }
      string mapped;
      try
      {
        mapped = Rows.Problem(Runtime);
      }
      catch (Exception ex)
      {
        mapped = ex.ToString();
      }
      Console.WriteLine($"rows into F# records: {mapped ?? "OK"}");
      if (mapped != null)
        failures++;
      Console.WriteLine(failures == 0 ? "PASSED" : $"FAILED: {failures}");
      return failures == 0 ? 0 : 1;
    }

    private static string ValueProblem(Case value, Func<LsMsgPackOptions> options, byte[] expected)
    {
      byte[] written = value.Write(Runtime(options()));
      if (!written.AsSpan().SequenceEqual(expected))
        return $"other bytes\n  expected {Convert.ToHexString(expected)}\n  written  {Convert.ToHexString(written)}";
      object read = value.Read(Runtime(options()), expected);
      return Equals(value.Value, read) ? null : $"read as {read}, not {value.Value}";
    }

    private static string RowsProblem(byte[] expected)
    {
      byte[] written = Runtime(new LsMsgPackOptions()).SerializeRows<FsInvoiceRow>(Rows.Reader());
      if (!written.AsSpan().SequenceEqual(expected))
        return $"other bytes\n  expected {Convert.ToHexString(expected)}\n  written  {Convert.ToHexString(written)}";
      List<FsInvoiceRow> read = Runtime(new LsMsgPackOptions()).Deserialize<List<FsInvoiceRow>>(expected);
      return read.SequenceEqual(Rows.Values()) ? null : "the rows read back differ";
    }
  }

  /// <summary>
  /// LsMsgPack.Data's row mapper into F# records (created by their constructor, or set with [&lt;CLIMutable&gt;]): options from NULLs, unions from names and fields.
  /// </summary>
  internal static class Rows
  {
    internal static List<FsInvoiceRow> Values()
    {
      List<FsInvoiceRow> rows = new List<FsInvoiceRow>();
      for (int t = 0; t < 6; t++)
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
    /// The rows as a provider has them: NULL or the value, unions by name or field.
    /// </summary>
    internal static IDataReader Reader()
    {
      DataTable table = new DataTable("Rows");
      table.Columns.Add("Id", typeof(int));
      table.Columns.Add("Customer", typeof(string));
      table.Columns.Add("Note", typeof(string));
      table.Columns.Add("Discount", typeof(decimal));
      table.Columns.Add("Paid", typeof(bool));
      table.Columns.Add("Total", typeof(double));
      table.Columns.Add("Colour", typeof(string));
      table.Columns.Add("Mail", typeof(string));
      table.Columns.Add("OrderId", typeof(Guid));
      table.Columns.Add("Created", typeof(DateTime));
      foreach (FsInvoiceRow row in Values())
        table.Rows.Add(row.Id, row.Customer, (object)row.Note?.Value ?? DBNull.Value, row.Discount is null ? DBNull.Value : row.Discount.Value,
          row.Paid.IsValueSome ? row.Paid.Value : DBNull.Value, row.Total, row.Colour.IsRed ? "Red" : row.Colour.IsGreen ? "Green" : "Blue",
          row.Mail.Item, row.OrderId.Item, row.Created);
      return table.CreateDataReader();
    }

    internal static string Problem(Func<LsMsgPackOptions, LsMsgPackSerializer> serializer)
    {
      List<FsInvoiceRow> expected = Values();
      List<FsInvoiceRow> read = serializer(new LsMsgPackOptions()).ReadRows<FsInvoiceRow>(Reader());
      if (!read.SequenceEqual(expected))
        return "ReadRows<FsInvoiceRow>: other rows\n  expected " + string.Join("; ", expected) + "\n  read     " + string.Join("; ", read);
      List<FsMutableRow> mutable = serializer(new LsMsgPackOptions()).ReadRows<FsMutableRow>(Reader());
      for (int t = 0; t < expected.Count; t++)
      {
        FsInvoiceRow e = expected[t];
        if (!mutable[t].Equals(new FsMutableRow(e.Id, e.Customer, e.Note, e.Discount, e.Paid, e.Total, e.Colour, e.Mail, e.OrderId, e.Created)))
          return $"ReadRows<FsMutableRow>: row {t} is {mutable[t]}";
      }
      return null;
    }
  }
}
