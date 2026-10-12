using LsMsgPack;
using LtMsgPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace LtMsgPackAotSmokeTest
{
  public enum InvoiceStatus { Draft, Sent, Paid }
  public enum Unit : byte { Piece, Hour, Kilogram }

  public class Address
  {
    public string Street { get; set; }
    public string City { get; set; }
    public string CountryCode { get; set; }
  }

  public class Customer
  {
    public int CustomerId { get; set; }
    public string Name { get; set; }
    public Address Billing { get; set; }
    public Address Shipping { get; set; }
    public Dictionary<string, int> Scores { get; set; }
    public HashSet<string> Tags { get; set; }
  }

  public class InvoiceLine
  {
    public int LineNumber { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public Unit Unit { get; set; }
    public decimal UnitPrice { get; set; }
  }

  public abstract class Shape
  {
    public string Label { get; set; }
  }

  public class Circle : Shape
  {
    public double Radius { get; set; }
  }

  public class Square : Shape
  {
    public double Side { get; set; }
  }

  public class Invoice
  {
    public Guid Id { get; set; }
    public string Number { get; set; }
    public DateTime Date { get; set; }
    public DateTime? Paid { get; set; }
    public DateTimeOffset Created { get; set; }
    public TimeSpan Term { get; set; }
    public InvoiceStatus Status { get; set; }
    public Customer Customer { get; set; }
    public List<InvoiceLine> Lines { get; set; }
    public InvoiceLine[] Extra { get; set; }
    public Guid? Reference { get; set; }
    public long[] Numbers { get; set; }
    public Shape Stamp { get; set; }
    public List<Shape> Shapes { get; set; }
    public decimal Total { get; set; }
    public bool Urgent { get; set; }
  }

  /// <summary>
  /// The subclasses are only reached through the type ids, so they are listed.
  /// </summary>
  [LtMsgPackSerializable(typeof(Invoice))]
  [LtMsgPackSerializable(typeof(Circle))]
  [LtMsgPackSerializable(typeof(Square))]
  public partial class SmokeContext : LtMsgPackContext { }

  public static class Program
  {
    private static readonly SchemaStore Store = new SchemaStore();

    private static IEnumerable<(string Name, Func<LtMsgPackOptions> Options)> OptionSets()
    {
      yield return ("default", () => new LtMsgPackOptions());
      yield return ("names-map", () => new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map });
      yield return ("names-array", () => new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array });
      yield return ("inline-map", () => new LtMsgPackOptions() { ObjectLayout = ObjectLayout.Map });
      yield return ("reference", () => new LtMsgPackOptions() { SchemaStore = Store, WriteSchemaReference = true });
      yield return ("trimmed", () => new LtMsgPackOptions() { TrimTrailingNulls = true, DynamicallyCompact = false });
    }

    private static LtMsgPackSerializer Runtime(LtMsgPackOptions options)
    {
      options.Contexts = null;
      options.UseRegisteredContexts = false;
      return new LtMsgPackSerializer(options);
    }

    private static LtMsgPackSerializer Explicit(LtMsgPackOptions options)
    {
      options.Contexts = new LtMsgPackContext[] { SmokeContext.Default };
      options.UseRegisteredContexts = false;
      return new LtMsgPackSerializer(options);
    }

    private static LtMsgPackSerializer Registered(LtMsgPackOptions options)
    {
      return new LtMsgPackSerializer(options); // the module initializer registered SmokeContext
    }

    public static int Main(string[] args)
    {
      string mode = args.Length > 0 ? args[0] : "write";
      string path = args.Length > 1 ? args[1] : "golden.txt";
      Console.WriteLine($"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, IsDynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported}, SmokeContext.IsUsable={SmokeContext.Default.IsUsable}");
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
    /// With a JIT: the bytes and values of the runtime plans, checked against the generated code.
    /// </summary>
    private static int Write(string path)
    {
      Invoice sample = Sample();
      StringBuilder golden = new StringBuilder();
      foreach ((string name, Func<LtMsgPackOptions> options) in OptionSets())
      {
        byte[] runtime = Runtime(options()).Serialize(sample);
        byte[] generated = Explicit(options()).Serialize(sample);
        if (!runtime.AsSpan().SequenceEqual(generated))
          throw new Exception($"{name}: the generated code writes other bytes\n  runtime   {Convert.ToHexString(runtime)}\n  generated {Convert.ToHexString(generated)}");
        string expected = Dump(Runtime(options()).Deserialize<Invoice>(runtime));
        string read = Dump(Explicit(options()).Deserialize<Invoice>(runtime));
        if (expected != read)
          throw new Exception($"{name}: the generated code reads other values\n  runtime   {expected}\n  generated {read}");
        golden.Append(name).Append('|').Append(Convert.ToHexString(runtime)).Append('|').Append(expected).Append('\n');
        Console.WriteLine($"{name}: {runtime.Length} bytes, the same with the generated code");
      }
      File.WriteAllText(path, golden.ToString());
      Console.WriteLine("Written " + path);
      return 0;
    }

    /// <summary>
    /// NativeAOT: the generated code (explicit and registered) writes and reads what the JIT run wrote. The runtime plans without generated code are tried too (reported, not required).
    /// </summary>
    private static int Check(string path)
    {
      Invoice sample = Sample();
      int failures = 0;
      foreach (string line in File.ReadAllLines(path))
      {
        string[] parts = line.Split('|');
        string name = parts[0];
        byte[] expected = Convert.FromHexString(parts[1]);
        string values = parts[2];
        Func<LtMsgPackOptions> options = OptionSets().First(o => o.Name == name).Options;

        foreach ((string which, Func<LtMsgPackOptions, LtMsgPackSerializer> create) in new (string, Func<LtMsgPackOptions, LtMsgPackSerializer>)[] { ("explicit", Explicit), ("registered", Registered) })
        {
          string problem = Problem(create(options()), sample, expected, values);
          Console.WriteLine($"{name} {which}: {problem ?? "OK"}");
          if (problem != null)
            failures++;
        }

        string runtimeProblem = Problem(Runtime(options()), sample, expected, values);
        Console.WriteLine($"{name} without generated code (informational): {(runtimeProblem is null ? "OK" : runtimeProblem.Split('\n')[0])}");
      }
      Console.WriteLine(failures == 0 ? "PASSED" : $"FAILED: {failures}");
      return failures == 0 ? 0 : 1;
    }

    private static string Problem(LtMsgPackSerializer serializer, Invoice sample, byte[] expected, string values)
    {
      try
      {
        byte[] written = serializer.Serialize(sample);
        if (!written.AsSpan().SequenceEqual(expected))
          return $"other bytes\n  expected {Convert.ToHexString(expected)}\n  written  {Convert.ToHexString(written)}";
        string read = Dump(serializer.Deserialize<Invoice>(expected));
        if (read != values)
          return $"other values\n  expected {values}\n  read     {read}";
        return null;
      }
      catch (Exception ex)
      {
        return ex.GetType().Name + ": " + ex.Message;
      }
    }

    private static Invoice Sample()
    {
      Address home = new Address() { Street = "Main street 1", City = "Utrecht", CountryCode = "NL" };
      return new Invoice()
      {
        Id = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
        Number = "2026-0042",
        Date = new DateTime(2026, 10, 12, 8, 30, 0, DateTimeKind.Utc),
        Paid = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
        Created = new DateTimeOffset(2026, 10, 12, 10, 30, 0, TimeSpan.FromHours(2)),
        Term = TimeSpan.FromDays(30),
        Status = InvoiceStatus.Sent,
        Customer = new Customer()
        {
          CustomerId = 7,
          Name = "Infotopie",
          Billing = home,
          Shipping = new Address() { Street = "Dock 5", City = "Rotterdam" },
          Scores = new Dictionary<string, int>() { { "a", 1 }, { "b", 2 } },
          Tags = new HashSet<string>() { "vip" }
        },
        Lines = new List<InvoiceLine>()
        {
          new InvoiceLine() { LineNumber = 1, Description = "Consulting", Quantity = 2.5m, Unit = Unit.Hour, UnitPrice = 95m },
          new InvoiceLine() { LineNumber = 2, Description = "Cable", Quantity = 3, Unit = Unit.Piece, UnitPrice = 4.99m }
        },
        Extra = new[] { new InvoiceLine() { LineNumber = 3, Description = "Shipping" } },
        Reference = null,
        Numbers = new long[] { 1, -2, 300000000000 },
        Stamp = new Circle() { Label = "paid", Radius = 1.5 },
        Shapes = new List<Shape>() { new Square() { Label = "s", Side = 2 }, new Circle() { Radius = 0.25 } },
        Total = 252.47m,
        Urgent = true
      };
    }

    private static string Dump(Invoice o)
    {
      if (o is null)
        return "null";
      StringBuilder text = new StringBuilder();
      text.Append(o.Id).Append(';').Append(o.Number).Append(';').Append(Time(o.Date)).Append(';').Append(o.Paid.HasValue ? Time(o.Paid.Value) : "null").Append(';')
        .Append(o.Created.ToString("o", CultureInfo.InvariantCulture)).Append(';').Append(o.Term.Ticks).Append(';').Append(o.Status).Append(';')
        .Append(Dump(o.Customer)).Append(';').Append(o.Lines is null ? "null" : string.Join(",", o.Lines.Select(Dump))).Append(';')
        .Append(o.Extra is null ? "null" : string.Join(",", o.Extra.Select(Dump))).Append(';').Append(o.Reference?.ToString() ?? "null").Append(';')
        .Append(o.Numbers is null ? "null" : string.Join(",", o.Numbers)).Append(';').Append(Dump(o.Stamp)).Append(';')
        .Append(o.Shapes is null ? "null" : string.Join(",", o.Shapes.Select(Dump))).Append(';').Append(o.Total.ToString(CultureInfo.InvariantCulture)).Append(';').Append(o.Urgent);
      return text.ToString();
    }

    private static string Time(DateTime value)
    {
      return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

    private static string Dump(Customer o)
    {
      if (o is null)
        return "null";
      return $"[{o.CustomerId} {o.Name} {Dump(o.Billing)} {Dump(o.Shipping)} {(o.Scores is null ? "null" : string.Join(",", o.Scores.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)))} {(o.Tags is null ? "null" : string.Join(",", o.Tags.OrderBy(t => t, StringComparer.Ordinal)))}]";
    }

    private static string Dump(Address o)
    {
      return o is null ? "null" : $"[{o.Street} {o.City} {o.CountryCode ?? "null"}]";
    }

    private static string Dump(InvoiceLine o)
    {
      return o is null ? "null" : $"[{o.LineNumber} {o.Description} {o.Quantity.ToString(CultureInfo.InvariantCulture)} {o.Unit} {o.UnitPrice.ToString(CultureInfo.InvariantCulture)}]";
    }

    private static string Dump(Shape o)
    {
      switch (o)
      {
        case null: return "null";
        case Circle circle: return $"Circle[{circle.Label ?? "null"} {circle.Radius.ToString("R", CultureInfo.InvariantCulture)}]";
        case Square square: return $"Square[{square.Label ?? "null"} {square.Side.ToString("R", CultureInfo.InvariantCulture)}]";
        default: return o.GetType().Name;
      }
    }
  }
}
