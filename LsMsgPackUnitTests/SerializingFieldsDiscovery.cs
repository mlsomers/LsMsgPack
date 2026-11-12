using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

// Attributes of other libraries, recognized by their full names (the unit tests do not reference those packages)
namespace MessagePack
{
  [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
  public sealed class KeyAttribute : Attribute
  {
    public KeyAttribute(int x) { IntKey = x; }
    public int IntKey { get; }
  }
}

namespace PolyType
{
  [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
  public sealed class PropertyShapeAttribute : Attribute
  {
    public bool Ignore { get; set; }
  }
}

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Any attribute with "Include" in its name opts a field in.
  /// </summary>
  [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
  public sealed class FldIncludeAttribute : Attribute { }

  /// <summary>
  /// What the tests of the fields share: the settings with a mode for the fields, round trips and the names written.
  /// </summary>
  public abstract class FldTestBase
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    protected static MsgPackSettings Settings(bool schema, ObjectLayout layout, SerializedFields fields, PropertyOrder order = PropertyOrder.Reflection,
      GetOnlyProperties getOnly = GetOnlyProperties.All, IgnoreAttributes ignore = IgnoreAttributes.All)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = schema, ObjectLayout = layout, PropertyOrder = order };
      settings.StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(getOnly, NonPublicProperties.None, fields), new FilterIgnoredAttribute(ignore), new FilterStatic() };
      return settings;
    }

    protected static IEnumerable<object[]> AllSettings()
    {
      foreach (bool schema in new[] { true, false })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          yield return new object[] { schema, layout };
    }

    protected T RoundTrip<T>(T value, MsgPackSettings settings)
    {
      byte[] bytes = Serializer.Serialize(value, settings);
      return Serializer.Deserialize<T>(bytes, settings);
    }

    /// <summary>
    /// The names of the members written, in the order written (the map layout without the schema).
    /// </summary>
    protected string WrittenNames<T>(T value, SerializedFields fields, PropertyOrder order = PropertyOrder.Reflection, GetOnlyProperties getOnly = GetOnlyProperties.All, IgnoreAttributes ignore = IgnoreAttributes.All)
    {
      return WrittenNames(value, Settings(false, ObjectLayout.Map, fields, order, getOnly, ignore));
    }

    protected string WrittenNames<T>(T value, MsgPackSettings settings)
    {
      MsgPackSettings written = (MsgPackSettings)settings.Clone();
      written.UseInexedSchema = false;
      written.ObjectLayout = ObjectLayout.Map;
      byte[] bytes = Serializer.Serialize(value, written);
      Dictionary<string, object> map = Serializer.Deserialize<Dictionary<string, object>>(bytes, Settings(false, ObjectLayout.Map, SerializedFields.None));
      return string.Join(", ", map.Keys);
    }
  }

  /// <summary>
  /// Which fields are found (attributes of several libraries, ignore attributes, what is never a field), records, inheritance and the order.
  /// </summary>
  public abstract class SerializingFieldsDiscovery : FldTestBase
  {
    // Case 1: opt-in attributes

    public class FldDiscovered
    {
      public string Prop { get; set; }

      [DataMember]
      public int Member; // not in a [DataContract] class

      [FldInclude]
      private int _included; // "Include" in the name of the attribute

      [MessagePack.Key(3)]
      public int Keyed; // the full name of MessagePack-CSharp's attribute

      [JsonProperty]
      private string _newtonsoft; // Json.NET

      [PolyType.PropertyShape]
      public int Shaped; // Nerdbank.MessagePack

      [PolyType.PropertyShape(Ignore = true)]
      public int ShapeIgnored;

      [PolyType.PropertyShape(Ignore = true)]
      private int _shapeIgnoredPrivate;

      public int Unmarked; // SerializedFields.Public

      public static FldDiscovered Sample()
      {
        return new FldDiscovered() { Prop = "p", Member = 1, _included = 2, Keyed = 3, _newtonsoft = "n", Shaped = 4, ShapeIgnored = 5, _shapeIgnoredPrivate = 6, Unmarked = 7 };
      }

      public int GetIncluded() { return _included; }
      public string GetNewtonsoft() { return _newtonsoft; }
      public int GetShapeIgnoredPrivate() { return _shapeIgnoredPrivate; }
    }

    [TestMethod]
    public void Attributes_OptFieldsIn()
    {
      Assert.AreEqual("Prop, Member, Keyed, Shaped, _included, _newtonsoft", WrittenNames(FldDiscovered.Sample(), SerializedFields.Attributed),
        "[DataMember], [Key] and [PropertyShape] on public fields, then the non-public ones; [PropertyShape(Ignore = true)] is no opt-in");
    }

    [TestMethod]
    public void Attributes_PublicIncludesTheUnmarkedAndKeepsTheIgnored()
    {
      Assert.AreEqual("Prop, Member, Keyed, Shaped, Unmarked, _included, _newtonsoft", WrittenNames(FldDiscovered.Sample(), SerializedFields.Public),
        "[PropertyShape(Ignore = true)] leaves the public field out of the public fields as well");
    }

    [TestMethod]
    public void Attributes_AllStillIgnoresShapeIgnore()
    {
      string names = WrittenNames(FldDiscovered.Sample(), SerializedFields.All);
      Assert.IsFalse(names.Contains("ShapeIgnored"), names);
      Assert.IsFalse(names.Contains("_shapeIgnoredPrivate"), names);
      Assert.IsTrue(names.Contains("Unmarked"), names);
    }

    [TestMethod]
    public void Attributes_NoneWritesProperties()
    {
      Assert.AreEqual("Prop", WrittenNames(FldDiscovered.Sample(), SerializedFields.None));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Attributes_RoundTrip(bool schema, ObjectLayout layout)
    {
      FldDiscovered read = RoundTrip(FldDiscovered.Sample(), Settings(schema, layout, SerializedFields.Attributed));
      Assert.AreEqual("p", read.Prop);
      Assert.AreEqual(1, read.Member);
      Assert.AreEqual(2, read.GetIncluded());
      Assert.AreEqual(3, read.Keyed);
      Assert.AreEqual("n", read.GetNewtonsoft());
      Assert.AreEqual(4, read.Shaped);
      Assert.AreEqual(0, read.ShapeIgnored);
      Assert.AreEqual(0, read.GetShapeIgnoredPrivate());
      Assert.AreEqual(0, read.Unmarked);
    }

    // Case 2: ignore attributes on fields

    public class FldIgnored
    {
      public int Kept;

      [System.Text.Json.Serialization.JsonIgnore]
      public int StjAlways;

      [System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
      public string StjWhenNull;

      [IgnoreDataMember]
      public int IgnoredMember;

      [NonSerialized]
      public int Transient;

      [JsonInclude, IgnoreDataMember]
      private int _both;

      [Newtonsoft.Json.JsonIgnore]
      public int NewtonsoftIgnored;

      public static FldIgnored Sample()
      {
        return new FldIgnored() { Kept = 1, StjAlways = 2, StjWhenNull = "w", IgnoredMember = 3, Transient = 4, _both = 5, NewtonsoftIgnored = 6 };
      }

      public int GetBoth() { return _both; }
    }

    [TestMethod]
    public void Ignore_Attributes()
    {
      Assert.AreEqual("Kept, StjWhenNull", WrittenNames(FldIgnored.Sample(), SerializedFields.Public),
        "[JsonIgnore] (Always), [IgnoreDataMember], [NonSerialized] and Json.NET's [JsonIgnore] leave a field out; ignore wins over [JsonInclude]");
    }

    [TestMethod]
    public void Ignore_WhenWritingNull_KeepsTheFieldButNullIsLeftOut()
    {
      FldIgnored value = FldIgnored.Sample();
      value.StjWhenNull = null;
      Assert.AreEqual("Kept", WrittenNames(value, SerializedFields.Public), "null is left out by the default filter");
    }

    [TestMethod]
    public void Ignore_NonSerialized_CanBeSwitchedOff()
    {
      Assert.AreEqual("Kept, StjWhenNull, Transient", WrittenNames(FldIgnored.Sample(), SerializedFields.Public, ignore: IgnoreAttributes.All & ~IgnoreAttributes.NonSerialized));
      Assert.AreEqual("Kept, StjWhenNull", WrittenNames(FldIgnored.Sample(), SerializedFields.Public, ignore: IgnoreAttributes.All));
    }

    [TestMethod]
    public void Ignore_NonSerialized_ThroughTheFilterConstructor()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Map, SerializedFields.Public);
      settings.StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, SerializedFields.Public), new FilterIgnoredAttribute(IgnoreAttributes.All & ~IgnoreAttributes.NonSerialized), new FilterStatic() };
      Assert.AreEqual("Kept, StjWhenNull, Transient", WrittenNames(FldIgnored.Sample(), settings));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Ignore_RoundTrip(bool schema, ObjectLayout layout)
    {
      FldIgnored read = RoundTrip(FldIgnored.Sample(), Settings(schema, layout, SerializedFields.Public));
      Assert.AreEqual(1, read.Kept);
      Assert.AreEqual("w", read.StjWhenNull);
      Assert.AreEqual(0, read.StjAlways);
      Assert.AreEqual(0, read.IgnoredMember);
      Assert.AreEqual(0, read.Transient);
      Assert.AreEqual(0, read.GetBoth());
      Assert.AreEqual(0, read.NewtonsoftIgnored);
    }

    [TestMethod]
    public void Ignore_JsonIncludeAndIgnoreDataMember_IgnoreWins_AlsoInAll()
    {
      string names = WrittenNames(FldIgnored.Sample(), SerializedFields.All);
      Assert.IsFalse(names.Contains("_both"), names);
    }

    // Case 3: what is never a field

    public class FldOddities
    {
      public static int StaticField = 1;
      public const int Constant = 2;
      public static readonly int StaticReadonly = 3;
      public int Auto { get; set; }
      public event EventHandler Changed;
      public int Normal;

      public void Raise() { Changed?.Invoke(this, EventArgs.Empty); }
    }

    public class FldDelegateField
    {
      public int Normal;
      public Func<int> Callback;
    }

    public class FldTypeField
    {
      public int Normal;
      public Type Kind;
    }

    [TestMethod]
    public void Excluded_InAll()
    {
      FldOddities value = new FldOddities() { Auto = 4, Normal = 5 };
      value.Changed += (s, e) => { };
      Assert.AreEqual("Auto, Normal", WrittenNames(value, SerializedFields.All), "no static, const, static readonly, backing field or event field");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Excluded_StaticFieldsAreNotChanged(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(schema, layout, SerializedFields.All);
      FldOddities read = RoundTrip(new FldOddities() { Auto = 4, Normal = 5 }, settings);
      Assert.AreEqual(4, read.Auto);
      Assert.AreEqual(5, read.Normal);
      Assert.AreEqual(1, FldOddities.StaticField);
    }

    /// <summary>
    /// Decides nothing: a field of a delegate type (not an event) is reported as it behaves.
    /// </summary>
    [TestMethod]
    public void Excluded_DelegateField_Observed()
    {
      FldDelegateField value = new FldDelegateField() { Normal = 3, Callback = () => 4 };
      string outcome;
      try
      {
        outcome = WrittenNames(value, SerializedFields.Public);
      }
      catch (Exception ex)
      {
        outcome = "throws " + ex.GetType().Name;
      }
      Assert.AreEqual("throws MsgPackException", outcome, "observed (see the report): a public field of a delegate type is not left out, writing it throws");
    }

    [TestMethod]
    public void Excluded_TypeField_Observed()
    {
      FldTypeField value = new FldTypeField() { Normal = 3, Kind = typeof(string) };
      string outcome;
      try
      {
        outcome = WrittenNames(value, SerializedFields.Public);
      }
      catch (Exception ex)
      {
        outcome = "throws " + ex.GetType().Name;
      }
      Assert.AreEqual("throws MsgPackException", outcome, "observed (see the report)");
    }

    // Case 4: records

    public record FldRecord(int Id, string Name);

    public record FldRecordFields
    {
      public int Id { get; init; }
      public string Label;
    }

    public record struct FldRecordStruct(int Id, string Name);

    public record struct FldRecordStructField
    {
      public int Id { get; set; }
      public string Label;
    }

    [TestMethod]
    [DataRow(SerializedFields.Public)]
    [DataRow(SerializedFields.All)]
    public void Records_NoGeneratedFields(SerializedFields fields)
    {
      Assert.AreEqual("Id, Name", WrittenNames(new FldRecord(1, "n"), fields));
      Assert.AreEqual("Id, Name", WrittenNames(new FldRecordStruct(1, "n"), fields));
    }

    [TestMethod]
    [DataRow(SerializedFields.Public)]
    [DataRow(SerializedFields.All)]
    public void Records_WithAFieldOfTheirOwn(SerializedFields fields)
    {
      Assert.AreEqual("Id, Label", WrittenNames(new FldRecordFields() { Id = 1, Label = "l" }, fields));
      Assert.AreEqual("Id, Label", WrittenNames(new FldRecordStructField() { Id = 1, Label = "l" }, fields));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Records_RoundTrip(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(schema, layout, SerializedFields.All);
      Assert.AreEqual(new FldRecord(1, "n"), RoundTrip(new FldRecord(1, "n"), settings));
      Assert.AreEqual(new FldRecordStruct(2, "m"), RoundTrip(new FldRecordStruct(2, "m"), settings));
      FldRecordFields read = RoundTrip(new FldRecordFields() { Id = 3, Label = "l" }, settings);
      Assert.AreEqual(3, read.Id);
      Assert.AreEqual("l", read.Label);
      FldRecordStructField readStruct = RoundTrip(new FldRecordStructField() { Id = 4, Label = "s" }, settings);
      Assert.AreEqual(4, readStruct.Id);
      Assert.AreEqual("s", readStruct.Label);
    }

    // Case 5: inheritance

    public class FldLevel1
    {
      public int One;

      [JsonInclude]
      private int _hidden1;

      public int Hidden1 { get { return _hidden1; } set { _hidden1 = value; } }
    }

    public class FldLevel2 : FldLevel1
    {
      public int Two;

      [JsonInclude]
      private int _hidden2;

      public int Hidden2 { get { return _hidden2; } set { _hidden2 = value; } }
    }

    public class FldLevel3 : FldLevel2
    {
      public int Three;
    }

    public class FldGenericBase<T>
    {
      public T Item;

      [JsonInclude]
      private T _secret;

      public T Secret { get { return _secret; } set { _secret = value; } }
    }

    public class FldGenericDerived : FldGenericBase<string>
    {
      public int Own;
    }

    public class FldGenericList : FldGenericBase<List<int>> { }

    [TestMethod]
    public void Inheritance_FieldsOfBaseClasses()
    {
      Assert.AreEqual("Hidden2, Hidden1, Three, Two, One, _hidden2, _hidden1", WrittenNames(new FldLevel3() { One = 1, Hidden1 = 2, Two = 3, Hidden2 = 4, Three = 5 }, SerializedFields.Public),
        "observed: properties of every level, then the public fields, then the attributed private ones");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Inheritance_RoundTrip(bool schema, ObjectLayout layout)
    {
      FldLevel3 read = RoundTrip(new FldLevel3() { One = 1, Hidden1 = 2, Two = 3, Hidden2 = 4, Three = 5 }, Settings(schema, layout, SerializedFields.Public));
      Assert.AreEqual(1, read.One);
      Assert.AreEqual(2, read.Hidden1);
      Assert.AreEqual(3, read.Two);
      Assert.AreEqual(4, read.Hidden2);
      Assert.AreEqual(5, read.Three);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Inheritance_PrivateFieldsOfBaseClasses_Attributed(bool schema, ObjectLayout layout)
    {
      FldLevel3 read = RoundTrip(new FldLevel3() { One = 1, Hidden1 = 2, Two = 3, Hidden2 = 4, Three = 5 }, Settings(schema, layout, SerializedFields.Attributed));
      Assert.AreEqual(2, read.Hidden1);
      Assert.AreEqual(4, read.Hidden2);
      Assert.AreEqual(0, read.One, "not attributed");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Inheritance_GenericBase(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(schema, layout, SerializedFields.Public);
      FldGenericDerived read = RoundTrip(new FldGenericDerived() { Item = "i", Secret = "s", Own = 3 }, settings);
      Assert.AreEqual("i", read.Item);
      Assert.AreEqual("s", read.Secret);
      Assert.AreEqual(3, read.Own);
      FldGenericList list = RoundTrip(new FldGenericList() { Item = new List<int> { 1, 2 }, Secret = new List<int> { 3 } }, settings);
      CollectionAssert.AreEqual(new[] { 1, 2 }, list.Item);
      CollectionAssert.AreEqual(new[] { 3 }, list.Secret);
    }

    [TestMethod]
    public void Inheritance_GenericBase_Names()
    {
      Assert.AreEqual("Secret, Own, Item, _secret", WrittenNames(new FldGenericDerived() { Item = "i", Secret = "s", Own = 3 }, SerializedFields.Public));
    }

    private class FldHideBase
    {
      public string Property { get; set; }
      public int Field;
      public int FieldHidingField;
    }

    private class FldHideDerived : FldHideBase
    {
      public new int Property; // a field hides a property
      public new string Field { get; set; } // a property hides a field
      public new int FieldHidingField; // a field hides a field
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Hiding_FieldPropertyField(bool schema, ObjectLayout layout)
    {
      FldHideDerived value = new FldHideDerived() { Property = 1, Field = "f", FieldHidingField = 2 };
      ((FldHideBase)value).Property = "base";
      ((FldHideBase)value).Field = 9;
      ((FldHideBase)value).FieldHidingField = 8;
      FldHideDerived read = RoundTrip(value, Settings(schema, layout, SerializedFields.Public));
      Assert.AreEqual(1, read.Property);
      Assert.AreEqual("f", read.Field);
      Assert.AreEqual(2, read.FieldHidingField);
      Assert.IsNull(((FldHideBase)read).Property, "the hidden member of the base class is not written");
      Assert.AreEqual(0, ((FldHideBase)read).Field);
      Assert.AreEqual(0, ((FldHideBase)read).FieldHidingField);
    }

    [TestMethod]
    public void Hiding_EachNameOnce()
    {
      FldHideDerived value = new FldHideDerived() { Property = 1, Field = "f", FieldHidingField = 2 };
      ((FldHideBase)value).Property = "base";
      ((FldHideBase)value).Field = 9;
      ((FldHideBase)value).FieldHidingField = 8;
      Assert.AreEqual("Field, Property, FieldHidingField", WrittenNames(value, SerializedFields.Public));
    }

    // Case 6: PropertyOrder

    public class FldOrderBase
    {
      public string BaseZulu { get; set; }
      public int BaseYankee;
      [DataMember(Order = 3)]
      public int BaseThree;
      [DataMember(Order = 1)]
      public string BaseOne { get; set; }
      public bool BaseXray;
    }

    public class FldOrderDerived : FldOrderBase
    {
      public double DerivedAlpha;
      public string DerivedBravo { get; set; }
      [DataMember(Order = 2)]
      public int DerivedTwo;
      [DataMember(Order = 0)]
      public Guid DerivedZero { get; set; }
      public long DerivedCharlie;
    }

    private static FldOrderDerived OrderSample()
    {
      return new FldOrderDerived()
      {
        BaseZulu = "z", BaseYankee = 1, BaseThree = 2, BaseOne = "o", BaseXray = true, DerivedAlpha = 1.5, DerivedBravo = "b", DerivedTwo = 3,
        DerivedZero = Guid.NewGuid(), DerivedCharlie = 4
      };
    }

    [TestMethod]
    public void Order_Reflection()
    {
      Assert.AreEqual("DerivedBravo, DerivedZero, BaseZulu, BaseOne, DerivedAlpha, DerivedTwo, DerivedCharlie, BaseYankee, BaseThree, BaseXray", WrittenNames(OrderSample(), SerializedFields.Public, PropertyOrder.Reflection));
    }

    [TestMethod]
    public void Order_Alphabetical()
    {
      Assert.AreEqual("BaseOne, BaseThree, BaseXray, BaseYankee, BaseZulu, DerivedAlpha, DerivedBravo, DerivedCharlie, DerivedTwo, DerivedZero", WrittenNames(OrderSample(), SerializedFields.Public, PropertyOrder.Alphabetical));
    }

    [TestMethod]
    public void Order_Declaration()
    {
      Assert.AreEqual("BaseZulu, BaseOne, BaseYankee, BaseThree, BaseXray, DerivedBravo, DerivedZero, DerivedAlpha, DerivedTwo, DerivedCharlie", WrittenNames(OrderSample(), SerializedFields.Public, PropertyOrder.Declaration),
        "base classes first, per class the properties, then the fields");
    }

    [TestMethod]
    public void Order_Explicit()
    {
      Assert.AreEqual("DerivedZero, BaseOne, DerivedTwo, BaseThree, BaseZulu, BaseYankee, BaseXray, DerivedBravo, DerivedAlpha, DerivedCharlie", WrittenNames(OrderSample(), SerializedFields.Public, PropertyOrder.Explicit),
        "by Order (properties and fields mixed), then the others in declaration order");
    }

    [TestMethod]
    public void Order_TypeThenDeclaration()
    {
      Assert.AreEqual("BaseXray, DerivedAlpha, DerivedZero, BaseYankee, BaseThree, DerivedTwo, DerivedCharlie, BaseZulu, BaseOne, DerivedBravo", WrittenNames(OrderSample(), SerializedFields.Public, PropertyOrder.TypeThenDeclaration),
        "by type name (Boolean, Double, Guid, Int32, Int64, String), declaration order within a type: properties before fields per class");
    }

    [TestMethod]
    [DataRow(PropertyOrder.Reflection)]
    [DataRow(PropertyOrder.Alphabetical)]
    [DataRow(PropertyOrder.Declaration)]
    [DataRow(PropertyOrder.Explicit)]
    [DataRow(PropertyOrder.TypeThenDeclaration)]
    public void Order_RoundTripPerOrder(PropertyOrder order)
    {
      foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        foreach (bool schema in new[] { true, false })
        {
          FldOrderDerived value = OrderSample();
          FldOrderDerived read = RoundTrip(value, Settings(schema, layout, SerializedFields.Public, order));
          Assert.AreEqual(value.BaseZulu, read.BaseZulu);
          Assert.AreEqual(value.BaseYankee, read.BaseYankee);
          Assert.AreEqual(value.BaseThree, read.BaseThree);
          Assert.AreEqual(value.BaseOne, read.BaseOne);
          Assert.AreEqual(value.BaseXray, read.BaseXray);
          Assert.AreEqual(value.DerivedAlpha, read.DerivedAlpha);
          Assert.AreEqual(value.DerivedBravo, read.DerivedBravo);
          Assert.AreEqual(value.DerivedTwo, read.DerivedTwo);
          Assert.AreEqual(value.DerivedZero, read.DerivedZero);
          Assert.AreEqual(value.DerivedCharlie, read.DerivedCharlie);
        }
    }
  }

  [TestClass]
  public class LsSerializingFieldsDiscovery : SerializingFieldsDiscovery
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingFieldsDiscovery : SerializingFieldsDiscovery
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenSerializingFieldsDiscovery : SerializingFieldsDiscovery
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
