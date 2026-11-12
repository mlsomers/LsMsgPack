# Web formatters (ASP.NET and HttpClient)

Three packages let controllers receive and return MsgPack the same way they handle JSON. They serialize with [LtMsgPack](../README.md#which-package), which writes the same data as LsMsgPackDebug, only faster.

| Scenario | NuGet package | Targets | Namespace |
|---|---|---|---|
| [ASP.NET Core](#aspnet-core) MVC / Web API controllers, [minimal APIs](#minimal-apis) | `LsMsgPack.AspNetCore` | ASP.NET Core 2.x (netstandard2.0, also on .NET Framework) and ASP.NET Core 3.0 and up (net8.0) | `LsMsgPackFormatters` |
| [ASP.NET Web API 2](#aspnet-web-api-2) (`System.Web.Http`) | `LsMsgPack.AspNet.WebApi` | netstandard2.0 (Web API 2 runs on .NET Framework) | `LsMsgPackWebApiFormatters` |
| [HttpClient](#httpclient) (`GetFromMsgPackAsync` / `PostAsMsgPackAsync`, `ObjectContent` / `ReadAsAsync`, [streams](#streams-with-httpclient)) | `LsMsgPack.AspNet.WebApi` | netstandard2.0 and 2.1 (`IAsyncEnumerable`), any platform | `LsMsgPackWebApiFormatters` |
| [ASP.NET MVC 5](#aspnet-mvc-5) (`System.Web.Mvc`) | `LsMsgPack.AspNet.Mvc` | .NET Framework 4.6.2+ | `LsMsgPackMvc` |

All of them depend on the `LtMsgPack` package (and through it `LsMsgPack.Core`) and speak exactly the same wire formats, so an HttpClient using the Web API formatter can talk to an ASP.NET Core server, for example.

Media types and settings
------------------------

Every package supports three media types (`Content-Type` for request bodies and `Accept` for responses). The constants are in `LsMsgPack.MsgPackMediaTypes`. The settings for each are in `LtMsgPack.Http.LtMsgPackHttpOptions`:

| Media type | Constant | Settings | Default |
|---|---|---|---|
| `application/msgpack` | `MsgPackMediaTypes.MsgPack` | `Plain` | What any MsgPack implementation reads (`LtMsgPackPresets.Generic()`): maps keyed by property names, every value, no indexed schema, no type ids, `Guid` and `decimal` as text, dates as timestamps (a `DateTimeOffset` loses its offset). Use a preset for a specific library (below). |
| `application/x-msgpack` | `MsgPackMediaTypes.XMsgPack` | `Plain` | Same as `application/msgpack` (an older, widely used name). |
| `application/x-lsmsgpack` | `MsgPackMediaTypes.XLsMsgPack` | `XLsMsgPack` | What LsMsgPack writes by default (`LtMsgPackPresets.LsMsgPack()`): the [indexed schema](schema.md#the-indexed-schema), objects as arrays of their values, type ids where the type differs from the declared type (full [polymorphism](schema.md#polymorphic-class-hierarchy-support)), and [schema references](#schema-references-applicationx-lsmsgpack) between these formatters (`NegotiateSchemas`). Use this between LsMsgPack / LtMsgPack endpoints. |

`Plain` and `XLsMsgPack` are independent `LtMsgPackOptions` objects. Changing one doesn't affect the other. To match a specific library, use its preset (see [Compatibility.md](Compatibility.md)):

```csharp
services.AddControllers().AddLsMsgPackSerializerFormatters(o =>
{
  o.Plain = LtMsgPackPresets.MessagePackCSharp(); // or Nerdbank()
});
```

No type ids are written for the plain media types, so properties declared as an interface or abstract class can't be read back from them. Use `application/x-lsmsgpack` for those, or see [resolving by signature](schema.md#resolving-by-signature). Use it too between .NET programs that need the offset of a `DateTimeOffset`.

Property names in bodies are matched ignoring case when no property has the exact name (`PropertyNameCaseInsensitive`, on by default, as ASP.NET Core reads JSON), so clients that write camelCase names are read too: the [TypeScript client](TypeScriptClient.md) (camelCase by default), objects of other JavaScript code. It applies to maps keyed by names (the plain media types, `application/x-lsmsgpack` without the indexed schema) and to the names of the indexed schema (matched once per class and schema), and costs nothing for the exact names, which are looked up first. When several properties of a class differ only in case (`Id` and `ID`), a key that equals neither exactly (`id`) sets none. Set it to `false` to read the exact names only; it is `MsgPackOptions.PropertyNameCaseInsensitive` of both serializers (off by default outside the formatters).

The writer of each class is compiled when the class is first written (`CompileWritersAfter = 0`, for both media types), so responses use it from the first one on; the first response of each class waits 1-7 ms for it. Outside the formatters a class is compiled after 1000 objects, on the thread pool (`LtMsgPackOptions.CompileWritersAfter`, see [the source generator](SourceGenerator.md), which writes the same code at compile time). The readers of request bodies are compiled the same way, when a class is first read (`CompileReadersAfter = 0`), so the first request body of each class waits for its reader. Where the runtime can't compile code (NativeAOT, WebAssembly) the settings do nothing.

Anonymous objects (`return Ok(new { Id = 1, Name = "x" });`, `Ok(items.Select(i => new { i.Id }))`) are written with their properties and read into classes with the same names, as with JSON. In a property declared as `object`, or inside another anonymous object, they're written as maps of names. See [anonymous types](schema.md#anonymous-types).

MsgPack has no form for a JSON document, so `JsonElement`, `JsonDocument`, `JsonNode` and Json.NET's `JObject` can't be used in models or bodies: a response with a `JsonElement` answers 500, a `[FromBody] JsonElement` 400. Hold the JSON text in a `string`, or the values in a class of your own. An endpoint that passes JSON through (a webhook) can also stay on JSON: the formatters only answer MsgPack to clients that ask for it. Analyzer [LSMP009](Analyzers.md#lsmp009-json-document-type-is-not-serialized) reports these types.

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

`application/msgpack` writes every value (the `Generic()` preset); Json.NET with `DefaultValueHandling.Ignore` (or System.Text.Json with `JsonIgnoreCondition.WhenWritingDefault`) leaves default values out. And Json.NET calls the `ShouldSerialize{Name}()` methods of the models. To write the same properties as such a JSON API:

```csharp
    o.Plain.DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues(), new FilterShouldSerialize() };
    o.XLsMsgPack.DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues(), new FilterShouldSerialize() };
```

See [Property names and filters](schema.md#property-names-and-filters) for the other presets, empty strings, default values and [ShouldSerialize methods](schema.md#shouldserialize-methods). A model with cycles (an object referring back to its parent) fails at `MaxDepth`, unless the references are left out or `IgnoreCycles` is set ([Cycles](schema.md#cycles)).

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
- An action returning an `IAsyncEnumerable<T>` (ASP.NET Core 3.0 and up) is answered with a `List<T>`: the formatter reads the items to the end and writes the same bytes as an action returning `List<T>`, so clients read it as a `List<T>` or `T[]`. The same goes for `Ok(items)` and `MsgPackResults.MsgPack(items)`. A client that reads streams can get the items as they come instead (see [streaming responses](#streaming-responses)). A value that is also an `IEnumerable<T>` (an EF Core query) is still enumerated synchronously unless the action declares it as `IAsyncEnumerable<T>`. In NativeAOT apps the items must be of a reference type: return a `List<T>` or an array of value types.

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
- An `IAsyncEnumerable<T>` is written as a `List<T>`, or streamed when the client asks for it, as with the output formatter.
- Trimmed and NativeAOT apps: pass the context of the [source generator](SourceGenerator.md) to `AddLsMsgPack`, the overload without one is marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. The JSON path uses the type info of the JSON options, as `Results.Json` does.

### Streaming responses

ASP.NET Core on .NET 8 and up. A MsgPack array starts with its length, so a list whose items are still coming can only be sent as an array of unknown length, a [stream](Streaming.md). Readers of a single payload (the serializers, other MsgPack libraries) refuse that, so the client has to ask for it:

```
Accept: application/x-lsmsgpack; stream=unknown-length, application/x-lsmsgpack; q=0.9
```

The parameter (`MsgPackMediaTypes.StreamParameter`) works on any of the three media types, and `schema=described` can be added to the first range. The second range is needed because MVC's negotiation only matches a media type without parameters. The .NET client's [`GetFromMsgPackAsAsyncEnumerable`](#streams-with-httpclient) and the TypeScript client's `stream(url)` send such a header.

- **`IAsyncEnumerable<T>`** (an action, `Ok(items)`, `MsgPackResults.MsgPack(items)`) is then written as a head (the schema of `List<T>`, once) followed by an array of unknown length, with each item written as it comes. With known lengths this would be the payload of the `List<T>`. The schema is negotiated as for other responses (`MsgPack-Schema`, a reference when the client holds it). A `List<KeyValuePair<K, V>>` is a map, so pairs aren't streamed. Other clients get the `List<T>` as before. The response varies with the `Accept` header, and `LtMsgPackHttpOptions.WriteStreams = false` turns streaming off.
- **When items go out**: what's written is sent whenever the source makes the formatter wait for the next item, and every `FlushThreshold` bytes (64 KiB). Nothing is sent before the first item, so an exception before it still gets an error response. An exception after it aborts the response: the array has no end, and the client finds the data cut off.
- **The response** has `stream=unknown-length` in its `Content-Type` and no `Content-Length`. Read it with HttpClient's [`GetFromMsgPackAsAsyncEnumerable<T>`](#streams-with-httpclient), with `MsgPackStreamReader` (`ReadHeadAsync<List<T>>()`, then `ReadElementsAsync<T>()`) or the TypeScript client's `stream(url)` (`readHead()`, `elements()`).

**Writing a stream yourself**: return a `MsgPackStreamResult` from an action (or `MsgPackResults.Stream(...)` / `Results.Extensions.MsgPackStream(...)` from an endpoint). It gives you an [asynchronous](Streaming.md#asynchronous-writing) `MsgPackStreamWriter` on the response, with the serializer of the media type:

```csharp
[HttpGet("export")]
public IActionResult Export() => new MsgPackStreamResult(async (writer, cancellationToken) =>
{
  writer.BeginEntry("orders");
  writer.WriteHead<List<Order>>();
  writer.WriteArrayStart();
  await foreach (Order order in db.Orders.AsAsyncEnumerable().WithCancellation(cancellationToken))
    await writer.WriteObjectAsync(order, cancellationToken);
  writer.WriteEnd();
}, new MsgPackStreamIndexOptions()); // optional: an index at the end
```

- The media type is the MsgPack one the `Accept` header asks for, and `application/x-lsmsgpack` when it asks for none (there's no JSON). The `Content-Type` has `stream=unknown-length`.
- With `application/x-lsmsgpack` and schema negotiation, each schema is inline the first time and referred to after that (`MsgPackStreamSchemas.PerStream`).
- After the callback, the writer is disposed asynchronously, which writes the index and the rest of the buffer. The same rules for exceptions apply.
- Await `FlushIfFullAsync` (or `WriteObjectAsync`) after each value or row, so the buffer goes out at `FlushThreshold` and the action waits while the client is slow. Await `FlushAsync` to send what's there now.

### Response compression

The response compression middleware only compresses the media types in `ResponseCompressionOptions.MimeTypes`, and its default list (`ResponseCompressionDefaults.MimeTypes`) has JSON but no MsgPack. Add them, or an API that compresses its JSON sends MsgPack uncompressed, which makes the responses several times larger on the wire. In the [measurement](TypeScriptClient.md#performance), 100 invoices take 42.1 KB as gzipped JSON, 300.6 KB as uncompressed `application/msgpack` and 166.9 KB as uncompressed `application/x-lsmsgpack` (38.0 KB gzipped):

```csharp
using System.Linq;
using LsMsgPack;
using Microsoft.AspNetCore.ResponseCompression;

builder.Services.AddResponseCompression(o =>
{
  o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(MsgPackMediaTypes.All);
});
// ...
app.UseResponseCompression(); // before the endpoints
```

- Parameters of the `Content-Type` (`schema=described`, `stream=unknown-length`) don't matter: the middleware compares the media type without them.
- Over HTTPS the middleware only compresses with `EnableForHttps = true`, which is off by default because of BREACH (secrets in a compressed response together with text an attacker can choose). This applies to JSON as well.
- IIS (`dynamicTypes`), nginx (`gzip_types`) and most CDNs also have lists of the types they compress: add the MsgPack media types there too.

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
using LsMsgPackWebApiFormatters;

LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter(); // trimmed or AOT apps: new LsMsgPackMediaTypeFormatter(new LtMsgPackHttpSerializer(AppMsgPackContext.Default, options))
HttpClient client = new HttpClient(formatter.CreateHandler()) { BaseAddress = new Uri("https://example.com/") }; // keep both for the lifetime of the app

Order order = await client.GetFromMsgPackAsync<Order>("api/orders/42", formatter);

HttpResponseMessage response = await client.PostAsMsgPackAsync("api/orders", order, formatter); // also PutAsMsgPackAsync, PatchAsMsgPackAsync
await response.EnsureSuccessStatusCodeAsync(); // a MsgPackHttpException with the problem details of an error
Order saved = await response.Content.ReadFromMsgPackAsync<Order>(formatter);
```

The helpers (`MsgPackHttpClientExtensions`) work like `GetFromJsonAsync`, `PostAsJsonAsync` and `ReadFromJsonAsync` of System.Net.Http.Json:
- They send `MsgPackHttpClientExtensions.Accept` (`application/x-lsmsgpack, application/msgpack; q=0.9`) when neither the request nor `client.DefaultRequestHeaders` has an `Accept` header.
- `PostAsMsgPackAsync` (and `Put`, `Patch`) send the value as `application/x-lsmsgpack`, with the declared type `T`, and return the response without checking its status code: `await response.EnsureSuccessStatusCodeAsync()` does.
- `GetFromMsgPackAsync` and `EnsureSuccessStatusCodeAsync` throw a [`MsgPackHttpException`](#error-responses) for a status code outside 200-299. `GetFromMsgPackAsync` and `ReadFromMsgPackAsync` throw a `NotSupportedException` for a body that isn't MsgPack (JSON, with the start of its text in the message) or is a [stream](#streams-with-httpclient), and a `MsgPackException` for a body that can't be read as `T`. An empty body (`204 No Content`) gives `default(T)`.
- With [`ReportDifferences`](#reporting-differences), read the response with `ReadFromMsgPackAsync` and then call `response.GetReadDifferences()`: `GetFromMsgPackAsync` disposes the response.

For another media type or your own request, use the formatter directly:

```csharp
HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "api/orders")
{
  Content = new ObjectContent<Order>(order, formatter, MsgPackMediaTypes.MsgPack)
};
request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));

HttpResponseMessage response = await client.SendAsync(request);
response.EnsureSuccessStatusCode();
Order result = await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter }); // or ReadFromMsgPackAsync<Order>(formatter)
```

Notes:
- `formatter.CreateHandler()` enables [schema references](#schema-references-applicationx-lsmsgpack) for the responses. It's optional: without it (or with `new HttpClient()`), every response carries its schema inline. Use `formatter.CreateHandler(inner)` to chain another handler.
- With `IHttpClientFactory`, which sets the inner handler itself and refuses a handler that has one, add `formatter.CreateDelegatingHandler()` (`CreateDelegatingHandler(true)` for version 2 of the schema): `services.AddHttpClient("api").AddHttpMessageHandler(() => formatter.CreateDelegatingHandler());`. Keep the formatter as a singleton: the handlers the factory creates (one per handler lifetime) share its schemas.
- `formatter.CreateHandler(true)` (`LsMsgPackSchemaHandler.RequestDescribedSchemas`) also asks for [version 2 of the schema](#schema-version-2-for-clients-without-the-classes), for a client whose classes differ from the server's (other names or namespaces, e.g. an SDK with its own models): the reader then pairs its classes with the server's by the declared types (see [schema.md](schema.md)).
- Without an explicit media type, `ObjectContent` uses the formatter's first media type, `application/msgpack` (plain MsgPack): a `DateTimeOffset` loses its offset and class hierarchies are lost. Pass `application/x-lsmsgpack` to keep them, as the helpers do.
- Without the helpers, set the `Accept` header, or the server will probably answer with JSON.
- `HttpClientHandler` doesn't ask for [compressed responses](#response-compression) by default, and the handler `CreateHandler()` makes doesn't either. Pass one that does: `formatter.CreateHandler(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })` (`DecompressionMethods.All` on .NET Core 3.0 and up, which adds Brotli). With `IHttpClientFactory`, set it in `ConfigurePrimaryHttpMessageHandler`.
- On the client side, a response that can't be deserialized throws from `ReadFromMsgPackAsync` or `ReadAsAsync` instead of being logged.
- Use the same `XLsMsgPack` options on the client and the server for `application/x-lsmsgpack` (at least the same `UseInexedSchema`).

### Error responses

ASP.NET Core answers errors with problem details ([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457), `application/problem+json`), also when the request asks for MsgPack: `NotFound()` and the other status code results without a value of an `[ApiController]`, `Problem()`, the automatic `400 Bad Request` of `[ApiController]` for a model that isn't valid (with the `errors` per property), and with `AddProblemDetails()` + `UseExceptionHandler()` an exception. MVC limits these results to the problem details media types, so the MsgPack formatter isn't chosen. A value of your own (`return NotFound(new OrderError(...))`) is negotiated as any value: MsgPack when the request asks for it. A request no endpoint matches gets an empty `404`, or a text with `UseStatusCodePages()`.

A `MsgPackHttpException` (an `HttpRequestException`) has what the body says:

```csharp
try
{
  Order order = await client.GetFromMsgPackAsync<Order>("api/orders/42", formatter);
}
catch (MsgPackHttpException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
{
  foreach (KeyValuePair<string, string[]> error in ex.Errors) // the validation errors per property
    Show(error.Key, error.Value);
}
```

- `StatusCode`, `ReasonPhrase` and `ContentType` of the response, and its `Body` (at most the first `MsgPackHttpException.MaxBodyLength` bytes, 64 KiB).
- `Text`: the body of a text, JSON or XML `Content-Type` (or none) as text, in its charset. Null for MsgPack.
- `Title`, `Detail`, `ProblemType` (`type`) and `Instance`: the members of problem details, for a JSON body. `Errors`: the `errors` of validation problem details, empty when the body has none.
- `ReadBody<T>(formatter)` reads a MsgPack body (a value of your own) as `T`.
- The message is that of `EnsureSuccessStatusCode` with the title, detail and errors, or the start of a text: `Response status code does not indicate success: 400 (Bad Request). One or more validation errors occurred. Customer: The Customer field is required.`
- On .NET 5 and up the `StatusCode` of `HttpRequestException` is set too, so `catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)` catches it.

### Streams with HttpClient

An ASP.NET Core action that returns an `IAsyncEnumerable<T>` [streams](#streaming-responses) its items when the request asks for it. The client reads them as they arrive (.NET Standard 2.1: .NET Core 3.0 and up, .NET 5 and up, MAUI...):

```csharp
await foreach (Order order in client.GetFromMsgPackAsAsyncEnumerable<Order>("api/orders/export", formatter, cancellationToken))
  Show(order);
```

- It sends `MsgPackStreamExtensions.StreamAccept` as the `Accept` header (application/x-lsmsgpack, then application/msgpack, each with `stream=unknown-length` and without), and returns once the headers are there (`HttpCompletionOption.ResponseHeadersRead`). The request is sent when the enumeration starts.
- A server that doesn't stream (Web API 2, MVC 5, or `WriteStreams = false`) sends the `List<T>` or `T[]` as one body: the same items, read the same way. Nil and an empty body (`204 No Content`) are no items.
- The schema of the stream's head is learned as the schema of any response: the next requests list it in `MsgPack-Schemas`, and the server refers to it. The handler (`formatter.CreateHandler()`) passes streams through without buffering them. With `CreateHandler(true)` the stream range gets `schema=described` too.
- A status code outside 200-299 throws a [`MsgPackHttpException`](#error-responses), a response that isn't MsgPack (JSON) a `NotSupportedException`. An exception of the server's source after the first item aborts the response: the enumeration throws, after the items that arrived.
- `response.ReadFromMsgPackAsAsyncEnumerable<T>(formatter)` reads the items of a response you sent yourself (with `HttpCompletionOption.ResponseHeadersRead`, otherwise HttpClient buffers it first).

Anything else in a stream (several tables, objects of several types, an index; see [Streaming.md](Streaming.md)) is read with a `MsgPackStreamReader`, on .NET Framework too:

```csharp
using (MsgPackStreamReader reader = await client.GetMsgPackStreamAsync("api/export", formatter)) // or SendForMsgPackStreamAsync(request, formatter)
{
  await reader.ReadHeadAsync<List<Order>>();
  await reader.ReadAsync();                        // ArrayStart
  while (!await reader.AtEndAsync())
    Show(await reader.ReadObjectAsync<Order>());
  await reader.ReadAsync();                        // ArrayEnd
}
```

**Request bodies that are streams**: a `MsgPackStreamContent` writes the body while it is sent (chunked, without a `Content-Length`), with an [asynchronous](Streaming.md#asynchronous-writing) `MsgPackStreamWriter`:

```csharp
await client.PostAsync("api/orders/import", MsgPackStreamContent.Create(formatter, ordersAsync));   // an IAsyncEnumerable<T>: a head and an array of unknown length
await client.PostAsync("api/import", new MsgPackStreamContent(formatter, async writer =>
{
  writer.WriteHead<List<Order>>();
  writer.WriteArrayStart();
  foreach (Order order in orders)
    await writer.WriteObjectAsync(order); // or FlushIfFullAsync() after each value
  writer.WriteEnd();
}));
```

- The `Content-Type` is the media type (application/x-lsmsgpack by default) with `stream=unknown-length`. With application/x-lsmsgpack each schema is inline the first time it is used, and referred to after that, in the same body.
- The formatters only read bodies of a known length. The server reads such a body with `serializer.CreateStreamReader(Request.Body, Request.ContentType)` (the `LtMsgPackHttpSerializer` that `services.AddLsMsgPack()` registers) and its async methods: ASP.NET Core refuses synchronous reads.
- On .NET Framework, set `request.Headers.TransferEncodingChunked = true`: `HttpClientHandler` buffers a body without a length to compute it otherwise.

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
| HttpClient | `response.GetReadDifferences()` (or `content.GetReadDifferences()`) after `ReadFromMsgPackAsync` or `ReadAsAsync` | No |
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
// or with IHttpClientFactory: services.AddHttpClient("api").AddHttpMessageHandler(() => formatter.CreateDelegatingHandler());
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
- `CompleteSchemas = false`: a schema gets the classes of the values written so far, smaller when most derived classes are never sent.

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
