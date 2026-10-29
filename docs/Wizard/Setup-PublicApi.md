[Setup wizard](README.md) › [Sent](Network.md) › [Other parties](NetworkOthers.md) › My server

# Setup: a public API with MsgPack

An ASP.NET Core API that others call, in any language, next to JSON: partners, customers, scripts, other teams' apps.

## In short

| | |
|---|---|
| Package | `LsMsgPack.AspNetCore` (ASP.NET Web API 2 / MVC 5: `LsMsgPack.AspNet.WebApi` / `LsMsgPack.AspNet.Mvc`) |
| Media types | `application/msgpack` for everyone, `application/x-lsmsgpack` for clients with these packages, JSON as before |
| Settings | `Plain = LtMsgPackPresets.Generic()`, a `TypeGuard` on both |
| Keep in mind | requests are untrusted; published property names are a contract |

```
dotnet add package LsMsgPack.AspNetCore
```

## The server

```csharp
using LsMsgPack.TypeResolving.Types;
using LsMsgPackFormatters;
using LtMsgPack;

builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(o =>
  {
    // application/msgpack: what any MsgPack library expects (every value, Guid and decimal as text)
    o.Plain = LtMsgPackPresets.Generic();

    // Only the classes of your API may be created from request data
    AllowedTypesGuard guard = new AllowedTypesGuard().AllowAssemblyOf(typeof(Shipment));
    o.Plain.TypeGuard = guard;
    o.XLsMsgPack.TypeGuard = guard;
  });
```

Controllers don't change. Each client picks the format with the usual headers:

| The client sends | It gets |
|---|---|
| `Accept: application/json` (or nothing, or `*/*`) | JSON, as before |
| `Accept: application/msgpack` | plain MsgPack: maps of property names, any library reads it |
| `Accept: application/x-lsmsgpack` | the compact format with schema and class hierarchies (the .NET and TypeScript clients of these packages) |

Request bodies are read by their `Content-Type` in the same way. With minimal APIs: `builder.Services.AddLsMsgPack(o => ...)` and `MsgPackResults.MsgPack(value)` / `MsgPackBody<T>`, see [WebFormatters.md](../WebFormatters.md#minimal-apis). The media types appear in the OpenAPI description.

## What your clients do

Python with `requests` and `msgpack`:

```python
import msgpack, requests
from datetime import datetime, timezone

r = requests.get('https://api.example.com/api/shipments/42', headers={'Accept': 'application/msgpack'})
shipment = msgpack.unpackb(r.content, timestamp=3)
print(shipment['Id'], shipment['Weight'])

body = msgpack.packb({'Destination': 'Utrecht', 'Weight': 12.5, 'PickUp': datetime.now(timezone.utc)}, datetime=True)
requests.post('https://api.example.com/api/shipments', data=body,
              headers={'Content-Type': 'application/msgpack', 'Accept': 'application/msgpack'})
```

Put the two media types and an example like this in your API documentation. The keys are the .NET property names (`Weight`), and request keys are matched ignoring case, so `weight` works too.

## Why these settings

- **`Generic()` for `application/msgpack`.** The plain default leaves out values that are `0`, `false` or `null` and writes `Guid` and `decimal` in .NET's binary form, which is right between .NET programs but makes a Python client check for missing keys and decode bytes. `Generic()` writes every value, and `Guid` and `decimal` as text. If most of your clients use MessagePack-CSharp, `LtMsgPackPresets.MessagePackCSharp()` writes exactly what it expects ([Compatibility.md](../Compatibility.md)).
- **The type guard.** Clients of `application/x-lsmsgpack` can send class names for properties declared as an interface or base class, and the server creates those classes. The serializers already refuse a class that doesn't fit the declared type and never load assemblies; the guard also limits it to the classes of your API. Plain MsgPack carries no class names, so it's mostly a safety net there.
- **`application/x-lsmsgpack` stays available** for your own .NET and TypeScript clients, which get smaller responses and class hierarchies. Nothing to configure.

## Changing your API later

Clients you don't control can't be updated with your server, so treat the property names as a published contract, like the names in your JSON:

- **Adding** properties to responses is fine, if clients ignore keys they don't know (most do; tell them to). Adding optional properties to requests is fine: an older client leaves them out and they keep the constructor's value.
- **Removing or renaming** a property breaks clients that use it. Keep it until you release a new version of the endpoint (`/api/v2/...`).
- **Find out what clients send** that your classes don't have: `o.ReportDifferences = true` logs it (at the `Debug` level, or as warnings with `LogDifferencesAsWarning`). Handy after releasing a change.

## Watch out

- **Avoid `object` in request classes** (`object`, `List<object>`, `Dictionary<string, object>`): there the data picks the class. The analyzers that come with the package warn about them (LSMP004, LSMP005). More in [security.md](../security.md).
- **Limits are built in**: nesting deeper than 256 levels is refused, and lengths are checked against the body before anything is allocated. Keep ASP.NET Core's request size limit (`MaxRequestBodySize`) at a sensible value for your API.
- **Class hierarchies over plain MsgPack**: a property declared as an interface or abstract class can't be read from plain MsgPack (there's no class name). Use concrete classes in public request types, or a property that says which variant it is.
- **`DateTimeOffset`** is sent as the moment only with `Generic()`: the offset is lost.

## Next

- Every option of the formatters: [WebFormatters.md](../WebFormatters.md).
- Settings per client library, tested: [Compatibility.md](../Compatibility.md).
- Your own front end on the same API: [TypeScript client](NetworkBrowser.md). Your own .NET app: [.NET to .NET over HTTP](Setup-DotNetHttp.md).
