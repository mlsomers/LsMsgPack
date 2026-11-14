using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Reading several root types in turn: LsMsgPack keeps the recent schemas, shared sessions and root types (Serializer.RecentReads), beyond the last one.
  /// More root types and schemas than it keeps, schemas that grow, and several threads read the values a fresh serializer reads.
  /// </summary>
  [TestClass]
  public class LsRecentSchemaTests
  {
    public class RecentRoot<T>
    {
      public string Name { get; set; }
      public T Value { get; set; }
      public object Extra { get; set; }
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
    };

    private static string Describe(object value)
    {
      if (value is null)
        return "null";
      Type type = value.GetType();
      object inner = type.GetProperty("Value").GetValue(value);
      object extra = type.GetProperty("Extra").GetValue(value);
      string innerText = inner is byte[] bytes ? string.Join(",", bytes) : inner is List<int> list ? string.Join(",", list) : inner is RecentExtraA a ? "A" + a.A : Convert.ToString(inner, System.Globalization.CultureInfo.InvariantCulture);
      string extraText = extra is RecentExtraA ea ? "A" + ea.A : extra is RecentExtraB eb ? "B" + eb.B : extra is null ? "-" : extra.ToString();
      return string.Concat(type.GetProperty("Name").GetValue(value), "=", innerText, "/", extraText);
    }

    private static IEnumerable<(string Name, Func<LsMsgPackOptions> Options)> Settings()
    {
      yield return ("names", () => new LsMsgPackOptions { UseInexedSchema = false });
      yield return ("inline", () => new LsMsgPackOptions());
      yield return ("inline, map", () => new LsMsgPackOptions { ObjectLayout = ObjectLayout.Map });
      SchemaStore store = new SchemaStore(); // the writer's and the readers' (references need the schemas)
      yield return ("reference", () => new LsMsgPackOptions { SchemaStore = store, WriteSchemaReference = true });
    }

    /// <summary>
    /// 12 root types (more than are kept), and values whose object property holds another class later: with schema references the schema of that root type grows (a new id),
    /// so old and new schemas of one root type are read in turn too.
    /// </summary>
    private static List<Sample> Samples(LsMsgPackSerializer writer)
    {
      List<Sample> samples = new List<Sample>();
      for (int round = 0; round < 3; round++)
      {
        for (int t = 0; t < Makers.Length; t++)
        {
          object value = Makers[t](round * 100 + t);
          if (round == 1)
            value.GetType().GetProperty("Extra").SetValue(value, new RecentExtraA { A = t });
          else if (round == 2)
            value.GetType().GetProperty("Extra").SetValue(value, new RecentExtraB { B = "b" + t });
          samples.Add(new Sample { Type = value.GetType(), Data = writer.Serialize(value, value.GetType()), Expected = Describe(value) });
        }
      }
      return samples;
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
