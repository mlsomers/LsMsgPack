using LsMsgPack;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LtMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// LtMsgPack compiles a writer per type at runtime (LtMsgPackOptions.CompileWritersAfter): it is used, and writes what the runtime's plans write.
  /// CrossLibraryTests also compares it with LsMsgPack (the corpus in all settings, compiled from the first object).
  /// </summary>
  [TestClass]
  public class CompiledWriterTests
  {
    public enum CwSmall : byte { None, One, Max = 255 }
    public enum CwSigned : sbyte { Low = -128, Zero = 0, High = 127 }
    public enum CwShort : short { A = -300, B = 0, C = 300 }
    public enum CwLong : long { Min = long.MinValue, Zero = 0, Max = long.MaxValue }
    public enum CwUnsigned : ulong { Zero = 0, Max = ulong.MaxValue }

    public struct CwPoint { public float X { get; set; } public double Y { get; set; } public int? N { get; set; } }

    public class CwBase
    {
      public string Inherited { get; set; }
      public virtual int Overridden { get; set; }
    }

    public class CwAllKinds : CwBase
    {
      public string S { get; set; }
      public bool B { get; set; }
      public sbyte I8 { get; set; }
      public short I16 { get; set; }
      public int I32 { get; set; }
      public long I64 { get; set; }
      public byte U8 { get; set; }
      public ushort U16 { get; set; }
      public uint U32 { get; set; }
      public ulong U64 { get; set; }
      public float F { get; set; }
      public double D { get; set; }
      public char C { get; set; }
      public decimal M { get; set; }
      public Guid G { get; set; }
      public DateTime Dt { get; set; }
      public TimeSpan Ts { get; set; }
      public CwSmall E8 { get; set; }
      public CwSigned Es { get; set; }
      public CwShort E16 { get; set; }
      public CwLong E64 { get; set; }
      public CwUnsigned Eu { get; set; }
      public int? NI { get; set; }
      public DateTime? NDt { get; set; }
      public decimal? NM { get; set; }
      public CwSmall? NE { get; set; }
      public Guid? NG { get; set; }
      public override int Overridden { get; set; }
      [DefaultValue(7)] public int WithDefault { get; set; } = 7;
      [DefaultValue("x")] public string WithDefaultText { get; set; } = "x";
      public DateTimeOffset Dto { get; set; }
      public byte[] Bin { get; set; }
      public object Anything { get; set; }
      public CwPoint Point { get; set; }
      public CwPoint? MaybePoint { get; set; }
      public CwAllKinds Child { get; set; }
      public List<CwAllKinds> Children { get; set; }
      public Dictionary<string, int> Map { get; set; }
      public int Computed { get { return I32 * 2; } }
      public static int Static { get; set; } = 3;
    }

    public class CwWide
    {
      public int P00 { get; set; } public int P01 { get; set; } public int P02 { get; set; } public int P03 { get; set; } public int P04 { get; set; }
      public string P05 { get; set; } public string P06 { get; set; } public string P07 { get; set; } public string P08 { get; set; } public string P09 { get; set; }
      public double P10 { get; set; } public double P11 { get; set; } public double P12 { get; set; } public double P13 { get; set; } public double P14 { get; set; }
      public DateTime P15 { get; set; } public DateTime? P16 { get; set; } public decimal P17 { get; set; } public Guid P18 { get; set; } public bool P19 { get; set; }
      public int? P20 { get; set; } public long P21 { get; set; } public CwSmall P22 { get; set; } public CwWide P23 { get; set; } public List<int> P24 { get; set; }
    }

    private static CwAllKinds Full()
    {
      return new CwAllKinds
      {
        Inherited = "base", Overridden = 5, S = "text ü", B = true, I8 = -5, I16 = -300, I32 = 70000, I64 = -5000000000, U8 = 200, U16 = 60000, U32 = 4000000000, U64 = ulong.MaxValue,
        F = -1.5f, D = double.NaN, C = 'é', M = -12.345m, G = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Dt = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Unspecified),
        Ts = TimeSpan.FromMinutes(-3), E8 = CwSmall.Max, Es = CwSigned.Low, E16 = CwShort.A, E64 = CwLong.Min, Eu = CwUnsigned.Max, NI = 0, NDt = new DateTime(1, 1, 1), NM = 0.00m,
        NE = CwSmall.None, NG = Guid.Empty, WithDefault = 8, WithDefaultText = "y", Dto = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), Bin = new byte[] { 1, 2 },
        Anything = new CwPoint { X = 1 }, Point = new CwPoint { X = 2, Y = -0.0, N = 4 }, MaybePoint = new CwPoint(),
        Child = new CwAllKinds { S = "", D = -0.0, F = float.NaN, Dt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Local) },
        Children = new List<CwAllKinds> { new CwAllKinds(), null }, Map = new Dictionary<string, int> { { "a", 1 } }
      };
    }

    private static IEnumerable<CwAllKinds> Values()
    {
      yield return Full();
      yield return new CwAllKinds(); // the defaults: left out by FilterDefaultValues, trailing nils trimmed
      yield return new CwAllKinds { WithDefault = 0, WithDefaultText = null, I32 = 1 };
      CwAllKinds last = new CwAllKinds { Children = new List<CwAllKinds> { Full() } }; // only the end of the array has values
      yield return last;
    }

    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <returns>The type name of the code the plan writes with, null for the runtime's plan</returns>
    private static string WriterOf(LtMsgPackSerializer serializer, Type type, bool names)
    {
      object inner = typeof(LtMsgPackSerializer).GetField("_serializer", Internal).GetValue(serializer);
      object plan = inner.GetType().GetMethod("Plan", Internal).Invoke(inner, new object[] { type, names });
      object generated = plan.GetType().GetField("GeneratedPlan", Internal).GetValue(plan);
      if (generated is null)
        return null;
      Type code = generated.GetType().GetField("Code", Internal).GetValue(generated).GetType();
      return code.Name.Substring(0, code.Name.IndexOf('`') < 0 ? code.Name.Length : code.Name.IndexOf('`'));
    }

    /// <summary>
    /// After the number of objects the writer is compiled on the thread pool: waits for it (at most 10 s).
    /// </summary>
    private static string WaitForWriter(LtMsgPackSerializer serializer, Type type, bool names)
    {
      Stopwatch waited = Stopwatch.StartNew();
      string writer;
      while ((writer = WriterOf(serializer, type, names)) is null && waited.ElapsedMilliseconds < 10000)
        System.Threading.Thread.Sleep(5);
      return writer;
    }

    private static LtMsgPackSerializer Create(LtMsgPackOptions options, int compileAfter)
    {
      LtMsgPackOptions copy = options.Clone();
      copy.UseRegisteredContexts = false;
      copy.CompileWritersAfter = compileAfter;
      return new LtMsgPackSerializer(copy);
    }

    private static IEnumerable<LtMsgPackOptions> AllOptions()
    {
      foreach (bool schema in new[] { false, true })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          foreach (bool filter in new[] { false, true })
            foreach (bool trim in new[] { false, true })
              foreach (bool reference in schema ? new[] { false, true } : new[] { false })
                yield return new LtMsgPackOptions
                {
                  UseInexedSchema = schema, WriteSchemaReference = reference, SchemaStore = reference ? new SchemaStore() : null, ObjectLayout = layout, TrimTrailingNulls = trim,
                  DynamicFilters = filter ? new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues() } : new IMsgPackPropertyIncludeDynamically[0],
                  UnspecifiedDateTimeKind = trim ? DateTimeKind.Utc : DateTimeKind.Local
                };
    }

    private static string Describe(LtMsgPackOptions o)
    {
      return $"schema {o.UseInexedSchema}, reference {o.WriteSchemaReference}, {o.ObjectLayout}, filters {o.DynamicFilters.Length}, trim {o.TrimTrailingNulls}";
    }

    [TestMethod]
    public void TheCompiledWriter_TakesOverAfterTheNumberOfObjects()
    {
      LtMsgPackSerializer runtime = Create(new LtMsgPackOptions { UseInexedSchema = false }, -1);
      LtMsgPackSerializer compiled = Create(new LtMsgPackOptions { UseInexedSchema = false }, 3);
      CwAllKinds value = new CwAllKinds { I32 = 4, S = "s" };
      byte[] expected = runtime.Serialize(value);
      for (int t = 1; t <= 2; t++)
      {
        CollectionAssert.AreEqual(expected, compiled.Serialize(value), "object " + t);
        Assert.IsNull(WriterOf(compiled, typeof(CwAllKinds), true), "after object " + t);
      }
      CollectionAssert.AreEqual(expected, compiled.Serialize(value), "object 3: compiles in the background");
      Assert.AreEqual("CompiledObjectCode", WaitForWriter(compiled, typeof(CwAllKinds), true));
      CollectionAssert.AreEqual(expected, compiled.Serialize(value), "object 4");
      Assert.IsNull(WriterOf(runtime, typeof(CwAllKinds), true), "-1: never");
    }

    [TestMethod]
    public void Default_CompilesAfterAThousandObjects()
    {
      Assert.AreEqual(1000, new LtMsgPackOptions().CompileWritersAfter);
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, UseRegisteredContexts = false });
      List<CwPoint> points = new List<CwPoint>();
      for (int t = 0; t < 999; t++)
        points.Add(new CwPoint { X = t });
      serializer.Serialize(points);
      Assert.IsNull(WriterOf(serializer, typeof(CwPoint), true));
      serializer.Serialize(new CwPoint());
      Assert.AreEqual("CompiledStructCode", WaitForWriter(serializer, typeof(CwPoint), true));
    }

    [TestMethod]
    public void AllKinds_SameBytesAsTheRuntimesPlans()
    {
      List<string> differences = new List<string>();
      foreach (LtMsgPackOptions options in AllOptions())
      {
        LtMsgPackSerializer runtime = Create(options, -1);
        LtMsgPackSerializer compiled = Create(options, 0);
        foreach (CwAllKinds value in Values())
        {
          foreach (Type declared in new[] { typeof(CwAllKinds), typeof(CwBase), typeof(object) })
          {
            byte[] expected, actual;
            try
            {
              expected = runtime.Serialize(value, declared);
            }
            catch (Exception ex) // e.g. the array layout of a value of a derived type: the compiled writer throws the same
            {
              Exception other = null;
              try
              {
                compiled.Serialize(value, declared);
              }
              catch (Exception compiledEx)
              {
                other = compiledEx;
              }
              Assert.AreEqual(ex.GetType(), other?.GetType(), Describe(options));
              Assert.AreEqual(ex.Message, other.Message, Describe(options));
              continue;
            }
            actual = compiled.Serialize(value, declared);
            if (!((IStructuralEquatable)expected).Equals(actual, StructuralComparisons.StructuralEqualityComparer))
              differences.Add($"{Describe(options)}, as {declared.Name}: {BitConverter.ToString(expected)} != {BitConverter.ToString(actual)}");
          }
        }
        bool names = !options.UseInexedSchema;
        Assert.AreEqual("CompiledObjectCode", WriterOf(compiled, typeof(CwAllKinds), names), Describe(options));
        Assert.AreEqual("CompiledStructCode", WriterOf(compiled, typeof(CwPoint), names), Describe(options));
      }
      Assert.IsEmpty(differences, string.Join("\n", differences));
    }

    [TestMethod]
    public void WideClass_SameBytesAsTheRuntimesPlans()
    {
      CwWide wide = new CwWide { P00 = 1, P05 = "five", P10 = 0.5, P15 = new DateTime(2020, 2, 2, 0, 0, 0, DateTimeKind.Utc), P17 = 1.7m, P19 = true, P20 = 20, P22 = CwSmall.One, P23 = new CwWide { P04 = 4 }, P24 = new List<int> { 1, 2 } };
      foreach (LtMsgPackOptions options in AllOptions())
      {
        byte[] expected = Create(options, -1).Serialize(wide);
        LtMsgPackSerializer compiled = Create(options, 0);
        CollectionAssert.AreEqual(expected, compiled.Serialize(wide), Describe(options));
        CwWide read = compiled.Deserialize<CwWide>(expected);
        Assert.AreEqual("five", read.P05);
        Assert.AreEqual(4, read.P23.P04);
      }
    }

    [TestMethod]
    public void Collections_WithSerializeProperties_UseTheMapWriter()
    {
      // An array layout plan writes the properties of such a collection as map entries: the compiled Write (with keys) is compiled at its first call
      foreach (LtMsgPackOptions options in AllOptions())
      {
        CwPropertyBag bag = new CwPropertyBag { Name = "bag", Count2 = 2 };
        bag.Add(1);
        bag.Add(2);
        byte[] expected = Create(options, -1).Serialize(bag);
        LtMsgPackSerializer compiled = Create(options, 0);
        CollectionAssert.AreEqual(expected, compiled.Serialize(bag), Describe(options));
        CollectionAssert.AreEqual(expected, compiled.Serialize(bag), Describe(options) + ", again");
      }
    }

    [SerializeEnumerable(true)]
    public class CwPropertyBag : List<int>
    {
      public string Name { get; set; }
      public int Count2 { get; set; }
    }

    [TestMethod]
    public void NonPublicProperties_AreRead()
    {
      LtMsgPackOptions options = new LtMsgPackOptions { UseInexedSchema = false, StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.All), new FilterIgnoredAttribute(), new FilterStatic() } };
      CwHidden hidden = CwHidden.Create(5, "secret");
      byte[] expected = Create(options, -1).Serialize(hidden);
      LtMsgPackSerializer compiled = Create(options, 0);
      CollectionAssert.AreEqual(expected, compiled.Serialize(hidden));
      Assert.AreEqual("CompiledObjectCode", WriterOf(compiled, typeof(CwHidden), true));
    }

    public class CwHidden
    {
      private int Number { get; set; }
      internal string Text { get; private set; }
      public int Visible { get; set; }

      public static CwHidden Create(int number, string text)
      {
        return new CwHidden { Number = number, Text = text, Visible = 1 };
      }
    }

    [TestMethod]
    public void Structs_ByReference_NoAllocation()
    {
      LtStructTests.LtsMover moving = new LtStructTests.LtsMover
      {
        Name = "m", Position = new LtStructTests.LtsVec { X = 1, Y = 2, Z = 3 }, Velocity = new LtStructTests.LtsVec { Z = -0.5f }, Target = new LtStructTests.LtsVec { Y = 4 },
        Path = new List<LtStructTests.LtsVec> { new LtStructTests.LtsVec { X = 1 } }, Points = new[] { new LtStructTests.LtsVec { Z = 9 } }
      };
      MemoryStream stream = new MemoryStream(1024);
      DebuggableAttribute debuggable = typeof(LtMsgPackSerializer).Assembly.GetCustomAttribute<DebuggableAttribute>();
      bool optimized = debuggable == null || !debuggable.IsJITOptimizerDisabled;
      foreach (LtMsgPackOptions options in AllOptions())
      {
        LtMsgPackSerializer compiled = Create(options, 0);
        byte[] expected = Create(options, -1).Serialize(moving);
        CollectionAssert.AreEqual(expected, compiled.Serialize(moving), Describe(options));
        Assert.AreEqual("CompiledStructCode", WriterOf(compiled, typeof(LtStructTests.LtsVec), !options.UseInexedSchema), Describe(options));
        for (int t = 0; t < 3; t++)
        {
          stream.SetLength(0);
          compiled.Serialize(moving, stream);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        stream.SetLength(0);
        compiled.Serialize(moving, stream);
        long written = GC.GetAllocatedBytesForCurrentThread() - before;
        if (optimized)
          Assert.AreEqual(0L, written, Describe(options));
      }
    }

    [TestMethod]
    public void SeveralThreads_WhileTheWriterIsCompiled()
    {
      LtMsgPackOptions options = new LtMsgPackOptions { UseInexedSchema = true, DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues() } };
      CwAllKinds value = Full();
      CwWide wide = new CwWide { P05 = "w", P23 = new CwWide { P00 = 1 } };
      byte[] expected = Create(options, -1).Serialize(value);
      byte[] expectedWide = Create(options, -1).Serialize(wide);
      for (int round = 0; round < 5; round++)
      {
        LtMsgPackSerializer compiled = Create(options, 20);
        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
        {
          if (t % 2 == 0)
            CollectionAssert.AreEqual(expected, compiled.Serialize(value));
          else
            CollectionAssert.AreEqual(expectedWide, compiled.Serialize(wide));
        });
        Assert.AreEqual("CompiledObjectCode", WaitForWriter(compiled, typeof(CwAllKinds), false));
        Assert.AreEqual("CompiledObjectCode", WaitForWriter(compiled, typeof(CwWide), false));
        CollectionAssert.AreEqual(expected, compiled.Serialize(value), "compiled");
        CollectionAssert.AreEqual(expectedWide, compiled.Serialize(wide), "compiled");
      }
    }

    private sealed class CwCustomFilter : IMsgPackPropertyIncludeDynamically
    {
      public bool IncludeProperty(LsMsgPack.Meta.FullPropertyInfo property, object value)
      {
        return property.PropertyInfo.Name != "S";
      }
    }

    [TestMethod]
    public void NotCompiled_WhereTheGeneratedCodeWouldNotBeUsed()
    {
      CwAllKinds value = Full();
      // Custom dynamic filters (asked per value with the value boxed) and a type id on every value
      foreach (LtMsgPackOptions options in new[]
      {
        new LtMsgPackOptions { UseInexedSchema = false, DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new CwCustomFilter() } },
        new LtMsgPackOptions { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.Always | AddTypeIdOption.FullName }
      })
      {
        LtMsgPackSerializer compiled = Create(options, 0);
        CollectionAssert.AreEqual(Create(options, -1).Serialize(value), compiled.Serialize(value));
        Assert.IsNull(WriterOf(compiled, typeof(CwAllKinds), true));
      }
    }

    [TestMethod]
    public void TheSourceGeneratorsCode_IsPreferred()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions
      {
        UseInexedSchema = false, Contexts = new LtMsgPackContext[] { TestMsgPackContext.Default }, UseRegisteredContexts = false, CompileWritersAfter = 0
      });
      serializer.Serialize(Full());
      Assert.AreNotEqual("CompiledObjectCode", WriterOf(serializer, typeof(CwAllKinds), true));
      Assert.IsNotNull(WriterOf(serializer, typeof(CwAllKinds), true), "the generated code");
    }
  }
}
