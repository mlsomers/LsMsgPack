// Experiment: what would a cached (pre-shared) indexed schema save, compared with the current per-call schema?
// Usage: cached <mode> [seconds]
//   verify   : checks that the cached body equals the current body and that the round trip is equal
//   invoices : 100 invoices x 20 rounds, write and read, all candidates
//   small    : per call cost of one Address / one Customer
//   phases   : where the time goes in the cached path (tree build, ToBytes, Unpack, Convert)
using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

public static partial class Program
{
  internal static readonly Func<MsgPackSettings, IndexedSchemaTypeResolver, MsgPackSettings> WithSchema =
    (Func<MsgPackSettings, IndexedSchemaTypeResolver, MsgPackSettings>)Delegate.CreateDelegate(
      typeof(Func<MsgPackSettings, IndexedSchemaTypeResolver, MsgPackSettings>),
      typeof(MsgPackSerializer).GetMethod("WithSchema", BindingFlags.NonPublic | BindingFlags.Static));

  internal static readonly Func<object, Type, MsgPackSettings, FullPropertyInfo, object> ConvertValue =
    (Func<object, Type, MsgPackSettings, FullPropertyInfo, object>)Delegate.CreateDelegate(
      typeof(Func<object, Type, MsgPackSettings, FullPropertyInfo, object>),
      typeof(MsgPackSerializer).GetMethod("ConvertDeserializeValue", BindingFlags.NonPublic | BindingFlags.Static));

  /// <summary>FNV-1a 64, deterministic (string.GetHashCode is randomized per process)</summary>
  internal static ulong Fnv1a64(byte[] data)
  {
    ulong hash = 14695981039346656037UL;
    foreach (byte b in data) { hash ^= b; hash *= 1099511628211UL; }
    return hash;
  }

  /// <summary>
  /// One schema session kept for all calls: the payload is a 10 byte reference (fixext8, ext type 2, 64 bit hash) followed by the body.
  /// Not thread safe (the session caches are plain dictionaries), good enough to measure.
  /// </summary>
  internal sealed class CachedSchemaWriter
  {
    private readonly IndexedSchemaTypeResolver _resolver = new IndexedSchemaTypeResolver();
    private readonly MsgPackSettings _session;
    private readonly FullPropertyInfo _root;
    public byte[] Schema;
    public ulong Hash;

    public CachedSchemaWriter(MsgPackSettings settings, Type root, IEnumerable<object> warmUp)
    {
      _session = WithSchema(settings, _resolver);
      _root = new FullPropertyInfo(root);
      foreach (object item in warmUp) // "ExtractSchemas": makes the schema complete before it is published
        MsgPackSerializer.SerializeObject(item, _session, _root).ToBytes();
      Schema = _resolver.Pack(settings);
      Hash = Fnv1a64(Schema);
    }

    public byte[] Serialize(object item)
    {
      byte[] body = MsgPackSerializer.SerializeObject(item, _session, _root).ToBytes();
      byte[] result = new byte[10 + body.Length];
      result[0] = 0xD7; // fixext 8
      result[1] = 2;    // ext type: schema reference
      BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(2), Hash);
      Buffer.BlockCopy(body, 0, result, 10, body.Length);
      return result;
    }

