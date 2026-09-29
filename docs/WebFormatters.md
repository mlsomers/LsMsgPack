# Web formatters

Three packages serialize request and response bodies with LtMsgPack (the same data as LsMsgPack, faster):

| Package | Project | Use |
|---|---|---|
| `LsMsgPack.AspNetCore` | `LsMsgPackFormatters` | ASP.NET Core MVC / Web API controllers: `services.AddControllers().AddLsMsgPackSerializerFormatters()` |
| `LsMsgPack.AspNet.WebApi` | `LsMsgPackWebApiFormatters` | ASP.NET Web API 2 (`config.Formatters.Add(new LsMsgPackMediaTypeFormatter())`) and HttpClient on any platform |
| `LsMsgPack.AspNet.Mvc` | `LsMsgPackMvc` | ASP.NET MVC 5: `LsMsgPackMvc.Register()` in Application_Start, `return this.MsgPack(data);` in actions |

## Media types and settings

| Content-Type / Accept | Settings (`LtMsgPackHttpOptions`) | Default |
|---|---|---|
| `application/msgpack`, `application/x-msgpack` | `Plain` | maps keyed by property names, no type ids, default values left out. Use a preset for another library (below). |
| `application/x-lsmsgpack` | `XLsMsgPack` | the indexed schema, type ids where the type differs, schema references between these formatters (`NegotiateSchemas`) |

```csharp
services.AddControllers().AddLsMsgPackSerializerFormatters(o =>
{
  o.Plain = LtMsgPackPresets.MessagePackCSharp(); // or Nerdbank(), Generic(), see Compatibility.md
});
```

## Schema references (application/x-lsmsgpack)

The indexed schema makes a payload smaller, but it is sent with every body. Between these formatters it is sent once:

1. The client lists the schemas it holds in the request header `MsgPack-Schemas` (ids of 32 hexadecimal digits, comma separated).
2. The server writes the body with a reference to its schema (18 bytes) when the client holds it, and with the schema inline otherwise. A schema of at most 18 bytes is always sent inline. The response names its schema in `MsgPack-Schema` and has `Vary: MsgPack-Schemas`.
3. The client registers the schemas it receives inline and lists them in the next requests to that server (at most `MaxAdvertisedSchemas`, 32 by default, the most recent ones).

On the client (HttpClient, the Web API package) this is one line:

```csharp
LsMsgPackMediaTypeFormatter formatter = new LsMsgPackMediaTypeFormatter();
HttpClient client = new HttpClient(formatter.CreateHandler()); // formatter.CreateHandler(inner) to chain another handler
```

Without the handler (or from any other client) nothing changes: the responses carry their schema inline, as plain `application/x-lsmsgpack`.

- **Request bodies always carry their schema inline.** A client cannot know whether the server (or which instance behind a load balancer) still holds a schema, and retrying a request that the server may already have handled is not safe. The server caches the schemas of requests by their bytes, so reading them costs little.
- A schema grows when a new type is written (e.g. another implementation of an interface): it gets a new id, the client learns it from the next inline response.
- The server keeps the schemas it writes; schemas received from clients are limited by `SchemaStore.MaxSchemas`. A client whose store is full keeps working, it just stops listing new schemas.
- ASP.NET Web API 2 as a server: a formatter cannot set response headers, so `MsgPack-Schema` is a content header and, instead of `Vary`, a response with a reference has `Expires` in the past, so shared caches do not keep a body only this client can read.
- `NegotiateSchemas = false` turns it off (the schema is always inline).

Measured on the invoices of `BenchmarkInvoices` (100 invoices, LtMsgPack, one serializer, in process): the schema inline 226.5 KB, write ~400 µs, read ~510 µs; with references 188.0 KB (-17%), write ~395 µs, read ~422 µs (-17%).
