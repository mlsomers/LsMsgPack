using LsMsgPack;
using LtMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// LtMsgPack's typed readers and handlers of dictionaries, sets and collections declared as interfaces: what is not simply their format is read as LsMsgPack does, from the start of the value.
  /// CrossLibraryTests compares the bytes and values with LsMsgPack (its corpus has them too).
  /// </summary>
  [TestClass]
  public class LtCollectionTests
  {
    public abstract class LcShape { public int Id { get; set; } }
    public class LcCircle : LcShape { public double Radius { get; set; } }
    public class LcSquare : LcShape { public double Side { get; set; } }
    public class LcTriangle : LcShape { public double Base { get; set; } }
    public class LcDrawing { public List<LcShape> Shapes { get; set; } public IList<LcShape> Interfaced { get; set; } public Dictionary<string, LcShape> ByName { get; set; } }
    public class LcHolder { public IList<int> List { get; set; } public IDictionary<string, int> Map { get; set; } public object Any { get; set; } }

    private static readonly LtMsgPackSerializer NoSchema = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false });
    private static readonly LtMsgPackSerializer WithSchema = new LtMsgPackSerializer();

    private static byte[] Text(string text)
    {
      byte[] bytes = Encoding.UTF8.GetBytes(text);
      return new[] { (byte)(0xA0 + bytes.Length) }.Concat(bytes).ToArray();
    }

    private static byte[] Bytes(params object[] parts)
    {
      List<byte> all = new List<byte>();
      for (int t = 0; t < parts.Length; t++)
      {
        if (parts[t] is string text)
          all.AddRange(Text(text));
        else
          all.Add(Convert.ToByte(parts[t]));
      }
      return all.ToArray();
    }

    [TestMethod]
    public void Dictionary_NotSimplyEntries_ReadAsLsMsgPack()
    {
      Assert.Throws<ArgumentNullException>(() => NoSchema.Deserialize<Dictionary<string, int>>(Bytes(0x81, "a", 0xC0)), "nil for a value type");
      Assert.IsNull(NoSchema.Deserialize<Dictionary<string, int?>>(Bytes(0x81, "a", 0xC0))["a"]);
      Assert.Throws<ArgumentException>(() => NoSchema.Deserialize<Dictionary<string, int>>(Bytes(0x82, "a", 1, "a", 2)), "a key twice");
      Assert.AreEqual(2, NoSchema.Deserialize<Dictionary<string, int>>(Bytes(0x81, 1, 2))["1"], "a number key into string keys, without the schema");
      Assert.Throws<MsgPackException>(() => WithSchema.Deserialize<Dictionary<string, int>>(Bytes(0x80, 0x81, 1, 2)), "the same with the schema: indexes of property names");
      Assert.AreEqual(1, NoSchema.Deserialize<Dictionary<int, int>>(Bytes(0x81, "5", 1))[5], "a key in another format");
      Assert.AreEqual(1, NoSchema.Deserialize<Dictionary<string, int>>(Bytes(0x81, 0xD9, 2, 0x61, 0x62, 1))["ab"], "a key in a longer form");
    }

    [TestMethod]
    public void Dictionary_WrappedInTheData_NoEntries()
    {
      byte[] wrapped = Bytes(0x82, "", "Dictionary<String, Int32>", "@", 0x81, "a", 1);
      Assert.AreEqual(1, NoSchema.Deserialize<Dictionary<string, int>>(wrapped)["a"]);
      Assert.AreEqual(1, NoSchema.Deserialize<IDictionary<string, int>>(wrapped)["a"]);
      byte[] reservedInContent = Bytes(0x82, "", "Dictionary<String, Int32>", "@", 0x82, "", 1, "@", 2); // the content of a dictionary is always entries
      Assert.AreEqual(2, NoSchema.Deserialize<IDictionary<string, int>>(reservedInContent)["@"]);
      Assert.AreEqual(1, NoSchema.Deserialize<IDictionary<string, int>>(reservedInContent)[""]);
    }

    [TestMethod]
    public void Interfaces_PlainData_ConcreteType()
    {
      Assert.IsInstanceOfType(NoSchema.Deserialize<IList<int>>(Bytes(0x92, 1, 2)), typeof(List<int>));
      Assert.IsInstanceOfType(NoSchema.Deserialize<IEnumerable<int>>(Bytes(0x92, 1, 2)), typeof(List<int>));
      Assert.IsInstanceOfType(NoSchema.Deserialize<ISet<int>>(Bytes(0x93, 1, 1, 2)), typeof(HashSet<int>));
      Assert.AreEqual(2, NoSchema.Deserialize<ISet<int>>(Bytes(0x93, 1, 1, 2)).Count, "a set keeps one of each");
      Assert.IsInstanceOfType(NoSchema.Deserialize<IDictionary<string, int>>(Bytes(0x81, "a", 1)), typeof(Dictionary<string, int>));
      Assert.IsNull(NoSchema.Deserialize<IList<int>>(Bytes(0xC0)));
    }

    [TestMethod]
    public void Interfaces_RuntimeTypeInTheData_KeptAndChecked()
    {
      for (int t = 0; t < 2; t++)
      {
        LtMsgPackSerializer serializer = t == 0 ? NoSchema : WithSchema;
        Assert.IsInstanceOfType(serializer.Deserialize<LcHolder>(serializer.Serialize(new LcHolder { List = new[] { 1, 2 } })).List, typeof(int[]));
        Assert.IsInstanceOfType(serializer.Deserialize<LcHolder>(serializer.Serialize(new LcHolder { List = new List<int> { 1 } })).List, typeof(List<int>));
        Assert.IsInstanceOfType(serializer.Deserialize<LcHolder>(serializer.Serialize(new LcHolder { Map = new SortedDictionary<string, int> { { "a", 1 } } })).Map, typeof(SortedDictionary<string, int>));
      }

      Assert.Throws<MsgPackException>(() => NoSchema.Deserialize<IList<int>>(Bytes(0x82, "", "HashSet<Int32>", "@", 0x91, 1)), "not assignable");
      Assert.Throws<MsgPackException>(() => NoSchema.Deserialize<IList<int>>(Bytes(0x82, "")), "cut off");
    }

    private sealed class RefuseAll : LsMsgPack.TypeResolving.Interfaces.IMsgPackTypeGuard
    {
      public bool IsAllowed(Type type, Type assignedTo, LsMsgPack.Meta.FullPropertyInfo prop, MsgPackOptions settings)
      {
        return false;
      }
    }

    [TestMethod]
    public void Interfaces_TypeGuard_AskedForTheTypeOfTheData()
    {
      LtMsgPackSerializer guarded = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, TypeGuard = new RefuseAll() });
      Assert.Throws<MsgPackException>(() => guarded.Deserialize<IList<int>>(Bytes(0x82, "", "List<Int32>", "@", 0x91, 1)));
      Assert.AreEqual(1, guarded.Deserialize<IList<int>>(Bytes(0x91, 1)).Count, "no type id: the data picks nothing");
    }

    [TestMethod]
    public void Differences_StillReported()
    {
      Dictionary<string, int> read = NoSchema.Deserialize<Dictionary<string, int>>(Bytes(0x81, "a", 1), out ReadDifferences differences);
      Assert.AreEqual(1, read["a"]);
      Assert.IsNull(differences);
    }

    [TestMethod]
    public void Polymorphic_ManyTypesAndThreads_SameBytes()
    {
      List<LcShape> shapes = new List<LcShape>();
      for (int t = 0; t < 60; t++)
        shapes.Add(t % 3 == 0 ? new LcCircle { Id = t, Radius = t } : t % 3 == 1 ? (LcShape)new LcSquare { Id = t, Side = t } : new LcTriangle { Id = t, Base = t });
      LcDrawing drawing = new LcDrawing { Shapes = shapes, Interfaced = shapes.ToArray(), ByName = shapes.ToDictionary(x => x.Id.ToString()) };

      for (int s = 0; s < 2; s++)
      {
        LtMsgPackOptions options = new LtMsgPackOptions { UseInexedSchema = s == 1 };
        byte[] expected = new LtMsgPackSerializer(options).Serialize(drawing);

        LtMsgPackSerializer shared = new LtMsgPackSerializer(options); // the caches per declared type are shared by the threads, starting empty
        Parallel.For(0, Environment.ProcessorCount * 4, t =>
        {
          for (int r = 0; r < 200; r++)
          {
            byte[] bytes = shared.Serialize(drawing);
            CollectionAssert.AreEqual(expected, bytes);
            LcDrawing back = shared.Deserialize<LcDrawing>(bytes);
            Assert.AreEqual(shapes.Count, back.Shapes.Count);
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
              Assert.AreEqual(shapes[i].GetType(), back.Shapes[i].GetType());
              Assert.AreEqual(shapes[i].GetType(), back.Interfaced[i].GetType());
              Assert.AreEqual(shapes[i].GetType(), back.ByName[shapes[i].Id.ToString()].GetType());
            }
          }
        });
      }
    }

    [TestMethod]
    public void HashSet_NoBoxPerElement()
    {
      HashSet<int> set = new HashSet<int>(Enumerable.Range(0, 1000));
      NoSchema.Serialize(set);
      long before = GC.GetAllocatedBytesForCurrentThread();
      byte[] bytes = NoSchema.Serialize(set);
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      DebuggableAttribute debuggable = typeof(LtMsgPackSerializer).Assembly.GetCustomAttribute<DebuggableAttribute>();
      if (debuggable == null || !debuggable.IsJITOptimizerDisabled) // unoptimized code boxes for "element == null"
        Assert.IsTrue(allocated < bytes.Length + 1024, $"{allocated} B for {bytes.Length} B of payload"); // boxed: 24 B per element and a list of them, 43 KB
      CollectionAssert.AreEquivalent(set.ToList(), NoSchema.Deserialize<HashSet<int>>(bytes).ToList());
    }
  }
}
