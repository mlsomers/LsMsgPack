# LtMsgPack

Fast MsgPack serializer for .NET classes, used like the xml and json serializers. It writes and reads your objects directly, without an intermediate item tree. On the invoices of the benchmark it writes about 3-4x and reads about 4x as fast as System.Text.Json, with 35-47% smaller payloads.

- **Indexed schema** (on by default): each message lists its types and property names once, and the body refers to them by index. Messages are smaller, and data written by older or newer versions of your classes still loads.
- **Polymorphism** out of the box: a type id is written where a value's type differs from the declared type (a `List<IShape>` holding circles and squares), without extra attributes.
- **Presets** for exchanging data with MessagePack-CSharp, Nerdbank.MessagePack and non-.NET libraries (Python, JavaScript...).
- Writes and reads exactly the same data as the [LsMsgPack](https://www.nuget.org/packages/LsMsgPack) serializer with the same settings.
- Targets .NET Standard 2.0 and 2.1: .NET Framework 4.6.2+, .NET, MAUI, Avalonia...

## Usage

```csharp
using LtMsgPack;

// Create one serializer per set of options and keep it: it's thread-safe and builds its plans per type once
LtMsgPackSerializer serializer = new LtMsgPackSerializer(); // or LtMsgPackSerializer.Default

byte[] data = serializer.Serialize(order);
Order copy = serializer.Deserialize<Order>(data);
```

There are also overloads that write to and read from a `Stream`.

## Settings

Pass an `LtMsgPackOptions` to the constructor (the options are copied):

```csharp
using LsMsgPack;

LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions
{
  UseInexedSchema = false,
  ObjectLayout = ObjectLayout.Map
});
```

To exchange data with other MsgPack libraries, start from a preset:

```csharp
LtMsgPackSerializer serializer = new LtMsgPackSerializer(LtMsgPackPresets.MessagePackCSharp()); // or Nerdbank(), Generic()
```

## Reading data you don't trust

Type ids let the data pick the classes that are created. The serializer only creates types assignable to the declared type. To restrict them further, set a type guard:

```csharp
using LsMsgPack.TypeResolving.Types;

LtMsgPackOptions options = new LtMsgPackOptions();
options.TypeGuard = new AllowedTypesGuard().AllowAssemblyOf(typeof(IShape));
```

## Documentation

- [Client applications](https://github.com/mlsomers/LsMsgPack/blob/master/docs/ClientApps.md): files, web services, trimming
- [Wire formats, the indexed schema and polymorphism](https://github.com/mlsomers/LsMsgPack/blob/master/docs/schema.md)
- [Compatibility with other MsgPack libraries](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Compatibility.md)
- [Security](https://github.com/mlsomers/LsMsgPack/blob/master/docs/security.md)
- [Reporting differences](https://github.com/mlsomers/LsMsgPack/blob/master/docs/ReadDifferences.md): what didn't match between the data and your classes (`serializer.Deserialize<T>(data, out ReadDifferences differences)`)
- ASP.NET formatters built on LtMsgPack: [LsMsgPack.AspNetCore](https://www.nuget.org/packages/LsMsgPack.AspNetCore), [LsMsgPack.AspNet.WebApi](https://www.nuget.org/packages/LsMsgPack.AspNet.WebApi), [LsMsgPack.AspNet.Mvc](https://www.nuget.org/packages/LsMsgPack.AspNet.Mvc)
- Source, issues and MsgPack Explorer (a tool to look inside MsgPack data): [github.com/mlsomers/LsMsgPack](https://github.com/mlsomers/LsMsgPack)
