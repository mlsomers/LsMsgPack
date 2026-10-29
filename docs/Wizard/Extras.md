[Setup wizard](README.md) › Add-ons

# Add-ons for every setup

These work with any of the setups. Add them when you need them.

- [Trimmed and AOT apps, and more speed](#trimmed-and-aot-apps-and-more-speed): the source generator
- [Warnings while you type](#warnings-while-you-type): the analyzers
- [Finding out what didn't match](#finding-out-what-didnt-match): `ReadDifferences`
- [Reading data you don't trust](#reading-data-you-dont-trust): declared types and the type guard
- [Looking inside the bytes](#looking-inside-the-bytes): MsgPack Explorer, Visual Studio, VS Code, AI agents

## Trimmed and AOT apps, and more speed

LtMsgPack normally learns your classes at runtime, with reflection. Some platforms don't allow that, or remove the code it would need:

- **NativeAOT** (`PublishAot`), **WebAssembly AOT** (Blazor with `RunAOTCompilation`), **iOS**: code can't be generated at runtime.
- **Trimmed apps** (Android, iOS, `PublishTrimmed`): the trimmer removes properties nobody seems to use, including the ones only the serializer reads.

The source generator, which comes with the `LtMsgPack` package, writes that code at compile time instead. Name the classes you serialize (what they reach is included):

```csharp
using LtMsgPack;

[LtMsgPackSerializable(typeof(Drawing))]
[LtMsgPackSerializable(typeof(Circle))] // classes only met through an interface or base class: list them too
[LtMsgPackSerializable(typeof(Square))]
public partial class AppMsgPackContext : LtMsgPackContext { }

LtMsgPackSerializer serializer = new LtMsgPackSerializer(AppMsgPackContext.Default, new LtMsgPackOptions());
```

The web formatters take it too: `AddLsMsgPackSerializerFormatters(AppMsgPackContext.Default, o => ...)`, `AddLsMsgPack(AppMsgPackContext.Default)`, and on the client `new LsMsgPackMediaTypeFormatter(new LtMsgPackHttpSerializer(AppMsgPackContext.Default, options))`.

Even without AOT it's faster: about 1.6x for writing and 1.15x for reading, and several times faster under WebAssembly. The bytes are exactly the same with or without it, so you can switch it on at any time. More: [SourceGenerator.md](../SourceGenerator.md).

## Warnings while you type

The packages include analyzers that check the classes you serialize, in Visual Studio, VS Code and Rider and in the build. Nothing to install. For example:

- a property with an initial value that could be read back as another value, with a fix that adds the right `[DefaultValue]`;
- a property declared as `object` or a broad interface, where the data decides which class is created (see below);
- classes of another project that no analyzer checked.

A project with only model classes can ask for all of them to be checked with `<LsMsgPackAnalyze>AllTypes</LsMsgPackAnalyze>`. More: [Analyzers.md](../Analyzers.md).

## Finding out what didn't match

The serializers read data that doesn't match your classes exactly, as the JSON serializers do: an unknown property is skipped, a missing one keeps its initial value. That's what makes version upgrades work, but it also hides typos. Ask what happened:

```csharp
Order order = serializer.Deserialize<Order>(data, out ReadDifferences differences);
if (differences != null)
  logger.LogWarning(differences.GenerateReport());
```

```
1 difference between the data and the classes:
- Order.ShippingAdress: not a property of the class, skipped, 1 time
    at $
    did you mean ShippingAddress? (left at its default)
```

The web formatters do this with `o.ReportDifferences = true` and write it to the log. A value that can't be converted (text where a number is expected) throws by default; with `ReadErrors = ReadErrorHandling.ReportAndContinue` it's skipped and reported instead. More: [ReadDifferences.md](../ReadDifferences.md).

## Reading data you don't trust

*Untrusted* is any data someone else could have written: request bodies of a web API (anyone can send a request, not only your app), files users exchange, queues others can publish to.

The default format can carry class names, so that a `List<IShape>` comes back with circles and squares. That means the data chooses which classes are created, and creating a class runs its constructor and setters. The serializers always protect you against the dangerous part: a class must fit the declared type (only an `IShape` where an `IShape` is declared), names can't load assemblies, only public framework types are found, nesting and lengths are limited. What's left is up to your classes:

1. **Declare what you expect.** `List<IShape>` only accepts shapes. `object`, `List<object>`, `Dictionary<string, object>` accept *any* class the serializer can find. Avoid them in data from outside; the analyzers point them out.
2. **Add a type guard** when you can't avoid them, or to be safe in a web API. It lists which classes the data may pick:

```csharp
using LsMsgPack.TypeResolving.Types;

AllowedTypesGuard guard = new AllowedTypesGuard().AllowAssemblyOf(typeof(Order)); // the classes of your models' assembly
LtMsgPackOptions options = new LtMsgPackOptions { TypeGuard = guard };

// the web formatters:
builder.Services.AddControllers().AddLsMsgPackSerializerFormatters(o =>
{
  o.XLsMsgPack.TypeGuard = guard;
  o.Plain.TypeGuard = guard;
});
```

3. **Validate** what your setters accept, or the object after reading it, as you would for JSON.

Data that only your own code writes and reads (your files, your cache, your internal queue) doesn't need a guard. The whole story: [security.md](../security.md).

## Looking inside the bytes

MsgPack is binary, so you can't read it in a text editor. These tools show it as a tree with the property names, types, offsets and any errors:

| Tool | Where | How |
|---|---|---|
| MsgPack Explorer | Windows | Download it from the [releases](https://github.com/mlsomers/LsMsgPack/releases), open a file or paste from the clipboard |
| Visual Studio visualizer | Visual Studio (Windows) | Install from the [marketplace](https://marketplace.visualstudio.com/items?itemName=mlsomers.V2025102900); while debugging, click the magnifier next to a `byte[]` or `Stream` |
| Fiddler inspector | Windows | Inspect MsgPack HTTP traffic, see the [README](../../README.md#fiddler-integration) |
| VS Code extension | any OS | [LsMsgPackVsCode](../../LsMsgPackVsCode/README.md): **View as MsgPack** on a variable while debugging, or open a `.msgpack` file |
| MCP server | AI agents (Claude Code, VS Code chat...) | `dotnet tool install -g LsMsgPack.Mcp`, then add `lsmsgpack-mcp` to your agent, see [Mcp.md](../Mcp.md) |

They also validate the data: for example, they flag places where the data picks a class for a property declared as `object`.

In your own code, the `LsMsgPack` package gives you the same tree: `MsgPackItem.Unpack(bytes)` returns the maps, arrays and values of any MsgPack data, also without classes. It writes and reads the same data as LtMsgPack, only slower, so use it for inspecting, not as your serializer.
