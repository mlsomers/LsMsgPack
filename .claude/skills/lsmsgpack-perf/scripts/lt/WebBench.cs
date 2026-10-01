// End-to-end: ASP.NET Core (TestServer, in process) with the LsMsgPack formatters vs System.Text.Json.
// Usage: <exe> <seconds per case>
using LsMsgPack;
using LsMsgPackFormatters;
using LtMsgPack.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

[ApiController]
[Route("b")]
public class BenchController : ControllerBase
{
  internal static Bench.Invoice[] Invoices = Bench.Invoices;
  private static int _next;

  [HttpGet("invoice")]
  public Bench.Invoice Get() => Invoices[(_next++ & 0x7FFFFFFF) % Invoices.Length];

  [HttpPost("invoice")]
  public int Post(Bench.Invoice invoice) => invoice.Lines.Count;
}

public static class Program
{
  public static async Task Main(string[] args)
  {
    double seconds = args.Length > 0 ? double.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 3;
    IHost host = await new HostBuilder()
      .ConfigureLogging(l => l.ClearProviders())
      .ConfigureWebHost(web => web.UseTestServer()
        .ConfigureServices(s => s.AddControllers().AddApplicationPart(typeof(BenchController).Assembly).AddLsMsgPackSerializerFormatters())
        .Configure(app => app.UseRouting().UseEndpoints(e => e.MapControllers())))
      .StartAsync();
    HttpClient client = host.GetTestClient();
    LtMsgPackHttpSerializer clientSide = new LtMsgPackHttpSerializer();

    var stj = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
    byte[][] json = Bench.Invoices.Select(i => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(i, stj)).ToArray();
    byte[][] ls = Bench.Invoices.Select(i => clientSide.Serialize(i, typeof(Bench.Invoice), MsgPackMediaTypes.XLsMsgPack).ToArray()).ToArray();
    byte[][] plain = Bench.Invoices.Select(i => clientSide.Serialize(i, typeof(Bench.Invoice), MsgPackMediaTypes.MsgPack).ToArray()).ToArray();

    // the schema header of the x-lsmsgpack responses (as LsMsgPackSchemaHandler would send it)
    HttpRequestMessage first = new HttpRequestMessage(HttpMethod.Get, "/b/invoice");
    first.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));
    first.Headers.Add(LtMsgPackHttpSerializer.SchemasHeader, "00");
    HttpResponseMessage r0 = await client.SendAsync(first);
    string schemaId = r0.Headers.TryGetValues(LtMsgPackHttpSerializer.SchemaHeader, out var ids) ? ids.First() : null;

    var cases = new List<(string, Func<int, HttpRequestMessage>)>
    {
      ("GET  json", i => Get("application/json", null)),
      ("GET  x-lsmsgpack", i => Get(MsgPackMediaTypes.XLsMsgPack, null)),
      ("GET  x-lsmsgpack ref", i => Get(MsgPackMediaTypes.XLsMsgPack, schemaId)),
      ("GET  msgpack", i => Get(MsgPackMediaTypes.MsgPack, null)),
      ("POST json", i => Post(json[i % json.Length], "application/json")),
      ("POST x-lsmsgpack", i => Post(ls[i % ls.Length], MsgPackMediaTypes.XLsMsgPack)),
      ("POST msgpack", i => Post(plain[i % plain.Length], MsgPackMediaTypes.MsgPack)),
    };
    foreach (var (name, make) in cases)
    {
      double best = double.MaxValue; long bytes = 0;
      Stopwatch total = Stopwatch.StartNew();
      int rounds = 0;
      do
      {
        Stopwatch sw = Stopwatch.StartNew();
        const int N = 2000;
        for (int i = 0; i < N; i++)
        {
          HttpResponseMessage response = await client.SendAsync(make(i));
          byte[] body = await response.Content.ReadAsByteArrayAsync();
          if (!response.IsSuccessStatusCode) throw new Exception(name + " " + response.StatusCode);
          bytes = body.Length;
        }
        if (++rounds > 1) best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000 / N);
      } while (total.Elapsed.TotalSeconds < seconds || rounds < 3);
      Console.WriteLine($"{name,-22} {best,7:N2} us/request  (last response {bytes} bytes)");
    }
    await host.StopAsync();
  }

  static HttpRequestMessage Get(string accept, string schemas)
  {
    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/b/invoice");
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
    if (schemas != null) request.Headers.Add(LtMsgPackHttpSerializer.SchemasHeader, schemas);
    return request;
  }

  static HttpRequestMessage Post(byte[] body, string contentType)
  {
    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/b/invoice") { Content = new ByteArrayContent(body) };
    request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    return request;
  }
}
