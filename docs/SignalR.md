# SignalR (LsMsgPack.SignalR)

The `LsMsgPack.SignalR` package is a MessagePack hub protocol for ASP.NET Core SignalR on LsMsgPack, for the server and the .NET client. It comes in three forms, which can be used side by side:

- **"lsmsgpack"**: LsMsgPack's values (exact decimals, DateTimes with their kind, type ids for class hierarchies), for the TypeScript client `@lsmsgpack/client/signalr` and .NET clients with this package.
- **"lsmsgpack-indexed"**: the same with the [indexed schema](schema.md#the-indexed-schema) in each value the server writes, so a list of objects has every name once: 43% smaller for 50 chat messages, and faster to read, also in JavaScript ([below](#the-indexed-schema-lsmsgpack-indexed)).
- **"messagepack"**: the bytes of Microsoft's MessagePack protocol (`AddMessagePackProtocol()`), so the clients of that protocol connect without a change: `@microsoft/signalr-protocol-msgpack` in JavaScript, and the .NET, Java and Swift clients. Use it instead of Microsoft's package.

The envelope of each message (its type, headers, invocation id, target, arguments, stream ids) is Microsoft's MessagePack protocol for every name; only the values in it differ. Writing and reading take about half the time of Microsoft's MessagePack protocol ([Performance](#performance)).

## Server

```
dotnet add package LsMsgPack.SignalR
```

```csharp
builder.Services.AddSignalR()
  .AddLsMsgPackProtocol()                 // "lsmsgpack"
  .AddLsMsgPackIndexedProtocol()          // "lsmsgpack-indexed" (optional)
  .AddLsMsgPackAsMessagePackProtocol();   // "messagepack", for the clients of Microsoft's protocol (optional)
```

The hubs stay as they are. Each client picks a protocol by name in its handshake; JSON stays available.

## .NET client

```csharp
HubConnection connection = new HubConnectionBuilder()
  .WithUrl("https://example.com/chat")
  .AddLsMsgPackProtocol()                 // or .AddLsMsgPackIndexedProtocol(), or .AddLsMsgPackAsMessagePackProtocol() for a server with Microsoft's protocol
  .Build();
```

The package targets .NET Standard 2.0 and .NET 8, so console, WPF, WinForms and MAUI apps (also on .NET Framework) use it with `Microsoft.AspNetCore.SignalR.Client`.

## TypeScript client

```ts
import { HubConnectionBuilder } from '@microsoft/signalr';
import { LsMsgPackHubProtocol } from '@lsmsgpack/client/signalr';

const connection = new HubConnectionBuilder()
  .withUrl('/chat')
  .withHubProtocol(new LsMsgPackHubProtocol())   // { name: 'lsmsgpack-indexed' }, or { name: 'messagepack' } for a server with Microsoft's protocol
  .build();
```

`LsMsgPackHubProtocol` takes the options of the client's decoder and encoder (`decimal`, `int64`, `references`, `maxDepth`, see [TypeScriptClient.md](TypeScriptClient.md#options)). It imports nothing of `@microsoft/signalr`: it has the shape of its `IHubProtocol`, so it adds no dependency and works with versions 8 and up.

With "lsmsgpack" the values come without a schema (below), so objects have the .NET property names (`message.User`, as with Microsoft's MessagePack client), a type id becomes `$type`, Guids are text, decimals numbers (the `decimal` option) and DateTimes `Date`s. Objects you send may use camelCase names: the server reads names ignoring case.

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

**No indexed schema** with "lsmsgpack" (see the next section for "lsmsgpack-indexed"): the protocol turns it off whatever the options say. Every value is written, also defaults, so a JavaScript client without the classes gets complete objects.

**Class hierarchies.** An argument, a stream item or a result is written as its runtime class, as Microsoft's protocols do (SignalR doesn't pass the declared types to the protocol). The type ids are written below it: a `List<Shape>` holding circles and squares, or a property declared as `Shape`, round trip. An argument of a derived class is read as the class the hub method declares. With "messagepack" there are no type ids, so abstract classes and interfaces can't be read.

**References.** Each value is a payload of its own: with `ReferenceHandling.Preserve` in the options, an object shared within an argument is written once and read back as one instance (see [References.md](References.md)). Arguments don't share objects with each other.

## The indexed schema ("lsmsgpack-indexed")

"lsmsgpack-indexed" writes each value with its [indexed schema](schema.md#the-indexed-schema) in front: the names of a class once, then the objects as arrays of their values. A list of 50 chat messages is 4,126 bytes instead of 7,214, and both sides read it faster ([Performance](#performance)). A single small object grows instead (247 bytes instead of 156): the schema costs more than the names it saves. So it pays for messages with lists of objects, not for chat-sized ones.

```csharp
builder.Services.AddSignalR().AddLsMsgPackProtocol().AddLsMsgPackIndexedProtocol();   // server: both names
new HubConnectionBuilder().WithUrl(url).AddLsMsgPackIndexedProtocol().Build();          // .NET client
```

```ts
new HubConnectionBuilder().withUrl('/chat').withHubProtocol(new LsMsgPackHubProtocol({ name: 'lsmsgpack-indexed' })).build();
```

- **Inline in each value, never a reference.** SignalR serializes a message once for all the connections it goes to (a group, all clients), so its bytes can't depend on what one connection received before: [schema references](WebFormatters.md#schema-references-applicationx-lsmsgpack) are out.
- **Each value is `nil`, `[schema, body]` or `[nil, value without a schema]`**, one item in the envelope's arrays (the arguments array keeps its count). The readers on both sides take all three. The .NET protocol writes `[schema, body]`; the TypeScript client writes `[nil, value]`, because it writes plain values (as in its HTTP requests). A value of the other name (no wrapper) is a binding failure that names the protocol.
- **The described schema** ([version 2](schema.md#version-2-the-declared-types)) by default: it gives a client without the classes (JavaScript, a .NET client with its own classes) the declared types and the defaults. So the values are LsMsgPack's defaults (`DefaultSerializerOptions(false, true)`): objects as arrays, default values left out, Guids as 16 bytes, which the JavaScript client still reads as text. The names-only schema (`SchemaVersion.Names` in `SerializerOptions`) is smaller for single objects (160 bytes), but only for .NET clients with the server's classes: JavaScript gets arrays of values from it.
- **The JavaScript client keeps the .NET names** with this name too (`propertyNames: 'dotnet'` unless you pass another), so switching between the names doesn't rename properties.
- **Register both names on the server** to serve clients of either. A broadcast to clients of both names is then serialized once per name, as SignalR does for every protocol.
- Not with "messagepack" (`AsMessagePack` and `IndexedSchema` together throw).

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

The [MCP server](Mcp.md), the [VS Code extension](../LsMsgPackVsCode/README.md) and MsgPack Explorer recognize the bytes of a hub connection (any of the names): the handshake, each message's length prefix and the message with its fields by name. With "lsmsgpack-indexed" each value is read with its own schema (class and property names, the declared types) and the schema is marked as one. A capture without the client's handshake is taken for "lsmsgpack-indexed" when every value has its shape (`nil`, `[schema, body]` or `[nil, value]`) and at least one holds a schema. Without that, a length prefix of 128 or more reads as a MsgPack header and the rest goes astray.

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
| "lsmsgpack" | 156 | 321 ns | 521 ns |
| "lsmsgpack-indexed" | 247 | 356 ns | 548 ns |
| "lsmsgpack-indexed", names-only schema | 160 | 366 ns | 567 ns |
| "messagepack" (LsMsgPack) | 145 | 368 ns | 533 ns |
| Microsoft's MessagePack protocol | 145 | 470 ns | 950 ns |
| Microsoft's JSON protocol | 222 | 782 ns | 2,190 ns |
| 50 messages: "lsmsgpack" | 7,214 | 7.9 µs | 14.6 µs |
| 50 messages: "lsmsgpack-indexed" | 4,126 | 6.8 µs | 14.1 µs |
| 50 messages: "lsmsgpack-indexed", names-only schema | 4,033 | 6.8 µs | 13.3 µs |
| 50 messages: "messagepack" (LsMsgPack) | 6,664 | 8.4 µs | 16.2 µs |
| 50 messages: Microsoft's MessagePack protocol | 6,664 | 15.4 µs | 32.8 µs |
| 50 messages: Microsoft's JSON protocol | 9,154 | 25.6 µs | 72.5 µs |

Writing allocates nothing; the values are read in place from the received bytes. Measured by `ProtocolBenchmark` in `LsMsgPack.SignalRTests` (`TestCategory=Benchmark`), November 2026.

| Node 22 | Bytes | Write | Read |
|---|---|---|---|
| `@lsmsgpack/client/signalr` | 147 | 1.21 µs | 1.70 µs |
| "lsmsgpack" as the server writes it | 156 | | 1.78 µs |
| "lsmsgpack-indexed" as the server writes it | 251 | | 2.37 µs |
| `@microsoft/signalr-protocol-msgpack` | 147 | 3.38 µs | 2.49 µs |
| JSON protocol | 254 | 2.45 µs | 1.56 µs |
| 50 messages: `@lsmsgpack/client/signalr` | 6,772 | 38 µs | 55 µs |
| 50 messages: "lsmsgpack" as the server writes it | 7,214 | | 60 µs |
| 50 messages: "lsmsgpack-indexed" as the server writes it | 4,130 | | 37 µs |
| 50 messages: `@microsoft/signalr-protocol-msgpack` | 6,764 | 90 µs | 72 µs |
| 50 messages: JSON protocol | 9,382 | 79 µs | 41 µs |

In JavaScript the JSON protocol reads large messages faster than plain MsgPack: `JSON.parse` is native code. With "lsmsgpack-indexed" the client reads the objects with code made once per schema and is faster than JSON for the list. The client writes plain values with every name, so its writes are the same for both names. Measured by `clients/typescript/bench/signalr.mjs` (the rows "as the server writes it" need the test server built).

## Compared with Microsoft's MessagePack protocol

- The same messages, fields, binding failures and error texts (`InvalidDataException` naming the field), protocol version 2 (stateful reconnect: Ack and Sequence messages), `RawResult` for the client results a server passes on.
- "messagepack" writes the same bytes as Microsoft's protocol with its default options (`LsMsgPack.SignalRTests` compares every message type and runs both clients against both servers).
- No MessagePack-CSharp dependency, and it's faster. Microsoft's protocol takes MessagePack-CSharp's resolvers and attributes (`[Key]`, `[Union]`): LsMsgPack doesn't read those, so classes written as arrays by `[Key]` don't fit.
