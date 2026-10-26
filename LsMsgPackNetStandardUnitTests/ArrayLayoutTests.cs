using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// <see cref="ObjectLayout.Array"/>: objects written as arrays of their values (nil for values the filters leave out), wrapped with a type id when needed.
  /// </summary>
  public abstract class ArrayLayoutTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public ArrayLayoutTests()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(AlAddress));
    }

    public class AlAddress
    {
      public string Street { get; set; }
      public int Number { get; set; }
    }

    public class AlPerson
    {
      public string Name { get; set; }
      public int Age { get; set; }
      public AlAddress Home { get; set; }
      public List<AlAddress> Others { get; set; }
      public int? Score { get; set; }
      public string Note { get; set; } = "default note";
      public DateTime Born { get; set; }
    }

    public interface IAlPet { string Name { get; set; } }

    public class AlDog : IAlPet
    {
      public string Name { get; set; }
      public bool Barks { get; set; }
    }

    public class AlCat : IAlPet
    {
      public string Name { get; set; }
      public int Lives { get; set; }
    }

    public class AlOwner
    {
      public IAlPet Pet { get; set; }
      public List<IAlPet> Pets { get; set; }
      public object Anything { get; set; }
    }

    public struct AlPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public class AlShape
    {
      public AlPoint Center { get; set; }
      public AlPoint[] Corners { get; set; }
    }

    public class AlBark
    {
      [DefaultValue("Woof")]
      public string Verb { get; set; } = "Woof";
    }

    public class AlInitialized
    {
      public int Retries { get; set; } = 3;
      public bool Enabled { get; set; } = true;
      public string Mode { get; set; } = "auto";
      public int? Limit { get; set; } = 10;
      [DefaultValue(5)]
      public int Level { get; set; } = 5;
      public DateTime Since { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public class AlWide // more than 15 values: an array 16 header of 3 bytes
    {
      public int V1 { get; set; }
      public int V2 { get; set; }
      public int V3 { get; set; }
      public int V4 { get; set; }
      public int V5 { get; set; }
      public int V6 { get; set; }
      public int V7 { get; set; }
      public int V8 { get; set; }
      public int V9 { get; set; }
      public int V10 { get; set; }
      public int V11 { get; set; }
      public int V12 { get; set; }
      public int V13 { get; set; }
      public int V14 { get; set; }
      public int V15 { get; set; }
      public int V16 { get; set; }
      public int V17 { get; set; }
    }

    public enum SchemaMode { Names, Inline, Reference }

    private static MsgPackSettings Settings(SchemaMode mode, PropertyOrder order = PropertyOrder.Declaration)
    {
      return new MsgPackSettings()
      {
        ObjectLayout = ObjectLayout.Array,
        PropertyOrder = order,
        UseInexedSchema = mode != SchemaMode.Names,
        WriteSchemaReference = mode == SchemaMode.Reference,
        SchemaStore = mode == SchemaMode.Reference ? new SchemaStore() : null
      };
    }

    private static AlPerson Person()
    {
      return new AlPerson()
      {
        Name = "Ann",
        Age = 42,
        Home = new AlAddress() { Street = "Main", Number = 7 },
        Others = new List<AlAddress>() { new AlAddress() { Street = "Side" }, null, new AlAddress() { Number = 3 } },
        Score = 0, // a nullable with a value: not a default value
        Note = "noted",
        Born = new DateTime(1980, 5, 6, 7, 8, 9, DateTimeKind.Utc)
      };
    }

    private static string Json(object value)
    {
      return JsonConvert.SerializeObject(value, new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Auto, DateTimeZoneHandling = DateTimeZoneHandling.Utc });
    }

    private T RoundTrip<T>(T value, MsgPackSettings settings)
    {
      return Serializer.Deserialize<T>(Serializer.Serialize(value, settings), settings);
    }

    [TestMethod]
    public void ValuesInOrderWithoutKeys()
    {
      byte[] data = Serializer.Serialize(new AlAddress() { Street = "a", Number = 5 }, Settings(SchemaMode.Names));
      CollectionAssert.AreEqual(new byte[] { 0x92, 0xA1, (byte)'a', 0x05 }, data, BitConverter.ToString(data));
    }

    [TestMethod]
    public void FilteredValuesAreNil()
    {
      byte[] data = Serializer.Serialize(new AlAddress(), Settings(SchemaMode.Names));
      CollectionAssert.AreEqual(new byte[] { 0x92, 0xC0, 0xC0 }, data, BitConverter.ToString(data));
    }

    [TestMethod]
    public void TypeIdWrapsTheArray()
    {
      byte[] data = Serializer.Serialize(new AlOwner() { Pet = new AlDog() { Name = "d", Barks = true } }, Settings(SchemaMode.Names));
      // [ { "": "AlDog", "@": [ "d", true ] }, nil, nil ]
      List<byte> expected = new List<byte>() { 0x93, 0x82, 0xA0, 0xA5 };
      expected.AddRange(System.Text.Encoding.UTF8.GetBytes("AlDog"));
      expected.AddRange(new byte[] { 0xA1, (byte)'@', 0x92, 0xA1, (byte)'d', 0xC3, 0xC0, 0xC0 });
      CollectionAssert.AreEqual(expected, data, BitConverter.ToString(data));
    }

    [TestMethod]
    [DataRow(SchemaMode.Names)]
    [DataRow(SchemaMode.Inline)]
    [DataRow(SchemaMode.Reference)]
    public void RoundTrip(SchemaMode mode)
    {
      AlPerson person = Person();
      Assert.AreEqual(Json(person), Json(RoundTrip(person, Settings(mode))));
    }

    [TestMethod]
    [DataRow(SchemaMode.Names)]
    [DataRow(SchemaMode.Inline)]
    [DataRow(SchemaMode.Reference)]
    public void LeftOutValuesAsJson(SchemaMode mode)
    {
      // A nil sets the value the filters left out (here null), a map leaves the key out, so its reader keeps what the constructor made: the null Note becomes "default note" again
      AlPerson person = Person();
      person.Note = null;
      person.Age = 0;
      MsgPackSettings asMap = Settings(mode);
      asMap.ObjectLayout = ObjectLayout.Map;

      AlPerson viaArray = RoundTrip(person, Settings(mode));
      Assert.AreEqual(Json(person), Json(viaArray));
      AlPerson viaMap = RoundTrip(person, asMap);
      Assert.AreEqual("default note", viaMap.Note);
      viaMap.Note = null;
      Assert.AreEqual(Json(viaArray), Json(viaMap), "the rest is the same");
    }

    [TestMethod]
    [DataRow(SchemaMode.Names)]
    [DataRow(SchemaMode.Inline)]
    [DataRow(SchemaMode.Reference)]
    public void Polymorphic(SchemaMode mode)
    {
      AlOwner owner = new AlOwner()
      {
        Pet = new AlDog() { Name = "Rex", Barks = true },
        Pets = new List<IAlPet>() { new AlCat() { Name = "Tom", Lives = 9 }, null, new AlDog() { Name = "Fido" } },
        Anything = new AlAddress() { Street = "Any", Number = 1 }
      };
      AlOwner read = RoundTrip(owner, Settings(mode));
      Assert.AreEqual(Json(owner), Json(read));
      Assert.IsInstanceOfType<AlDog>(read.Pet);
      Assert.IsInstanceOfType<AlCat>(read.Pets[0]);
      Assert.IsInstanceOfType<AlAddress>(read.Anything);
    }

    [TestMethod]
    [DataRow(SchemaMode.Names)]
    [DataRow(SchemaMode.Inline)]
    public void Structs(SchemaMode mode)
    {
      AlShape shape = new AlShape() { Center = new AlPoint() { X = 1, Y = 2 }, Corners = new[] { new AlPoint() { X = 3 }, new AlPoint() { Y = 4 } } };
      Assert.AreEqual(Json(shape), Json(RoundTrip(shape, Settings(mode))));
    }

    [TestMethod]
    [DataRow(SchemaMode.Names)]
    [DataRow(SchemaMode.Inline)]
    public void ListOfObjects(SchemaMode mode)
    {
      List<AlAddress> list = new List<AlAddress>() { new AlAddress() { Street = "x", Number = 1 }, new AlAddress() { Street = "y" } };
      Assert.AreEqual(Json(list), Json(RoundTrip(list, Settings(mode))));
    }

    [TestMethod]
    [DataRow(SchemaMode.Inline)]
    [DataRow(SchemaMode.Reference)]
    public void SchemaMatchesPositionsByName(SchemaMode mode)
    {
      // The reader has another order than the writer: the schema of the data names the positions
      MsgPackSettings writer = Settings(mode, PropertyOrder.Alphabetical);
      MsgPackSettings reader = Settings(mode, PropertyOrder.Declaration);
      reader.SchemaStore = writer.SchemaStore;

      AlPerson person = Person();
      Assert.AreEqual(Json(person), Json(Serializer.Deserialize<AlPerson>(Serializer.Serialize(person, writer), reader)));
    }

    [TestMethod]
    public void ExtraValuesAreSkippedAndMissingOnesKeepTheirDefault()
    {
      MsgPackSettings settings = Settings(SchemaMode.Names);
      byte[] longer = { 0x93, 0xA1, (byte)'a', 0x05, 0x81, 0xA1, (byte)'x', 0x01 }; // a newer writer added a property
      AlAddress read = Serializer.Deserialize<AlAddress>(longer, settings);
      Assert.AreEqual("a", read.Street);
      Assert.AreEqual(5, read.Number);

      byte[] shorter = { 0x91, 0xA1, (byte)'b' }; // an older writer
      read = Serializer.Deserialize<AlAddress>(shorter, settings);
      Assert.AreEqual("b", read.Street);
      Assert.AreEqual(0, read.Number);

      byte[] nils = { 0x96, 0xA1, (byte)'n', 0xC0, 0xC0, 0xC0, 0xC0, 0xC0 }; // Name, then nil for Age, Home, Others, Score and Note, Born is missing
      AlPerson person = Serializer.Deserialize<AlPerson>(nils, settings);
      Assert.AreEqual("n", person.Name);
      Assert.IsNull(person.Note, "a nil is null");
      Assert.IsNull(person.Score);

      byte[] fewer = { 0x95, 0xA1, (byte)'n', 0xC0, 0xC0, 0xC0, 0xC0 }; // Note is missing too
      Assert.AreEqual("default note", Serializer.Deserialize<AlPerson>(fewer, settings).Note, "missing: as the constructor made it");
    }

    [TestMethod]
    [DataRow(SchemaMode.Names)]
    [DataRow(SchemaMode.Inline)]
    public void MapsAreStillRead(SchemaMode mode)
    {
      // The layout only affects writing
      MsgPackSettings asMap = Settings(mode);
      asMap.ObjectLayout = ObjectLayout.Map;
      AlPerson person = Person();
      Assert.AreEqual(Json(person), Json(Serializer.Deserialize<AlPerson>(Serializer.Serialize(person, asMap), Settings(mode))));
    }

    [TestMethod]
    public void WithoutTypeIdsAmbiguousValuesThrow()
    {
      MsgPackSettings settings = Settings(SchemaMode.Names);
      settings.AddTypeIdOptions = AddTypeIdOption.Never;
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new AlOwner() { Pet = new AlDog() }, settings));
      StringAssert.Contains(ex.Message, nameof(ObjectLayout.Array));

      // Values of the declared type need no type id
      AlAddress address = new AlAddress() { Street = "s", Number = 2 };
      Assert.AreEqual(Json(address), Json(RoundTrip(address, settings)));
    }

    [TestMethod]
    public void ArraysByDefault()
    {
      Assert.AreEqual(ObjectLayout.Array, new MsgPackSettings().ObjectLayout);
      Assert.IsFalse(new MsgPackSettings().TrimTrailingNulls);
      CollectionAssert.AreEqual(new byte[] { 0x92, 0xA1, (byte)'a', 0x05 }, Serializer.Serialize(new AlAddress() { Street = "a", Number = 5 }, new MsgPackSettings() { UseInexedSchema = false, PropertyOrder = PropertyOrder.Declaration }));
    }

    [TestMethod]
    public void TrailingNullsTrimmed()
    {
      MsgPackSettings settings = Settings(SchemaMode.Names);
      settings.TrimTrailingNulls = true;
      CollectionAssert.AreEqual(new byte[] { 0x91, 0xA1, (byte)'a' }, Serializer.Serialize(new AlAddress() { Street = "a" }, settings));
      CollectionAssert.AreEqual(new byte[] { 0x92, 0xC0, 0x05 }, Serializer.Serialize(new AlAddress() { Number = 5 }, settings)); // only at the end
      CollectionAssert.AreEqual(new byte[] { 0x90 }, Serializer.Serialize(new AlAddress(), settings));

      settings.TrimTrailingNulls = false;
      CollectionAssert.AreEqual(new byte[] { 0x92, 0xA1, (byte)'a', 0xC0 }, Serializer.Serialize(new AlAddress() { Street = "a" }, settings));
    }

    [TestMethod]
    public void TrimmedHeaderBecomesSmaller()
    {
      MsgPackSettings settings = Settings(SchemaMode.Names);
      settings.TrimTrailingNulls = true;
      byte[] data = Serializer.Serialize(new AlWide() { V1 = 1, V2 = 2 }, settings);
      CollectionAssert.AreEqual(new byte[] { 0x92, 0x01, 0x02 }, data, BitConverter.ToString(data)); // reserved for 17 values (DC 00 11), 2 are left

      AlWide full = new AlWide() { V1 = 1, V17 = 17 };
      data = Serializer.Serialize(full, settings);
      Assert.AreEqual(0xDC, data[0]);
      Assert.AreEqual(Json(full), Json(Serializer.Deserialize<AlWide>(data, settings)));
    }

    [TestMethod]
    [DataRow(SchemaMode.Names)]
    [DataRow(SchemaMode.Inline)]
    [DataRow(SchemaMode.Reference)]
    public void TrimmedRoundTrip(SchemaMode mode)
    {
      MsgPackSettings settings = Settings(mode);
      settings.TrimTrailingNulls = true;
      AlPerson person = Person();
      person.Born = default(DateTime); // the last values are left out
      person.Note = null;
      AlPerson read = RoundTrip(person, settings);
      AlPerson expected = RoundTrip(person, Settings(mode));
      Assert.IsNull(expected.Note, "a nil sets null");
      expected.Note = "default note"; // trimmed: after the end of the array, so as the constructor made it
      Assert.AreEqual(Json(expected), Json(read));

      AlOwner owner = new AlOwner() { Pet = new AlDog() { Name = "Rex" }, Pets = new List<IAlPet>() { new AlCat() { Name = "Tom" } } };
      Assert.AreEqual(Json(owner), Json(RoundTrip(owner, settings)));
    }

    /// <summary>
    /// A nil sets what the writer left out (the default of the type, or null), as JSON reads them, not what the constructor sets: until October 2026 a nil left the property as the constructor
    /// made it, so null could not be read into an initialized property, and a left out 0 or false was read as the initial value (still so for maps, where the key is left out: analyzer rule LSMP001).
    /// </summary>
    [TestMethod]
    [DataRow(SchemaMode.Names)]
    [DataRow(SchemaMode.Inline)]
    [DataRow(SchemaMode.Reference)]
    public void ExplicitNilSetsTheLeftOutValue(SchemaMode mode)
    {
      AlInitialized value = new AlInitialized() { Retries = 0, Enabled = false, Mode = null, Limit = null, Level = 5, Since = default(DateTime) };
      AlInitialized read = RoundTrip(value, Settings(mode));
      Assert.AreEqual(0, read.Retries);
      Assert.IsFalse(read.Enabled);
      Assert.IsNull(read.Mode);
      Assert.IsNull(read.Limit);
      Assert.AreEqual(5, read.Level, "the [DefaultValue] that was left out");
      Assert.AreEqual(default(DateTime), read.Since);

      value.Level = 0; // not the [DefaultValue]: written
      Assert.AreEqual(0, RoundTrip(value, Settings(mode)).Level);
    }

    /// <summary>
    /// Values after the end of the array (a writer whose class has fewer properties, or TrimTrailingNulls) are left as the constructor made them, like the keys a map does not have.
    /// </summary>
    [TestMethod]
    public void MissingValuesKeepTheConstructorValue()
    {
      AlInitialized read = Serializer.Deserialize<AlInitialized>(new byte[] { 0x92, 0x00, 0xC0 }, Settings(SchemaMode.Names)); // [0, nil]
      Assert.AreEqual(0, read.Retries);
      Assert.IsFalse(read.Enabled);
      Assert.AreEqual("auto", read.Mode);
      Assert.AreEqual(10, read.Limit);
      Assert.AreEqual(5, read.Level);
    }

    /// <summary>
    /// The case nil cannot tell apart: a null where [DefaultValue] is not null is nil too, so it is read as the [DefaultValue] (maps keep the null; analyzer rule LSMP006).
    /// Without FilterDefaultValues the writer leaves nothing out, so a nil is null.
    /// </summary>
    [TestMethod]
    public void NullOfAPropertyWithADefaultValue()
    {
      MsgPackSettings asArray = Settings(SchemaMode.Names);
      MsgPackSettings asMap = Settings(SchemaMode.Names);
      asMap.ObjectLayout = ObjectLayout.Map;
      AlBark silent = new AlBark() { Verb = null };

      Assert.IsNull(RoundTrip(silent, asMap).Verb);
      Assert.AreEqual("Woof", RoundTrip(silent, asArray).Verb);

      MsgPackSettings everything = Settings(SchemaMode.Names);
      everything.DynamicFilters = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIncludeDynamically[0];
      Assert.IsNull(RoundTrip(silent, everything).Verb);
      Assert.AreEqual("Woof", RoundTrip(new AlBark(), everything).Verb);
    }

    [TestMethod]
    public void UndefinedLayoutThrows()
    {
      Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MsgPackSettings() { ObjectLayout = (ObjectLayout)7 });
    }
  }

  [TestClass]
  public class LsArrayLayoutTests : ArrayLayoutTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtArrayLayoutTests : ArrayLayoutTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenArrayLayoutTests : ArrayLayoutTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
