using LsMsgPack;
using LtMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// LtMsgPack's typed plans for structs (boxed once, properties read and set in the box) and its default check for structs (StructDefault, the same decision as Core's ValueType.Equals).
  /// CrossLibraryTests compares the bytes and values with LsMsgPack (its corpus has structs too).
  /// </summary>
  [TestClass]
  public class LtStructTests
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

    private static readonly Type StructDefault = typeof(LtMsgPackSerializer).Assembly.GetType("LtMsgPack.Writing.StructDefault`1", true);

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

    [TestMethod]
    public void Structs_GetTypedPlans()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions() { UseRegisteredContexts = false });
      object serializer = typeof(LtMsgPackSerializer).GetField("_serializer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lt);
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
    public void Structs_RoundTrip()
    {
      foreach (bool schema in new[] { false, true })
      {
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        {
          LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, UseRegisteredContexts = false });
          string setting = $"schema {schema}, {layout}";

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

    [TestMethod]
    public void Structs_ReportDifferences()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, UseRegisteredContexts = false });
      byte[] data = lt.Serialize(new LtsWriterShape { P = new LtsWriterPoint { X = 4, Extra = 5 } });

      LtsReaderShape read = lt.Deserialize<LtsReaderShape>(data, out ReadDifferences differences);

      Assert.AreEqual(4, read.P.X);
      Assert.IsNotNull(differences);
      Assert.IsTrue(differences.Differences.Any(d => d.Name == "Extra"), differences.GenerateReport());
    }
  }
}
