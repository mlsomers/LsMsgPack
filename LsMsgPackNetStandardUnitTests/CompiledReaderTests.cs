using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LtMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// LtMsgPack compiles a reader per type at runtime (LtMsgPackOptions.CompileReadersAfter): it is used, and reads what the runtime's plans read (the same values, conversions and errors).
  /// CrossLibraryTests also compares it with LsMsgPack (the corpus in all settings, compiled from the first object).
  /// </summary>
  [TestClass]
  public class CompiledReaderTests
  {
    public class CrSimple
    {
      public bool B { get; set; }
      public char C { get; set; }
      public double D { get; set; }
      public CompiledWriterTests.CwSmall E { get; set; }
      public float F { get; set; }
      public Guid G { get; set; }
      public int I { get; set; }
      public long L { get; set; }
      public decimal M { get; set; }
      public int? N { get; set; }
      public string S { get; set; }
      public DateTime T { get; set; }
      public TimeSpan Ts { get; set; }
      public ulong U64 { get; set; }
      public byte U8 { get; set; }
      [DefaultValue(7)] public int WithDefault { get; set; } = 7;
      public List<int> Items { get; set; }
      public short Field; // not serialized by default
    }

    /// <summary>
    /// The properties of <see cref="CrSimple"/> declared as object (any value in their place), and one more at the end.
    /// </summary>
    public class CrLooseTail
    {
      public object B { get; set; }
      public object C { get; set; }
      public object D { get; set; }
      public object E { get; set; }
      public object F { get; set; }
      public object G { get; set; }
      public object I { get; set; }
      public object L { get; set; }
      public object M { get; set; }
      public object N { get; set; }
      public object S { get; set; }
      public object T { get; set; }
      public object Ts { get; set; }
      public object U64 { get; set; }
      public object U8 { get; set; }
      public object WithDefault { get; set; }
      public object Items { get; set; }
      public object Zz { get; set; }
    }

    /// <summary>
    /// Some of the properties of <see cref="CrSimple"/> in another order (PropertyOrder.Declaration), with one it does not have before them.
    /// </summary>
    public class CrLooseHead
    {
      public object A0 { get; set; }
      public object S { get; set; }
      public object I { get; set; }
      public object B { get; set; }
      public object N { get; set; }
      public object D { get; set; }
    }

    public class CrImmutable
    {
      public CrImmutable(int number, string text)
      {
        Number = number;
        Text = text;
      }

      public int Number { get; }
      public string Text { get; }
    }

    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object CodeOf(LtMsgPackSerializer serializer, Type type)
    {
      object inner = typeof(LtMsgPackSerializer).GetField("_serializer", Internal).GetValue(serializer);
      object plan = inner.GetType().GetMethod("GetReadPlan", Internal).Invoke(inner, new object[] { type });
      return plan?.GetType().GetField("Code", Internal).GetValue(plan);
    }

    /// <returns>The type name of the code the read plan reads with, null for the runtime's readers</returns>
    internal static string ReaderOf(LtMsgPackSerializer serializer, Type type)
    {
      Type code = CodeOf(serializer, type)?.GetType();
      if (code is null)
        return null;
      return code.Name.Substring(0, code.Name.IndexOf('`') < 0 ? code.Name.Length : code.Name.IndexOf('`'));
    }

    /// <returns>Whether the plan has a compiled reader of the layout (maps: with the indexed schema)</returns>
    private static bool IsCompiled(LtMsgPackSerializer serializer, Type type, bool map)
    {
      object code = CodeOf(serializer, type);
      return code != null && (bool)code.GetType().GetMethod("IsCompiled").Invoke(code, new object[] { map });
    }

    /// <summary>
    /// After the number of objects the reader is compiled on the thread pool: waits for it (at most 10 s).
    /// </summary>
    private static string WaitForReader(LtMsgPackSerializer serializer, Type type)
    {
      Stopwatch waited = Stopwatch.StartNew();
      string reader;
      while ((reader = ReaderOf(serializer, type)) is null && waited.ElapsedMilliseconds < 10000)
        System.Threading.Thread.Sleep(5);
      return reader;
    }

    /// <param name="compileAfter">For the readers; the writers are the runtime's</param>
    private static LtMsgPackSerializer Create(LtMsgPackOptions options, int compileAfter)
    {
      LtMsgPackOptions copy = options.Clone();
      copy.UseRegisteredContexts = false;
      copy.CompileWritersAfter = -1;
      copy.CompileReadersAfter = compileAfter;
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
                  ReadDateTimeKind = trim ? DateTimeKind.Local : DateTimeKind.Utc, PropertyOrder = filter ? PropertyOrder.Declaration : PropertyOrder.Alphabetical
                };
      foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map }) // Guids and decimals as strings (the Generic() preset), read by the compiled code too
        foreach (bool schema in new[] { false, true })
          yield return new LtMsgPackOptions
          {
            UseInexedSchema = schema, ObjectLayout = layout, GuidFormat = GuidFormat.String, DecimalFormat = DecimalFormat.String,
            DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues() }
          };
    }

    private static string Describe(LtMsgPackOptions o)
    {
      return $"schema {o.UseInexedSchema}, reference {o.WriteSchemaReference}, {o.ObjectLayout}, filters {o.DynamicFilters.Length}, trim {o.TrimTrailingNulls}, {o.GuidFormat}/{o.DecimalFormat}, {o.PropertyOrder}";
    }

    /// <summary>
    /// Everything of a value that was read, as bytes (maps with names, every value written).
    /// </summary>
    private static readonly LtMsgPackSerializer Dump = new LtMsgPackSerializer(new LtMsgPackOptions
    {
      UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0], UseRegisteredContexts = false, CompileWritersAfter = -1,
      UnspecifiedDateTimeKind = DateTimeKind.Utc
    });

    /// <summary>
    /// What reading gives: the value as bytes, or the exception.
    /// </summary>
    private static string Read(LtMsgPackSerializer serializer, Type type, byte[] data)
    {
      object read;
      try
      {
        read = serializer.Deserialize(type, data);
      }
      catch (Exception ex)
      {
        return ex.GetType().Name + ": " + ex.Message;
      }
      return (read?.GetType().Name ?? "null") + " " + BitConverter.ToString(Dump.Serialize(read, typeof(object)));
    }

    private static CrSimple Simple()
    {
      return new CrSimple
      {
        B = true, C = 'é', D = -1.5, E = CompiledWriterTests.CwSmall.Max, F = 2.5f, G = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), I = -70000, L = 5000000000, M = 12.50m, N = 3, S = "text ü",
        T = new DateTime(2026, 11, 10, 12, 30, 0, 250, DateTimeKind.Utc), Ts = TimeSpan.FromSeconds(90), U64 = ulong.MaxValue, U8 = 200, WithDefault = 8, Items = new List<int> { 1, 2 }
      };
    }

    private static IEnumerable<CompiledWriterTests.CwAllKinds> Values()
    {
      CompiledWriterTests.CwAllKinds full = new CompiledWriterTests.CwAllKinds
      {
        Inherited = "base", Overridden = 5, S = "text ü", B = true, I8 = -5, I16 = -300, I32 = 70000, I64 = -5000000000, U8 = 200, U16 = 60000, U32 = 4000000000, U64 = ulong.MaxValue,
        F = -1.5f, D = double.NaN, C = 'é', M = -12.345m, G = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Dt = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc),
        Ts = TimeSpan.FromMinutes(-3), E8 = CompiledWriterTests.CwSmall.Max, Es = CompiledWriterTests.CwSigned.Low, E16 = CompiledWriterTests.CwShort.A, E64 = CompiledWriterTests.CwLong.Min,
        Eu = CompiledWriterTests.CwUnsigned.Max, NI = 0, NDt = new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc), NM = 0.00m, NE = CompiledWriterTests.CwSmall.None, NG = Guid.Empty, WithDefault = 8, WithDefaultText = "y",
        Dto = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), Bin = new byte[] { 1, 2 }, Point = new CompiledWriterTests.CwPoint { X = 2, Y = -0.0, N = 4 }, MaybePoint = new CompiledWriterTests.CwPoint(),
        Child = new CompiledWriterTests.CwAllKinds { S = "", D = -0.0, F = float.NaN }, Children = new List<CompiledWriterTests.CwAllKinds> { new CompiledWriterTests.CwAllKinds(), null },
        Map = new Dictionary<string, int> { { "a", 1 } }
      };
      yield return full;
      yield return new CompiledWriterTests.CwAllKinds(); // the defaults: left out by FilterDefaultValues, nils in the array layout
      yield return new CompiledWriterTests.CwAllKinds { WithDefault = 0, WithDefaultText = null, I32 = 1 };
      yield return new CompiledWriterTests.CwAllKinds { Children = new List<CompiledWriterTests.CwAllKinds> { full } }; // only the end of the array has values
    }

    [TestMethod]
    public void TheCompiledReader_TakesOverAfterTheNumberOfObjects()
    {
      LtMsgPackOptions options = new LtMsgPackOptions { UseInexedSchema = false };
      LtMsgPackSerializer runtime = Create(options, -1);
      LtMsgPackSerializer compiled = Create(options, 3);
      byte[] data = runtime.Serialize(Simple());
      string expected = Read(runtime, typeof(CrSimple), data);
      for (int t = 1; t <= 2; t++)
      {
        Assert.AreEqual(expected, Read(compiled, typeof(CrSimple), data), "object " + t);
        Assert.IsNull(ReaderOf(compiled, typeof(CrSimple)), "after object " + t);
      }
      Assert.AreEqual(expected, Read(compiled, typeof(CrSimple), data), "object 3: compiles in the background");
      Assert.AreEqual("CompiledObjectReader", WaitForReader(compiled, typeof(CrSimple)));
      Assert.IsTrue(IsCompiled(compiled, typeof(CrSimple), false));
      Assert.AreEqual(expected, Read(compiled, typeof(CrSimple), data), "object 4");
      Assert.IsNull(ReaderOf(runtime, typeof(CrSimple)), "-1: never");
    }

    [TestMethod]
    public void Default_CompilesAfterAThousandObjects()
    {
      Assert.AreEqual(1000, new LtMsgPackOptions().CompileReadersAfter);
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, UseRegisteredContexts = false });
      List<CompiledWriterTests.CwPoint> points = new List<CompiledWriterTests.CwPoint>();
      for (int t = 0; t < 999; t++)
        points.Add(new CompiledWriterTests.CwPoint { X = t });
      serializer.Deserialize<List<CompiledWriterTests.CwPoint>>(serializer.Serialize(points));
      Assert.IsNull(ReaderOf(serializer, typeof(CompiledWriterTests.CwPoint)));
      serializer.Deserialize<CompiledWriterTests.CwPoint>(serializer.Serialize(new CompiledWriterTests.CwPoint()));
      Assert.AreEqual("CompiledStructReader", WaitForReader(serializer, typeof(CompiledWriterTests.CwPoint)));
    }

    [TestMethod]
    public void AllKinds_ReadAsTheRuntimesPlans()
    {
      List<string> differences = new List<string>();
      foreach (LtMsgPackOptions options in AllOptions())
      {
        LtMsgPackSerializer runtime = Create(options, -1);
        LtMsgPackSerializer compiled = Create(options, 0);
        foreach (CompiledWriterTests.CwAllKinds value in Values())
        {
          foreach (Type declared in new[] { typeof(CompiledWriterTests.CwAllKinds), typeof(CompiledWriterTests.CwBase), typeof(object) })
          {
            byte[] data;
            try
            {
              data = runtime.Serialize(value, declared);
            }
            catch (Exception) // e.g. the array layout of a value of a derived type
            {
              continue;
            }
            string expected = Read(runtime, declared, data);
            string actual = Read(compiled, declared, data);
            if (expected != actual)
              differences.Add($"{Describe(options)}, as {declared.Name}:\n  {expected}\n  {actual}");
          }
        }
        bool map = options.ObjectLayout == ObjectLayout.Map;
        Assert.AreEqual("CompiledObjectReader", ReaderOf(compiled, typeof(CompiledWriterTests.CwAllKinds)), Describe(options));
        Assert.AreEqual("CompiledStructReader", ReaderOf(compiled, typeof(CompiledWriterTests.CwPoint)), Describe(options));
        Assert.AreEqual(!map, IsCompiled(compiled, typeof(CompiledWriterTests.CwAllKinds), false), Describe(options) + ": arrays");
        Assert.AreEqual(map && options.UseInexedSchema, IsCompiled(compiled, typeof(CompiledWriterTests.CwAllKinds), true), Describe(options) + ": maps (only with the indexed schema)");
      }
      Assert.IsEmpty(differences, string.Join("\n", differences));
    }

    [TestMethod]
    public void WideClass_ReadAsTheRuntimesPlans()
    {
      CompiledWriterTests.CwWide wide = new CompiledWriterTests.CwWide
      {
        P00 = 1, P05 = "five", P10 = 0.5, P15 = new DateTime(2020, 2, 2, 0, 0, 0, DateTimeKind.Utc), P17 = 1.7m, P19 = true, P20 = 20, P22 = CompiledWriterTests.CwSmall.One,
        P23 = new CompiledWriterTests.CwWide { P04 = 4 }, P24 = new List<int> { 1, 2 }
      };
      foreach (LtMsgPackOptions options in AllOptions())
      {
        LtMsgPackSerializer runtime = Create(options, -1);
        byte[] data = runtime.Serialize(wide);
        Assert.AreEqual(Read(runtime, typeof(CompiledWriterTests.CwWide), data), Read(Create(options, 0), typeof(CompiledWriterTests.CwWide), data), Describe(options));
      }
    }

    /// <summary>
    /// Values in other formats than the reader of the property's type reads directly (other number formats, text, nil, out of range), properties in another order, properties the reader's class
    /// does not have and ones the data does not have: the compiled code leaves them to the runtime's readers, which convert them or throw.
    /// </summary>
    [TestMethod]
    public void OtherFormatsAndClasses_ReadAsTheRuntimesPlans()
    {
      List<object> data = new List<object>
      {
        new CrLooseTail { B = true, C = 'x', D = 1.5, E = (byte)1, F = 1.5f, G = Guid.NewGuid(), I = 5, L = 6L, M = 1.5m, N = 3, S = "s", T = DateTime.UtcNow, Ts = 5L, U64 = 7UL, U8 = (byte)8, WithDefault = 9, Items = new[] { 1 }, Zz = "more" },
        new CrLooseTail(), // every value nil (or left out)
        new CrLooseTail { B = 1, C = "x", D = 2, E = "One", F = 3.5, G = "0f8fad5b-d9cb-469f-a165-70867728950e", I = 5.0, L = "6", M = 2, N = "3", S = 5, T = "2026-11-10T12:00:00Z", Ts = "00:01:00", U64 = 7.0, U8 = 8L, WithDefault = "9", Items = "x" },
        new CrLooseTail { I = long.MaxValue },
        new CrLooseTail { I = uint.MaxValue },
        new CrLooseTail { I = int.MinValue, L = ulong.MaxValue },
        new CrLooseTail { U8 = 256 },
        new CrLooseTail { U8 = -1 },
        new CrLooseTail { C = 70000 },
        new CrLooseTail { U64 = -1 },
        new CrLooseTail { E = 300, N = long.MinValue },
        new CrLooseTail { B = "yes" },
        new CrLooseTail { G = new byte[] { 1, 2, 3 } },
        new CrLooseTail { G = "not a guid" },
        new CrLooseTail { M = "1.5" },
        new CrLooseTail { M = "x" },
        new CrLooseTail { T = 5 },
        new CrLooseTail { D = "NaN", F = double.MaxValue },
        new CrLooseTail { S = new[] { 1, 2 } },
        new CrLooseTail { Zz = new[] { 1, 2 }, WithDefault = null, I = 1 },
        new CrLooseHead { A0 = "first", S = "s", I = 5, B = true, N = 2, D = 0.5 },
        new CrLooseHead { A0 = new[] { 1, 2 }, I = "5", N = null, D = 1 },
        new CrLooseHead(),
        Simple(),
        new CrSimple(),
      };
      List<string> differences = new List<string>();
      int same = 0;
      foreach (LtMsgPackOptions options in AllOptions())
      {
        LtMsgPackSerializer runtime = Create(options, -1);
        LtMsgPackSerializer compiled = Create(options, 0);
        for (int t = 0; t < data.Count; t++)
        {
          byte[] bytes = runtime.Serialize(data[t], data[t].GetType());
          string expected = Read(runtime, typeof(CrSimple), bytes);
          string actual = Read(compiled, typeof(CrSimple), bytes);
          if (expected != actual)
            differences.Add($"{Describe(options)}, #{t} {data[t].GetType().Name}:\n  {expected}\n  {actual}");
          else
            same++;
        }
        Assert.AreEqual("CompiledObjectReader", ReaderOf(compiled, typeof(CrSimple)), Describe(options));
      }
      Assert.IsEmpty(differences, string.Join("\n", differences));
      Assert.IsGreaterThan(400, same);
    }

    /// <summary>
    /// Data that ends anywhere: the same error (or value, where the rest was not needed).
    /// </summary>
    [TestMethod]
    public void DataThatEndsEarly_SameErrorAsTheRuntimesPlans()
    {
      foreach (LtMsgPackOptions options in AllOptions())
      {
        LtMsgPackSerializer runtime = Create(options, -1);
        LtMsgPackSerializer compiled = Create(options, 0);
        byte[] whole = runtime.Serialize(Simple());
        for (int length = 0; length < whole.Length; length++)
        {
          byte[] cut = new byte[length];
          Array.Copy(whole, cut, length);
          Assert.AreEqual(Read(runtime, typeof(CrSimple), cut), Read(compiled, typeof(CrSimple), cut), $"{Describe(options)}, {length} of {whole.Length} bytes");
        }
      }
    }

    [TestMethod]
    public void Structs_ByReference_ReadAsTheRuntimesPlans()
    {
      LtStructTests.LtsMover moving = new LtStructTests.LtsMover
      {
        Name = "m", Position = new LtStructTests.LtsVec { X = 1, Y = 2, Z = 3 }, Velocity = new LtStructTests.LtsVec { Z = -0.5f }, Target = new LtStructTests.LtsVec { Y = 4 },
        Path = new List<LtStructTests.LtsVec> { new LtStructTests.LtsVec { X = 1 } }, Points = new[] { new LtStructTests.LtsVec { Z = 9 } }
      };
      foreach (LtMsgPackOptions options in AllOptions())
      {
        LtMsgPackSerializer runtime = Create(options, -1);
        LtMsgPackSerializer compiled = Create(options, 0);
        byte[] data = runtime.Serialize(moving);
        Assert.AreEqual(Read(runtime, typeof(LtStructTests.LtsMover), data), Read(compiled, typeof(LtStructTests.LtsMover), data), Describe(options));
        byte[] root = runtime.Serialize(moving.Position);
        Assert.AreEqual(Read(runtime, typeof(LtStructTests.LtsVec), root), Read(compiled, typeof(LtStructTests.LtsVec), root), Describe(options) + ", as the root");
        Assert.AreEqual(Read(runtime, typeof(object), root), Read(compiled, typeof(object), root), Describe(options) + ", as an object");
        Assert.AreEqual("CompiledStructReader", ReaderOf(compiled, typeof(LtStructTests.LtsVec)), Describe(options));
      }
    }

    /// <summary>
    /// A serializer reads both layouts whatever its options write: the reader of the other layout is compiled when its first object is read.
    /// </summary>
    [TestMethod]
    public void TheOtherLayout_IsCompiledWhenItIsRead()
    {
      foreach (bool schema in new[] { false, true })
      {
        LtMsgPackOptions maps = new LtMsgPackOptions { UseInexedSchema = schema, ObjectLayout = ObjectLayout.Map };
        LtMsgPackOptions arrays = new LtMsgPackOptions { UseInexedSchema = schema, ObjectLayout = ObjectLayout.Array };
        byte[] asMap = Create(maps, -1).Serialize(Simple());
        byte[] asArray = Create(arrays, -1).Serialize(Simple());
        string expected = Read(Create(maps, -1), typeof(CrSimple), asMap);

        LtMsgPackSerializer ofMaps = Create(maps, 0);
        Assert.AreEqual(expected, Read(ofMaps, typeof(CrSimple), asMap));
        Assert.IsFalse(IsCompiled(ofMaps, typeof(CrSimple), false), "no array read yet");
        Assert.AreEqual(expected, Read(ofMaps, typeof(CrSimple), asArray));
        Assert.IsTrue(IsCompiled(ofMaps, typeof(CrSimple), false), "an array was read");
        Assert.AreEqual(expected, Read(ofMaps, typeof(CrSimple), asArray), "by the compiled reader");

        LtMsgPackSerializer ofArrays = Create(arrays, 0);
        Assert.AreEqual(expected, Read(ofArrays, typeof(CrSimple), asArray));
        Assert.IsFalse(IsCompiled(ofArrays, typeof(CrSimple), true), "no map read yet");
        Assert.AreEqual(expected, Read(ofArrays, typeof(CrSimple), asMap));
        Assert.AreEqual(schema, IsCompiled(ofArrays, typeof(CrSimple), true), "a map was read: compiled with the indexed schema, maps with names stay with the runtime's loop");
        Assert.AreEqual(expected, Read(ofArrays, typeof(CrSimple), asMap), "again");
      }
    }

    [TestMethod]
    public void SeveralThreads_WhileTheReaderIsCompiled()
    {
      foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
      {
        LtMsgPackOptions options = new LtMsgPackOptions { UseInexedSchema = true, ObjectLayout = layout };
        LtMsgPackSerializer runtime = Create(options, -1);
        byte[] data = runtime.Serialize(Simple());
        byte[] list = runtime.Serialize(new List<CrSimple> { Simple(), new CrSimple(), Simple() });
        string expected = Read(runtime, typeof(CrSimple), data);
        string expectedList = Read(runtime, typeof(List<CrSimple>), list);
        for (int round = 0; round < 5; round++)
        {
          LtMsgPackSerializer compiled = Create(options, 20);
          Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
          {
            if (t % 2 == 0)
              Assert.AreEqual(expected, Read(compiled, typeof(CrSimple), data));
            else
              Assert.AreEqual(expectedList, Read(compiled, typeof(List<CrSimple>), list));
          });
          Assert.AreEqual("CompiledObjectReader", WaitForReader(compiled, typeof(CrSimple)));
          Assert.AreEqual(expected, Read(compiled, typeof(CrSimple), data), "compiled");
        }
      }
    }

    [TestMethod]
    public void NotCompiled_WhereThePlansDoNotReadTheValues()
    {
      // A constructor that takes the values: they are collected first
      LtMsgPackSerializer compiled = Create(new LtMsgPackOptions { UseInexedSchema = false }, 0);
      CrImmutable read = compiled.Deserialize<CrImmutable>(compiled.Serialize(new CrImmutable(5, "five")));
      Assert.AreEqual(5, read.Number);
      Assert.AreEqual("five", read.Text);
      Assert.IsNull(ReaderOf(compiled, typeof(CrImmutable)));

      // No property of a type the compiled code reads itself
      compiled.Deserialize<LtStructTests.LtsMover>(compiled.Serialize(new LtStructTests.LtsMover { Position = new LtStructTests.LtsVec { X = 1 } }));
      Assert.AreEqual("CompiledObjectReader", ReaderOf(compiled, typeof(LtStructTests.LtsMover)), "it has a string");
      compiled.Deserialize<CrOnlyObjects>(compiled.Serialize(new CrOnlyObjects { Items = new List<int> { 1 }, Child = new CrOnlyObjects() }));
      Assert.IsNull(ReaderOf(compiled, typeof(CrOnlyObjects)));
    }

    public class CrOnlyObjects
    {
      public List<int> Items { get; set; }
      public CrOnlyObjects Child { get; set; }
    }

    [TestMethod]
    public void TheSourceGeneratorsCode_IsPreferred()
    {
      LtMsgPackSerializer generated = new LtMsgPackSerializer(new LtMsgPackOptions
      {
        UseInexedSchema = false, Contexts = new LtMsgPackContext[] { TestMsgPackContext.Default }, UseRegisteredContexts = false, CompileReadersAfter = 0
      });
      CrSimple read = generated.Deserialize<CrSimple>(generated.Serialize(Simple()));
      Assert.AreEqual(-70000, read.I);
      string reader = ReaderOf(generated, typeof(CrSimple));
      Assert.IsNotNull(reader);
      Assert.IsFalse(reader.StartsWith("Compiled", StringComparison.Ordinal), reader);
    }

    [TestMethod]
    public void NonPublicSettersAndFields_AreSet()
    {
      LtMsgPackOptions options = new LtMsgPackOptions
      {
        UseInexedSchema = false,
        StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.All, SerializedFields.Public), new FilterIgnoredAttribute(), new FilterStatic() }
      };
      LtMsgPackSerializer runtime = Create(options, -1);
      LtMsgPackSerializer compiled = Create(options, 0);
      CrHidden hidden = CrHidden.Create(5, "secret");
      hidden.Counter = 9;
      byte[] data = runtime.Serialize(hidden);
      Assert.AreEqual(Read(runtime, typeof(CrHidden), data), Read(compiled, typeof(CrHidden), data));
      CrHidden read = compiled.Deserialize<CrHidden>(data);
      Assert.AreEqual("5 secret 1 9 3", read.Describe());
      Assert.AreEqual("CompiledObjectReader", ReaderOf(compiled, typeof(CrHidden)));
    }

    public class CrHidden
    {
      private int Number { get; set; }
      internal string Text { get; private set; }
      public int Visible { get; set; }
      public int Counter;
      public int InitOnly { get; init; }

      public static CrHidden Create(int number, string text)
      {
        return new CrHidden { Number = number, Text = text, Visible = 1, InitOnly = 3 };
      }

      public string Describe()
      {
        return $"{Number} {Text} {Visible} {Counter} {InitOnly}";
      }
    }
  }
}
