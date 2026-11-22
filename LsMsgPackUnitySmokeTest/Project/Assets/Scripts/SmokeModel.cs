using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.Serialization;

// The model of LsMsgPackAotSmokeTest, without the types Unity's .NET Standard 2.1 profile lacks (DateOnly, TimeOnly, Half, Int128, Rune, immutable and frozen collections,
// System.Text.Json's attributes and interfaces). Compiled by Unity (Assembly-CSharp) and by Golden/Golden.csproj (.NET 10, writes the expected bytes and values).
namespace LsMsgPackUnitySmoke
{
  public enum InvoiceStatus { Draft, Sent, Paid }
  public enum Unit : byte { Piece, Hour, Kilogram }

  [MsgPackEnumFormat(EnumFormat.Name)]
  public enum Priority { Low, [EnumMember(Value = "high")] High }

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

  public class Extras
  {
    public BigInteger Giant { get; set; }
    public Complex Point { get; set; }
    public Version Release { get; set; }
    public (int, string) Pair { get; set; }
    public Tuple<int, string> Couple { get; set; }
    public KeyValuePair<string, int> Entry { get; set; }
    public IList<int> Counts { get; set; }
    public IReadOnlyList<Address> Places { get; set; }
    public List<KeyValuePair<string, double>> Weights { get; set; }
    public Dictionary<string, int> Stock { get; set; }
    public Dictionary<int, Vector> Grid { get; set; }
    public HashSet<int> Seen { get; set; }
    public ISet<string> Labels { get; set; }
    public IDictionary<string, Address> ByName { get; set; }
    public ICollection<Vector> Track { get; set; }
    public IEnumerable<long> Totals { get; set; }
    public int[] Samples { get; set; }
    public List<Guid> Keys { get; set; }
    public decimal[] Prices { get; set; }
    public List<double> Readings { get; set; }
  }

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
    public int Logged { get { return _log.Count; } }
  }

  public record Signature(string By, DateTime At);

  public struct Vector
  {
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int? Tag { get; set; }
    public Unit Unit { get; set; }
    public float Sum => X + Y + Z;
  }

  // A readonly record struct in the AOT smoke test: Unity compiles C# 9 (no record structs), this is its shape
  public readonly struct Fee
  {
    public Fee(decimal amount, string currency) { Amount = amount; Currency = currency; }
    public decimal Amount { get; init; }
    public string Currency { get; init; }
  }

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

  public struct FieldPoint
  {
    [DataMember] public float X;
    [DataMember] public float Y;
    [DataMember] private int _level;

    public int Level { get { return _level; } }

    public FieldPoint(float x, float y, int level)
    {
      X = x;
      Y = y;
      _level = level;
    }
  }

  public class Route
  {
    [DataMember] public string Driver;
    public int Stops;
    public System.Numerics.Vector3 Heading { get; set; }
    public FieldPoint Anchor { get; set; }
    public string Name { get; set; }
    public Vector Start { get; set; }
    public Vector? End { get; set; }
    public List<Vector> Points { get; set; }
    public Vector[] Corners { get; set; }
    public Fee Fee { get; set; }
    public InitLimits Limits { get; set; }
  }

  [LsMsgPack.Converters.MsgPackConverter(typeof(DiscountConverter))]
  public sealed class Discount
  {
    public decimal Percent { get; set; }
    public string Code { get; set; }
  }

  public sealed class DiscountConverter : LsMsgPack.Converters.MsgPackConverter<Discount, string>
  {
    public override string ToSurrogate(Discount value) { return value.Percent.ToString(CultureInfo.InvariantCulture) + "%" + value.Code; }
    public override Discount FromSurrogate(string surrogate) { int at = surrogate.IndexOf('%'); return new Discount { Percent = decimal.Parse(surrogate.Substring(0, at), CultureInfo.InvariantCulture), Code = surrogate.Substring(at + 1) }; }
  }

  [LsMsgPack.Converters.MsgPackConverter(typeof(StampConverter))]
  public sealed class Postmark
  {
    public Postmark(string office, int day) { Office = office; Day = day; }
    public string Office { get; }
    public int Day { get; }
  }

  public class PostmarkDto
  {
    public string Office { get; set; }
    public int Day { get; set; }
  }

  public sealed class StampConverter : LsMsgPack.Converters.MsgPackConverter<Postmark, PostmarkDto>
  {
    public override PostmarkDto ToSurrogate(Postmark value) { return new PostmarkDto { Office = value.Office, Day = value.Day }; }
    public override Postmark FromSurrogate(PostmarkDto surrogate) { return new Postmark(surrogate.Office, surrogate.Day); }
  }

  public sealed class Parcel
  {
    public Parcel(string code, double weight) { Code = code; Weight = weight; }
    public string Code { get; }
    public double Weight { get; }
  }

  public class ParcelDto
  {
    public string Code { get; set; }
    public double Weight { get; set; }
  }

  public sealed class ParcelConverter : LsMsgPack.Converters.MsgPackConverter<Parcel, ParcelDto>
  {
    public override ParcelDto ToSurrogate(Parcel value) { return new ParcelDto { Code = value.Code, Weight = value.Weight }; }
    public override Parcel FromSurrogate(ParcelDto surrogate) { return new Parcel(surrogate.Code, surrogate.Weight); }
  }

  public sealed class UnixSecondsConverter : LsMsgPack.Converters.MsgPackConverter<DateTime, long>
  {
    public override long ToSurrogate(DateTime value) { return new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeSeconds(); }
    public override DateTime FromSurrogate(long surrogate) { return DateTimeOffset.FromUnixTimeSeconds(surrogate).UtcDateTime; }
  }

  public struct Sample
  {
    public float A;
    public int B;
  }

  public class Remark
  {
    public string Text { get; set; }
    public MsgPackExtensionData Extra { get; set; }
  }

  public class Audit : IMsgPackOnSerializing, IMsgPackOnDeserializing, IMsgPackOnDeserialized
  {
    public string Written { get; set; }
    public string Read { get; set; }

    public void OnSerializing() { Written = "stamped"; }
    public void OnDeserializing() { Read = "created"; }
    public void OnDeserialized() { Read += "+filled " + Written; }
  }

  public class Delivery
  {
    public string Street { get; set; }
    internal string Origin;
  }

  public class Invoice
  {
    [MsgPackPopulate]
    public Delivery ShipTo { get; } = new Delivery() { Origin = "ctor" };
    public Audit Audit { get; set; }
    public Remark Remark { get; set; }
    [LsMsgPack.Converters.MsgPackConverter(typeof(LsMsgPack.Converters.BlitArrayConverter<Sample>))]
    public Sample[] Samples { get; set; }
    [LsMsgPack.Converters.MsgPackConverter(typeof(UnixSecondsConverter))]
    public DateTime Due { get; set; }
    [LsMsgPack.Converters.MsgPackConverter(typeof(UnixSecondsConverter))]
    public DateTime? Reminded { get; set; }
    public Discount Discount { get; set; }
    public List<Postmark> Postmarks { get; set; }
    public Parcel Parcel { get; set; }
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
    public Priority Priority { get; set; }
    [MsgPackEnumFormat(EnumFormat.Name)]
    public InvoiceStatus StatusName { get; set; }
    public Extras Extras { get; set; }
    public Payment Payment { get; set; }
    public Signature Signed { get; set; }
    public Route Route { get; set; }
    // Ignored by attributes LsMsgPack recognizes by name: Unity's High stripping kept them only once LsMsgPack.Core named their types (written otherwise)
    [IgnoreDataMember]
    public string Secret { get; set; }
    [System.Xml.Serialization.XmlIgnore]
    public string Draft { get; set; }
  }
}

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
  // init accessors and records with Unity's compiler (C# 9 on .NET Standard 2.1)
  internal static class IsExternalInit { }
}
#endif
