using LsMsgPack;
using LsMsgPackDebug;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace LsMsgPackUnitySmoke
{
  /// <summary>
  /// The checks of LsMsgPackAotSmokeTest for Unity: .NET 10 (Golden) writes the bytes and values of each option set, Unity (Mono or IL2CPP) writes and reads the same.
  /// No UnityEngine here: Golden compiles this file too.
  /// </summary>
  public static class SmokeChecks
  {
    private static readonly SchemaStore Store = new SchemaStore();

    private static readonly LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIncludeStatically[] PublicFields = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIncludeStatically[]
    {
      new LsMsgPack.TypeResolving.Filters.FilterNonSettable(LsMsgPack.TypeResolving.Filters.GetOnlyProperties.All, LsMsgPack.TypeResolving.Filters.NonPublicProperties.None, LsMsgPack.TypeResolving.Filters.SerializedFields.Public),
      new LsMsgPack.TypeResolving.Filters.FilterIgnoredAttribute(),
      new LsMsgPack.TypeResolving.Filters.FilterStatic()
    };

    /// <summary>
    /// How the LsMsgPack option sets get their serializer: by default the runtime plans (no context, not the registered ones); Unity's runner also checks a context's generated code.
    /// </summary>
    public static Func<LsMsgPackOptions, LsMsgPackSerializer> Create = Runtime;

    public static LsMsgPackSerializer Runtime(LsMsgPackOptions options)
    {
      options.Contexts = null;
      options.UseRegisteredContexts = false;
      return new LsMsgPackSerializer(options);
    }

    private static T Converted<T>(T options) where T : MsgPackOptions
    {
      options.Converters = new LsMsgPack.Converters.MsgPackConverter[] { new ParcelConverter() };
      return options;
    }

    public static IEnumerable<(string Name, Func<LsMsgPackOptions> Options)> OptionSets()
    {
      yield return ("default", () => Converted(new LsMsgPackOptions()));
      yield return ("names-map", () => Converted(new LsMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }));
      yield return ("names-array", () => Converted(new LsMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array }));
      yield return ("inline-map", () => Converted(new LsMsgPackOptions() { ObjectLayout = ObjectLayout.Map }));
      yield return ("reference", () => Converted(new LsMsgPackOptions() { SchemaStore = Store, WriteSchemaReference = true }));
      yield return ("trimmed", () => Converted(new LsMsgPackOptions() { TrimTrailingNulls = true, DynamicallyCompact = false }));
      yield return ("public-fields", () => Converted(new LsMsgPackOptions() { StaticFilters = PublicFields }));
      yield return ("naming-policies", () => Converted(new LsMsgPackOptions()
      {
        UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, EnumFormat = EnumFormat.Name,
        PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower, DictionaryKeyPolicy = MsgPackNamingPolicy.CamelCase, EnumNamingPolicy = MsgPackNamingPolicy.KebabCaseLower
      }));
      yield return ("preserve", () => Converted(new LsMsgPackOptions() { ReferenceHandling = ReferenceHandling.Preserve }));
    }

    /// <summary>
    /// LsMsgPackDebug, the item-tree serializer: the same bytes as LsMsgPack.
    /// </summary>
    public static IEnumerable<(string Name, Func<MsgPackSettings> Settings)> DebugSets()
    {
      yield return ("debug-default", () => Converted(new MsgPackSettings()));
      yield return ("debug-names-map", () => Converted(new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }));
    }

    private static byte[] Write(string name, Invoice sample)
    {
      foreach ((string set, Func<LsMsgPackOptions> options) in OptionSets())
        if (set == name)
          return Create(options()).Serialize(sample);
      foreach ((string set, Func<MsgPackSettings> settings) in DebugSets())
        if (set == name)
          return MsgPackSerializer.Serialize(sample, settings());
      throw new ArgumentException("unknown option set " + name);
    }

    private static Invoice Read(string name, byte[] data)
    {
      foreach ((string set, Func<LsMsgPackOptions> options) in OptionSets())
        if (set == name)
          return Create(options()).Deserialize<Invoice>(data);
      foreach ((string set, Func<MsgPackSettings> settings) in DebugSets())
        if (set == name)
          return MsgPackSerializer.Deserialize<Invoice>(data, settings());
      throw new ArgumentException("unknown option set " + name);
    }

    private static IEnumerable<string> Names()
    {
      return OptionSets().Select(o => o.Name).Concat(DebugSets().Select(d => d.Name));
    }

    /// <summary>
    /// The expected data: name|hex|values per line.
    /// </summary>
    public static string Golden(Action<string> log)
    {
      StringBuilder golden = new StringBuilder();
      foreach (string name in Names())
      {
        byte[] bytes = Write(name, Sample());
        string values = Dump(Read(name, bytes));
        golden.Append(name).Append('|').Append(Hex(bytes)).Append('|').Append(values).Append('\n');
        log($"{name}: {bytes.Length} bytes");
      }
      return golden.ToString();
    }

    public static int Check(string golden, Action<string> log, string label = null)
    {
      int failures = 0;
      foreach (string line in golden.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
      {
        string[] parts = line.Split('|');
        string name = parts[0];
        if (label != null && name.StartsWith("debug-", StringComparison.Ordinal))
          continue; // LsMsgPackDebug has no generated code
        byte[] expected = FromHex(parts[1]);
        string values = parts[2];
        string problem = Problem(name, expected, values);
        if (problem == null && name == "preserve")
        {
          Invoice read = Read(name, expected);
          if (!ReferenceEquals(read.Customer.Billing, read.Customer.Shipping))
            problem = "the shared address was read as two objects";
        }
        log($"{name}{label}: {problem ?? "OK"}");
        if (problem != null)
          failures++;
      }
      return failures;
    }

    private static string Problem(string name, byte[] expected, string values)
    {
      try
      {
        byte[] written = Write(name, Sample());
        string read = Dump(Read(name, expected)); // also when the bytes differ: reading on its own
        string problem = null;
        if (!written.AsSpan().SequenceEqual(expected))
          problem = $"other bytes\n  expected {Hex(expected)}\n  written  {Hex(written)}";
        if (read != values)
          problem = (problem == null ? "" : problem + "\n") + $"other values\n  expected {values}\n  read     {read}";
        return problem;
      }
      catch (Exception ex)
      {
        return ex.ToString();
      }
    }

    public static string Hex(byte[] bytes)
    {
      StringBuilder text = new StringBuilder(bytes.Length * 2);
      for (int i = 0; i < bytes.Length; i++)
        text.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
      return text.ToString();
    }

    public static byte[] FromHex(string hex)
    {
      byte[] bytes = new byte[hex.Length / 2];
      for (int i = 0; i < bytes.Length; i++)
        bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
      return bytes;
    }

    // float.NaN and double.NaN are negative NaNs on .NET (FFC00000), positive ones under IL2CPP (7FC00000): the same bits on both
    private static readonly float NegativeNaN = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00000));
    private static readonly double NegativeDoubleNaN = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000000));

    public static Invoice Sample()
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
          Shipping = home,
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
        Discount = new Discount { Percent = 12.5m, Code = "AUTUMN" },
        Due = new DateTime(2026, 11, 30, 12, 0, 0, DateTimeKind.Utc),
        Samples = new[] { new Sample { A = 1.5f, B = -2 }, new Sample { A = NegativeNaN, B = int.MaxValue } },
        Reminded = new DateTime(2026, 12, 7, 9, 0, 0, DateTimeKind.Utc),
        Postmarks = new List<Postmark> { new Postmark("Utrecht", 12), null },
        Parcel = new Parcel("PX-7", 2.25),
        Remark = SampleRemark(),
        ShipTo = { Street = "Dock 4" },
        Audit = new Audit(),
        Urgent = true,
        Priority = Priority.High,
        StatusName = InvoiceStatus.Paid,
        Payment = new Payment(100.5m) { Note = "first" },
        Signed = new Signature("Clerk", new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc)),
        Route = SampleRoute(),
        Secret = "not written",
        Draft = "not written",
        Extras = new Extras()
        {
          Giant = BigInteger.Pow(3, 50),
          Point = new Complex(1.5, -2),
          Release = new Version(1, 2, 3),
          Pair = (4, "four"),
          Couple = Tuple.Create(5, "five"),
          Entry = new KeyValuePair<string, int>("six", 6),
          Counts = new List<int>() { 7, 8 },
          Places = new List<Address>() { home },
          Weights = new List<KeyValuePair<string, double>>() { new KeyValuePair<string, double>("w", 0.5) },
          Stock = new Dictionary<string, int>() { { "a", 1 }, { "b", 300 } },
          Grid = new Dictionary<int, Vector>() { { 1, new Vector() { X = 1 } } },
          Seen = new HashSet<int>() { 3, 2, 1 },
          Labels = new HashSet<string>() { "l" },
          ByName = new Dictionary<string, Address>() { { "home", home } },
          Track = new List<Vector>() { new Vector() { Y = 2 } },
          Totals = new long[] { long.MaxValue },
          Samples = new[] { 1, -1, 300, int.MinValue },
          Keys = new List<Guid>() { new Guid("7d3c1b2a-5e4f-4a6b-9c8d-0e1f2a3b4c5d"), Guid.Empty },
          Prices = new[] { 12.5m, -0.01m, decimal.MaxValue },
          Readings = new List<double>() { 0.5, NegativeDoubleNaN, -1e300 }
        }
      };
    }

    private static Route SampleRoute()
    {
      return new Route()
      {
        Driver = "Kim",
        Stops = 4,
        Heading = new System.Numerics.Vector3(0.5f, -1, 2),
        Anchor = new FieldPoint(3, -4.5f, 2),
        Name = "delivery",
        Start = new Vector() { X = 1, Y = -2.5f, Z = 3, Tag = 4, Unit = Unit.Kilogram },
        End = new Vector() { Z = 9 },
        Points = new List<Vector>() { new Vector() { X = 0.5f }, default(Vector) },
        Corners = new[] { new Vector() { Y = 7, Tag = 0 } },
        Fee = new Fee(12.5m, "EUR"),
        Limits = new InitLimits() { Min = 300, Max = 70000, Rate = 0.25, On = true, Point = new InitPoint() { X = 300, Y = -300 } }
      };
    }

    private static Remark SampleRemark()
    {
      Remark remark = new Remark() { Text = "kept", Extra = new MsgPackExtensionData() };
      remark.Extra.SetRaw("Note", new byte[] { 0xA2, (byte)'h', (byte)'i' });
      remark.Extra.SetRaw("Count", new byte[] { 0xCD, 0x01, 0x2C });
      return remark;
    }

    private static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    private static string D(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    private static string M(decimal value) { return value.ToString(CultureInfo.InvariantCulture); }
    private static string Time(DateTime value) { return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }

    private static string Dump(Route o)
    {
      if (o is null)
        return "null";
      return $"[{o.Driver ?? "null"} {o.Stops} {F(o.Heading.X)},{F(o.Heading.Y)},{F(o.Heading.Z)} {F(o.Anchor.X)},{F(o.Anchor.Y)},{o.Anchor.Level} {o.Name ?? "null"} {Dump(o.Start)} "
        + $"{(o.End.HasValue ? Dump(o.End.Value) : "null")} {(o.Points is null ? "null" : string.Join(",", o.Points.Select(Dump)))} "
        + $"{(o.Corners is null ? "null" : string.Join(",", o.Corners.Select(Dump)))} {M(o.Fee.Amount)}/{o.Fee.Currency ?? "null"} {Dump(o.Limits)}]";
    }

    private static string Dump(InitLimits o)
    {
      return o is null ? "null" : $"({o.Min} {o.Max} {D(o.Rate)} {o.On} {o.Point.X} {o.Point.Y})";
    }

    private static string Dump(Vector o)
    {
      return $"({F(o.X)} {F(o.Y)} {F(o.Z)} {o.Tag?.ToString(CultureInfo.InvariantCulture) ?? "null"} {o.Unit})";
    }

    private static string Dump(Remark o)
    {
      if (o is null)
        return "null";
      StringBuilder text = new StringBuilder(o.Text ?? "null");
      if (o.Extra != null)
        foreach (string name in o.Extra.Names)
          text.Append(' ').Append(name).Append('=').Append(o.Extra.TryGetRaw(name, out byte[] raw) ? Hex(raw) : "?");
      return text.ToString();
    }

    public static string Dump(Invoice o)
    {
      if (o is null)
        return "null";
      StringBuilder text = new StringBuilder();
      text.Append(o.Id).Append(';').Append(o.Number).Append(';').Append(Time(o.Date)).Append(';').Append(o.Paid.HasValue ? Time(o.Paid.Value) : "null").Append(';')
        .Append(o.Created.ToString("o", CultureInfo.InvariantCulture)).Append(';').Append(o.Term.Ticks).Append(';').Append(o.Status).Append(';')
        .Append(Dump(o.Customer)).Append(';').Append(o.Lines is null ? "null" : string.Join(",", o.Lines.Select(Dump))).Append(';')
        .Append(o.Extra is null ? "null" : string.Join(",", o.Extra.Select(Dump))).Append(';').Append(o.Reference?.ToString() ?? "null").Append(';')
        .Append(o.Numbers is null ? "null" : string.Join(",", o.Numbers)).Append(';').Append(Dump(o.Stamp)).Append(';')
        .Append(o.Shapes is null ? "null" : string.Join(",", o.Shapes.Select(Dump))).Append(';').Append(M(o.Total)).Append(';').Append(o.Urgent).Append(';')
        .Append(o.Priority).Append(';').Append(o.StatusName).Append(';').Append(Dump(o.Extras)).Append(';')
        .Append(o.Payment is null ? "null" : $"[{M(o.Payment.Amount)} {o.Payment.Note ?? "null"} {o.Payment.Logged}]").Append(';')
        .Append(o.Signed is null ? "null" : $"[{o.Signed.By ?? "null"} {Time(o.Signed.At)}]").Append(';').Append(Dump(o.Route)).Append(';')
        .Append(o.Discount is null ? "null" : M(o.Discount.Percent) + "/" + o.Discount.Code).Append(';')
        .Append(Time(o.Due)).Append(';').Append(o.Samples is null ? "null" : string.Join(",", o.Samples.Select(x => F(x.A) + "/" + x.B))).Append(';')
        .Append(o.Reminded.HasValue ? Time(o.Reminded.Value) : "null").Append(';')
        .Append(o.Postmarks is null ? "null" : string.Join(",", o.Postmarks.Select(p => p is null ? "null" : p.Office + "/" + p.Day))).Append(';')
        .Append(o.Parcel is null ? "null" : o.Parcel.Code + "/" + D(o.Parcel.Weight)).Append(';')
        .Append(Dump(o.Remark)).Append(';')
        .Append(o.ShipTo is null ? "null" : o.ShipTo.Street + "/" + o.ShipTo.Origin).Append(';')
        .Append(o.Audit is null ? "null" : o.Audit.Written + "/" + o.Audit.Read);
      return text.ToString();
    }

    private static string Dump(Extras o)
    {
      if (o is null)
        return "null";
      return string.Join(" ", new string[]
      {
        o.Giant.ToString(CultureInfo.InvariantCulture), D(o.Point.Real) + "/" + D(o.Point.Imaginary),
        o.Release?.ToString() ?? "null", o.Pair.Item1 + "," + (o.Pair.Item2 ?? "null"), o.Couple is null ? "null" : o.Couple.Item1 + "," + o.Couple.Item2, o.Entry.Key + "=" + o.Entry.Value,
        o.Counts is null ? "null" : o.Counts.GetType().Name + ":" + string.Join(",", o.Counts), o.Places is null ? "null" : string.Join(",", o.Places.Select(Dump)),
        o.Weights is null ? "null" : string.Join(",", o.Weights.Select(w => w.Key + "=" + D(w.Value))),
        o.Stock is null ? "null" : o.Stock.GetType().Name + ":" + string.Join(",", o.Stock.Select(e => e.Key + "=" + e.Value)), o.Grid is null ? "null" : string.Join(",", o.Grid.Select(e => e.Key + "=" + Dump(e.Value))),
        o.Seen is null ? "null" : o.Seen.GetType().Name + ":" + string.Join(",", o.Seen), o.Labels is null ? "null" : o.Labels.GetType().Name + ":" + string.Join(",", o.Labels),
        o.ByName is null ? "null" : o.ByName.GetType().Name + ":" + string.Join(",", o.ByName.Select(e => e.Key + "=" + Dump(e.Value))), o.Track is null ? "null" : o.Track.GetType().Name + ":" + string.Join(",", o.Track.Select(Dump)),
        o.Totals is null ? "null" : o.Totals.GetType().Name + ":" + string.Join(",", o.Totals),
        o.Samples is null ? "null" : string.Join(",", o.Samples), o.Keys is null ? "null" : string.Join(",", o.Keys),
        o.Prices is null ? "null" : string.Join(",", o.Prices.Select(M)), o.Readings is null ? "null" : string.Join(",", o.Readings.Select(D))
      });
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
      return o is null ? "null" : $"[{o.LineNumber} {o.Description} {M(o.Quantity)} {o.Unit} {M(o.UnitPrice)}]";
    }

    private static string Dump(Shape o)
    {
      switch (o)
      {
        case null: return "null";
        case Circle circle: return $"Circle[{circle.Label ?? "null"} {D(circle.Radius)}]";
        case Square square: return $"Square[{square.Label ?? "null"} {D(square.Side)}]";
        default: return o.GetType().Name;
      }
    }
  }
}
