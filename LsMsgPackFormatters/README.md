# LsMsgPack.AspNetCore

MsgPack input and output formatters for ASP.NET Core MVC / Web API controllers. Controllers receive and return MsgPack the same way they handle JSON, without any changes. Serializing is done by [LtMsgPack](https://www.nuget.org/packages/LtMsgPack), which reads and writes several times faster than System.Text.Json.

Targets ASP.NET Core 2.x (netstandard2.0, also on .NET Framework) and ASP.NET Core 3.0 and up (net8.0).

## Usage

```csharp
using LsMsgPackFormatters;

builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters();
```

Clients choose the format with the `Content-Type` and `Accept` headers. JSON stays the default for clients that don't ask for MsgPack.

| Media type | Format |
|---|---|
| `application/msgpack`, `application/x-msgpack` | Plain MsgPack: maps keyed by property names, readable by any MsgPack implementation. |
| `application/x-lsmsgpack` | The indexed schema with type ids: smaller, polymorphic, and with the schema sent only once per client. Use this between LsMsgPack / LtMsgPack endpoints. |

To change the settings, pass a configuration action:

```csharp
using LsMsgPack.TypeResolving.Types;
using LtMsgPack;

builder.Services.AddControllers()
  .AddLsMsgPackSerializerFormatters(o =>
  {
    o.Plain = LtMsgPackPresets.MessagePackCSharp(); // or Nerdbank(), Generic()
    o.XLsMsgPack.TypeGuard = new AllowedTypesGuard().AllowAssemblyOf(typeof(Order));
  });
```

To find out what didn't match between request bodies and your classes (properties of another version of the models), set `o.ReportDifferences = true`: the differences are available as `HttpContext.GetReadDifferences()` and logged at the `Debug` level (as a warning with `o.LogDifferencesAsWarning = true`). For example, to tell the client why a value is missing:

```csharp
if (order.Address == null)
  return BadRequest("Validation failed: address is required\r\n" + 
    // Don't do this if your schema should not be public knowledge!
    HttpContext.GetReadDifferences());
```

On the client side, use [LsMsgPack.AspNet.WebApi](https://www.nuget.org/packages/LsMsgPack.AspNet.WebApi) with `HttpClient`, or any MsgPack library for the plain media types.

## Documentation

- [Web formatters](https://github.com/mlsomers/LsMsgPack/blob/master/docs/WebFormatters.md): settings per media type, examples, schema negotiation
- [Compatibility with other MsgPack libraries](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Compatibility.md)
- [Security](https://github.com/mlsomers/LsMsgPack/blob/master/docs/security.md): request bodies from clients you don't trust
- Source and issues: [github.com/mlsomers/LsMsgPack](https://github.com/mlsomers/LsMsgPack)
