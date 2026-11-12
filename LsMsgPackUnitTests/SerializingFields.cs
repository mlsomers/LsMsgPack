using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Fields (<see cref="SerializedFields"/>, an option of FilterNonSettable): the attributed ones by default, all public ones with <see cref="SerializedFields.Public"/>, after the properties.
  /// The framework's structs whose state is their public fields (Vector3) always.
  /// </summary>
  public abstract class SerializingFields
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class FldBase
    {
      public int BaseValue;

      [JsonInclude]
      private string _baseSecret;

      public string GetBaseSecret() { return _baseSecret; }
      public void SetBaseSecret(string value) { _baseSecret = value; }
    }

    public class FldAccount : FldBase
    {
      public string Name { get; set; }

      public int Count; // SerializedFields.Public only

      [DataMember]
      public string Marked; // attributed: by default

      [JsonInclude]
      private int _pin; // attributed, private: by default

      public string Auto { get; set; } // its backing field is never a field of its own

      public static int Shared = 5; // static: never

      public const int Constant = 7; // never

#pragma warning disable CS0067 // never used
      public event EventHandler Changed; // the event's field: never
#pragma warning restore CS0067

      private int _unmarked; // SerializedFields.All only

      public int GetPin() { return _pin; }
      public int GetUnmarked() { return _unmarked; }

      public static FldAccount Sample()
      {
        FldAccount account = new FldAccount() { Name = "n", Count = 3, Marked = "m", _pin = 1234, Auto = "a", BaseValue = 9, _unmarked = 8 };
        account.SetBaseSecret("s");
        return account;
      }
    }

    public class FldReadOnly
    {
      public readonly int Taken; // set by the constructor
      public readonly int Computed = 42; // no constructor takes it: written, skipped when read
      public readonly List<int> Items = new List<int>(); // a collection: filled
      public string Name;

      public FldReadOnly(int taken)
      {
        Taken = taken;
      }
    }

    public struct FldPoint
    {
      public int X;
      public int Y;
      public string Label { get; set; } // before the fields
    }

    public class FldHiding : FldBase
    {
      public new string BaseValue { get; set; } // hides the field of the base class
    }

    public class FldWithVector
    {
      public Vector3 Position { get; set; }
      public Quaternion Rotation { get; set; }
      public Matrix4x4 Transform { get; set; }
    }

    private static MsgPackSettings Settings(bool schema, ObjectLayout layout, SerializedFields fields, PropertyOrder order = PropertyOrder.Reflection)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = schema, ObjectLayout = layout, PropertyOrder = order };
      if (fields != SerializedFields.Attributed)
        settings.StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, fields), new FilterIgnoredAttribute(), new FilterStatic() };
      return settings;
    }

    private static IEnumerable<object[]> AllSettings()
    {
      foreach (bool schema in new[] { true, false })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          yield return new object[] { schema, layout };
    }

    private T RoundTrip<T>(T value, MsgPackSettings settings)
    {
      byte[] bytes = Serializer.Serialize(value, settings);
      return Serializer.Deserialize<T>(bytes, settings);
    }

    /// <summary>
    /// The names of the members written, in the order written (the map layout without the schema).
    /// </summary>
    private string WrittenNames<T>(T value, SerializedFields fields, PropertyOrder order = PropertyOrder.Reflection)
    {
      byte[] bytes = Serializer.Serialize(value, Settings(false, ObjectLayout.Map, fields, order));
      Dictionary<string, object> map = Serializer.Deserialize<Dictionary<string, object>>(bytes, Settings(false, ObjectLayout.Map, SerializedFields.None));
      return string.Join(", ", map.Keys);
    }

    [TestMethod]
    public void Default_AttributedFields()
    {
      Assert.AreEqual("Name, Auto, Marked, _pin, _baseSecret", WrittenNames(FldAccount.Sample(), SerializedFields.Attributed));
    }

    [TestMethod]
    public void None_NoFields()
    {
      Assert.AreEqual("Name, Auto", WrittenNames(FldAccount.Sample(), SerializedFields.None));
    }

    [TestMethod]
    public void Public_PublicFieldsAfterProperties()
    {
      Assert.AreEqual("Name, Auto, Count, Marked, BaseValue, _pin, _baseSecret", WrittenNames(FldAccount.Sample(), SerializedFields.Public),
        "properties, then the public fields (the class's own first), then the attributed non-public ones");
    }

    [TestMethod]
    public void All_NonPublicFieldsToo()
    {
      Assert.AreEqual("Name, Auto, Count, Marked, BaseValue, _pin, _unmarked, _baseSecret", WrittenNames(FldAccount.Sample(), SerializedFields.All),
        "never: static, const, backing fields of auto-properties and events");
    }

    [TestMethod]
    public void Declaration_PropertiesThenFieldsPerClass()
    {
      Assert.AreEqual("BaseValue, _baseSecret, Name, Auto, Count, Marked, _pin", WrittenNames(FldAccount.Sample(), SerializedFields.Public, PropertyOrder.Declaration));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Default_RoundTrip(bool schema, ObjectLayout layout)
    {
      FldAccount read = RoundTrip(FldAccount.Sample(), Settings(schema, layout, SerializedFields.Attributed));
      Assert.AreEqual("n", read.Name);
      Assert.AreEqual("m", read.Marked);
      Assert.AreEqual(1234, read.GetPin());
      Assert.AreEqual("s", read.GetBaseSecret());
      Assert.AreEqual(0, read.Count, "not attributed");
      Assert.AreEqual(0, read.BaseValue, "not attributed");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Public_RoundTrip(bool schema, ObjectLayout layout)
    {
      FldAccount read = RoundTrip(FldAccount.Sample(), Settings(schema, layout, SerializedFields.Public));
      Assert.AreEqual("n", read.Name);
      Assert.AreEqual(3, read.Count);
      Assert.AreEqual(9, read.BaseValue);
      Assert.AreEqual("m", read.Marked);
      Assert.AreEqual(1234, read.GetPin());
      Assert.AreEqual(0, read.GetUnmarked());
      Assert.AreEqual(5, FldAccount.Shared);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ReadOnly_LikeGetOnlyProperties(bool schema, ObjectLayout layout)
    {
      FldReadOnly value = new FldReadOnly(7) { Name = "r" };
      value.Items.Add(1);
      value.Items.Add(2);
      MsgPackSettings settings = Settings(schema, layout, SerializedFields.Public);
      FldReadOnly read = RoundTrip(value, settings);
      Assert.AreEqual(7, read.Taken, "through the constructor");
      Assert.AreEqual(42, read.Computed);
      CollectionAssert.AreEqual(new[] { 1, 2 }, read.Items, "filled");
      Assert.AreEqual("r", read.Name);
    }

    [TestMethod]
    public void ReadOnly_ComputedIsWrittenNotRead()
    {
      Assert.AreEqual("Taken, Computed, Items, Name", WrittenNames(new FldReadOnly(7) { Name = "x" }, SerializedFields.Public));
      byte[] bytes = Serializer.Serialize(new Dictionary<string, object>() { { "Taken", 1 }, { "Computed", 5 }, { "Name", "x" } }, Settings(false, ObjectLayout.Map, SerializedFields.Public));
      FldReadOnly read = Serializer.Deserialize<FldReadOnly>(bytes, Settings(false, ObjectLayout.Map, SerializedFields.Public));
      Assert.AreEqual(42, read.Computed, "a readonly field is never set behind the type's back");
      Assert.AreEqual(1, read.Taken);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Struct_RoundTrip(bool schema, ObjectLayout layout)
    {
      FldPoint read = RoundTrip(new FldPoint() { X = 1, Y = -2, Label = "p" }, Settings(schema, layout, SerializedFields.Public));
      Assert.AreEqual(1, read.X);
      Assert.AreEqual(-2, read.Y);
      Assert.AreEqual("p", read.Label);
    }

    [TestMethod]
    public void Struct_ArrayPositions()
    {
      byte[] bytes = Serializer.Serialize(new FldPoint() { X = 1, Y = 2, Label = "p" }, Settings(false, ObjectLayout.Array, SerializedFields.Public));
      CollectionAssert.AreEqual(new byte[] { 0x93, 0xA1, (byte)'p', 0x01, 0x02 }, bytes, "the property, then the fields");
    }

    [TestMethod]
    public void Hidden_ByDerivedProperty()
    {
      FldHiding value = new FldHiding() { BaseValue = "derived" };
      ((FldBase)value).BaseValue = 3;
      value.SetBaseSecret("s"); // null would be left out (FilterDefaultValues)
      Assert.AreEqual("BaseValue, _baseSecret", WrittenNames(value, SerializedFields.Public));
      FldHiding read = RoundTrip(value, Settings(false, ObjectLayout.Map, SerializedFields.Public));
      Assert.AreEqual("derived", read.BaseValue);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void FrameworkStructs_ByTheirFields(bool schema, ObjectLayout layout)
    {
      FldWithVector value = new FldWithVector() { Position = new Vector3(1, 2, 3), Rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), Transform = Matrix4x4.CreateTranslation(4, 5, 6) * Matrix4x4.CreateScale(2) };
      FldWithVector read = RoundTrip(value, Settings(schema, layout, SerializedFields.None)); // whatever the fields mode
      Assert.AreEqual(value.Position, read.Position);
      Assert.AreEqual(value.Rotation, read.Rotation);
      Assert.AreEqual(value.Transform, read.Transform);
    }

    [TestMethod]
    public void Vector3_AsArrayOfFloats()
    {
      byte[] bytes = Serializer.Serialize(new Vector3(1, 2, 3), new MsgPackSettings() { UseInexedSchema = false });
      CollectionAssert.AreEqual(new byte[] { 0x93, 0xCA, 0x3F, 0x80, 0, 0, 0xCA, 0x40, 0, 0, 0, 0xCA, 0x40, 0x40, 0, 0 }, bytes, "as MessagePack-CSharp writes it");
      Assert.AreEqual(new Vector3(1, 2, 3), Serializer.Deserialize<Vector3>(bytes, new MsgPackSettings() { UseInexedSchema = false }));
    }

    [TestMethod]
    public void Filter_RefusesUnknownValues()
    {
      Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, (SerializedFields)4));
    }
  }

  [TestClass]
  public class DebugSerializingFields : SerializingFields
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsSerializingFields : SerializingFields
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class GenSerializingFields : SerializingFields
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
