# LtMsgPack vs MessagePack-CSharp vs Nerdbank.MessagePack

All three serialize .NET classes to MsgPack. This page compares their features, speed and size. It's written by LtMsgPack's author, and the benchmarks are in this repository ([how to run them](#how-it-was-measured)). Open an issue if one of the other libraries can be set up faster than it is here.

> **Benchmark your own use case.** These numbers come from two data models on one machine. MessagePack-CSharp and Nerdbank.MessagePack will probably always have scenarios where they excel, and some of those aren't measured here. Before you choose, run a benchmark with your own data, on your own target platform.

## When to pick which

**LtMsgPack** when you want:

- **Polymorphism without attributes.** A `List<Shape>`, or a property declared as an interface or `object`, round trips out of the box through type ids. The other two need `[Union]`, or `[DerivedTypeShape]` / `DerivedTypeUnion`. Add a [type guard](security.md) for data you don't trust.
- **A schema in the payload.** The default [indexed schema](schema.md) lists property names once per message, so data from older or newer versions of your classes loads by name. With the other two you choose between arrays (the `[Key]` order is the contract) and maps (every name in every object).
- **A TypeScript client** for your ASP.NET backend: [@lsmsgpack/client](TypeScriptClient.md) with generated [models](TypeScriptModels.md).
- **Plain classes on a JIT:** no attributes, `partial` classes or source generator. The [generator](SourceGenerator.md) is optional there: LtMsgPack compiles the same writers at runtime, so it writes as fast without it, and the generator mainly speeds up reading (about 1.2x).
- Also: [ReadDifferences](ReadDifferences.md), [streaming](Streaming.md) with an index for seeking, [ADO.NET data readers](DataReader.md), and [presets](Compatibility.md#ltmsgpack-presets) for exchanging data with the other two.

**[MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)** when you want:

- **A long track record.** It's widely used in production, and ASP.NET Core SignalR's MessagePack protocol is built on it.
- **Unity support, LZ4 compression or typeless serialization.**
- **The smallest library on .NET 8 and later, and the smallest payloads with decimal values as text** (see the tables below).
- **Fast writes of small messages as maps:** 1.3-1.6x as fast as LtMsgPack in the game benchmark, also against its generated code.

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

In the tables, each column runs from green (the best value, also in bold) to red (the worst); in the package size table each row does. System.Text.Json is the baseline and isn't colored.

## Package size

What each library adds to an app, with its dependencies. Generated code isn't counted (except where noted), because it depends on your model.

| Target | LtMsgPack | MessagePack-CSharp | Nerdbank.MessagePack |
|---|---|---|---|
| .NET 10 | $\color{#118500}\text{504.8 KB (2 dlls)}$ | $\color{#009600}\textbf{434.3 KB (3)}$ | $\color{#960000}\text{1,048.0 KB (3)}$ |
| .NET 8 | $\color{#0F8700}\text{504.8 KB (2)}$ | $\color{#009600}\textbf{433.8 KB (3)}$ | $\color{#960000}\text{1,129.0 KB (4)}$ |
| .NET Framework 4.8 | $\color{#009600}\textbf{791.2 KB (6)}$ | $\color{#158100}\text{1,035.5 KB (10)}$ | $\color{#960000}\text{2,529.8 KB (15)}$ |
| NativeAOT, .NET 10, linux-x64: growth of the executable, including the generated code of one small class | $\color{#118500}\text{2.60 MB}$ | $\color{#960000}\text{4.20 MB}$ | $\color{#009600}\textbf{2.40 MB}$ |
| NuGet download, not deployed as a whole | $\color{#009600}\textbf{867.1 KB}$ | $\color{#217500}\text{1,342.8 KB}$ | $\color{#960000}\text{3,017.1 KB}$ |

- **Debug symbols:** on .NET 8 and later, LtMsgPack's dlls include 85.7 KB of embedded debug symbols and the other two libraries' none. Without those LtMsgPack would be about 419 KB.
- **.NET Framework:** much of the difference is framework packages such as `System.Text.Json` and `System.Collections.Immutable`, which your app may already deploy.
- **NativeAOT:** the executable only holds the code an app reaches, so a real app's numbers will differ. MessagePack-CSharp ran correctly but reported trim and AOT warnings at publish.
- **NuGet download:** the packages of the .NET 10 dependency tree, with all their target frameworks, analyzers and generators.

## Benchmark: invoices

100 invoices with strings, decimals, `DateTime`s, a `Guid`, enums, a nested customer and 3-24 lines each. The time is for 20 rounds of `Serialize` to a `byte[]` or `Deserialize` from one, with the speed relative to System.Text.Json.

| Setup | Size | Write | Read |
|---|---|---|---|
| System.Text.Json | 330.6 KB (100%) | 16.44 ms | 28.30 ms |
| **Arrays** |  |  |  |
| MessagePack-CSharp, `[Key(n)]`, generated | $\color{#009600}\textbf{107.4 KB (32\%)}$ | $\color{#811500}\text{11.28 ms (1.5x)}$ | $\color{#573F00}\text{16.72 ms (1.7x)}$ |
| MessagePack-CSharp, also native Guid and decimal | $\color{#356100}\text{200.6 KB (61\%)}$ | $\color{#494D00}\text{7.64 ms (2.2x)}$ | $\color{#415500}\text{13.74 ms (2.1x)}$ |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | $\color{#356100}\text{200.6 KB (61\%)}$ | $\color{#306600}\text{6.01 ms (2.7x)}$ | $\color{#4C4A00}\text{15.13 ms (1.9x)}$ |
| LtMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#009600}\textbf{107.4 KB (32\%)}$ | $\color{#1F7700}\text{4.89 ms (3.4x)}$ | $\color{#148200}\text{7.69 ms (3.7x)}$ |
| LtMsgPack, no schema | $\color{#267000}\text{174.4 KB (53\%)}$ | $\color{#009600}\textbf{2.87 ms (5.7x)}$ | $\color{#088E00}\text{5.96 ms (4.7x)}$ |
| the same, generated |  | $\color{#009600}\text{2.90 ms (5.7x)}$ | $\color{#009600}\textbf{4.94 ms (5.7x)}$ |
| LtMsgPack, default options (indexed schema) | $\color{#3D5900}\text{214.7 KB (65\%)}$ | $\color{#0A8C00}\text{3.55 ms (4.6x)}$ | $\color{#088E00}\text{6.07 ms (4.7x)}$ |
| the same, generated |  | $\color{#0B8B00}\text{3.58 ms (4.6x)}$ | $\color{#019500}\text{5.07 ms (5.6x)}$ |
| **Maps** |  |  |  |
| MessagePack-CSharp, `keyAsPropertyName`, generated | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#960000}\text{12.63 ms (1.3x)}$ | $\color{#791D00}\text{21.18 ms (1.3x)}$ |
| Nerdbank.MessagePack, defaults, generated shapes | $\color{#960000}\text{372.1 KB (113\%)}$ | $\color{#554100}\text{8.41 ms (2.0x)}$ | $\color{#8D0900}\text{23.99 ms (1.2x)}$ |
| LtMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#425400}\text{7.16 ms (2.3x)}$ | $\color{#267000}\text{10.12 ms (2.8x)}$ |
| LtMsgPack, property names | $\color{#752100}\text{314.2 KB (95\%)}$ | $\color{#227400}\text{5.11 ms (3.2x)}$ | $\color{#187E00}\text{8.22 ms (3.4x)}$ |
| the same, generated |  | $\color{#276F00}\text{5.44 ms (3.0x)}$ | $\color{#0D8900}\text{6.72 ms (4.2x)}$ |
| LtMsgPack, indexed schema | $\color{#435300}\text{226.5 KB (69\%)}$ | $\color{#1E7800}\text{4.84 ms (3.4x)}$ | $\color{#118500}\text{7.28 ms (3.9x)}$ |
| **Without attributes or generated code (maps)** |  |  |  |
| MessagePack-CSharp, contractless | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#831300}\text{11.38 ms (1.4x)}$ | $\color{#752100}\text{20.68 ms (1.4x)}$ |
| Nerdbank.MessagePack, reflection | $\color{#960000}\text{372.1 KB (113\%)}$ | $\color{#5F3700}\text{9.08 ms (1.8x)}$ | $\color{#960000}\text{25.14 ms (1.1x)}$ |

- **Size depends mostly on decimals and Guids.** MessagePack-CSharp's default writes them as text (`"123.45"` is 7 bytes, a binary decimal 18), which makes it the smallest here. LtMsgPack's own arrays are smaller than the native rows because it writes a zero decimal as nil. The indexed schema costs 12 percentage points on these arrays.
- **Writing:** LtMsgPack was the fastest in every layout, with or without generated code: on arrays 2.1x as fast as Nerdbank.MessagePack (2.87 ms against 6.01 ms, with a 13% smaller payload). LtMsgPack compiles its writers at runtime (after 1000 objects of a class, see [the source generator](SourceGenerator.md)), so generated code writes no faster.
- **Reading:** LtMsgPack was the fastest, with or without generated code: 2.3-2.5x as fast as the other two on arrays with binary decimals. Generated code reads about 1.2x as fast as LtMsgPack without it.
- **Same bytes:** LtMsgPack's preset wrote MessagePack-CSharp's arrays 2.3x and its maps 1.8x as fast, and read both 2.1-2.2x as fast.
- **Allocations** were about the same for all MsgPack setups: roughly the payload when writing, the objects (4.5 KB per invoice) when reading.

## Benchmark: game messages

A 16 player game: each tick a client sends its state (14 numbers, with two `Vec3` struct properties) and receives a snapshot of the 15 other players. The times are per message, for `Serialize` to a new `byte[]` and `Deserialize` from one. Schema reference rows share the schemas once when connecting (`SchemaStore`), and write 18 bytes per message instead of the inline schema.

| Setup | Size: state / snapshot | State: write / read | Snapshot: write / read |
|---|---|---|---|
| System.Text.Json | 190 B / 2,928 B | 1,292 / 1,444 ns | 18.4 / 21.3 µs |
| **Arrays** |  |  |  |
| MessagePack-CSharp, `[Key(n)]`, generated | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#158100}\text{116}$ / $\color{#148200}\text{189 ns}$ | $\color{#197D00}\text{1.41}$ / $\color{#0D8900}\text{2.36 µs}$ |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#395D00}\text{157}$ / $\color{#1F7700}\text{224 ns}$ | $\color{#316500}\text{1.80}$ / $\color{#1C7A00}\text{3.04 µs}$ |
| LtMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#009600}\textbf{92}$ / $\color{#078F00}\text{150 ns}$ | $\color{#009600}\textbf{0.99}$ / $\color{#0A8C00}\text{2.25 µs}$ |
| LtMsgPack, no schema | $\color{#009600}\textbf{45 B}$ / $\color{#009600}\textbf{690 B}$ | $\color{#0F8700}\text{109}$ / $\color{#078F00}\text{150 ns}$ | $\color{#108600}\text{1.26}$ / $\color{#098D00}\text{2.17 µs}$ |
| the same, generated |  | $\color{#0F8700}\text{109}$ / $\color{#009600}\textbf{127 ns}$ | $\color{#118500}\text{1.27}$ / $\color{#009600}\textbf{1.77 µs}$ |
| LtMsgPack, schema reference | $\color{#1F7700}\text{63 B}$ / $\color{#029400}\text{708 B}$ | $\color{#227400}\text{131}$ / $\color{#0D8900}\text{169 ns}$ | $\color{#148200}\text{1.32}$ / $\color{#0B8B00}\text{2.27 µs}$ |
| the same, generated |  | $\color{#217500}\text{129}$ / $\color{#088E00}\text{151 ns}$ | $\color{#148200}\text{1.32}$ / $\color{#029400}\text{1.87 µs}$ |
| LtMsgPack, default options (indexed schema) | $\color{#960000}\text{131 B}$ / $\color{#118500}\text{826 B}$ | $\color{#405600}\text{165}$ / $\color{#0E8800}\text{172 ns}$ | $\color{#217500}\text{1.54}$ / $\color{#0B8B00}\text{2.28 µs}$ |
| the same, generated |  | $\color{#3F5700}\text{163}$ / $\color{#088E00}\text{152 ns}$ | $\color{#217500}\text{1.54}$ / $\color{#029400}\text{1.88 µs}$ |
| **Maps** |  |  |  |
| MessagePack-CSharp, `keyAsPropertyName`, generated | $\color{#880E00}\text{123 B}$ / $\color{#960000}\text{1,890 B}$ | $\color{#375F00}\text{154}$ / $\color{#4C4A00}\text{365 ns}$ | $\color{#326400}\text{1.82}$ / $\color{#484E00}\text{5.08 µs}$ |
| Nerdbank.MessagePack, defaults, generated shapes | $\color{#880E00}\text{123 B}$ / $\color{#960000}\text{1,890 B}$ | $\color{#960000}\text{262}$ / $\color{#960000}\text{597 ns}$ | $\color{#960000}\text{3.49}$ / $\color{#960000}\text{8.66 µs}$ |
| LtMsgPack, property names | $\color{#712500}\text{110 B}$ / $\color{#7F1700}\text{1,703 B}$ | $\color{#633300}\text{204}$ / $\color{#267000}\text{247 ns}$ | $\color{#5F3700}\text{2.58}$ / $\color{#286E00}\text{3.60 µs}$ |
| the same, generated |  | $\color{#6B2B00}\text{213}$ / $\color{#1C7A00}\text{216 ns}$ | $\color{#6F2700}\text{2.84}$ / $\color{#1D7900}\text{3.11 µs}$ |

- **Arrays:** LtMsgPack wrote the fastest, with or without generated code: its preset 92 ns per state and 0.99 µs per snapshot against MessagePack-CSharp's 116 ns and 1.41 µs. With the inline schema in every message (default options) LtMsgPack wrote slower than MessagePack-CSharp (165 ns and 1.54 µs). LtMsgPack read faster in every array setup, generated code 1.2x faster still.
- **Maps:** MessagePack-CSharp wrote the fastest, also against generated LtMsgPack (154 ns against 204 ns per state). LtMsgPack read faster, also without generated code.
- **Leave the inline schema out of small messages.** It writes the names in every message: 131 B per state against 45 B. Use arrays without a schema, or a schema reference to keep reading by name across versions.
- **Size:** LtMsgPack's arrays are the smallest because a zero is written as nil. As a side effect, `-0.0` reads back as `0.0`. The other two keep the sign.
- **Allocations:** writing to a reused buffer allocated 0 B in every MsgPack setup (`ArrayBufferWriter<byte>` for the other two, `MemoryStream` for LtMsgPack). Reading allocated the same in all of them (64 B per state, 1,168 B per snapshot). With `Vec3` as a class, LtMsgPack allocated more when reading (120 B and 2,008 B) and took up to 1.3x as long.
- **JSON** took 8-13x as long as MessagePack-CSharp, with payloads 3.7x as large.

## How it was measured

- **Versions:** MessagePack-CSharp 3.1.12, Nerdbank.MessagePack 1.4.95 (PolyType 1.4.1), and LtMsgPack built from this repository, November 2026.
- **Setups:** each library's fastest documented setup that the test covers (LtMsgPack's rows without generated code are measured after it compiled its writers, see [the source generator](SourceGenerator.md)), with default (trusted data) security and no compression. The test checks every round trip, and that the `MessagePackCSharp()` preset writes MessagePack-CSharp's bytes.
- **Timing:** Intel i7-7700K, Linux, .NET 10.0.14, Release build. `Stopwatch` rather than BenchmarkDotNet, the fastest of 7 interleaved passes per run, and the tables have the medians of three runs. Between the runs 109 of 116 values varied by less than 10%, a few by up to 29% (Nerdbank.MessagePack's arrays of game messages). All setups share one process and its JIT profile, so running each library alone can give different numbers.
- **Not measured:** NativeAOT speed, the `IBufferWriter<byte>` and `Stream` overloads for invoices (the other two libraries are built around `IBufferWriter<byte>`), large binary data, deep nesting, polymorphic lists, `UntrustedData` security, and reading from a slice of a larger buffer.
- **Package size:** a minimal console app per library with one small class that writes and reads it, published framework-dependent in Release. The sizes are the dlls of the published folder, without the app's own. The NativeAOT row subtracts the size of an app without a serializer. The apps ran on .NET 8, .NET 10 and NativeAOT; the .NET Framework row counts the published files only.

To run the benchmarks:

```bash
dotnet test LsMsgPackInteropTests/LsMsgPackInteropTests.csproj -c Release --filter "FullyQualifiedName~BenchmarkLibraries|FullyQualifiedName~BenchmarkGame" --logger "console;verbosity=detailed"
```
