using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LtMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// FilterShouldSerialize (the ShouldSerialize{Name}() methods, as Json.NET calls them) and dynamic filters that get the owner (IMsgPackPropertyIncludeWithOwner).
  /// </summary>
  public abstract class ShouldSerializeTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class SsConditional
    {
      public string Name { get; set; }
      public int Count { get; set; }
      public string Always { get; set; }

      public bool ShouldSerializeName() { return Count > 0; }
    }

    public class SsBase
    {
      public string Note { get; set; }
      public string Plain { get; set; }

      public virtual bool ShouldSerializeNote() { return true; }
    }

    public class SsDerived : SsBase
    {
      public bool HideNote { get; set; }

      public override bool ShouldSerializeNote() { return !HideNote; }

      // Json.NET looks on the type declaring the property: a method of a derived class for a base property is not called
      public bool ShouldSerializePlain() { return false; }
    }

    public class SsNotMethods
    {
      public string A { get; set; }
      public string B { get; set; }
      public string C { get; set; }
      public string D { get; set; }

      private bool ShouldSerializeA() { return false; } // not public
      public bool ShouldSerializeB(int x) { return false; } // takes a parameter
      public int ShouldSerializeC() { return 0; } // does not return bool
      public static bool ShouldSerializeD() { return false; } // static: Json.NET calls it too
    }

    public class SsHidingBase
    {
      public string R { get; set; }

      public bool ShouldSerializeR() { return true; }
    }

    public class SsHiding : SsHidingBase
    {
      public new bool ShouldSerializeR() { return false; } // Json.NET calls the one of the declaring type
    }

    public struct SsPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
      public string Label { get; set; }

      public bool ShouldSerializeLabel() { return X != 0; }
    }

    public class SsPointHolder
    {
      public SsPoint Point { get; set; }
    }

    // The cycle of the report: a group's discounts point back to the group
    public class SsGroup
    {
      public int Id { get; set; }
      public string Name { get; set; }
      public List<SsGroupDiscount> Discounts { get; set; }
    }

    public class SsGroupDiscount
    {
      public decimal Percentage { get; set; }
      public SsGroup Group { get; set; }

      public bool ShouldSerializeGroup() { return false; }
    }

    /// <summary>
    /// Records the owners it is asked with, leaves out <see cref="SsConditional.Always"/> when the owner's Count is 2.
    /// </summary>
    private sealed class OwnerFilter : IMsgPackPropertyIncludeWithOwner
    {
      internal readonly List<object> Owners = new List<object>();

      public bool IncludeProperty(FullPropertyInfo propertyInfo, object value)
      {
        throw new InvalidOperationException("The serializers pass the owner");
      }

      public bool IncludeProperty(FullPropertyInfo propertyInfo, object owner, object value)
      {
        lock (Owners)
          Owners.Add(owner);
        return !(propertyInfo.Name == nameof(SsConditional.Always) && owner is SsConditional c && c.Count == 2);
      }
    }

    private static MsgPackSettings Settings(ObjectLayout layout, bool useSchema, params IMsgPackPropertyIncludeDynamically[] filters)
    {
      return new MsgPackSettings() { ObjectLayout = layout, UseInexedSchema = useSchema, DynamicFilters = filters };
    }

    private static readonly IMsgPackPropertyIncludeDynamically[] ShouldSerializeOnly = new IMsgPackPropertyIncludeDynamically[] { new FilterShouldSerialize() };
    private static readonly IMsgPackPropertyIncludeDynamically[] WithDefaults = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues(), new FilterShouldSerialize() };

    private static string[] Keys(byte[] bytes)
    {
      MpMap map = (MpMap)MsgPackItem.Unpack(bytes);
      return ((KeyValuePair<object, object>[])map.Value).Select(kv => (string)kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    private static string[] JsonNetKeys(object value)
    {
      JObject json = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(value));
      return json.Properties().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    private void AssertKeysLikeJsonNet(object value)
    {
      byte[] bytes = Serializer.Serialize(value, value.GetType(), Settings(ObjectLayout.Map, false, ShouldSerializeOnly));
      CollectionAssert.AreEqual(JsonNetKeys(value), Keys(bytes), value.GetType().Name + ": " + string.Join(", ", Keys(bytes)));
    }

    [TestMethod]
    public void LeavesOutLikeJsonNet()
    {
      AssertKeysLikeJsonNet(new SsConditional() { Name = "n", Count = 0, Always = "a" });
      AssertKeysLikeJsonNet(new SsConditional() { Name = "n", Count = 1, Always = "a" });
      AssertKeysLikeJsonNet(new SsDerived() { Note = "x", Plain = "p", HideNote = true });
      AssertKeysLikeJsonNet(new SsDerived() { Note = "x", Plain = "p", HideNote = false });
      AssertKeysLikeJsonNet(new SsBase() { Note = "x", Plain = "p" });
      AssertKeysLikeJsonNet(new SsNotMethods() { A = "a", B = "b", C = "c", D = "d" });
      AssertKeysLikeJsonNet(new SsHiding() { R = "r" });
      AssertKeysLikeJsonNet(new SsPoint() { X = 0, Y = 1, Label = "l" });
      AssertKeysLikeJsonNet(new SsPoint() { X = 1, Y = 1, Label = "l" });
    }

    [TestMethod]
    public void WithoutTheFilterTheMethodsAreNotCalled()
    {
      SsConditional value = new SsConditional() { Name = "n", Count = 0, Always = "a" };
      byte[] bytes = Serializer.Serialize(value, Settings(ObjectLayout.Map, false));
      CollectionAssert.AreEqual(new[] { "Always", "Count", "Name" }, Keys(bytes));
    }

    [TestMethod]
    [DataRow(ObjectLayout.Map, false)]
    [DataRow(ObjectLayout.Map, true)]
    [DataRow(ObjectLayout.Array, false)]
    [DataRow(ObjectLayout.Array, true)]
    public void RoundTrip(ObjectLayout layout, bool useSchema)
    {
      foreach (IMsgPackPropertyIncludeDynamically[] filters in new[] { ShouldSerializeOnly, WithDefaults })
      {
        MsgPackSettings settings = Settings(layout, useSchema, filters);
        // Several objects: the compiled writers (after CompileWritersAfter objects) and the plans see more than one
        List<SsConditional> values = new List<SsConditional>();
        for (int t = 0; t < 30; t++)
          values.Add(new SsConditional() { Name = "n" + t, Count = t % 3, Always = "a" });

        List<SsConditional> read = Serializer.Deserialize<List<SsConditional>>(Serializer.Serialize(values, settings), settings);
        for (int t = 0; t < values.Count; t++)
        {
          Assert.AreEqual(values[t].Count > 0 ? values[t].Name : null, read[t].Name, $"{layout}, schema {useSchema}, {t}");
          Assert.AreEqual("a", read[t].Always);
          Assert.AreEqual(values[t].Count, read[t].Count);
        }

        SsPointHolder holder = new SsPointHolder() { Point = new SsPoint() { X = 0, Y = 2, Label = "l" } };
        SsPointHolder readHolder = Serializer.Deserialize<SsPointHolder>(Serializer.Serialize(holder, settings), settings);
        Assert.IsNull(readHolder.Point.Label);
        Assert.AreEqual(2, readHolder.Point.Y);
      }
    }

    /// <summary>
    /// The loop of the report (group, its discounts, the group again) is broken by ShouldSerializeGroup; without the filter it is too deep.
    /// </summary>
    [TestMethod]
    public void ShouldSerializeBreaksACycle()
    {
      SsGroup group = new SsGroup() { Id = 1, Name = "g", Discounts = new List<SsGroupDiscount>() };
      group.Discounts.Add(new SsGroupDiscount() { Percentage = 2.5m, Group = group });
      group.Discounts.Add(new SsGroupDiscount() { Percentage = 5m, Group = group });

      foreach (ObjectLayout layout in new[] { ObjectLayout.Map, ObjectLayout.Array })
      {
        MsgPackSettings settings = Settings(layout, true, WithDefaults);
        SsGroup read = Serializer.Deserialize<SsGroup>(Serializer.Serialize(group, settings), settings);
        Assert.AreEqual(2, read.Discounts.Count);
        Assert.AreEqual(5m, read.Discounts[1].Percentage);
        Assert.IsNull(read.Discounts[0].Group);

        Assert.Throws<MsgPackException>(() => Serializer.Serialize(group, Settings(layout, true, new FilterDefaultValues())));
      }
    }

    [TestMethod]
    public void OwnerFiltersGetTheOwner()
    {
      OwnerFilter filter = new OwnerFilter();
      MsgPackSettings settings = Settings(ObjectLayout.Map, false, filter);
      SsConditional one = new SsConditional() { Name = "n", Count = 1, Always = "a" };
      SsConditional two = new SsConditional() { Name = "n", Count = 2, Always = "a" };
      CollectionAssert.AreEqual(new[] { "Always", "Count", "Name" }, Keys(Serializer.Serialize(one, settings)));
      CollectionAssert.AreEqual(new[] { "Count", "Name" }, Keys(Serializer.Serialize(two, settings)));
      Assert.IsTrue(filter.Owners.Count > 0 && filter.Owners.All(o => ReferenceEquals(o, one) || ReferenceEquals(o, two)));

      OwnerFilter structFilter = new OwnerFilter();
      Serializer.Serialize(new SsPointHolder() { Point = new SsPoint() { X = 3 } }, Settings(ObjectLayout.Map, false, structFilter));
      Assert.IsTrue(structFilter.Owners.Any(o => o is SsPoint p && p.X == 3), "the struct, boxed");
    }

    [TestMethod]
    [DataRow(ObjectLayout.Map, false)]
    [DataRow(ObjectLayout.Map, true)]
    [DataRow(ObjectLayout.Array, false)]
    [DataRow(ObjectLayout.Array, true)]
    public void SameBytesAsLsMsgPack(ObjectLayout layout, bool useSchema)
    {
      foreach (IMsgPackPropertyIncludeDynamically[] filters in new[] { ShouldSerializeOnly, WithDefaults })
      {
        MsgPackSettings settings = Settings(layout, useSchema, filters);
        object[] values =
        {
          new SsConditional() { Name = "n", Count = 0, Always = "a" },
          new SsConditional() { Name = "n", Count = 4 },
          new SsDerived() { Note = "x", Plain = "p", HideNote = true },
          new SsPointHolder() { Point = new SsPoint() { X = 0, Y = 1, Label = "l" } },
          new SsPointHolder() { Point = new SsPoint() { X = 1, Y = 0, Label = "l" } },
        };
        foreach (object value in values)
          CollectionAssert.AreEqual(Serializers.Ls.Serialize(value, value.GetType(), settings), Serializer.Serialize(value, value.GetType(), settings), $"{value.GetType().Name}, {layout}, schema {useSchema}");
      }
    }
  }

  [TestClass]
  public class LsShouldSerializeTests : ShouldSerializeTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtShouldSerializeTests : ShouldSerializeTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }

    /// <summary>
    /// The compiled writers (compiled with the plan) leave the property with the method to the runtime's writer.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void CompiledWriters(ObjectLayout layout)
    {
      LtMsgPackSerializer compiled = new LtMsgPackSerializer(new LtMsgPackOptions()
      {
        ObjectLayout = layout,
        DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues(), new FilterShouldSerialize() },
        CompileWritersAfter = 0,
        UseRegisteredContexts = false
      });
      SsConditional hidden = new SsConditional() { Name = "n", Count = 0, Always = "a" };
      SsConditional shown = new SsConditional() { Name = "n", Count = 1, Always = "a" };
      Assert.IsNull(compiled.Deserialize<SsConditional>(compiled.Serialize(hidden)).Name);
      Assert.AreEqual("n", compiled.Deserialize<SsConditional>(compiled.Serialize(shown)).Name);

      MsgPackSettings settings = new MsgPackSettings() { ObjectLayout = layout, DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues(), new FilterShouldSerialize() } };
      CollectionAssert.AreEqual(Serializers.Ls.Serialize(hidden, settings), compiled.Serialize(hidden));
    }
  }

  [TestClass]
  public class GenShouldSerializeTests : ShouldSerializeTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
