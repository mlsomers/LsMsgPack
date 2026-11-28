using LsMsgPackFormatters;
using LsMsgPackTsTestServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Listens on a free port of 127.0.0.1, writes its address as the first line, and stops when its standard input closes (the test ends).
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddControllers()
  .AddJsonOptions(o =>
  {
    o.JsonSerializerOptions.PropertyNamingPolicy = null; // the names of the classes, as the schema has them
    o.JsonSerializerOptions.IncludeFields = true; // Motion: the fields of Vector3 and the opted in ones (no model has other public fields)
  })
  .AddLsMsgPackSerializerFormatters();
builder.Services.AddSignalR(o => o.MaximumParallelInvocationsPerClient = 2) // 2: a hub method waits for a client result
  .AddLsMsgPackProtocol()
  .AddLsMsgPackIndexedProtocol()
  .AddLsMsgPackAsMessagePackProtocol();

WebApplication app = builder.Build();
app.MapControllers();
app.MapHub<LsMsgPackTsTestServer.TestHub>("/hub");
// bench/signalr.mjs: an invocation as the server's protocol of that name writes it, "Send" with a chat message (count 0) or "List" with count of them
app.MapGet("/hub-message", (string name, int count, IEnumerable<IHubProtocol> protocols) =>
{
  IHubProtocol protocol = protocols.First(p => p.Name == name);
  object first = count == 0 ? ChatMessage.Sample(1) : Enumerable.Range(0, count).Select(ChatMessage.Sample).ToList();
  return Results.Bytes(protocol.GetMessageBytes(new InvocationMessage(count == 0 ? "Send" : "List", new object[] { first, 7 })).ToArray(), "application/octet-stream");
});
await app.StartAsync();

string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
Console.WriteLine(address);
Console.Out.Flush();

await Task.Run(() => { while (Console.In.ReadLine() != null) { } });
await app.StopAsync();
