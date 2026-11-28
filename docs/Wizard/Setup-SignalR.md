[Setup wizard](README.md) › [Sent](Network.md) › [My .NET programs](NetworkDotNet.md) › SignalR

# Setup: SignalR (real-time messages)

Your ASP.NET Core server has SignalR hubs, and its clients (.NET apps, a browser) call hub methods and receive messages as they happen: chat, live dashboards, notifications, games. SignalR serializes the messages; this package makes it use LsMsgPack instead of JSON.

## In short

| | |
|---|---|
| Package | `LsMsgPack.SignalR` (server and .NET clients), `@lsmsgpack/client` (browser) |
| Registration | `AddSignalR().AddLsMsgPackProtocol()` on the server, `.AddLsMsgPackProtocol()` on the `HubConnectionBuilder` |
| Format | MsgPack without a schema: objects as maps of property names, type ids where needed |
| Keep in mind | your hubs stay as they are; existing clients of Microsoft's MessagePack protocol need `AddLsMsgPackAsMessagePackProtocol()` |

```
dotnet add package LsMsgPack.SignalR
```

## The code

Server (`Program.cs`):

```csharp
builder.Services.AddSignalR()
  .AddLsMsgPackProtocol();

app.MapHub<ChatHub>("/chat");
```

The hub is an ordinary hub, nothing changes in it:

```csharp
public class ChatHub : Hub
{
  public Task Send(ChatMessage message) => Clients.All.SendAsync("Receive", message);
}
```

A .NET client (WPF, MAUI, a console app), with `Microsoft.AspNetCore.SignalR.Client` and `LsMsgPack.SignalR`:

```csharp
HubConnection connection = new HubConnectionBuilder()
  .WithUrl("https://example.com/chat")
  .AddLsMsgPackProtocol()
  .Build();

connection.On<ChatMessage>("Receive", message => Show(message));
await connection.StartAsync();
await connection.InvokeAsync("Send", new ChatMessage { User = "ann", Text = "Hello" });
```

A browser, with `@microsoft/signalr` and `@lsmsgpack/client`:

```ts
import { HubConnectionBuilder } from '@microsoft/signalr';
import { LsMsgPackHubProtocol } from '@lsmsgpack/client/signalr';

const connection = new HubConnectionBuilder()
  .withUrl('/chat')
  .withHubProtocol(new LsMsgPackHubProtocol())
  .build();

connection.on('Receive', (message) => show(message.User, message.Text)); // the .NET property names
await connection.start();
await connection.invoke('Send', { user: 'ann', text: 'Hello' });         // camelCase works too
```

## Why these settings

- **No schema in the messages.** SignalR writes a message once and sends the same bytes to every connection of a group, so a message can't depend on what one client received before. The property names go in every object instead, which is what lets old and new versions of client and server understand each other.
- **Every value is written**, also defaults, so a browser gets complete objects without knowing your classes.
- **Type ids** where a value's class differs from the declared one: a `List<Shape>` of circles and squares arrives as circles and squares (`$type` in JavaScript).

## Already using Microsoft's MessagePack protocol?

Clients that use `@microsoft/signalr-protocol-msgpack` or `.AddMessagePackProtocol()` keep working when the server registers LsMsgPack under that protocol's name instead of Microsoft's package:

```csharp
builder.Services.AddSignalR()
  .AddLsMsgPackProtocol()                // new clients
  .AddLsMsgPackAsMessagePackProtocol();  // instead of .AddMessagePackProtocol(): the same bytes for the existing clients
```

Then move clients over at your own pace.

## Changing your classes later

Clients update at different times (an app store, a browser tab left open for days), so old and new versions talk to each other:

- **Safe**: adding a property (the other side skips it or keeps its default), removing one, reordering.
- **Not safe**: renaming a property, a hub method or a client method, or changing a parameter's type. Add the new one next to the old one, and remove the old one when no client uses it anymore.

## Watch out

- **Messages come from clients**: anyone who can connect can send anything. The protocol limits each value (a million objects) and SignalR limits a message to 32 KB by default; a value that doesn't fit the hub method's parameters fails that invocation, not the connection. See [Add-ons](Extras.md#reading-data-you-dont-trust) for limiting the classes type ids can create.
- **Hub methods are found by name**, and so are the properties of a message: keep the names of the parameters' classes stable (see above).
- **Dates arrive with their kind** in .NET, as a `Date` in the browser.

## Next

- Messages with lists of objects: add `.AddLsMsgPackIndexedProtocol()` next to `.AddLsMsgPackProtocol()` and let those clients ask for "lsmsgpack-indexed" (every name once per value: 50 objects 43% smaller, faster to read; larger for a single small object), see [docs/SignalR.md](../SignalR.md#the-indexed-schema-lsmsgpack-indexed).
- NativeAOT or trimming: pass your source generator context, `AddLsMsgPackProtocol(AppMsgPackContext.Default)`, see [docs/SignalR.md](../SignalR.md#nativeaot-and-trimming).
- What's inside a message: the VS Code extension and the MCP server show the bytes of a connection message by message, see [docs/SignalR.md](../SignalR.md#debugging).
- All options, the values per protocol and the measurements: [docs/SignalR.md](../SignalR.md).
