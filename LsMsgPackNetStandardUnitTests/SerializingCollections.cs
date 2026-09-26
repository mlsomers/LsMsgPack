using LsMsgPack;
using LsMsgPack.TypeResolving.Attributes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Collections are only wrapped in a map ({ "": typeId, "@": elements }) when needed and their elements only get a type id when ambiguous.
  /// </summary>
  [TestClass]
  public class SerializingCollections
  {
    public SerializingCollections()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(IIPet));
    }

    public class WithCats
    {
      public List<Cat> Cats { get; set; }
      public Dictionary<string, Cat> CatsByName { get; set; }
      public Dictionary<int, IIPet> Pets { get; set; }
    }

    [SerializeEnumerable(SerializeProperties = true)]
    public class PagedList : List<int>
    {
      public int Page { get; set; }
    }

    public class TaggedList : List<string>
    {
      public string Tag { get; set; }
    }

    public class WithTaggedList
    {
      [SerializeEnumerable(SerializeProperties = true)]
      public TaggedList Tagged { get; set; }

      public TaggedList NotTagged { get; set; }
    }

    [SerializeEnumerable(SerializeElements = false, SerializeProperties = true)]
    public class Bag : IEnumerable<string>
    {
      public List<string> Items { get; set; } = new List<string>();

      public IEnumerator<string> GetEnumerator() { return Items.GetEnumerator(); }

      IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    [SerializeEnumerable(SerializeElements = false, SerializeProperties = true)]
    public class NamedDictionary : Dictionary<string, int>
    {
      public string Name { get; set; }
    }

    [SerializeEnumerable(SerializeProperties = true)]
    public class NamedDictionaryWithEntries : Dictionary<string, int>
    {
      public string Name { get; set; }
    }

    private static MsgPackSettings Settings(AddTypeIdOption option = AddTypeIdOption.IfAmbiguious)
    {
      return new MsgPackSettings() { UseInexedSchema = false, AddTypeIdOptions = option };
    }

    private static Dictionary<object, object> UnpackMap(byte[] buffer)
    {
      KeyValuePair<object, object>[] pairs = (KeyValuePair<object, object>[])MsgPackItem.Unpack(buffer).Value;
      return pairs.ToDictionary(p => p.Key, p => p.Value);
    }

    [TestMethod]
    public void RootCollectionIsNotWrapped()
    {
      CollectionAssert.AreEqual(new byte[] { 0x92, 1, 2 }, MsgPackSerializer.Serialize(new List<int>() { 1, 2 }, Settings()));
      CollectionAssert.AreEqual(new byte[] { 0x92, 1, 2 }, MsgPackSerializer.Serialize(new[] { 1, 2 }, Settings()));
      CollectionAssert.AreEqual(new byte[] { 0x81, 0xA1, (byte)'a', 1 }, MsgPackSerializer.Serialize(new Dictionary<string, int>() { { "a", 1 } }, Settings()));
    }

    [TestMethod]
    public void ElementsOnlyGetTypeIdsWhenAmbiguous()
    {
      WithCats org = new WithCats()
      {
        Cats = new List<Cat>() { new Cat() { Name = "Mia" } },
        CatsByName = new Dictionary<string, Cat>() { { "Mia", new Cat() { Name = "Mia" } } },
        Pets = new Dictionary<int, IIPet>() { { 1, new Dog() { Name = "Rex" } } },
      };
      byte[] buffer = MsgPackSerializer.Serialize(org, Settings());
      Dictionary<object, object> root = UnpackMap(buffer);

      object[] cats = root["Cats"] as object[]; // a plain array, not { "@": [...] }
      Assert.IsNotNull(cats);
      Assert.IsFalse(((KeyValuePair<object, object>[])cats[0]).Any(p => "".Equals(p.Key)), "A Cat in a List<Cat> is not ambiguous");

      KeyValuePair<object, object>[] catsByName = (KeyValuePair<object, object>[])root["CatsByName"];
      Assert.IsFalse(((KeyValuePair<object, object>[])catsByName[0].Value).Any(p => "".Equals(p.Key)), "A Cat in a Dictionary<string, Cat> is not ambiguous");

      KeyValuePair<object, object>[] pets = (KeyValuePair<object, object>[])root["Pets"];
      Assert.IsTrue(((KeyValuePair<object, object>[])pets[0].Value).Any(p => "".Equals(p.Key) && "Dog".Equals(p.Value)), "A Dog in a Dictionary<int, IIPet> is ambiguous");

      WithCats ret = MsgPackSerializer.Deserialize<WithCats>(buffer, Settings());
      Assert.AreEqual("Mia", ret.Cats[0].Name);
      Assert.AreEqual("Mia", ret.CatsByName["Mia"].Name);
      Assert.IsInstanceOfType<Dog>(ret.Pets[1]);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious, false)]
    [DataRow(AddTypeIdOption.Always, false)]
    [DataRow(AddTypeIdOption.Never, false)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true)]
    public void SerializePropertiesOnType(AddTypeIdOption option, bool useSchema)
    {
      MsgPackSettings settings = Settings(option);
      settings.UseInexedSchema = useSchema;

      PagedList org = new PagedList() { 1, 2, 3 };
      org.Page = 7;

      PagedList ret = MsgPackSerializer.Deserialize<PagedList>(MsgPackSerializer.Serialize(org, settings), settings);
      Assert.AreEqual(7, ret.Page);
      CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ret);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Never)]
    public void SerializePropertiesOnProperty(AddTypeIdOption option)
    {
      WithTaggedList org = new WithTaggedList()
      {
        Tagged = new TaggedList() { "a" },
        NotTagged = new TaggedList() { "b" },
      };
      org.Tagged.Tag = "tag";
      org.NotTagged.Tag = "not serialized";

      WithTaggedList ret = MsgPackSerializer.Deserialize<WithTaggedList>(MsgPackSerializer.Serialize(org, Settings(option)), Settings(option));
      Assert.AreEqual("tag", ret.Tagged.Tag);
      CollectionAssert.AreEqual(new[] { "a" }, ret.Tagged);
      Assert.IsNull(ret.NotTagged.Tag);
      CollectionAssert.AreEqual(new[] { "b" }, ret.NotTagged);
    }

    [TestMethod]
    public void SerializeElementsFalse()
    {
      Bag org = new Bag() { Items = new List<string>() { "a", "b" } };
      byte[] buffer = MsgPackSerializer.Serialize(org, Settings());

      Dictionary<object, object> root = UnpackMap(buffer);
      Assert.HasCount(1, root); // Only the Items property
      CollectionAssert.AreEqual(new[] { "a", "b" }, MsgPackSerializer.Deserialize<Bag>(buffer, Settings()).Items);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    public void DictionaryWithoutElementsIsNotReadAsEntries(AddTypeIdOption option)
    {
      NamedDictionary org = new NamedDictionary() { { "not serialized", 1 } };
      org.Name = "name";

      NamedDictionary ret = MsgPackSerializer.Deserialize<NamedDictionary>(MsgPackSerializer.Serialize(org, Settings(option)), Settings(option));
      Assert.AreEqual("name", ret.Name);
      Assert.IsEmpty(ret);
    }

    [TestMethod]
    public void DictionaryWithEntriesAndProperties()
    {
      NamedDictionaryWithEntries org = new NamedDictionaryWithEntries() { { "one", 1 } };
      org.Name = "name";

      NamedDictionaryWithEntries ret = MsgPackSerializer.Deserialize<NamedDictionaryWithEntries>(MsgPackSerializer.Serialize(org, Settings()), Settings());
      Assert.AreEqual("name", ret.Name);
      Assert.AreEqual(1, ret["one"]);
    }
  }
}
