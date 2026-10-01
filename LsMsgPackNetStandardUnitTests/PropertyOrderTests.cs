using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// <see cref="MsgPackOptions.PropertyOrder"/>: the order of the map entries (names) and of the property names in the indexed schema. Readers look properties up by name, so any order reads with any other.
  /// </summary>
  public abstract class PropertyOrderTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public PropertyOrderTests()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(PoBase));
    }

    public class PoBase
    {
      public virtual string Zulu { get; set; }
      public int Alpha { get; set; }
    }

    public class PoDerived : PoBase
    {
      public string Mike { get; set; }
      public override string Zulu { get; set; } // keeps the position of PoBase.Zulu in declaration order
      public int Bravo { get; set; }
    }

    public class PoExplicit
    {
      public string Unordered1 { get; set; }
      [DataMember(Order = 2)]
      public string Two { get; set; }
      [DataMember(Order = 1)]
      public string One { get; set; }
      public string Unordered2 { get; set; }
    }

    private static PoDerived Derived()
    {
      return new PoDerived() { Zulu = "z", Alpha = 1, Mike = "m", Bravo = 2 };
    }

    private static PoExplicit Explicit()
    {
      return new PoExplicit() { Unordered1 = "u1", Two = "2", One = "1", Unordered2 = "u2" };
    }

    private static MsgPackSettings Settings(PropertyOrder order, bool schema)
    {
      return new MsgPackSettings() { PropertyOrder = order, UseInexedSchema = schema, ObjectLayout = ObjectLayout.Map }; // the names are only in the data as map keys (or in the schema)
    }

    /// <summary>
    /// The names (as fixstr) are found in the data in this order: the keys of the map, or the names in the schema.
    /// </summary>
    private static void AssertOrder(byte[] data, params string[] names)
    {
      int previous = -1;
      foreach (string name in names)
      {
        byte[] encoded = Encoding.UTF8.GetBytes(name);
        byte[] fixstr = new byte[encoded.Length + 1];
        fixstr[0] = (byte)(0xA0 | encoded.Length);
        Array.Copy(encoded, 0, fixstr, 1, encoded.Length);

        int at = IndexOf(data, fixstr);
        Assert.IsGreaterThanOrEqualTo(0, at, $"{name} not found");
        Assert.IsGreaterThan(previous, at, $"{name} is not after {string.Join(", ", names)}[previous]: {BitConverter.ToString(data)}");
        previous = at;
      }
    }

    private static int IndexOf(byte[] data, byte[] part)
    {
      for (int t = 0; t <= data.Length - part.Length; t++)
      {
        int i = 0;
        while (i < part.Length && data[t + i] == part[i])
          i++;
        if (i == part.Length)
          return t;
      }
      return -1;
    }

    private static void AssertEqual(PoDerived expected, PoDerived actual)
    {
      Assert.AreEqual(expected.Zulu, actual.Zulu);
      Assert.AreEqual(expected.Alpha, actual.Alpha);
      Assert.AreEqual(expected.Mike, actual.Mike);
      Assert.AreEqual(expected.Bravo, actual.Bravo);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Alphabetical(bool schema)
    {
      AssertOrder(Serializer.Serialize(Derived(), Settings(PropertyOrder.Alphabetical, schema)), "Alpha", "Bravo", "Mike", "Zulu");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Declaration(bool schema)
    {
      AssertOrder(Serializer.Serialize(Derived(), Settings(PropertyOrder.Declaration, schema)), "Zulu", "Alpha", "Mike", "Bravo");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Explicit(bool schema)
    {
      AssertOrder(Serializer.Serialize(Explicit(), Settings(PropertyOrder.Explicit, schema)), "One", "Two", "Unordered1", "Unordered2");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TypeThenDeclaration(bool schema)
    {
      // System.Int32 before System.String, each in declaration order (base class first)
      AssertOrder(Serializer.Serialize(Derived(), Settings(PropertyOrder.TypeThenDeclaration, schema)), "Alpha", "Bravo", "Zulu", "Mike");
    }

    [TestMethod]
    public void ExplicitWithoutOrdersIsDeclaration()
    {
      AssertOrder(Serializer.Serialize(Derived(), Settings(PropertyOrder.Explicit, false)), "Zulu", "Alpha", "Mike", "Bravo");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AnyOrderReadsWithAnyOther(bool schema)
    {
      PropertyOrder[] orders = (PropertyOrder[])Enum.GetValues(typeof(PropertyOrder));
      foreach (PropertyOrder writeOrder in orders)
      {
        byte[] data = Serializer.Serialize(Derived(), Settings(writeOrder, schema));
        foreach (PropertyOrder readOrder in orders)
          AssertEqual(Derived(), Serializer.Deserialize<PoDerived>(data, Settings(readOrder, schema)));
      }
    }

    [TestMethod]
    public void CachedSchemasPerOrder()
    {
      SchemaStore store = new SchemaStore();
      MsgPackSettings alphabetical = new MsgPackSettings() { PropertyOrder = PropertyOrder.Alphabetical, SchemaStore = store, WriteSchemaReference = true };
      MsgPackSettings declaration = new MsgPackSettings() { PropertyOrder = PropertyOrder.Declaration, SchemaStore = store, WriteSchemaReference = true };

      byte[] first = Serializer.Serialize(Derived(), alphabetical);
      byte[] second = Serializer.Serialize(Derived(), declaration);

      // The order is part of the schema, a session of one order must not be used for another
      Assert.AreEqual(2, store.Count);
      AssertOrder(ReferencedSchema(store, first), "Alpha", "Bravo", "Mike", "Zulu");
      AssertOrder(ReferencedSchema(store, second), "Zulu", "Alpha", "Mike", "Bravo");

      AssertEqual(Derived(), Serializer.Deserialize<PoDerived>(first, alphabetical));
      AssertEqual(Derived(), Serializer.Deserialize<PoDerived>(second, alphabetical));
    }

    private static byte[] ReferencedSchema(SchemaStore store, byte[] data)
    {
      foreach (SchemaId id in store.GetSchemaIds())
        if (IndexOf(data, id.ToByteArray()) >= 0)
          return store.GetSchema(id);
      Assert.Fail("The data does not refer to a schema of the store");
      return null;
    }

    [TestMethod]
    public void UndefinedOrderThrows()
    {
      Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MsgPackSettings() { PropertyOrder = (PropertyOrder)42 });
    }
  }

  [TestClass]
  public class LsPropertyOrderTests : PropertyOrderTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtPropertyOrderTests : PropertyOrderTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
