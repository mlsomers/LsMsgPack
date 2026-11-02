// A/B benchmark of LtMsgPack.Data and the stream writer/reader (data/setup.sh builds it against the working tree and a base ref), ns and allocated bytes per row (best run):
// 10 000 invoice rows of 10 columns (the data tests' Data.Invoices, an in-memory ColumnarReader) and 10 000 orders of 5 lines in a head's body.
// Modes (comma separated, or all) and seconds per mode: dump-read|dump-values|dump-typed|dump-rows|dump-async (MsgPackDataReader over a dump, at its index entry),
// readrows, serrows, serrows-stream, table (WriteTable), stream-write|stream-read|stream-read-async (WriteObject/ReadObject per element), serialize-list|deserialize-list.
// Other modes: percall (the closures of the libraries' assemblies, and bytes and ns per Serialize/Deserialize of one small object), allocs <mode> (allocated types, sampled), sizes.
using LsMsgPack.Streaming;
using LtMsgPack;
using LtMsgPack.Data;
using LtMsgPack.DataTests;
using LtMsgPack.Streaming;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;

public class Line { public string Product { get; set; } public int Quantity { get; set; } public decimal Price { get; set; } }
public class Order { public int Id { get; set; } public string Customer { get; set; } public DateTime Date { get; set; } public List<Line> Lines { get; set; } }

public static class Program
{
  const int Rows = 10000;
  static LtMsgPackSerializer lt = new LtMsgPackSerializer();
  static List<InvoiceRow> rows = Data.Invoices(Rows);
  static ColumnarReader mem = Data.InvoiceReader(rows);
  static byte[] dump; static MsgPackStreamEntry entry;
  static List<Order> orders = new List<Order>();
  static byte[] orderStream;
  static MemoryStream sink = new MemoryStream();

  static void Setup()
  {
    MemoryStream ms = new MemoryStream();
    using (MsgPackStreamWriter w = new MsgPackStreamWriter(ms, lt, leaveOpen: true, index: new MsgPackStreamIndexOptions()))
      w.WriteTable(Data.InvoiceReader(rows), "Invoices");
    dump = ms.ToArray();
    using (MsgPackStreamReader r = new MsgPackStreamReader(new MemoryStream(dump), lt))
      entry = r.ReadIndexes()[0].Entries[0];
    for (int t = 0; t < Rows; t++)
    {
      List<Line> lines = new List<Line>();
      for (int l = 0; l < 5; l++) lines.Add(new Line() { Product = "Product " + (t * 7 + l) % 100, Quantity = l + 1, Price = 1.25m * (l + t % 13) });
      orders.Add(new Order() { Id = t, Customer = "Customer " + t % 500, Date = new DateTime(2026, 1, 1).AddMinutes(t), Lines = lines });
    }
    orderStream = StreamWrite();
  }

  static IDataReader Fresh() { mem.Reset(); return mem; }
  static MsgPackDataReader AtEntry() { return new MsgPackDataReader(new MsgPackStreamReader(new MemoryStream(dump), lt), entry); }

  static byte[] StreamWrite()
  {
    sink.SetLength(0);
    using (MsgPackStreamWriter w = new MsgPackStreamWriter(sink, lt, leaveOpen: true))
    {
      w.WriteHead<List<Order>>();
      w.WriteArrayStart();
      for (int t = 0; t < orders.Count; t++) w.WriteObject(orders[t]);
      w.WriteEnd();
    }
    return sink.ToArray();
  }

  static long Run(string mode)
  {
    switch (mode)
    {
      case "dump-read": using (MsgPackDataReader r = AtEntry()) { int n = 0; while (r.Read()) n++; return n; }
      case "dump-values": { object[] v = new object[10]; using (MsgPackDataReader r = AtEntry()) { int n = 0; while (r.Read()) n += r.GetValues(v); return n; } }
      case "dump-typed":
        using (MsgPackDataReader r = AtEntry())
        {
          long n = 0;
          while (r.Read())
          {
            n += r.GetInt32(0) + r.GetString(1).Length + r.GetString(2).Length + r.GetDateTime(3).Ticks + (long)r.GetDecimal(4) + (long)r.GetDecimal(5)
              + (r.GetBoolean(6) ? 1 : 0) + r.GetGuid(7).GetHashCode() + r.GetInt32(8) + (r.IsDBNull(9) ? 0 : r.GetString(9).Length);
          }
          return n;
        }
      case "dump-rows": using (MsgPackDataReader r = AtEntry()) return lt.ReadRows<InvoiceRow>(r).Count;
      case "dump-async": { object[] v = new object[10]; return DumpAsync(v).GetAwaiter().GetResult(); }
      case "readrows": return lt.ReadRows<InvoiceRow>(Fresh()).Count;
      case "serrows": return lt.SerializeRows<InvoiceRow>(Fresh()).Length;
      case "serrows-stream": return lt.SerializeRows<InvoiceRow>(Fresh(), Stream.Null);
      case "table": using (MsgPackStreamWriter w = new MsgPackStreamWriter(Stream.Null, lt)) return w.WriteTable(Fresh(), "Invoices");
      case "stream-write": return StreamWrite().Length;
      case "serialize-list": return lt.Serialize(orders).Length;
      case "stream-read":
        using (MsgPackStreamReader r = new MsgPackStreamReader(new MemoryStream(orderStream), lt))
        {
          r.ReadHead<List<Order>>(); r.Read(); long n = 0;
          while (!r.AtEnd) n += r.ReadObject<Order>().Id;
          return n;
        }
      case "stream-read-async": return StreamReadAsync().GetAwaiter().GetResult();
      case "deserialize-list": return lt.Deserialize<List<Order>>(listBytes ?? (listBytes = lt.Serialize(orders))).Count;
    }
    throw new ArgumentException(mode);
  }
  static byte[] listBytes;

