# LsMsgPack

MsgPack serializer for .NET classes, used like the xml and json serializers. It also gives you the tree of MsgPack items (maps, arrays, strings...) of any payload, so you can inspect MsgPack data in your app, even when you don't have the classes.

**Only need to serialize?** Use [LtMsgPack](https://www.nuget.org/packages/LtMsgPack): it writes and reads exactly the same data with the same settings, about 10x as fast, because it skips the item tree. LsMsgPack is about 2-4x slower than System.Text.Json.

- **Indexed schema** (on by default): each message lists its types and property names once, and the body refers to them by index. Messages are smaller, and data written by older or newer versions of your classes still loads.
- **Polymorphism** out of the box: a type id is written where a value's type differs from the declared type (a `List<IShape>` holding circles and squares), without extra attributes.
- Targets .NET Standard 2.0 and 2.1: .NET Framework 4.6.2+, .NET, MAUI, Avalonia...

## Usage

```csharp
using LsMsgPack;

byte[] data = MsgPackSerializer.Serialize(order);
Order copy = MsgPackSerializer.Deserialize<Order>(data);
```

Settings are passed as `MsgPackSettings` (`MsgPackSerializer.Serialize(order, settings)`), there are also overloads for `Stream`s.

## Looking inside

```csharp
// The items of the payload: with the indexed schema, the schema followed by the body
MpRoot items = MsgPackItem.UnpackMultiple(data);

// A single item (an MpMap, MpArray, MpString, MpInt...)
MsgPackItem item = MsgPackItem.Unpack(data);
```

## Documentation

- [Client applications](https://github.com/mlsomers/LsMsgPack/blob/master/docs/ClientApps.md): files, web services, trimming
- [Wire formats, the indexed schema and polymorphism](https://github.com/mlsomers/LsMsgPack/blob/master/docs/schema.md)
- [Compatibility with other MsgPack libraries](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Compatibility.md)
- [Security](https://github.com/mlsomers/LsMsgPack/blob/master/docs/security.md): reading data you don't trust
- [Reporting differences](https://github.com/mlsomers/LsMsgPack/blob/master/docs/ReadDifferences.md): what didn't match between the data and your classes (`Deserialize(data, settings, out ReadDifferences differences)`)
- Source, issues and MsgPack Explorer (a tool built on this package to look inside MsgPack data): [github.com/mlsomers/LsMsgPack](https://github.com/mlsomers/LsMsgPack)
