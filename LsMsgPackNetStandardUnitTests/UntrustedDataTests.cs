using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPackUnitTests
{
  public class StaticStateHolder
  {
    public string Name { get; set; }
    public static string Shared { get; set; } = "original";
  }

  /// <summary>
  /// Only used with static filters without <see cref="FilterStatic"/> (the decisions of the static filters are cached per property, the first settings win).
  /// </summary>
  public class StaticStateHolderUnfiltered
  {
    public string Name { get; set; }
    public static string Shared { get; set; } = "original";
  }

  public class DepthNode
  {
    public int Level { get; set; }
    public DepthNode Next { get; set; }

    public static DepthNode Chain(int length)
    {
      DepthNode first = new DepthNode() { Level = 0 };
      DepthNode last = first;
      for (int t = 1; t < length; t++)
        last = last.Next = new DepthNode() { Level = t };
      return first;
    }
  }

  /// <summary>
  /// Static properties are left out (<see cref="FilterStatic"/>) and nesting is limited (<see cref="MsgPackOptions.MaxDepth"/>), see docs/security.md.
  /// </summary>
  public abstract class UntrustedDataTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    private static bool KeepTrack
    {
      get { return typeof(MsgPackSettings).GetProperty("PreservePackages") != null; }
    }

    private static MsgPackSettings MapsWithoutSchema(int maxDepth = 256)
    {
      return new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, MaxDepth = maxDepth };
    }

    private static byte[] Map(params (string Key, string Value)[] entries)
    {
      MsgPackSettings settings = new MsgPackSettings();
      return new MpMap(entries.Select(e => new KeyValuePair<object, object>(new MpString(settings) { Value = e.Key }, new MpString(settings) { Value = e.Value })).ToArray(), settings).ToBytes();
    }

    [TestMethod]
    public void StaticProperties_NotWritten()
    {
      byte[] bytes = Serializer.Serialize(new StaticStateHolder() { Name = "x" }, MapsWithoutSchema());
      KeyValuePair<object, object>[] entries = (KeyValuePair<object, object>[])MsgPackItem.Unpack(bytes).Value;
      CollectionAssert.AreEqual(new object[] { nameof(StaticStateHolder.Name) }, entries.Select(e => e.Key).ToArray());
    }

    [TestMethod]
    public void StaticProperties_NotSet()
    {
      StaticStateHolder.Shared = "original";
      StaticStateHolder read = Serializer.Deserialize<StaticStateHolder>(Map(("Name", "x"), ("Shared", "changed by the data")), MapsWithoutSchema());
      Assert.AreEqual("x", read.Name);
      Assert.AreEqual("original", StaticStateHolder.Shared);
    }

    /// <summary>
    /// Without <see cref="FilterStatic"/> (the behavior before it was added to the defaults).
    /// </summary>
    [TestMethod]
    public void StaticProperties_WithoutTheFilter()
    {
      MsgPackSettings settings = MapsWithoutSchema();
      settings.StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(), new FilterIgnoredAttribute() };

      StaticStateHolderUnfiltered.Shared = "original";
      Serializer.Deserialize<StaticStateHolderUnfiltered>(Map(("Name", "x"), ("Shared", "changed by the data")), settings);
      Assert.AreEqual("changed by the data", StaticStateHolderUnfiltered.Shared);
      StaticStateHolderUnfiltered.Shared = "original";
    }

    /// <summary>
    /// Arrays nested <paramref name="levels"/> deep around a nil.
    /// </summary>
    private static byte[] NestedArrays(int levels)
    {
      byte[] bytes = new byte[levels + 1];
      for (int t = 0; t < levels; t++)
        bytes[t] = 0x91; // fixarray of 1
      bytes[levels] = 0xC0;
      return bytes;
    }

    private void AssertTooDeep(byte[] bytes, MsgPackSettings settings)
    {
      if (KeepTrack && Serializer is LsSerializer) // errors become MpError items instead of exceptions
      {
        Assert.AreEqual("MpError", MsgPackItem.Unpack(new System.IO.MemoryStream(bytes), settings).GetType().Name);
        return;
      }

      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<object>(bytes, settings));
      StringAssert.Contains(ex.Message, "nested deeper than");
      StringAssert.Contains(ex.Message, nameof(MsgPackOptions.MaxDepth));
    }

    /// <summary>
    /// Without the limit LsMsgPack ended in a stack overflow (which ends the process) on data like this: 100 kB of 0x91.
    /// </summary>
    [TestMethod]
    public void DeepData_Refused()
    {
      AssertTooDeep(NestedArrays(100000), MapsWithoutSchema());
    }

    [TestMethod]
    public void DeepData_Limit()
    {
      Assert.IsNotNull(Serializer.Deserialize<object>(NestedArrays(10), MapsWithoutSchema(10)));
      AssertTooDeep(NestedArrays(11), MapsWithoutSchema(10));
    }

    /// <param name="items">The items of the complete payload: 2 with the indexed schema (the schema and the body)</param>
    private void AssertTruncated<T>(byte[] bytes, MsgPackSettings settings, int items, string what)
    {
      if (KeepTrack && Serializer is LsSerializer) // errors become MpError items instead of exceptions (Deserialize can not return them as a T)
      {
        MpRoot read = MsgPackItem.UnpackMultiple(bytes, settings);
        Assert.IsTrue(read.Count < items || read.Any(i => i.GetType().Name == "MpError"), what);
        return;
      }

      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<T>(bytes, settings), what);
    }

    /// <summary>
    /// LsMsgPack used to read the missing bytes as zeros: "a3 41" became "A\0\0" and "cd 01" 256, without an error (LtMsgPack threw).
    /// </summary>
    [TestMethod]
    [DataRow("a3 41")] // fixstr of 3
    [DataRow("d9")] // str8 without its length
    [DataRow("c4 05 01")] // bin8 of 5
    [DataRow("cc")] // uint8
    [DataRow("d0")] // int8
    [DataRow("cd 01")] // uint16
    [DataRow("cb 40 09")] // float64
    [DataRow("d4")] // fixext1 without its type
    [DataRow("c7 01")] // ext8 without its type
    [DataRow("d6 ff 00 00")] // timestamp32
    [DataRow("92 01")] // fixarray of 2
    [DataRow("de 00")] // map16 without all of its length
    public void TruncatedValue_Refused(string hex)
    {
      byte[] bytes = hex.Split(' ').Select(b => Convert.ToByte(b, 16)).ToArray();
      AssertTruncated<object>(bytes, MapsWithoutSchema(), 1, hex);
    }

    private static byte[] Hex(string hex)
    {
      return hex.Split(' ').Select(b => Convert.ToByte(b, 16)).ToArray();
    }

    /// <summary>
    /// A header of a few bytes can claim billions of items or bytes: refused before anything is allocated for them. The explorers (KEEPTRACK,
    /// reading on after errors) allocated and walked all of them: 6 bytes took 40 s and 6 GB, 134 million claimed items ended the process.
    /// </summary>
    [TestMethod]
    [DataRow("dd 7f ff ff ff 01")] // array32
    [DataRow("dd 01 00 00 00 01")]
    [DataRow("df 7f ff ff ff 01 02")] // map32
    [DataRow("df 00 10 00 00")]
    [DataRow("c6 7f ff ff ff 01")] // bin32
    [DataRow("db 7f ff ff ff 41")] // str32
    [DataRow("c9 7f ff ff ff 01 02")] // ext32
    public void ClaimedLength_RefusedWithoutAllocatingIt(string hex)
    {
      byte[] bytes = Hex(hex);
      AssertTruncated<object>(bytes, MapsWithoutSchema(), 1, hex); // once for the caches
      long before = GC.GetAllocatedBytesForCurrentThread();
      AssertTruncated<object>(bytes, MapsWithoutSchema(), 1, hex);
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      Assert.IsLessThan(1 << 20, allocated, $"{hex}: {allocated} bytes allocated");
    }

    /// <summary>
    /// The explorers (KEEPTRACK with <c>ContinueProcessingOnBreakingError</c>) show a truncated array or map up to where the data ends,
    /// the readers refuse it at its header.
    /// </summary>
    [TestMethod]
    [DataRow("dd 7f ff ff ff 01 02", new object[] { 1, 2 })]
    [DataRow("93 01", new object[] { 1 })]
    [DataRow("df 7f ff ff ff 01 02 03", new object[] { 1, 2, 3 })] // the key of the second entry without its value
    public void ClaimedCount_ReadUpToTheEnd(string hex, object[] expected)
    {
      byte[] bytes = Hex(hex);
      if (!(KeepTrack && Serializer is LsSerializer))
      {
        MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<object>(bytes, MapsWithoutSchema()));
        StringAssert.Contains(ex.ToString(), "more than the remaining");
        return;
      }

      MsgPackSettings settings = MapsWithoutSchema();
      typeof(MsgPackSettings).GetProperty("ContinueProcessingOnBreakingError").SetValue(settings, true);
      typeof(MsgPackSettings).GetProperty("PreservePackages").SetValue(settings, true);
      MsgPackItem read = MsgPackItem.Unpack(new System.IO.MemoryStream(bytes), settings);
      Assert.AreEqual("MpError", read.GetType().Name, hex);
      MsgPackItem partial = (MsgPackItem)read.GetType().GetProperty("PartialItem").GetValue(read);
      List<object> values = new List<object>();
      if (partial is MpArray)
        values.AddRange((object[])partial.Value);
      else
        foreach (KeyValuePair<object, object> entry in (KeyValuePair<object, object>[])partial.Value)
          values.AddRange(new[] { entry.Key, entry.Value });
      Assert.IsLessThanOrEqualTo(bytes.Length * 2, values.Count, "slots for what the data can hold, not for the count it claims");
      CollectionAssert.AreEqual(expected, values.Where(v => v != null && !(v is Exception)).Select(v => (object)Convert.ToInt32(v)).ToArray(), hex);
    }

    public class TruncationProbe
    {
      public string Name { get; set; }
      public int Number { get; set; }
      public long Big { get; set; }
      public double Ratio { get; set; }
      public decimal Price { get; set; }
      public DateTime Date { get; set; }
      public Guid Id { get; set; }
      public byte[] Data { get; set; }
      public List<string> Tags { get; set; }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TruncatedPayload_RefusedAtEveryLength(bool schema)
    {
      MsgPackSettings settings = schema ? new MsgPackSettings() : MapsWithoutSchema();
      TruncationProbe value = new TruncationProbe()
      {
        Name = "truncated", Number = 300, Big = long.MaxValue, Ratio = 0.5, Price = 12.34m, Date = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc),
        Id = Guid.NewGuid(), Data = new byte[] { 1, 2, 3 }, Tags = new List<string>() { "a", "b" }
      };
      byte[] bytes = Serializer.Serialize(value, settings);
      Assert.AreEqual(value.Name, Serializer.Deserialize<TruncationProbe>(bytes, settings).Name);

      for (int length = 1; length < bytes.Length; length++)
        AssertTruncated<TruncationProbe>(bytes.Take(length).ToArray(), settings, schema ? 2 : 1, $"{length} of {bytes.Length} bytes");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Cycle_Refused(bool schema)
    {
      DepthNode node = new DepthNode();
      node.Next = node;
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(node, new MsgPackSettings() { UseInexedSchema = schema }));
      StringAssert.Contains(ex.Message, nameof(MsgPackOptions.MaxDepth));
    }

    [TestMethod]
    [DataRow(true, ObjectLayout.Array)]
    [DataRow(false, ObjectLayout.Array)]
    [DataRow(false, ObjectLayout.Map)]
    public void DeepGraph(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = schema, ObjectLayout = layout };
      DepthNode read = Serializer.Deserialize<DepthNode>(Serializer.Serialize(DepthNode.Chain(200), settings), settings);
      int levels = 0;
      for (DepthNode node = read; node != null; node = node.Next)
        Assert.AreEqual(levels++, node.Level);
      Assert.AreEqual(200, levels);

      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(DepthNode.Chain(300), settings));

      settings.MaxDepth = 400;
      Assert.AreEqual(1, Serializer.Deserialize<DepthNode>(Serializer.Serialize(DepthNode.Chain(300), settings), settings).Next.Level);
    }
  }

  [TestClass]
  public class LsUntrustedDataTests : UntrustedDataTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtUntrustedDataTests : UntrustedDataTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
