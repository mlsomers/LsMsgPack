using LsMsgPack;
using LtMsgPack;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
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

  /// <summary>
  /// Framework types the serializers use by reflection (members found by name, generic factories, instantiations): kept by the DynamicDependency attributes of LsMsgPack.Core and the generated code.
  /// </summary>
  public class Extras
  {
    public DateOnly Day { get; set; }
    public TimeOnly At { get; set; }
    public Half Ratio { get; set; }
    public Int128 Big { get; set; }
    public UInt128 Huge { get; set; }
    public BigInteger Giant { get; set; }
    public Complex Point { get; set; }
    public Rune Letter { get; set; }
    public Version Release { get; set; }
    public (int, string) Pair { get; set; }
    public Tuple<int, string> Couple { get; set; }
    public KeyValuePair<string, int> Entry { get; set; }
    public IList<int> Counts { get; set; }
    public IReadOnlyList<Address> Places { get; set; }
    public ImmutableArray<int> Fixed { get; set; }
    public ImmutableList<string> Names { get; set; }
    public IImmutableSet<string> Codes { get; set; }
    public FrozenSet<string> Frozen { get; set; }
    public List<KeyValuePair<string, double>> Weights { get; set; }
    public Dictionary<string, int> Stock { get; set; }
    public Dictionary<int, Vector> Grid { get; set; }
    public HashSet<int> Seen { get; set; }
    public ISet<string> Labels { get; set; }
    public IDictionary<string, Address> ByName { get; set; }
    public ICollection<Vector> Track { get; set; }
    public IEnumerable<long> Totals { get; set; }
  }

  /// <summary>
  /// Created by the constructor that takes the values (LsMsgPack.Core ObjectConstructor, ConstructorInvoker): a get-only property and a settable one it does not take.
  /// </summary>
  public class Payment
  {
    private readonly List<string> _log = new List<string>();

    public Payment(decimal amount)
    {
      Amount = amount;
      _log.Add("paid");
    }

    public decimal Amount { get; }
    public string Note { get; set; }
    public int Logged { get { return _log.Count; } } // computed: written, skipped when read
  }

  public record Signature(string By, DateTime At);

  /// <summary>
  /// A struct: written and read by reference with the generated accessors and code (without them NativeAOT would box each value and use reflection).
  /// </summary>
  public struct Vector
  {
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int? Tag { get; set; }
    public Unit Unit { get; set; }
    public float Sum => X + Y + Z; // computed: written, skipped when read
  }

  /// <summary>
  /// A struct with init-only properties (set through the UnsafeAccessors of the generated code).
  /// </summary>
  public readonly record struct Fee(decimal Amount, string Currency);

  /// <summary>
  /// Init-only setters are called through UnsafeAccessors: reading allocates the same as for <see cref="SetsLimits"/>, the same class with plain setters (see ReadAllocationProblem).
  /// </summary>
  public class InitLimits
  {
    public int Min { get; init; }
    public int Max { get; init; }
    public double Rate { get; init; }
    public bool On { get; init; }
    public InitPoint Point { get; init; }
  }

  public readonly struct InitPoint
  {
    public int X { get; init; }
    public int Y { get; init; }
  }

  public class SetsLimits
  {
    public int Min { get; set; }
    public int Max { get; set; }
    public double Rate { get; set; }
    public bool On { get; set; }
    public SetsPoint Point { get; set; }
  }

  public struct SetsPoint
  {
    public int X { get; set; }
    public int Y { get; set; }
  }

  public class Route
  {
    public string Name { get; set; }
    public Vector Start { get; set; }
    public Vector? End { get; set; }
    public List<Vector> Points { get; set; }
    public Vector[] Corners { get; set; }
    public Fee Fee { get; set; }
    public InitLimits Limits { get; set; }
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
    public Extras Extras { get; set; }
    public Payment Payment { get; set; }
    public Signature Signed { get; set; }
    public Route Route { get; set; }
  }

  /// <summary>
  /// The subclasses are only reached through the type ids, so they are listed.
  /// </summary>
  [LtMsgPackSerializable(typeof(Invoice))]
  [LtMsgPackSerializable(typeof(Circle))]
  [LtMsgPackSerializable(typeof(Square))]
  [LtMsgPackSerializable(typeof(SetsLimits))]
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Deliberately without generated code: the JIT run writes the expected data with it, the NativeAOT run only reports how it does.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Deliberately without generated code: the JIT run writes the expected data with it, the NativeAOT run only reports how it does.")]
    private static LtMsgPackSerializer Runtime(LtMsgPackOptions options)
    {
      options.Contexts = null;
      options.UseRegisteredContexts = false;
      return new LtMsgPackSerializer(options);
    }

    private static LtMsgPackSerializer Explicit(LtMsgPackOptions options)
    {
      return new LtMsgPackSerializer(SmokeContext.Default, options); // the warning-free constructor for trimmed and NativeAOT apps (it registers the context: LsMsgPack.Core creates collections with it)
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Checks the contexts registered by the module initializer, which a serializer without a context uses.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Checks the contexts registered by the module initializer, which a serializer without a context uses.")]
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
          string problem = Problem(create(options()), sample, expected, values) ?? AllocationProblem(create(options())) ?? ReadAllocationProblem(create(options())) ?? CollectionAllocationProblem(create(options()));
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
        return ex.ToString(); // the first line is shown for the informational runs
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
        Urgent = true,
        Payment = new Payment(100.5m) { Note = "first" },
        Signed = new Signature("Clerk", new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc)),
        Route = SampleRoute(),
        Extras = new Extras()
        {
          Day = new DateOnly(2026, 10, 13),
          At = new TimeOnly(14, 30, 15),
          Ratio = (Half)0.75,
          Big = Int128.MaxValue - 5,
          Huge = UInt128.MaxValue,
          Giant = BigInteger.Pow(3, 50),
          Point = new Complex(1.5, -2),
          Letter = new Rune(0x1F600),
          Release = new Version(1, 2, 3),
          Pair = (4, "four"),
          Couple = Tuple.Create(5, "five"),
          Entry = new KeyValuePair<string, int>("six", 6),
          Counts = new List<int>() { 7, 8 },
          Places = new List<Address>() { home },

          Fixed = ImmutableArray.Create(9, 10),
          Names = ImmutableList.Create("x", "y"),
          Codes = ImmutableHashSet.Create("NL"),
          Frozen = new[] { "f" }.ToFrozenSet(),
          Weights = new List<KeyValuePair<string, double>>() { new KeyValuePair<string, double>("w", 0.5) },
          Stock = new Dictionary<string, int>() { { "a", 1 }, { "b", 300 } },
          Grid = new Dictionary<int, Vector>() { { 1, new Vector() { X = 1 } } },
          Seen = new HashSet<int>() { 3, 2, 1 },
          Labels = new HashSet<string>() { "l" },
          ByName = new Dictionary<string, Address>() { { "home", home } },
          Track = new List<Vector>() { new Vector() { Y = 2 } },
          Totals = new long[] { long.MaxValue }
        }
      };
    }

    private static Route SampleRoute()
    {
      return new Route()
      {
        Name = "delivery",
        Start = new Vector() { X = 1, Y = -2.5f, Z = 3, Tag = 4, Unit = Unit.Kilogram },
        End = new Vector() { Z = 9 },
        Points = new List<Vector>() { new Vector() { X = 0.5f }, default(Vector) },
        Corners = new[] { new Vector() { Y = 7, Tag = 0 } },
        Fee = new Fee(12.5m, "EUR"),
        Limits = new InitLimits() { Min = 300, Max = 70000, Rate = 0.25, On = true, Point = new InitPoint() { X = 300, Y = -300 } }
      };
    }

    /// <summary>
    /// The structs are written by reference and compared with their defaults by the generated code: writing a route into a stream that has the room allocates nothing
    /// (boxed, or with the boxed default check of FilterDefaultValues, each struct value would allocate).
    /// </summary>
    private static string AllocationProblem(LtMsgPackSerializer serializer)
    {
      Route route = SampleRoute();
      MemoryStream stream = new MemoryStream(4096);
      for (int t = 0; t < 3; t++) // warm up (plans, contexts)
      {
        stream.SetLength(0);
        serializer.Serialize(route, stream);
      }
      long before = GC.GetAllocatedBytesForCurrentThread();
      stream.SetLength(0);
      serializer.Serialize(route, stream);
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      return allocated == 0 ? null : $"writing the structs allocated {allocated} bytes";
    }

    /// <summary>
    /// The typed handler of a set: boxed, each element would allocate.
    /// </summary>
    private static string CollectionAllocationProblem(LtMsgPackSerializer serializer)
    {
      HashSet<int> set = new HashSet<int>();
      for (int t = 0; t < 1000; t++)
        set.Add(t);
      MemoryStream stream = new MemoryStream(8192);
      for (int t = 0; t < 3; t++) // warm up (plans, contexts)
      {
        stream.SetLength(0);
        serializer.Serialize(set, stream);
      }
      long before = GC.GetAllocatedBytesForCurrentThread();
      stream.SetLength(0);
      serializer.Serialize(set, stream);
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      return allocated < set.Count ? null : $"writing a set of {set.Count} allocated {allocated} bytes";
    }

    /// <summary>
    /// Init-only setters cost the same as plain ones: by reflection each value would be boxed.
    /// </summary>
    private static string ReadAllocationProblem(LtMsgPackSerializer serializer)
    {
      long init = ReadAllocation(serializer, new InitLimits() { Min = 300, Max = 70000, Rate = 0.25, On = true, Point = new InitPoint() { X = 300, Y = -300 } });
      long plain = ReadAllocation(serializer, new SetsLimits() { Min = 300, Max = 70000, Rate = 0.25, On = true, Point = new SetsPoint() { X = 300, Y = -300 } });
      return init == plain ? null : $"reading init-only properties allocated {init} bytes, plain setters {plain}";
    }

    private static long ReadAllocation<T>(LtMsgPackSerializer serializer, T value)
    {
      byte[] data = serializer.Serialize(value);
      for (int t = 0; t < 3; t++) // warm up (plans, contexts)
        serializer.Deserialize<T>(data);
      long before = GC.GetAllocatedBytesForCurrentThread();
      serializer.Deserialize<T>(data);
      return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static string Dump(Route o)
    {
      if (o is null)
        return "null";
      return $"[{o.Name ?? "null"} {Dump(o.Start)} {(o.End.HasValue ? Dump(o.End.Value) : "null")} {(o.Points is null ? "null" : string.Join(",", o.Points.Select(Dump)))} "
        + $"{(o.Corners is null ? "null" : string.Join(",", o.Corners.Select(Dump)))} {o.Fee.Amount.ToString(CultureInfo.InvariantCulture)}/{o.Fee.Currency ?? "null"} {Dump(o.Limits)}]";
    }

    private static string Dump(InitLimits o)
    {
      if (o is null)
        return "null";
      return $"({o.Min} {o.Max} {o.Rate.ToString("R", CultureInfo.InvariantCulture)} {o.On} {o.Point.X} {o.Point.Y})";
    }

    private static string Dump(Vector o)
    {
      return $"({o.X.ToString("R", CultureInfo.InvariantCulture)} {o.Y.ToString("R", CultureInfo.InvariantCulture)} {o.Z.ToString("R", CultureInfo.InvariantCulture)} {o.Tag?.ToString(CultureInfo.InvariantCulture) ?? "null"} {o.Unit})";
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
        .Append(o.Shapes is null ? "null" : string.Join(",", o.Shapes.Select(Dump))).Append(';').Append(o.Total.ToString(CultureInfo.InvariantCulture)).Append(';').Append(o.Urgent).Append(';').Append(Dump(o.Extras)).Append(';')
        .Append(o.Payment is null ? "null" : $"[{o.Payment.Amount.ToString(CultureInfo.InvariantCulture)} {o.Payment.Note ?? "null"} {o.Payment.Logged}]").Append(';')
        .Append(o.Signed is null ? "null" : $"[{o.Signed.By ?? "null"} {Time(o.Signed.At)}]").Append(';').Append(Dump(o.Route));
      return text.ToString();
    }

    private static string Dump(Extras o)
    {
      if (o is null)
        return "null";
      return string.Join(" ", new string[]
      {
        o.Day.ToString("o", CultureInfo.InvariantCulture), o.At.ToString("o", CultureInfo.InvariantCulture), ((float)o.Ratio).ToString("R", CultureInfo.InvariantCulture),
        o.Big.ToString(CultureInfo.InvariantCulture), o.Huge.ToString(CultureInfo.InvariantCulture), o.Giant.ToString(CultureInfo.InvariantCulture),
        o.Point.Real.ToString("R", CultureInfo.InvariantCulture) + "/" + o.Point.Imaginary.ToString("R", CultureInfo.InvariantCulture), o.Letter.Value.ToString(CultureInfo.InvariantCulture),
        o.Release?.ToString() ?? "null", o.Pair.Item1 + "," + (o.Pair.Item2 ?? "null"), o.Couple is null ? "null" : o.Couple.Item1 + "," + o.Couple.Item2, o.Entry.Key + "=" + o.Entry.Value,
        o.Counts is null ? "null" : o.Counts.GetType().Name + ":" + string.Join(",", o.Counts), o.Places is null ? "null" : string.Join(",", o.Places.Select(Dump)),
        o.Fixed.IsDefault ? "default" : string.Join(",", o.Fixed), o.Names is null ? "null" : string.Join(",", o.Names), o.Codes is null ? "null" : o.Codes.GetType().Name + ":" + string.Join(",", o.Codes),
        o.Frozen is null ? "null" : string.Join(",", o.Frozen), o.Weights is null ? "null" : string.Join(",", o.Weights.Select(w => w.Key + "=" + w.Value.ToString("R", CultureInfo.InvariantCulture))),
        o.Stock is null ? "null" : o.Stock.GetType().Name + ":" + string.Join(",", o.Stock.Select(e => e.Key + "=" + e.Value)), o.Grid is null ? "null" : string.Join(",", o.Grid.Select(e => e.Key + "=" + Dump(e.Value))),
        o.Seen is null ? "null" : o.Seen.GetType().Name + ":" + string.Join(",", o.Seen), o.Labels is null ? "null" : o.Labels.GetType().Name + ":" + string.Join(",", o.Labels),
        o.ByName is null ? "null" : o.ByName.GetType().Name + ":" + string.Join(",", o.ByName.Select(e => e.Key + "=" + Dump(e.Value))), o.Track is null ? "null" : o.Track.GetType().Name + ":" + string.Join(",", o.Track.Select(Dump)),
        o.Totals is null ? "null" : o.Totals.GetType().Name + ":" + string.Join(",", o.Totals)
      });
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
