using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Several root types in turn: LsMsgPack keeps the recent schemas, shared sessions and root types when reading (Serializer.RecentReads), the root types per thread when writing (Serializer.WriteRootEntry),
  /// and the plans of a class below several root types their ids and bindings in the session of each (ObjectPlan.RecentSessions, ReadPlan.RecentBindings, CompiledReads), beyond the last one.
  /// More root types and schemas than are kept, schemas that grow, and several threads write the bytes and read the values of a serializer that sees one root type at a time.
  /// </summary>
  [TestClass]
  public class LsRecentSchemaTests
  {
    public class RecentRoot<T>
    {
      public string Name { get; set; }
      public T Value { get; set; }
      public object Extra { get; set; }
      public RecentPoint At { get; set; }
    }

    public struct RecentStructRoot
    {
      public string Name { get; set; }
      public int Value { get; set; }
      public object Extra { get; set; }
      public RecentPoint At { get; set; }
    }

    /// <summary>
    /// Below every root type: its plans are written and read with the schema session of each.
    /// </summary>
    public class RecentPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public class RecentExtraA
    {
      public int A { get; set; }
    }

    public class RecentExtraB
    {
      public string B { get; set; }
    }

    /// <summary>
    /// A payload, its root type and how to check what was read.
    /// </summary>
    private sealed class Sample
    {
      internal Type Type;
      internal byte[] Data;
      internal string Expected;
    }

    private static readonly Func<int, object>[] Makers =
    {
      i => new RecentRoot<int> { Name = "int" + i, Value = i },
      i => new RecentRoot<long> { Name = "long" + i, Value = -i },
      i => new RecentRoot<string> { Name = "string" + i, Value = "v" + i },
      i => new RecentRoot<double> { Name = "double" + i, Value = i / 2.0 },
      i => new RecentRoot<bool> { Name = "bool" + i, Value = i % 2 == 0 },
      i => new RecentRoot<byte[]> { Name = "bin" + i, Value = new byte[] { (byte)i, 2, 3 } },
      i => new RecentRoot<List<int>> { Name = "list" + i, Value = new List<int> { i, i + 1 } },
      i => new RecentRoot<Guid> { Name = "guid" + i, Value = new Guid(i, 2, 3, new byte[8]) },
      i => new RecentRoot<DateTime> { Name = "date" + i, Value = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i) },
      i => new RecentRoot<decimal> { Name = "decimal" + i, Value = i * 1.25m },
      i => new RecentRoot<short> { Name = "short" + i, Value = (short)i },
      i => new RecentRoot<RecentExtraA> { Name = "nested" + i, Value = new RecentExtraA { A = i } },
      i => new RecentStructRoot { Name = "struct" + i, Value = i },
    };

    /// <summary>
    /// The value of maker <paramref name="t"/> in a round: its object property holds another class in each round (with schema references the schema of that root type grows: a new id).
    /// </summary>
    private static object Make(int t, int round)
    {
      object value = Makers[t](round * 100 + t); // a struct is boxed: the properties below are set in the box
      Type type = value.GetType();
      if (round == 1)
        type.GetProperty("Extra").SetValue(value, new RecentExtraA { A = t });
      else if (round == 2)
        type.GetProperty("Extra").SetValue(value, new RecentExtraB { B = "b" + t });
      type.GetProperty("At").SetValue(value, new RecentPoint { X = t, Y = round });
      return value;
    }

    private static string Describe(object value)
    {
      if (value is null)
        return "null";
      Type type = value.GetType();
      object inner = type.GetProperty("Value").GetValue(value);
      object extra = type.GetProperty("Extra").GetValue(value);
      string innerText = inner is byte[] bytes ? string.Join(",", bytes) : inner is List<int> list ? string.Join(",", list) : inner is RecentExtraA a ? "A" + a.A : Convert.ToString(inner, System.Globalization.CultureInfo.InvariantCulture);
      string extraText = extra is RecentExtraA ea ? "A" + ea.A : extra is RecentExtraB eb ? "B" + eb.B : extra is null ? "-" : extra.ToString();
      RecentPoint at = (RecentPoint)type.GetProperty("At").GetValue(value);
      string atText = at is null ? "-" : at.X + "," + at.Y;
      return string.Concat(type.GetProperty("Name").GetValue(value), "=", innerText, "/", extraText, "@", atText);
    }

    private static IEnumerable<(string Name, Func<LsMsgPackOptions> Options)> Settings()
    {
      yield return ("names", () => new LsMsgPackOptions { UseIndexedSchema = false });
      yield return ("inline", () => new LsMsgPackOptions());
      yield return ("inline, map", () => new LsMsgPackOptions { ObjectLayout = ObjectLayout.Map });
      SchemaStore store = new SchemaStore(); // the writer's and the readers' (references need the schemas)
      yield return ("reference", () => new LsMsgPackOptions { SchemaStore = store, WriteSchemaReference = true });
    }

    /// <summary>
    /// 13 root types (more than are kept), 3 rounds (see <see cref="Make"/>): old and new schemas of one root type are read in turn too.
    /// </summary>
    private static List<Sample> Samples(LsMsgPackSerializer writer)
    {
      List<Sample> samples = new List<Sample>();
      for (int round = 0; round < 3; round++)
      {
        for (int t = 0; t < Makers.Length; t++)
        {
          object value = Make(t, round);
          samples.Add(new Sample { Type = value.GetType(), Data = writer.Serialize(value, value.GetType()), Expected = Describe(value) });
        }
      }
      return samples;
    }

    private static readonly MethodInfo TypedSerialize = typeof(LsMsgPackSerializer).GetMethods()
      .Single(m => m.Name == nameof(LsMsgPackSerializer.Serialize) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

    private static readonly MethodInfo TypedSerializeToBuffer = typeof(LsMsgPackSerializer).GetMethods()
      .Single(m => m.Name == nameof(LsMsgPackSerializer.Serialize) && m.IsGenericMethodDefinition && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(IBufferWriter<byte>));

    /// <summary>
    /// Writes the value by one of the calls (<paramref name="call"/>: typed or untyped, into a byte[] or an <see cref="IBufferWriter{T}"/>), as its own type.
    /// </summary>
    private static byte[] Write(LsMsgPackSerializer serializer, object value, int call)
    {
      Type type = value.GetType();
      switch (call)
      {
        case 0:
          return serializer.Serialize(value, type);
        case 1:
          return (byte[])TypedSerialize.MakeGenericMethod(type).Invoke(serializer, new[] { value });
        case 2:
          ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
          serializer.Serialize(value, type, buffer);
          return buffer.WrittenSpan.ToArray();
        default:
          ArrayBufferWriter<byte> typedBuffer = new ArrayBufferWriter<byte>();
          TypedSerializeToBuffer.MakeGenericMethod(type).Invoke(serializer, new[] { value, typedBuffer });
          return typedBuffer.WrittenSpan.ToArray();
      }
    }

    /// <summary>
    /// The options of the settings <paramref name="name"/> with a schema store of their own (the writes of one serializer may not grow the schemas of another).
    /// </summary>
    private static LsMsgPackOptions Fresh(string name)
    {
      return Settings().First(s => s.Name == name).Options(); // a new enumeration makes a new store
    }

    /// <summary>
    /// Writing the root types in a random order (each type's values in their order, so a schema grows at the same value) by all calls: the bytes of a serializer that writes one root type at a time.
    /// </summary>
    [TestMethod]
    public void AlternatingRootTypes_WriteSameBytes()
    {
      foreach (string name in Settings().Select(s => s.Name))
      {
        LsMsgPackSerializer grouped = new LsMsgPackSerializer(Fresh(name));
        byte[][][] expected = new byte[Makers.Length][][];
        for (int t = 0; t < Makers.Length; t++)
        {
          expected[t] = new byte[3][];
          for (int round = 0; round < 3; round++)
          {
            object value = Make(t, round);
            expected[t][round] = grouped.Serialize(value, value.GetType());
          }
        }

        for (int pass = 0; pass < 4; pass++)
        {
          LsMsgPackSerializer alternating = new LsMsgPackSerializer(Fresh(name));
          Random random = new Random(pass);
          int[] next = new int[Makers.Length];
          int left = Makers.Length * 3;
          while (left > 0)
          {
            int t = random.Next(Makers.Length);
            if (next[t] == 3)
              continue;
            int round = next[t]++;
            left--;
            int call = random.Next(4);
            CollectionAssert.AreEqual(expected[t][round], Write(alternating, Make(t, round), call), $"{name}: {Describe(Make(t, round))}, call {call}, pass {pass}");
          }
        }
      }
    }

    /// <summary>
    /// Threads writing the root types in turn with one serializer: each payload reads as its value (with schema references another thread may have grown the schema: other bytes, the same value), without references the same bytes.
    /// </summary>
    [TestMethod]
    public void AlternatingRootTypes_WriteThreads()
    {
      foreach ((string name, Func<LsMsgPackOptions> options) in Settings())
      {
        LsMsgPackSerializer grouped = new LsMsgPackSerializer(options());
        List<Sample> samples = Samples(grouped);
        bool sameBytes = name != "reference";
        LsMsgPackSerializer writer = new LsMsgPackSerializer(options()); // shared by the threads
        LsMsgPackSerializer reader = new LsMsgPackSerializer(options());
        string[] failures = new string[8];
        Parallel.For(0, failures.Length, new ParallelOptions { MaxDegreeOfParallelism = failures.Length }, thread =>
        {
          Random random = new Random(thread);
          for (int t = 0; t < 3000 && failures[thread] is null; t++)
          {
            int index = random.Next(samples.Count);
            Sample sample = samples[index];
            object value = Make(index % Makers.Length, index / Makers.Length);
            string read;
            try
            {
              byte[] data = Write(writer, value, random.Next(4));
              if (sameBytes && !data.SequenceEqual(sample.Data))
                read = "other bytes";
              else
                read = Describe(reader.Deserialize(sample.Type, data));
            }
            catch (Exception ex)
            {
              read = ex.GetType().Name + ": " + ex.Message;
            }
            if (read != sample.Expected)
              failures[thread] = $"{sample.Expected} written as {read}";
          }
        });
        Assert.AreEqual(string.Empty, string.Join(" | ", failures.Where(f => f != null)), name);
      }
    }

    [TestMethod]
    public void AlternatingRootTypes_SameValues()
    {
      foreach ((string name, Func<LsMsgPackOptions> options) in Settings())
      {
        LsMsgPackSerializer writer = new LsMsgPackSerializer(options());
        List<Sample> samples = Samples(writer);
        LsMsgPackSerializer reader = new LsMsgPackSerializer(options());
        Random random = new Random(7);
        for (int t = 0; t < 2000; t++) // in a random order: every switch of the root type and schema, and more of them than are kept
        {
          Sample sample = samples[random.Next(samples.Count)];
          Assert.AreEqual(sample.Expected, Describe(reader.Deserialize(sample.Type, sample.Data)), name);
        }
        for (int t = 0; t < samples.Count; t++) // and each once with a fresh serializer
          Assert.AreEqual(samples[t].Expected, Describe(new LsMsgPackSerializer(options()).Deserialize(samples[t].Type, samples[t].Data)), name + " fresh");
      }
    }

    [TestMethod]
    public void AlternatingRootTypes_Threads()
    {
      foreach ((string name, Func<LsMsgPackOptions> options) in Settings())
      {
        LsMsgPackSerializer writer = new LsMsgPackSerializer(options());
        List<Sample> samples = Samples(writer);
        LsMsgPackSerializer reader = new LsMsgPackSerializer(options()); // shared by the threads
        string[] failures = new string[8];
        Parallel.For(0, failures.Length, new ParallelOptions { MaxDegreeOfParallelism = failures.Length }, thread =>
        {
          Random random = new Random(thread);
          for (int t = 0; t < 5000 && failures[thread] is null; t++)
          {
            Sample sample = samples[random.Next(samples.Count)];
            string read;
            try
            {
              read = Describe(reader.Deserialize(sample.Type, sample.Data));
            }
            catch (Exception ex)
            {
              read = ex.GetType().Name + ": " + ex.Message;
            }
            if (read != sample.Expected)
              failures[thread] = $"{sample.Expected} read as {read}";
          }
        });
        Assert.AreEqual(string.Empty, string.Join(" | ", failures.Where(f => f != null)), name);
      }
    }

    // Writer classes that the reader does not have: their schemas bind the reader's RecentDtoPoint in two orders
    public class RecentDtoPointYX
    {
      public int Y { get; set; }
      public int X { get; set; }
    }

    public class RecentDtoPointXY
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public class RecentDtoPointX
    {
      public int X { get; set; }
    }

    public class RecentDtoWriter1
    {
      public int Id { get; set; }
      public RecentDtoPointYX At { get; set; }
    }

    public class RecentDtoWriter2
    {
      public int Id { get; set; }
      public RecentDtoPointXY At { get; set; }
    }

    public class RecentDtoWriter3
    {
      public int Id { get; set; }
      public RecentDtoPointX At { get; set; }
    }

    public class RecentDtoPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public class RecentDtoReader1
    {
      public int Id { get; set; }
      public RecentDtoPoint At { get; set; }
    }

    public class RecentDtoReader2
    {
      public int Id { get; set; }
      public RecentDtoPoint At { get; set; }
    }

    public class RecentDtoReader3
    {
      public int Id { get; set; }
      public RecentDtoPoint At { get; set; }
    }

    /// <summary>
    /// A class below two or three root types whose schemas list its properties in other orders (classes paired by their property names: Y, X / X, Y / X), read in turn, each payload with its own binding,
    /// by the runtime's readers and by the compiled ones (which read the positions that are in the plan's order straight-line: 0 / 2 / 1). Two in turn swap the last and previous bindings of the plan,
    /// three in turn find them in the tables behind those.
    /// </summary>
    [TestMethod]
    public void AlternatingRootTypes_BindingsInOtherOrders()
    {
      foreach ((string name, Func<LsMsgPackOptions> options) in Settings())
      {
        if (name == "names")
          continue; // no schema, no binding
        for (int run = 0; run < 10; run++) // runtime's or compiled readers; 2 types either one first, 3 types each one first (a recent answer of the binding in the plan's order would make another one wrong)
        {
          LsMsgPackSerializer writer = new LsMsgPackSerializer(options());
          byte[][] data =
          {
            writer.Serialize(new RecentDtoWriter1 { Id = 1, At = new RecentDtoPointYX { X = 10, Y = 20 } }),
            writer.Serialize(new RecentDtoWriter2 { Id = 2, At = new RecentDtoPointXY { X = 30, Y = 40 } }),
            writer.Serialize(new RecentDtoWriter3 { Id = 3, At = new RecentDtoPointX { X = 50 } }),
          };
          Func<LsMsgPackSerializer, byte[], string>[] readers =
          {
            (s, d) => { RecentDtoReader1 v = s.Deserialize<RecentDtoReader1>(d); return v.Id + ":" + v.At.X + "," + v.At.Y; },
            (s, d) => { RecentDtoReader2 v = s.Deserialize<RecentDtoReader2>(d); return v.Id + ":" + v.At.X + "," + v.At.Y; },
            (s, d) => { RecentDtoReader3 v = s.Deserialize<RecentDtoReader3>(d); return v.Id + ":" + v.At.X + "," + v.At.Y; },
          };
          string[] expected = { "1:10,20", "2:30,40", "3:50,0" };
          int types = run % 5 < 2 ? 2 : 3;
          int first = run % 5 < 2 ? run % 5 : run % 5 - 2;
          LsMsgPackOptions readerOptions = options();
          readerOptions.CompileReadersAfter = run < 5 ? 1000 : 0;
          LsMsgPackSerializer reader = new LsMsgPackSerializer(readerOptions);
          string context = $"{name}, compile after {readerOptions.CompileReadersAfter}, {types} types, {first} first";
          for (int t = 0; t < 120; t++)
          {
            int type = (first + t) % types;
            Assert.AreEqual(expected[type], readers[type](reader, data[type]), $"{context}, read {t}");
          }
        }
      }
    }

    /// <summary>
    /// The writer of several root types in turn, and with differences reported: the same as one root type at a time.
    /// </summary>
    [TestMethod]
    public void AlternatingRootTypes_DifferencesAndBytes()
    {
      foreach ((string name, Func<LsMsgPackOptions> options) in Settings())
      {
        LsMsgPackSerializer alternating = new LsMsgPackSerializer(options());
        List<Sample> samples = Samples(alternating);
        LsMsgPackSerializer reporting = new LsMsgPackSerializer(options());
        for (int round = 0; round < 3; round++)
        {
          for (int t = 0; t < samples.Count; t++)
          {
            Assert.AreEqual(samples[t].Expected, Describe(reporting.Deserialize(samples[t].Type, samples[t].Data, out ReadDifferences differences)), name);
            Assert.IsNull(differences, name + ": " + differences?.GenerateReport());
          }
        }
      }
    }
  }
}
