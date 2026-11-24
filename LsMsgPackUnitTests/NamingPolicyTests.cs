using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LsMsgPackUnitTests
{
  #region Model

  public enum NpStatus { NotStarted, InProgress, Done, [EnumMember(Value = "Custom-Name")] Custom }

  [Flags]
  public enum NpRights { None = 0, CanRead = 1, CanWrite = 2, IsOwner = 4 }

  public class NpAddress
  {
    public string StreetName { get; set; }
    public string City { get; set; }
  }

  public class NpLine
  {
    public string ProductCode { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
  }

  public class NpOrder
  {
    public int OrderId { get; set; }
    public string CustomerName { get; set; }
    public string URLValue { get; set; }
    public NpAddress ShipTo { get; set; }
    public List<NpLine> Lines { get; set; }
    public Dictionary<string, int> CountsByName { get; set; }
    public Dictionary<int, string> NamesById { get; set; }
    public NpStatus Status { get; set; }
    public NpRights Rights { get; set; }
    public object Boxed { get; set; }
  }

  public class NpRenamed
  {
    [JsonPropertyName("Given_Name")] public string GivenName { get; set; }
    public string FamilyName { get; set; }
  }

  public class NpClash
  {
    public int Id { get; set; }
    public int ID { get; set; }
  }

  public class NpKept
  {
    public string KnownValue { get; set; }
    [JsonExtensionData] public IDictionary<string, object> Rest { get; set; }
  }

  public class NpKeptNewer
  {
    public string KnownValue { get; set; }
    public string NewerValue { get; set; }
  }

  #endregion

  public abstract class NamingPolicyTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    /// <summary>
    /// Two keys for one property: LsMsgPack sets the last one (as for duplicate keys), LsMsgPackDebug the exact name.
    /// </summary>
    protected virtual bool LastKeyWins { get { return true; } }

    private static readonly MsgPackSettings Untyped = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map };

    internal static readonly MsgPackNamingPolicy[] Policies = new MsgPackNamingPolicy[]
    {
      MsgPackNamingPolicy.CamelCase, MsgPackNamingPolicy.SnakeCaseLower, MsgPackNamingPolicy.SnakeCaseUpper, MsgPackNamingPolicy.KebabCaseLower, MsgPackNamingPolicy.KebabCaseUpper
    };

    private static readonly JsonNamingPolicy[] JsonPolicies = new JsonNamingPolicy[]
    {
      JsonNamingPolicy.CamelCase, JsonNamingPolicy.SnakeCaseLower, JsonNamingPolicy.SnakeCaseUpper, JsonNamingPolicy.KebabCaseLower, JsonNamingPolicy.KebabCaseUpper
    };

    internal static NpOrder Sample()
    {
      return new NpOrder()
      {
        OrderId = 42,
        CustomerName = "Ann",
        URLValue = "https://example.org",
        ShipTo = new NpAddress() { StreetName = "Main Street", City = "Utrecht" },
        Lines = new List<NpLine>() { new NpLine() { ProductCode = "P1", UnitPrice = 2.5m, Quantity = 3 }, new NpLine() { ProductCode = "P2", UnitPrice = 10m, Quantity = 1 } },
        CountsByName = new Dictionary<string, int>() { { "FirstKey", 1 }, { "SecondKey", 2 } },
        NamesById = new Dictionary<int, string>() { { 1, "One" } },
        Status = NpStatus.InProgress,
        Rights = NpRights.CanRead | NpRights.IsOwner,
        Boxed = new NpAddress() { StreetName = "Side Street", City = "Delft" }
      };
    }

    internal static IEnumerable<MsgPackSettings> AllSettings(MsgPackNamingPolicy policy)
    {
      foreach (bool schema in new bool[] { false, true })
      {
        foreach (ObjectLayout layout in new ObjectLayout[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          yield return new MsgPackSettings() { UseIndexedSchema = schema, ObjectLayout = layout, PropertyNamingPolicy = policy };
          yield return new MsgPackSettings() { UseIndexedSchema = schema, ObjectLayout = layout, PropertyNamingPolicy = policy, SchemaVersion = SchemaVersion.Described };
        }
      }
    }

    private static void AssertSame(NpOrder expected, NpOrder actual, string message)
    {
      Assert.AreEqual(expected.OrderId, actual.OrderId, message);
      Assert.AreEqual(expected.CustomerName, actual.CustomerName, message);
      Assert.AreEqual(expected.URLValue, actual.URLValue, message);
      Assert.AreEqual(expected.ShipTo.StreetName, actual.ShipTo.StreetName, message);
      Assert.AreEqual(expected.ShipTo.City, actual.ShipTo.City, message);
      Assert.AreEqual(expected.Lines.Count, actual.Lines.Count, message);
      for (int t = 0; t < expected.Lines.Count; t++)
      {
        Assert.AreEqual(expected.Lines[t].ProductCode, actual.Lines[t].ProductCode, message);
        Assert.AreEqual(expected.Lines[t].UnitPrice, actual.Lines[t].UnitPrice, message);
        Assert.AreEqual(expected.Lines[t].Quantity, actual.Lines[t].Quantity, message);
      }
      Assert.AreEqual(expected.Status, actual.Status, message);
      Assert.AreEqual(expected.Rights, actual.Rights, message);
      CollectionAssert.AreEquivalent(expected.NamesById.ToList(), actual.NamesById.ToList(), message);
      Assert.AreEqual(((NpAddress)expected.Boxed).City, ((NpAddress)actual.Boxed).City, message);
    }

    private static string[] Keys(object map)
    {
      return Map(map).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// A map read untyped: the root as a dictionary, nested maps as their raw pairs.
    /// </summary>
    private static Dictionary<string, object> Map(object map)
    {
      if (map is Dictionary<string, object> dictionary)
        return dictionary;
      Dictionary<string, object> result = new Dictionary<string, object>();
      foreach (KeyValuePair<object, object> pair in (KeyValuePair<object, object>[])map)
        result[pair.Key.ToString()] = pair.Value;
      return result;
    }

    [TestMethod]
    public void Policies_SameNamesAsSystemTextJson()
    {
      string[] names = new string[]
      {
        "FirstName", "URLValue", "ID", "Id", "IOStream", "XMLHttpRequest", "SHA512Hash", "already_snake", "Name With Spaces", "  Leading", "Trailing  ", "Ünïcode", "ÄpfelBäume",
        "ABC", "a", "A", "", "Hello2World", "IPv6Address", "Some-Kebab", "MyURL", "_private", "Ab__Cd", "X1Y2", "Über", "camelCase", "PascalCase", "UPPER_CASE", "With.Dot", "Tab\tHere", "ǅungla"
      };
      for (int p = 0; p < Policies.Length; p++)
      {
        for (int t = 0; t < names.Length; t++)
          Assert.AreEqual(JsonPolicies[p].ConvertName(names[t]), Policies[p].ConvertName(names[t]), $"policy {p}, \"{names[t]}\"");
      }
    }

    [TestMethod]
    public void PropertyNames_WrittenByThePolicy()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower };
      Dictionary<string, object> written = Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(Sample(), settings), Untyped);
      CollectionAssert.AreEqual(new string[] { "boxed", "counts_by_name", "customer_name", "lines", "names_by_id", "order_id", "rights", "ship_to", "status", "url_value" }, Keys(written));
      CollectionAssert.AreEqual(new string[] { "city", "street_name" }, Keys(written["ship_to"]));
      object line = ((object[])written["lines"])[0];
      CollectionAssert.AreEqual(new string[] { "product_code", "quantity", "unit_price" }, Keys(line));

      written = Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(Sample(), new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.CamelCase }), Untyped);
      CollectionAssert.AreEqual(new string[] { "boxed", "countsByName", "customerName", "lines", "namesById", "orderId", "rights", "shipTo", "status", "urlValue" }, Keys(written));
    }

    [TestMethod]
    public void RoundTrip_AllSettings()
    {
      NpOrder value = Sample();
      for (int p = 0; p < Policies.Length; p++)
      {
        foreach (MsgPackSettings settings in AllSettings(Policies[p]))
        {
          string message = $"policy {p}, schema {settings.UseIndexedSchema}, {settings.ObjectLayout}, {settings.SchemaVersion}";
          AssertSame(value, Serializer.Deserialize<NpOrder>(Serializer.Serialize(value, settings), settings), message);
        }
      }
    }

    [TestMethod]
    public void Reading_TheDotNetNamesToo()
    {
      NpOrder value = Sample();
      for (int p = 0; p < Policies.Length; p++)
      {
        foreach (MsgPackSettings reader in AllSettings(Policies[p]))
        {
          MsgPackSettings writer = new MsgPackSettings() { UseIndexedSchema = reader.UseIndexedSchema, ObjectLayout = reader.ObjectLayout, SchemaVersion = reader.SchemaVersion };
          string message = $"policy {p}, schema {reader.UseIndexedSchema}, {reader.ObjectLayout}, {reader.SchemaVersion}";
          AssertSame(value, Serializer.Deserialize<NpOrder>(Serializer.Serialize(value, writer), reader), "written without the policy: " + message);
        }
      }
    }

    [TestMethod]
    public void Reading_IgnoringCase()
    {
      NpOrder value = Sample();
      // A camelCase writer read by a reader without the policy (PropertyNameCaseInsensitive, as the web formatters)
      foreach (MsgPackSettings writer in AllSettings(MsgPackNamingPolicy.CamelCase))
      {
        MsgPackSettings reader = new MsgPackSettings() { UseIndexedSchema = writer.UseIndexedSchema, ObjectLayout = writer.ObjectLayout, PropertyNameCaseInsensitive = true };
        AssertSame(value, Serializer.Deserialize<NpOrder>(Serializer.Serialize(value, writer), reader), $"schema {writer.UseIndexedSchema}, {writer.ObjectLayout}, {writer.SchemaVersion}");
      }
      // Upper case snake names read by a snake_case reader ignoring case, and .NET names in another case
      MsgPackSettings upper = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseUpper };
      MsgPackSettings lower = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };
      AssertSame(value, Serializer.Deserialize<NpOrder>(Serializer.Serialize(value, upper), lower), "SNAKE_CASE");
      Dictionary<string, object> map = new Dictionary<string, object>() { { "customername", "Bob" }, { "ORDERID", 7 } };
      NpOrder read = Serializer.Deserialize<NpOrder>(Serializer.Serialize(map, Untyped), lower);
      Assert.AreEqual("Bob", read.CustomerName);
      Assert.AreEqual(7, read.OrderId);
      MsgPackSettings exact = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower };
      read = Serializer.Deserialize<NpOrder>(Serializer.Serialize(map, Untyped), exact);
      Assert.IsNull(read.CustomerName, "not ignoring case without the option");
    }

    [TestMethod]
    public void Reading_BothNames()
    {
      // A map with both the snake_case name and the .NET name: LsMsgPackDebug sets the written name, LsMsgPack the last key (as for duplicate keys)
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower };
      foreach (bool first in new bool[] { false, true })
      {
        Dictionary<string, object> map = first
          ? new Dictionary<string, object>() { { "customer_name", "Written" }, { "CustomerName", "DotNet" } }
          : new Dictionary<string, object>() { { "CustomerName", "DotNet" }, { "customer_name", "Written" } };
        string expected = LastKeyWins && first ? "DotNet" : "Written";
        Assert.AreEqual(expected, Serializer.Deserialize<NpOrder>(Serializer.Serialize(map, Untyped), settings).CustomerName, $"written name first: {first}");
      }
    }

    [TestMethod]
    public void AttributeNames_Verbatim()
    {
      NpRenamed value = new NpRenamed() { GivenName = "Ann", FamilyName = "Smith" };
      foreach (bool schema in new bool[] { false, true })
      {
        MsgPackSettings settings = new MsgPackSettings()
        {
          UseIndexedSchema = schema, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.CamelCase,
          PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new AttributePropertyNameResolver() }
        };
        byte[] data = Serializer.Serialize(value, settings);
        if (!schema)
          CollectionAssert.AreEqual(new string[] { "Given_Name", "familyName" }, Keys(Serializer.Deserialize<Dictionary<string, object>>(data, Untyped)));
        NpRenamed back = Serializer.Deserialize<NpRenamed>(data, settings);
        Assert.AreEqual("Ann", back.GivenName, $"schema {schema}");
        Assert.AreEqual("Smith", back.FamilyName, $"schema {schema}");
      }
    }

    [TestMethod]
    public void SameConvertedName_Refused()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.CamelCase };
      MsgPackException error = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new NpClash(), settings));
      StringAssert.Contains(error.Message, "\"id\"");
      settings.PropertyNamingPolicy = null;
      Serializer.Serialize(new NpClash(), settings); // without the policy the names differ
    }

    [TestMethod]
    public void CustomPolicy()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = new PrefixPolicy() };
      NpAddress value = new NpAddress() { StreetName = "Main", City = "Utrecht" };
      byte[] data = Serializer.Serialize(value, settings);
      CollectionAssert.AreEqual(new string[] { "p_City", "p_StreetName" }, Keys(Serializer.Deserialize<Dictionary<string, object>>(data, Untyped)));
      Assert.AreEqual("Utrecht", Serializer.Deserialize<NpAddress>(data, settings).City);

      settings.PropertyNamingPolicy = new EmptyPolicy();
      MsgPackException error = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(value, settings));
      StringAssert.Contains(error.Message, "EmptyPolicy");
    }

    private sealed class PrefixPolicy : MsgPackNamingPolicy
    {
      public override string ConvertName(string name)
      {
        return "p_" + name;
      }
    }

    private sealed class EmptyPolicy : MsgPackNamingPolicy
    {
      public override string ConvertName(string name)
      {
        return "";
      }
    }

    [TestMethod]
    public void AnonymousTypes_ByThePolicy()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.KebabCaseLower };
      object value = new { Outer = 1, Inner = new { FirstValue = "a" } };
      Dictionary<string, object> written = Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(value, typeof(object), settings), Untyped);
      CollectionAssert.AreEqual(new string[] { "inner", "outer" }, Keys(written));
      CollectionAssert.AreEqual(new string[] { "first-value" }, Keys(written["inner"]));
    }

    [TestMethod]
    public void DictionaryKeys_ByTheirOwnPolicy()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, DictionaryKeyPolicy = MsgPackNamingPolicy.CamelCase };
      NpOrder value = Sample();
      Dictionary<string, object> written = Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(value, settings), Untyped);
      Assert.IsTrue(written.ContainsKey("CountsByName"), "the property names stay");
      CollectionAssert.AreEqual(new string[] { "firstKey", "secondKey" }, Keys(written["CountsByName"]));
      Assert.IsTrue(((KeyValuePair<object, object>[])written["NamesById"]).Any(e => Equals(e.Key, (byte)1) || Equals(e.Key, 1)), "other keys as they are");

      NpOrder back = Serializer.Deserialize<NpOrder>(Serializer.Serialize(value, settings), settings);
      CollectionAssert.AreEquivalent(new string[] { "firstKey", "secondKey" }, back.CountsByName.Keys.ToArray(), "read as written (as System.Text.Json)");

      // A dictionary declared otherwise: object keys, a root dictionary, a sorted one
      Dictionary<object, object> mixed = new Dictionary<object, object>() { { "SomeName", 1 }, { 2, "Two" } };
      Dictionary<object, object> mixedBack = Serializer.Deserialize<Dictionary<object, object>>(Serializer.Serialize(mixed, settings), Untyped);
      Assert.IsTrue(mixedBack.ContainsKey("someName"));
      SortedDictionary<string, int> sorted = new SortedDictionary<string, int>() { { "BKey", 2 }, { "AKey", 1 } };
      CollectionAssert.AreEqual(new string[] { "aKey", "bKey" }, Serializer.Deserialize<Dictionary<string, int>>(Serializer.Serialize(sorted, settings), Untyped).Keys.ToArray());

      // Both policies
      settings.PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower;
      settings.DictionaryKeyPolicy = MsgPackNamingPolicy.KebabCaseUpper;
      written = Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(value, settings), Untyped);
      CollectionAssert.AreEqual(new string[] { "FIRST-KEY", "SECOND-KEY" }, Keys(written["counts_by_name"]));
    }

    [TestMethod]
    public void ExtensionData_KeptAsRead()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.CamelCase, DictionaryKeyPolicy = MsgPackNamingPolicy.SnakeCaseUpper };
      byte[] newer = Serializer.Serialize(new NpKeptNewer() { KnownValue = "k", NewerValue = "n" }, new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map });
      NpKept kept = Serializer.Deserialize<NpKept>(newer, settings);
      Assert.AreEqual("k", kept.KnownValue, "by the .NET name");
      Assert.AreEqual("n", kept.Rest["NewerValue"]);
      Dictionary<string, object> written = Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(kept, settings), Untyped);
      CollectionAssert.AreEqual(new string[] { "NewerValue", "knownValue" }, Keys(written), "kept names written as they came");
    }

    [TestMethod]
    public void EnumNames_ByTheirOwnPolicy()
    {
      foreach (bool schema in new bool[] { false, true })
      {
        MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = schema, ObjectLayout = ObjectLayout.Map, EnumFormat = EnumFormat.Name, EnumNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower };
        Assert.AreEqual("in_progress", Serializer.Deserialize<string>(Serializer.Serialize(NpStatus.InProgress, settings), settings));
        Assert.AreEqual("Custom-Name", Serializer.Deserialize<string>(Serializer.Serialize(NpStatus.Custom, settings), settings), "custom names as they are");
        Assert.AreEqual("can_read, is_owner", Serializer.Deserialize<string>(Serializer.Serialize(NpRights.CanRead | NpRights.IsOwner, settings), settings));
        Assert.AreEqual(NpStatus.InProgress, Serializer.Deserialize<NpStatus>(Serializer.Serialize(NpStatus.InProgress, settings), settings));
        Assert.AreEqual(NpStatus.Done, Serializer.Deserialize<NpStatus>(Serializer.Serialize("Done", settings), settings), "the .NET name too");
        Assert.AreEqual(NpRights.CanWrite | NpRights.IsOwner, Serializer.Deserialize<NpRights>(Serializer.Serialize("can_write, is_owner", settings), settings));
        Assert.AreEqual(NpStatus.Done, Serializer.Deserialize<NpStatus>(Serializer.Serialize(2, settings), settings), "numbers");

        NpOrder value = Sample();
        NpOrder back = Serializer.Deserialize<NpOrder>(Serializer.Serialize(value, settings), settings);
        AssertSame(value, back, $"schema {schema}");

        MsgPackSettings without = new MsgPackSettings() { UseIndexedSchema = schema, ObjectLayout = ObjectLayout.Map };
        Assert.AreEqual(NpStatus.InProgress, Serializer.Deserialize<NpStatus>(Serializer.Serialize("InProgress", settings), without));
        Assert.ThrowsExactly<ArgumentException>(() => Serializer.Deserialize<NpStatus>(Serializer.Serialize("in_progress", settings), without), "a reader without the policy knows the .NET names only");

        settings.EnumNamingPolicy = MsgPackNamingPolicy.CamelCase;
        Assert.AreEqual("inProgress", Serializer.Deserialize<string>(Serializer.Serialize(NpStatus.InProgress, settings), settings));
        Assert.AreEqual(NpStatus.InProgress, Serializer.Deserialize<NpStatus>(Serializer.Serialize("inProgress", settings), without), "camelCase: .NET names ignoring case");
      }
      MsgPackSettings properties = new MsgPackSettings() { UseIndexedSchema = false, EnumFormat = EnumFormat.Name, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower };
      Assert.AreEqual("InProgress", Serializer.Deserialize<string>(Serializer.Serialize(NpStatus.InProgress, properties), properties), "the property names' policy is not the enums'");
    }

    [TestMethod]
    public void EnumNames_SameConvertedName_Refused()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, EnumFormat = EnumFormat.Name, EnumNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower };
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(NpEnumClash.FooBar, settings));
    }

    [TestMethod]
    public void Schema_HasTheWrittenNames()
    {
      // The reader's class is another one with the same names: paired through the schema's names
      MsgPackSettings writer = new MsgPackSettings() { UseIndexedSchema = true, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower, SchemaVersion = SchemaVersion.Described };
      byte[] data = Serializer.Serialize(Sample(), writer);
      string text = System.Text.Encoding.UTF8.GetString(data);
      StringAssert.Contains(text, "street_name");
      Assert.IsFalse(text.Contains("StreetName"), "only the written names");
      NpOrderCopy copy = Serializer.Deserialize<NpOrderCopy>(data, new MsgPackSettings() { PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower });
      Assert.AreEqual("Utrecht", copy.ShipTo.City);
      Assert.AreEqual("P2", copy.Lines[1].ProductCode);
    }

    [TestMethod]
    public void Differences_ReportTheWrittenNames()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower };
      Dictionary<string, object> map = new Dictionary<string, object>() { { "street_name", "Main" }, { "post_code", "1234" } };
      NpAddress read = Serializer.Deserialize<NpAddress>(Serializer.Serialize(map, Untyped), settings, out ReadDifferences differences);
      Assert.AreEqual("Main", read.StreetName);
      Assert.AreEqual(1, differences.Differences.Count);
      Assert.AreEqual("post_code", differences.Differences[0].Name);
    }
  }

  public enum NpEnumClash { FooBar, Foo_Bar }

  public class NpOrderCopy
  {
    public NpAddressCopy ShipTo { get; set; }
    public List<NpLineCopy> Lines { get; set; }
  }

  public class NpAddressCopy
  {
    public string City { get; set; }
  }

  public class NpLineCopy
  {
    public string ProductCode { get; set; }
  }

  [TestClass]
  public class DebugNamingPolicyTests : NamingPolicyTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }

    protected override bool LastKeyWins { get { return false; } }
  }

  [TestClass]
  public class LsNamingPolicyTests : NamingPolicyTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }

    /// <summary>
    /// The same bytes as LsMsgPackDebug, also with the compiled writers and readers from the first object and with the generated code; read back by each.
    /// </summary>
    [TestMethod]
    public void SameBytesAsDebug_RuntimeCompiledGenerated()
    {
      object[] values = new object[] { Sample(), new List<NpOrder>() { Sample(), new NpOrder() }, new NpRenamed() { GivenName = "a" }, new Dictionary<string, NpAddress>() { { "HomeAddress", new NpAddress() { City = "x" } } } };
      for (int p = 0; p < Policies.Length; p++)
      {
        foreach (MsgPackSettings settings in AllSettings(Policies[p]))
        {
          settings.DictionaryKeyPolicy = Policies[(p + 1) % Policies.Length];
          settings.EnumNamingPolicy = Policies[(p + 2) % Policies.Length];
          settings.EnumFormat = EnumFormat.Name;
          foreach (bool filter in new bool[] { false, true })
          {
            if (filter)
              settings.DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues() };
            for (int t = 0; t < values.Length; t++)
            {
              object value = values[t];
              string message = $"policy {p}, schema {settings.UseIndexedSchema}, {settings.ObjectLayout}, {settings.SchemaVersion}, filter {filter}, {value.GetType().Name}";
              byte[] expected = MsgPackSerializer.Serialize(value, value.GetType(), settings);
              CollectionAssert.AreEqual(expected, Serializers.Ls.Serialize(value, value.GetType(), settings), "runtime: " + message);
              CollectionAssert.AreEqual(expected, Serializers.Gen.Serialize(value, value.GetType(), settings), "generated: " + message);
              CollectionAssert.AreEqual(expected, MsgPackSerializer.Serialize(Serializers.Gen.Deserialize(value.GetType(), expected, settings), value.GetType(), settings), "read back generated: " + message);
              foreach (bool generated in new bool[] { false, true })
              {
                LsMsgPackOptions compiled = new LsMsgPackOptions()
                {
                  UseIndexedSchema = settings.UseIndexedSchema, ObjectLayout = settings.ObjectLayout, SchemaVersion = settings.SchemaVersion, DynamicFilters = settings.DynamicFilters,
                  PropertyNamingPolicy = settings.PropertyNamingPolicy, DictionaryKeyPolicy = settings.DictionaryKeyPolicy, EnumNamingPolicy = settings.EnumNamingPolicy, EnumFormat = EnumFormat.Name,
                  CompileWritersAfter = 0, CompileReadersAfter = 0, UseRegisteredContexts = false
                };
                if (generated)
                  compiled.Contexts = new LsMsgPackContext[] { TestMsgPackContext.Default };
                LsMsgPackSerializer serializer = new LsMsgPackSerializer(compiled);
                for (int round = 0; round < 3; round++)
                {
                  byte[] bytes = serializer.Serialize(value, value.GetType());
                  CollectionAssert.AreEqual(expected, bytes, $"compiled ({(generated ? "generated" : "runtime")}), round {round}: " + message);
                  object back = serializer.Deserialize(value.GetType(), bytes);
                  CollectionAssert.AreEqual(expected, MsgPackSerializer.Serialize(back, value.GetType(), settings), $"read back compiled, round {round}: " + message);
                }
              }
            }
          }
        }
      }
    }

    /// <summary>
    /// The compiled and generated readers of maps keyed by names read data of a writer without the policy (.NET names), in any order and case.
    /// </summary>
    [TestMethod]
    public void CompiledAndGenerated_ReadTheDotNetNames()
    {
      foreach (bool generated in new bool[] { false, true })
      {
        foreach (bool ignoreCase in new bool[] { false, true })
        {
          LsMsgPackOptions options = new LsMsgPackOptions()
          {
            UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = ignoreCase,
            CompileWritersAfter = 0, CompileReadersAfter = 0, UseRegisteredContexts = false
          };
          if (generated)
            options.Contexts = new LsMsgPackContext[] { TestMsgPackContext.Default };
          LsMsgPackSerializer serializer = new LsMsgPackSerializer(options);
          NpLine line = new NpLine() { ProductCode = "P", UnitPrice = 1.5m, Quantity = 4 };
          for (int round = 0; round < 3; round++)
          {
            foreach (MsgPackNamingPolicy writerPolicy in new MsgPackNamingPolicy[] { null, MsgPackNamingPolicy.SnakeCaseLower, MsgPackNamingPolicy.CamelCase })
            {
              byte[] data = MsgPackSerializer.Serialize(line, new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = writerPolicy });
              NpLine back = serializer.Deserialize<NpLine>(data);
              string message = $"generated {generated}, ignoring case {ignoreCase}, round {round}, writer {writerPolicy?.GetType().Name ?? "none"}";
              if (writerPolicy == MsgPackNamingPolicy.CamelCase && !ignoreCase)
              {
                Assert.IsNull(back.ProductCode, message);
                continue;
              }
              Assert.AreEqual("P", back.ProductCode, message);
              Assert.AreEqual(1.5m, back.UnitPrice, message);
              Assert.AreEqual(4, back.Quantity, message);
            }
          }
        }
      }
    }
  }

  [TestClass]
  public class GenNamingPolicyTests : NamingPolicyTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
