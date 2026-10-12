using LsMsgPack;
using LsMsgPack.Generated;
using LsMsgPack.Meta;
using LtMsgPack;
using LtMsgPack.Generated;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The code of LtMsgPack's source generator is used (not only equivalent: see the Gen* tests and CrossLibraryTests), and ignored when it does not fit.
  /// </summary>
  [TestClass]
  public partial class GeneratedCodeTests // partial: GcRegisteredContext is nested in it
  {
    public enum GcColor { Red, Green, Blue }

    public class GcPerson
    {
      public string Name { get; set; }
      public int Age { get; set; }
      public GcColor Favorite { get; set; }
      public DateTime? Born { get; set; }
      public List<GcPerson> Friends { get; set; }
    }

    public class GcRegistered
    {
      public string Name { get; set; }
      public int Number { get; set; }
    }

    public class GcStale
    {
      public string Name { get; set; }
      public int Number { get; set; }
    }

    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object Inner(LtMsgPackSerializer serializer)
    {
      return typeof(LtMsgPackSerializer).GetField("_serializer", Internal).GetValue(serializer);
    }

    private static GcPerson Sample()
    {
      return new GcPerson() { Name = "Ann", Age = 41, Favorite = GcColor.Blue, Born = new DateTime(1985, 3, 4, 0, 0, 0, DateTimeKind.Utc), Friends = new List<GcPerson>() { new GcPerson() { Name = "Bob" } } };
    }

    private static void AssertSameAsRuntime(LtMsgPackOptions options, GcPerson value)
    {
      LtMsgPackOptions runtime = options.Clone();
      runtime.Contexts = null;
      runtime.UseRegisteredContexts = false;
      byte[] expected = new LtMsgPackSerializer(runtime).Serialize(value);
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(options);
      byte[] actual = serializer.Serialize(value);
      CollectionAssert.AreEqual(expected, actual);
      GcPerson read = serializer.Deserialize<GcPerson>(actual);
      Assert.AreEqual(value.Name, read.Name);
      Assert.AreEqual(value.Age, read.Age);
      Assert.AreEqual(value.Favorite, read.Favorite);
      Assert.AreEqual(value.Born?.ToUniversalTime(), read.Born?.ToUniversalTime());
      Assert.AreEqual(value.Friends[0].Name, read.Friends[0].Name);
    }

    [TestMethod]
    public void TheTestContext_IsUsable()
    {
      Assert.IsTrue(TestMsgPackContext.Default.IsUsable);
      Assert.AreEqual(GeneratedContract.Current, TestMsgPackContext.Default.ContractVersion);
    }

    [TestMethod]
    public void Lt_UsesTheGeneratedAccessorsAndConstructor()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions() { Contexts = new LtMsgPackContext[] { TestMsgPackContext.Default }, UseRegisteredContexts = false, UseInexedSchema = false });
      byte[] data = serializer.Serialize(Sample());
      serializer.Deserialize<GcPerson>(data);

      object inner = Inner(serializer);
      object plan = inner.GetType().GetMethod("Plan", Internal).Invoke(inner, new object[] { typeof(GcPerson), true });
      foreach (object writer in (Array)plan.GetType().GetField("Props", Internal).GetValue(plan))
      {
        Delegate get = (Delegate)writer.GetType().GetField("_get", Internal).GetValue(writer);
        Assert.AreEqual(typeof(TestMsgPackContext), get.Method.DeclaringType.DeclaringType, "the getter is a lambda of the generated context");
      }

      object readPlan = inner.GetType().GetMethod("GetReadPlan", Internal).Invoke(inner, new object[] { typeof(GcPerson) });
      foreach (object reader in (Array)readPlan.GetType().GetField("Props", Internal).GetValue(readPlan))
      {
        Delegate set = (Delegate)reader.GetType().GetField("_set", Internal).GetValue(reader);
        Assert.AreEqual(typeof(TestMsgPackContext), set.Method.DeclaringType.DeclaringType, "the setter is a lambda of the generated context");
      }
      Delegate create = (Delegate)readPlan.GetType().GetField("_create", Internal).GetValue(readPlan);
      Assert.AreEqual("LtGeneratedType`1", create.Method.DeclaringType.DeclaringType.Name, "the generated constructor call (boxed by the description)");
    }

    [TestMethod]
    public void Lt_WithoutContexts_DoesNotUseGeneratedCode()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions() { UseRegisteredContexts = false, UseInexedSchema = false });
      serializer.Serialize(Sample());
      object inner = Inner(serializer);
      object plan = inner.GetType().GetMethod("Plan", Internal).Invoke(inner, new object[] { typeof(GcPerson), true });
      object writer = ((Array)plan.GetType().GetField("Props", Internal).GetValue(plan)).GetValue(0);
      Delegate get = (Delegate)writer.GetType().GetField("_get", Internal).GetValue(writer);
      Assert.AreEqual(typeof(GcPerson), get.Method.DeclaringType, "bound to the getter itself");
    }

    [TestMethod]
    public void Lt_EveryLayoutAndSchemaMode()
    {
      foreach (bool schema in new[] { false, true })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          AssertSameAsRuntime(new LtMsgPackOptions() { Contexts = new LtMsgPackContext[] { TestMsgPackContext.Default }, UseRegisteredContexts = false, UseInexedSchema = schema, ObjectLayout = layout }, Sample());
    }

    #region Registered for the process (LsMsgPack's accessors and creators)

    [LtMsgPackSerializable(typeof(GcRegistered))]
    public partial class GcRegisteredContext : LtMsgPackContext { }

    [TestMethod]
    public void Ls_UsesRegisteredAccessorsAndConstructor()
    {
      GeneratedTypes.Register(GcRegisteredContext.Default); // only GcRegistered: the process keeps it

      PropertyAccessor accessor = PropertyAccessor.Get(typeof(GcRegistered).GetProperty(nameof(GcRegistered.Name)));
      Delegate getter = (Delegate)typeof(PropertyAccessor).GetField("_getter", Internal).GetValue(accessor);
      Assert.IsNotNull(getter, "set at once (not after 100 calls)");
      Delegate typed = (Delegate)getter.Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0].GetValue(getter.Target);
      Assert.AreEqual(typeof(GcRegisteredContext), typed.Method.DeclaringType.DeclaringType, "the getter of the generated context");
      Assert.IsNotNull(GeneratedTypes.Find(typeof(GcRegistered)).Creator);

      GcRegistered value = new GcRegistered() { Name = "x", Number = 5 };
      GcRegistered read = MsgPackSerializer.Deserialize<GcRegistered>(MsgPackSerializer.Serialize(value));
      Assert.AreEqual("x", read.Name);
      Assert.AreEqual(5, read.Number);

      LtMsgPackSerializer lt = new LtMsgPackSerializer(); // UseRegisteredContexts: on by default
      Assert.AreEqual(5, lt.Deserialize<GcRegistered>(lt.Serialize(value)).Number);
    }

    #endregion

    #region Ignored when it does not fit

    /// <summary>
    /// A context written by hand, as generated code of another version would be.
    /// </summary>
    private sealed class HandWritten : LtMsgPackContext
    {
      private readonly Action<GeneratedTypeSet> _add;

      public HandWritten(int version, Action<GeneratedTypeSet> add) : base(version)
      {
        _add = add;
      }

      protected override void AddTypes(GeneratedTypeSet types)
      {
        _add(types);
      }
    }

    private static void AddPerson(GeneratedTypeSet types)
    {
      types.Add(GeneratedValues.Object<GcPerson>(() => { throw new InvalidOperationException("must not be used"); },
        GeneratedValues.Property<GcPerson, string>("Name", o => { throw new InvalidOperationException("must not be used"); }, null)));
    }

    [TestMethod]
    [DataRow(GeneratedContract.MinSupported - 1)]
    [DataRow(GeneratedContract.Current + 1)]
    public void Version_OutsideTheSupportedRange_IsIgnored(int version)
    {
      HandWritten context = new HandWritten(version, AddPerson);
      Assert.IsFalse(context.IsUsable);
      AssertSameAsRuntime(new LtMsgPackOptions() { Contexts = new LtMsgPackContext[] { context }, UseRegisteredContexts = false }, Sample());
    }

    [TestMethod]
    public void MissingMembersOfTheRuntime_AreIgnored()
    {
      HandWritten context = new HandWritten(GeneratedContract.Current, types => { throw new MissingMethodException("GeneratedValues", "Future"); });
      Assert.IsFalse(context.IsUsable);
      AssertSameAsRuntime(new LtMsgPackOptions() { Contexts = new LtMsgPackContext[] { context }, UseRegisteredContexts = false }, Sample());
    }

    [TestMethod]
    public void StaleModel_PropertiesThatChanged_UseReflection()
    {
      bool numberGetter = false;
      HandWritten context = new HandWritten(GeneratedContract.Current, types => types.Add(GeneratedValues.Object<GcStale>(() => new GcStale(),
        GeneratedValues.Property<GcStale, long>("Name", o => { throw new InvalidOperationException("the type changed: must not be used"); }, null), // generated when Name was a long
        GeneratedValues.Property<GcStale, int>("Number", o => { numberGetter = true; return o.Number; }, (o, v) => o.Number = v),
        GeneratedValues.Property<GcStale, int>("Removed", o => { throw new InvalidOperationException("gone"); }, null))));
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions() { Contexts = new LtMsgPackContext[] { context }, UseRegisteredContexts = false });
      GcStale read = serializer.Deserialize<GcStale>(serializer.Serialize(new GcStale() { Name = "n", Number = 3 }));
      Assert.AreEqual("n", read.Name);
      Assert.AreEqual(3, read.Number);
      Assert.IsTrue(numberGetter, "the property that did not change uses the generated getter");
    }

    #endregion
  }
}
