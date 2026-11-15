// End-to-end with many endpoints: a backend in the Vertical Slice Architecture (WebManyTypes.cs, made by webmany-types.py: 200 slices, each with its own
// Request/Response classes, even ones GET, odd ones POST) on ASP.NET Core's TestServer, called by parallel clients that pick the endpoints at random.
// Hosts: minimal APIs (MsgPackBody<T>, MsgPackResults.MsgPack; /s/<area>/<name>[/{id}]) and MVC controllers with the formatters (/m/...), the same slices.
// Formats (Accept and request Content-Type):
//   json        System.Text.Json (web defaults, as ASP.NET Core)
//   ls          application/x-lsmsgpack through LsMsgPackSchemaHandler (negotiated: the response refers to a schema the client advertises, inline otherwise)
//   ls-inline   application/x-lsmsgpack without the handler (every response has its schema inline)
//   msgpack     application/msgpack (names, no schema)
// Picks: uniform (the 200 endpoints alike), skewed (10 endpoints 80% of the calls), one (endpoint 0 only, for comparison)
// The responses are read as bytes (the client does not deserialize them): the time is the server's, TestServer's and HttpClient's.
// Per row: requests per second (all clients), wall and CPU µs per request (CPU = process CPU time / requests: what a busy server pays per request),
// allocated KB per request (process wide), gen0/gen1/gen2 collections per 1000 requests, bytes per response body, % of x-lsmsgpack responses with a schema reference.
// The CPU time includes the thread pool's spinning, which varies from run to run: DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 for A/B runs.
// Usage: dotnet webmany-new.dll [seconds per row=3] [host=minimal,mvc] [format=json,ls,ls-inline,msgpack] [pick=uniform,skewed,one] [clients=1,8,32]
//        dotnet webmany-new.dll one <host> <format> <pick> <clients> <seconds>     one row, for perf
// Run it in a memory limited scope (scripts/limit.sh): server GC (the default of ASP.NET Core) takes what the machine has.
using LsMsgPack;
using LsMsgPack.Http;
using LsMsgPackFormatters;
using LsMsgPackWebApiFormatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class Program
{
  const int Variants = 4; // responses and request bodies per slice
  static readonly string[] Formats = { "json", "ls", "ls-inline", "msgpack" };

  static byte[][][] _bodies; // [format][slice * Variants + v], null for queries
  static HttpClient _negotiating, _plain;
  static Uri _base;

  public static async Task Main(string[] args)
  {
    Fill();
    Dictionary<string, string> named = new Dictionary<string, string>();
    bool one = args.Length > 0 && args[0] == "one";
    double seconds = 3;
    if (!one)
    {
      for (int a = 0; a < args.Length; a++)
      {
        int eq = args[a].IndexOf('=');
        if (eq > 0) named[args[a].Substring(0, eq)] = args[a].Substring(eq + 1);
        else seconds = double.Parse(args[a], CultureInfo.InvariantCulture);
      }
    }
    string[] hosts = one ? new[] { args[1] } : Get(named, "host", "minimal,mvc");
    string[] formats = one ? new[] { args[2] } : Get(named, "format", "json,ls,ls-inline,msgpack");
    string[] picks = one ? new[] { args[3] } : Get(named, "pick", "uniform,skewed,one");
    int[] clients = (one ? new[] { args[4] } : Get(named, "clients", "1,8,32")).Select(int.Parse).ToArray();
    if (one) seconds = double.Parse(args[5], CultureInfo.InvariantCulture);

    IHost host = await new HostBuilder()
      .ConfigureLogging(l => l.ClearProviders())
      .ConfigureWebHost(web => web.UseTestServer()
        .ConfigureServices(s =>
        {
          s.AddRouting();
          s.AddLsMsgPack();
          s.AddControllers().AddApplicationPart(typeof(Program).Assembly).AddLsMsgPackSerializerFormatters();
        })
        .Configure(app => app.UseRouting().UseEndpoints(e => { e.MapControllers(); Slices.MapMinimal(e); })))
      .StartAsync();
    TestServer server = host.GetTestServer();
    _base = server.BaseAddress;
    LsMsgPackHttpSerializer clientSide = new LsMsgPackHttpSerializer();
    _negotiating = new HttpClient(new LsMsgPackSchemaHandler(clientSide, server.CreateHandler())) { BaseAddress = _base };
    _plain = new HttpClient(server.CreateHandler()) { BaseAddress = _base };
    Bodies(clientSide);

    Console.WriteLine($"{Environment.Version}, server GC {System.Runtime.GCSettings.IsServerGC}, {Environment.ProcessorCount} cores, {Slices.N} endpoints, {seconds} s per row");
    // warm-up: every endpoint in every format of the run (compiling the plans, tiering), then a few seconds of each
    for (int h = 0; h < hosts.Length; h++)
      for (int f = 0; f < formats.Length; f++)
      {
        for (int e = 0; e < Slices.N; e++)
          for (int v = 0; v < Variants; v++)
            await Call(hosts[h], formats[f], e, v);
        await Run(hosts[h], formats[f], "uniform", 4, one ? 2 : 1);
      }

    Console.WriteLine($"{"host",-8} {"format",-10} {"pick",-8} {"clients",7} {"req/s",9} {"wall µs",8} {"CPU µs",8} {"KB/req",7} {"gc0/1/2 per 1000",17} {"B/resp",7} {"refs",5}  peak MB");
    for (int h = 0; h < hosts.Length; h++)
      for (int f = 0; f < formats.Length; f++)
        for (int p = 0; p < picks.Length; p++)
          for (int c = 0; c < clients.Length; c++)
          {
            Row best = null;
            for (int pass = 0; pass < 3; pass++)
            {
              Row row = await Run(hosts[h], formats[f], picks[p], clients[c], seconds / 3);
              if (best == null || row.CpuUs < best.CpuUs) best = row;
            }
            Console.WriteLine($"{hosts[h],-8} {formats[f],-10} {picks[p],-8} {clients[c],7} {best.PerSecond,9:F0} {best.WallUs,8:F1} {best.CpuUs,8:F1} {best.KB,7:F1} {best.Gc,17} {best.Bytes,7:F0} {best.Refs,5}  {Process.GetCurrentProcess().PeakWorkingSet64 >> 20}");
          }
    await host.StopAsync();
  }

  static string[] Get(Dictionary<string, string> named, string key, string fallback) => (named.TryGetValue(key, out string v) ? v : fallback).Split(',');

  sealed class Row
  {
    public double PerSecond, WallUs, CpuUs, KB, Bytes;
    public string Gc, Refs;
  }

  static async Task<Row> Run(string host, string format, string pick, int clients, double seconds)
  {
    int[] gc = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
    long allocated = GC.GetTotalAllocatedBytes(false);
    TimeSpan cpu = Process.GetCurrentProcess().TotalProcessorTime;
    long calls = 0, refs = 0, bytes = 0;
    Stopwatch sw = Stopwatch.StartNew();
    Task[] workers = new Task[clients];
    for (int c = 0; c < clients; c++)
    {
      int seed = 17 + c;
      workers[c] = Task.Run(async () =>
      {
        Random random = new Random(seed);
        long mine = 0, myRefs = 0, myBytes = 0;
        while (sw.Elapsed.TotalSeconds < seconds)
        {
          for (int i = 0; i < 16; i++)
          {
            int e = pick == "one" ? 0 : pick == "skewed" ? (random.Next(10) < 8 ? random.Next(10) : 10 + random.Next(Slices.N - 10)) : random.Next(Slices.N);
            int length = await Call(host, format, e, random.Next(Variants));
            if (length < 0)
              myRefs++;
            myBytes += Math.Abs(length);
          }
          mine += 16;
        }
        Interlocked.Add(ref calls, mine);
        Interlocked.Add(ref refs, myRefs);
        Interlocked.Add(ref bytes, myBytes);
      });
    }
    await Task.WhenAll(workers);
    double elapsed = sw.Elapsed.TotalMilliseconds;
    double cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds;
    return new Row
    {
      PerSecond = calls * 1000 / elapsed,
      WallUs = elapsed * 1000 / calls,
      CpuUs = cpuMs * 1000 / calls,
      KB = (GC.GetTotalAllocatedBytes(false) - allocated) / 1024.0 / calls,
      Bytes = bytes / (double)calls,
      Gc = $"{(GC.CollectionCount(0) - gc[0]) * 1000.0 / calls:F2}/{(GC.CollectionCount(1) - gc[1]) * 1000.0 / calls:F2}/{(GC.CollectionCount(2) - gc[2]) * 1000.0 / calls:F2}",
      Refs = format.StartsWith("ls", StringComparison.Ordinal) ? $"{refs * 100.0 / calls:F0}%" : "",
    };
  }

  /// <returns>the length of the response body, negative when it refers to its schema</returns>
  static async Task<int> Call(string host, string format, int slice, int variant)
  {
    var s = Slices.All[slice];
    string prefix = host == "mvc" ? "m/" : "s/";
    string mediaType = MediaType(format);
    HttpRequestMessage request;
    if (s.Get)
      request = new HttpRequestMessage(HttpMethod.Get, new Uri(_base, prefix + s.Path + "/" + variant.ToString(CultureInfo.InvariantCulture)));
    else
    {
      request = new HttpRequestMessage(HttpMethod.Post, new Uri(_base, prefix + s.Path)) { Content = new ByteArrayContent(_bodies[Array.IndexOf(Formats, format)][slice * Variants + variant]) };
      request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
    }
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
    using (request)
    using (HttpResponseMessage response = await (format == "ls" ? _negotiating : _plain).SendAsync(request))
    {
      byte[] body = await response.Content.ReadAsByteArrayAsync();
      if (!response.IsSuccessStatusCode || body.Length == 0)
        throw new Exception($"{host} {format} {slice}: {(int)response.StatusCode} {Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 300))}");
      return format.StartsWith("ls", StringComparison.Ordinal) && body[0] == (byte)MsgPackTypeId.MpFExt16 ? -body.Length : body.Length;
    }
  }

  static string MediaType(string format)
  {
    switch (format)
    {
      case "json": return "application/json";
      case "ls": case "ls-inline": return MsgPackMediaTypes.XLsMsgPack;
      case "msgpack": return MsgPackMediaTypes.MsgPack;
      default: throw new ArgumentException("format: json, ls, ls-inline or msgpack");
    }
  }

  static void Bodies(LsMsgPackHttpSerializer clientSide)
  {
    System.Text.Json.JsonSerializerOptions web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
    _bodies = new byte[Formats.Length][][];
    for (int f = 0; f < Formats.Length; f++)
      _bodies[f] = new byte[Slices.N * Variants][];
    for (int e = 0; e < Slices.N; e++)
    {
      var s = Slices.All[e];
      if (s.Get)
        continue;
      for (int v = 0; v < Variants; v++)
      {
        object value = Create(s.Request, new Random(e * 31 + v), s.ListLength);
        s.Request.GetProperty("Id").SetValue(value, v);
        for (int f = 0; f < Formats.Length; f++)
          _bodies[f][e * Variants + v] = Formats[f] == "json" ? System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, s.Request, web)
            : clientSide.Serialize(value, s.Request, MediaType(Formats[f])).ToArray();
      }
    }
  }

  /// <summary>The responses of the handlers: Data&lt;T&gt;.Values per slice, filled with random values (seeded).</summary>
  static void Fill()
  {
    for (int e = 0; e < Slices.N; e++)
    {
      var s = Slices.All[e];
      Array values = Array.CreateInstance(s.Response, Variants);
      for (int v = 0; v < Variants; v++)
        values.SetValue(Create(s.Response, new Random(e * 7919 + v), s.ListLength), v);
      typeof(Data<>).MakeGenericType(s.Response).GetField("Values").SetValue(null, values);
    }
  }

  static readonly string[] Words = { "alpha", "Bravo", "charlie delta", "Échelle", "foxtrot", "golf hotel india", "juliett", "kilo", "lima-mike", "november", "oscar papa quebec romeo", "s" };

  static object Create(Type type, Random r, int listLength)
  {
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
    {
      IList list = (IList)Activator.CreateInstance(type);
      for (int i = 0; i < listLength; i++)
        list.Add(Create(type.GetGenericArguments()[0], r, listLength));
      return list;
    }
    Type nullable = Nullable.GetUnderlyingType(type);
    if (nullable != null)
      return r.Next(3) == 0 ? null : Create(nullable, r, listLength);
    if (type == typeof(int)) return r.Next(3) == 0 ? r.Next(100) : r.Next();
    if (type == typeof(long)) return r.Next(2) == 0 ? (long)r.Next(1000) : (long)r.Next() * r.Next();
    if (type == typeof(string)) return r.Next(10) == 0 ? null : Words[r.Next(Words.Length)] + (r.Next(2) == 0 ? "" : r.Next(10000).ToString(CultureInfo.InvariantCulture));
    if (type == typeof(bool)) return r.Next(2) == 0;
    if (type == typeof(decimal)) return r.Next(4) == 0 ? 0m : Math.Round((decimal)r.NextDouble() * 10000, r.Next(3));
    if (type == typeof(double)) return r.NextDouble() * 1000;
    if (type == typeof(DateTime)) return new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(r.Next(200_000_000));
    if (type == typeof(Guid)) { byte[] g = new byte[16]; r.NextBytes(g); return new Guid(g); }
    if (type.IsEnum) return Enum.ToObject(type, r.Next(4));
    object o = Activator.CreateInstance(type);
    foreach (PropertyInfo p in type.GetProperties())
      p.SetValue(o, Create(p.PropertyType, r, listLength));
    return o;
  }
}
