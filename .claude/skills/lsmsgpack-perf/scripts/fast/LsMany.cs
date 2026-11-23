// Many root types in turn (a server with hundreds of endpoints): 200 root types (LsManyTypes.cs, made by many-types.sh) with a nested class they all have, written (Serialize<T> into an ArrayBufferWriter)
// and read (Deserialize<T>) by one serializer, per setup (names, inline, reference):
//   alone      each type alone (the average of 8 types): the cost of one root type at a time
//   uniform    the 200 types in a random order
//   skewed     10 types get 80% of the calls, the other 190 the rest
//   4 threads  4 threads on the one serializer, each its own random order (ns per call per thread)
// The difference between alone and the others is the caches behind the single-entry ones (root entries, known schemas, the plans' sessions and bindings of the nested class) and the CPU's caches
// (the data of 200 classes): without a schema none of the former, so its ratio is what the CPU costs.
// Usage: dotnet many-new.dll [seconds per measurement=1] [setup]              prints ns and allocated bytes per call
//        dotnet many-new.dll one <names|inline|reference> <write|read> <seconds> [uniform|skewed]   one measurement, for perf
using LsMsgPack;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

static class Program
{
  const int SequenceLength = 1 << 14;

  static LsMsgPackOptions Options(string setup)
  {
    switch (setup)
    {
      case "names": return new LsMsgPackOptions { UseIndexedSchema = false };
      case "inline": return new LsMsgPackOptions();
      case "reference": return new LsMsgPackOptions { SchemaStore = new SchemaStore(), WriteSchemaReference = true };
      default: throw new ArgumentException("setup: names, inline or reference");
    }
  }

  static int[] Uniform(int seed)
  {
    Random random = new Random(seed);
    int[] sequence = new int[SequenceLength];
    for (int t = 0; t < sequence.Length; t++)
      sequence[t] = random.Next(Many.N);
    return sequence;
  }

  static int[] Skewed(int seed)
  {
    Random random = new Random(seed);
    int[] sequence = new int[SequenceLength];
    for (int t = 0; t < sequence.Length; t++)
      sequence[t] = random.Next(10) < 8 ? random.Next(10) : 10 + random.Next(Many.N - 10);
    return sequence;
  }

  static int[] Alone(int type)
  {
    int[] sequence = new int[SequenceLength];
    for (int t = 0; t < sequence.Length; t++)
      sequence[t] = type;
    return sequence;
  }

  static byte[][] Payloads(LsMsgPackSerializer s)
  {
    byte[][] data = new byte[Many.N][];
    for (int t = 0; t < Many.N; t++)
      data[t] = Many.Bytes[t](s);
    return data;
  }

  static void Main(string[] args)
  {
    if (args.Length > 0 && args[0] == "one")
    {
      LsMsgPackSerializer one = new LsMsgPackSerializer(Options(args[1]));
      bool skewed = args.Length > 4 && args[4] == "skewed";
      (double ns, double bytes) = Run(one, Payloads(one), skewed ? Skewed(2) : Uniform(1), args[2] == "write", double.Parse(args[3], CultureInfo.InvariantCulture));
      Console.WriteLine($"{args[1]} {args[2]} {(skewed ? "skewed" : "uniform")}: {ns:F0} ns {bytes:F0} B");
      return;
    }

    double seconds = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : 1;
    string only = args.Length > 1 ? args[1] : null;
    Console.WriteLine($"{"setup",-10} {"op",-6} {"alone",16} {"uniform 200",16} {"skewed",16} {"4 threads",16}   ns and B per call");
    string[] setups = { "names", "inline", "reference" };
    for (int u = 0; u < setups.Length; u++)
    {
      string setup = setups[u];
      if (only != null && only != setup)
        continue;
      LsMsgPackSerializer s = new LsMsgPackSerializer(Options(setup));
      byte[][] data = Payloads(s);

      // past tiering and the compiled writers and readers (after 1000 objects of a plan), with all types
      int[] warm = Uniform(99);
      Run(s, data, warm, true, 2.0);
      Run(s, data, warm, false, 2.0);

      for (int op = 0; op < 2; op++)
      {
        bool write = op == 0;
        double aloneNs = 0, aloneB = 0;
        int[] samples = { 0, 1, 2, 3, 4, 57, 123, 199 };
        for (int t = 0; t < samples.Length; t++)
        {
          (double ns, double b) = Run(s, data, Alone(samples[t]), write, seconds / 2);
          aloneNs += ns / samples.Length;
          aloneB += b / samples.Length;
        }
        (double uNs, double uB) = Run(s, data, Uniform(1), write, seconds);
        (double kNs, double kB) = Run(s, data, Skewed(2), write, seconds);
        (double tNs, double tB) = Threads(s, data, write, 4, seconds);
        Console.WriteLine($"{setup,-10} {(write ? "write" : "read"),-6} {aloneNs,7:F0} {aloneB,4:F0} B {uNs,7:F0} {uB,4:F0} B {kNs,7:F0} {kB,4:F0} B {tNs,7:F0} {tB,4:F0} B");
      }
    }
  }

  /// <returns>ns per call (the fastest of 3 passes) and B per call (results are discarded)</returns>
  static (double, double) Run(LsMsgPackSerializer s, byte[][] data, int[] sequence, bool write, double seconds)
  {
    ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(4096);
    double best = double.MaxValue, bytes = 0;
    for (int pass = 0; pass < 3; pass++)
    {
      long calls = 0;
      long before = GC.GetAllocatedBytesForCurrentThread();
      Stopwatch sw = Stopwatch.StartNew();
      while (sw.Elapsed.TotalSeconds < seconds / 3)
      {
        if (write)
          for (int t = 0; t < sequence.Length; t++)
          {
            buffer.ResetWrittenCount();
            Many.Writers[sequence[t]](s, buffer);
          }
        else
          for (int t = 0; t < sequence.Length; t++)
          {
            int type = sequence[t];
            Many.Readers[type](s, data[type]);
          }
        calls += sequence.Length;
      }
      double elapsed = sw.Elapsed.TotalMilliseconds;
      best = Math.Min(best, elapsed * 1e6 / calls);
      bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)calls;
    }
    return (best, bytes);
  }

  /// <returns>ns per call per thread (wall time × threads / calls) and B per call, threads on one serializer, each its own random order</returns>
  static (double, double) Threads(LsMsgPackSerializer s, byte[][] data, bool write, int count, double seconds)
  {
    long calls = 0;
    long before = GC.GetTotalAllocatedBytes(true);
    Stopwatch sw = Stopwatch.StartNew();
    Thread[] threads = new Thread[count];
    for (int t = 0; t < count; t++)
    {
      int seed = 10 + t;
      threads[t] = new Thread(() =>
      {
        int[] sequence = Uniform(seed);
        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(4096);
        long mine = 0;
        while (sw.Elapsed.TotalSeconds < seconds)
        {
          if (write)
            for (int i = 0; i < sequence.Length; i++)
            {
              buffer.ResetWrittenCount();
              Many.Writers[sequence[i]](s, buffer);
            }
          else
            for (int i = 0; i < sequence.Length; i++)
            {
              int type = sequence[i];
              Many.Readers[type](s, data[type]);
            }
          mine += sequence.Length;
        }
        Interlocked.Add(ref calls, mine);
      });
      threads[t].Start();
    }
    for (int t = 0; t < count; t++)
      threads[t].Join();
    double elapsed = sw.Elapsed.TotalMilliseconds;
    return (elapsed * 1e6 * count / calls, (GC.GetTotalAllocatedBytes(true) - before) / (double)calls);
  }
}
