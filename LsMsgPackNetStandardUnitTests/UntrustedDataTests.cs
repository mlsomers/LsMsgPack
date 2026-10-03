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
