[Setup wizard](README.md) › [Sent](Network.md) › [My .NET programs](NetworkDotNet.md) › HTTP

# Setup: .NET to .NET over HTTP

An ASP.NET API and a .NET client (WPF, WinForms, MAUI, Avalonia, Blazor, a console app or another service) that you both control.

## In short

| | Server | Client |
|---|---|---|
| Package | `LsMsgPack.AspNetCore` (ASP.NET Core) | `LsMsgPack.AspNet.WebApi` (`HttpClient`, any platform) |
| Setup | `AddLsMsgPackSerializerFormatters()` | `LsMsgPackMediaTypeFormatter` + `formatter.CreateHandler()` |
| Media type | `application/x-lsmsgpack` | |
| Models | a shared class library referenced by both (recommended) | |

```
dotnet add Shop.Api package LsMsgPack.AspNetCore
dotnet add Shop.App package LsMsgPack.AspNet.WebApi
```

ASP.NET Web API 2 or MVC 5 on .NET Framework: `LsMsgPack.AspNet.WebApi` or `LsMsgPack.AspNet.Mvc` on the server, see [WebFormatters.md](../WebFormatters.md). The client side is the same.

## The server

```csharp
using LsMsgPackFormatters;

builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters();
```

That's all. Your controllers don't change:

```csharp
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
  [HttpGet("{id}")]
  public ActionResult<Order> Get(int id) => _orders.Find(id);

  [HttpPost]
  public ActionResult<Order> Post(Order order) => _orders.Add(order);
}
```

JSON keeps working for every client that doesn't ask for MsgPack (browsers, Swagger UI, `curl`). A client gets MsgPack by sending `Accept: application/x-lsmsgpack`, and sends MsgPack with `Content-Type: application/x-lsmsgpack`.

Minimal APIs instead of controllers:

```csharp
builder.Services.AddLsMsgPack();

app.MapGet("/api/orders/{id}", (int id) => MsgPackResults.MsgPack(orders.Find(id)));
app.MapPost("/api/orders", (MsgPackBody<Order> order) => MsgPackResults.MsgPack(orders.Add(order.Value)));
```

## The client

```csharp
using LsMsgPack;
using LsMsgPackWebApiFormatters;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;

// Once, for the lifetime of the app (a static field, or a singleton in your DI container)
LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
HttpClient client = new HttpClient(formatter.CreateHandler()) { BaseAddress = new Uri("https://api.example.com/") };
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));

// Per call
Order order = await (await client.GetAsync("api/orders/42")).EnsureSuccessStatusCode()
  .Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter });

HttpResponseMessage response = await client.PostAsync("api/orders",
  new ObjectContent<Order>(newOrder, formatter, MsgPackMediaTypes.XLsMsgPack));
Order saved = await response.EnsureSuccessStatusCode().Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter });
```

With `IHttpClientFactory` (ASP.NET Core, a worker service, MAUI with DI):

```csharp
LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
services.AddSingleton(formatter);
services.AddHttpClient("api", c => c.BaseAddress = new Uri("https://api.example.com/"))
  .AddHttpMessageHandler(() => formatter.CreateDelegatingHandler());
```

## Why these settings

- **`application/x-lsmsgpack`** is LsMsgPack's own format. A response starts with a schema (the property names of each class, once), followed by the values. Classes in a hierarchy (an `Animal` property holding a `Dog`) are sent with their class name, and the client creates the same classes, without attributes.
- **`formatter.CreateHandler()`** makes the schema travel only once. The handler tells the server which schemas it already has (a header with their ids), and the server then sends an 18-byte reference instead of the schema. Without the handler everything still works, the schema is just in every response.
- **A shared class library** with the request and response classes (`Shop.Contracts`), referenced by both projects, means both sides always agree on the classes. It isn't required (see below).

## Changing your classes later (rolling upgrades)

Server and clients are rarely updated at the same moment: the server is deployed first, and users update their app days later, or never. Values are matched by the property names in the schema, so:

| Change on one side | The other side |
|---|---|
| Add a property | An older reader skips it; a request from an older client leaves it at the value the constructor sets. |
| Remove a property | Its value from an older writer is skipped. |
| Reorder properties | No effect. |
| Rename a property | The value is lost between old and new. Add the new property, keep the old one until all clients are updated. |
| Rename a class that is sent with its name (a `Dog` in an `Animal` property) | Old clients can't find the new name. Give such classes stable names with `[XmlRoot("Dog")]` and the `XmlRootAttributeTypeResolver`, on both sides: see [documents](Setup-Documents.md#changing-your-classes-later). |

To see what didn't match while you test (a property the client sends that the server doesn't know, a misspelled name), turn on `ReportDifferences` and look at the log:

```csharp
builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(o => o.ReportDifferences = true);
```

**Client classes that differ from the server's.** If the client has its own classes (other names or namespaces, generated by another tool), ask for the described schema with `formatter.CreateHandler(true)`. It tells the client the declared type of every property, so the client can pair its classes with the server's by their structure.

## Watch out

- **Requests come from the internet**, even if only your app is meant to call the API: anyone can send a request. Avoid `object` properties in request classes, and see [Add-ons](Extras.md#reading-data-you-dont-trust) for the type guard.
- **Set the `Accept` header**, or the server answers with JSON (which `ReadAsAsync` reads too, but slower and bigger).
- **Request bodies always carry their schema.** The client can't know whether the server (or the instance behind a load balancer) still holds it, so only responses use references. The server recognizes repeated schemas cheaply.
- **Dates arrive in UTC** (`Kind = Utc`), as with JSON.
- Keep one `HttpClient` and one formatter: the formatter remembers the schemas.

## Next

- A Blazor WebAssembly or MAUI client with trimming or AOT: use the source generator on both sides, [Add-ons](Extras.md#trimmed-and-aot-apps-and-more-speed).
- Also serving a browser front end? Add the [TypeScript client](NetworkBrowser.md), the server stays as it is.
- Everything about the formatters (Web API 2, MVC 5, minimal APIs, schema negotiation): [WebFormatters.md](../WebFormatters.md).
