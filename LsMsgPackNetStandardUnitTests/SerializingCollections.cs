using LsMsgPack;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Collections are only wrapped in a map ({ "": typeId, "@": elements }) when needed and their elements only get a type id when ambiguous.
  /// </summary>
  public abstract class SerializingCollections
  {
    protected abstract ISerializerUnderTest Serializer { get; }

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

    /// <summary>
    /// IEnumerable&lt;T&gt; and a public Add(T), as XmlSerializer wants it: not an IList or ICollection&lt;T&gt;.
    /// </summary>
    public class AddOnlyCollection<T> : IEnumerable<T>
    {
      private readonly List<T> _items = new List<T>();
      public void Add(T item) { _items.Add(item); }
      public IEnumerator<T> GetEnumerator() { return _items.GetEnumerator(); }
      IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class ConstructedCollection<T> : IEnumerable<T>
    {
      private readonly List<T> _items;
      public ConstructedCollection(IEnumerable<T> items) { _items = new List<T>(items); }
      public IEnumerator<T> GetEnumerator() { return _items.GetEnumerator(); }
      IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    /// <summary>
    /// A dictionary with only the generic interface (no IDictionary).
    /// </summary>
    public class GenericOnlyDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
      private readonly Dictionary<TKey, TValue> _items = new Dictionary<TKey, TValue>();
      public TValue this[TKey key] { get { return _items[key]; } set { _items[key] = value; } }
      public ICollection<TKey> Keys { get { return _items.Keys; } }
      public ICollection<TValue> Values { get { return _items.Values; } }
      public int Count { get { return _items.Count; } }
      public bool IsReadOnly { get { return false; } }
      public void Add(TKey key, TValue value) { _items.Add(key, value); }
      public void Add(KeyValuePair<TKey, TValue> item) { _items.Add(item.Key, item.Value); }
      public void Clear() { _items.Clear(); }
      public bool Contains(KeyValuePair<TKey, TValue> item) { return ((ICollection<KeyValuePair<TKey, TValue>>)_items).Contains(item); }
      public bool ContainsKey(TKey key) { return _items.ContainsKey(key); }
      public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) { ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex); }
      public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() { return _items.GetEnumerator(); }
      public bool Remove(TKey key) { return _items.Remove(key); }
      public bool Remove(KeyValuePair<TKey, TValue> item) { return ((ICollection<KeyValuePair<TKey, TValue>>)_items).Remove(item); }
      public bool TryGetValue(TKey key, out TValue value) { return _items.TryGetValue(key, out value); }
      IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class PairCollections
    {
      public List<KeyValuePair<long, string>> List { get; set; }
      public System.Collections.ObjectModel.Collection<KeyValuePair<long, string>> Collection { get; set; }
      public AddOnlyCollection<KeyValuePair<long, string>> AddOnly { get; set; }
      public ConstructedCollection<KeyValuePair<long, string>> Constructed { get; set; }
      public GenericOnlyDictionary<long, string> GenericOnly { get; set; }
      public IEnumerable<KeyValuePair<long, string>> Enumerable { get; set; }
    }

    private static readonly KeyValuePair<long, string>[] SamplePairs = { new KeyValuePair<long, string>(1, "a"), new KeyValuePair<long, string>(2, "b"), new KeyValuePair<long, string>(1, "duplicate key") };

    private static string Show(IEnumerable<KeyValuePair<long, string>> pairs)
    {
      return pairs is null ? "null" : string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"));
    }

    /// <summary>
    /// Only arrays of KeyValuePair were written as a map, other collections of pairs wrote each pair as an empty object (a KeyValuePair has no settable properties): the keys and values were lost.
    /// </summary>
    [TestMethod]
    [DataRow(false, ObjectLayout.Map)]
    [DataRow(true, ObjectLayout.Map)]
    [DataRow(false, ObjectLayout.Array)]
    [DataRow(true, ObjectLayout.Array)]
    public void CollectionsOfKeyValuePairs(bool useSchema, ObjectLayout layout)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = layout };
      AddOnlyCollection<KeyValuePair<long, string>> addOnly = new AddOnlyCollection<KeyValuePair<long, string>>();
      foreach (KeyValuePair<long, string> pair in SamplePairs)
        addOnly.Add(pair);
      GenericOnlyDictionary<long, string> genericOnly = new GenericOnlyDictionary<long, string>() { { 1, "a" }, { 2, "b" } };
      PairCollections value = new PairCollections()
      {
        List = SamplePairs.ToList(),
        Collection = new System.Collections.ObjectModel.Collection<KeyValuePair<long, string>>(SamplePairs.ToList()),
        AddOnly = addOnly,
        Constructed = new ConstructedCollection<KeyValuePair<long, string>>(SamplePairs),
        GenericOnly = genericOnly,
        Enumerable = SamplePairs.ToList()
      };

      PairCollections read = Serializer.Deserialize<PairCollections>(Serializer.Serialize(value, settings), settings);
      string expected = Show(SamplePairs); // duplicate keys are kept, in order
      Assert.AreEqual(expected, Show(read.List));
      Assert.AreEqual(expected, Show(read.Collection));
      Assert.AreEqual(expected, Show(read.AddOnly));
      Assert.AreEqual(expected, Show(read.Constructed));
      Assert.AreEqual("1=a, 2=b", Show(read.GenericOnly));
      Assert.AreEqual(expected, Show(read.Enumerable));

      // As the root, the same bytes as an array of pairs
      byte[] asArray = Serializer.Serialize(SamplePairs, settings);
      CollectionAssert.AreEqual(asArray, Serializer.Serialize(SamplePairs.ToList(), settings));
      CollectionAssert.AreEqual(asArray, Serializer.Serialize(addOnly, settings));
      Assert.AreEqual(expected, Show(Serializer.Deserialize<AddOnlyCollection<KeyValuePair<long, string>>>(asArray, settings)));
      Assert.AreEqual(expected, Show(Serializer.Deserialize<List<KeyValuePair<long, string>>>(asArray, settings)));
      Assert.AreEqual("1=a, 2=b", Show(Serializer.Deserialize<GenericOnlyDictionary<long, string>>(Serializer.Serialize(genericOnly, settings), settings)));
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
      CollectionAssert.AreEqual(new byte[] { 0x92, 1, 2 }, Serializer.Serialize(new List<int>() { 1, 2 }, Settings()));
      CollectionAssert.AreEqual(new byte[] { 0x92, 1, 2 }, Serializer.Serialize(new[] { 1, 2 }, Settings()));
      CollectionAssert.AreEqual(new byte[] { 0x81, 0xA1, (byte)'a', 1 }, Serializer.Serialize(new Dictionary<string, int>() { { "a", 1 } }, Settings()));
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
      MsgPackSettings settings = Settings();
      settings.ObjectLayout = ObjectLayout.Map; // the test looks the properties up by name
      byte[] buffer = Serializer.Serialize(org, settings);
      Dictionary<object, object> root = UnpackMap(buffer);

      object[] cats = root["Cats"] as object[]; // a plain array, not { "@": [...] }
      Assert.IsNotNull(cats);
      Assert.IsFalse(((KeyValuePair<object, object>[])cats[0]).Any(p => "".Equals(p.Key)), "A Cat in a List<Cat> is not ambiguous");

      KeyValuePair<object, object>[] catsByName = (KeyValuePair<object, object>[])root["CatsByName"];
      Assert.IsFalse(((KeyValuePair<object, object>[])catsByName[0].Value).Any(p => "".Equals(p.Key)), "A Cat in a Dictionary<string, Cat> is not ambiguous");

      KeyValuePair<object, object>[] pets = (KeyValuePair<object, object>[])root["Pets"];
      Assert.IsTrue(((KeyValuePair<object, object>[])pets[0].Value).Any(p => "".Equals(p.Key) && "Dog".Equals(p.Value)), "A Dog in a Dictionary<int, IIPet> is ambiguous");

      WithCats ret = Serializer.Deserialize<WithCats>(buffer, Settings());
      Assert.AreEqual("Mia", ret.Cats[0].Name);
      Assert.AreEqual("Mia", ret.CatsByName["Mia"].Name);
      Assert.IsInstanceOfType<Dog>(ret.Pets[1]);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious, false)]
    [DataRow(AddTypeIdOption.Always, false)]
    [DataRow(AddTypeIdOption.Never, false)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true)]
    [DataRow(AddTypeIdOption.Always, true)]
    public void SerializePropertiesOnType(AddTypeIdOption option, bool useSchema)
    {
      MsgPackSettings settings = Settings(option);
      settings.UseInexedSchema = useSchema;

      PagedList org = new PagedList() { 1, 2, 3 };
      org.Page = 7;

      PagedList ret = Serializer.Deserialize<PagedList>(Serializer.Serialize(org, settings), settings);
      Assert.AreEqual(7, ret.Page);
      CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ret);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious, false)]
    [DataRow(AddTypeIdOption.Never, false)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true)]
    [DataRow(AddTypeIdOption.Always, true)]
    public void SerializePropertiesOnProperty(AddTypeIdOption option, bool useSchema)
    {
      MsgPackSettings settings = Settings(option);
      settings.UseInexedSchema = useSchema;

      WithTaggedList org = new WithTaggedList()
      {
        Tagged = new TaggedList() { "a" },
        NotTagged = new TaggedList() { "b" },
      };
      org.Tagged.Tag = "tag";
      org.NotTagged.Tag = "not serialized";

      WithTaggedList ret = Serializer.Deserialize<WithTaggedList>(Serializer.Serialize(org, settings), settings);
      Assert.AreEqual("tag", ret.Tagged.Tag);
      CollectionAssert.AreEqual(new[] { "a" }, ret.Tagged);
      Assert.IsNull(ret.NotTagged.Tag);
      CollectionAssert.AreEqual(new[] { "b" }, ret.NotTagged);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    public void SchemaOnlyListsSerializedPropertiesOfCollections(AddTypeIdOption option)
    {
      MsgPackSettings settings = Settings(option);
      settings.UseInexedSchema = true;

      List<IIPet> org = new List<IIPet>() { new Cat() { Name = "Mia" }, new Dog() { Name = "Rex" } };
      byte[] buffer = Serializer.Serialize(org, settings);

      IndexedSchemaTypeResolver schema = IndexedSchemaTypeResolver.Unpack(new MemoryStream(buffer), settings);
      foreach (ComplexTypeDef def in schema.ByTypeId.Where(d => typeof(IEnumerable).IsAssignableFrom(d.Type)))
        Assert.IsEmpty(def.Props, $"{def.TypeName} lists properties that are not serialized: {string.Join(", ", def.Props)}");

      List<IIPet> ret = Serializer.Deserialize<List<IIPet>>(buffer, settings);
      Assert.IsInstanceOfType<Cat>(ret[0]);
      Assert.IsInstanceOfType<Dog>(ret[1]);
      Assert.AreEqual("Rex", ret[1].Name);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    public void SchemaDoesNotListStaticallyIgnoredPropertiesOfCollections(AddTypeIdOption option)
    {
      MsgPackSettings settings = Settings(option);
      settings.UseInexedSchema = true;

      PagedList org = new PagedList() { 1, 2, 3 };
      org.Page = 7;
      byte[] buffer = Serializer.Serialize(org, settings);

      IndexedSchemaTypeResolver schema = IndexedSchemaTypeResolver.Unpack(new MemoryStream(buffer), settings);
      ComplexTypeDef def = schema.ByTypeId.Single(d => d.Type == typeof(PagedList));
      CollectionAssert.Contains(def.Props, nameof(PagedList.Page));
      CollectionAssert.DoesNotContain(def.Props, nameof(PagedList.Count), "Count cannot be set, so it is never serialized");

      PagedList ret = Serializer.Deserialize<PagedList>(buffer, settings);
      Assert.AreEqual(7, ret.Page);
      CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ret);
    }

    [TestMethod]
    public void SerializeElementsFalse()
    {
      Bag org = new Bag() { Items = new List<string>() { "a", "b" } };
      byte[] buffer = Serializer.Serialize(org, Settings());

      Dictionary<object, object> root = UnpackMap(buffer);
      Assert.HasCount(1, root); // Only the Items property
      CollectionAssert.AreEqual(new[] { "a", "b" }, Serializer.Deserialize<Bag>(buffer, Settings()).Items);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    public void DictionaryWithoutElementsIsNotReadAsEntries(AddTypeIdOption option)
    {
      NamedDictionary org = new NamedDictionary() { { "not serialized", 1 } };
      org.Name = "name";

      NamedDictionary ret = Serializer.Deserialize<NamedDictionary>(Serializer.Serialize(org, Settings(option)), Settings(option));
      Assert.AreEqual("name", ret.Name);
      Assert.IsEmpty(ret);
    }

    [TestMethod]
    public void DictionaryWithEntriesAndProperties()
    {
      NamedDictionaryWithEntries org = new NamedDictionaryWithEntries() { { "one", 1 } };
      org.Name = "name";

      NamedDictionaryWithEntries ret = Serializer.Deserialize<NamedDictionaryWithEntries>(Serializer.Serialize(org, Settings()), Settings());
      Assert.AreEqual("name", ret.Name);
      Assert.AreEqual(1, ret["one"]);
    }
  }

  [TestClass]
  public class LsSerializingCollections : SerializingCollections
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingCollections : SerializingCollections
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
