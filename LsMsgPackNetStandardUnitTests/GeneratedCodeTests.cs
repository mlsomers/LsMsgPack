using LsMsgPack;
using LsMsgPack.Generated;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using LtMsgPack;
using LtMsgPack.Generated;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
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
      Assert.AreEqual(typeof(TestMsgPackContext), create.Method.DeclaringType.DeclaringType, "the generated constructor call itself");
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

    #region The generated writers and readers (tier 2)

    public enum GcSmall : byte { None, One, Two = 200 }
    public enum GcSigned : sbyte { Minus = -3, Zero, Plus }
    public enum GcLong : long { Zero, Big = 5000000000 }
    public enum GcUnsigned : ulong { Zero, Huge = ulong.MaxValue }

    public class GcBase
    {
      public string BaseName { get; set; }
      public virtual int Overridden { get; set; }
    }

    /// <summary>
    /// Every kind the generated code writes and reads itself, Nullable ones, and what it leaves to the runtime.
    /// </summary>
    public class GcAllKinds : GcBase
    {
      public string Text { get; set; }
      public bool Flag { get; set; }
      public sbyte S8 { get; set; }
      public short S16 { get; set; }
      public int S32 { get; set; }
      public long S64 { get; set; }
      public byte U8 { get; set; }
      public ushort U16 { get; set; }
      public uint U32 { get; set; }
      public ulong U64 { get; set; }
      public float F32 { get; set; }
      public double F64 { get; set; }
      public char Letter { get; set; }
      public decimal Money { get; set; }
      public Guid Id { get; set; }
      public DateTime When { get; set; }
      public TimeSpan Span { get; set; }
      public GcColor Color { get; set; }
      public GcSmall Small { get; set; }
      public GcSigned Signed { get; set; }
      public GcLong Long { get; set; }
      public GcUnsigned Unsigned { get; set; }
      public int? MaybeInt { get; set; }
      public decimal? MaybeMoney { get; set; }
      public Guid? MaybeId { get; set; }
      public DateTime? MaybeWhen { get; set; }
      public GcSmall? MaybeSmall { get; set; }
      public bool? MaybeFlag { get; set; }
      public override int Overridden { get; set; }
      [System.ComponentModel.DefaultValue(7)]
      public int WithDefault { get; set; } = 7;
      public string InitOnly { get; init; }
      public DateTimeOffset Offset { get; set; }
      public byte[] Bytes { get; set; }
      public GcPerson Child { get; set; }
      public List<int> Numbers { get; set; }
      public int ReadOnly { get { return S32 + 1; } }
    }

    private static GcAllKinds AllKinds(bool defaults)
    {
      if (defaults)
        return new GcAllKinds() { WithDefault = 0 }; // every value its default (WithDefault: not the [DefaultValue])
      return new GcAllKinds()
      {
        BaseName = "base", Overridden = 9, Text = "text", Flag = true, S8 = -8, S16 = -1600, S32 = -320000, S64 = -64000000000, U8 = 200, U16 = 60000, U32 = 4000000000, U64 = ulong.MaxValue,
        F32 = 1.5f, F64 = -2.25, Letter = 'é', Money = -12.3456m, Id = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), When = new DateTime(2026, 10, 12, 8, 30, 0, DateTimeKind.Unspecified),
        Span = TimeSpan.FromMinutes(90), Color = GcColor.Green, Small = GcSmall.Two, Signed = GcSigned.Minus, Long = GcLong.Big, Unsigned = GcUnsigned.Huge,
        MaybeInt = 0, MaybeMoney = 1m, MaybeId = Guid.Empty, MaybeWhen = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), MaybeSmall = GcSmall.One, MaybeFlag = false,
        WithDefault = 7, InitOnly = "init", Offset = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5)), Bytes = new byte[] { 1, 2 }, Child = new GcPerson() { Name = "child" }, Numbers = new List<int>() { 1, 2, 3 }
      };
    }

    private static IEnumerable<(string Name, LtMsgPackOptions Options)> OptionVariants()
    {
      foreach (bool schema in new[] { false, true })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          string name = (schema ? "schema " : "names ") + layout;
          yield return (name, new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout });
          yield return (name + " not compact", new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, DynamicallyCompact = false });
          yield return (name + " no filters", new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0] });
          yield return (name + " custom filters", new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new LsMsgPack.TypeResolving.Filters.FilterNullValues() } });
          yield return (name + " other formats", new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, GuidFormat = GuidFormat.String, DecimalFormat = DecimalFormat.String, UnspecifiedDateTimeKind = DateTimeKind.Utc, ReadDateTimeKind = DateTimeKind.Utc });
          yield return (name + " little endian", new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, EndianAction = EndianAction.NeverSwap });
          yield return (name + " type ids always", new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, AddTypeIdOptions = AddTypeIdOption.Always });
          yield return (name + " decimal type 5", new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, Extensions = new LtMsgPack.Extensions.LtExtension[] { new LtMsgPack.Extensions.DecimalExtension(5) } });
          yield return (name + " order", new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, PropertyOrder = PropertyOrder.Alphabetical });
          if (layout == ObjectLayout.Array)
            yield return (name + " trimmed", new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, TrimTrailingNulls = true });
        }
    }

    private static LtMsgPackSerializer WithCode(LtMsgPackOptions options, bool generated)
    {
      LtMsgPackOptions copy = options.Clone();
      copy.Contexts = generated ? new LtMsgPackContext[] { TestMsgPackContext.Default } : null;
      copy.UseRegisteredContexts = false;
      return new LtMsgPackSerializer(copy);
    }

    private static string Read(LtMsgPackSerializer serializer, byte[] data, LtMsgPackSerializer dumper)
    {
      try
      {
        return BitConverter.ToString(dumper.Serialize(serializer.Deserialize<GcAllKinds>(data)));
      }
      catch (Exception ex)
      {
        return "EX " + ex.GetType().Name;
      }
    }

    [TestMethod]
    public void AllKinds_SameBytesAndValuesAsTheRuntime_InEveryOptionVariant()
    {
      List<string> differences = new List<string>();
      LtMsgPackSerializer dumper = new LtMsgPackSerializer(new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, UseRegisteredContexts = false });
      foreach ((string name, LtMsgPackOptions options) in OptionVariants())
      {
        foreach (bool defaults in new[] { false, true })
        {
          GcAllKinds value = AllKinds(defaults);
          byte[] runtime = WithCode(options, false).Serialize(value);
          byte[] generated = WithCode(options, true).Serialize(value);
          if (!runtime.AsSpan().SequenceEqual(generated))
          {
            differences.Add($"{name} (defaults {defaults}): bytes differ\n  runtime   {BitConverter.ToString(runtime)}\n  generated {BitConverter.ToString(generated)}");
            continue;
          }
          string expected = Read(WithCode(options, false), runtime, dumper);
          string actual = Read(WithCode(options, true), runtime, dumper);
          if (expected != actual)
            differences.Add($"{name} (defaults {defaults}): values differ\n  runtime   {expected}\n  generated {actual}");
        }
      }
      Assert.IsEmpty(differences, string.Join("\n", differences));
    }

    [TestMethod]
    public void AllKinds_DataOfOtherOptions_ReadAsTheRuntimeReadsIt()
    {
      // the generated readers meet formats they do not read themselves (other number formats, strings for Guids and decimals, ...): the runtime reads those values
      List<string> differences = new List<string>();
      LtMsgPackSerializer dumper = new LtMsgPackSerializer(new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, UseRegisteredContexts = false });
      List<(string Name, LtMsgPackOptions Options)> variants = new List<(string, LtMsgPackOptions)>(OptionVariants());
      foreach ((string writerName, LtMsgPackOptions writer) in variants)
      {
        byte[] data = WithCode(writer, false).Serialize(AllKinds(false));
        foreach ((string readerName, LtMsgPackOptions reader) in variants)
        {
          if (reader.UseInexedSchema != writer.UseInexedSchema)
            continue;
          string expected = Read(WithCode(reader, false), data, dumper);
          string actual = Read(WithCode(reader, true), data, dumper);
          if (expected != actual)
            differences.Add($"written {writerName}, read {readerName}\n  runtime   {expected}\n  generated {actual}");
        }
      }
      Assert.IsEmpty(differences, string.Join("\n", differences.GetRange(0, Math.Min(10, differences.Count))));
    }

    [TestMethod]
    public void OutOfRangeAndOtherFormats_FailAndConvertAsTheRuntime()
    {
      LtMsgPackSerializer names = new LtMsgPackSerializer(new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, UseRegisteredContexts = false });
      Dictionary<string, object>[] maps = new[]
      {
        new Dictionary<string, object>() { { "S8", 300 } }, // overflow
        new Dictionary<string, object>() { { "U32", -1 } },
        new Dictionary<string, object>() { { "Letter", 70000 } },
        new Dictionary<string, object>() { { "S32", "12" } }, // a string for a number
        new Dictionary<string, object>() { { "Text", 12 } }, // a number for a string
        new Dictionary<string, object>() { { "Id", "0f8fad5b-d9cb-469f-a165-70867728950e" } },
        new Dictionary<string, object>() { { "Money", 1.5 } },
        new Dictionary<string, object>() { { "Small", 513 } }, // an enum wraps around
        new Dictionary<string, object>() { { "MaybeInt", null }, { "Flag", null }, { "Text", null } },
        new Dictionary<string, object>() { { "F32", 2.5 } }, // a double for a float
      };
      foreach (Dictionary<string, object> map in maps)
      {
        byte[] data = names.Serialize(map);
        foreach (ObjectLayout layout in new[] { ObjectLayout.Map, ObjectLayout.Array })
        {
          LtMsgPackOptions options = new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = layout };
          string expected = Read(WithCode(options, false), data, names);
          string actual = Read(WithCode(options, true), data, names);
          Assert.AreEqual(expected, actual, string.Join(", ", map.Keys) + " " + layout);
        }
      }
    }

    private static object WritePlan(LtMsgPackSerializer serializer, Type type, bool names)
    {
      object inner = Inner(serializer);
      object plan = inner.GetType().GetMethod("Plan", Internal).Invoke(inner, new object[] { type, names });
      return plan.GetType().GetField("GeneratedPlan", Internal).GetValue(plan);
    }

    [TestMethod]
    public void TheGeneratedWriterAndReader_AreUsed()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions() { Contexts = new LtMsgPackContext[] { TestMsgPackContext.Default }, UseRegisteredContexts = false, UseInexedSchema = false });
      serializer.Deserialize<GcAllKinds>(serializer.Serialize(AllKinds(false)));

      object generated = WritePlan(serializer, typeof(GcAllKinds), true);
      Assert.IsNotNull(generated, "the generated writer is used");
      Assert.IsTrue((bool)generated.GetType().GetField("Unrolled", Internal).GetValue(generated), "the runtime's order is the predicted one (derived class first, then the base class)");
      int[] ids = ((LtMsgPack.Generated.GeneratedWritePlan)generated).Ids;
      Assert.DoesNotContain(-1, ids, "every property is a generated member (the runtime writes the ones of kind None, at their position)");

      object inner = Inner(serializer);
      object readPlan = inner.GetType().GetMethod("GetReadPlan", Internal).Invoke(inner, new object[] { typeof(GcAllKinds) });
      foreach (LtMsgPack.Reading.PropReader reader in (LtMsgPack.Reading.PropReader[])readPlan.GetType().GetField("Props", Internal).GetValue(readPlan))
      {
        string name = ((FullPropertyInfo)typeof(LtMsgPack.Reading.PropReader).GetField("Info", Internal).GetValue(reader)).PropertyInfo.Name;
        bool runtime = name == nameof(GcAllKinds.InitOnly) || name == nameof(GcAllKinds.Offset) || name == nameof(GcAllKinds.Bytes) || name == nameof(GcAllKinds.Child) || name == nameof(GcAllKinds.Numbers);
        Assert.AreEqual(runtime, reader.GeneratedId < 0, name);
      }
    }

    [TestMethod]
    public void OptionsTheGeneratedCodeDoesNotFit_AreLeftToTheRuntime()
    {
      LtMsgPackOptions options = new LtMsgPackOptions() { Contexts = new LtMsgPackContext[] { TestMsgPackContext.Default }, UseRegisteredContexts = false, UseInexedSchema = false, GuidFormat = GuidFormat.String };
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(options);
      serializer.Serialize(AllKinds(false));
      LtMsgPack.Generated.GeneratedWritePlan plan = (LtMsgPack.Generated.GeneratedWritePlan)WritePlan(serializer, typeof(GcAllKinds), true);
      Assert.IsFalse((bool)plan.GetType().GetField("Unrolled", Internal).GetValue(plan), "a Guid is a string with these options: not unrolled");
      Assert.Contains(-1, plan.Ids, "the Guids are written by the runtime");

      options.GuidFormat = GuidFormat.Binary;
      options.DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new LsMsgPack.TypeResolving.Filters.FilterNullValues() };
      serializer = new LtMsgPackSerializer(options);
      serializer.Serialize(AllKinds(false));
      Assert.IsNull(WritePlan(serializer, typeof(GcAllKinds), true), "custom dynamic filters: the runtime writes");
    }

    public class GcVersion1
    {
      public string Name { get; set; }
      public int Age { get; set; }
      public GcColor Color { get; set; }
      public List<GcVersion1> Children { get; set; }
    }

    /// <summary>
    /// A later version of GcVersion1: reordered, one property removed, some added.
    /// </summary>
    public class GcVersion2
    {
      public decimal Score { get; set; }
      public int Age { get; set; }
      public string Name { get; set; }
      public DateTime? Added { get; set; }
      public List<GcVersion2> Children { get; set; }
    }

    /// <summary>
    /// GcVersion1 with properties appended (the only change the array layout without the schema allows).
    /// </summary>
    public class GcVersion1Appended
    {
      public string Name { get; set; }
      public int Age { get; set; }
      public GcColor Color { get; set; }
      public List<GcVersion1Appended> Children { get; set; }
      public decimal Score { get; set; }
      public Guid Id { get; set; }
    }

    private static string ReadAs<T>(LtMsgPackSerializer serializer, byte[] data, LtMsgPackSerializer dumper)
    {
      try
      {
        return BitConverter.ToString(dumper.Serialize(serializer.Deserialize<T>(data)));
      }
      catch (Exception ex)
      {
        return "EX " + ex.GetType().Name;
      }
    }

    [TestMethod]
    public void RollingUpgrade_OtherClassShapes_ReadAsTheRuntime()
    {
      LtMsgPackSerializer dumper = new LtMsgPackSerializer(new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, UseRegisteredContexts = false });
      GcVersion1 old = new GcVersion1() { Name = "one", Age = 1, Color = GcColor.Blue, Children = new List<GcVersion1>() { new GcVersion1() { Name = "kid", Age = 2 } } };
      GcVersion2 newer = new GcVersion2() { Score = 9.5m, Age = 3, Name = "two", Added = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Children = new List<GcVersion2>() { new GcVersion2() { Name = "kid" } } };
      GcVersion1Appended appended = new GcVersion1Appended() { Name = "three", Age = 4, Color = GcColor.Green, Score = 1m, Id = Guid.NewGuid() };
      List<string> differences = new List<string>();
      foreach (bool schema in new[] { false, true })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          LtMsgPackOptions options = new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout };
          LtMsgPackSerializer runtime = WithCode(options, false);
          LtMsgPackSerializer generated = WithCode(options, true);
          string mode = (schema ? "schema " : "names ") + layout;
          byte[] fromOld = runtime.Serialize(old);
          byte[] fromNewer = runtime.Serialize(newer);
          byte[] fromAppended = runtime.Serialize(appended);
          foreach ((string name, string expected, string actual) in new[]
          {
            ("v1 as v2", ReadAs<GcVersion2>(runtime, fromOld, dumper), ReadAs<GcVersion2>(generated, fromOld, dumper)),
            ("v2 as v1", ReadAs<GcVersion1>(runtime, fromNewer, dumper), ReadAs<GcVersion1>(generated, fromNewer, dumper)),
            ("v1 as appended", ReadAs<GcVersion1Appended>(runtime, fromOld, dumper), ReadAs<GcVersion1Appended>(generated, fromOld, dumper)),
            ("appended as v1", ReadAs<GcVersion1>(runtime, fromAppended, dumper), ReadAs<GcVersion1>(generated, fromAppended, dumper))
          })
          {
            if (expected != actual)
              differences.Add($"{mode} {name}\n  runtime   {expected}\n  generated {actual}");
          }
        }
      Assert.IsEmpty(differences, string.Join("\n", differences));
    }

    #endregion

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
      types.Add(GeneratedValues.Object<GcPerson>(() => { throw new InvalidOperationException("must not be used"); }, null,
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
      HandWritten context = new HandWritten(GeneratedContract.Current, types => types.Add(GeneratedValues.Object<GcStale>(() => new GcStale(), null,
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
