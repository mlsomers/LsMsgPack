# LsMsgPack

LsMsgPack is the MessagePack (MsgPack) serializer to use: a fast serializer for .NET classes, used like the xml and json serializers. It writes and reads your objects directly, without an intermediate item tree. On the invoices of the benchmark it writes and reads about 4.5-5.5x as fast as System.Text.Json, with 35-47% smaller payloads.

To look inside MsgPack data (an item tree, validation), use [LsMsgPackDebug](https://www.nuget.org/packages/LsMsgPackDebug) instead.

- **Indexed schema** (on by default): each message lists its types and property names once, and the body refers to them by index. Messages are smaller, and data written by older or newer versions of your classes still loads.
- **Polymorphism** out of the box: a type id is written where a value's type differs from the declared type (a `List<IShape>` holding circles and squares), without extra attributes.
- **Fields** are serialized when they opt in (`[DataMember]`, `[JsonInclude]`...), or all public ones with a setting, after the properties: [schema.md](https://github.com/mlsomers/LsMsgPack/blob/master/docs/schema.md#fields).
- **Presets** for exchanging data with MessagePack-CSharp, Nerdbank.MessagePack and non-.NET libraries (Python, JavaScript...).
- Writes and reads exactly the same data as the [LsMsgPackDebug](https://www.nuget.org/packages/LsMsgPackDebug) serializer with the same settings.
- Targets .NET Standard 2.0 and 2.1: .NET Framework 4.6.2+, .NET, MAUI, Avalonia...

## Usage

```csharp
using LsMsgPack;

// Create one serializer per set of options and keep it: it's thread-safe and builds its plans per type once
LsMsgPackSerializer serializer = new LsMsgPackSerializer(); // or LsMsgPackSerializer.Default

byte[] data = serializer.Serialize(order);
Order copy = serializer.Deserialize<Order>(data);
```

There are also overloads that write to and read from a `Stream`.

To write and read many values one after another (database rows, several result sets or tables in one file), including arrays and maps whose length isn't known when they start and tables of objects with their schema written once, use `MsgPackStreamWriter` and `MsgPackStreamReader` (namespace `LsMsgPack.Streaming`), synchronously or asynchronously (`ReadElementsAsync<T>()` for the rows of a table as they arrive). See [streaming](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Streaming.md). For ADO.NET data readers (result sets and table dumps without a class, dumps read back as a `DbDataReader`) there is the add-on [LsMsgPack.Data](https://www.nuget.org/packages/LsMsgPack.Data).

## Settings

Pass an `LsMsgPackOptions` to the constructor (the options are copied):

```csharp
using LsMsgPack;

LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions
{
  UseInexedSchema = false,
  ObjectLayout = ObjectLayout.Map
});
```

To exchange data with other MsgPack libraries, start from a preset:

```csharp
LsMsgPackSerializer serializer = new LsMsgPackSerializer(LsMsgPackPresets.MessagePackCSharp()); // or Nerdbank(), Generic()
```

## Source generator (NativeAOT, Blazor, trimming)

The package includes a source generator that writes the plans of your classes at compile time: it works where code can't be generated at runtime (NativeAOT, WebAssembly AOT, iOS), keeps your classes when trimming, and is faster (on WebAssembly AOT 7-8x writing, 3x reading). The data stays the same.

```csharp
[LsMsgPackSerializable(typeof(Order))]
public partial class AppMsgPackContext : LsMsgPackContext { }
```

Create the serializer with it: `new LsMsgPackSerializer(AppMsgPackContext.Default, options)`. For trimmed and NativeAOT apps that's the constructor without trim or AOT warnings: the ones without a context are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. With C# 9 or later the context is also registered when the assembly loads, so serializers created without it use it too. To use the classes you already marked, without a context class: `<LsMsgPackGenerate>Serializable;DataContract;JsonSerializable</LsMsgPackGenerate>` in the project file. See [the source generator](https://github.com/mlsomers/LsMsgPack/blob/master/docs/SourceGenerator.md).

## Reading data you don't trust

Type ids let the data pick the classes that are created. The serializer only creates types assignable to the declared type. To restrict them further, set a type guard:

```csharp
using LsMsgPack.TypeResolving.Types;

LsMsgPackOptions options = new LsMsgPackOptions();
options.TypeGuard = new AllowedTypesGuard().AllowAssemblyOf(typeof(IShape));
```

## Documentation

- [Client applications](https://github.com/mlsomers/LsMsgPack/blob/master/docs/ClientApps.md): files, web services, trimming
- [The source generator](https://github.com/mlsomers/LsMsgPack/blob/master/docs/SourceGenerator.md): NativeAOT, WebAssembly AOT, trimming, speed
- [Wire formats, the indexed schema and polymorphism](https://github.com/mlsomers/LsMsgPack/blob/master/docs/schema.md)
- [Compatibility with other MsgPack libraries](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Compatibility.md)
- [LsMsgPack vs MessagePack-CSharp vs Nerdbank.MessagePack](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Comparison.md): benchmarks, and when to pick which
- [Security](https://github.com/mlsomers/LsMsgPack/blob/master/docs/security.md)
- [Streaming](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Streaming.md): `MsgPackStreamWriter` and `MsgPackStreamReader`, values one after another, arrays and maps of unknown length, an index at the end of the stream to go to any table or row
- [Reporting differences](https://github.com/mlsomers/LsMsgPack/blob/master/docs/ReadDifferences.md): what didn't match between the data and your classes (`serializer.Deserialize<T>(data, out ReadDifferences differences)`)
- ASP.NET formatters built on LsMsgPack: [LsMsgPack.AspNetCore](https://www.nuget.org/packages/LsMsgPack.AspNetCore), [LsMsgPack.AspNet.WebApi](https://www.nuget.org/packages/LsMsgPack.AspNet.WebApi), [LsMsgPack.AspNet.Mvc](https://www.nuget.org/packages/LsMsgPack.AspNet.Mvc)
- Source, issues and MsgPack Explorer (a tool to look inside MsgPack data): [github.com/mlsomers/LsMsgPack](https://github.com/mlsomers/LsMsgPack)
