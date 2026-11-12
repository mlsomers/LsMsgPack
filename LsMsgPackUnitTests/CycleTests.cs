using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// MsgPackOptions.IgnoreCycles: a reference to an object that is being written is nil (as System.Text.Json's ReferenceHandler.IgnoreCycles), the same bytes in both serializers.
  /// </summary>
  public abstract class CycleTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class CyGroup
    {
      public int Id { get; set; }
      public string Name { get; set; }
      public List<CyDiscount> Discounts { get; set; }
    }

    public class CyDiscount
    {
      public decimal Percentage { get; set; }
      public CyGroup Group { get; set; }
    }

    public class CyNode
    {
      public string Name { get; set; }
      public CyNode Self { get; set; }
      public CyNode Parent { get; set; }
      public List<CyNode> Children { get; set; }
      public object Tag { get; set; } // declared as object: written boxed, with a type id
    }

    public abstract class CyAnimal
    {
      public string Name { get; set; }
    }

    public class CyDog : CyAnimal
    {
      public CyPerson Owner { get; set; } // only the derived class refers back
    }

    public class CyPerson
    {
      public string Name { get; set; }
      public CyAnimal Pet { get; set; }
    }

    public interface ICyLink
    {
      string Name { get; set; }
    }

    public class CyLink : ICyLink
    {
      public string Name { get; set; }
      public ICyLink Next { get; set; }
    }

    // Only numbers and strings: LsMsgPack does not track it (nothing below it can be it), a derived class referring back is tracked through the boxed path
    public class CyPlain
    {
      public int X { get; set; }
      public string Label { get; set; }
    }

    public class CyPlainBack : CyPlain
    {
      public CyPlainHolder Back { get; set; }
    }

    public class CyPlainHolder
    {
      public string Name { get; set; }
      public CyPlain Item { get; set; }
      public List<CyPlain> Items { get; set; }
    }

    public class CyThrowing
    {
      public string Name { get; set; }
      public CyThrowing Next { get; set; }
      public int Fails { get { throw new InvalidOperationException("getter"); } set { } }
    }

    protected static MsgPackSettings Settings(ObjectLayout layout, bool useSchema, bool ignoreCycles = true)
    {
      return new MsgPackSettings() { ObjectLayout = layout, UseInexedSchema = useSchema, IgnoreCycles = ignoreCycles };
    }

    private static IEnumerable<MsgPackSettings> AllSettings()
    {
      foreach (ObjectLayout layout in new[] { ObjectLayout.Map, ObjectLayout.Array })
        foreach (bool useSchema in new[] { false, true })
          yield return Settings(layout, useSchema);
      yield return new MsgPackSettings() { IgnoreCycles = true, ObjectLayout = ObjectLayout.Array, TrimTrailingNulls = true };
      yield return new MsgPackSettings() { IgnoreCycles = true, ObjectLayout = ObjectLayout.Map, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0] };
    }

    protected static CyGroup Group()
    {
      CyGroup group = new CyGroup() { Id = 1, Name = "g", Discounts = new List<CyDiscount>() };
      group.Discounts.Add(new CyDiscount() { Percentage = 2.5m, Group = group });
      group.Discounts.Add(new CyDiscount() { Percentage = 5m, Group = group });
      return group;
    }

    protected static CyNode Tree()
    {
      CyNode root = new CyNode() { Name = "root", Children = new List<CyNode>() };
      root.Self = root;
      for (int t = 0; t < 3; t++)
      {
        CyNode child = new CyNode() { Name = "c" + t, Parent = root, Tag = root };
        child.Children = new List<CyNode>() { new CyNode() { Name = "leaf" + t, Parent = child, Tag = child } };
        root.Children.Add(child);
      }
      return root;
    }

    [TestMethod]
    public void BackReferencesAreNil()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        CyGroup read = Serializer.Deserialize<CyGroup>(Serializer.Serialize(Group(), settings), settings);
        Assert.AreEqual("g", read.Name);
        Assert.AreEqual(2, read.Discounts.Count);
        Assert.AreEqual(5m, read.Discounts[1].Percentage);
        Assert.IsNull(read.Discounts[0].Group);
        Assert.IsNull(read.Discounts[1].Group);
      }
    }

    [TestMethod]
    public void TreeWithParentsSelfAndObjectReferences()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        CyNode read = Serializer.Deserialize<CyNode>(Serializer.Serialize(Tree(), settings), settings);
        Assert.IsNull(read.Self);
        Assert.AreEqual(3, read.Children.Count);
        Assert.AreEqual("c2", read.Children[2].Name);
        Assert.IsNull(read.Children[2].Parent);
        Assert.IsNull(read.Children[2].Tag, "declared as object");
        Assert.AreEqual("leaf2", read.Children[2].Children[0].Name);
        Assert.IsNull(read.Children[2].Children[0].Parent);
        Assert.IsNull(read.Children[2].Children[0].Tag);
      }
    }

    /// <summary>
    /// Only the objects being written count: one object referenced twice side by side is written twice.
    /// </summary>
    [TestMethod]
    public void SharedReferencesAreWrittenEachTime()
    {
      CyNode shared = new CyNode() { Name = "shared" };
      CyNode root = new CyNode() { Name = "root", Self = shared, Parent = shared, Children = new List<CyNode>() { shared, shared } };
      foreach (MsgPackSettings settings in AllSettings())
      {
        CyNode read = Serializer.Deserialize<CyNode>(Serializer.Serialize(root, settings), settings);
        Assert.AreEqual("shared", read.Self.Name);
        Assert.AreEqual("shared", read.Parent.Name);
        Assert.AreEqual("shared", read.Children[0].Name);
        Assert.AreEqual("shared", read.Children[1].Name);
      }
    }

    /// <summary>
    /// The back reference is only in a derived class (declared as the abstract base), or behind an interface: written boxed with a type id.
    /// </summary>
    [TestMethod]
    public void CyclesThroughDerivedClassesAndInterfaces()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        CyPerson person = new CyPerson() { Name = "p" };
        person.Pet = new CyDog() { Name = "d", Owner = person };
        CyPerson read = Serializer.Deserialize<CyPerson>(Serializer.Serialize(person, settings), settings);
        Assert.AreEqual("d", read.Pet.Name);
        Assert.IsNull(((CyDog)read.Pet).Owner);

        CyLink first = new CyLink() { Name = "1" };
        first.Next = new CyLink() { Name = "2", Next = first };
        CyLink link = Serializer.Deserialize<CyLink>(Serializer.Serialize(first, settings), settings);
        Assert.AreEqual("2", link.Next.Name);
        Assert.IsNull(((CyLink)link.Next).Next);
      }
    }

    [TestMethod]
    public void BackReferenceInADerivedClassOfAClassWithoutObjects()
    {
      CyPlainHolder holder = new CyPlainHolder() { Name = "h" };
      holder.Item = new CyPlainBack() { X = 1, Label = "i", Back = holder };
      holder.Items = new List<CyPlain>() { new CyPlain() { X = 2 }, new CyPlainBack() { X = 3, Back = holder }, holder.Item };
      foreach (MsgPackSettings settings in AllSettings())
      {
        byte[] bytes = Serializer.Serialize(holder, settings);
        CollectionAssert.AreEqual(Serializers.Debug.Serialize(holder, settings), bytes, $"{settings.ObjectLayout}, trimmed {settings.TrimTrailingNulls}, schema {settings.UseInexedSchema}");
        CyPlainHolder read = Serializer.Deserialize<CyPlainHolder>(bytes, settings);
        Assert.AreEqual(1, read.Item.X);
        Assert.IsNull(((CyPlainBack)read.Item).Back);
        Assert.AreEqual(2, read.Items[0].X);
        Assert.IsNull(((CyPlainBack)read.Items[1]).Back);
        Assert.IsNull(((CyPlainBack)read.Items[2]).Back);
      }
    }

    [TestMethod]
    public void AnonymousTypes()
    {
      CyGroup group = Group();
      var value = new { Title = "t", Group = group, Again = group.Discounts[0] };
      foreach (MsgPackSettings settings in AllSettings())
        CollectionAssert.AreEqual(Serializers.Debug.Serialize(value, settings), Serializer.Serialize(value, settings));
    }

    [TestMethod]
    public void WithoutTheOptionTooDeep()
    {
      foreach (ObjectLayout layout in new[] { ObjectLayout.Map, ObjectLayout.Array })
        Assert.Throws<MsgPackException>(() => Serializer.Serialize(Group(), Settings(layout, true, ignoreCycles: false)));
    }

    /// <summary>
    /// A call that failed half way leaves no objects on the path of the next one.
    /// </summary>
    [TestMethod]
    public void AfterAnException()
    {
      MsgPackSettings settings = Settings(ObjectLayout.Map, false);
      CyThrowing throwing = new CyThrowing() { Name = "t" };
      throwing.Next = new CyThrowing() { Name = "n", Next = throwing };
      Assert.Throws<InvalidOperationException>(() => Serializer.Serialize(throwing, settings));

      CyGroup read = Serializer.Deserialize<CyGroup>(Serializer.Serialize(Group(), settings), settings);
      Assert.AreEqual(2, read.Discounts.Count);
      Assert.AreEqual(2.5m, read.Discounts[0].Percentage);
    }

    [TestMethod]
    public void SameBytesAsLsMsgPack()
    {
      List<object> values = new List<object>() { Group(), Tree(), new List<CyGroup>() { Group(), Group() }, new Dictionary<string, CyNode>() { { "a", Tree() } } };
      CyPerson person = new CyPerson() { Name = "p" };
      person.Pet = new CyDog() { Name = "d", Owner = person };
      values.Add(person);
      foreach (MsgPackSettings settings in AllSettings())
        foreach (object value in values)
          CollectionAssert.AreEqual(Serializers.Debug.Serialize(value, value.GetType(), settings), Serializer.Serialize(value, value.GetType(), settings), $"{value.GetType().Name}, {settings.ObjectLayout}, trimmed {settings.TrimTrailingNulls}, schema {settings.UseInexedSchema}");
    }

    [TestMethod]
    public void WithShouldSerialize()
    {
      MsgPackSettings settings = Settings(ObjectLayout.Map, true);
      settings.DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues(), new FilterShouldSerialize() };
      CollectionAssert.AreEqual(Serializers.Debug.Serialize(Tree(), settings), Serializer.Serialize(Tree(), settings));
    }
  }

  [TestClass]
  public class DebugCycleTests : CycleTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }

    /// <summary>
    /// A serialization inside another (here by a getter, e.g. a custom extension) does not see the objects of the outer one: its root is not nil.
    /// </summary>
    [TestMethod]
    public void NestedCallHasItsOwnPath()
    {
      CyNesting nesting = new CyNesting() { Name = "n" };
      CyNesting.Settings = Settings(ObjectLayout.Map, false);
      MsgPackSerializer.Serialize(nesting, CyNesting.Settings);
      Assert.AreNotEqual((byte)0xC0, CyNesting.Written[0], "the root of the nested call");
      Assert.AreEqual("n", MsgPackSerializer.Deserialize<CyNesting>(CyNesting.Written, CyNesting.Settings).Name);
    }

    public class CyNesting
    {
      internal static MsgPackSettings Settings;
      internal static byte[] Written;
      [ThreadStatic] private static bool _inside;

      public string Name { get; set; }

      public int Probe
      {
        get
        {
          if (!_inside)
          {
            _inside = true;
            Written = MsgPackSerializer.Serialize(this, Settings); // during the outer call, which has this object on its path
            _inside = false;
          }
          return 1;
        }
        set { }
      }
    }
  }

  [TestClass]
  public class LsCycleTests : CycleTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }

    /// <summary>
    /// The compiled writers (compiled with the plan) write nested objects through the handlers, which track them.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void CompiledWriters(ObjectLayout layout)
    {
      LsMsgPackSerializer compiled = new LsMsgPackSerializer(new LsMsgPackOptions() { ObjectLayout = layout, IgnoreCycles = true, CompileWritersAfter = 0, UseRegisteredContexts = false });
      for (int t = 0; t < 3; t++)
        CollectionAssert.AreEqual(Serializers.Debug.Serialize(Tree(), new MsgPackSettings() { ObjectLayout = layout, IgnoreCycles = true }), compiled.Serialize(Tree()));
    }
  }

  [TestClass]
  public class GenCycleTests : CycleTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
