using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPackUnitTests
{
  namespace OtherClassesWriter
  {
    public class CustomerDto
    {
      public int Id { get; set; }
      public string Name { get; set; }
      public string City { get; set; }
      public AddressDto Address { get; set; }
      public List<LineDto> Lines { get; set; }
      public Dictionary<string, AddressDto> Others { get; set; }
      public string OnlyWritten { get; set; }
    }

    public class AddressDto
    {
      public string Street { get; set; }
      public string Zip { get; set; }
    }

    public class LineDto
    {
      public string Product { get; set; }
      public int Quantity { get; set; }
    }

    public class TwoKindsDto
    {
      public AddressDto Home { get; set; }
      public LineDto Work { get; set; }
    }
  }

  namespace OtherClassesReader
  {
    /// <summary>
    /// The properties of the writer's class in another order (positions would put the values on the wrong properties), one less and one more.
    /// </summary>
    public class CustomerEntity
    {
      public string OnlyRead { get; set; } = "constructor";
      public string City { get; set; }
      public AddressEntity Address { get; set; }
      public string Name { get; set; }
      public Dictionary<string, AddressEntity> Others { get; set; }
      public int Id { get; set; }
      public List<LineEntity> Lines { get; set; }
    }

    public class AddressEntity
    {
      public string Zip { get; set; }
      public string Street { get; set; }
    }

    public class LineEntity
    {
      public int Quantity { get; set; }
      public string Product { get; set; }
    }

    /// <summary>
    /// One class for two classes of the writer: which entry its values belong to is not known.
    /// </summary>
    public class TwoKindsEntity
    {
      public AddressEntity Home { get; set; }
      public AddressEntity Work { get; set; }
    }
  }

  /// <summary>
  /// With the indexed schema, the values of an object are indexes into the entry of the writer's class. Reading into another class (a DTO into an entity) matches them by name:
  /// the root is the first entry, the other classes follow from the properties both classes have.
  /// </summary>
  public abstract class ReadingOtherClassesTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    private static OtherClassesWriter.CustomerDto Customer(int id)
    {
      return new OtherClassesWriter.CustomerDto()
      {
        Id = id,
        Name = "Ann",
        City = "Utrecht",
        Address = new OtherClassesWriter.AddressDto() { Street = "Main street", Zip = "1234 AB" },
        Lines = new List<OtherClassesWriter.LineDto>() { new OtherClassesWriter.LineDto() { Product = "Apples", Quantity = 3 } },
        Others = new Dictionary<string, OtherClassesWriter.AddressDto>() { { "work", new OtherClassesWriter.AddressDto() { Street = "Side street", Zip = "5678 CD" } } },
        OnlyWritten = "ignored"
      };
    }

    private static void AssertCustomer(int id, OtherClassesReader.CustomerEntity read)
    {
      Assert.AreEqual(id, read.Id);
      Assert.AreEqual("Ann", read.Name);
      Assert.AreEqual("Utrecht", read.City);
      Assert.AreEqual("constructor", read.OnlyRead);
      Assert.AreEqual("Main street", read.Address.Street);
      Assert.AreEqual("1234 AB", read.Address.Zip);
      Assert.AreEqual("Apples", read.Lines.Single().Product);
      Assert.AreEqual(3, read.Lines.Single().Quantity);
      Assert.AreEqual("Side street", read.Others["work"].Street);
      Assert.AreEqual("5678 CD", read.Others["work"].Zip);
    }

    /// <param name="schema">inline, store (inline, sessions shared by the calls) or reference</param>
    private static MsgPackSettings Settings(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = new MsgPackSettings() { ObjectLayout = layout };
      if (schema != "inline")
        settings.SchemaStore = new SchemaStore();
      if (schema == "reference")
        settings.WriteSchemaReference = true;
      return settings;
    }

    [TestMethod]
    [DataRow(ObjectLayout.Map, "inline")]
    [DataRow(ObjectLayout.Array, "inline")]
    [DataRow(ObjectLayout.Map, "store")]
    [DataRow(ObjectLayout.Array, "store")]
    [DataRow(ObjectLayout.Map, "reference")]
    [DataRow(ObjectLayout.Array, "reference")]
    public void RootObjectOfAnotherClass(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = Settings(layout, schema);
      for (int call = 0; call < 3; call++) // later calls use the shared session of the store
        AssertCustomer(call, Serializer.Deserialize<OtherClassesReader.CustomerEntity>(Serializer.Serialize(Customer(call), settings), settings));
    }

    [TestMethod]
    [DataRow(ObjectLayout.Map, "inline")]
    [DataRow(ObjectLayout.Array, "inline")]
    [DataRow(ObjectLayout.Map, "reference")]
    [DataRow(ObjectLayout.Array, "reference")]
    public void RootListOfAnotherClass(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = Settings(layout, schema);
      List<OtherClassesWriter.CustomerDto> written = new List<OtherClassesWriter.CustomerDto>() { Customer(1), Customer(2) };
      List<OtherClassesReader.CustomerEntity> read = Serializer.Deserialize<List<OtherClassesReader.CustomerEntity>>(Serializer.Serialize(written, settings), settings);
      Assert.AreEqual(2, read.Count);
      AssertCustomer(1, read[0]);
      AssertCustomer(2, read[1]);
    }

    /// <summary>
    /// An object whose entry is not known throws (it used to be matched by position, silently wrong when the types fit). Reading with an out ReadDifferences skips and reports it (ReportingDifferencesTests).
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void UnknownEntryThrows(ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(layout, "inline");
      OtherClassesWriter.TwoKindsDto written = new OtherClassesWriter.TwoKindsDto()
      {
        Home = new OtherClassesWriter.AddressDto() { Street = "Main street", Zip = "1234 AB" },
        Work = new OtherClassesWriter.LineDto() { Product = "Apples", Quantity = 3 }
      };
      byte[] bytes = Serializer.Serialize(written, settings);
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<OtherClassesReader.TwoKindsEntity>(bytes, settings));
      StringAssert.Contains(ex.Message, "no schema entry for " + typeof(OtherClassesReader.AddressEntity).FullName);
    }

    /// <summary>
    /// The usual case is not affected: the same classes, and a class with properties the writer did not have (or the other way around).
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void SameClassesAsBefore(ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(layout, "inline");
      OtherClassesWriter.CustomerDto read = Serializer.Deserialize<OtherClassesWriter.CustomerDto>(Serializer.Serialize(Customer(5), settings), settings);
      Assert.AreEqual(5, read.Id);
      Assert.AreEqual("Main street", read.Address.Street);
      Assert.AreEqual("ignored", read.OnlyWritten);
    }
  }

  [TestClass]
  public class LsReadingOtherClassesTests : ReadingOtherClassesTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtReadingOtherClassesTests : ReadingOtherClassesTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
