using LsMsgPack;
using LsMsgPack.Converters;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Converters (MsgPackOptions.Converters, [MsgPackConverter]): values written as their surrogates by both serializers (CrossLibraryTests.SameBytesAndValuesConverters compares the bytes).
  /// </summary>
  [TestClass]
  public class ConverterTests
  {
    #region Model

    /// <summary>A value object with its converter in an attribute, written as a string.</summary>
    [MsgPackConverter(typeof(CvMoneyConverter))]
    public sealed class CvMoney
    {
      public CvMoney(decimal amount, string currency) { Amount = amount; Currency = currency; }
      public decimal Amount { get; }
      public string Currency { get; }
      public override bool Equals(object obj) { return obj is CvMoney other && other.Amount == Amount && other.Currency == Currency; }
      public override int GetHashCode() { return Amount.GetHashCode(); }
    }

    public sealed class CvMoneyConverter : MsgPackConverter<CvMoney, string>
    {
      public static int ToCalls;
      public static int FromCalls;

      public override string ToSurrogate(CvMoney value)
      {
        ToCalls++;
        return value.Amount.ToString(CultureInfo.InvariantCulture) + " " + value.Currency;
      }

      public override CvMoney FromSurrogate(string surrogate)
      {
        FromCalls++;
        int space = surrogate.IndexOf(' ');
        if (space < 0)
          throw new FormatException("Not an amount and a currency: " + surrogate);
        return new CvMoney(decimal.Parse(surrogate.Substring(0, space), CultureInfo.InvariantCulture), surrogate.Substring(space + 1));
      }
    }

    /// <summary>A struct written as its inner value.</summary>
    [MsgPackConverter(typeof(CvTemperatureConverter))]
    public struct CvTemperature
    {
      public CvTemperature(double celsius) { Celsius = celsius; }
      public double Celsius { get; }
    }

    public sealed class CvTemperatureConverter : MsgPackConverter<CvTemperature, double>
    {
      public override double ToSurrogate(CvTemperature value) { return value.Celsius; }
      public override CvTemperature FromSurrogate(double surrogate) { return new CvTemperature(surrogate); }
    }

    /// <summary>A class that is not sealed, converted through the options; a class deriving from it is written as an object.</summary>
    public class CvLabel
    {
      public string Text { get; set; }
    }

    public class CvSpecialLabel : CvLabel
    {
      public int Level { get; set; }
    }

    public sealed class CvLabelConverter : MsgPackConverter<CvLabel, string>
    {
      public override string ToSurrogate(CvLabel value) { return value.Text is null ? null : "label:" + value.Text; }
      public override CvLabel FromSurrogate(string surrogate) { return new CvLabel { Text = surrogate.Substring("label:".Length) }; }
    }

    /// <summary>A type of "another library" (no setters, no constructor the serializers bind), written as a class of our own.</summary>
    public sealed class CvThirdParty
    {
      private readonly int _x;
      private readonly int _y;
      private CvThirdParty(int x, int y) { _x = x; _y = y; }
      public static CvThirdParty Create(int x, int y) { return new CvThirdParty(x, y); }
      public int GetX() { return _x; }
      public int GetY() { return _y; }
      public override string ToString() { return _x + "," + _y; }
    }

    public class CvThirdPartyDto
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public sealed class CvThirdPartyConverter : MsgPackConverter<CvThirdParty, CvThirdPartyDto>
    {
      public override CvThirdPartyDto ToSurrogate(CvThirdParty value) { return new CvThirdPartyDto { X = value.GetX(), Y = value.GetY() }; }
      public override CvThirdParty FromSurrogate(CvThirdPartyDto surrogate) { return CvThirdParty.Create(surrogate.X, surrogate.Y); }
    }

    /// <summary>A surrogate type with derived classes: the surrogate gets a type id.</summary>
    public sealed class CvShape
    {
      public string Kind { get; set; }
      public double Size { get; set; }
    }

    public abstract class CvShapeDto { public double Size { get; set; } }
    public class CvCircleDto : CvShapeDto { public bool Filled { get; set; } }
    public class CvSquareDto : CvShapeDto { }

    public sealed class CvShapeConverter : MsgPackConverter<CvShape, CvShapeDto>
    {
      public override CvShapeDto ToSurrogate(CvShape value)
      {
        if (value.Kind == "circle")
          return new CvCircleDto { Size = value.Size, Filled = true };
        return new CvSquareDto { Size = value.Size };
      }

      public override CvShape FromSurrogate(CvShapeDto surrogate)
      {
        return new CvShape { Kind = surrogate is CvCircleDto ? "circle" : "square", Size = surrogate.Size };
      }
    }

    public class CvHolder
    {
      public CvMoney Price { get; set; }
      public CvMoney Missing { get; set; }
      public List<CvMoney> Prices { get; set; }
      public CvMoney[] PriceArray { get; set; }
      public Dictionary<string, CvMoney> ByName { get; set; }
      public Dictionary<CvMoney, int> ByPrice { get; set; }
      public object Boxed { get; set; }
      public object BoxedTemperature { get; set; }
      public CvLabel Label { get; set; }
      public CvLabel Special { get; set; }
      public CvTemperature Temperature { get; set; }
      public CvTemperature? MaybeTemperature { get; set; }
      public CvTemperature? NoTemperature { get; set; }
      public List<CvTemperature> Temperatures { get; set; }
      public CvThirdParty Point { get; set; }
      public List<CvThirdParty> Points { get; set; }
      public CvShape Shape { get; set; }
      public List<CvShape> Shapes { get; set; }
      public int After { get; set; }
    }

    /// <summary>
    /// The converters of the options (CvMoney and CvTemperature have attributes).
    /// </summary>
    internal static MsgPackConverter[] OptionConverters()
    {
      return new MsgPackConverter[] { new CvLabelConverter(), new CvThirdPartyConverter(), new CvShapeConverter() };
    }

    internal static CvHolder Full()
    {
      return new CvHolder
      {
        Price = new CvMoney(12.5m, "EUR"),
        Prices = new List<CvMoney> { new CvMoney(1m, "USD"), null, new CvMoney(-0.01m, "JPY") },
        PriceArray = new[] { new CvMoney(3m, "GBP") },
        ByName = new Dictionary<string, CvMoney> { { "a", new CvMoney(2m, "CHF") }, { "none", null } },
        ByPrice = new Dictionary<CvMoney, int> { { new CvMoney(9m, "SEK"), 9 } },
        Boxed = new CvMoney(4m, "DKK"),
        BoxedTemperature = new CvTemperature(-40),
        Label = new CvLabel { Text = "plain" },
        Special = new CvSpecialLabel { Text = "special", Level = 3 },
        Temperature = new CvTemperature(21.5),
        MaybeTemperature = new CvTemperature(0),
        Temperatures = new List<CvTemperature> { new CvTemperature(1), new CvTemperature(-2.25) },
        Point = CvThirdParty.Create(3, -4),
        Points = new List<CvThirdParty> { CvThirdParty.Create(0, 0), null },
        Shape = new CvShape { Kind = "circle", Size = 2 },
        Shapes = new List<CvShape> { new CvShape { Kind = "square", Size = 1 }, new CvShape { Kind = "circle", Size = 0.5 } },
        After = 7
      };
    }

    /// <summary>
    /// The values CrossLibraryTests writes and reads with both serializers (declared as their own type and as object).
    /// </summary>
    internal static IEnumerable<object> Corpus()
    {
      yield return Full();
      yield return new CvHolder();
      yield return NullSurrogateAndDefault();
      yield return new CvMoney(0m, "EUR");
      yield return new CvTemperature(37);
      yield return CvThirdParty.Create(1, 2);
      yield return new CvShape { Kind = "square", Size = 3 };
      yield return new CvSpecialLabel { Text = "derived root", Level = 1 };
      yield return new List<CvMoney> { new CvMoney(5m, "NOK") };
      yield return new object[] { new CvMoney(6m, "PLN"), new CvTemperature(6), CvThirdParty.Create(6, 6), new CvLabel { Text = "six" } };
      yield return new Dictionary<string, object> { { "money", new CvMoney(7m, "CZK") } };
      yield return MemberFull();
      yield return new CvMembers();
      yield return new CvMemberStruct { At = When, N = 3 };
      yield return new List<CvMemberStruct> { new CvMemberStruct { At = When }, default(CvMemberStruct) };
      yield return new CvMemberImmutable(When, "x");
    }

    #region Converters on members

    private static readonly DateTime When = new DateTime(2026, 11, 15, 10, 20, 30, 456, DateTimeKind.Utc);

    /// <summary>The members' own converters: on properties, a nullable, a field, and over a type's converter.</summary>
    public class CvMembers
    {
      [MsgPackConverter(typeof(CvUnixMilliseconds))]
      public DateTime At { get; set; }
      [MsgPackConverter(typeof(CvUnixMilliseconds))]
      public DateTime? MaybeAt { get; set; }
      [MsgPackConverter(typeof(CvUnixMilliseconds))]
      public DateTime? NoAt { get; set; }
      public DateTime Plain { get; set; }
      public List<DateTime> Dates { get; set; }
      [MsgPackConverter(typeof(CvMoneyAsCents))]
      public CvMoney Cents { get; set; }
      public CvMoney Text { get; set; }
      [MsgPackConverter(typeof(CvTemperatureAsText))]
      public CvTemperature Temperature { get; set; }
      [MsgPackConverter(typeof(CvThirdPartyConverter))]
      public CvThirdParty Point { get; set; }
      [DataMember]
      [MsgPackConverter(typeof(CvIntAsText))]
      public int Count;
    }

    /// <summary>The same members as <see cref="CvMembers"/>, declared as the surrogates (the bytes must be the same).</summary>
    public class CvMembersDto
    {
      public long At { get; set; }
      public long? MaybeAt { get; set; }
      public long? NoAt { get; set; }
      public DateTime Plain { get; set; }
      public List<DateTime> Dates { get; set; }
      public long Cents { get; set; }
      public string Text { get; set; }
      public string Temperature { get; set; }
      public CvThirdPartyDto Point { get; set; }
      [DataMember]
      public string Count;
    }

    public struct CvMemberStruct
    {
      [MsgPackConverter(typeof(CvUnixMilliseconds))]
      public DateTime At { get; set; }
      public int N { get; set; }
    }

    /// <summary>Created by the constructor that takes the values (the converted value is a constructor argument).</summary>
    public class CvMemberImmutable
    {
      public CvMemberImmutable(DateTime at, string name) { At = at; Name = name; }
      [MsgPackConverter(typeof(CvUnixMilliseconds))]
      public DateTime At { get; }
      public string Name { get; }
    }

    public class CvBadMember
    {
      [MsgPackConverter(typeof(CvIntAsText))]
      public long Wrong { get; set; }
    }

    public sealed class CvMoneyAsCents : MsgPackConverter<CvMoney, long>
    {
      public override long ToSurrogate(CvMoney value) { return (long)(value.Amount * 100); }
      public override CvMoney FromSurrogate(long surrogate) { return new CvMoney(surrogate / 100m, "EUR"); }
    }

    public sealed class CvTemperatureAsText : MsgPackConverter<CvTemperature, string>
    {
      public override string ToSurrogate(CvTemperature value) { return value.Celsius.ToString(CultureInfo.InvariantCulture) + "C"; }
      public override CvTemperature FromSurrogate(string surrogate) { return new CvTemperature(double.Parse(surrogate.TrimEnd('C'), CultureInfo.InvariantCulture)); }
    }

    internal static CvMembers MemberFull()
    {
      return new CvMembers
      {
        At = When, MaybeAt = When.AddDays(1), Plain = When, Dates = new List<DateTime> { When }, Cents = new CvMoney(12.34m, "EUR"), Text = new CvMoney(5m, "USD"),
        Temperature = new CvTemperature(-3.5), Point = CvThirdParty.Create(7, 8), Count = 42
      };
    }

    private static CvMembersDto MemberFullAsDto()
    {
      CvMembers m = MemberFull();
      return new CvMembersDto
      {
        At = new DateTimeOffset(m.At).ToUnixTimeMilliseconds(), MaybeAt = new DateTimeOffset(m.MaybeAt.Value).ToUnixTimeMilliseconds(), Plain = m.Plain, Dates = m.Dates, Cents = 1234, Text = "5 USD",
        Temperature = "-3.5C", Point = new CvThirdPartyDto { X = 7, Y = 8 }, Count = "#42"
      };
    }

    [TestMethod]
    [DataRow(false, ObjectLayout.Map)]
    [DataRow(false, ObjectLayout.Array)]
    public void Members_WrittenAsTheSurrogates(bool schema, ObjectLayout layout)
    {
      // Without the schema the class names are not written, so the bytes equal those of a class declaring the surrogates
      MsgPackSettings debug = DebugSettings(schema, layout);
      LsMsgPackSerializer ls = Ls(schema, layout);
      byte[] expected = MsgPackSerializer.Serialize(MemberFullAsDto(), typeof(CvMembersDto), debug);
      CollectionAssert.AreEqual(expected, MsgPackSerializer.Serialize(MemberFull(), typeof(CvMembers), debug), "Debug");
      CollectionAssert.AreEqual(expected, ls.Serialize(MemberFull()), "Ls");
      Assert.AreEqual(CrossLibraryTests.Dump(MemberFull()), CrossLibraryTests.Dump(ls.Deserialize<CvMembers>(expected)));
      Assert.AreEqual(CrossLibraryTests.Dump(MemberFull()), CrossLibraryTests.Dump(MsgPackSerializer.Deserialize<CvMembers>(expected, debug)));
    }

    [TestMethod]
    public void Members_TheConverterMustBeOfTheMembersType()
    {
      Assert.Throws<MsgPackException>(() => Ls().Serialize(new CvBadMember { Wrong = 1 }));
      Assert.Throws<MsgPackException>(() => MsgPackSerializer.Serialize(new CvBadMember { Wrong = 1 }, typeof(CvBadMember), DebugSettings()));
    }

    [TestMethod]
    [DataRow(SchemaVersion.Described, false)]
    [DataRow(SchemaVersion.Described, true)]
    public void Members_SchemaHasTheSurrogates(SchemaVersion version, bool complete)
    {
      LsMsgPackSerializer ls = new LsMsgPackSerializer(new LsMsgPackOptions { Converters = OptionConverters(), SchemaVersion = version, CompleteSchemas = complete });
      byte[] data = ls.Serialize(MemberFull());
      MsgPackSettings debug = DebugSettings();
      debug.SchemaVersion = version;
      debug.CompleteSchemas = complete;
      CollectionAssert.AreEqual(MsgPackSerializer.Serialize(MemberFull(), typeof(CvMembers), debug), data);
      string text = System.Text.Encoding.UTF8.GetString(data);
      foreach (string member in new[] { "CvThirdPartyDto", "Amount", "Celsius" })
        Assert.AreEqual(member == "CvThirdPartyDto", text.Contains(member, StringComparison.Ordinal), member);
      Assert.AreEqual(CrossLibraryTests.Dump(MemberFull()), CrossLibraryTests.Dump(ls.Deserialize<CvMembers>(data)));
    }

    #endregion

    /// <summary>
    /// A surrogate that is null (nil, read as null) and a struct that FilterDefaultValues leaves out (-0.0 is a default, read as 0).
    /// </summary>
    private static CvHolder NullSurrogateAndDefault()
    {
      return new CvHolder { Label = new CvLabel(), Temperature = new CvTemperature(-0.0) };
    }

    /// <summary>Built-in types with converters of the options: the typed handlers, the arrays in bulk, the decimal path and the generated and compiled code step aside.</summary>
    public class CvBuiltIns
    {
      public DateTime When { get; set; }
      public DateTime? Maybe { get; set; }
      public List<DateTime> Dates { get; set; }
      public int Count { get; set; }
      public int[] Ints { get; set; }
      public List<int> IntList { get; set; }
      public HashSet<int> IntSet { get; set; }
      public Dictionary<int, string> ByInt { get; set; }
      public decimal Price { get; set; }
      public decimal[] Prices { get; set; }
      public long Plain { get; set; }
      public long[] PlainArray { get; set; }
    }

    public sealed class CvUnixMilliseconds : MsgPackConverter<DateTime, long>
    {
      public override long ToSurrogate(DateTime value) { return new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds(); }
      public override DateTime FromSurrogate(long surrogate) { return DateTimeOffset.FromUnixTimeMilliseconds(surrogate).UtcDateTime; }
    }

    public sealed class CvIntAsText : MsgPackConverter<int, string>
    {
      public override string ToSurrogate(int value) { return "#" + value.ToString(CultureInfo.InvariantCulture); }
      public override int FromSurrogate(string surrogate) { return int.Parse(surrogate.Substring(1), CultureInfo.InvariantCulture); }
    }

    public sealed class CvDecimalAsDouble : MsgPackConverter<decimal, double>
    {
      public override double ToSurrogate(decimal value) { return (double)value; }
      public override decimal FromSurrogate(double surrogate) { return (decimal)surrogate; }
    }

    internal static MsgPackConverter[] BuiltInConverters()
    {
      return new MsgPackConverter[] { new CvUnixMilliseconds(), new CvIntAsText(), new CvDecimalAsDouble() };
    }

    internal static IEnumerable<object> BuiltInCorpus()
    {
      DateTime when = new DateTime(2026, 11, 14, 12, 30, 15, 250, DateTimeKind.Utc);
      yield return new CvBuiltIns
      {
        When = when, Maybe = when.AddDays(1), Dates = new List<DateTime> { when, DateTime.UnixEpoch }, Count = 42, Ints = new[] { 1, -2, 0 }, IntList = Enumerable.Range(0, 40).ToList(),
        IntSet = new HashSet<int> { 3 }, ByInt = new Dictionary<int, string> { { 5, "five" } }, Price = 2.5m, Prices = new[] { 0.5m, -1m }, Plain = 7, PlainArray = new long[] { 1, 2 }
      };
      yield return new CvBuiltIns();
      yield return when;
      yield return 12;
      yield return new[] { 1, 2, 3 };
      yield return 1.25m;
    }

    #endregion

    private static MsgPackSettings DebugSettings(bool schema = true, ObjectLayout layout = ObjectLayout.Array)
    {
      return new MsgPackSettings { Converters = OptionConverters(), UseInexedSchema = schema, ObjectLayout = layout };
    }

    private static LsMsgPackSerializer Ls(bool schema = true, ObjectLayout layout = ObjectLayout.Array)
    {
      return new LsMsgPackSerializer(new LsMsgPackOptions { Converters = OptionConverters(), UseInexedSchema = schema, ObjectLayout = layout });
    }

    [TestMethod]
    [DataRow(true, ObjectLayout.Array)]
    [DataRow(true, ObjectLayout.Map)]
    [DataRow(false, ObjectLayout.Array)]
    [DataRow(false, ObjectLayout.Map)]
    public void RoundTrip(bool schema, ObjectLayout layout)
    {
      MsgPackSettings debug = DebugSettings(schema, layout);
      LsMsgPackSerializer ls = Ls(schema, layout);
      foreach (object value in Corpus())
      {
        string expected = CrossLibraryTests.Dump(value);
        if (value is CvHolder h && h.Label != null && h.Label.Text is null)
          expected = CrossLibraryTests.Dump(new CvHolder()); // see NullSurrogateAndDefault
        Type type = value.GetType();
        Assert.AreEqual(expected, CrossLibraryTests.Dump(MsgPackSerializer.Deserialize(type, MsgPackSerializer.Serialize(value, type, debug), debug)), "Debug " + type.Name);
        Assert.AreEqual(expected, CrossLibraryTests.Dump(ls.Deserialize(type, ls.Serialize(value, type))), "Ls " + type.Name);
      }
    }

    [TestMethod]
    [DataRow(true, ObjectLayout.Array)]
    [DataRow(false, ObjectLayout.Map)]
    public void BuiltInTypes_RoundTrip(bool schema, ObjectLayout layout)
    {
      MsgPackSettings debug = new MsgPackSettings { Converters = BuiltInConverters(), UseInexedSchema = schema, ObjectLayout = layout };
      LsMsgPackSerializer ls = new LsMsgPackSerializer(new LsMsgPackOptions { Converters = BuiltInConverters(), UseInexedSchema = schema, ObjectLayout = layout });
      foreach (object value in BuiltInCorpus())
      {
        string expected = CrossLibraryTests.Dump(value);
        Type type = value.GetType();
        Assert.AreEqual(expected, CrossLibraryTests.Dump(MsgPackSerializer.Deserialize(type, MsgPackSerializer.Serialize(value, type, debug), debug)), "Debug " + type.Name);
        Assert.AreEqual(expected, CrossLibraryTests.Dump(ls.Deserialize(type, ls.Serialize(value, type))), "Ls " + type.Name);
      }
      LsMsgPackSerializer plain = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = schema, ObjectLayout = layout });
      CollectionAssert.AreEqual(plain.Serialize(new List<string> { "#1", "#2" }), ls.Serialize(new List<int> { 1, 2 }));
      CollectionAssert.AreEqual(plain.Serialize(0.5), ls.Serialize(0.5m));
    }

    [TestMethod]
    public void WrittenAsTheSurrogate()
    {
      LsMsgPackSerializer ls = Ls(false);
      LsMsgPackSerializer plain = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false });
      CollectionAssert.AreEqual(plain.Serialize("12.5 EUR"), ls.Serialize(new CvMoney(12.5m, "EUR")));
      CollectionAssert.AreEqual(plain.Serialize(21.5), ls.Serialize(new CvTemperature(21.5)));
      CollectionAssert.AreEqual(plain.Serialize(new CvThirdPartyDto { X = 3, Y = -4 }), ls.Serialize(CvThirdParty.Create(3, -4)));
      CollectionAssert.AreEqual(plain.Serialize(new List<string> { "label:x", null }), ls.Serialize(new List<CvLabel> { new CvLabel { Text = "x" }, new CvLabel() }));
    }

    /// <summary>
    /// The schema has entries for the surrogates, not for the converted types (only their names, where a type id names them).
    /// </summary>
    [TestMethod]
    [DataRow(SchemaVersion.Names, false)]
    [DataRow(SchemaVersion.Described, false)]
    [DataRow(SchemaVersion.Names, true)]
    [DataRow(SchemaVersion.Described, true)]
    public void SchemaHasTheSurrogates(SchemaVersion version, bool complete)
    {
      MsgPackSettings debug = DebugSettings();
      debug.SchemaVersion = version;
      debug.CompleteSchemas = complete;
      LsMsgPackSerializer ls = new LsMsgPackSerializer(new LsMsgPackOptions { Converters = OptionConverters(), SchemaVersion = version, CompleteSchemas = complete });
      byte[] data = ls.Serialize(Full());
      CollectionAssert.AreEqual(MsgPackSerializer.Serialize(Full(), typeof(CvHolder), debug), data);
      string text = System.Text.Encoding.UTF8.GetString(data);
      foreach (string member in new[] { "Amount", "Currency", "Celsius", "Kind" })
        Assert.DoesNotContain(member, text, $"{member} of a converted type is in the schema");
      foreach (string member in new[] { "CvThirdPartyDto", "Filled" })
        Assert.Contains(member, text);
      Assert.AreEqual(CrossLibraryTests.Dump(Full()), CrossLibraryTests.Dump(ls.Deserialize<CvHolder>(data)));
    }

    [TestMethod]
    public void NilIsNeverConverted()
    {
      foreach (bool debug in new[] { false, true })
      {
        CvMoneyConverter.ToCalls = 0;
        CvMoneyConverter.FromCalls = 0;
        CvHolder holder = new CvHolder { Prices = new List<CvMoney> { null } };
        CvHolder read = debug
          ? (CvHolder)MsgPackSerializer.Deserialize(typeof(CvHolder), MsgPackSerializer.Serialize(holder, typeof(CvHolder), DebugSettings()), DebugSettings())
          : Ls().Deserialize<CvHolder>(Ls().Serialize(holder));
        Assert.IsNull(read.Price);
        Assert.IsNull(read.Prices[0]);
        Assert.AreEqual(0, CvMoneyConverter.ToCalls, debug ? "Debug" : "Ls");
        Assert.AreEqual(0, CvMoneyConverter.FromCalls, debug ? "Debug" : "Ls");
      }
    }

    [TestMethod]
    public void DerivedClassesAreObjects()
    {
      LsMsgPackSerializer ls = Ls(false, ObjectLayout.Map);
      CvHolder read = ls.Deserialize<CvHolder>(ls.Serialize(new CvHolder { Label = new CvLabel { Text = "a" }, Special = new CvSpecialLabel { Text = "b", Level = 2 } }));
      Assert.AreEqual(typeof(CvLabel), read.Label.GetType());
      Assert.AreEqual("a", read.Label.Text);
      CvSpecialLabel special = (CvSpecialLabel)read.Special;
      Assert.AreEqual("b", special.Text);
      Assert.AreEqual(2, special.Level);
    }

    [TestMethod]
    public void ReadDifferencesAndStreams()
    {
      LsMsgPackSerializer ls = Ls();
      byte[] data = ls.Serialize(Full());
      CvHolder read = ls.Deserialize<CvHolder>(data, out ReadDifferences differences);
      Assert.AreEqual(CrossLibraryTests.Dump(Full()), CrossLibraryTests.Dump(read));
      Assert.IsTrue(differences is null || differences.Differences.Count == 0, differences?.ToString());
      read = MsgPackSerializer.Deserialize<CvHolder>(data, DebugSettings(), out ReadDifferences debugDifferences);
      Assert.AreEqual(CrossLibraryTests.Dump(Full()), CrossLibraryTests.Dump(read));
      Assert.IsTrue(debugDifferences is null || debugDifferences.Differences.Count == 0, debugDifferences?.ToString());

      CvHolder streamed = Full();
      streamed.Boxed = null; // a head's complete schema does not reach values declared as object
      streamed.BoxedTemperature = null;
      System.IO.MemoryStream stream = new System.IO.MemoryStream();
      using (LsMsgPack.Streaming.MsgPackStreamWriter writer = new LsMsgPack.Streaming.MsgPackStreamWriter(stream, ls, leaveOpen: true))
      {
        writer.WriteHead<List<CvHolder>>();
        writer.WriteArrayStart();
        writer.WriteObject(streamed);
        writer.WriteObject(new CvHolder { Price = new CvMoney(1m, "EUR") });
        writer.WriteEnd();
      }
      stream.Position = 0;
      using (LsMsgPack.Streaming.MsgPackStreamReader reader = new LsMsgPack.Streaming.MsgPackStreamReader(stream, ls))
      {
        reader.ReadHead<List<CvHolder>>();
        reader.Read(); // the array
        Assert.AreEqual(CrossLibraryTests.Dump(streamed), CrossLibraryTests.Dump(reader.ReadObject<CvHolder>()));
        Assert.AreEqual("1 EUR", new CvMoneyConverter().ToSurrogate(reader.ReadObject<CvHolder>().Price));
      }
    }

    [TestMethod]
    public void ConverterExceptionsArePassedOn()
    {
      LsMsgPackSerializer plain = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false });
      byte[] data = plain.Serialize("no-space");
      Assert.Throws<FormatException>(() => Ls(false).Deserialize<CvMoney>(data));
      Assert.Throws<FormatException>(() => MsgPackSerializer.Deserialize<CvMoney>(data, DebugSettings(false)));
    }

    [TestMethod]
    public void InvalidConverters()
    {
      Assert.Throws<ArgumentException>(() => new LsMsgPackOptions { Converters = new MsgPackConverter[] { new CvLabelConverter(), new CvLabelConverter() } });
      Assert.Throws<ArgumentException>(() => new LsMsgPackOptions { Converters = new MsgPackConverter[] { new CvObjectConverter() } });
      Assert.Throws<ArgumentException>(() => new LsMsgPackOptions { Converters = new MsgPackConverter[] { new CvNullableConverter() } });
      Assert.Throws<ArgumentException>(() => new LsMsgPackOptions { Converters = new MsgPackConverter[] { new CvPingConverter(), new CvPongConverter() } });
      Assert.Throws<ArgumentException>(() => new LsMsgPackOptions { Converters = new MsgPackConverter[] { new CvSelfConverter() } });
      new LsMsgPackOptions { Converters = new MsgPackConverter[] { new CvPingConverter() } }; // a chain that ends
    }

    public sealed class CvObjectConverter : MsgPackConverter<object, string>
    {
      public override string ToSurrogate(object value) { return value.ToString(); }
      public override object FromSurrogate(string surrogate) { return surrogate; }
    }

    public sealed class CvNullableConverter : MsgPackConverter<int?, string>
    {
      public override string ToSurrogate(int? value) { return value.ToString(); }
      public override int? FromSurrogate(string surrogate) { return int.Parse(surrogate, CultureInfo.InvariantCulture); }
    }

    public sealed class CvPing { }
    public sealed class CvPong { }

    public sealed class CvPingConverter : MsgPackConverter<CvPing, CvPong>
    {
      public override CvPong ToSurrogate(CvPing value) { return new CvPong(); }
      public override CvPing FromSurrogate(CvPong surrogate) { return new CvPing(); }
    }

    public sealed class CvPongConverter : MsgPackConverter<CvPong, CvPing>
    {
      public override CvPing ToSurrogate(CvPong value) { return new CvPing(); }
      public override CvPong FromSurrogate(CvPing surrogate) { return new CvPong(); }
    }

    public sealed class CvSelfConverter : MsgPackConverter<CvPing, CvPing>
    {
      public override CvPing ToSurrogate(CvPing value) { return value; }
      public override CvPing FromSurrogate(CvPing surrogate) { return surrogate; }
    }
  }
}
