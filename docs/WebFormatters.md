# Web formatters (ASP.NET and HttpClient)

Three packages let controllers receive and return MsgPack the same way they handle JSON. They serialize with [LtMsgPack](../README.md#which-package), which writes the same data as LsMsgPack, only faster.

| Scenario | NuGet package | Targets | Namespace |
|---|---|---|---|
| [ASP.NET Core](#aspnet-core) MVC / Web API controllers, [minimal APIs](#minimal-apis) | `LsMsgPack.AspNetCore` | ASP.NET Core 2.x (netstandard2.0, also on .NET Framework) and ASP.NET Core 3.0 and up (net8.0) | `LsMsgPackFormatters` |
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

Property names in bodies are matched ignoring case when no property has the exact name (`PropertyNameCaseInsensitive`, on by default, as ASP.NET Core reads JSON), so clients that write camelCase names are read too: the [TypeScript client](TypeScriptClient.md) with `propertyNames: 'camelCase'`, objects of other JavaScript code. It applies to maps keyed by names (the plain media types, `application/x-lsmsgpack` without the indexed schema) and to the names of the indexed schema (matched once per class and schema, since October 2026), and costs nothing for the exact names, which are looked up first. When several properties of a class differ only in case (`Id` and `ID`), a key that equals neither exactly (`id`) sets none. Set it to `false` to read the exact names only; it is `MsgPackOptions.PropertyNameCaseInsensitive` of both serializers (off by default outside the formatters).

Anonymous objects (`return Ok(new { Id = 1, Name = "x" });`, `Ok(items.Select(i => new { i.Id }))`) are written with their properties and read into classes with the same names, as with JSON. In a property declared as `object`, or inside another anonymous object, they're written as maps of names. See [anonymous types](schema.md#anonymous-types).

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

With the code of LtMsgPack's [source generator](SourceGenerator.md), pass the context first: `AddLsMsgPackSerializerFormatters(AppMsgPackContext.Default, o => ...)` on the `IMvcBuilder`, or `options.AddLsMsgPackSerializerFormatters(AppMsgPackContext.Default, settings)`. The overloads without a context are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, as LtMsgPack's serializer is (see [Trimming and NativeAOT](SourceGenerator.md#trimming-and-nativeaot)). ASP.NET Core MVC itself is not trim compatible, so the formatters don't claim to be.

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
- An action returning an `IAsyncEnumerable<T>` (ASP.NET Core 3.0 and up) is answered with a `List<T>`: the formatter reads the items to the end and writes the same bytes as an action returning `List<T>`, so clients read it as a `List<T>` or `T[]`. A MsgPack array starts with its length, so it can't be streamed the way System.Text.Json streams it. The same goes for `Ok(items)` and `MsgPackResults.MsgPack(items)`. A value that is also an `IEnumerable<T>` (an EF Core query) is still enumerated synchronously unless the action declares it as `IAsyncEnumerable<T>`. In NativeAOT apps the items must be of a reference type: return a `List<T>` or an array of value types.

### Minimal APIs

Minimal APIs (ASP.NET Core 8 and up) don't use formatters: they write results that implement `IResult` and bind parameters that read themselves. `LsMsgPack.AspNetCore` has both, and they behave like a controller with the formatters. Register the serializer:

```csharp
using LsMsgPackFormatters;

builder.Services.AddLsMsgPack(); // or AddLsMsgPack(o => ...), AddLsMsgPack(AppMsgPackContext.Default, o => ...)
```

It registers an `LtMsgPackHttpSerializer` singleton (unless one is registered already) with the `LtMsgPackHttpOptions` of the services, the same options `AddLsMsgPackSerializerFormatters` configures. An app with controllers too gets a second serializer with its own schema store: the schema ids are the same in both, so negotiation works either way.

Return `MsgPackResults.MsgPack(value)` (or `Results.Extensions.MsgPack(value)`), and take the body as a `MsgPackBody<T>`:

```csharp
app.MapGet("/api/orders/{id}", (int id) => MsgPackResults.MsgPack(orders.Find(id)));

app.MapPost("/api/orders", (MsgPackBody<Order> order) =>
  MsgPackResults.MsgPack(orders.Add(order.Value), StatusCodes.Status201Created));

// with TypedResults
app.MapGet("/api/invoices/{id}", Results<MsgPackResult<Invoice>, NotFound> (int id) =>
  invoices.Find(id) is Invoice invoice ? MsgPackResults.MsgPack(invoice) : TypedResults.NotFound());
```

- **The response** is MsgPack when the `Accept` header asks for one of the MsgPack media types, and JSON otherwise (with the JSON options of minimal APIs, `ConfigureHttpJsonOptions`), as MVC chooses between the JSON formatter and the MsgPack output formatter: by quality, then by position, and JSON when the header lists `*/*`. Responses have `Vary: Accept`. Schema negotiation, version 2 of the schema and the headers are the same as the output formatter's.
- **The declared type** is the type argument: `MsgPackResults.MsgPack<Animal>(dog)` gets a type id on the root with `application/x-lsmsgpack`, as an action declared to return `Animal`. Declared as `object`, the value is written as its class, as `Ok(value)`.
- **The body** is read with LtMsgPack when the `Content-Type` is one of the MsgPack media types, and as JSON for a JSON `Content-Type`. An empty body, nil or JSON `null` is no value: a `MsgPackBody<Order>?` parameter (or one with a default value) is then `null`, and ASP.NET Core answers `400 Bad Request` for a required one. In a project without nullable annotations, ASP.NET Core takes every reference type parameter as optional. A body that can't be read throws a `BadHttpRequestException` with `400`, another `Content-Type` one with `415 Unsupported Media Type`: Kestrel answers with that status code (an exception handler of your own can read it from the exception).
- `ReportDifferences` works as with the input formatter: `HttpContext.GetReadDifferences()` and the log (category `LsMsgPackFormatters.MsgPackBody`).
- Both add their media types to the endpoint's metadata (`Produces` and `Accepts`), so OpenAPI lists them.
- An `IAsyncEnumerable<T>` is written as a `List<T>`, as with the output formatter.
- Trimmed and NativeAOT apps: pass the context of the [source generator](SourceGenerator.md) to `AddLsMsgPack`, the overload without one is marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. The JSON path uses the type info of the JSON options, as `Results.Json` does.

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

LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter(); // trimmed or AOT apps: new LsMsgPackMediaTypeFormatter(new LtMsgPackHttpSerializer(AppMsgPackContext.Default, options))
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
- `formatter.CreateHandler(true)` (`LsMsgPackSchemaHandler.RequestDescribedSchemas`) also asks for [version 2 of the schema](#schema-version-2-for-clients-without-the-classes), for a client whose classes differ from the server's (other names or namespaces, e.g. an SDK with its own models): the reader then pairs its classes with the server's by the declared types (see [schema.md](schema.md)).
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
| ASP.NET Core | `HttpContext.GetReadDifferences()` (in `HttpContext.Features`) | Through `ILogger` (category `LsMsgPackFormatters.LsMsgPackInputFormatter`), with the report: at the `Debug` level, or as a warning with `LogDifferencesAsWarning` |
| Web API 2 | `Request.GetReadDifferences()` in the controller | No |
| HttpClient | `response.GetReadDifferences()` (or `content.GetReadDifferences()`) after `ReadAsAsync` | No |
| MVC 5 | `HttpContext.GetReadDifferences()` in the controller | No |

Values that can't be read (a string where an `int` is declared...) fail the request by default. Set `ReadErrors` on the options of the media type to skip them instead (`o.XLsMsgPack.ReadErrors = ReadErrorHandling.ReportAndContinue`, they're then reported with the other differences), or to fail with all of them at once (`FailDeferred`, the message in the `ModelState` has the complete report), see [ReadDifferences.md](ReadDifferences.md#values-that-cant-be-read-readerrors).

It's `null` when the body matched the classes (or wasn't read by the MsgPack formatter). When reading fails, it has what was found until the error, which is often the cause (a misspelled property that was skipped). `LtMsgPackHttpSerializer.Deserialize` also has an overload with `out ReadDifferences`.

It's off by default: reading with it is a bit slower (about 0.15 µs per request body, and 10% on large bodies), and logging the report walks the objects that were read to find the paths (only when the log level is enabled). Turn it on while you look for a problem, or in a test environment.

In ASP.NET Core the differences are logged at the `Debug` level by default, because clients on a newer version of the models would otherwise add a warning to the log with every request. Set `LogDifferencesAsWarning` to log them as warnings (it has no effect without `ReportDifferences`):

```csharp
builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(o =>
  {
    o.ReportDifferences = true;
    o.LogDifferencesAsWarning = true;
  });
```

When your schema is already public (exposed by swagger for example) you can also give the report to the client when its body is missing something you need. For example, an `Order` needs an `Address`, and a client on an older version of the models still calls it `ShippingAddress`:

```csharp
using LsMsgPackFormatters; // GetReadDifferences()

public class Order
{
  public int Id { get; set; }
  public Address Address { get; set; } // Address? with nullable reference types, see below
}

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
  [HttpPost]
  public IActionResult Post(Order order)
  {
    if (order.Address == null)
      return BadRequest("Validation failed: address is required\r\n" + HttpContext.GetReadDifferences());

    // ... store the order
    return Ok(order);
  }
}
```

The client gets `400 Bad Request` with this body, instead of only "address is required":

```
Validation failed: address is required
1 difference between the data and the classes:
- Order.ShippingAddress: not a property of the class, skipped, 1 time
    at $
    left at their default (first object): Address
```

Notes:

- `ReadDifferences.ToString()` is the report. When the body matched the classes, `GetReadDifferences()` is `null` and adds nothing to the message.
- The string is written as `text/plain` when the client accepts it, otherwise by the formatter its `Accept` header asks for (a MsgPack string for the MsgPack media types).
- With nullable reference types enabled, declare the property as `Address?`. A non-nullable `Address` is required by `[ApiController]`, which then answers `400 Bad Request` with its own validation problem before the action runs, without the report.
- The report names your classes and properties. That's fine between your own applications, think twice before sending it to clients of a public facing API. Endpoints could be probed to get the expected schema information this way.

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

### Complete schemas

By default (`CompleteSchemas`, with `NegotiateSchemas`) the schema of a response type is made at once with every class its values can hold, in a fixed order: the classes of its properties, the elements of collections and values of dictionaries, and the classes deriving from them (or implementing an interface) in the assemblies the type reaches. So its id depends only on the classes and the options, not on what the first response happened to contain:

- Every instance of the server (a web farm, after a restart) writes the same schemas, so a client keeps using the ones it holds.
- A client can hold them before its first request: [lsmsgpack-ts](TypeScriptModels.md) writes them for the TypeScript client, made by the same code (`LtMsgPackHttpSerializer.PrepareSchema(declaredType, describedSchema)`, which also tells the id of a type's schema).
- A value with a class the schema does not have still makes it grow: a value in a property declared as `object`, a class of an assembly the type does not reach, a collection with a type id (`IEnumerable<Address>` holding a `List<Address>`).
- `CompleteSchemas = false`: a schema gets the classes of the values written so far (the behavior before October 2026), smaller when most derived classes are never sent.

It is `MsgPackOptions.CompleteSchemas` of the `application/x-lsmsgpack` serializers, which both serializers support with `WriteSchemaReference`.

### Schema version 2 for clients without the classes

A client that doesn't have the .NET classes (e.g. JavaScript) can't tell from the property names alone whether a left-out value was `0`, `false` or `null`, or whether a nested array is an object or a list. It asks for [version 2 of the schema](schema.md#version-2-the-declared-types), which also has the type and default of each property and the type of the root:

```
Accept: application/x-lsmsgpack; schema=described, application/x-lsmsgpack; q=0.9
```

- The body is the same, only the schema differs. Its schema has its own id, and is negotiated and referred to like any other (`MsgPack-Schemas`, `MsgPack-Schema`).
- List the plain media type as well: ASP.NET Core's content negotiation only matches a parameter it knows, and a server that doesn't offer version 2 then answers with version 1.
- The .NET readers read both versions, so a response that a cache serves to another client can be read by it either way. ASP.NET Core and MVC 5 add `Accept` to `Vary`. Web API 2 can't set `Vary`, which is harmless here.
- The .NET client asks for it with `formatter.CreateHandler(true)` (`LsMsgPackSchemaHandler.RequestDescribedSchemas`): it adds `application/x-lsmsgpack; schema=described` next to an `application/x-lsmsgpack` the request accepts, or sets the header above when the request has none.
- `DescribeSchemas = false` turns it off: the server always writes version 1. It also needs `XLsMsgPack.UseInexedSchema`, and works with or without `NegotiateSchemas`.
- With `XLsMsgPack.SchemaVersion = SchemaVersion.Described`, every response has version 2.
