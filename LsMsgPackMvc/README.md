# LsMsgPack.AspNet.Mvc

MsgPack model binding and action result for ASP.NET MVC 5 controllers on .NET Framework 4.6.2+. Serializing is done by [LtMsgPack](https://www.nuget.org/packages/LtMsgPack), which reads and writes several times faster than System.Text.Json.

It speaks the same formats as [LsMsgPack.AspNetCore](https://www.nuget.org/packages/LsMsgPack.AspNetCore) and [LsMsgPack.AspNet.WebApi](https://www.nuget.org/packages/LsMsgPack.AspNet.WebApi) (also for `HttpClient`).

| Media type | Format |
|---|---|
| `application/msgpack`, `application/x-msgpack` | Plain MsgPack: maps keyed by property names, readable by any MsgPack implementation. |
| `application/x-lsmsgpack` | The indexed schema with type ids: smaller, polymorphic, and with the schema sent only once per client. Use this between LsMsgPack / LtMsgPack endpoints. |

## Usage

Register the model binder in `Global.asax.cs`:

```csharp
using LsMsgPackMvc;

protected void Application_Start()
{
  LsMsgPackMvc.LsMsgPackMvc.Register(); // or Register(new LtMsgPackHttpOptions { ... })
  RouteConfig.RegisterRoutes(RouteTable.Routes);
}
```

Complex parameters are then read from MsgPack request bodies (based on the `Content-Type`), all other requests are bound as usual. Return MsgPack with `this.MsgPack(...)`, which picks the media type from the `Accept` header:

```csharp
using LsMsgPackMvc;

public class OrdersController : Controller
{
  [HttpPost]
  public ActionResult Save(int id, Order order) // id from the route or query string, order from the MsgPack body
  {
    if (!ModelState.IsValid)
      return new HttpStatusCodeResult(400);

    return this.MsgPack(order);
  }
}
```

With `Register(new LtMsgPackHttpOptions { ReportDifferences = true })`, `HttpContext.GetReadDifferences()` says what didn't match between the request body and your classes, for example properties of another version of the models.

## Documentation

- [Web formatters](https://github.com/mlsomers/LsMsgPack/blob/master/docs/WebFormatters.md): settings per media type, `LsMsgPackResult`, schema negotiation
- [Compatibility with other MsgPack libraries](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Compatibility.md)
- [Security](https://github.com/mlsomers/LsMsgPack/blob/master/docs/security.md): request bodies from clients you don't trust
- Source and issues: [github.com/mlsomers/LsMsgPack](https://github.com/mlsomers/LsMsgPack)
