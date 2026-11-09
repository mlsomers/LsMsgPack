using LtMsgPack;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyType;
using System;
using System.Collections.Generic;
using NB = Nerdbank.MessagePack;

namespace LsMsgPackInteropTests
{
  public struct IopFieldPoint { public int X; public int Y; }

  public class IopFieldModel
  {
    public string Name { get; set; }
    public int Count;
    public double Ratio;
    public IopFieldPoint Point;
    public List<int> Items;
  }

  public class IopJsonFields
  {
    public string Name { get; set; }
    public int Plain;
    [System.Text.Json.Serialization.JsonInclude]
    public int Included;
    [System.Text.Json.Serialization.JsonInclude]
    private int _hidden;
    public int Number { get; set; }

    public int Hidden { get { return _hidden; } }

    public static IopJsonFields Sample()
    {
      IopJsonFields value = new IopJsonFields() { Name = "n", Plain = 1, Included = 2, Number = 4 };
      value._hidden = 3;
      return value;
    }
  }

  [GenerateShapeFor<IopFieldModel>]
  public partial class IopFieldShapes
  {
  }

  public class IopNonSerialized
  {
    public string Name { get; set; }
    public int Kept;
    [NonSerialized]
    public int Skipped;
  }

  /// <summary>
  /// Fields with the presets: MessagePack-CSharp (contractless) and Nerdbank.MessagePack write the public fields after the properties, so their presets serialize them (SerializedFields.Public)
  /// and write the same bytes; Generic keeps the default (attributed fields only, as System.Text.Json). The JSON serializers' fields, as docs/Compatibility.md "Fields" describes them.
  /// </summary>
  [TestClass]
  public class PresetFieldTests
  {
    private static readonly NB.MessagePackSerializer Nerdbank = new NB.MessagePackSerializer();

    private static IopFieldModel Sample()
    {
      return new IopFieldModel() { Name = "n", Count = 3, Ratio = 0.5, Point = new IopFieldPoint() { X = 1, Y = -2 }, Items = new List<int>() { 4, 5 } };
    }

    private static void AssertSame(IopFieldModel expected, IopFieldModel actual, string message)
    {
      Assert.AreEqual(expected.Name, actual.Name, message);
      Assert.AreEqual(expected.Count, actual.Count, message);
      Assert.AreEqual(expected.Ratio, actual.Ratio, message);
      Assert.AreEqual(expected.Point, actual.Point, message);
      CollectionAssert.AreEqual(expected.Items, actual.Items, message);
    }

