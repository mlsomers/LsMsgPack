# LsMsgPack.SignalR

A MessagePack hub protocol for ASP.NET Core SignalR on [LsMsgPack](https://www.nuget.org/packages/LsMsgPack), for the server and the .NET client.

- **"lsmsgpack"**: LsMsgPack's values (exact decimals, DateTimes with their kind, DateTimeOffset with its offset, type ids for class hierarchies), for the TypeScript client [`@lsmsgpack/client/signalr`](https://www.npmjs.com/package/@lsmsgpack/client) and .NET clients with this package.
- **"lsmsgpack-indexed"**: the same with the indexed schema in each value the server writes, so a list of objects has every name once (50 chat messages: 4,126 bytes instead of 7,214, read faster in .NET and JavaScript). Larger for a single small object.
- **"messagepack"**: the bytes of Microsoft's MessagePack protocol (`AddMessagePackProtocol()`), so the existing clients (JavaScript, .NET, Java, Swift) connect without a change. Use it instead of Microsoft's package.
- The envelope is Microsoft's MessagePack protocol, field by field, for every name. About twice as fast as Microsoft's MessagePack protocol, and the values of a message are read in place.

## Server

```csharp
builder.Services.AddSignalR()
  .AddLsMsgPackProtocol()                 // "lsmsgpack"
  .AddLsMsgPackIndexedProtocol()          // "lsmsgpack-indexed" (optional)
  .AddLsMsgPackAsMessagePackProtocol();   // "messagepack", for the clients of Microsoft's protocol (optional)
```

## .NET client

```csharp
HubConnection connection = new HubConnectionBuilder()
  .WithUrl("https://example.com/chat")
  .AddLsMsgPackProtocol()                 // or .AddLsMsgPackIndexedProtocol()
  .Build();
```

## TypeScript client

```ts
import { HubConnectionBuilder } from '@microsoft/signalr';
import { LsMsgPackHubProtocol } from '@lsmsgpack/client/signalr';

const connection = new HubConnectionBuilder().withUrl('/chat').withHubProtocol(new LsMsgPackHubProtocol()).build(); // { name: 'lsmsgpack-indexed' }
```

## Options

```csharp
.AddLsMsgPackProtocol(o =>
{
  o.SerializerOptions = LsMsgPackHubProtocolOptions.DefaultSerializerOptions(false); // then change what you need
  o.MaxObjects = 100000;                                                            // per value (untrusted data)
});
```

Never schema references: SignalR writes a message once for every connection it goes to, so "lsmsgpack-indexed" has the schema inline in each value (`[schema, body]`; the TypeScript client writes `[nil, value]`, both are read). Trimmed and NativeAOT apps pass the context of LsMsgPack's source generator, with the parameter and return types of the hub methods: `.AddLsMsgPackProtocol(AppMsgPackContext.Default)`.

Documentation: [docs/SignalR.md](https://github.com/mlsomers/LsMsgPack/blob/master/docs/SignalR.md).
