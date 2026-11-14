using LsMsgPack;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// LsMsgPack's typed plans for structs (by reference, or boxed once with the properties read and set in the box) and its default check for structs (StructDefault, the same decision as Core's ValueType.Equals).
  /// CrossLibraryTests compares the bytes and values with LsMsgPack (its corpus has structs too).
  /// </summary>
  [TestClass]
  public class LsStructTests
  {
    public enum LtsKind : byte { None, Some }

    public struct LtsFloats { public float X { get; set; } public double Y { get; set; } }
    public struct LtsPadded { public byte A { get; set; } public long B { get; set; } }
    public struct LtsRefs { public string S { get; set; } public int[] Items { get; set; } }
    public struct LtsNested { public LtsFloats Inner { get; set; } public int? N { get; set; } public LtsKind Kind { get; set; } public bool Flag { get; set; } public char C { get; set; } }
    public struct LtsFramework { public DateTime D { get; set; } public Guid G { get; set; } public decimal M { get; set; } public TimeSpan T { get; set; } public DateTimeOffset O { get; set; } }
    public struct LtsTens { public int A { get; set; } public override bool Equals(object obj) { return obj is LtsTens other && other.A % 10 == A % 10; } public override int GetHashCode() { return A % 10; } }
    public struct LtsHoldsTens { public LtsTens Inner { get; set; } public int B { get; set; } }
    public struct LtsEquatable : IEquatable<LtsEquatable> { public int A { get; set; } public bool Equals(LtsEquatable other) { return true; } } // ValueType.Equals(object) still decides
    public struct LtsWithConstructor { public LtsWithConstructor() { X = 5; } public int X { get; set; } }
    public record struct LtsRecord(int A, string B);

    public struct LtsTree { public int V { get; set; } public LtsHolder Holder { get; set; } }
    public class LtsHolder { public LtsTree Inner { get; set; } public string Name { get; set; } }

    public class LtsWriterShape { public LtsWriterPoint P { get; set; } }
    public class LtsWriterPoint { public int X { get; set; } public int Extra { get; set; } }
    public class LtsReaderShape { public LtsReaderPoint P { get; set; } }
    public struct LtsReaderPoint { public int X { get; set; } }

    /// <summary>A vector as games use it, with a computed property (written, skipped when read)</summary>
    public struct LtsVec { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } public float Sum => X + Y + Z; }
    public class LtsMover { public string Name { get; set; } public LtsVec Position { get; set; } public LtsVec Velocity { get; set; } public LtsVec? Target { get; set; } public List<LtsVec> Path { get; set; } public LtsVec[] Points { get; set; } }

    private static readonly Type StructDefault = typeof(LsMsgPackSerializer).Assembly.GetType("LsMsgPack.Writing.StructDefault`1", true);

    private static bool IsDefault<T>(T value)
    {
      object[] args = { value }; // ref T
      return (bool)StructDefault.MakeGenericType(typeof(T)).GetMethod("IsDefault", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    }

    /// <summary>Not the boxed comparison of Core (the compiled one)</summary>
    private static bool IsCompiled<T>()
    {
      Delegate predicate = (Delegate)StructDefault.MakeGenericType(typeof(T)).GetField("Predicate", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
      return predicate.Method.Name != "IsDefaultBoxed";
    }

    private static void SameAsCore<T>(params T[] values)
    {
      for (int t = 0; t < values.Length; t++)
        Assert.AreEqual(Activator.CreateInstance(typeof(T)).Equals(values[t]), IsDefault(values[t]), $"{typeof(T).Name} #{t}"); // as FilterDefaultValues compares
    }

    [TestMethod]
    public void DefaultCheck_SameAsValueTypeEquals()
    {
      SameAsCore(default(LtsFloats), new LtsFloats { X = -0f }, new LtsFloats { Y = -0.0 }, new LtsFloats { X = float.NaN }, new LtsFloats { Y = double.NaN }, new LtsFloats { X = 1 }, new LtsFloats { Y = double.Epsilon });
      SameAsCore(default(LtsPadded), new LtsPadded { A = 1 }, new LtsPadded { B = -1 });
      SameAsCore(default(LtsRefs), new LtsRefs { S = "" }, new LtsRefs { Items = new int[0] });
      SameAsCore(default(LtsNested), new LtsNested { N = 0 }, new LtsNested { Inner = new LtsFloats { X = -0f } }, new LtsNested { Inner = new LtsFloats { Y = 2 } }, new LtsNested { Kind = LtsKind.Some }, new LtsNested { Flag = true }, new LtsNested { C = 'c' });
      SameAsCore(default(LtsFramework), new LtsFramework { M = 0.00m }, new LtsFramework { M = 0.01m }, new LtsFramework { D = new DateTime(0, DateTimeKind.Local) }, new LtsFramework { D = new DateTime(1) },
        new LtsFramework { G = Guid.NewGuid() }, new LtsFramework { T = TimeSpan.FromTicks(-1) }, new LtsFramework { O = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });
      SameAsCore(default(LtsTens), new LtsTens { A = 10 }, new LtsTens { A = 11 });
      SameAsCore(default(LtsHoldsTens), new LtsHoldsTens { Inner = new LtsTens { A = 20 } }, new LtsHoldsTens { Inner = new LtsTens { A = 21 } }, new LtsHoldsTens { B = 1 });
      SameAsCore(default(LtsEquatable), new LtsEquatable { A = 1 });
      SameAsCore(default(LtsWithConstructor), new LtsWithConstructor(), new LtsWithConstructor { X = 0 });
      SameAsCore(default(LtsRecord), new LtsRecord(0, ""), new LtsRecord(1, null));

      // Compiled where the struct has no Equals or constructor of its own (then Core's way)
      Assert.IsTrue(IsCompiled<LtsFloats>());
      Assert.IsTrue(IsCompiled<LtsNested>());
      Assert.IsTrue(IsCompiled<LtsFramework>());
      Assert.IsTrue(IsCompiled<LtsHoldsTens>());
      Assert.IsFalse(IsCompiled<LtsTens>());
      Assert.IsFalse(IsCompiled<LtsWithConstructor>());
      Assert.IsFalse(IsCompiled<LtsRecord>());
    }

    /// <summary>
    /// The source generator's default check (GeneratedStructCode.IsDefault), where it writes one, says what Core says.
    /// </summary>
    private static void GeneratedSameAsCore<T>(bool generated, params T[] values)
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions() { UseRegisteredContexts = false, Contexts = new LsMsgPack.LsMsgPackContext[] { TestMsgPackContext.Default } });
      object serializer = typeof(LsMsgPackSerializer).GetField("_serializer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lt);
      LsMsgPack.Generated.GeneratedStructCode<T> code = (LsMsgPack.Generated.GeneratedStructCode<T>)serializer.GetType().GetMethod("GeneratedCode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(serializer, new object[] { typeof(T) });
      LsMsgPack.Generated.GeneratedCapabilities capabilities = (LsMsgPack.Generated.GeneratedCapabilities)typeof(LsMsgPack.Generated.GeneratedObjectCode).GetField("Capabilities", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(code);
      Assert.AreEqual(generated, (capabilities & LsMsgPack.Generated.GeneratedCapabilities.IsDefault) != 0, typeof(T).Name + ": a generated default check");
      for (int t = 0; generated && t < values.Length; t++)
      {
        T value = values[t];
        Assert.AreEqual(Activator.CreateInstance(typeof(T)).Equals(values[t]), code.IsDefault(ref value), $"{typeof(T).Name} #{t}");
      }
    }

    [TestMethod]
    public void DefaultCheck_Generated_SameAsValueTypeEquals()
    {
      GeneratedSameAsCore(true, default(LtsFloats), new LtsFloats { X = -0f }, new LtsFloats { Y = -0.0 }, new LtsFloats { X = float.NaN }, new LtsFloats { Y = double.NaN }, new LtsFloats { X = 1 }, new LtsFloats { Y = double.Epsilon });
      GeneratedSameAsCore(true, default(LtsPadded), new LtsPadded { A = 1 }, new LtsPadded { B = -1 });
      GeneratedSameAsCore(true, default(LtsRefs), new LtsRefs { S = "" }, new LtsRefs { Items = new int[0] });
      GeneratedSameAsCore(true, default(LtsNested), new LtsNested { N = 0 }, new LtsNested { Inner = new LtsFloats { X = -0f } }, new LtsNested { Inner = new LtsFloats { Y = 2 } }, new LtsNested { Kind = LtsKind.Some }, new LtsNested { Flag = true }, new LtsNested { C = 'c' });
      GeneratedSameAsCore(true, default(LtsFramework), new LtsFramework { M = 0.00m }, new LtsFramework { M = 0.01m }, new LtsFramework { D = new DateTime(0, DateTimeKind.Local) }, new LtsFramework { D = new DateTime(1) },
        new LtsFramework { G = Guid.NewGuid() }, new LtsFramework { T = TimeSpan.FromTicks(-1) }, new LtsFramework { O = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });
      GeneratedSameAsCore(true, default(LtsEquatable), new LtsEquatable { A = 1 });
      GeneratedSameAsCore(true, default(LtsRecord), new LtsRecord(0, ""), new LtsRecord(1, null), new LtsRecord(0, null));
      GeneratedSameAsCore(true, default(LtsVec), new LtsVec { Z = -0f }, new LtsVec { X = 1, Y = -1 });
      GeneratedSameAsCore(true, default(LtsTree), new LtsTree { Holder = new LtsHolder() });
      GeneratedSameAsCore<LtsTens>(false); // its own Equals: compared as Core does
      GeneratedSameAsCore<LtsHoldsTens>(false); // a field with its own Equals
      GeneratedSameAsCore<LtsWithConstructor>(false); // Core's default is what its constructor makes
    }

    [TestMethod]
    public void Structs_GetTypedPlans()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions() { UseRegisteredContexts = false });
      object serializer = typeof(LsMsgPackSerializer).GetField("_serializer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lt);
      MethodInfo handler = serializer.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m => m.Name == "Handler" && m.IsGenericMethodDefinition);
      MethodInfo reader = serializer.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m => m.Name == "Reader" && m.IsGenericMethodDefinition);

      foreach (Type type in new[] { typeof(LtsFloats), typeof(LtsNested), typeof(LtsRecord) })
      {
        Assert.AreEqual("ObjectHandler`1", handler.MakeGenericMethod(type).Invoke(serializer, new object[] { null }).GetType().Name, type.Name);
        Assert.AreEqual("ObjectReader`1", reader.MakeGenericMethod(type).Invoke(serializer, null).GetType().Name, type.Name);
      }
      Assert.AreEqual("NullableHandler`1", handler.MakeGenericMethod(typeof(LtsFloats?)).Invoke(serializer, new object[] { null }).GetType().Name);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Structs_ByRefPlans(bool generated)
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions() { UseRegisteredContexts = false, Contexts = generated ? new LsMsgPack.LsMsgPackContext[] { TestMsgPackContext.Default } : null });
      object serializer = typeof(LsMsgPackSerializer).GetField("_serializer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lt);
      MethodInfo plan = serializer.GetType().GetMethod("Plan", BindingFlags.Instance | BindingFlags.NonPublic);
      MethodInfo readPlan = serializer.GetType().GetMethod("GetReadPlan", BindingFlags.Instance | BindingFlags.NonPublic);
      bool WritesByRef(Type type, bool names)
      {
        object p = plan.Invoke(serializer, new object[] { type, names });
        Assert.AreEqual(generated, p.GetType().GetField("GeneratedPlan", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p) != null, type.Name + ": the generated writer");
        return (bool)p.GetType().GetField("ByRef", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p);
      }
      bool ReadsByRef(Type type) { object p = readPlan.Invoke(serializer, new object[] { type }); return (bool)p.GetType().GetField("ByRef", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p); }

      foreach (Type type in new[] { typeof(LtsFloats), typeof(LtsNested), typeof(LtsVec), typeof(LtsTree) })
      {
        Assert.IsTrue(WritesByRef(type, true), type.Name);
        Assert.IsTrue(WritesByRef(type, false), type.Name);
        Assert.IsTrue(ReadsByRef(type), type.Name);
      }
      Assert.IsTrue(WritesByRef(typeof(LtsWithConstructor), false));
      Assert.IsFalse(ReadsByRef(typeof(LtsWithConstructor)), "its constructor makes the instance (boxed)");
      Assert.IsFalse(WritesByRef(typeof(LtsMover), false), "a class");
      Assert.IsFalse(ReadsByRef(typeof(LtsMover)), "a class");
    }

    /// <summary>
    /// Struct values are written and read by reference: nothing is allocated for them (into a reused stream nothing at all), in every layout and id mode.
    /// A struct as the root value is not boxed either (<see cref="StructRoot_NoAllocation"/>).
    /// <para>Only measured when the code is optimized (Release, ReleaseKeepTrack): the generic code boxes values behind type checks, and only the optimizing JIT removes those boxes. The round trips are checked in every configuration.</para>
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Structs_NoAllocation(bool generated)
    {
      LtsMover moving = new LtsMover { Name = "m", Position = new LtsVec { X = 1, Y = 2, Z = 3 }, Velocity = new LtsVec { Z = -0.5f }, Target = new LtsVec { Y = 4 }, Path = new List<LtsVec> { new LtsVec { X = 1 }, new LtsVec { Y = 2 } }, Points = new[] { new LtsVec { Z = 9 } } };
      LtsMover resting = new LtsMover { Name = "m", Target = default(LtsVec), Path = new List<LtsVec> { default(LtsVec), default(LtsVec) }, Points = new[] { default(LtsVec) } };
      MemoryStream stream = new MemoryStream(1024);
      bool optimized = IsOptimized(typeof(LsMsgPackSerializer)) && IsOptimized(typeof(LsStructTests)); // the generated code is in this assembly
      foreach (bool schema in new[] { false, true })
      {
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          foreach (bool trim in new[] { false, true })
          {
            LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, TrimTrailingNulls = trim, UseRegisteredContexts = false,
              Contexts = generated ? new LsMsgPack.LsMsgPackContext[] { TestMsgPackContext.Default } : null });
            string setting = $"schema {schema}, {layout}, trim {trim}, generated {generated}";

            for (int t = 0; t < 3; t++) // warm up (plans, contexts, the stream's capacity)
            {
              stream.SetLength(0);
              lt.Serialize(moving, stream);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            stream.SetLength(0);
            lt.Serialize(moving, stream);
            long written = GC.GetAllocatedBytesForCurrentThread() - before;
            if (optimized)
              Assert.AreEqual(0L, written, setting + ": writing");

            // Reading the vectors costs nothing: the same allocations as when they are all left out (written as nil, or default elements)
            byte[] movingData = lt.Serialize(moving);
            byte[] restingData = lt.Serialize(resting);
            for (int t = 0; t < 3; t++)
            {
              lt.Deserialize<LtsMover>(movingData);
              lt.Deserialize<LtsMover>(restingData);
            }
            before = GC.GetAllocatedBytesForCurrentThread();
            LtsMover read = lt.Deserialize<LtsMover>(movingData);
            long readMoving = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            lt.Deserialize<LtsMover>(restingData);
            long readResting = GC.GetAllocatedBytesForCurrentThread() - before;
            if (optimized)
              Assert.AreEqual(readResting, readMoving, setting + ": reading the vectors");

            Assert.AreEqual(moving.Position, read.Position, setting);
            Assert.AreEqual(moving.Velocity, read.Velocity, setting);
            Assert.AreEqual(moving.Target, read.Target, setting);
            CollectionAssert.AreEqual(moving.Path, read.Path, setting);
            CollectionAssert.AreEqual(moving.Points, read.Points, setting);
          }
        }
      }
    }

    public struct LtsFieldVec { public float X; public float Y; public float Z; }
    public class LtsFieldMover { public string Name; public LtsFieldVec Position; public System.Numerics.Vector3 Velocity; public int Count; public double Heading; public List<LtsFieldVec> Path; public System.Numerics.Vector3[] Points; }

    /// <summary>
    /// Fields are read and written by typed delegates compiled when the plan is built (structs by reference): nothing is boxed, as for properties.
    /// The reflection path (BoxedPropWriter/BoxedPropReader) would box every int, float and struct.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FieldStructs_NoAllocation(bool generated)
    {
      LtsFieldMover moving = new LtsFieldMover { Name = "m", Position = new LtsFieldVec { X = 1, Y = 2, Z = 3 }, Velocity = new System.Numerics.Vector3(4, 5, 6), Count = 7, Heading = 0.5,
        Path = new List<LtsFieldVec> { new LtsFieldVec { X = 1 }, new LtsFieldVec { Y = 2 } }, Points = new[] { new System.Numerics.Vector3(7, 8, 9) } };
      // The same objects with default values: the elements are written all the same (the nested objects of the schema cost the same), their fields are left out
      LtsFieldMover resting = new LtsFieldMover { Name = "m", Position = moving.Position, Velocity = moving.Velocity, Count = 7, Heading = 0.5,
        Path = new List<LtsFieldVec> { default(LtsFieldVec), default(LtsFieldVec) }, Points = new[] { default(System.Numerics.Vector3) } };
      MemoryStream stream = new MemoryStream(1024);
      bool optimized = IsOptimized(typeof(LsMsgPackSerializer)) && IsOptimized(typeof(LsStructTests));
      foreach (bool schema in new[] { false, true })
      {
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, UseRegisteredContexts = false,
            Contexts = generated ? new LsMsgPack.LsMsgPackContext[] { TestMsgPackContext.Default } : null,
            StaticFilters = new LsMsgPack.TypeResolving.Interfaces.IMsgPackPropertyIncludeStatically[] { new LsMsgPack.TypeResolving.Filters.FilterNonSettable(LsMsgPack.TypeResolving.Filters.GetOnlyProperties.All,
              LsMsgPack.TypeResolving.Filters.NonPublicProperties.None, LsMsgPack.TypeResolving.Filters.SerializedFields.Public), new LsMsgPack.TypeResolving.Filters.FilterIgnoredAttribute(), new LsMsgPack.TypeResolving.Filters.FilterStatic() } });
          string setting = $"schema {schema}, {layout}, generated {generated}";

          for (int t = 0; t < 3; t++)
          {
            stream.SetLength(0);
            lt.Serialize(moving, stream);
          }
          long before = GC.GetAllocatedBytesForCurrentThread();
          stream.SetLength(0);
          lt.Serialize(moving, stream);
          long written = GC.GetAllocatedBytesForCurrentThread() - before;
          if (optimized)
            Assert.AreEqual(0L, written, setting + ": writing");

          byte[] movingData = lt.Serialize(moving);
          byte[] restingData = lt.Serialize(resting);
          for (int t = 0; t < 3; t++)
          {
            lt.Deserialize<LtsFieldMover>(movingData);
            lt.Deserialize<LtsFieldMover>(restingData);
          }
          before = GC.GetAllocatedBytesForCurrentThread();
          LtsFieldMover read = lt.Deserialize<LtsFieldMover>(movingData);
          long readMoving = GC.GetAllocatedBytesForCurrentThread() - before;
          before = GC.GetAllocatedBytesForCurrentThread();
          lt.Deserialize<LtsFieldMover>(restingData);
          long readResting = GC.GetAllocatedBytesForCurrentThread() - before;
          if (optimized)
            Assert.AreEqual(readResting, readMoving, setting + ": reading the values");

          Assert.AreEqual(moving.Position, read.Position, setting);
          Assert.AreEqual(moving.Velocity, read.Velocity, setting);
          Assert.AreEqual(7, read.Count, setting);
          Assert.AreEqual(0.5, read.Heading, setting);
          CollectionAssert.AreEqual(moving.Path, read.Path, setting);
          CollectionAssert.AreEqual(moving.Points, read.Points, setting);
        }
      }
    }

    public class LtsVectorHolder { public System.Numerics.Vector3 V { get; set; } public System.Numerics.Quaternion Q { get; set; } public int N { get; set; } = 1; }

    /// <summary>
    /// The default check of a framework struct with its own typed Equals (StructDefault.TypedEquals) decides as Core's boxed Equals: the same values left out.
    /// </summary>
    [TestMethod]
    public void DefaultCheck_FrameworkStructs_SameAsCore()
    {
      float nan = float.NaN;
      System.Numerics.Vector3[] vectors = { default(System.Numerics.Vector3), new System.Numerics.Vector3(-0f, 0f, -0f), new System.Numerics.Vector3(nan, 0, 0), new System.Numerics.Vector3(0, 0, 1e-45f), System.Numerics.Vector3.One };
      System.Numerics.Quaternion[] quaternions = { default(System.Numerics.Quaternion), new System.Numerics.Quaternion(-0f, 0, 0, 0), new System.Numerics.Quaternion(0, nan, 0, 0), System.Numerics.Quaternion.Identity };
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };
      for (int v = 0; v < vectors.Length; v++)
      {
        for (int q = 0; q < quaternions.Length; q++)
        {
          LtsVectorHolder holder = new LtsVectorHolder() { V = vectors[v], Q = quaternions[q] };
          CollectionAssert.AreEqual(Serializers.Debug.Serialize(holder, settings), Serializers.Ls.Serialize(holder, settings), $"V {vectors[v]}, Q {quaternions[q]}");
        }
      }
    }

    private static bool IsOptimized(Type type)
    {
      DebuggableAttribute debuggable = type.Assembly.GetCustomAttribute<DebuggableAttribute>();
      return debuggable == null || !debuggable.IsJITOptimizerDisabled;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Structs_RoundTrip(bool generated)
    {
      foreach (bool schema in new[] { false, true })
      {
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, UseRegisteredContexts = false,
            Contexts = generated ? new LsMsgPack.LsMsgPackContext[] { TestMsgPackContext.Default } : null });
          string setting = $"schema {schema}, {layout}, generated {generated}";

          // A struct holding a class holding the same struct type: each value has its own box
          LtsTree tree = new LtsTree { V = 1, Holder = new LtsHolder { Name = "h", Inner = new LtsTree { V = 2, Holder = new LtsHolder { Name = "leaf" } } } };
          LtsTree read = lt.Deserialize<LtsTree>(lt.Serialize(tree));
          Assert.AreEqual(1, read.V, setting);
          Assert.AreEqual("h", read.Holder.Name, setting);
          Assert.AreEqual(2, read.Holder.Inner.V, setting);
          Assert.AreEqual("leaf", read.Holder.Inner.Holder.Name, setting);
          Assert.IsNull(read.Holder.Inner.Holder.Inner.Holder, setting);

          LtsNested nested = new LtsNested { Inner = new LtsFloats { X = -2.5f, Y = double.NaN }, N = 0, Kind = LtsKind.Some, C = 'q' };
          Assert.AreEqual(nested, lt.Deserialize<LtsNested>(lt.Serialize(nested)), setting);
          Assert.AreEqual(new LtsRecord(3, "three"), lt.Deserialize<LtsRecord>(lt.Serialize(new LtsRecord(3, "three"))), setting);

          List<LtsFloats?> nullables = new List<LtsFloats?> { new LtsFloats { X = 1 }, null, default(LtsFloats) };
          CollectionAssert.AreEqual(nullables, lt.Deserialize<List<LtsFloats?>>(lt.Serialize(nullables)), setting);
          Assert.AreEqual(default(LtsFloats), lt.Deserialize<LtsFloats>(lt.Serialize<object>(null)), setting + ": nil is the default");
        }
      }
    }

    /// <summary>A root struct whose computed property serializes another value of its type, and whose setter reads one: calls nested in a call with the same root type</summary>
    public struct LtsReentrant
    {
      public static LsMsgPackSerializer Nester;
      public static int LastInner = -1;
      public int A { get; set; }
      public string Echo
      {
        get { return A > 0 ? Convert.ToBase64String(Nester.Serialize(new LtsReentrant { A = A - 1 })) : null; }
        set { if (value != null) LastInner = Nester.Deserialize<LtsReentrant>(Convert.FromBase64String(value)).A; }
      }
    }

    /// <summary>
    /// A struct as the root value of the typed calls is held in a reused slot instead of a box (RootSlot): writing and reading it allocates nothing, in every layout and id mode, also through the stream writer and reader.
    /// <para>Measured only when the code is optimized, as <see cref="Structs_NoAllocation"/>.</para>
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StructRoot_NoAllocation(bool generated)
    {
      LtsVec vec = new LtsVec { X = 1, Y = -2.5f, Z = 3 };
      MemoryStream stream = new MemoryStream(1024);
      bool optimized = IsOptimized(typeof(LsMsgPackSerializer)) && IsOptimized(typeof(LsStructTests));
      foreach (bool schema in new[] { false, true })
      {
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, UseRegisteredContexts = false,
            Contexts = generated ? new LsMsgPack.LsMsgPackContext[] { TestMsgPackContext.Default } : null });
          string setting = $"schema {schema}, {layout}, generated {generated}";
          byte[] data = lt.Serialize(vec);
          CollectionAssert.AreEqual(lt.Serialize((object)vec, typeof(LtsVec)), data, setting + ": the same bytes as the boxed call");

          for (int t = 0; t < 3; t++) // warm up
          {
            stream.SetLength(0);
            lt.Serialize(vec, stream);
            lt.Deserialize<LtsVec>(data);
          }
          long before = GC.GetAllocatedBytesForCurrentThread();
          stream.SetLength(0);
          lt.Serialize(vec, stream);
          long written = GC.GetAllocatedBytesForCurrentThread() - before;
          before = GC.GetAllocatedBytesForCurrentThread();
          LtsVec read = lt.Deserialize<LtsVec>(data);
          long readBytes = GC.GetAllocatedBytesForCurrentThread() - before;
          Assert.AreEqual(vec, read, setting);
          if (optimized)
          {
            Assert.AreEqual(0L, written, setting + ": writing");
            Assert.AreEqual(0L, readBytes, setting + ": reading");
          }

          // The stream writer and reader: one value each, the same as the serializer's
          stream.SetLength(0);
          using (LsMsgPack.Streaming.MsgPackStreamWriter writer = new LsMsgPack.Streaming.MsgPackStreamWriter(stream, lt, LsMsgPack.Streaming.MsgPackStreamSchemas.PerObject, true))
            writer.WriteObject(vec);
          CollectionAssert.AreEqual(data, stream.ToArray(), setting + ": stream writer");
          using (LsMsgPack.Streaming.MsgPackStreamReader reader = new LsMsgPack.Streaming.MsgPackStreamReader(new MemoryStream(data), lt))
            Assert.AreEqual(vec, reader.ReadObject<LtsVec>(), setting + ": stream reader");
        }
      }
    }

    /// <summary>
    /// The typed calls with a struct root (in a slot, not boxed) give the same bytes and values as the boxed calls on every path: nullable structs (still boxed), structs with references,
    /// alternating root types, calls nested in a call of the same root type, a head's body in a stream, the paths that report differences or collect errors, nil, and a root value that cannot be converted (fails as before).
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StructRoot_SameAsBoxed(bool generated)
    {
      foreach (bool schema in new[] { false, true })
      {
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          LsMsgPackOptions options = new LsMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, UseRegisteredContexts = false,
            Contexts = generated ? new LsMsgPack.LsMsgPackContext[] { TestMsgPackContext.Default } : null };
          LsMsgPackSerializer lt = new LsMsgPackSerializer(options);
          string setting = $"schema {schema}, {layout}, generated {generated}";

          LtsRefs refs = new LtsRefs { S = "s", Items = new[] { 1, 2, 3 } };
          byte[] refsData = lt.Serialize(refs);
          CollectionAssert.AreEqual(lt.Serialize((object)refs, typeof(LtsRefs)), refsData, setting);
          LtsRefs refsRead = lt.Deserialize<LtsRefs>(refsData);
          Assert.AreEqual("s", refsRead.S, setting);
          CollectionAssert.AreEqual(refs.Items, refsRead.Items, setting);
          refsRead = lt.Deserialize<LtsRefs>(new MemoryStream(refsData));
          CollectionAssert.AreEqual(refs.Items, refsRead.Items, setting + ": from a stream");
          byte[] padded = new byte[refsData.Length + 4];
          Array.Copy(refsData, 0, padded, 2, refsData.Length);
          Assert.AreEqual("s", lt.Deserialize<LtsRefs>(padded, 2, refsData.Length).S, setting + ": offset and count");

          // Alternating struct root types: each call makes its own slot, the values stay apart
          LtsFloats floats = new LtsFloats { X = 7, Y = 8 };
          LtsVec vec = new LtsVec { X = 4 };
          for (int t = 0; t < 2; t++)
          {
            Assert.AreEqual(floats, lt.Deserialize<LtsFloats>(lt.Serialize(floats)), setting);
            Assert.AreEqual(vec, lt.Deserialize<LtsVec>(lt.Serialize(vec)), setting);
            Assert.AreEqual(42, lt.Deserialize<int>(lt.Serialize(42)), setting + ": a number as the root");
          }

          // Nullable structs stay boxed (the box of null is null)
          LtsFloats? some = floats;
          Assert.AreEqual(floats, lt.Deserialize<LtsFloats?>(lt.Serialize(some)), setting);
          Assert.IsNull(lt.Deserialize<LtsFloats?>(lt.Serialize<LtsFloats?>(null)), setting);
          Assert.AreEqual(default(LtsVec), lt.Deserialize<LtsVec>(lt.Serialize<object>(null)), setting + ": nil is the default");

          // Nested calls of the same root type while the outer one holds its slot
          LtsReentrant.Nester = lt;
          LtsReentrant.LastInner = -1;
          LtsReentrant reentrant = new LtsReentrant { A = 2 };
          byte[] reentrantData = lt.Serialize(reentrant);
          CollectionAssert.AreEqual(lt.Serialize((object)reentrant, typeof(LtsReentrant)), reentrantData, setting + ": nested writes");
          Assert.AreEqual(2, lt.Deserialize<LtsReentrant>(reentrantData).A, setting + ": nested reads");
          Assert.AreEqual(1, LtsReentrant.LastInner, setting + ": the nested read");

          // The paths that report differences or collect errors read boxed: the same values
          LtsRefs reported = lt.Deserialize<LtsRefs>(refsData, out ReadDifferences differences);
          Assert.IsNull(differences, setting);
          CollectionAssert.AreEqual(refs.Items, reported.Items, setting);
          LsMsgPackOptions collecting = options.Clone();
          collecting.ReadErrors = ReadErrorHandling.ReportAndContinue;
          CollectionAssert.AreEqual(refs.Items, new LsMsgPackSerializer(collecting).Deserialize<LtsRefs>(refsData).Items, setting + ": collecting errors");

          // A root value that cannot be read as the type fails as the boxed call with the cast does
          byte[] text = lt.Serialize("text");
          Type boxedFails = Failure(() => { LtsVec unused = (LtsVec)lt.Deserialize(typeof(LtsVec), text); });
          Assert.IsNotNull(boxedFails, setting);
          Assert.AreEqual(boxedFails, Failure(() => lt.Deserialize<LtsVec>(text)), setting + ": an unconverted root");
          Assert.AreEqual(Failure(() => { int unused = (int)lt.Deserialize(typeof(int), text); }), Failure(() => lt.Deserialize<int>(text)), setting + ": an unconverted number");
          Assert.AreEqual(vec, lt.Deserialize<LtsVec>(lt.Serialize(vec)), setting + ": after a failed call");
        }
      }

      // The body of a head: values of a struct in a list, written and read as the list's elements
      LsMsgPackSerializer heads = new LsMsgPackSerializer(new LsMsgPackOptions() { UseRegisteredContexts = false, Contexts = generated ? new LsMsgPack.LsMsgPackContext[] { TestMsgPackContext.Default } : null });
      List<LtsVec> list = new List<LtsVec> { new LtsVec { X = 1 }, new LtsVec { Y = 2 }, default(LtsVec) };
      MemoryStream stream = new MemoryStream();
      using (LsMsgPack.Streaming.MsgPackStreamWriter writer = new LsMsgPack.Streaming.MsgPackStreamWriter(stream, heads, LsMsgPack.Streaming.MsgPackStreamSchemas.PerObject, true))
      {
        writer.WriteHead<List<LtsVec>>();
        writer.WriteArrayHeader(list.Count);
        for (int i = 0; i < list.Count; i++)
          writer.WriteObject(list[i]);
      }
      CollectionAssert.AreEqual(list, heads.Deserialize<List<LtsVec>>(stream.ToArray()), "head and body read as one payload");
      using (LsMsgPack.Streaming.MsgPackStreamReader reader = new LsMsgPack.Streaming.MsgPackStreamReader(new MemoryStream(stream.ToArray()), heads))
      {
        reader.ReadHead<List<LtsVec>>();
        Assert.IsTrue(reader.Read());
        List<LtsVec> read = new List<LtsVec>();
        while (!reader.AtEnd)
          read.Add(reader.ReadObject<LtsVec>());
        CollectionAssert.AreEqual(list, read, "the body read per element");
      }
    }

    private static Type Failure(Action action)
    {
      try
      {
        action();
        return null;
      }
      catch (Exception ex)
      {
        return ex.GetType();
      }
    }

    [TestMethod]
    public void Structs_ReportDifferences()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, UseRegisteredContexts = false });
      byte[] data = lt.Serialize(new LtsWriterShape { P = new LtsWriterPoint { X = 4, Extra = 5 } });

      LtsReaderShape read = lt.Deserialize<LtsReaderShape>(data, out ReadDifferences differences);

      Assert.AreEqual(4, read.P.X);
      Assert.IsNotNull(differences);
      Assert.IsTrue(differences.Differences.Any(d => d.Name == "Extra"), differences.GenerateReport());
    }
  }
}