  static async System.Threading.Tasks.Task<long> StreamReadAsync()
  {
    using (MsgPackStreamReader r = new MsgPackStreamReader(new MemoryStream(orderStream), lt))
    {
      await r.ReadHeadAsync<List<Order>>(); await r.ReadAsync(); long n = 0;
      while (!await r.AtEndAsync()) n += (await r.ReadObjectAsync<Order>()).Id;
      return n;
    }
  }

  static async System.Threading.Tasks.Task<int> DumpAsync(object[] v)
  {
    using (MsgPackDataReader r = AtEntry()) { int n = 0; while (await r.ReadAsync()) n += r.GetValues(v); return n; }
  }

  static readonly string[] All = { "dump-read", "dump-values", "dump-typed", "dump-rows", "dump-async", "readrows", "serrows", "serrows-stream", "table", "stream-write", "serialize-list", "stream-read", "stream-read-async", "deserialize-list" };

  sealed class AllocListener : System.Diagnostics.Tracing.EventListener
  {
    public Dictionary<string, long> Types = new Dictionary<string, long>(); public bool On;
    protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource s)
    { if (s.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(s, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1); }
    protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
    {
      if (!On || e.EventName != "GCAllocationTick_V4") return;
      string t = (string)e.Payload[e.PayloadNames.IndexOf("TypeName")];
      lock (Types) { Types.TryGetValue(t, out long n); Types[t] = n + 1; }
    }
  }

  public static void Main(string[] args)
  {
    Setup();
    if (args[0] == "percall")
    {
      foreach (System.Reflection.Assembly asm in new[] { typeof(LtMsgPackSerializer).Assembly, typeof(MsgPackDataReader).Assembly, typeof(LsMsgPack.MsgPackOptions).Assembly })
      foreach (Type t in asm.GetTypes())
        if (t.Name.Contains("DisplayClass"))
        { Console.Write(t.DeclaringType.FullName + "." + t.Name + ":"); foreach (var f in t.GetFields()) Console.Write(" " + f.Name); Console.WriteLine(); }
      Line one = new Line() { Product = "abc", Quantity = 2, Price = 1.5m };
      LtMsgPackSerializer named = new LtMsgPackSerializer(new LtMsgPackOptions() { UseInexedSchema = false });
      foreach (LtMsgPackSerializer ser in new[] { lt, named })
      {
        byte[] b = ser.Serialize(one);
        for (int w = 0; w < 100000; w++) { ser.Serialize(one); ser.Deserialize<Line>(b); }
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int w = 0; w < 100000; w++) ser.Serialize(one);
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        for (int w = 0; w < 100000; w++) ser.Deserialize<Line>(b);
        long a2 = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch sw = Stopwatch.StartNew(); for (int w = 0; w < 2000000; w++) ser.Serialize(one); double ws = sw.Elapsed.TotalMilliseconds * 1e6 / 2000000;
        sw.Restart(); for (int w = 0; w < 2000000; w++) ser.Deserialize<Line>(b); double rs = sw.Elapsed.TotalMilliseconds * 1e6 / 2000000;
        Console.WriteLine($"{(ser == lt ? "indexed" : "named  ")}: Serialize {(a1 - a0) / 100000} B {ws:0.0} ns, Deserialize {(a2 - a1) / 100000} B {rs:0.0} ns (payload {b.Length} B)");
      }
      return;
    }
    if (args[0] == "sizes") { Console.WriteLine($"SerializeRows {lt.SerializeRows<InvoiceRow>(Fresh()).Length} B, dump {dump.Length} B, orders {orderStream.Length} B"); return; }
    if (args[0] == "allocs")
    {
      for (int w = 0; w < 20; w++) Run(args[1]);
      using (AllocListener l = new AllocListener())
      {
        l.On = true;
        for (int w = 0; w < 200; w++) Run(args[1]);
        l.On = false;
        List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>(l.Types);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        long sum = 0; foreach (var kv in list) sum += kv.Value;
        foreach (var kv in list) Console.WriteLine($"{kv.Value * 100.0 / sum,6:0.0}%  {kv.Key}");
      }
      return;
    }
    double seconds = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 2;
    string[] modes = args[0] == "all" ? All : args[0].Split(',');
    foreach (string mode in modes)
    {
      for (int w = 0; w < 20; w++) Run(mode);
      double best = double.MaxValue; long alloc = 0;
      Stopwatch total = Stopwatch.StartNew();
      while (total.Elapsed.TotalSeconds < seconds)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch sw = Stopwatch.StartNew();
        Run(mode);
        double ns = sw.Elapsed.TotalMilliseconds * 1e6 / Rows;
        alloc = (GC.GetAllocatedBytesForCurrentThread() - before) / Rows;
        if (ns < best) best = ns;
      }
      Console.WriteLine($"{mode,-18} {best,8:0.0} ns/row {alloc,6} B/row");
    }
  }
}
