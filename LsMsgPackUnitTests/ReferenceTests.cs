using LsMsgPack;
using LsMsgPack.Converters;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// ReferenceHandling.Preserve: an object or collection met again is a reference (extension type 5) holding the path to its first occurrence, read back as the same instance.
  /// </summary>
  public abstract class ReferenceTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    protected ReferenceTests()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(RfOrder)); // type ids by short names
    }

    public class RfCustomer
    {
      public string Name { get; set; }
      public List<string> Tags { get; set; }
    }

    public class RfOrder
    {
      public int Id { get; set; }
      public RfCustomer Customer { get; set; }
      public object Extra { get; set; }
    }

    public class RfNode
    {
      public string Name { get; set; }
      public RfNode Self { get; set; }
      public RfNode Parent { get; set; }
      public List<RfNode> Children { get; set; }
      public object Tag { get; set; }
    }

    public class RfTwoLists
    {
      public List<int> First { get; set; }
      public List<int> Second { get; set; }
      public int[] Numbers { get; set; }
      public int[] SameNumbers { get; set; }
    }

    public class RfKey
    {
      public string Code { get; set; }
    }

    public class RfKeyed
    {
      public RfKey Owner { get; set; }
      public Dictionary<RfKey, int> ByKey { get; set; }
    }

    public class RfImmutable
    {
      public RfImmutable(string name, RfImmutable other)
      {
        Name = name;
        Other = other;
      }

      public string Name { get; }
      public RfImmutable Other { get; }
    }

    public class RfImmutableHolder
    {
      public RfImmutable A { get; set; }
      public RfImmutable B { get; set; }
    }

    [MsgPackConverter(typeof(RfMoneyConverter))]
    public class RfMoney
    {
      public decimal Amount { get; set; }
      public string Currency { get; set; }
    }

    public sealed class RfMoneyConverter : MsgPackConverter<RfMoney, string>
    {
      public override string ToSurrogate(RfMoney value) { return value.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + value.Currency; }

      public override RfMoney FromSurrogate(string surrogate)
      {
        string[] parts = surrogate.Split(' ');
        return new RfMoney() { Amount = decimal.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), Currency = parts[1] };
      }
    }

    public class RfPrices
    {
      public RfMoney Price { get; set; }
      public RfMoney Same { get; set; }
      public string Name { get; set; }
    }

    protected static MsgPackSettings Settings(ObjectLayout layout, bool useSchema, ReferenceHandling handling = ReferenceHandling.Preserve)
    {
      return new MsgPackSettings() { ObjectLayout = layout, UseInexedSchema = useSchema, ReferenceHandling = handling };
    }

    protected static IEnumerable<MsgPackSettings> AllSettings()
    {
      foreach (ObjectLayout layout in new[] { ObjectLayout.Map, ObjectLayout.Array })
        foreach (bool useSchema in new[] { false, true })
          yield return Settings(layout, useSchema);
      yield return new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve, ObjectLayout = ObjectLayout.Array, TrimTrailingNulls = true };
      yield return new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve, ObjectLayout = ObjectLayout.Map, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0] };
      yield return new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve, ObjectLayout = ObjectLayout.Map, UseInexedSchema = true, SchemaVersion = SchemaVersion.Described };
    }

    protected static List<RfOrder> Orders()
    {
      RfCustomer shared = new RfCustomer() { Name = "shared", Tags = new List<string>() { "a" } };
      return new List<RfOrder>()
      {
        new RfOrder() { Id = 1, Customer = shared },
        new RfOrder() { Id = 2, Customer = new RfCustomer() { Name = "other" } },
        new RfOrder() { Id = 3, Customer = new RfCustomer() { Name = "third" } },
        new RfOrder() { Id = 4, Customer = shared, Extra = shared.Tags }
      };
    }

    protected static RfNode Tree()
    {
      RfNode root = new RfNode() { Name = "root", Children = new List<RfNode>() };
      root.Self = root;
      for (int t = 0; t < 3; t++)
      {
        RfNode child = new RfNode() { Name = "c" + t, Parent = root, Tag = root };
        child.Children = new List<RfNode>() { new RfNode() { Name = "leaf" + t, Parent = child, Tag = child } };
        root.Children.Add(child);
      }
      return root;
    }

    /// <summary>
    /// Values with shared objects and collections, cycles, converted values and map keys (also the corpus of CrossLibraryTests with Preserve).
    /// </summary>
    internal static IEnumerable<object[]> SharedValues()
    {
      yield return new object[] { Orders(), typeof(List<RfOrder>) };
      yield return new object[] { Tree(), typeof(RfNode) };
      List<int> list = new List<int>() { 1, 2, 3 };
      int[] numbers = new[] { 4, 5 };
      yield return new object[] { new RfTwoLists() { First = list, Second = list, Numbers = numbers, SameNumbers = numbers }, typeof(RfTwoLists) };
      RfKey key = new RfKey() { Code = "k" };
      yield return new object[] { new RfKeyed() { Owner = key, ByKey = new Dictionary<RfKey, int>() { { key, 1 } } }, typeof(RfKeyed) };
      RfMoney money = new RfMoney() { Amount = 12.5m, Currency = "EUR" };
      yield return new object[] { new RfPrices() { Price = money, Same = money, Name = "p" }, typeof(RfPrices) };
      RfImmutable shared = new RfImmutable("s", null);
      yield return new object[] { new RfImmutableHolder() { A = shared, B = shared }, typeof(RfImmutableHolder) };
      yield return new object[] { new List<object>() { Tree(), Orders() }, typeof(List<object>) };
      Dictionary<string, object> map = new Dictionary<string, object>() { { "a", list } };
      map["b"] = list;
      map["self"] = map;
      yield return new object[] { map, typeof(object) };
      RfCustomer customer = new RfCustomer() { Name = "c", Tags = new List<string>() { "t" } };
      yield return new object[] { new List<RfOrder>() { new RfOrder() { Id = 1, Customer = customer }, new RfOrder() { Id = 2, Extra = customer } }, typeof(List<RfOrder>) }; // a repeat declared as object: the type id wrapper
      Dictionary<string, RfNode> nodes = new Dictionary<string, RfNode>() { { "root", Tree() } };
      nodes["first child"] = nodes["root"].Children[0];
      yield return new object[] { nodes, typeof(Dictionary<string, RfNode>) };
      RfTag tag = new RfTag() { Name = "t" };
      RfSample[] samples = new[] { new RfSample() { A = 1.5f, B = 2 } };
      yield return new object[] { new RfMemberConverted() { First = tag, Second = tag, Samples = samples, SameSamples = samples }, typeof(RfMemberConverted) };
    }

    /// <summary>
    /// The array layout without the schema, Order = [Id, Customer, Extra], Customer = [Name, Tags]: the fourth order's customer is the first one's, the path [0, 1]: d5 05 00 01.
    /// </summary>
    [TestMethod]
    public void SharedObject_PathToTheFirstOccurrence()
    {
      MsgPackSettings settings = Settings(ObjectLayout.Array, false);
      byte[] bytes = Serializer.Serialize(Orders(), settings);
      string hex = BitConverter.ToString(bytes).Replace("-", " ");
      StringAssert.Contains(hex, "93 04 D5 05 00 01", "the fourth order: [4, ref [0, 1], ...]: " + hex);
      StringAssert.Contains(hex, "82 A0 AC 4C 69 73 74 3C 53 74 72 69 6E 67 3E A1 40 C7 03 05 00 01 01", "Extra (object): { \"\": \"List<String>\", \"@\": ref [0, 1, 1] } (the customer's tags), ext 8 of 3 bytes: " + hex);
    }

    [TestMethod]
    public void SharedObject_ReadAsOneInstance()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        List<RfOrder> read = Serializer.Deserialize<List<RfOrder>>(Serializer.Serialize(Orders(), settings), settings);
        Assert.AreEqual(4, read.Count, settings.ToString());
        Assert.AreSame(read[0].Customer, read[3].Customer, settings.ObjectLayout + " " + settings.UseInexedSchema);
        Assert.AreNotSame(read[0].Customer, read[1].Customer);
        Assert.AreEqual("shared", read[3].Customer.Name);
        Assert.AreSame(read[0].Customer.Tags, read[3].Extra, "a list declared as object: the same list");
      }
    }

    /// <summary>
    /// Declared as object, the root (a list with a type id) is converted by LsMsgPack.Core, last value first: the references of the fourth order are resolved before the first order
    /// is read, so its customer and that customer's tags are read from their bytes then, once (a second read of the tags for the customer made a second list).
    /// </summary>
    [TestMethod]
    public void ReferencesBeforeTheirTargets_OneInstance()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        List<RfOrder> read = (List<RfOrder>)Serializer.Deserialize(typeof(object), Serializer.Serialize(Orders(), typeof(object), settings), settings);
        string message = settings.ObjectLayout + " schema=" + settings.UseInexedSchema;
        Assert.AreSame(read[0].Customer, read[3].Customer, message);
        Assert.AreSame(read[0].Customer.Tags, read[3].Extra, message);
      }
    }

    [TestMethod]
    public void Cycles_ReadBack()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        RfNode read = Serializer.Deserialize<RfNode>(Serializer.Serialize(Tree(), settings), settings);
        Assert.AreSame(read, read.Self, settings.ObjectLayout + " " + settings.UseInexedSchema);
        Assert.AreEqual(3, read.Children.Count);
        for (int t = 0; t < 3; t++)
        {
          RfNode child = read.Children[t];
          Assert.AreSame(read, child.Parent);
          Assert.AreSame(read, child.Tag, "declared as object: a reference wrapped with the type id");
          Assert.AreSame(child, child.Children[0].Parent);
          Assert.AreSame(child, child.Children[0].Tag);
          Assert.AreEqual("leaf" + t, child.Children[0].Name);
        }
      }
    }

    public class RfSelfContaining
    {
      public object[] Array { get; set; }
      public List<object> List { get; set; }
      public HashSet<object> Set { get; set; }
      public Dictionary<string, object> Map { get; set; }
      public RfNode Node { get; set; }
    }

    /// <summary>
    /// Collections are registered before their elements (maintainer's decision 2026-11-17), so a cycle back through an array, a list, a set or a dictionary resolves; also a list
    /// whose element refers back to it (declared as object, so wrapped with its type id).
    /// </summary>
    [TestMethod]
    public void Cycles_ThroughCollections()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        string message = settings.ObjectLayout + " schema=" + settings.UseInexedSchema;
        object[] array = new object[2];
        array[0] = "a";
        array[1] = array;
        List<object> list = new List<object>() { 1 };
        list.Add(list);
        HashSet<object> set = new HashSet<object>() { "s" };
        set.Add(set);
        Dictionary<string, object> map = new Dictionary<string, object>() { { "n", 1 } };
        map["self"] = map;
        RfNode node = new RfNode() { Name = "n", Children = new List<RfNode>() };
        node.Children.Add(new RfNode() { Name = "child", Tag = node.Children });
        RfSelfContaining value = new RfSelfContaining() { Array = array, List = list, Set = set, Map = map, Node = node };

        RfSelfContaining read = Serializer.Deserialize<RfSelfContaining>(Serializer.Serialize(value, settings), settings);
        Assert.AreSame(read.Array, read.Array[1], message);
        Assert.AreEqual("a", read.Array[0], message);
        Assert.AreSame(read.List, read.List[1], message);
        Assert.AreEqual(2, read.Set.Count, message);
        Assert.IsTrue(read.Set.Contains(read.Set), message);
        Assert.AreSame(read.Map, read.Map["self"], message);
        Assert.AreSame(read.Node.Children, read.Node.Children[0].Tag, message);

        List<object> root = Serializer.Deserialize<List<object>>(Serializer.Serialize(list, settings), settings);
        Assert.AreSame(root, root[1], message + " (the root)");
      }
    }

    /// <summary>
    /// A collection made of its elements (here by the constructor taking them, a stack) does not exist before them: a cycle back to it is an error, as for an object made by its constructor with values.
    /// </summary>
    [TestMethod]
    public void CollectionMadeOfItsElements_CycleFails()
    {
      Stack<object> stack = new Stack<object>();
      stack.Push(stack);
      MsgPackSettings settings = Settings(ObjectLayout.Map, false);
      byte[] bytes = Serializer.Serialize(new RfStackHolder() { Extra = stack }, settings);
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<RfStackHolder>(bytes, settings));
      StringAssert.Contains(ex.Message, "cycle");
    }

    public class RfStackHolder
    {
      public Stack<object> Extra { get; set; }
    }

    [TestMethod]
    public void SharedCollections()
    {
      List<int> list = new List<int>() { 1, 2, 3 };
      int[] numbers = new[] { 4, 5 };
      RfTwoLists value = new RfTwoLists() { First = list, Second = list, Numbers = numbers, SameNumbers = numbers };
      foreach (MsgPackSettings settings in AllSettings())
      {
        RfTwoLists read = Serializer.Deserialize<RfTwoLists>(Serializer.Serialize(value, settings), settings);
        Assert.AreSame(read.First, read.Second);
        CollectionAssert.AreEqual(list, read.First);
        Assert.AreSame(read.Numbers, read.SameNumbers);
        CollectionAssert.AreEqual(numbers, read.Numbers);
      }
    }

    [TestMethod]
    public void Strings_NotTracked()
    {
      string text = new string('x', 40);
      RfOrder order = new RfOrder() { Customer = new RfCustomer() { Name = text, Tags = new List<string>() { text } } };
      byte[] bytes = Serializer.Serialize(order, Settings(ObjectLayout.Array, false));
      CollectionAssert.AreEqual(Serializer.Serialize(order, Settings(ObjectLayout.Array, false, ReferenceHandling.None)), bytes);
    }

    [TestMethod]
    public void WithoutTheOption_Copies()
    {
      byte[] bytes = Serializer.Serialize(Orders(), Settings(ObjectLayout.Map, true));
      List<RfOrder> read = Serializer.Deserialize<List<RfOrder>>(bytes, Settings(ObjectLayout.Map, true, ReferenceHandling.None));
      Assert.AreNotSame(read[0].Customer, read[3].Customer, "read again: a copy");
      Assert.AreEqual("shared", read[3].Customer.Name);
      CollectionAssert.AreEqual(new List<string>() { "a" }, (System.Collections.ICollection)read[3].Extra);

      // A collection read after the first reference (which made the table of the call) is not registered either: the second reference to it is a copy too
      RfCustomer first = new RfCustomer() { Name = "a" };
      List<string> tags = new List<string>() { "t" };
      List<RfOrder> orders = new List<RfOrder>()
      {
        new RfOrder() { Id = 1, Customer = first },
        new RfOrder() { Id = 2, Customer = first }, // the first reference
        new RfOrder() { Id = 3, Customer = new RfCustomer() { Name = "c", Tags = tags } },
        new RfOrder() { Id = 4, Customer = new RfCustomer() { Name = "d", Tags = tags } }
      };
      List<RfOrder> again = Serializer.Deserialize<List<RfOrder>>(Serializer.Serialize(orders, Settings(ObjectLayout.Map, true)), Settings(ObjectLayout.Map, true, ReferenceHandling.None));
      Assert.AreNotSame(again[2].Customer.Tags, again[3].Customer.Tags, "after the first reference: still a copy");
      CollectionAssert.AreEqual(tags, again[3].Customer.Tags);
    }

    [TestMethod]
    public void WithoutTheOption_CycleFails()
    {
      byte[] bytes = Serializer.Serialize(Tree(), Settings(ObjectLayout.Map, false));
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<RfNode>(bytes, Settings(ObjectLayout.Map, false, ReferenceHandling.None)));
      StringAssert.Contains(ex.Message, "cycle");
    }

    [TestMethod]
    public void MapKeys_WrittenInFull()
    {
      RfKey key = new RfKey() { Code = "k" };
      RfKeyed value = new RfKeyed() { Owner = key, ByKey = new Dictionary<RfKey, int>() { { key, 1 } } };
      MsgPackSettings settings = Settings(ObjectLayout.Map, false);
      byte[] bytes = Serializer.Serialize(value, settings);
      CollectionAssert.AreEqual(Serializer.Serialize(value, Settings(ObjectLayout.Map, false, ReferenceHandling.None)), bytes);
    }

    [TestMethod]
    public void ConstructorWithValues_SharedIsOneInstance()
    {
      RfImmutable shared = new RfImmutable("s", null);
      RfImmutableHolder holder = new RfImmutableHolder() { A = shared, B = shared };
      foreach (MsgPackSettings settings in AllSettings())
      {
        RfImmutableHolder read = Serializer.Deserialize<RfImmutableHolder>(Serializer.Serialize(holder, settings), settings);
        Assert.AreSame(read.A, read.B);
        Assert.AreEqual("s", read.A.Name);
      }
    }

    [TestMethod]
    public void ConstructorWithValues_CycleFails()
    {
      RfImmutable a = new RfImmutable("a", null);
      typeof(RfImmutable).GetField("<Other>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(a, a);
      MsgPackSettings settings = Settings(ObjectLayout.Map, false);
      byte[] bytes = Serializer.Serialize(a, settings);
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<RfImmutable>(bytes, settings));
      StringAssert.Contains(ex.Message, "cycle");
    }

    [TestMethod]
    public void Converted_IdentityFollowsTheValue()
    {
      RfMoney money = new RfMoney() { Amount = 12.5m, Currency = "EUR" };
      RfPrices prices = new RfPrices() { Price = money, Same = money, Name = "p" };
      foreach (MsgPackSettings settings in AllSettings())
      {
        byte[] bytes = Serializer.Serialize(prices, settings);
        RfPrices read = Serializer.Deserialize<RfPrices>(bytes, settings);
        Assert.AreEqual(12.5m, read.Price.Amount);
        Assert.AreSame(read.Price, read.Same, settings.ObjectLayout + " " + settings.UseInexedSchema);
      }
    }

    public struct RfSample
    {
      public float A;
      public int B;
    }

    public class RfTag
    {
      public string Name { get; set; }
    }

    public sealed class RfTagConverter : MsgPackConverter<RfTag, string>
    {
      public override string ToSurrogate(RfTag value) { return value.Name; }
      public override RfTag FromSurrogate(string surrogate) { return new RfTag() { Name = surrogate }; }
    }

    public class RfMemberConverted
    {
      [MsgPackConverter(typeof(RfTagConverter))]
      public RfTag First { get; set; }
      [MsgPackConverter(typeof(RfTagConverter))]
      public RfTag Second { get; set; }
      [MsgPackConverter(typeof(BlitArrayConverter<RfSample>))]
      public RfSample[] Samples { get; set; }
      [MsgPackConverter(typeof(BlitArrayConverter<RfSample>))]
      public RfSample[] SameSamples { get; set; }
    }

    /// <summary>
    /// A converter on a member (also a binary one, as the NativeAOT smoke test's blit array): identity follows the original value there too.
    /// </summary>
    [TestMethod]
    public void MemberConverters_IdentityFollowsTheValue()
    {
      RfTag tag = new RfTag() { Name = "t" };
      RfSample[] samples = new[] { new RfSample() { A = 1.5f, B = 2 } };
      RfMemberConverted value = new RfMemberConverted() { First = tag, Second = tag, Samples = samples, SameSamples = samples };
      foreach (MsgPackSettings settings in AllSettings())
      {
        string message = settings.ObjectLayout + " schema=" + settings.UseInexedSchema;
        RfMemberConverted read = Serializer.Deserialize<RfMemberConverted>(Serializer.Serialize(value, settings), settings);
        Assert.AreEqual("t", read.First.Name, message);
        Assert.AreSame(read.First, read.Second, message);
        Assert.AreEqual(2, read.Samples[0].B, message);
        Assert.AreSame(read.Samples, read.SameSamples, message);
      }
    }

    public class RfNewer
    {
      public string Name { get; set; }
      public RfCustomer Main { get; set; }
      public RfCustomer Backup { get; set; }
    }

    public class RfNewerFirst
    {
      public string Name { get; set; }
      public RfCustomer Backup { get; set; } // only the newer class has it, and it comes first
      public RfCustomer Main { get; set; }
    }

    public class RfOlder
    {
      public string Name { get; set; }
      public RfCustomer Main { get; set; }
      public MsgPackExtensionData Extra { get; set; }
    }

    private static IEnumerable<MsgPackSettings> ByNameSettings()
    {
      yield return Settings(ObjectLayout.Map, false); // version 1 of the schema refuses kept arrays and maps
      yield return new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve, ObjectLayout = ObjectLayout.Map, UseInexedSchema = true, SchemaVersion = SchemaVersion.Described };
      yield return new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve, ObjectLayout = ObjectLayout.Array, UseInexedSchema = true, SchemaVersion = SchemaVersion.Described };
    }

    /// <summary>
    /// Extension data (maintainer's decision, option a): a kept value that is a reference becomes a copy of what it points to, so the older class writes back a value that stands on its own.
    /// </summary>
    [TestMethod]
    public void ExtensionData_KeptReferenceIsACopy()
    {
      RfCustomer main = new RfCustomer() { Name = "m", Tags = new List<string>() { "t" } };
      foreach (MsgPackSettings settings in ByNameSettings())
      {
        byte[] newer = Serializer.Serialize(new RfNewer() { Name = "n", Main = main, Backup = main }, settings);
        RfOlder older = Serializer.Deserialize<RfOlder>(newer, settings);
        Assert.AreEqual("m", older.Main.Name);
        byte[] written = Serializer.Serialize(older, settings);
        RfNewer back = Serializer.Deserialize<RfNewer>(written, settings);
        Assert.AreEqual("m", back.Main.Name, settings.ObjectLayout + " " + settings.UseInexedSchema);
        Assert.IsNotNull(back.Backup, settings.ObjectLayout + " " + settings.UseInexedSchema + " " + BitConverter.ToString(written));
        Assert.AreEqual("m", back.Backup.Name);
        CollectionAssert.AreEqual(new List<string>() { "t" }, back.Backup.Tags);
      }
    }

    /// <summary>
    /// A known member refers to a value only the newer class has (kept): it is read from the bytes at the path, a copy.
    /// </summary>
    [TestMethod]
    public void ExtensionData_KnownMemberRefersIntoKeptValue()
    {
      RfCustomer main = new RfCustomer() { Name = "m" };
      foreach (MsgPackSettings settings in ByNameSettings())
      {
        byte[] newer = Serializer.Serialize(new RfNewerFirst() { Name = "n", Main = main, Backup = main }, settings);
        RfOlder older = Serializer.Deserialize<RfOlder>(newer, settings);
        Assert.AreEqual("m", older.Main?.Name, settings.ObjectLayout + " " + settings.UseInexedSchema + " " + BitConverter.ToString(newer));
        RfNewerFirst back = Serializer.Deserialize<RfNewerFirst>(Serializer.Serialize(older, settings), settings);
        Assert.AreEqual("m", back.Main.Name);
        Assert.AreEqual("m", back.Backup.Name);
      }
    }

    [MsgPackConverter(typeof(RfCodeConverter))]
    public struct RfCode
    {
      public int Value;
    }

    public sealed class RfCodeConverter : MsgPackBinaryConverter<RfCode>
    {
      public RfCodeConverter() : base(5) { }
      public override int GetLength(RfCode value) { return 1; }
      public override void Write(RfCode value, Span<byte> destination) { destination[0] = (byte)value.Value; }
      public override RfCode Read(ReadOnlySpan<byte> source) { return new RfCode() { Value = source[0] }; }
    }

    public class RfCoded
    {
      public RfCode Code { get; set; }
    }

    /// <summary>
    /// Extension type code 5 is refused with ReferenceHandling.Preserve only (maintainer's decision): without it a converter may use it, and reads it as its own.
    /// </summary>
    [TestMethod]
    public void Code5_RefusedOnlyWithPreserve()
    {
      MsgPackSettings without = Settings(ObjectLayout.Map, false, ReferenceHandling.None);
      RfCoded read = Serializer.Deserialize<RfCoded>(Serializer.Serialize(new RfCoded() { Code = new RfCode() { Value = 7 } }, without), without);
      Assert.AreEqual(7, read.Code.Value);

      MsgPackSettings with = Settings(ObjectLayout.Map, false); // the attribute: checked where the converter is used
      InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Serializer.Serialize(new RfCoded() { Code = new RfCode() { Value = 1 } }, with)); // not the default (left out, never converted)
      StringAssert.Contains(ex.Message, "code 5");
    }

    [TestMethod]
    public void PathToNothing_FollowsReadErrors()
    {
      // { "Id": 1, "Customer": ref [9] } (map layout, names)
      byte[] bytes = new byte[] { 0x82, 0xa2, (byte)'I', (byte)'d', 0x01, 0xa8, (byte)'C', (byte)'u', (byte)'s', (byte)'t', (byte)'o', (byte)'m', (byte)'e', (byte)'r', 0xd4, 0x05, 0x09 };
      MsgPackSettings settings = Settings(ObjectLayout.Map, false);
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<RfOrder>(bytes, settings));
      StringAssert.Contains(ex.Message, "points to no value");

      settings.ReadErrors = ReadErrorHandling.ReportAndContinue;
      RfOrder read = Serializer.Deserialize<RfOrder>(bytes, settings);
      Assert.AreEqual(1, read.Id);
      Assert.IsNull(read.Customer);
    }

    [TestMethod]
    public void PathForward_Fails()
    {
      // [ref [1], {"Name": "x"}] read as List<RfCustomer> (names)
      byte[] bytes = new byte[] { 0x92, 0xd4, 0x05, 0x01, 0x81, 0xa4, (byte)'N', (byte)'a', (byte)'m', (byte)'e', 0xa1, (byte)'x' };
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<List<RfCustomer>>(bytes, Settings(ObjectLayout.Map, false)));
      StringAssert.Contains(ex.Message, "before it");
    }
  }

  [TestClass]
  public class DebugReferenceTests : ReferenceTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  /// <summary>
  /// Also: LsMsgPack writes the bytes LsMsgPackDebug writes (the references found by a walk over the finished value: reserved headers patched smaller, elements copied from a rented writer).
  /// </summary>
  [TestClass]
  public class LsReferenceTests : ReferenceTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }

    /// <summary>
    /// The stream writer: each WriteObject is a root of its own (the maintainer's decision), its references point into it and never into another value; read whole or element by element,
    /// the values are the same. In the body of a head the elements are parts of one root value (the payload of Serialize of the list), so a path starts at that root.
    /// </summary>
    [TestMethod]
    public void Streams_EachValueItsOwnRoot()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        string message = settings.ObjectLayout + " schema=" + settings.UseInexedSchema + " trim=" + settings.TrimTrailingNulls;
        LsMsgPackSerializer serializer = ((LsSerializer)Serializers.Ls).For(settings);
        RfCustomer shared = new RfCustomer() { Name = "s" };
        RfOrder first = new RfOrder() { Id = 1, Customer = shared, Extra = shared };
        RfOrder second = new RfOrder() { Id = 2, Customer = shared };

        System.IO.MemoryStream ms = new System.IO.MemoryStream();
        using (LsMsgPack.Streaming.MsgPackStreamWriter writer = new LsMsgPack.Streaming.MsgPackStreamWriter(ms, serializer, LsMsgPack.Streaming.MsgPackStreamSchemas.PerObject, true))
        {
          writer.WriteObject(first);
          writer.WriteObject(second);
        }
        ms.Position = 0;
        using (LsMsgPack.Streaming.MsgPackStreamReader reader = new LsMsgPack.Streaming.MsgPackStreamReader(ms, serializer))
        {
          RfOrder a = reader.ReadObject<RfOrder>();
          RfOrder b = reader.ReadObject<RfOrder>();
          Assert.AreSame(a.Customer, a.Extra, message);
          Assert.AreNotSame(a.Customer, b.Customer, message + ": references don't cross values");
          Assert.AreEqual("s", b.Customer.Name, message);
        }

        ms = new System.IO.MemoryStream();
        using (LsMsgPack.Streaming.MsgPackStreamWriter writer = new LsMsgPack.Streaming.MsgPackStreamWriter(ms, serializer, LsMsgPack.Streaming.MsgPackStreamSchemas.PerObject, true))
        {
          writer.WriteHead<List<RfOrder>>();
          writer.WriteArrayHeader(2);
          writer.WriteObject(first);
          writer.WriteObject(second);
        }
        List<RfOrder> whole = serializer.Deserialize<List<RfOrder>>(ms.ToArray());
        Assert.AreSame(whole[0].Customer, whole[0].Extra, message + " (a head's body read whole)");
        Assert.AreEqual("s", whole[1].Customer.Name, message);
        ms.Position = 0;
        using (LsMsgPack.Streaming.MsgPackStreamReader reader = new LsMsgPack.Streaming.MsgPackStreamReader(ms, serializer))
        {
          reader.ReadHead<List<RfOrder>>();
          reader.Read(); // the array header
          RfOrder a = reader.ReadObject<RfOrder>();
          RfOrder b = reader.ReadObject<RfOrder>();
          Assert.AreSame(a.Customer, a.Extra, message + " (a head's body element by element)");
          Assert.AreEqual("s", b.Customer.Name, message);
        }

        // In a map: a step is the entry's number (the second entry: 1)
        ms = new System.IO.MemoryStream();
        using (LsMsgPack.Streaming.MsgPackStreamWriter writer = new LsMsgPack.Streaming.MsgPackStreamWriter(ms, serializer, LsMsgPack.Streaming.MsgPackStreamSchemas.PerObject, true))
        {
          writer.WriteHead<Dictionary<string, RfOrder>>();
          writer.WriteMapHeader(2);
          writer.Write("second");
          writer.WriteObject(second);
          writer.Write("first");
          writer.WriteObject(first);
        }
        Dictionary<string, RfOrder> byKey = serializer.Deserialize<Dictionary<string, RfOrder>>(ms.ToArray());
        Assert.AreSame(byKey["first"].Customer, byKey["first"].Extra, message + " (a map body read whole)");
        ms.Position = 0;
        using (LsMsgPack.Streaming.MsgPackStreamReader reader = new LsMsgPack.Streaming.MsgPackStreamReader(ms, serializer))
        {
          reader.ReadHead<Dictionary<string, RfOrder>>();
          reader.Read(); // the map header
          reader.Read(); // "second"
          reader.ReadObject<RfOrder>();
          reader.Read(); // "first"
          RfOrder a = reader.ReadObject<RfOrder>();
          Assert.AreSame(a.Customer, a.Extra, message + " (a map body entry by entry)");
        }
      }
    }

    [TestMethod]
    public void SameBytesAsDebug()
    {
      List<string> different = new List<string>();
      foreach (MsgPackSettings settings in AllSettings())
      {
        foreach (object[] value in SharedValues())
        {
          byte[] debug = Serializers.Debug.Serialize(value[0], (Type)value[1], settings);
          byte[] ls = Serializers.Ls.Serialize(value[0], (Type)value[1], settings);
          if (!System.Linq.Enumerable.SequenceEqual(debug, ls))
            different.Add($"{((Type)value[1]).Name} {settings.ObjectLayout} schema={settings.UseInexedSchema} trim={settings.TrimTrailingNulls}:\n  debug {BitConverter.ToString(debug)}\n  ls    {BitConverter.ToString(ls)}");
        }
      }
      Assert.AreEqual(0, different.Count, string.Join("\n", different));
    }
  }

  /// <summary>
  /// With the code of the source generator: it steps aside (it writes and reads nested values itself), the runtime's plans keep the references.
  /// </summary>
  [TestClass]
  public class GenReferenceTests : ReferenceTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
