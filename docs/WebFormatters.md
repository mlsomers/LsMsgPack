# Web formatters (ASP.NET and HttpClient)

Three packages let controllers receive and return MsgPack the same way they handle JSON. They serialize with [LtMsgPack](../README.md#which-package), which writes the same data as LsMsgPack, only faster.

| Scenario | NuGet package | Targets | Namespace |
|---|---|---|---|
| [ASP.NET Core](#aspnet-core) MVC / Web API controllers | `LsMsgPack.AspNetCore` | ASP.NET Core 2.x (netstandard2.0, also on .NET Framework) and ASP.NET Core 3.0 and up (net8.0) | `LsMsgPackFormatters` |
| [ASP.NET Web API 2](#aspnet-web-api-2) (`System.Web.Http`) | `LsMsgPack.AspNet.WebApi` | netstandard2.0 (Web API 2 runs on .NET Framework) | `LsMsgPackWebApiFormatters` |
| [HttpClient](#httpclient) (`ObjectContent` / `ReadAsAsync`) | `LsMsgPack.AspNet.WebApi` | netstandard2.0, any platform | `LsMsgPackWebApiFormatters` |
| [ASP.NET MVC 5](#aspnet-mvc-5) (`System.Web.Mvc`) | `LsMsgPack.AspNet.Mvc` | .NET Framework 4.6.2+ | `LsMsgPackMvc` |

All of them depend on the `LtMsgPack` package (and through it `LsMsgPack.Core`) and speak exactly the same wire formats, so an HttpClient using the Web API formatter can talk to an ASP.NET Core server, for example.

Media types and settings
------------------------

Every package supports three media types (`Content-Type` for request bodies and `Accept` for responses). The constants are in `LsMsgPack.MsgPackMediaTypes`. The settings for each are in `LtMsgPack.Http.LtMsgPackHttpOptions`:

| Media type | Constant | Settings | Default |
|---|---|---|---|
| `application/msgpack` | `MsgPackMediaTypes.MsgPack` | `Plain` | Plain MsgPack: maps keyed by property names, no indexed schema, no type ids, default values left out. Any MsgPack implementation can read and write it. Use a preset for another library (below). |
| `application/x-msgpack` | `MsgPackMediaTypes.XMsgPack` | `Plain` | Same as `application/msgpack` (an older, widely used name). |
| `application/x-lsmsgpack` | `MsgPackMediaTypes.XLsMsgPack` | `XLsMsgPack` | What LsMsgPack writes by default (`LtMsgPackPresets.LsMsgPack()`): the [indexed schema](schema.md#the-indexed-schema), objects as arrays of their values, type ids where the type differs from the declared type (full [polymorphism](schema.md#polymorphic-class-hierarchy-support)), and [schema references](#schema-references-applicationx-lsmsgpack) between these formatters (`NegotiateSchemas`). Use this between LsMsgPack / LtMsgPack endpoints. |

`Plain` and `XLsMsgPack` are independent `LtMsgPackOptions` objects. Changing one doesn't affect the other. To exchange plain MsgPack with another library, use one of its presets (see [Compatibility.md](Compatibility.md)):

```csharp
services.AddControllers().AddLsMsgPackSerializerFormatters(o =>
{
  o.Plain = LtMsgPackPresets.MessagePackCSharp(); // or Nerdbank(), Generic()
});
```

No type ids are written for the plain media types, so properties declared as an interface or abstract class can't be read back from them. Use `application/x-lsmsgpack` for those, or see [resolving by signature](schema.md#resolving-by-signature).

The options are read once, when a formatter is created, so later changes have no effect. A formatter can safely handle concurrent requests.

ASP.NET Core
------------

```
dotnet add package LsMsgPack.AspNetCore
```

Register the formatters in `Program.cs` (or `Startup.ConfigureServices`):

```csharp
using LsMsgPackFormatters;

builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters();
```

To change the settings, pass a configuration action. The options are also registered as `IOptions<LtMsgPackHttpOptions>`:

```csharp
builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(o =>
  {
    o.XLsMsgPack.AddTypeIdOptions = AddTypeIdOption.Always;
    o.XLsMsgPack.TypeResolvers = new IMsgPackTypeResolver[] { new XmlRootAttributeTypeResolver() };
  });
```

When the models were made for another serializer, leave out the same properties as that one. By default any "ignore" attribute leaves a property out, `[XmlIgnore]` too. With the API's Json.NET models, for example (set it on both media types):

```csharp
builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(o =>
  {
    IMsgPackPropertyIncludeStatically[] likeJsonNet = { new FilterNonSettable(), FilterIgnoredAttribute.LikeNewtonsoft, new FilterStatic() };
    o.XLsMsgPack.StaticFilters = likeJsonNet;
    o.Plain.StaticFilters = likeJsonNet;
  });
```

See [Property names and filters](schema.md#property-names-and-filters) for the other presets and for empty strings and default values.

Alternatively, add them to `MvcOptions` directly, with or without an `LtMsgPackHttpOptions` instance:

```csharp
builder.Services.AddControllers(options => options.AddLsMsgPackSerializerFormatters(new LtMsgPackHttpOptions()));
```

Controllers don't need any changes. Body parameters are read with the input formatter when the request's `Content-Type` is one of the MsgPack media types. Results are written with the output formatter when the client's `Accept` header asks for one:

```csharp
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
  [HttpPost]
  public ActionResult<Order> Post(Order order) // [FromBody] is implied by [ApiController]
  {
    return order;
  }
}
```

Notes:
- The formatters are added after the existing ones, so JSON stays the default for clients that don't ask for MsgPack (for example `Accept: */*`).
- If the body can't be deserialized, the error is added to the `ModelState`. With `[ApiController]`, the request is answered with `400 Bad Request` automatically. Otherwise, check `ModelState.IsValid`.
- An empty body binds as `null` (or the type's default value) when empty bodies are allowed, like the JSON formatter does.
- Request bodies are buffered (on .NET 8 read in place from the request pipe) and responses are written asynchronously, so no `AllowSynchronousIO` is needed.
- With `application/x-lsmsgpack`, a response whose runtime type differs from the action's declared return type gets a type id on the root. The client can then deserialize it as the declared base type or interface.

ASP.NET Web API 2
-----------------

```
Install-Package LsMsgPack.AspNet.WebApi
```

Add the formatter in `WebApiConfig.Register`:

```csharp
using LsMsgPackWebApiFormatters;

public static void Register(HttpConfiguration config)
{
  config.Formatters.Add(new LsMsgPackMediaTypeFormatter());
  // or: config.Formatters.Add(new LsMsgPackMediaTypeFormatter(new LtMsgPackHttpOptions { ... }));

  config.MapHttpAttributeRoutes();
}
```

Web API's content negotiation then handles the rest. Complex parameters are read from MsgPack bodies based on the `Content-Type`, and responses are written as MsgPack when the `Accept` header asks for it. JSON stays the default because the formatter is added after the JSON formatter.

A body that can't be deserialized is reported in the `ModelState` (check `ModelState.IsValid`), just like the JSON formatter does.

HttpClient
----------

The same package works on the client side, on any platform (.NET Framework, .NET, MAUI, Avalonia and so on). It builds on `System.Net.Http.Formatting` (the `Microsoft.AspNet.WebApi.Client` package):

```csharp
using LsMsgPack;
using LsMsgPackWebApiFormatters;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;

LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
HttpClient client = new HttpClient(formatter.CreateHandler()) { BaseAddress = new Uri("https://example.com/") }; // keep both for the lifetime of the app

HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "api/orders")
{
  Content = new ObjectContent<Order>(order, formatter, MsgPackMediaTypes.XLsMsgPack)
};
request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));

HttpResponseMessage response = await client.SendAsync(request);
response.EnsureSuccessStatusCode();
Order result = await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter });
```

Notes:
- `formatter.CreateHandler()` enables [schema references](#schema-references-applicationx-lsmsgpack) for the responses. It's optional: without it (or with `new HttpClient()`), every response carries its schema inline. Use `formatter.CreateHandler(inner)` to chain another handler.
- Without an explicit media type, `ObjectContent` uses the formatter's first media type, `application/msgpack` (plain MsgPack).
- Set the `Accept` header, or the server will probably answer with JSON.
- On the client side, a response that can't be deserialized throws from `ReadAsAsync` instead of being logged.
- Use the same `XLsMsgPack` options on the client and the server for `application/x-lsmsgpack` (at least the same `UseInexedSchema`).

ASP.NET MVC 5
-------------

```
Install-Package LsMsgPack.AspNet.Mvc
```

MVC 5 has no formatters, so this package provides a model binder for requests and an `ActionResult` for responses. Register the binder in `Global.asax.cs`:

```csharp
using LsMsgPackMvc;

protected void Application_Start()
{
  LsMsgPackMvc.LsMsgPackMvc.Register();
  // or: LsMsgPackMvc.LsMsgPackMvc.Register(new LtMsgPackHttpOptions { ... });

  RouteConfig.RegisterRoutes(RouteTable.Routes);
}
```

`Register` can be called again to replace the options. It never adds a second binder.

Use it in a controller:

```csharp
using LsMsgPackMvc;

public class OrdersController : Controller
{
  [HttpPost]
  public ActionResult Save(int id, Order order) // id from the route or query string, order from the MsgPack body
  {
    if (!ModelState.IsValid)
      return new HttpStatusCodeResult(400);

    return this.MsgPack(order); // "this." is required to call the extension method
  }
}
```

Model binding:
- Complex parameters are deserialized from the body when the `Content-Type` is one of the MsgPack media types. All other requests (forms, JSON and so on) are bound as usual.
- Simple types (those that convert from a string, such as `int`, `string`, `Guid` or `DateTime`) are left to the other binders, so they can still come from the route or query string.
- Deserialization errors are added to the `ModelState`. The model is validated with DataAnnotations just like the default model binder does, so `ModelState.IsValid` works as usual.

Responses (`LsMsgPackResult`):
- `this.MsgPack(data)` picks the media type from the request's `Accept` header (highest quality first) and falls back to `application/msgpack` when none of the MsgPack types is accepted.
- To control the output, create the result yourself:

```csharp
return new LsMsgPackResult(pets)
{
  ContentType = MsgPackMediaTypes.XLsMsgPack, // skip content negotiation
  DeclaredType = typeof(IPet[]),              // add type ids relative to this type (x-lsmsgpack only)
  Serializer = mySerializer                   // an LtMsgPackHttpSerializer, created once, instead of the one Register made
};
```

Reporting differences
---------------------

A client or server on another version of the models sends properties your classes don't have, or leaves out ones they do. Reading goes on (an unknown property is skipped), so the request succeeds while a value you expected stays empty. Set `ReportDifferences` to find out what didn't match ([ReadDifferences.md](ReadDifferences.md) explains the report):

```csharp
builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(o => o.ReportDifferences = true);
```

| Package | Where the differences are | Logged |
|---|---|---|
| ASP.NET Core | `HttpContext.GetReadDifferences()` (in `HttpContext.Features`) | As a warning through `ILogger` (category `LsMsgPackFormatters.LsMsgPackInputFormatter`), with the report |
| Web API 2 | `Request.GetReadDifferences()` in the controller | No |
| HttpClient | `response.GetReadDifferences()` (or `content.GetReadDifferences()`) after `ReadAsAsync` | No |
| MVC 5 | `HttpContext.GetReadDifferences()` in the controller | No |

Values that can't be read (a string where an `int` is declared...) fail the request by default. Set `ReadErrors` on the options of the media type to skip them instead (`o.XLsMsgPack.ReadErrors = ReadErrorHandling.ReportAndContinue`, they're then reported with the other differences), or to fail with all of them at once (`FailDeferred`, the message in the `ModelState` has the complete report), see [ReadDifferences.md](ReadDifferences.md#values-that-cant-be-read-readerrors).

It's `null` when the body matched the classes (or wasn't read by the MsgPack formatter). When reading fails, it has what was found until the error, which is often the cause (a misspelled property that was skipped). `LtMsgPackHttpSerializer.Deserialize` also has an overload with `out ReadDifferences`.

It's off by default: reading with it is a bit slower (about 0.15 µs per request body, and 10% on large bodies), and the warning walks the objects that were read to find the paths. Turn it on while you look for a problem, or in a test environment.

Schema references (application/x-lsmsgpack)
--------------------------------------------

The indexed schema makes a payload smaller, but it is sent with every body. Between these formatters, it is sent only once:

1. The client lists the schemas it holds in the request header `MsgPack-Schemas` (ids of 32 hexadecimal digits, comma separated).
2. The server writes the body with a reference to its schema (18 bytes) when the client holds it, and with the schema inline otherwise. A schema of at most 18 bytes is always sent inline. The response names its schema in `MsgPack-Schema` and has `Vary: MsgPack-Schemas`.
3. The client registers the schemas it receives inline and lists them in its next requests to that server (at most `MaxAdvertisedSchemas`, 32 by default, the most recent ones).

On the client (HttpClient, the Web API package), this takes one line:

```csharp
LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
HttpClient client = new HttpClient(formatter.CreateHandler()); // formatter.CreateHandler(inner) to chain another handler
```

Without the handler (or from any other client), nothing changes: the responses carry their schema inline, as plain `application/x-lsmsgpack`.

- **Request bodies always carry their schema inline.** A client can't know whether the server (or the instance behind a load balancer) still holds a schema, and retrying a request that the server may already have handled isn't safe. The server caches the schemas of requests by their bytes, so reading them costs little.
- A schema grows when a new type is written (e.g. another implementation of an interface). It then gets a new id, and the client learns it from the next inline response.
- The server keeps the schemas it writes. Schemas received from clients are limited by `SchemaStore.MaxSchemas`. A client whose store is full keeps working, it just stops listing new schemas.
- ASP.NET Web API 2 as a server: a formatter can't set response headers, so `MsgPack-Schema` is a content header. Instead of `Vary`, a response with a reference has `Expires` in the past, so shared caches don't keep a body only this client can read.
- `NegotiateSchemas = false` turns it off (the schema is always inline). It's also off when `XLsMsgPack.UseInexedSchema` is false.

Measured on the invoices of `BenchmarkInvoices` (100 invoices, LtMsgPack with the default options, one serializer, in process, one run): with the schema inline, 214.7 KB, write ~310 µs, read ~425 µs. With references, 176.2 KB (-18%), write ~255 µs, read ~415 µs.