    [TestMethod]
    public void MessagePackCSharp_SameBytes()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.MessagePackCSharp());
      CollectionAssert.AreEqual(MessagePackSerializer.Serialize(Sample(), ContractlessStandardResolver.Options), lt.Serialize(Sample()));
    }

    [TestMethod]
    public void MessagePackCSharp_BothWays()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.MessagePackCSharp());
      AssertSame(Sample(), MessagePackSerializer.Deserialize<IopFieldModel>(lt.Serialize(Sample()), ContractlessStandardResolver.Options), "MessagePack-CSharp reads ours");
      AssertSame(Sample(), lt.Deserialize<IopFieldModel>(MessagePackSerializer.Serialize(Sample(), ContractlessStandardResolver.Options)), "we read MessagePack-CSharp's");
    }

    [TestMethod]
    public void Nerdbank_SameBytes()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.Nerdbank());
      CollectionAssert.AreEqual(Nerdbank.Serialize<IopFieldModel, IopFieldShapes>(Sample()), lt.Serialize(Sample()));
    }

    [TestMethod]
    public void Nerdbank_BothWays()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.Nerdbank());
      AssertSame(Sample(), Nerdbank.Deserialize<IopFieldModel, IopFieldShapes>(lt.Serialize(Sample())), "Nerdbank reads ours");
      AssertSame(Sample(), lt.Deserialize<IopFieldModel>(Nerdbank.Serialize<IopFieldModel, IopFieldShapes>(Sample())), "we read Nerdbank's");
    }

    [TestMethod]
    public void Generic_DefaultFieldsOnly()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.Generic());
      Assert.AreEqual("{\"Name\":\"n\"}", MessagePackSerializer.ConvertToJson(lt.Serialize(Sample())), "no attributed fields in the model");
    }

    /// <summary>
    /// Json.NET writes public fields and leaves out [NonSerialized] ones: LikeNewtonsoft with the public fields does the same.
    /// </summary>
    [TestMethod]
    public void Newtonsoft_NonSerialized()
    {
      IopNonSerialized value = new IopNonSerialized() { Name = "n", Kept = 1, Skipped = 2 };
      LsMsgPack.MsgPackSettings settings = new LsMsgPack.MsgPackSettings() { UseInexedSchema = false, ObjectLayout = LsMsgPack.ObjectLayout.Map,
        StaticFilters = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIncludeStatically[] { new LsMsgPack.TypeResolving.Filters.FilterNonSettable(LsMsgPack.TypeResolving.Filters.GetOnlyProperties.All,
          LsMsgPack.TypeResolving.Filters.NonPublicProperties.None, LsMsgPack.TypeResolving.Filters.SerializedFields.Public), LsMsgPack.TypeResolving.Filters.FilterIgnoredAttribute.LikeNewtonsoft, new LsMsgPack.TypeResolving.Filters.FilterStatic() } };
      CollectionAssert.AreEquivalent(new[] { "Kept", "Name" }, Keys(Newtonsoft.Json.JsonConvert.SerializeObject(value)), "Json.NET (its order differs: fields first)");
      CollectionAssert.AreEquivalent(new[] { "Kept", "Name" }, Keys(MessagePackSerializer.ConvertToJson(LsMsgPack.MsgPackSerializer.Serialize(value, settings))), "LsMsgPack");
    }

    /// <summary>
    /// Json.NET writes the public fields before the properties: the same names as SerializedFields.Public, in another order (docs/Compatibility.md "Fields").
    /// </summary>
    [TestMethod]
    public void Newtonsoft_FieldsFirst()
    {
      CollectionAssert.AreEqual(new[] { "Count", "Ratio", "Point", "Items", "Name" }, Keys(Newtonsoft.Json.JsonConvert.SerializeObject(Sample())), "Json.NET");
      CollectionAssert.AreEqual(new[] { "Name", "Count", "Ratio", "Point", "Items" }, Keys(MessagePackSerializer.ConvertToJson(LsMsgPack.MsgPackSerializer.Serialize(Sample(), MapSettings(LsMsgPack.TypeResolving.Filters.SerializedFields.Public)))), "LsMsgPack");
    }

    /// <summary>
    /// System.Text.Json writes fields only with IncludeFields (the public ones: SerializedFields.Public) or [JsonInclude] (also private ones: Attributed, the default),
    /// in the same order: the properties, then the fields (docs/Compatibility.md "Fields").
    /// </summary>
    [TestMethod]
    public void SystemTextJson_IncludeFieldsAndJsonInclude()
    {
      IopJsonFields value = IopJsonFields.Sample();
      CollectionAssert.AreEqual(new[] { "Name", "Number", "Hidden", "Included", "_hidden" }, Keys(System.Text.Json.JsonSerializer.Serialize(value)), "System.Text.Json");
      CollectionAssert.AreEqual(new[] { "Name", "Number", "Hidden", "Included", "_hidden" }, Keys(MessagePackSerializer.ConvertToJson(LsMsgPack.MsgPackSerializer.Serialize(value, MapSettings(LsMsgPack.TypeResolving.Filters.SerializedFields.Attributed)))), "LsMsgPack Attributed");

      System.Text.Json.JsonSerializerOptions includeFields = new System.Text.Json.JsonSerializerOptions() { IncludeFields = true };
      CollectionAssert.AreEqual(new[] { "Name", "Number", "Hidden", "Plain", "Included", "_hidden" }, Keys(System.Text.Json.JsonSerializer.Serialize(value, includeFields)), "System.Text.Json IncludeFields");
      CollectionAssert.AreEqual(new[] { "Name", "Number", "Hidden", "Plain", "Included", "_hidden" }, Keys(MessagePackSerializer.ConvertToJson(LsMsgPack.MsgPackSerializer.Serialize(value, MapSettings(LsMsgPack.TypeResolving.Filters.SerializedFields.Public)))), "LsMsgPack Public");

      IopJsonFields read = System.Text.Json.JsonSerializer.Deserialize<IopJsonFields>(System.Text.Json.JsonSerializer.Serialize(value));
      Assert.AreEqual(3, read.Hidden, "System.Text.Json sets the private [JsonInclude] field");
      Assert.AreEqual(3, LsMsgPack.MsgPackSerializer.Deserialize<IopJsonFields>(LsMsgPack.MsgPackSerializer.Serialize(value)).Hidden, "LsMsgPack too");
    }

    private static LsMsgPack.MsgPackSettings MapSettings(LsMsgPack.TypeResolving.Filters.SerializedFields fields)
    {
      return new LsMsgPack.MsgPackSettings() { UseInexedSchema = false, ObjectLayout = LsMsgPack.ObjectLayout.Map,
        StaticFilters = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIncludeStatically[] { new LsMsgPack.TypeResolving.Filters.FilterNonSettable(LsMsgPack.TypeResolving.Filters.GetOnlyProperties.All,
          LsMsgPack.TypeResolving.Filters.NonPublicProperties.None, fields), new LsMsgPack.TypeResolving.Filters.FilterIgnoredAttribute(), new LsMsgPack.TypeResolving.Filters.FilterStatic() } };
    }

    private static List<string> Keys(string json)
    {
      List<string> keys = new List<string>();
      foreach (Newtonsoft.Json.Linq.JProperty property in Newtonsoft.Json.Linq.JObject.Parse(json).Properties())
        keys.Add(property.Name);
      return keys;
    }
  }
}
