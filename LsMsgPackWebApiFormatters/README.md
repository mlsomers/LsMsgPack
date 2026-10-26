# LsMsgPack.AspNet.WebApi

MsgPack `MediaTypeFormatter` for ASP.NET Web API 2 and for `HttpClient` (`ObjectContent` / `ReadAsAsync`) on any platform. Serializing is done by [LtMsgPack](https://www.nuget.org/packages/LtMsgPack), which reads and writes several times faster than System.Text.Json.

It speaks the same formats as [LsMsgPack.AspNetCore](https://www.nuget.org/packages/LsMsgPack.AspNetCore) and [LsMsgPack.AspNet.Mvc](https://www.nuget.org/packages/LsMsgPack.AspNet.Mvc), so a client using this package can talk to a server using either of them.

| Media type | Format |
|---|---|
| `application/msgpack`, `application/x-msgpack` | Plain MsgPack: maps keyed by property names, readable by any MsgPack implementation. |
| `application/x-lsmsgpack` | The indexed schema with type ids: smaller, polymorphic, and with the schema sent only once per client. Use this between LsMsgPack / LtMsgPack endpoints. |

## ASP.NET Web API 2

```csharp
using LsMsgPackWebApiFormatters;

public static void Register(HttpConfiguration config)
{
  config.Formatters.Add(new LsMsgPackMediaTypeFormatter());
  config.MapHttpAttributeRoutes();
}
```

Web API's content negotiation handles the rest, based on the `Content-Type` and `Accept` headers. JSON stays the default.

## HttpClient

```csharp
using LsMsgPack;
using LsMsgPackWebApiFormatters;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;

// Once, for the lifetime of the app. The handler lets the server stop repeating schemas the client already has.
LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
HttpClient client = new HttpClient(formatter.CreateHandler()) { BaseAddress = new Uri("https://example.com/") };
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.XLsMsgPack));

// Per call
HttpResponseMessage response = await client.PostAsync("api/orders", new ObjectContent<Order>(order, formatter, MsgPackMediaTypes.XLsMsgPack));
response.EnsureSuccessStatusCode();
Order saved = await response.Content.ReadAsAsync<Order>(new MediaTypeFormatter[] { formatter });
```

When your classes differ from the server's (other names or namespaces, e.g. an SDK with its own models), create the handler with `formatter.CreateHandler(true)`: it asks for the described schema, by which the reader pairs your classes with the server's ([details](https://github.com/mlsomers/LsMsgPack/blob/master/docs/WebFormatters.md#schema-version-2-for-clients-without-the-classes)).

Settings are passed as `new LsMsgPackMediaTypeFormatter(new LtMsgPackHttpOptions { ... })`. In trimmed or AOT apps (e.g. iOS), use the code of LtMsgPack's source generator: `new LsMsgPackMediaTypeFormatter(new LtMsgPackHttpSerializer(AppMsgPackContext.Default, options))` (the constructors without a context are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, see [Trimming and NativeAOT](https://github.com/mlsomers/LsMsgPack/blob/master/docs/SourceGenerator.md#trimming-and-nativeaot)). With `ReportDifferences = true`, `response.GetReadDifferences()` (on the server `Request.GetReadDifferences()`) says what didn't match between the body and your classes, for example properties of another version of the models.

## Documentation

- [Web formatters](https://github.com/mlsomers/LsMsgPack/blob/master/docs/WebFormatters.md): settings per media type, examples, schema negotiation
- [Client applications](https://github.com/mlsomers/LsMsgPack/blob/master/docs/ClientApps.md)
- [Compatibility with other MsgPack libraries](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Compatibility.md)
- [Security](https://github.com/mlsomers/LsMsgPack/blob/master/docs/security.md): request bodies from clients you don't trust
- Source and issues: [github.com/mlsomers/LsMsgPack](https://github.com/mlsomers/LsMsgPack)
