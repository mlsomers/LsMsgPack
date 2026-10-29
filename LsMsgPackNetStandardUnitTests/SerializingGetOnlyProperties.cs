using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Properties without a setter (<see cref="GetOnlyProperties"/>, kept by FilterNonSettable by default since October 2026; they used to be left out):
  /// computed properties are written and skipped when read, get-only collections are written and filled when read (cleared first).
  /// </summary>
  public abstract class SerializingGetOnlyProperties
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class GopLine
    {
      public decimal Amount { get; set; }
    }

    public class GopCart
    {
      public List<GopLine> Lines { get; } = new List<GopLine>() { new GopLine() { Amount = 2m } };
      public decimal Total { get { return Lines.Sum(l => l.Amount); } }
      public string Customer { get; set; } = "c";
    }

    /// <summary>
    /// The class of a reader without the computed property's code (the same properties in the same order, for the array layout without the schema).
    /// </summary>
    public class GopCartDto
    {
      public List<GopLine> Lines { get; set; }
      public decimal Total { get; set; }
      public string Customer { get; set; }
    }

    public class GopCollections
    {
      public Dictionary<string, int> ByName { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      public HashSet<int> Set { get; } = new HashSet<int>() { 9 };
      public ICollection<string> Names { get; } = new List<string>();
      public IList<int> Fixed { get; } = new int[] { 1, 2 }; // a fixed size: left as it is
      public ReadOnlyCollection<int> ReadOnly { get; } = new ReadOnlyCollection<int>(new[] { 5 }); // read-only: left as it is
      public List<int> Missing { get { return null; } } // nothing to fill
      public int[] Array { get; } = new[] { 1 }; // not a collection that can be filled: computed
    }

    public class GopCollectionsDto
    {
      public Dictionary<string, int> ByName { get; set; }
      public HashSet<int> Set { get; set; }
      public ICollection<string> Names { get; set; }
      public IList<int> Fixed { get; set; }
      public ReadOnlyCollection<int> ReadOnly { get; set; }
      public List<int> Missing { get; set; }
      public int[] Array { get; set; }
    }

    /// <summary>
    /// An immutable class (its constructor takes Name) with a get-only collection and a computed property.
    /// </summary>
    public class GopImmutable
    {
      public GopImmutable(string name) { Name = name; }

      public string Name { get; }
      public List<string> Tags { get; } = new List<string>();
      public int Length { get { return Name?.Length ?? 0; } }
    }

    /// <summary>
    /// Computed properties of types that are never written (a Type, MemberInfo, a delegate, an array of them): left out, the others written.
    /// </summary>
    public class GopTyped<T>
    {
      public int Id { get; set; } = 1;
      public Type ItemType { get { return typeof(T); } }
      public System.Reflection.PropertyInfo Property { get { return typeof(GopTyped<T>).GetProperty(nameof(Id)); } }
      public Func<int> Next { get { return () => Id + 1; } }
      public Type[] Arguments { get { return typeof(GopTyped<T>).GetGenericArguments(); } }
      public int Twice { get { return Id * 2; } }
    }

    public class GopTypedDto
    {
      public int Id { get; set; }
      public int Twice { get; set; }
    }

    public class GopSettableType
    {
      public Type ItemType { get; set; } = typeof(int);
    }

    public struct GopSize
    {
      public int Width { get; set; }
      public int Height { get; set; }
      public int Area { get { return Width * Height; } }
    }

    private static MsgPackSettings Settings(bool schema, ObjectLayout layout, GetOnlyProperties getOnly = GetOnlyProperties.All)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = schema, ObjectLayout = layout };
      if (getOnly != GetOnlyProperties.All)
        settings.StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(getOnly), new FilterIgnoredAttribute(), new FilterStatic() };
      return settings;
    }

    private static IEnumerable<object[]> AllSettings()
    {
      foreach (bool schema in new[] { true, false })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          yield return new object[] { schema, layout };
    }

    private T RoundTrip<T>(T value, MsgPackSettings settings)
    {
      byte[] bytes = Serializer.Serialize(value, settings);
      return Serializer.Deserialize<T>(bytes, settings);
    }

    private static GopCart Cart(params decimal[] amounts)
    {
      GopCart cart = new GopCart() { Customer = "x" };
      cart.Lines.Clear();
      foreach (decimal amount in amounts)
        cart.Lines.Add(new GopLine() { Amount = amount });
      return cart;
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void GetOnlyCollection_ClearedAndFilled(bool schema, ObjectLayout layout)
    {
      GopCart read = RoundTrip(Cart(3m, 4m), Settings(schema, layout));
      Assert.AreEqual("3, 4", string.Join(", ", read.Lines.Select(l => l.Amount)), "the constructor's line is not kept");
      Assert.AreEqual(7m, read.Total);
      Assert.AreEqual("x", read.Customer);

      read = RoundTrip(Cart(), Settings(schema, layout));
      Assert.AreEqual(0, read.Lines.Count, "an empty collection is written (not a default value)");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Computed_WrittenForOtherReaders(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Serializer.Serialize(Cart(3m, 4m), Settings(schema, layout));
      GopCartDto dto = Serializer.Deserialize<GopCartDto>(bytes, Settings(schema, layout));
      Assert.AreEqual(7m, dto.Total);
      Assert.AreEqual("3, 4", string.Join(", ", dto.Lines.Select(l => l.Amount)));
      Assert.AreEqual("x", dto.Customer);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Computed_SkippedWhenRead(bool schema, ObjectLayout layout)
    {
      GopCartDto dto = new GopCartDto() { Lines = new List<GopLine>() { new GopLine() { Amount = 5m } }, Total = 100m, Customer = "d" };
      byte[] bytes = Serializer.Serialize(dto, Settings(schema, layout));
      GopCart read = Serializer.Deserialize<GopCart>(bytes, Settings(schema, layout), out ReadDifferences differences);
      Assert.AreEqual(5m, read.Total, "computed from the lines");
      Assert.AreEqual("d", read.Customer);
      Assert.IsTrue(differences is null || differences.Differences.Count == 0, differences?.GenerateReport());
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void NilCollection_KeepsTheConstructorsOne(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Serializer.Serialize(new GopCartDto() { Lines = null, Customer = "d" }, Settings(schema, layout));
      GopCart read = Serializer.Deserialize<GopCart>(bytes, Settings(schema, layout));
      Assert.AreEqual(2m, read.Total, "a null cannot be set: the collection stays as the constructor made it");
      Assert.AreEqual("d", read.Customer);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Collections_FilledWhereTheyCanBe(bool schema, ObjectLayout layout)
    {
      GopCollectionsDto dto = new GopCollectionsDto()
      {
        ByName = new Dictionary<string, int>() { { "a", 1 }, { "B", 2 } },
        Set = new HashSet<int>() { 3, 4 },
        Names = new List<string>() { "n" },
        Fixed = new List<int>() { 7, 8 },
        ReadOnly = new ReadOnlyCollection<int>(new[] { 6 }),
        Missing = new List<int>() { 1 },
        Array = new[] { 2, 3 },
      };
      byte[] bytes = Serializer.Serialize(dto, Settings(schema, layout));
      GopCollections read = Serializer.Deserialize<GopCollections>(bytes, Settings(schema, layout));
      Assert.AreEqual(2, read.ByName["b"], "filled: the dictionary keeps its comparer");
      Assert.AreEqual(2, read.ByName.Count);
      Assert.AreEqual("3, 4", string.Join(", ", read.Set.OrderBy(i => i)), "ICollection<T> without IList: cleared and filled");
      Assert.AreEqual("n", string.Join(", ", read.Names));
      Assert.AreEqual("1, 2", string.Join(", ", read.Fixed), "a fixed size is left as it is");
      Assert.AreEqual("5", string.Join(", ", read.ReadOnly), "read-only is left as it is");
      Assert.IsNull(read.Missing);
      Assert.AreEqual("1", string.Join(", ", read.Array), "an array is not filled");

      GopCollectionsDto back = Serializer.Deserialize<GopCollectionsDto>(Serializer.Serialize(read, Settings(schema, layout)), Settings(schema, layout));
      Assert.AreEqual("1", string.Join(", ", back.Array), "written");
      Assert.AreEqual("5", string.Join(", ", back.ReadOnly), "written");
      Assert.IsNull(back.Missing, "null is left out");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Immutable_GetOnlyCollectionFilledAfterTheConstructor(bool schema, ObjectLayout layout)
    {
      GopImmutable value = new GopImmutable("name");
      value.Tags.Add("t1");
      value.Tags.Add("t2");
      GopImmutable read = RoundTrip(value, Settings(schema, layout));
      Assert.AreEqual("name", read.Name);
      Assert.AreEqual("t1, t2", string.Join(", ", read.Tags));
      Assert.AreEqual(4, read.Length);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Struct_ComputedSkipped(bool schema, ObjectLayout layout)
    {
      GopSize read = RoundTrip(new GopSize() { Width = 3, Height = 4 }, Settings(schema, layout));
      Assert.AreEqual(3, read.Width);
      Assert.AreEqual(4, read.Height);
      Assert.AreEqual(12, read.Area);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void None_LeavesThemOut(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Serializer.Serialize(Cart(3m, 4m), Settings(schema, layout, GetOnlyProperties.None));
      if (!schema)
        CollectionAssert.AreEqual(Serializer.Serialize(new GopCartOld() { Customer = "x" }, Settings(schema, layout)), bytes, "as before October 2026");
      else
      {
        GopCartDto dto = Serializer.Deserialize<GopCartDto>(bytes, Settings(schema, layout));
        Assert.IsNull(dto.Lines);
        Assert.AreEqual(0m, dto.Total);
        Assert.AreEqual("x", dto.Customer);
      }

      if (!schema && layout == ObjectLayout.Array)
        return; // without names the reader must have the writer's properties

      GopCart read = Serializer.Deserialize<GopCart>(Serializer.Serialize(Cart(3m, 4m), Settings(schema, layout)), Settings(schema, layout, GetOnlyProperties.None));
      Assert.AreEqual(2m, read.Total, "not read either");
      Assert.AreEqual("x", read.Customer);
    }

    public class GopCartOld
    {
      public string Customer { get; set; }
    }

    /// <summary>
    /// The settings with names (the schema or maps): another reader class finds the values by name.
    /// </summary>
    private static IEnumerable<object[]> NamedSettings()
    {
      return AllSettings().Where(s => (bool)s[0] || (ObjectLayout)s[1] == ObjectLayout.Map);
    }

    [TestMethod]
    [DynamicData(nameof(NamedSettings))]
    public void OnlyCollectionsOrOnlyComputed(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Serializer.Serialize(Cart(3m, 4m), Settings(schema, layout, GetOnlyProperties.Collections));
      GopCartDto dto = Serializer.Deserialize<GopCartDto>(bytes, Settings(schema, layout));
      Assert.AreEqual(2, dto.Lines.Count);
      Assert.AreEqual(0m, dto.Total);

      bytes = Serializer.Serialize(Cart(3m, 4m), Settings(schema, layout, GetOnlyProperties.Computed));
      dto = Serializer.Deserialize<GopCartDto>(bytes, Settings(schema, layout));
      Assert.IsNull(dto.Lines);
      Assert.AreEqual(7m, dto.Total);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Computed_TypesNeverWrittenAreLeftOut(bool schema, ObjectLayout layout)
    {
      GopTyped<int> value = new GopTyped<int>() { Id = 3 };
      byte[] bytes = Serializer.Serialize(value, Settings(schema, layout));
      if (!schema)
        CollectionAssert.AreEqual(Serializer.Serialize(new GopTypedDto() { Id = 3, Twice = 6 }, Settings(schema, layout)), bytes, "only Id and Twice");
      GopTypedDto dto = Serializer.Deserialize<GopTypedDto>(bytes, Settings(schema, layout));
      Assert.AreEqual(3, dto.Id);
      Assert.AreEqual(6, dto.Twice);
      Assert.AreEqual(3, Serializer.Deserialize<GopTyped<int>>(bytes, Settings(schema, layout)).Id);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void SettableType_StillRefused(bool schema, ObjectLayout layout)
    {
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new GopSettableType(), Settings(schema, layout)), "a Type with a setter would be read from the data");
    }

    [TestMethod]
    public void FrameworkProperties_StillLeftOut()
    {
      System.Text.Json.JsonElement element = System.Text.Json.JsonDocument.Parse("1").RootElement;
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(element, Settings(true, ObjectLayout.Array)), "refused as before: its get-only properties are the framework's");
    }
  }

  [TestClass]
  public class LsSerializingGetOnlyProperties : SerializingGetOnlyProperties
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingGetOnlyProperties : SerializingGetOnlyProperties
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenSerializingGetOnlyProperties : SerializingGetOnlyProperties
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