    public MsgPackItem BuildTree(object item) { return MsgPackSerializer.SerializeObject(item, _session, _root); }
  }

  internal sealed class CachedSchemaReader
  {
    private readonly MsgPackSettings _session;
    private readonly ulong _hash;

    public CachedSchemaReader(byte[] schema, MsgPackSettings settings, Type root)
    {
      MsgPackSerializer.CacheAssemblyTypes(root);
      IndexedSchemaTypeResolver resolver = IndexedSchemaTypeResolver.Unpack(new MemoryStream(schema), settings);
      _session = WithSchema(settings, resolver);
      _hash = Fnv1a64(schema);
    }

    public T Deserialize<T>(byte[] payload)
    {
      if (payload[0] != 0xD7 || payload[1] != 2 || BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(2)) != _hash)
        throw new InvalidDataException("unknown schema"); // here the GetSchema(hash) callback would be called
      MemoryStream ms = new MemoryStream(payload, 10, payload.Length - 10);
      MsgPackItem unpacked = MsgPackItem.Unpack(ms, _session);
      return (T)ConvertValue(unpacked.Value, typeof(T), _session, null);
    }

    public MsgPackItem UnpackOnly(byte[] payload) { return MsgPackItem.Unpack(new MemoryStream(payload, 10, payload.Length - 10), _session); }
    public object ConvertOnly(MsgPackItem item, Type t) { return ConvertValue(item.Value, t, _session, null); }
  }

  // ------------------------------------------------------------------ candidates

  internal sealed class Candidate
  {
    public string Name;
    public Func<object, byte[]> Write;
    public Func<byte[], object> Read;
    public byte[][] Payloads;
  }

  static readonly System.Text.Json.JsonSerializerOptions StjOptions = new System.Text.Json.JsonSerializerOptions();

  static List<Candidate> CreateCandidates(Type t, object[] samples)
  {
    MsgPackSettings indexed = new MsgPackSettings() { UseInexedSchema = true };
    MsgPackSettings named = new MsgPackSettings() { UseInexedSchema = false };
    CachedSchemaWriter writer = new CachedSchemaWriter(indexed, t, samples);
    CachedSchemaReader reader = new CachedSchemaReader(writer.Schema, indexed, t);
    MethodInfo generic = typeof(CachedSchemaReader).GetMethod(nameof(CachedSchemaReader.Deserialize)).MakeGenericMethod(t);
    Func<byte[], object> cachedRead = b => generic.Invoke(reader, new object[] { b }); // replaced below for known types
    if (t == typeof(Invoice)) cachedRead = b => reader.Deserialize<Invoice>(b);
    else if (t == typeof(Customer)) cachedRead = b => reader.Deserialize<Customer>(b);
    else if (t == typeof(Address)) cachedRead = b => reader.Deserialize<Address>(b);

    MessagePack.MessagePackSerializerOptions mpContractless = MessagePack.Resolvers.ContractlessStandardResolver.Options;

    return new List<Candidate>
    {
      new Candidate { Name = "Json.NET (UTF-8 bytes)",
        Write = o => Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(o)),
        Read = b => Newtonsoft.Json.JsonConvert.DeserializeObject(Encoding.UTF8.GetString(b), t) },
      new Candidate { Name = "System.Text.Json (reflection)",
        Write = o => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(o, t, StjOptions),
        Read = b => System.Text.Json.JsonSerializer.Deserialize(b, t, StjOptions) },
      new Candidate { Name = "MessagePack-CSharp contractless",
        Write = o => MessagePack.MessagePackSerializer.Serialize(t, o, mpContractless),
        Read = b => MessagePack.MessagePackSerializer.Deserialize(t, b, mpContractless) },
      new Candidate { Name = "LsMsgPack indexed (current)",
        Write = o => MsgPackSerializer.Serialize(o, t, indexed),
        Read = b => MsgPackSerializer.Deserialize(t, b, indexed) },
      new Candidate { Name = "LsMsgPack names (current)",
        Write = o => MsgPackSerializer.Serialize(o, t, named),
        Read = b => MsgPackSerializer.Deserialize(t, b, named) },
      new Candidate { Name = "LsMsgPack indexed, cached schema",
        Write = writer.Serialize,
        Read = cachedRead },
      Direct("Tree-less names (prototype)", new Direct.DirectSerializer(null, 0), t),
      Direct("Tree-less cached schema (prototype)", new Direct.DirectSerializer(BoundSchema(writer.Schema, indexed, t), writer.Hash), t),
      Inline("Tree-less inline schema, local cache", new Direct.InlineSchemaSerializer(writer.Schema, BoundSchema(writer.Schema, indexed, t), indexed), t),
    };
  }

  /// <summary>The schema as a reader gets it (names only, resolved to local types)</summary>
  internal static IndexedSchemaTypeResolver BoundSchema(byte[] schema, MsgPackSettings settings, Type root)
  {
    MsgPackSerializer.CacheAssemblyTypes(root);
    return IndexedSchemaTypeResolver.Unpack(new MemoryStream(schema), settings);
  }

  static Candidate Inline(string name, Direct.InlineSchemaSerializer s, Type t)
  {
    if (t == typeof(Invoice)) return new Candidate { Name = name, Write = o => s.Serialize((Invoice)o), Read = b => s.Deserialize<Invoice>(b) };
    if (t == typeof(Customer)) return new Candidate { Name = name, Write = o => s.Serialize((Customer)o), Read = b => s.Deserialize<Customer>(b) };
    if (t == typeof(Address)) return new Candidate { Name = name, Write = o => s.Serialize((Address)o), Read = b => s.Deserialize<Address>(b) };
    throw new NotSupportedException(t.Name);
  }

  static Candidate Direct(string name, Direct.DirectSerializer s, Type t)
  {
    if (t == typeof(Invoice)) return new Candidate { Name = name, Write = o => s.Serialize((Invoice)o), Read = b => s.Deserialize<Invoice>(b) };
    if (t == typeof(Customer)) return new Candidate { Name = name, Write = o => s.Serialize((Customer)o), Read = b => s.Deserialize<Customer>(b) };
    if (t == typeof(Address)) return new Candidate { Name = name, Write = o => s.Serialize((Address)o), Read = b => s.Deserialize<Address>(b) };
    throw new NotSupportedException(t.Name);
  }

  // ------------------------------------------------------------------ measuring

  /// <summary>Fastest of the passes, each pass is one call per sample repeated <paramref name="rounds"/> times</summary>
  static double[] Measure(List<Candidate> candidates, object[] samples, int rounds, double seconds, bool write)
  {
    double[] best = Enumerable.Repeat(double.MaxValue, candidates.Count).ToArray();
    Stopwatch total = Stopwatch.StartNew();
    int pass = 0;
    while (total.Elapsed.TotalSeconds < seconds || pass < 4)
    {
      for (int c = 0; c < candidates.Count; c++)
      {
        Candidate cand = candidates[c];
        Stopwatch sw = Stopwatch.StartNew();
        if (write)
        {
          for (int r = 0; r < rounds; r++)
            for (int s = 0; s < samples.Length; s++)
              cand.Write(samples[s]);
        }
        else
        {
          byte[][] payloads = cand.Payloads;
          for (int r = 0; r < rounds; r++)
            for (int s = 0; s < payloads.Length; s++)
              cand.Read(payloads[s]);
        }
        double ms = sw.Elapsed.TotalMilliseconds;
        if (pass >= 2 && ms < best[c]) best[c] = ms; // the first passes are warm-up
      }
      pass++;
    }
    return best;
  }

  static void Report(string title, List<Candidate> candidates, object[] samples, int rounds, double seconds)
  {
    foreach (Candidate c in candidates)
      c.Payloads = samples.Select(s => c.Write(s)).ToArray();

    double[] write = Measure(candidates, samples, rounds, seconds, true);
    double[] read = Measure(candidates, samples, rounds, seconds, false);
    long jsonBytes = candidates[0].Payloads.Sum(p => (long)p.Length);
    int calls = rounds * samples.Length;

    Console.WriteLine($"{title}: {samples.Length} samples x {rounds} rounds, fastest pass");
    Console.WriteLine($"{"Serializer",-34} {"Bytes",9} {"Size",5} {"Write ms",9} {"µs/call",8} {"Speed",6} {"Read ms",9} {"µs/call",8} {"Speed",6}");
    for (int c = 0; c < candidates.Count; c++)
    {
      long bytes = candidates[c].Payloads.Sum(p => (long)p.Length);
      Console.WriteLine($"{candidates[c].Name,-34} {bytes,9:N0} {(double)bytes / jsonBytes,5:P0} {write[c],9:N1} {write[c] * 1000 / calls,8:N2} {write[0] / write[c],5:N2}x {read[c],9:N1} {read[c] * 1000 / calls,8:N2} {read[0] / read[c],5:N2}x");
    }
    Console.WriteLine();
  }

  static string Json(object o)
  {
    return Newtonsoft.Json.JsonConvert.SerializeObject(o, new Newtonsoft.Json.JsonSerializerSettings { DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc });
  }

  public static void Main(string[] args)
  {
    string mode = args.Length > 0 ? args[0] : "invoices";
    double seconds = args.Length > 1 && double.TryParse(args[1], out double parsed) ? parsed : 6;
    Invoice[] invoices = GetInvoices(100);
    object[] invoiceSamples = invoices.Cast<object>().ToArray();

    if (mode == "verify")
    {
      MsgPackSettings indexed = new MsgPackSettings() { UseInexedSchema = true };
      CachedSchemaWriter writer = new CachedSchemaWriter(indexed, typeof(Invoice), invoiceSamples);
      CachedSchemaReader reader = new CachedSchemaReader(writer.Schema, indexed, typeof(Invoice));
      int same = 0, equal = 0; long current = 0, cached = 0;
      foreach (Invoice inv in invoices)
      {
        byte[] full = MsgPackSerializer.Serialize(inv, indexed);
        MemoryStream ms = new MemoryStream(full);
        MsgPackItem.Unpack(ms, indexed); // skip the inline schema
        byte[] body = full.Skip((int)ms.Position).ToArray();
        byte[] c = writer.Serialize(inv);
        if (body.SequenceEqual(c.Skip(10))) same++;
        if (Json(reader.Deserialize<Invoice>(c)) == Json(inv)) equal++;
        current += full.Length; cached += c.Length;
      }
      Console.WriteLine($"cached body identical to the current body: {same}/{invoices.Length}, round trip equal: {equal}/{invoices.Length}");
      Console.WriteLine($"schema {writer.Schema.Length} bytes (4 types), payload current {current:N0} bytes, cached {cached:N0} bytes ({(double)cached / current:P1})");

      // The tree-less prototype must write the same bytes as the current serializer, and read them back
      MsgPackSettings named = new MsgPackSettings() { UseInexedSchema = false };
      Direct.DirectSerializer directNames = new Direct.DirectSerializer(null, 0);
      Direct.DirectSerializer directCached = new Direct.DirectSerializer(BoundSchema(writer.Schema, indexed, typeof(Invoice)), writer.Hash);
      int namesSame = 0, cachedSame = 0, namesTrip = 0, cachedTrip = 0, crossTrip = 0;
      foreach (Invoice inv in invoices)
      {
        byte[] currentNames = MsgPackSerializer.Serialize(inv, named);
        byte[] dn = directNames.Serialize(inv);
        byte[] dc = directCached.Serialize(inv);
        if (dn.SequenceEqual(currentNames)) namesSame++;
        if (dc.SequenceEqual(writer.Serialize(inv))) cachedSame++;
        string expected = Json(inv);
        if (Json(directNames.Deserialize<Invoice>(currentNames)) == expected) namesTrip++;
        if (Json(directCached.Deserialize<Invoice>(dc)) == expected) cachedTrip++;
        if (Json(MsgPackSerializer.Deserialize<Invoice>(dn, named)) == expected && Json(reader.Deserialize<Invoice>(dc)) == expected) crossTrip++;
      }
      Console.WriteLine($"tree-less writer, same bytes as current: names {namesSame}/100, cached schema {cachedSame}/100");
      Direct.InlineSchemaSerializer inline = new Direct.InlineSchemaSerializer(writer.Schema, BoundSchema(writer.Schema, indexed, typeof(Invoice)), indexed);
      int inlineSame = 0, inlineTrip = 0;
      foreach (Invoice inv in invoices)
      {
        byte[] currentIndexed = MsgPackSerializer.Serialize(inv, indexed);
        if (inline.Serialize(inv).SequenceEqual(currentIndexed)) inlineSame++;
        if (Json(inline.Deserialize<Invoice>(currentIndexed)) == Json(inv)) inlineTrip++;
      }
      Console.WriteLine($"tree-less inline schema: same bytes as the current indexed payload {inlineSame}/100, reads current payloads {inlineTrip}/100");
      Console.WriteLine($"tree-less reader, round trip equal: names {namesTrip}/100, cached schema {cachedTrip}/100; current reader on tree-less bytes: {crossTrip}/100");
      return;
    }

    if (mode == "cold") // first call in a fresh process: plan building, reflection and JIT
    {
      string which = args[1];
      Invoice inv = invoices[0];
      Func<object, byte[]> write; Func<byte[], object> read;
      Stopwatch setup = Stopwatch.StartNew();
      switch (which)
      {
        case "json.net": write = o => Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(o)); read = b => Newtonsoft.Json.JsonConvert.DeserializeObject<Invoice>(Encoding.UTF8.GetString(b)); break;
        case "stj": write = o => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes((Invoice)o); read = b => System.Text.Json.JsonSerializer.Deserialize<Invoice>(b); break;
        case "messagepack": write = o => MessagePack.MessagePackSerializer.Serialize((Invoice)o, MessagePack.Resolvers.ContractlessStandardResolver.Options); read = b => MessagePack.MessagePackSerializer.Deserialize<Invoice>(b, MessagePack.Resolvers.ContractlessStandardResolver.Options); break;
        case "indexed": { MsgPackSettings s = new MsgPackSettings(); write = o => MsgPackSerializer.Serialize((Invoice)o, s); read = b => MsgPackSerializer.Deserialize<Invoice>(b, s); break; }
        case "names": { MsgPackSettings s = new MsgPackSettings() { UseInexedSchema = false }; write = o => MsgPackSerializer.Serialize((Invoice)o, s); read = b => MsgPackSerializer.Deserialize<Invoice>(b, s); break; }
        case "treeless": { Direct.DirectSerializer d = new Direct.DirectSerializer(null, 0); write = o => d.Serialize((Invoice)o); read = b => d.Deserialize<Invoice>(b); break; }
        default: throw new ArgumentException(which);
      }
      Stopwatch sw = Stopwatch.StartNew();
      byte[] payload = write(inv);
      double w1 = sw.Elapsed.TotalMilliseconds; sw.Restart();
      read(payload);
      double r1 = sw.Elapsed.TotalMilliseconds; sw.Restart();
      write(invoices[1]);
      double w2 = sw.Elapsed.TotalMilliseconds; sw.Restart();
      read(write(invoices[1]));
      double r2 = sw.Elapsed.TotalMilliseconds;
      Console.WriteLine($"{which,-12} first write {w1,7:N1} ms, first read {r1,7:N1} ms | second write {w2 * 1000,7:N0} µs, second write+read {r2 * 1000,7:N0} µs");
      return;
    }

    if (mode == "compress") // per message, as an HTTP response would be compressed
    {
      foreach (bool small in new[] { false, true })
      {
        object[] samples = small ? Enumerable.Range(1, 100).Select(i => (object)GetAddress(i)).ToArray() : invoiceSamples;
        List<Candidate> all = CreateCandidates(small ? typeof(Address) : typeof(Invoice), samples);
        Console.WriteLine(small ? "One Address per message (100 messages)" : "One invoice per message (100 messages)");
        Console.WriteLine($"{"Serializer",-36} {"raw",9} {"gzip fastest",13} {"gzip optimal",13} {"brotli q4",10} {"µs/msg brotli q4",17}");
        foreach (Candidate c in all.Where(x => !x.Name.StartsWith("System.Text") && !x.Name.StartsWith("Tree-less")))
        {
          byte[][] payloads = samples.Select(s => c.Write(s)).ToArray();
          long raw = payloads.Sum(p => (long)p.Length);
          long Gzip(System.IO.Compression.CompressionLevel level) => payloads.Sum(p => { MemoryStream ms = new MemoryStream(); using (var gz = new System.IO.Compression.GZipStream(ms, level, true)) gz.Write(p, 0, p.Length); return ms.Length; });
          long Brotli(int quality) { long total = 0; byte[] dst = new byte[1 << 20]; foreach (byte[] p in payloads) { System.IO.Compression.BrotliEncoder.TryCompress(p, dst, out int written, quality, 22); total += written; } return total; }
          long gf = Gzip(System.IO.Compression.CompressionLevel.Fastest), go = Gzip(System.IO.Compression.CompressionLevel.Optimal), b4 = Brotli(4);
          Stopwatch sw = Stopwatch.StartNew();
          for (int r = 0; r < 5; r++) Brotli(4);
          double us = sw.Elapsed.TotalMilliseconds * 1000 / (5 * payloads.Length);
          Console.WriteLine($"{c.Name,-36} {raw,9:N0} {gf,13:N0} {go,13:N0} {b4,10:N0} {us,17:N1}");
        }
        Console.WriteLine();
      }
      return;
    }

    if (mode == "parallel") // throughput with 1..4 threads, each thread writes and reads its own copy of the invoices
    {
      bool small = args.Length > 1 && args[1] == "address";
      if (small) invoiceSamples = Enumerable.Range(1, 100).Select(i => (object)GetAddress(i)).ToArray();
      List<Candidate> all = CreateCandidates(small ? typeof(Address) : typeof(Invoice), invoiceSamples);
      string[] names = { "Json.NET (UTF-8 bytes)", "LsMsgPack indexed (current)", "LsMsgPack names (current)", "Tree-less names (prototype)", "Tree-less inline schema, local cache" };
      foreach (Candidate c in all) c.Payloads = invoiceSamples.Select(s => c.Write(s)).ToArray();
      Console.WriteLine($"{"Serializer",-36} {"threads",7} {"write msg/s",12} {"scaling",8} {"read msg/s",12} {"scaling",8}");
      foreach (string name in names)
      {
        Candidate c = all.First(x => x.Name == name);
        double write1 = 0, read1 = 0;
        foreach (int threads in new[] { 1, 2, 4 })
        {
          double best(bool write)
          {
            double max = 0;
            for (int pass = 0; pass < 5; pass++)
            {
              long done = 0;
              System.Threading.Barrier barrier = new System.Threading.Barrier(threads);
              System.Threading.Thread[] ts = Enumerable.Range(0, threads).Select(_ => new System.Threading.Thread(() =>
              {
                barrier.SignalAndWait();
                Stopwatch local = Stopwatch.StartNew();
                long n = 0;
                while (local.ElapsedMilliseconds < 400)
                {
                  if (write) foreach (object s in invoiceSamples) c.Write(s);
                  else foreach (byte[] p in c.Payloads) c.Read(p);
                  n += invoiceSamples.Length;
                }
                System.Threading.Interlocked.Add(ref done, n);
              })).ToArray();
              Stopwatch sw = Stopwatch.StartNew();
              foreach (var t in ts) t.Start();
              foreach (var t in ts) t.Join();
              if (pass > 0) max = Math.Max(max, done / sw.Elapsed.TotalSeconds);
            }
            return max;
          }
          double w = best(true), r = best(false);
          if (threads == 1) { write1 = w; read1 = r; }
          Console.WriteLine($"{name,-36} {threads,7} {w,12:N0} {w / write1,7:N2}x {r,12:N0} {r / read1,7:N2}x");
        }
      }
      return;
    }

    if (mode == "invoices")
    {
      Report("Invoices", CreateCandidates(typeof(Invoice), invoiceSamples), invoiceSamples, 20, seconds);
      return;
    }

    if (mode == "small")
    {
      object[] addresses = Enumerable.Range(1, 100).Select(i => (object)GetAddress(i)).ToArray();
      Report("One Address per call (6 strings)", CreateCandidates(typeof(Address), addresses), addresses, 200, seconds);
      object[] customers = Enumerable.Range(1, 100).Select(i => (object)GetCustomer(i)).ToArray();
      Report("One Customer per call (6 values + 2 Addresses)", CreateCandidates(typeof(Customer), customers), customers, 100, seconds);
      return;
    }

    if (mode == "phases")
    {
      MsgPackSettings indexed = new MsgPackSettings() { UseInexedSchema = true };
      CachedSchemaWriter writer = new CachedSchemaWriter(indexed, typeof(Invoice), invoiceSamples);
      CachedSchemaReader reader = new CachedSchemaReader(writer.Schema, indexed, typeof(Invoice));
      byte[][] payloads = invoices.Select(i => writer.Serialize(i)).ToArray();
      double minBuild = double.MaxValue, minBoth = double.MaxValue, minUnpack = double.MaxValue, minConvert = double.MaxValue;
      MsgPackItem[] trees = new MsgPackItem[payloads.Length];
      for (int pass = 0; pass < 12; pass++)
      {
        Stopwatch sw = Stopwatch.StartNew();
        for (int r = 0; r < 20; r++)
          foreach (Invoice inv in invoices)
            writer.BuildTree(inv);
        double build = sw.Elapsed.TotalMilliseconds; sw.Restart();
        for (int r = 0; r < 20; r++)
          foreach (Invoice inv in invoices)
            writer.BuildTree(inv).ToBytes();
        double both = sw.Elapsed.TotalMilliseconds; sw.Restart();
        for (int r = 0; r < 20; r++)
          for (int p = 0; p < payloads.Length; p++)
            reader.UnpackOnly(payloads[p]);
        double unpack = sw.Elapsed.TotalMilliseconds;
        for (int p = 0; p < payloads.Length; p++) trees[p] = reader.UnpackOnly(payloads[p]); // converting does not change the trees
        sw.Restart();
        for (int r = 0; r < 20; r++)
          for (int p = 0; p < payloads.Length; p++)
            reader.ConvertOnly(trees[p], typeof(Invoice));
        double convert = sw.Elapsed.TotalMilliseconds;
        if (pass >= 2)
        {
          minBuild = Math.Min(minBuild, build); minBoth = Math.Min(minBoth, both);
          minUnpack = Math.Min(minUnpack, unpack); minConvert = Math.Min(minConvert, convert);
        }
      }
      Console.WriteLine($"cached write: tree build {minBuild,6:N1} ms + ToBytes {minBoth - minBuild,6:N1} ms (total {minBoth,6:N1} ms)");
      Console.WriteLine($"cached read : Unpack (bytes -> item tree) {minUnpack,6:N1} ms + Convert (tree -> objects) {minConvert,6:N1} ms");
      return;
    }
  }
}
