using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

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
    public void SameValuesAsMap(SchemaMode mode)
    {
      // Values the filters leave out keep what the constructor made, as with a map: the null Note becomes "default note" again
      AlPerson person = Person();
      person.Note = null;
      person.Age = 0;
      MsgPackSettings asMap = Settings(mode);
      asMap.ObjectLayout = ObjectLayout.Map;

      string viaArray = Json(RoundTrip(person, Settings(mode)));
      Assert.AreEqual(Json(RoundTrip(person, asMap)), viaArray);
      StringAssert.Contains(viaArray, "default note");
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

      byte[] nils = { 0x96, 0xA1, (byte)'n', 0xC0, 0xC0, 0xC0, 0xC0, 0xC0 }; // Name, then nil for Age, Home, Others, Score and Note
      AlPerson person = Serializer.Deserialize<AlPerson>(nils, settings);
      Assert.AreEqual("n", person.Name);
      Assert.AreEqual("default note", person.Note);
      Assert.IsNull(person.Score);
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
}
