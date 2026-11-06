# LtMsgPack vs MessagePack-CSharp vs Nerdbank.MessagePack

All three serialize .NET classes to MsgPack. This page compares their features, speed and size. It's written by LtMsgPack's author, and the benchmarks are in this repository ([how to run them](#how-it-was-measured)). Open an issue if one of the other libraries can be set up faster than it is here.

> **Benchmark your own use case.** These numbers come from two data models on one machine. MessagePack-CSharp and Nerdbank.MessagePack will probably always have scenarios where they excel, and some of those aren't measured here. Before you choose, run a benchmark with your own data, on your own target platform.

## When to pick which

**LtMsgPack** when you want:

- **Polymorphism without attributes.** A `List<Shape>`, or a property declared as an interface or `object`, round trips out of the box through type ids. The other two need `[Union]`, or `[DerivedTypeShape]` / `DerivedTypeUnion`. Add a [type guard](security.md) for data you don't trust.
- **A schema in the payload.** The default [indexed schema](schema.md) lists property names once per message, so data from older or newer versions of your classes loads by name. With the other two you choose between arrays (the `[Key]` order is the contract) and maps (every name in every object).
- **A TypeScript client** for your ASP.NET backend: [@lsmsgpack/client](TypeScriptClient.md) with generated [models](TypeScriptModels.md).
- **Plain classes on a JIT:** no attributes, `partial` classes or source generator. The [generator](SourceGenerator.md) is optional there.
- Also: [ReadDifferences](ReadDifferences.md), [streaming](Streaming.md) with an index for seeking, [ADO.NET data readers](DataReader.md), and [presets](Compatibility.md#ltmsgpack-presets) for exchanging data with the other two.

**[MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)** when you want:

- **A long track record.** It's widely used in production, and ASP.NET Core SignalR's MessagePack protocol is built on it.
- **Unity support, LZ4 compression or typeless serialization.**
- **The smallest library on .NET 8 and later, and the smallest payloads with decimal values as text** (see the tables below).
- **Fast writes of small messages without LtMsgPack's generator:** 1.2-2x as fast as LtMsgPack's runtime plans in the game benchmark, depending on the setup.

**[Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack)** when you want:

- **NativeAOT and trimming as the main target**, with PolyType's shape model. It added the least to a NativeAOT app here, without trim or AOT warnings.
- **Async serialization** straight to and from pipes and streams.
- **Preserved object references, structural equality, or JSON schemas** for your types.

**Not LtMsgPack (yet) when:**

- **You need a track record.** It has no known production users, and breaking changes are still allowed. Pin the version and check the release notes before upgrading.
- **You target NativeAOT, WebAssembly AOT or iOS and don't want a source generator.** Without it, LtMsgPack fails under NativeAOT on the first object.
- **The other side uses another library's polymorphism format.** LtMsgPack can't read or write their unions. See [what settings cannot match](Compatibility.md#what-settings-cannot-match).
- **Your structs have public fields instead of properties**, like `System.Numerics.Vector3`. LtMsgPack refuses them with an exception; the other two write the fields.
- **You need Unity:** LtMsgPack isn't tested there.

**Whatever you pick,** LsMsgPack's debugging tools help you track down issues with any of these libraries: the standalone [MsgPack Explorer](../README.md#standalone-debugger-for-windows) for Windows, the [Fiddler inspector](../README.md#fiddler-integration), the [Visual Studio debugger visualizer](../README.md#visual-studio-integration), the [VS Code extension](../LsMsgPackVsCode/README.md) and the [MCP server](Mcp.md) for AI agents.

## Package size

What each library adds to an app, with its dependencies. Generated code isn't counted (except where noted), because it depends on your model.

| Target | LtMsgPack | MessagePack-CSharp | Nerdbank.MessagePack |
|---|---|---|---|
| .NET 10 | 504.8 KB (2 dlls) | **434.3 KB** (3) | 1,048.0 KB (3) |
| .NET 8 | 504.8 KB (2) | **433.8 KB** (3) | 1,129.0 KB (4) |
| .NET Framework 4.8 | **791.2 KB** (6) | 1,035.5 KB (10) | 2,529.8 KB (15) |
| NativeAOT, .NET 10, linux-x64: growth of the executable, including the generated code of one small class | 2.60 MB | 4.20 MB | **2.40 MB** |
| NuGet download, not deployed as a whole | **867.1 KB** | 1,342.8 KB | 3,017.1 KB |

- **Debug symbols:** on .NET 8 and later, LtMsgPack's dlls include 85.7 KB of embedded debug symbols and the other two libraries' none. Without those LtMsgPack would be about 419 KB.
- **.NET Framework:** much of the difference is framework packages such as `System.Text.Json` and `System.Collections.Immutable`, which your app may already deploy.
- **NativeAOT:** the executable only holds the code an app reaches, so a real app's numbers will differ. MessagePack-CSharp ran correctly but reported trim and AOT warnings at publish.
- **NuGet download:** the packages of the .NET 10 dependency tree, with all their target frameworks, analyzers and generators.

## Benchmark: invoices

100 invoices with strings, decimals, `DateTime`s, a `Guid`, enums, a nested customer and 3-24 lines each. The time is for 20 rounds of `Serialize` to a `byte[]` or `Deserialize` from one, with the speed relative to System.Text.Json.

| Setup | Size | Write | Read |
|---|---|---|---|
| System.Text.Json | 330.6 KB (100%) | 15.79 ms | 29.75 ms |
| **Arrays** | | | |
| MessagePack-CSharp, `[Key(n)]`, generated | 107.4 KB (32%) | 10.93 ms (1.4x) | 16.61 ms (1.8x) |
| MessagePack-CSharp, also native Guid and decimal | 200.6 KB (61%) | 7.80 ms (2.0x) | 13.80 ms (2.2x) |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | 200.6 KB (61%) | 5.05 ms (3.1x) | 14.15 ms (2.1x) |
| LtMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | 107.4 KB (32%) | 5.54 ms (2.9x) | 7.52 ms (4.0x) |
| LtMsgPack, no schema | 174.4 KB (53%) | 5.05 ms (3.1x) | 5.81 ms (5.1x) |
| the same, generated | | 2.71 ms (5.8x) | 4.85 ms (6.1x) |
| LtMsgPack, default options (indexed schema) | 214.7 KB (65%) | 5.69 ms (2.8x) | 5.99 ms (5.0x) |
| the same, generated | | 3.04 ms (5.2x) | 4.94 ms (6.0x) |
| **Maps** | | | |
| MessagePack-CSharp, `keyAsPropertyName`, generated | 278.9 KB (84%) | 12.30 ms (1.3x) | 21.01 ms (1.4x) |
| Nerdbank.MessagePack, defaults, generated shapes | 372.1 KB (113%) | 7.95 ms (2.0x) | 24.02 ms (1.2x) |
| LtMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | 278.9 KB (84%) | 7.81 ms (2.0x) | 9.61 ms (3.1x) |
| LtMsgPack, property names | 314.2 KB (95%) | 7.11 ms (2.2x) | 7.63 ms (3.9x) |
| the same, generated | | 5.16 ms (3.1x) | 6.59 ms (4.5x) |
| LtMsgPack, indexed schema | 226.5 KB (69%) | 6.95 ms (2.3x) | 6.78 ms (4.4x) |
| **Without attributes or generated code (maps)** | | | |
| MessagePack-CSharp, contractless | 278.9 KB (84%) | 11.51 ms (1.4x) | 20.59 ms (1.4x) |
| Nerdbank.MessagePack, reflection | 372.1 KB (113%) | 8.46 ms (1.9x) | 25.01 ms (1.2x) |

- **Size depends mostly on decimals and Guids.** MessagePack-CSharp's default writes them as text (`"123.45"` is 7 bytes, a binary decimal 18), which makes it the smallest here. LtMsgPack's own arrays are smaller than the native rows because it writes a zero decimal as nil. The indexed schema costs 12 percentage points on these arrays.
- **Writing:** Nerdbank.MessagePack matched LtMsgPack without generated code (5.05 ms, with a 15% larger payload) on arrays, and beat its default options and preset there. Generated LtMsgPack was the fastest.
- **Reading:** LtMsgPack was the fastest, with or without generated code: 2.4x as fast as the other two on arrays with binary decimals.
- **Same bytes:** LtMsgPack's preset wrote MessagePack-CSharp's arrays 2.0x and its maps 1.6x as fast, and read both 2.2x as fast.
- **Allocations** were about the same for all MsgPack setups: roughly the payload when writing, the objects (4.5 KB per invoice) when reading.

## Benchmark: game messages

A 16 player game: each tick a client sends its state (14 numbers, with two `Vec3` struct properties) and receives a snapshot of the 15 other players. The times are per message, for `Serialize` to a new `byte[]` and `Deserialize` from one. Schema reference rows share the schemas once when connecting (`SchemaStore`), and write 18 bytes per message instead of the inline schema.

| Setup | Size: state / snapshot | State: write / read | Snapshot: write / read |
|---|---|---|---|
| System.Text.Json | 190 B / 2,928 B | 1,310 / 1,438 ns | 18.6 / 21.0 µs |
| **Arrays** | | | |
| MessagePack-CSharp, `[Key(n)]`, generated | 51 B / 775 B | 117 / 188 ns | 1.41 / 2.36 µs |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | 51 B / 775 B | 139 / 225 ns | 2.05 / 3.03 µs |
| LtMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | 51 B / 775 B | 141 / 153 ns | 1.78 / 2.23 µs |
| LtMsgPack, no schema | 45 B / 690 B | 176 / 148 ns | 2.19 / 2.16 µs |
| the same, generated | | 107 / 126 ns | 1.16 / 1.76 µs |
| LtMsgPack, schema reference | 63 B / 708 B | 196 / 174 ns | 2.28 / 2.26 µs |
| the same, generated | | 126 / 149 ns | 1.29 / 1.86 µs |
| LtMsgPack, default options (indexed schema) | 131 B / 826 B | 234 / 173 ns | 2.51 / 2.26 µs |
| the same, generated | | 161 / 149 ns | 1.50 / 1.85 µs |
| **Maps** | | | |
| MessagePack-CSharp, `keyAsPropertyName`, generated | 123 B / 1,890 B | 158 / 368 ns | 1.84 / 5.08 µs |
| Nerdbank.MessagePack, defaults, generated shapes | 123 B / 1,890 B | 259 / 603 ns | 3.50 / 8.79 µs |
| LtMsgPack, property names | 110 B / 1,703 B | 259 / 253 ns | 3.46 / 3.69 µs |
| the same, generated | | 214 / 215 ns | 2.82 / 3.09 µs |

- **Arrays:** generated LtMsgPack was the fastest. Without generated code, MessagePack-CSharp wrote faster than every LtMsgPack setup (1.2x the preset, 1.5x no schema, up to 2x default options), while LtMsgPack still read faster.
- **Maps:** MessagePack-CSharp wrote the fastest, also against generated LtMsgPack. LtMsgPack read faster, also without generated code.
- **Leave the inline schema out of small messages.** It writes the names in every message: 131 B per state against 45 B. Use arrays without a schema, or a schema reference to keep reading by name across versions.
- **Size:** LtMsgPack's arrays are the smallest because a zero is written as nil. As a side effect, `-0.0` reads back as `0.0`. The other two keep the sign.
- **Allocations:** writing to a reused buffer allocated 0 B in every MsgPack setup (`ArrayBufferWriter<byte>` for the other two, `MemoryStream` for LtMsgPack). Reading allocated the same in all of them (64 B per state, 1,168 B per snapshot). With `Vec3` as a class, LtMsgPack allocated more when reading (120 B and 2,008 B) and took up to 1.3x as long.
- **JSON** took 8-13x as long as MessagePack-CSharp, with payloads 3.7x as large.

## How it was measured

- **Versions:** MessagePack-CSharp 3.1.12, Nerdbank.MessagePack 1.4.95 (PolyType 1.4.1), and LtMsgPack built from this repository, November 2026.
- **Setups:** each library's fastest documented setup that the test covers, with default (trusted data) security and no compression. The test checks every round trip, and that the `MessagePackCSharp()` preset writes MessagePack-CSharp's bytes.
- **Timing:** Intel i7-7700K, Linux, .NET 10.0.14, Release build. `Stopwatch` rather than BenchmarkDotNet, the fastest of 7 interleaved passes per run, and the tables have the medians of three runs. Between the runs most rows varied by less than 10%, a few by up to 24%. All setups share one process and its JIT profile, so running each library alone can give different numbers.
- **Not measured:** NativeAOT speed, the `IBufferWriter<byte>` and `Stream` overloads for invoices (the other two libraries are built around `IBufferWriter<byte>`), large binary data, deep nesting, polymorphic lists, `UntrustedData` security, and reading from a slice of a larger buffer.
- **Package size:** a minimal console app per library with one small class that writes and reads it, published framework-dependent in Release. The sizes are the dlls of the published folder, without the app's own. The NativeAOT row subtracts the size of an app without a serializer. The apps ran on .NET 8, .NET 10 and NativeAOT; the .NET Framework row counts the published files only.

To run the benchmarks:

```bash
dotnet test LsMsgPackInteropTests/LsMsgPackInteropTests.csproj -c Release --filter "FullyQualifiedName~BenchmarkLibraries|FullyQualifiedName~BenchmarkGame" --logger "console;verbosity=detailed"
```
