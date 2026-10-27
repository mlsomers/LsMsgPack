using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
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

    public class WithExtraDto
    {
      public AddressDto Address { get; set; }
      public object Extra { get; set; }
    }

    /// <summary>
    /// A collection written by its properties, not as its elements: a map like an object.
    /// </summary>
    [SerializeEnumerable(SerializeElements = false, SerializeProperties = true)]
    public class AddressPageDto : IEnumerable<AddressDto>
    {
      public int Total { get; set; }
      public List<AddressDto> Items { get; set; }

      public IEnumerator<AddressDto> GetEnumerator() { return Items.GetEnumerator(); }
      IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class AddressBookDto
    {
      public string Owner { get; set; }
      public AddressPageDto Page { get; set; }
    }

    /// <summary>
    /// A collection written with its elements and its properties: <c>{ "@": elements, ...properties }</c>.
    /// </summary>
    [SerializeEnumerable(SerializeProperties = true)]
    public class TaggedLinesDto : List<LineDto>
    {
      public string Tag { get; set; }
    }

    /// <summary>
    /// Names the writer's classes as no reader resolves them (a server's classes the client does not have).
    /// </summary>
    public class ServerNames : IMsgPackTypeResolver
    {
      public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackOptions settings)
      {
        return type.Namespace == typeof(ServerNames).Namespace ? "Server." + type.Name : null;
      }

      public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackOptions settings)
      {
        return null;
      }
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

    public class WithExtraEntity
    {
      public AddressEntity Address { get; set; }
      public AddressEntity Extra { get; set; }
    }

    public class PageEntity<T>
    {
      public int Total { get; set; }
      public List<T> Items { get; set; }
    }

    public class AddressBookEntity
    {
      public string Owner { get; set; }
      public PageEntity<AddressEntity> Page { get; set; }
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
    /// Writer and reader settings for a reader without the writer's classes (their names are not resolved, see <see cref="OtherClassesWriter.ServerNames"/>).
    /// </summary>
    private static MsgPackSettings[] WithoutWriterClasses(ObjectLayout layout, string schema, SchemaVersion version)
    {
      MsgPackSettings writer = Settings(layout, schema);
      writer.SchemaVersion = version;
      writer.TypeResolvers = new IMsgPackTypeResolver[] { new OtherClassesWriter.ServerNames() };
      MsgPackSettings reader = Settings(layout, schema);
      reader.SchemaStore = writer.SchemaStore;
      return new[] { writer, reader };
    }

    /// <summary>
    /// A reader without the writer's classes pairs its classes by the types of schema version 2: the root by the root entry, then the entries of the properties with the same names.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map, "inline")]
    [DataRow(ObjectLayout.Array, "inline")]
    [DataRow(ObjectLayout.Map, "store")]
    [DataRow(ObjectLayout.Array, "reference")]
    public void ClassesTheReaderDoesNotHave(ObjectLayout layout, string schema)
    {
      MsgPackSettings[] settings = WithoutWriterClasses(layout, schema, SchemaVersion.Described);
      for (int call = 0; call < 3; call++) // later calls use the shared session of the store
        AssertCustomer(call, Serializer.Deserialize<OtherClassesReader.CustomerEntity>(Serializer.Serialize(Customer(call), settings[0]), settings[1]));

      List<OtherClassesWriter.CustomerDto> written = new List<OtherClassesWriter.CustomerDto>() { Customer(1), Customer(2) };
      List<OtherClassesReader.CustomerEntity> read = Serializer.Deserialize<List<OtherClassesReader.CustomerEntity>>(Serializer.Serialize(written, settings[0]), settings[1]);
      AssertCustomer(1, read[0]);
      AssertCustomer(2, read[1]);
    }

    private static OtherClassesWriter.AddressPageDto AddressPage()
    {
      return new OtherClassesWriter.AddressPageDto()
      {
        Total = 7,
        Items = new List<OtherClassesWriter.AddressDto>() { new OtherClassesWriter.AddressDto() { Street = "Main street", Zip = "1234 AB" }, new OtherClassesWriter.AddressDto() { Street = "Side street" } }
      };
    }

    private static void AssertAddressPage(OtherClassesReader.PageEntity<OtherClassesReader.AddressEntity> read)
    {
      Assert.AreEqual(7, read.Total);
      Assert.AreEqual(2, read.Items.Count);
      Assert.AreEqual("Main street", read.Items[0].Street);
      Assert.AreEqual("1234 AB", read.Items[0].Zip);
      Assert.AreEqual("Side street", read.Items[1].Street);
    }

    /// <summary>
    /// A collection written by its properties (<c>SerializeElements = false</c>) is an object of its entry in schema version 2 (not a list of its elements), as the root and as a property.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map, "inline")]
    [DataRow(ObjectLayout.Array, "inline")]
    [DataRow(ObjectLayout.Map, "store")]
    [DataRow(ObjectLayout.Array, "reference")]
    public void CollectionWrittenByItsPropertiesTheReaderDoesNotHave(ObjectLayout layout, string schema)
    {
      MsgPackSettings[] settings = WithoutWriterClasses(layout, schema, SchemaVersion.Described);
      for (int call = 0; call < 2; call++)
        AssertAddressPage(Serializer.Deserialize<OtherClassesReader.PageEntity<OtherClassesReader.AddressEntity>>(Serializer.Serialize(AddressPage(), settings[0]), settings[1]));

      OtherClassesWriter.AddressBookDto book = new OtherClassesWriter.AddressBookDto() { Owner = "Ann", Page = AddressPage() };
      OtherClassesReader.AddressBookEntity read = Serializer.Deserialize<OtherClassesReader.AddressBookEntity>(Serializer.Serialize(book, settings[0]), settings[1]);
      Assert.AreEqual("Ann", read.Owner);
      AssertAddressPage(read.Page);
    }

    /// <summary>
    /// A collection written with its elements and its properties, read into a list: the elements are objects of the entry of its content.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void CollectionWithPropertiesReadAsAList(ObjectLayout layout)
    {
      MsgPackSettings[] settings = WithoutWriterClasses(layout, "inline", SchemaVersion.Described);
      OtherClassesWriter.TaggedLinesDto written = new OtherClassesWriter.TaggedLinesDto() { Tag = "x" };
      written.Add(new OtherClassesWriter.LineDto() { Product = "Apples", Quantity = 3 });
      List<OtherClassesReader.LineEntity> read = Serializer.Deserialize<List<OtherClassesReader.LineEntity>>(Serializer.Serialize(written, settings[0]), settings[1]);
      Assert.AreEqual("Apples", read.Single().Product);
      Assert.AreEqual(3, read.Single().Quantity);
    }

    /// <summary>
    /// A type id of a class the reader does not have: the reader's class paired with its entry, when it fits.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void TypeIdOfAClassTheReaderDoesNotHave(ObjectLayout layout)
    {
      MsgPackSettings[] settings = WithoutWriterClasses(layout, "inline", SchemaVersion.Described);
      OtherClassesWriter.WithExtraDto written = new OtherClassesWriter.WithExtraDto()
      {
        Address = new OtherClassesWriter.AddressDto() { Street = "Main street" },
        Extra = new OtherClassesWriter.AddressDto() { Street = "Side street" }
      };
      OtherClassesReader.WithExtraEntity read = Serializer.Deserialize<OtherClassesReader.WithExtraEntity>(Serializer.Serialize(written, settings[0]), settings[1]);
      Assert.AreEqual("Main street", read.Address.Street);
      Assert.AreEqual("Side street", read.Extra.Street);
    }

    /// <summary>
    /// Schema version 1 does not say which classes the properties hold: only the root (the first entry) is paired.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void ClassesTheReaderDoesNotHaveVersion1(ObjectLayout layout)
    {
      MsgPackSettings[] settings = WithoutWriterClasses(layout, "inline", SchemaVersion.Names);
      OtherClassesWriter.CustomerDto flat = new OtherClassesWriter.CustomerDto() { Id = 4, Name = "Ann", City = "Utrecht" };
      OtherClassesReader.CustomerEntity read = Serializer.Deserialize<OtherClassesReader.CustomerEntity>(Serializer.Serialize(flat, settings[0]), settings[1]);
      Assert.AreEqual(4, read.Id);
      Assert.AreEqual("Ann", read.Name);
      Assert.AreEqual("Utrecht", read.City);

      byte[] nested = Serializer.Serialize(Customer(5), settings[0]);
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<OtherClassesReader.CustomerEntity>(nested, settings[1]));
      StringAssert.Contains(ex.Message, "no schema entry for " + typeof(OtherClassesReader.AddressEntity).Namespace); // the address or the line, whichever is read first
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

  [TestClass]
  public class GenReadingOtherClassesTests : ReadingOtherClassesTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
