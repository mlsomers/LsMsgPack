# SignalR (LsMsgPack.SignalR)

The `LsMsgPack.SignalR` package is a MessagePack hub protocol for ASP.NET Core SignalR on LsMsgPack, for the server and the .NET client. It comes in two forms, which can be used side by side:

- **"lsmsgpack"**: LsMsgPack's values (exact decimals, DateTimes with their kind, type ids for class hierarchies), for the TypeScript client `@lsmsgpack/client/signalr` and .NET clients with this package.
- **"messagepack"**: the bytes of Microsoft's MessagePack protocol (`AddMessagePackProtocol()`), so the clients of that protocol connect without a change: `@microsoft/signalr-protocol-msgpack` in JavaScript, and the .NET, Java and Swift clients. Use it instead of Microsoft's package.

The envelope of each message (its type, headers, invocation id, target, arguments, stream ids) is Microsoft's MessagePack protocol for both names; only the values in it differ. Writing and reading take about half the time of Microsoft's MessagePack protocol ([Performance](#performance)).

## Server

```
dotnet add package LsMsgPack.SignalR
```

```csharp
builder.Services.AddSignalR()
  .AddLsMsgPackProtocol()                 // "lsmsgpack"
  .AddLsMsgPackAsMessagePackProtocol();   // "messagepack", for the clients of Microsoft's protocol (optional)
```

The hubs stay as they are. Each client picks a protocol by name in its handshake; JSON stays available.

## .NET client

```csharp
HubConnection connection = new HubConnectionBuilder()
  .WithUrl("https://example.com/chat")
  .AddLsMsgPackProtocol()                 // or .AddLsMsgPackAsMessagePackProtocol() for a server with Microsoft's protocol
  .Build();
```

The package targets .NET Standard 2.0 and .NET 8, so console, WPF, WinForms and MAUI apps (also on .NET Framework) use it with `Microsoft.AspNetCore.SignalR.Client`.

## TypeScript client

```ts
import { HubConnectionBuilder } from '@microsoft/signalr';
import { LsMsgPackHubProtocol } from '@lsmsgpack/client/signalr';

const connection = new HubConnectionBuilder()
  .withUrl('/chat')
  .withHubProtocol(new LsMsgPackHubProtocol())   // { name: 'messagepack' } for a server with Microsoft's protocol
  .build();
```

`LsMsgPackHubProtocol` takes the options of the client's decoder and encoder (`decimal`, `int64`, `references`, `maxDepth`, see [TypeScriptClient.md](TypeScriptClient.md#options)). It imports nothing of `@microsoft/signalr`: it has the shape of its `IHubProtocol`, so it adds no dependency and works with versions 8 and up.

The values come without a schema (below), so objects have the .NET property names (`message.User`, as with Microsoft's MessagePack client), a type id becomes `$type`, Guids are text, decimals numbers (the `decimal` option) and DateTimes `Date`s. Objects you send may use camelCase names: the server reads names ignoring case.

## The values

| | "lsmsgpack" | "messagepack" |
|---|---|---|
| objects | maps keyed by property names, every value written | the same |
| class hierarchies | type ids where a value's class differs from the declared type | none (MessagePack-CSharp's contractless resolver has none) |
| enums | numbers | names |
| `Guid` | text | text |
| `decimal` | exact (LsMsgPack's extension type 1) | text |
| `DateTime` | with its kind (extension type 6) | timestamp (`Unspecified` taken as UTC) |
| `DateTimeOffset` | the moment and the offset in minutes | the clock time and the offset in minutes |
| fields | the attributed ones (`[DataMember]`, `[JsonInclude]`) | public fields too |
| property names read | exact, else ignoring case | the same |

These are `LsMsgPackHubProtocolOptions.DefaultSerializerOptions(asMessagePack)`. To change them, start from those:

```csharp
.AddLsMsgPackProtocol(o =>
{
  LsMsgPackOptions values = LsMsgPackHubProtocolOptions.DefaultSerializerOptions(false);
  values.EnumFormat = EnumFormat.Name;
  o.SerializerOptions = values;
});
```

**No indexed schema.** SignalR serializes a message once for all the connections it goes to (a group, all clients), so its bytes can't depend on what one connection received before: schema references are out. A schema inline in every message costs more than it saves on small messages. The protocol turns the indexed schema off whatever the options say. Every value is written, also defaults, so a JavaScript client without the classes gets complete objects.

**Class hierarchies.** An argument, a stream item or a result is written as its runtime class, as Microsoft's protocols do (SignalR doesn't pass the declared types to the protocol). The type ids are written below it: a `List<Shape>` holding circles and squares, or a property declared as `Shape`, round trip. An argument of a derived class is read as the class the hub method declares. With "messagepack" there are no type ids, so abstract classes and interfaces can't be read.

**References.** Each value is a payload of its own: with `ReferenceHandling.Preserve` in the options, an object shared within an argument is written once and read back as one instance (see [References.md](References.md)). Arguments don't share objects with each other.

## Untrusted data

The values of a message come from the other side. The limits of the web formatters apply to each value: at most 1,000,000 arrays and maps (`MaxObjects`) and 10,000,000 elements and entries (`MaxItems`); SignalR's `MaximumReceiveMessageSize` (32 KB by default) limits the bytes. A value past a limit, or one that doesn't fit the hub method's parameters, is a binding failure: SignalR answers the invocation with an error and the connection stays. Type ids pick classes only where the declared type allows them; a type guard limits them further, see [security.md](security.md).

```csharp
.AddLsMsgPackProtocol(o => o.MaxObjects = 10000)
```

## NativeAOT and trimming

Pass the context of LsMsgPack's [source generator](SourceGenerator.md), with the parameter and return types of the hub methods (and of the client methods the server calls):

```csharp
[LsMsgPackSerializable(typeof(ChatMessage))]
[LsMsgPackSerializable(typeof(RoomState))]
public partial class AppMsgPackContext : LsMsgPackContext { }

builder.Services.AddSignalR().AddLsMsgPackProtocol(AppMsgPackContext.Default);
```

The overloads without a context are marked as requiring unreferenced and dynamic code. The NativeAOT smoke test (`LsMsgPackAotSmokeTest/run.sh`) checks both names with the generated code, without trim or AOT warnings, and by reflection without it (the model kept by the app, see [Without the generator](SourceGenerator.md#without-the-generator)).

## Debugging

The [MCP server](Mcp.md), the [VS Code extension](../LsMsgPackVsCode/README.md) and MsgPack Explorer recognize the bytes of a hub connection (either name): the handshake, each message's length prefix and the message with its fields by name. Without that, a length prefix of 128 or more reads as a MsgPack header and the rest goes astray.

```
Structure: a capture of a SignalR hub connection (...): the handshake {"protocol":"lsmsgpack","version":2},
6 messages (1 Invocation, 1 StreamItem, 2 Completion, 1 Ping, 1 Close), targets Send. ...
  { // SignalR Invocation
    "type": 1, // Invocation
    "headers": { "trace": "abc" },
    "invocationId": "1",
    "target": "Send",
    "arguments": [ { "User": "ann", "Text": "Hello", "Sent": "2026-11-20T09:00:00Z", ... }, 7 ]
  },
```

Where to get the bytes:

- **The browser**: DevTools, Network, the WebSocket connection, Messages: a binary message copied as base64 goes into **MsgPack: Explore Bytes from Clipboard** (VS Code) or `lsmsgpack-mcp decode --data <base64>`. With long polling the bytes are the bodies of the poll responses.
- **A .NET program paused in the debugger**: in a hub protocol's `TryParseMessage` the `input` (`ReadOnlySequence<byte>`) and in `WriteMessage` the bytes written are read by **View as MsgPack** and the MCP server's `msgpack_debug_read`.

## Performance

Writing and reading an invocation with a chat message (about 150 bytes), and one with a list of 50 of them, per message:

| .NET 10, Release | Bytes | Write | Read |
|---|---|---|---|
| "lsmsgpack" | 156 | 305 ns | 688 ns |
| "messagepack" (LsMsgPack) | 145 | 355 ns | 587 ns |
| Microsoft's MessagePack protocol | 145 | 556 ns | 1,105 ns |
| Microsoft's JSON protocol | 222 | 764 ns | 2,210 ns |
| 50 messages: "lsmsgpack" | 7,214 | 9.3 µs | 21.0 µs |
| 50 messages: Microsoft's MessagePack protocol | 6,664 | 18.4 µs | 38.8 µs |
| 50 messages: Microsoft's JSON protocol | 9,154 | 25.7 µs | 72.3 µs |

Writing allocates nothing; the values are read in place from the received bytes. Measured by `ProtocolBenchmark` in `LsMsgPack.SignalRTests` (`TestCategory=Benchmark`).

| Node 22 | Bytes | Write | Read |
|---|---|---|---|
| `@lsmsgpack/client/signalr` | 147 | 1.27 µs | 1.77 µs |
| `@microsoft/signalr-protocol-msgpack` | 147 | 3.63 µs | 2.47 µs |
| JSON protocol | 254 | 2.39 µs | 1.55 µs |
| 50 messages: `@lsmsgpack/client/signalr` | 6,772 | 38 µs | 59 µs |
| 50 messages: `@microsoft/signalr-protocol-msgpack` | 6,764 | 92 µs | 75 µs |
| 50 messages: JSON protocol | 9,382 | 81 µs | 43 µs |

In JavaScript the JSON protocol reads large messages faster: `JSON.parse` is native code, and without a schema the client reads plain MsgPack. Measured by `clients/typescript/bench/signalr.mjs`.

## Compared with Microsoft's MessagePack protocol

- The same messages, fields, binding failures and error texts (`InvalidDataException` naming the field), protocol version 2 (stateful reconnect: Ack and Sequence messages), `RawResult` for the client results a server passes on.
- "messagepack" writes the same bytes as Microsoft's protocol with its default options (`LsMsgPack.SignalRTests` compares every message type and runs both clients against both servers).
- No MessagePack-CSharp dependency, and it's faster. Microsoft's protocol takes MessagePack-CSharp's resolvers and attributes (`[Key]`, `[Union]`): LsMsgPack doesn't read those, so classes written as arrays by `[Key]` don't fit.
