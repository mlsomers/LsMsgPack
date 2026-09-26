using LsMsgPack;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Round-trip tests for types that need conversion while deserializing (nested collections, dictionaries, enums, Guids, nullables...).
  /// </summary>
  [TestClass]
  public class DeserializingEdgeCases
  {
    public DeserializingEdgeCases()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(IIPet));
    }

    public enum Colour { Red = 1, Green = 2, Blue = 300 }

    public enum Tiny : byte { A = 1, B = 200 }

    public class WithEnums
    {
      public Colour Colour { get; set; }
      public Colour? NullableColour { get; set; }
      public Tiny Tiny { get; set; }
      public Colour[] Colours { get; set; }
      public List<Colour> ColourList { get; set; }
    }

    public class WithDictionaries
    {
      public Dictionary<string, int> StringToInt { get; set; }
      public Dictionary<int, Cat> IntToCat { get; set; }
      public Dictionary<string, Dictionary<string, int>> Nested { get; set; }
      public Dictionary<string, List<int>> StringToList { get; set; }
      public SortedDictionary<string, long> Sorted { get; set; }
    }

    public class WithCollections
    {
      public List<int[]> ListOfArrays { get; set; }
      public int[][] Jagged { get; set; }
      public List<List<string>> ListOfLists { get; set; }
      public List<Guid> Guids { get; set; }
      public Guid[] GuidArray { get; set; }
      public List<int?> NullableInts { get; set; }
      public List<long> Longs { get; set; }
      public sbyte[] SBytes { get; set; }
      public List<double> Doubles { get; set; }
      public HashSet<string> Set { get; set; }
      public List<Cat> Cats { get; set; }
      public List<IIPet> Pets { get; set; }
      public ObservableCollection<Dog> Dogs { get; set; }
    }

    public class WithKeyValuePairs
    {
      public KeyValuePair<int, string>[] Pairs { get; set; }
    }

    public class WithScalars
    {
      public float Single { get; set; }
      public double Double { get; set; }
      public decimal Decimal { get; set; }
      public ulong ULong { get; set; }
      public short Short { get; set; }
      public bool? NullableBool { get; set; }
      public long? NullableLong { get; set; }
      public DateTime Date { get; set; }
    }

    [SerializeEnumerable(typeof(Dog))]
    public class DogList : List<Dog> { }

    public class WithCustomCollection
    {
      public DogList Dogs { get; set; }
    }

    public class WithStringList { public List<string> Items { get; set; } }
    public class WithObjectList { public List<object> Items { get; set; } }
    public class WithIntCollection { public ObservableCollection<int> Items { get; set; } }

    public class WithObject
    {
      public object Anything { get; set; }
    }

    public class WithAbstractions
    {
      public IList<int> IntList { get; set; }
      public IEnumerable<Cat> Cats { get; set; }
      public IReadOnlyList<string> Strings { get; set; }
      public ISet<int> Set { get; set; }
      public IDictionary<string, int> Dictionary { get; set; }
      public IReadOnlyDictionary<int, string> ReadOnlyDictionary { get; set; }
    }

    public class WithBoxedValues
    {
      public object Guid { get; set; }
      public object Enum { get; set; }
      public object Pets { get; set; }
      public object[] Objects { get; set; }
      public ValueType Number { get; set; }
    }

    public class WithStacksAndQueues
    {
      public Stack<int> Stack { get; set; }
      public Queue<string> Queue { get; set; }
      public LinkedList<int> Linked { get; set; }
    }

    private static MsgPackSettings Settings(AddTypeIdOption option)
    {
      return new MsgPackSettings()
      {
        UseInexedSchema = false,
        AddTypeIdOptions = option,
        DynamicFilters = Array.Empty<IMsgPackPropertyIncludeDynamically>()
      };
    }

    private static T RoundTrip<T>(T item, MsgPackSettings settings)
    {
      byte[] buffer = MsgPackSerializer.Serialize(item, settings);
      T ret = MsgPackSerializer.Deserialize<T>(buffer, settings);

      string org = JsonConvert.SerializeObject(item);
      string returned = JsonConvert.SerializeObject(ret);
      Assert.AreEqual(org, returned, $"Not equal, Original - returned:\r\n{org}\r\n{returned}");
      return ret;
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.Never)]
    public void Enums(AddTypeIdOption option)
    {
      WithEnums ret = RoundTrip(new WithEnums()
      {
        Colour = Colour.Blue,
        NullableColour = Colour.Green,
        Tiny = Tiny.B,
        Colours = new[] { Colour.Red, Colour.Blue },
        ColourList = new List<Colour>() { Colour.Green, Colour.Blue }
      }, Settings(option));

      Assert.AreEqual(Colour.Blue, ret.Colour);
      Assert.AreEqual(Colour.Green, ret.NullableColour);
      Assert.AreEqual(Tiny.B, ret.Tiny);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.Never)]
    public void Dictionaries(AddTypeIdOption option)
    {
      WithDictionaries ret = RoundTrip(new WithDictionaries()
      {
        StringToInt = new Dictionary<string, int>() { { "one", 1 }, { "thousand", 1000 }, { "minus", -5 } },
        IntToCat = new Dictionary<int, Cat>() { { 1, new Cat() { Name = "Mia", ClawLengthMilimeters = 2.5f, NotNullable = 3 } }, { 70000, new Cat() { Name = "Tom" } } },
        Nested = new Dictionary<string, Dictionary<string, int>>() { { "a", new Dictionary<string, int>() { { "b", 2 } } } },
        StringToList = new Dictionary<string, List<int>>() { { "list", new List<int>() { 1, 300, 70000 } } },
        Sorted = new SortedDictionary<string, long>() { { "z", 1 }, { "a", long.MaxValue } },
      }, Settings(option));

      Assert.HasCount(3, ret.StringToInt);
      Assert.AreEqual("Tom", ret.IntToCat[70000].Name);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.Never)]
    public void NestedAndConvertedCollections(AddTypeIdOption option)
    {
      MsgPackSettings settings = Settings(option);
      if (option == AddTypeIdOption.Never)
        settings.TypeResolvers = new IMsgPackTypeResolver[] { new PetBySignature() };

      RoundTrip(new WithCollections()
      {
        ListOfArrays = new List<int[]>() { new[] { 1, 2 }, new[] { 300, 70000 }, null },
        Jagged = new[] { new[] { 1 }, new[] { 2, 3 } },
        ListOfLists = new List<List<string>>() { new List<string>() { "a", "b" }, new List<string>() },
        Guids = new List<Guid>() { Guid.NewGuid(), Guid.NewGuid() },
        GuidArray = new[] { Guid.NewGuid() },
        NullableInts = new List<int?>() { 1, null, 70000 },
        Longs = new List<long>() { 1, -1, long.MaxValue },
        SBytes = new sbyte[] { 1, 100, -100 },
        Doubles = new List<double>() { 1, 0.5, 1.1 },
        Set = new HashSet<string>() { "x", "y" },
        Cats = new List<Cat>() { new Cat() { Name = "Mia" }, null },
        Pets = new List<IIPet>() { new Cat() { Name = "Mia", ClawLengthMilimeters = 1 }, new Dog() { Name = "Rex", BarkingDecibels = 3 } },
        Dogs = new ObservableCollection<Dog>() { new Dog() { Name = "Rex" } },
      }, settings);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.Never)]
    public void KeyValuePairArray(AddTypeIdOption option)
    {
      RoundTrip(new WithKeyValuePairs()
      {
        Pairs = new[] { new KeyValuePair<int, string>(1, "one"), new KeyValuePair<int, string>(70000, "many") }
      }, Settings(option));
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)] // Wraps DateTime and decimal in a map with a type id: { "": "DateTime", "@": value }
    [DataRow(AddTypeIdOption.Never)]
    public void Scalars(AddTypeIdOption option)
    {
      RoundTrip(new WithScalars()
      {
        Single = 1.5f,
        Double = 2,
        Decimal = 1.23456789012345678m,
        ULong = ulong.MaxValue,
        Short = -3,
        NullableBool = true,
        NullableLong = 5,
        Date = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local) // DateTime is returned in local time
      }, Settings(option));
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.Never)]
    public void CollectionSubclassWithoutGenericArguments(AddTypeIdOption option)
    {
      WithCustomCollection ret = RoundTrip(new WithCustomCollection()
      {
        Dogs = new DogList() { new Dog() { Name = "Rex" }, new Dog() { Name = "Fido" } }
      }, Settings(option));

      Assert.HasCount(2, ret.Dogs);
    }

    [TestMethod]
    public void DifferentListTypesWithoutTypeIds()
    {
      MsgPackSettings settings = Settings(AddTypeIdOption.Never);

      // Constructors used to be cached by parameter type only (not by the collection type they belong to)
      RoundTrip(new WithStringList() { Items = new List<string>() { "a", "b" } }, settings);
      RoundTrip(new WithObjectList() { Items = new List<object>() { "a", true } }, settings);
      RoundTrip(new WithIntCollection() { Items = new ObservableCollection<int>() { 1, 2 } }, settings);
      RoundTrip(new WithStringList() { Items = new List<string>() { "c" } }, settings);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.Never)]
    public void RootCollections(AddTypeIdOption option)
    {
      MsgPackSettings settings = Settings(option);
      RoundTrip(new List<Cat>() { new Cat() { Name = "Mia" } }, settings);
      RoundTrip(new Dictionary<string, Cat>() { { "mia", new Cat() { Name = "Mia" } } }, settings);
      RoundTrip(new Dictionary<string, int>() { { "one", 1 } }, settings);
      RoundTrip(new[] { 1, 300, 70000 }, settings);
      RoundTrip(new[] { new Cat() { Name = "Mia" } }, settings);
      RoundTrip(new List<int>() { 1, 300 }, settings);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NilAtRoot(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      byte[] nil = MsgPackSerializer.Serialize<Cat>(null, settings);

      Assert.IsNull(MsgPackSerializer.Deserialize(typeof(string), nil, settings));
      Assert.IsNull(MsgPackSerializer.Deserialize<string>(nil, settings));
      Assert.IsNull(MsgPackSerializer.Deserialize<int?>(nil, settings));
      Assert.IsNull(MsgPackSerializer.Deserialize<Cat>(nil, settings));
      Assert.AreEqual(0, MsgPackSerializer.Deserialize<int>(nil, settings));
    }

    [TestMethod]
    public void NilAtRootWithDefaultSettings()
    {
      byte[] nil = MsgPackSerializer.Serialize<Cat>(null);
      Assert.IsNull(MsgPackSerializer.Deserialize<Cat>(nil));
    }

    [TestMethod]
    public void UntypedMapAssignedToObject()
    {
      MsgPackSettings settings = Settings(AddTypeIdOption.Never);
      WithObject org = new WithObject() { Anything = new Dictionary<string, int>() { { "one", 1 }, { "two", 2 } } };

      byte[] buffer = MsgPackSerializer.Serialize(org, settings);
      WithObject ret = MsgPackSerializer.Deserialize<WithObject>(buffer, settings);

      // Without a type id there is no way to know the original type, the raw map should be preserved (not replaced by "new object()")
      KeyValuePair<object, object>[] map = ret.Anything as KeyValuePair<object, object>[];
      Assert.IsNotNull(map, $"Expected the raw map but got {ret.Anything?.GetType().FullName ?? "null"}");
      Assert.HasCount(2, map);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.Never)] // No type ids, so the collection type is derived from the interface
    public void InterfaceTypedCollections(AddTypeIdOption option)
    {
      RoundTrip(new WithAbstractions()
      {
        IntList = new List<int>() { 1, 300 },
        Cats = new List<Cat>() { new Cat() { Name = "Mia" } },
        Strings = new List<string>() { "a" },
        Set = new HashSet<int>() { 5, 6 },
        Dictionary = new Dictionary<string, int>() { { "one", 1 } },
        ReadOnlyDictionary = new Dictionary<int, string>() { { 1, "one" } },
      }, Settings(option));
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    public void BoxedValues(AddTypeIdOption option)
    {
      WithBoxedValues ret = RoundTrip(new WithBoxedValues()
      {
        Guid = Guid.NewGuid(), // { "": "Guid", "@": bytes }
        Enum = Colour.Blue,
        Pets = new List<IIPet>() { new Cat() { Name = "Mia" }, new Dog() { Name = "Rex" } },
        Objects = new object[] { new Dog() { Name = "Rex" }, 1, "two" },
        Number = 3.5d,
      }, Settings(option));

      Assert.IsInstanceOfType<Guid>(ret.Guid);
      Assert.IsInstanceOfType<Colour>(ret.Enum);
      Assert.IsInstanceOfType<List<IIPet>>(ret.Pets);
      Assert.IsInstanceOfType<Dog>(ret.Objects[0]);
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious)]
    [DataRow(AddTypeIdOption.Always)]
    [DataRow(AddTypeIdOption.Never)]
    public void StacksAndQueuesKeepTheirOrder(AddTypeIdOption option)
    {
      Stack<int> stack = new Stack<int>();
      stack.Push(1);
      stack.Push(2);
      stack.Push(3);

      WithStacksAndQueues ret = RoundTrip(new WithStacksAndQueues()
      {
        Stack = stack,
        Queue = new Queue<string>(new[] { "first", "second" }),
        Linked = new LinkedList<int>(new[] { 1, 2, 3 }),
      }, Settings(option));

      Assert.AreEqual(3, ret.Stack.Pop());
      Assert.AreEqual("first", ret.Queue.Dequeue());
    }

    private class PetBySignature : IMsgPackTypeResolver
    {
      public object IdForType(Type type, LsMsgPack.Meta.FullPropertyInfo assignedTo, MsgPackSettings settings) => null;

      public Type Resolve(object typeId, Type assignedTo, LsMsgPack.Meta.FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
      {
        if (assignedTo != typeof(IIPet))
          return null;
        if (properties.ContainsKey("ClawLengthMilimeters"))
          return typeof(Cat);
        if (properties.ContainsKey("BarkingDecibels"))
          return typeof(Dog);
        return null;
      }
    }
  }
}
