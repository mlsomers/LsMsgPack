using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace LsMsgPackAotBenchmark
{
  [LsMsgPackSerializable(typeof(Invoice))]
  public partial class BenchContext : LsMsgPackContext { }

  /// <summary>
  /// System.Text.Json's source generated metadata: the baseline that also runs under NativeAOT (its reflection is off there), the same bytes as its default options.
  /// </summary>
  [JsonSerializable(typeof(Invoice))]
  internal partial class StjContext : JsonSerializerContext { }

  /// <summary>
  /// Writes and reads the invoices of the benchmarks (100 invoices, 20 rounds) per setup, the fastest of 7 interleaved passes after a warm-up, as LsMsgPackInteropTests.BenchmarkLibraries does.
  /// The same program runs with a JIT (<c>dotnet run -c Release -p:PublishAot=false</c>) and as a NativeAOT binary; LsMsgPack with its source generated context and without one (the runtime plans: compiled with a JIT, reflection under NativeAOT).
  /// </summary>
  public static class Program
  {
    private const int InvoiceCount = 100;
    private const int Rounds = 20;
    private const int Passes = 7;
    private const int WarmUpMilliseconds = 500;

    private static readonly Invoice[] AllInvoices = Invoices.Create(InvoiceCount);

    private sealed class Candidate
    {
      internal string Name;
      internal Func<Invoice, byte[]> Serialize;
      internal Func<byte[], Invoice> Deserialize;
      internal byte[][] Payloads;
      internal long Bytes;
      internal TimeSpan FastestWrite, FastestRead;

      internal void Prepare()
      {
        Payloads = new byte[AllInvoices.Length][];
        for (int t = 0; t < AllInvoices.Length; t++)
        {
          Bytes += (Payloads[t] = Serialize(AllInvoices[t])).Length;
          string expected = JsonSerializer.Serialize(AllInvoices[t], StjContext.Default.Invoice);
          string read = JsonSerializer.Serialize(Deserialize(Payloads[t]), StjContext.Default.Invoice);
          if (expected != read)
            throw new Exception($"{Name}: the round trip of {AllInvoices[t].InvoiceNumber} differs\n  {expected}\n  {read}");
        }
      }

      internal TimeSpan Write()
      {
        byte[][] payloads = new byte[AllInvoices.Length][];
        Stopwatch sw = Stopwatch.StartNew();
        for (int r = 0; r < Rounds; r++)
          for (int t = 0; t < AllInvoices.Length; t++)
            payloads[t] = Serialize(AllInvoices[t]);
        return sw.Elapsed;
      }

      internal TimeSpan Read()
      {
        Stopwatch sw = Stopwatch.StartNew();
        for (int r = 0; r < Rounds; r++)
          for (int t = 0; t < Payloads.Length; t++)
            Deserialize(Payloads[t]);
        return sw.Elapsed;
      }

      internal double Allocated(bool write)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        if (write)
          for (int t = 0; t < AllInvoices.Length; t++)
            Serialize(AllInvoices[t]);
        else
          for (int t = 0; t < Payloads.Length; t++)
            Deserialize(Payloads[t]);
        return (double)(GC.GetAllocatedBytesForCurrentThread() - before) / AllInvoices.Length;
      }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Deliberately without generated code: the speed of the runtime plans, the model kept by Model.xml.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Deliberately without generated code: the speed of the runtime plans, the model kept by Model.xml.")]
    private static LsMsgPackSerializer Runtime(LsMsgPackOptions options)
    {
      options.Contexts = null;
      options.UseRegisteredContexts = false;
      return new LsMsgPackSerializer(options);
    }

    private static LsMsgPackSerializer Generated(LsMsgPackOptions options)
    {
      return new LsMsgPackSerializer(BenchContext.Default, options);
    }

    private static Candidate Ls(string name, LsMsgPackSerializer serializer)
    {
      return new Candidate() { Name = name, Serialize = i => serializer.Serialize(i), Deserialize = b => serializer.Deserialize<Invoice>(b) };
    }

    private static IEnumerable<(string Name, Func<LsMsgPackOptions> Options)> OptionSets()
    {
      yield return ("default options", () => new LsMsgPackOptions());
      yield return ("arrays, no schema", () => new LsMsgPackOptions() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration });
      yield return ("maps, property names", () => new LsMsgPackOptions() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map });
    }

    public static int Main(string[] args)
    {
      string mode = RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT";
      Console.WriteLine($"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {mode}");
      List<Candidate> candidates = new List<Candidate>();
      candidates.Add(new Candidate()
      {
        Name = "System.Text.Json, source generated",
        Serialize = i => JsonSerializer.SerializeToUtf8Bytes(i, StjContext.Default.Invoice),
        Deserialize = b => JsonSerializer.Deserialize(b, StjContext.Default.Invoice)
      });
      foreach ((string name, Func<LsMsgPackOptions> options) in OptionSets())
      {
        candidates.Add(Ls("LsMsgPack " + name, Runtime(options())));
        candidates.Add(Ls("LsMsgPack " + name + ", generated", Generated(options())));
      }
      for (int c = 0; c < candidates.Count; c++)
        candidates[c].Prepare();

      foreach (bool write in new[] { true, false })
      {
        for (int c = 0; c < candidates.Count; c++)
        {
          Candidate candidate = candidates[c];
          Stopwatch warm = Stopwatch.StartNew();
          while (warm.ElapsedMilliseconds < WarmUpMilliseconds) // also past CompileWritersAfter/CompileReadersAfter with a JIT
            _ = write ? candidate.Write() : candidate.Read();
        }
        for (int p = 0; p < Passes; p++)
        {
          for (int c = 0; c < candidates.Count; c++)
          {
            TimeSpan elapsed = write ? candidates[c].Write() : candidates[c].Read();
            if (write && (p == 0 || elapsed < candidates[c].FastestWrite))
              candidates[c].FastestWrite = elapsed;
            if (!write && (p == 0 || elapsed < candidates[c].FastestRead))
              candidates[c].FastestRead = elapsed;
          }
        }
      }

      Candidate baseline = candidates[0];
      StringBuilder sb = new StringBuilder();
      sb.AppendLine($"{InvoiceCount} invoices, {Rounds} rounds each, fastest of {Passes} passes; speed: times as fast as System.Text.Json");
      sb.AppendLine(string.Format("{0,-50} {1,9} {2,9} {3,7} {4,10} {5,9} {6,7} {7,10}", "Setup", "Bytes", "write ms", "speed", "alloc/inv", "read ms", "speed", "alloc/inv"));
      for (int c = 0; c < candidates.Count; c++)
      {
        Candidate x = candidates[c];
        sb.AppendLine(string.Format("{0,-50} {1,9:N0} {2,9:N2} {3,6:N2}x {4,8:N0} B {5,9:N2} {6,6:N2}x {7,8:N0} B", x.Name, x.Bytes,
          x.FastestWrite.TotalMilliseconds, baseline.FastestWrite.TotalMilliseconds / x.FastestWrite.TotalMilliseconds, x.Allocated(true),
          x.FastestRead.TotalMilliseconds, baseline.FastestRead.TotalMilliseconds / x.FastestRead.TotalMilliseconds, x.Allocated(false)));
      }
      Console.Write(sb.ToString());
      for (int c = 0; c < candidates.Count; c++) // for the medians of several runs
        Console.WriteLine(FormattableString.Invariant($"RESULT|{mode}|{candidates[c].Name}|{candidates[c].FastestWrite.TotalMilliseconds:F3}|{candidates[c].FastestRead.TotalMilliseconds:F3}"));
      return 0;
    }
  }
}
