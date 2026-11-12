using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Reading fields: array positions, names, defaults, readonly fields, constructors, structs and the framework's structs.
  /// </summary>
  public abstract class SerializingFieldsReading : FldTestBase
  {
    public SerializingFieldsReading()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(SerializingFieldsReading));
    }

    private static string Hex(byte[] bytes)
    {
      return BitConverter.ToString(bytes).Replace("-", " ");
    }

    // Case 7: array positions

    public class FldPositionsBefore
    {
      public int A { get; set; }
      public string B { get; set; }
    }

    public class FldPositionsAfter
    {
      public int A { get; set; }
      public string B { get; set; }
      public int C = 77;
      public string D = "initial";
    }

    [TestMethod]
    public void ArrayPositions_OldDataIntoAClassWithFields()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Array, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldPositionsBefore() { A = 1, B = "b" }, settings);
      CollectionAssert.AreEqual(new byte[] { 0x92, 0x01, 0xA1, (byte)'b' }, bytes);
      FldPositionsAfter read = Serializer.Deserialize<FldPositionsAfter>(bytes, settings);
      Assert.AreEqual(1, read.A);
      Assert.AreEqual("b", read.B);
      Assert.AreEqual(77, read.C, "the fields come after the properties: not in the data, they keep their initial values");
      Assert.AreEqual("initial", read.D);
    }

    [TestMethod]
    public void ArrayPositions_NewDataIntoAClassWithoutFields()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Array, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldPositionsAfter() { A = 1, B = "b", C = 5, D = "d" }, settings);
      CollectionAssert.AreEqual(new byte[] { 0x94, 0x01, 0xA1, (byte)'b', 0x05, 0xA1, (byte)'d' }, bytes);
      FldPositionsBefore read = Serializer.Deserialize<FldPositionsBefore>(bytes, settings);
      Assert.AreEqual(1, read.A);
      Assert.AreEqual("b", read.B);
    }

    [TestMethod]
    public void ArrayPositions_NewDataIntoAClassWithoutFields_ReportsTheExtraValues()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Array, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldPositionsAfter() { A = 1, B = "b", C = 5, D = "d" }, settings);
      FldPositionsBefore read = Serializer.Deserialize<FldPositionsBefore>(bytes, settings, out ReadDifferences differences);
      Assert.AreEqual("b", read.B);
      Assert.AreEqual(2, differences.Differences.Count, differences.ToString());
      Assert.AreEqual(DifferenceKind.ExtraValue, differences.Differences[0].Kind);
      Assert.AreEqual(DifferenceKind.ExtraValue, differences.Differences[1].Kind, "the two values after the last property, one each");
    }

    // Case 8: names

    [DataContract]
    public class FldNamed
    {
      [JsonPropertyName("renamedField")]
      public int Field;

      [DataMember(Name = "dm")]
      public string Member;

      [JsonPropertyName("renamedProperty")]
      public int Property { get; set; }
    }

    private static MsgPackSettings Named(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(schema, layout, SerializedFields.Public);
      settings.PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new AttributePropertyNameResolver() };
      return settings;
    }

    [TestMethod]
    public void Names_ResolvedFromAttributes()
    {
      Assert.AreEqual("renamedProperty, renamedField, dm", WrittenNames(new FldNamed() { Field = 1, Member = "m", Property = 2 }, Named(false, ObjectLayout.Map)));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Names_RoundTrip(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = Named(schema, layout);
      FldNamed read = RoundTrip(new FldNamed() { Field = 1, Member = "m", Property = 2 }, settings);
      Assert.AreEqual(1, read.Field);
      Assert.AreEqual("m", read.Member);
      Assert.AreEqual(2, read.Property);
    }

    [TestMethod]
    public void Names_InTheIndexedSchema()
    {
      MsgPackSettings settings = Named(true, ObjectLayout.Array);
      byte[] bytes = Serializer.Serialize(new FldNamed() { Field = 1, Member = "m", Property = 2 }, settings);
      Dictionary<string, object> schema = Serializer.Deserialize<Dictionary<string, object>>(bytes, new MsgPackSettings() { UseInexedSchema = false });
      Assert.IsTrue(schema.ContainsKey(nameof(FldNamed)), string.Join(", ", schema.Keys));
      List<object> names = ((IEnumerable<object>)schema[nameof(FldNamed)]).ToList();
      CollectionAssert.AreEqual(new object[] { "renamedProperty", "renamedField", "dm" }, names);
    }

    public class FldCamel
    {
      public int Count;
      public string Label;
      [JsonInclude]
      private int _secret = 0; // set by the serializer only
      public int GetSecret() { return _secret; }
    }

    [TestMethod]
    public void Names_CaseInsensitive_IntoFields()
    {
      MsgPackSettings writer = Settings(false, ObjectLayout.Map, SerializedFields.None);
      byte[] bytes = Serializer.Serialize(new Dictionary<string, object>() { { "count", 3 }, { "label", "x" }, { "_Secret", 9 } }, writer);
      MsgPackSettings reader = Settings(false, ObjectLayout.Map, SerializedFields.Public);
      FldCamel exact = Serializer.Deserialize<FldCamel>(bytes, reader);
      Assert.AreEqual(0, exact.Count, "case sensitive by default");
      reader.PropertyNameCaseInsensitive = true;
      FldCamel read = Serializer.Deserialize<FldCamel>(bytes, reader);
      Assert.AreEqual(3, read.Count);
      Assert.AreEqual("x", read.Label);
      Assert.AreEqual(9, read.GetSecret());
    }

    // Case 9: duplicate ids

    public class FldDuplicate
    {
      [JsonPropertyName("X")]
      public int F;

      public int X { get; set; }
    }

    [TestMethod]
    public void Duplicate_FieldAndPropertyWithTheSameId()
    {
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Serialize(new FldDuplicate() { F = 1, X = 2 }, Named(false, ObjectLayout.Map)));
      StringAssert.Contains(ex.Message, "same id");
      StringAssert.Contains(ex.Message, "field");
    }

    [TestMethod]
    public void Duplicate_FieldAndPropertyWithTheSameId_IndexedSchema()
    {
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Serialize(new FldDuplicate() { F = 1, X = 2 }, Named(true, ObjectLayout.Map)));
      StringAssert.Contains(ex.Message, "same id");
    }

    [TestMethod]
    public void Duplicate_FieldAndPropertyWithTheSameId_IndexedSchema_SaysField()
    {
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Serialize(new FldDuplicate() { F = 1, X = 2 }, Named(true, ObjectLayout.Map)));
      StringAssert.Contains(ex.Message, "field");
    }

    // Case 10: default values

    public class FldDefaults
    {
      public int P;

      [DefaultValue(5)]
      public int Level = 5;

      [DefaultValue("hi")]
      public string Greeting = "hi";
    }

    public class FldDefaultsNoInitializer
    {
      public int P;

      [DefaultValue(5)]
      public int Level;

      [DefaultValue("hi")]
      public string Greeting;
    }

    public class FldDefaultsProperty
    {
      public int P { get; set; }

      [DefaultValue(5)]
      public int Level { get; set; }
    }

    [TestMethod]
    public void Defaults_LeftOutWhenEqual()
    {
      Assert.AreEqual("P", WrittenNames(new FldDefaults() { P = 1, Level = 5 }, SerializedFields.Public));
      Assert.AreEqual("P, Level", WrittenNames(new FldDefaults() { P = 1, Level = 6 }, SerializedFields.Public));
      Assert.AreEqual("P, Level, Greeting", WrittenNames(new FldDefaults() { P = 1, Level = 0, Greeting = "other" }, SerializedFields.Public), "0 is not the [DefaultValue]");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Defaults_RoundTrip(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(schema, layout, SerializedFields.Public);
      FldDefaults read = RoundTrip(new FldDefaults() { P = 1, Level = 5, Greeting = "hi" }, settings);
      Assert.AreEqual(1, read.P);
      Assert.AreEqual(5, read.Level);
      Assert.AreEqual("hi", read.Greeting);
      read = RoundTrip(new FldDefaults() { P = 1, Level = 0, Greeting = "x" }, settings);
      Assert.AreEqual(0, read.Level);
      Assert.AreEqual("x", read.Greeting);
    }

    [TestMethod]
    public void Defaults_NilInTheArrayLayoutReadsTheDefaultValue()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Array, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldDefaults() { P = 1, Level = 5, Greeting = "hi" }, settings);
      CollectionAssert.AreEqual(new byte[] { 0x93, 0x01, 0xC0, 0xC0 }, bytes, "the default values are left out: nil");
      FldDefaultsNoInitializer read = Serializer.Deserialize<FldDefaultsNoInitializer>(bytes, settings);
      Assert.AreEqual(5, read.Level, "nil: the [DefaultValue] of the field (a property does the same)");
      Assert.AreEqual("hi", read.Greeting);
      FldDefaultsProperty property = Serializer.Deserialize<FldDefaultsProperty>(new byte[] { 0x92, 0x01, 0xC0 }, settings);
      Assert.AreEqual(5, property.Level);
    }

    [TestMethod]
    public void Defaults_DescribedSchemaWritesTheDefaultOfTheField()
    {
      MsgPackSettings settings = Settings(true, ObjectLayout.Array, SerializedFields.Public);
      settings.SchemaVersion = SchemaVersion.Described;
      string hex = Hex(Serializer.Serialize(new FldDefaults() { P = 1, Level = 5, Greeting = "hi" }, settings));
      StringAssert.Contains(hex, "93 A5 4C 65 76 65 6C A3 69 6E 74 05", "[\"Level\", \"int\", 5]");
      StringAssert.Contains(hex, "93 A8 47 72 65 65 74 69 6E 67 A6 73 74 72 69 6E 67 A2 68 69", "[\"Greeting\", \"string\", \"hi\"]");
    }

    // Case 11: readonly fields taken by a constructor

    public class FldCtorClass
    {
      public readonly int Id;
      public readonly string Name;
      public readonly int Required = 42;

      public FldCtorClass(int id, string name)
      {
        Id = id;
        Name = name;
      }
    }

    public readonly struct FldCtorStruct
    {
      public readonly int X;
      public readonly int Y;

      [JsonConstructor]
      public FldCtorStruct(int x, int y)
      {
        X = x;
        Y = y;
      }
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ReadonlyCtor_Class(bool schema, ObjectLayout layout)
    {
      FldCtorClass read = RoundTrip(new FldCtorClass(7, "n"), Settings(schema, layout, SerializedFields.Public));
      Assert.AreEqual(7, read.Id);
      Assert.AreEqual("n", read.Name);
      Assert.AreEqual(42, read.Required);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ReadonlyCtor_Struct(bool schema, ObjectLayout layout)
    {
      FldCtorStruct read = RoundTrip(new FldCtorStruct(3, 4), Settings(schema, layout, SerializedFields.Public));
      Assert.AreEqual(3, read.X);
      Assert.AreEqual(4, read.Y);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ReadonlyCtor_UninitializedStillCallsTheConstructor(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(schema, layout, SerializedFields.Public);
      settings.ObjectCreation = ObjectCreation.Uninitialized;
      FldCtorClass read = RoundTrip(new FldCtorClass(7, "n"), settings);
      Assert.AreEqual(7, read.Id);
      Assert.AreEqual("n", read.Name);
      Assert.AreEqual(42, read.Required, "the constructor that takes the fields ran: its initializers too");
      FldCtorStruct readStruct = RoundTrip(new FldCtorStruct(3, 4), settings);
      Assert.AreEqual(3, readStruct.X);
      Assert.AreEqual(4, readStruct.Y);
    }

    // Case 12: readonly collections and arrays

    public class FldReadonlyCollections
    {
      public readonly List<int> Items = new List<int>();
      public readonly Dictionary<string, int> Map = new Dictionary<string, int>();
      public readonly int[] Array = new int[] { 1, 2 };
      public string Name;
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ReadonlyCollections_Filled(bool schema, ObjectLayout layout)
    {
      FldReadonlyCollections value = new FldReadonlyCollections() { Name = "n" };
      value.Items.AddRange(new[] { 5, 6 });
      value.Map.Add("a", 1);
      value.Array[0] = 9;
      FldReadonlyCollections read = RoundTrip(value, Settings(schema, layout, SerializedFields.Public));
      CollectionAssert.AreEqual(new[] { 5, 6 }, read.Items);
      Assert.AreEqual(1, read.Map["a"]);
      CollectionAssert.AreEqual(new[] { 1, 2 }, read.Array, "a readonly array is computed: written, skipped when read");
      Assert.AreEqual("n", read.Name);
    }

    [TestMethod]
    public void ReadonlyCollections_ArrayIsWritten()
    {
      FldReadonlyCollections value = new FldReadonlyCollections() { Name = "n" };
      value.Items.Add(1);
      value.Map.Add("a", 1);
      Assert.AreEqual("Items, Map, Array, Name", WrittenNames(value, SerializedFields.Public));
    }

    // Case 13: GetOnlyProperties

    public class FldGetOnlyMix
    {
      public readonly int Taken;
      public readonly int Computed = 42;
      public readonly List<int> Items = new List<int>();
      public string Name;

      public FldGetOnlyMix(int taken)
      {
        Taken = taken;
      }
    }

    private static FldGetOnlyMix GetOnlyMix()
    {
      FldGetOnlyMix value = new FldGetOnlyMix(1) { Name = "n" };
      value.Items.Add(1);
      return value;
    }

    [TestMethod]
    public void GetOnly_None_LeavesOutTheReadonlyFieldsNoConstructorTakes()
    {
      Assert.AreEqual("Taken, Name", WrittenNames(GetOnlyMix(), SerializedFields.Public, getOnly: GetOnlyProperties.None));
    }

    [TestMethod]
    public void GetOnly_Collections_KeepsOnlyTheCollection()
    {
      Assert.AreEqual("Taken, Items, Name", WrittenNames(GetOnlyMix(), SerializedFields.Public, getOnly: GetOnlyProperties.Collections));
    }

    [TestMethod]
    public void GetOnly_Computed_KeepsTheComputed()
    {
      Assert.AreEqual("Taken, Computed, Name", WrittenNames(GetOnlyMix(), SerializedFields.Public, getOnly: GetOnlyProperties.Computed));
    }

    [TestMethod]
    public void GetOnly_All()
    {
      Assert.AreEqual("Taken, Computed, Items, Name", WrittenNames(GetOnlyMix(), SerializedFields.Public, getOnly: GetOnlyProperties.All));
    }

    [TestMethod]
    [DataRow(GetOnlyProperties.None)]
    [DataRow(GetOnlyProperties.Collections)]
    [DataRow(GetOnlyProperties.Computed)]
    [DataRow(GetOnlyProperties.All)]
    public void GetOnly_RoundTrip(GetOnlyProperties getOnly)
    {
      foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        foreach (bool schema in new[] { true, false })
        {
          FldGetOnlyMix read = RoundTrip(GetOnlyMix(), Settings(schema, layout, SerializedFields.Public, getOnly: getOnly));
          Assert.AreEqual(1, read.Taken, getOnly + " " + layout + " " + schema);
          Assert.AreEqual("n", read.Name);
          Assert.AreEqual(42, read.Computed);
          Assert.AreEqual(getOnly == GetOnlyProperties.Collections || getOnly == GetOnlyProperties.All ? 1 : 0, read.Items.Count, getOnly + " " + layout + " " + schema);
        }
    }

    // Case 14: a constructor that takes the values of public fields

    public class FldCtorFields
    {
      public int A;
      public string B;

      public FldCtorFields(int a, string b)
      {
        A = a;
        B = b;
      }
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ConstructorFields_Public(bool schema, ObjectLayout layout)
    {
      FldCtorFields read = RoundTrip(new FldCtorFields(5, "b"), Settings(schema, layout, SerializedFields.Public));
      Assert.AreEqual(5, read.A);
      Assert.AreEqual("b", read.B);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ConstructorFields_DefaultMode_NotSerialized(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = schema, ObjectLayout = layout };
      Assert.AreEqual("", WrittenNames(new FldCtorFields(5, "b"), settings), "the fields are not attributed");
      FldCtorFields read = RoundTrip(new FldCtorFields(5, "b"), settings);
      Assert.IsNotNull(read);
      Assert.AreEqual(0, read.A, "the constructor gets the default");
      Assert.IsNull(read.B);
    }

    // Case 15: ambiguous parameters

    public class FldAmbiguous
    {
      public int Value { get; set; }
      public int value;

      public FldAmbiguous(int vALUE)
      {
        _created = vALUE;
      }

      private int _created;
      public int GetCreated() { return _created; }
      public void SetCreated(int created) { _created = created; }
    }

    public class FldExactMatch
    {
      public int Value { get; set; }
      public int value;

      public FldExactMatch(int value)
      {
        _created = value;
      }

      private int _created;
      public int GetCreated() { return _created; }
      public void SetCreated(int created) { _created = created; }
    }

    [TestMethod]
    public void Ambiguous_ParameterMatchingTwoMembersIgnoringCaseBindsNone()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Map, SerializedFields.Public);
      FldAmbiguous written = new FldAmbiguous(0) { Value = 3, value = 4 };
      written.SetCreated(8);
      FldAmbiguous read = RoundTrip(written, settings);
      Assert.AreEqual(0, read.GetCreated(), "no constructor ran: the parameter is not bound");
      Assert.AreEqual(3, read.Value);
      Assert.AreEqual(4, read.value);
    }

    [TestMethod]
    public void Ambiguous_ExactMatchObserved()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Map, SerializedFields.Public);
      FldExactMatch written = new FldExactMatch(0) { Value = 3, value = 4 };
      written.SetCreated(8);
      FldExactMatch read = RoundTrip(written, settings);
      Assert.AreEqual(4, read.GetCreated(), "observed: the parameter that equals a name exactly is bound (to the field value), although Value matches ignoring case");
      Assert.AreEqual(3, read.Value);
      Assert.AreEqual(0, read.value, "taken by the constructor, which did not assign it: a member the constructor takes is not set afterwards");
    }

    // Case 16: user structs with fields only

    public struct FldVec
    {
      public int X;
      public int Y;
    }

    public class FldVecHolder
    {
      public FldVec Position;
      public FldVec? Maybe;
      public List<FldVec> Path;
      public Dictionary<string, FldVec> ByName;
      public object Boxed;
      public FldVec[] Points { get; set; }
      public FldVecHolder Child;
    }

    private static FldVecHolder VecHolder()
    {
      return new FldVecHolder()
      {
        Position = new FldVec { X = 1, Y = 2 }, Maybe = new FldVec { X = 3 }, Path = new List<FldVec> { new FldVec { X = 4, Y = 5 }, new FldVec { Y = 6 } },
        ByName = new Dictionary<string, FldVec> { { "k", new FldVec { X = 7, Y = 8 } } }, Boxed = new FldVec { X = 9, Y = 10 }, Points = new[] { new FldVec { X = 11 } },
        Child = new FldVecHolder() { Position = new FldVec { Y = 12 } }
      };
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void FieldStructs_RoundTrip(bool schema, ObjectLayout layout)
    {
      FldVecHolder read = RoundTrip(VecHolder(), Settings(schema, layout, SerializedFields.Public));
      Assert.AreEqual(new FldVec { X = 1, Y = 2 }.X, read.Position.X);
      Assert.AreEqual(2, read.Position.Y);
      Assert.AreEqual(3, read.Maybe.Value.X);
      Assert.AreEqual(2, read.Path.Count);
      Assert.AreEqual(4, read.Path[0].X);
      Assert.AreEqual(6, read.Path[1].Y);
      Assert.AreEqual(8, read.ByName["k"].Y);
      Assert.IsInstanceOfType<FldVec>(read.Boxed, "a type id");
      Assert.AreEqual(10, ((FldVec)read.Boxed).Y);
      Assert.AreEqual(11, read.Points[0].X);
      Assert.AreEqual(12, read.Child.Position.Y);
    }

    [TestMethod]
    public void FieldStructs_AsObjectRoot()
    {
      MsgPackSettings settings = Settings(true, ObjectLayout.Array, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize((object)new FldVec { X = 1, Y = 2 }, typeof(object), settings);
      object read = Serializer.Deserialize(typeof(object), bytes, settings);
      Assert.IsInstanceOfType<FldVec>(read);
      Assert.AreEqual(2, ((FldVec)read).Y);
    }

    [TestMethod]
    public void FieldStructs_ArrayPositions()
    {
      byte[] bytes = Serializer.Serialize(new FldVec { X = 1, Y = 2 }, Settings(false, ObjectLayout.Array, SerializedFields.Public));
      CollectionAssert.AreEqual(new byte[] { 0x92, 0x01, 0x02 }, bytes);
    }

    public struct FldPropVec { public int X { get; set; } }

    public class FldPropVecHolder { public FldPropVec Position { get; set; } }

    /// <summary>
    /// The path of a sample that is a struct: the same for a struct with properties, so it is no matter of the fields.
    /// </summary>
    [TestMethod]
    public void FieldStructs_PathOfAStructWithPropertiesForComparison()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Map, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldUnknownHolder() { Position = new FldUnknownPosition() { X = 1, Unknown = 2 } }, settings);
      FldPropVecHolder read = Serializer.Deserialize<FldPropVecHolder>(bytes, settings, out ReadDifferences differences);
      Assert.AreEqual(1, differences.Differences.Count, differences.ToString());
      Assert.AreEqual("<null>", differences.PathOf(differences.Differences[0].Samples[0]) ?? "<null>");
    }

    public class FldUnknownHolder { public FldUnknownPosition Position; }

    public class FldUnknownPosition { public int X; public int Unknown; }

    [TestMethod]
    public void FieldStructs_PathOfThroughAStructInTheDifferences()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Map, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldUnknownHolder() { Position = new FldUnknownPosition() { X = 1, Unknown = 2 } }, settings);
      FldVecHolder read = Serializer.Deserialize<FldVecHolder>(bytes, settings, out ReadDifferences differences);
      Assert.AreEqual(1, read.Position.X);
      Assert.AreEqual(1, differences.Differences.Count, differences.ToString());
      Assert.AreEqual("Unknown", differences.Differences[0].Name);
      Assert.AreEqual(typeof(FldVec), differences.Differences[0].Class);
      Assert.IsNull(differences.PathOf(differences.Differences[0].Samples[0]), "the sample is a boxed copy of the struct: no path, as for a struct with properties (see the next test)");
    }

    // Case 17: System.Numerics

    public class FldNumerics
    {
      public Vector2 V2 { get; set; }
      public Vector3 V3 { get; set; }
      public Vector4 V4 { get; set; }
      public Plane Plane { get; set; }
      public Matrix3x2 M3 { get; set; }
      public Matrix4x4 M4 { get; set; }
      public Quaternion Q { get; set; }
      public Vector3[] Points { get; set; }
      public List<Vector2> List { get; set; }
      public object Boxed { get; set; }
      public object[] Boxes { get; set; }
      public Vector3? Maybe { get; set; }
    }

    private static FldNumerics NumericsSample()
    {
      return new FldNumerics()
      {
        V2 = new Vector2(1, 2), V3 = new Vector3(3, 4, 5), V4 = new Vector4(6, 7, 8, 9), Plane = new Plane(1, 2, 3, 4), M3 = Matrix3x2.CreateTranslation(5, 6) * Matrix3x2.CreateScale(2),
        M4 = Matrix4x4.CreateTranslation(4, 5, 6) * Matrix4x4.CreateScale(2), Q = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f),
        Points = new[] { new Vector3(1, 1, 1), new Vector3(2, 3, 4) }, List = new List<Vector2> { new Vector2(7, 8) },
        Boxed = new Vector3(9, 8, 7), Boxes = new object[] { new Vector2(1, 2), new Quaternion(1, 2, 3, 4), Matrix3x2.Identity, new Plane(1, 0, 0, 5), new Vector4(1, 2, 3, 4), Matrix4x4.Identity },
        Maybe = new Vector3(0.5f, 0.25f, 0.125f)
      };
    }

    private static IEnumerable<object[]> AllFieldModes()
    {
      foreach (SerializedFields fields in new[] { SerializedFields.None, SerializedFields.Attributed, SerializedFields.Public, SerializedFields.All })
        foreach (bool schema in new[] { true, false })
          foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
            yield return new object[] { fields, schema, layout };
    }

    [TestMethod]
    [DynamicData(nameof(AllFieldModes))]
    public void Numerics_RoundTrip(SerializedFields fields, bool schema, ObjectLayout layout)
    {
      FldNumerics value = NumericsSample();
      FldNumerics read = RoundTrip(value, Settings(schema, layout, fields));
      Assert.AreEqual(value.V2, read.V2);
      Assert.AreEqual(value.V3, read.V3);
      Assert.AreEqual(value.V4, read.V4);
      Assert.AreEqual(value.Plane, read.Plane);
      Assert.AreEqual(value.M3, read.M3);
      Assert.AreEqual(value.M4, read.M4);
      Assert.AreEqual(value.Q, read.Q);
      CollectionAssert.AreEqual(value.Points, read.Points);
      CollectionAssert.AreEqual(value.List, read.List);
      Assert.AreEqual(value.Boxed, read.Boxed);
      CollectionAssert.AreEqual(value.Boxes, read.Boxes);
      Assert.AreEqual(value.Maybe, read.Maybe);
    }

    [TestMethod]
    public void Numerics_Layouts()
    {
      MsgPackSettings array = Settings(false, ObjectLayout.Array, SerializedFields.None);
      CollectionAssert.AreEqual(new byte[] { 0x92, 0xCA, 0x3F, 0x80, 0, 0, 0xCA, 0x40, 0, 0, 0 }, Serializer.Serialize(new Vector2(1, 2), array));
      CollectionAssert.AreEqual(new byte[] { 0x94, 0xCA, 0x3F, 0x80, 0, 0, 0xCA, 0x40, 0, 0, 0, 0xCA, 0x40, 0x40, 0, 0, 0xCA, 0x40, 0x80, 0, 0 }, Serializer.Serialize(new Vector4(1, 2, 3, 4), array));
      CollectionAssert.AreEqual(new byte[] { 0x94, 0xCA, 0x3F, 0x80, 0, 0, 0xCA, 0x40, 0, 0, 0, 0xCA, 0x40, 0x40, 0, 0, 0xCA, 0x40, 0x80, 0, 0 }, Serializer.Serialize(new Quaternion(1, 2, 3, 4), array));
      Assert.AreEqual(0x96, Serializer.Serialize(Matrix3x2.Identity, array)[0], "6 floats");
      byte[] matrix = Serializer.Serialize(Matrix4x4.Identity, array);
      CollectionAssert.AreEqual(new byte[] { 0xDC, 0, 16 }, matrix.Take(3).ToArray(), "16 floats (array 16)");
      MsgPackSettings map = Settings(false, ObjectLayout.Map, SerializedFields.None);
      Dictionary<string, object> plane = Serializer.Deserialize<Dictionary<string, object>>(Serializer.Serialize(new Plane(1, 0, 0, 5), map), map);
      CollectionAssert.AreEqual(new[] { "Normal", "D" }, plane.Keys.ToArray());
    }

    [TestMethod]
    public void Numerics_Vector3SchemaEntryListsTheFloats()
    {
      MsgPackSettings settings = Settings(true, ObjectLayout.Array, SerializedFields.None);
      settings.SchemaVersion = SchemaVersion.Described;
      string hex = Hex(Serializer.Serialize(new Vector3(1, 2, 3), settings));
      // ["X", "float"], ["Y", "float"], ["Z", "float"]
      StringAssert.Contains(hex, "92 A1 58 A5 66 6C 6F 61 74 92 A1 59 A5 66 6C 6F 61 74 92 A1 5A A5 66 6C 6F 61 74");
    }

    // Case 18: framework structs

    [TestMethod]
    public void FrameworkStructs_IndexStaysRefused()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Array, SerializedFields.All);
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Serialize(new Index(1), settings));
      StringAssert.StartsWith(ex.Message, "System.Index is not supported: it has no settable properties", "refused as before: no public fields, no settable properties");
    }

    [TestMethod]
    public void FrameworkStructs_ComStructWithPublicFields_Observed()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Map, SerializedFields.None);
      System.Runtime.InteropServices.ComTypes.FILETIME value = new System.Runtime.InteropServices.ComTypes.FILETIME() { dwLowDateTime = 1, dwHighDateTime = 2 };
      System.Runtime.InteropServices.ComTypes.FILETIME read = RoundTrip(value, settings);
      Assert.AreEqual(1, read.dwLowDateTime);
      Assert.AreEqual(2, read.dwHighDateTime);
    }
  }

  [TestClass]
  public class DebugSerializingFieldsReading : SerializingFieldsReading
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsSerializingFieldsReading : SerializingFieldsReading
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class GenSerializingFieldsReading : SerializingFieldsReading
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
