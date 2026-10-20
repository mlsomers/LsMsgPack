using LsMsgPackFormatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

// Listens on a free port of 127.0.0.1, writes its address as the first line, and stops when its standard input closes (the test ends).
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddControllers()
  .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null) // the names of the classes, as the schema has them
  .AddLsMsgPackSerializerFormatters();

WebApplication app = builder.Build();
app.MapControllers();
await app.StartAsync();

string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
Console.WriteLine(address);
Console.Out.Flush();

await Task.Run(() => { while (Console.In.ReadLine() != null) { } });
await app.StopAsync();
