[Setup wizard](README.md) › [Sent](Network.md) › [My .NET programs](NetworkDotNet.md) › A queue, a socket

# Setup: .NET messaging (queues, sockets, streams)

Your .NET programs exchange messages through a broker (RabbitMQ, Azure Service Bus, Kafka, Redis pub/sub), a WebSocket or TCP connection, or a pipe, and you serialize the messages yourself.

## In short

| | |
|---|---|
| Package | `LtMsgPack` |
| Serializer | `LtMsgPackSerializer` with the default options, one per app |
| Format | the indexed schema in every message, type ids where needed |
| Keep in mind | messages outlive deployments; several message types can share one queue through an interface |

```
dotnet add package LtMsgPack
```

## The code

The serializer turns a message into a `byte[]`; every transport can carry that. With Azure Service Bus, for example:

```csharp
using LsMsgPack;
using LtMsgPack;

LtMsgPackSerializer serializer = new LtMsgPackSerializer(); // one for the whole app, thread-safe

// Producer
ServiceBusMessage message = new ServiceBusMessage(serializer.Serialize(orderPlaced))
{
  ContentType = MsgPackMediaTypes.XLsMsgPack // "application/x-lsmsgpack": tells tools and other consumers what the bytes are
};
await sender.SendMessageAsync(message);

// Consumer
processor.ProcessMessageAsync += async args =>
{
  OrderPlaced order = serializer.Deserialize<OrderPlaced>(args.Message.Body.ToArray());
  await Handle(order);
};
```

RabbitMQ (`body: serializer.Serialize(...)`), Kafka (a `byte[]` value serializer), a WebSocket (`SendAsync(bytes, WebSocketMessageType.Binary, ...)`) work the same way.

## Several message types on one queue

Declare an interface (or base class) for your messages, and serialize *as that interface*. The class name is then written with the message, and the consumer gets the right class back:

```csharp
public interface IShopEvent { Guid Id { get; set; } }
public class OrderPlaced : IShopEvent { public Guid Id { get; set; } public int OrderId { get; set; } }
public class OrderCancelled : IShopEvent { public Guid Id { get; set; } public string Reason { get; set; } }

byte[] body = serializer.Serialize<IShopEvent>(new OrderCancelled { ... }); // <IShopEvent>: written with its class name

IShopEvent received = serializer.Deserialize<IShopEvent>(body);
switch (received)
{
  case OrderPlaced placed: ...; break;
  case OrderCancelled cancelled: ...; break;
}
```

The class name (`OrderCancelled`) becomes part of your message contract: renaming the class breaks messages that are still in the queue and consumers that weren't updated. Give the classes stable names with `[XmlRoot("OrderCancelled")]` and the `XmlRootAttributeTypeResolver`, see [documents](Setup-Documents.md#changing-your-classes-later).

## Why these settings

- **Every message carries its own schema** (the property names of its classes, once). A consumer that starts tomorrow, or a new version of it, reads a message without knowing anything about the producer's version. LtMsgPack recognizes a schema it has seen before by its bytes, so reading the same schema again costs almost nothing.
- **Values are matched by name**, through that schema: a producer that already has a new property and a consumer that doesn't yet (or the other way around) still understand each other.
- **No schema references.** `WriteSchemaReference` replaces the schema by an 18-byte reference to a schema held in memory. That's right for HTTP responses, where the client says which schemas it has; a message in a queue may be read by a process that never saw the schema, so keep the schema in the message.

## Changing your classes later

Messages wait in queues, and services are deployed one at a time, so old and new versions run side by side:

- **Safe**: adding a property (an old consumer skips it, a new consumer reading an old message gets the constructor's value), removing one, reordering.
- **Not safe**: renaming a property or a message class, changing a property to an incompatible type. Add the new one, wait until no producer writes the old one, then remove it.
- **Find out what didn't match**: `serializer.Deserialize<OrderPlaced>(body, out ReadDifferences differences)` lists the properties a message had that the class doesn't. Log it while you roll out a change.

## Watch out

- **Messages that can't be read** (a corrupt body, an incompatible change) throw a `MsgPackException`. Catch it and move the message to a dead-letter queue rather than retrying it forever.
- **Who can write to the queue?** If only your services can, the messages are trusted. If other parties or clients can publish (a public WebSocket, a shared broker), treat them as untrusted: see [Add-ons](Extras.md#reading-data-you-dont-trust).
- **Very small, very frequent messages**: the schema is a fixed cost per message. For a message that is one small object, `new LtMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }` (the property names in the object, no schema) is about as small and just as tolerant to changes; set the same on both sides.
- **Dates arrive in UTC** (`Kind = Utc`).
- Consumers in other languages on the same queue? Then use the [plain format](Setup-FilesPlain.md) (`LtMsgPackPresets.Generic()`); the settings there apply to messages just as well.

## Next

- Speed: the source generator writes the serialization code at compile time, [Add-ons](Extras.md#trimmed-and-aot-apps-and-more-speed).
- What's inside a message: [Add-ons](Extras.md#looking-inside-the-bytes) (MsgPack Explorer, VS Code, the MCP server for AI agents).
