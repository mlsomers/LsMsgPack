# ASP.NET integration

LsMsgPack provides formatters for the ASP.NET stacks, so controllers can receive and return MsgPack the same way they handle JSON:

| Scenario | NuGet package | Targets | Namespace |
|---|---|---|---|
| [ASP.NET Core](#aspnet-core) MVC / Web API controllers | `LsMsgPack.AspNetCore` | ASP.NET Core 2.x (netstandard2.0, also on .NET Framework) and ASP.NET Core 3.0 and up (net8.0) | `LsMsgPackFormatters` |
| [ASP.NET Web API 2](#aspnet-web-api-2) (`System.Web.Http`) | `LsMsgPack.AspNet.WebApi` | netstandard2.0 (Web API 2 runs on .NET Framework) | `LsMsgPackWebApiFormatters` |
| [HttpClient](#httpclient) (`ObjectContent` / `ReadAsAsync`) | `LsMsgPack.AspNet.WebApi` | netstandard2.0, any platform | `LsMsgPackWebApiFormatters` |
| [ASP.NET MVC 5](#aspnet-mvc-5) (`System.Web.Mvc`) | `LsMsgPack.AspNet.Mvc` | .NET Framework 4.6.2+ | `LsMsgPackMvc` |

All of them depend on the core `LsMsgPack` package and speak exactly the same wire formats. For example, an HttpClient using the Web API formatter can talk to an ASP.NET Core server.

Media types
-----------

Every package supports three media types (`Content-Type` for request bodies and `Accept` for responses). Constants for them are defined in `LsMsgPack.MsgPackMediaTypes`:

| Media type | Constant | Format |
|---|---|---|
| `application/msgpack` | `MsgPackMediaTypes.MsgPack` | Plain MsgPack: maps of property names and values, with no indexed schema and no type ids. Any MsgPack implementation can read and write it. |
| `application/x-msgpack` | `MsgPackMediaTypes.XMsgPack` | Same as `application/msgpack` (an older, widely used name). |
| `application/x-lsmsgpack` | `MsgPackMediaTypes.XLsMsgPack` | Uses the `MsgPackSettings` you configure. By default, that is the [indexed schema](schema.md#the-indexed-schema) with type ids where needed, which gives smaller messages and full [polymorphism](schema.md#polymorphic-class-hierarchy-support). Use this between LsMsgPack endpoints. |

For the plain media types, the configured settings are used with `UseInexedSchema = false` and `AddTypeIdOptions = Never`. Other settings (filters, property name resolvers, endianness and so on) still apply. Because no type ids are written, properties declared as an interface or abstract class can't be read back from plain MsgPack. Use `application/x-lsmsgpack` for those, or see [resolving by signature](schema.md#resolving-by-signature).

The settings are copied when a formatter is created, so changing the `MsgPackSettings` instance afterwards has no effect. The same formatter can safely handle concurrent requests.

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

To change the settings used for `application/x-lsmsgpack`, pass a configuration action. The settings are also registered as `IOptions<MsgPackSettings>`:

```csharp
builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(settings =>
  {
    settings.AddTypeIdOptions = AddTypeIdOption.Always;
    settings.TypeResolvers = new IMsgPackTypeResolver[] { new XmlRootAttributeTypeResolver() };
  });
```

Alternatively, add them to `MvcOptions` directly, with or without a `MsgPackSettings` instance:

```csharp
builder.Services.AddControllers(options => options.AddLsMsgPackSerializerFormatters(new MsgPackSettings()));
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
- Request bodies are buffered and responses are written asynchronously, so no `AllowSynchronousIO` is needed.
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
  // or: config.Formatters.Add(new LsMsgPackMediaTypeFormatter(new MsgPackSettings { ... }));

  config.MapHttpAttributeRoutes();
}
```

Web API's content negotiation then handles the rest. Complex parameters are read from MsgPack bodies based on the `Content-Type`, and responses are written as MsgPack when the `Accept` header asks for it. JSON stays the default because the formatter is added after the JSON formatter.

A body that can't be deserialized is reported in the `ModelState` (check `ModelState.IsValid`), just like the JSON formatter does.

HttpClient
----------

The same package works on the client side, on any platform (.NET Framework, .NET Core / .NET 5+, Xamarin and so on). It builds on `System.Net.Http.Formatting` (the `Microsoft.AspNet.WebApi.Client` package):

```csharp
using LsMsgPack;
using LsMsgPackWebApiFormatters;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;

var formatter = new LsMsgPackMediaTypeFormatter();

var request = new HttpRequestMessage(HttpMethod.Post, "api/orders")
{
  Content = new ObjectContent<Order>(order, formatter, MsgPackMediaTypes.XLsMsgPack)
};
request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));

HttpResponseMessage response = await client.SendAsync(request);
response.EnsureSuccessStatusCode();
Order result = await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter });
```

Notes:
- Without an explicit media type, `ObjectContent` uses the formatter's first media type, `application/msgpack` (plain MsgPack).
- Set the `Accept` header, or the server will probably answer with JSON.
- On the client side, a response that can't be deserialized throws from `ReadAsAsync` instead of being logged.
- Use the same settings (at least the same `UseInexedSchema`) on the client and the server when using `application/x-lsmsgpack`.

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
  // or: LsMsgPackMvc.LsMsgPackMvc.Register(new MsgPackSettings { ... });

  RouteConfig.RegisterRoutes(RouteTable.Routes);
}
```

`Register` can be called again to replace the settings. It never adds a second binder.

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
  Settings = mySettings                       // instead of the settings passed to Register
};
```
