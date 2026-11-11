# LtMsgPack vs MessagePack-CSharp vs Nerdbank.MessagePack

All three serialize .NET classes to MsgPack. This page compares their features, speed and size. It's written by LtMsgPack's author, and the benchmarks are in this repository ([how to run them](#how-it-was-measured)). Open an issue if one of the other libraries can be set up faster than it is here.

> **Benchmark your own use case.** These numbers come from two data models on one machine. MessagePack-CSharp and Nerdbank.MessagePack will probably always have scenarios where they excel, and some of those aren't measured here. Before you choose, run a benchmark with your own data, on your own target platform.

## When to pick which

**LtMsgPack** when you want:

- **Polymorphism without attributes.** A `List<Shape>`, or a property declared as an interface or `object`, round trips out of the box through type ids. The other two need `[Union]`, or `[DerivedTypeShape]` / `DerivedTypeUnion`. Add a [type guard](security.md) for data you don't trust.
- **A schema in the payload.** The default [indexed schema](schema.md) lists property names once per message, so data from older or newer versions of your classes loads by name. With the other two you choose between arrays (the `[Key]` order is the contract) and maps (every name in every object).
- **A TypeScript client** for your ASP.NET backend: [@lsmsgpack/client](TypeScriptClient.md) with generated [models](TypeScriptModels.md).
- **Plain classes on a JIT:** no attributes, `partial` classes or source generator. The [generator](SourceGenerator.md) is optional there: LtMsgPack compiles the same writers and readers at runtime, so it writes and reads about as fast without it (maps keyed by property names it reads faster without it).
- **[ReadDifferences](ReadDifferences.md)** to relieve your helpdesk: show the detailed differences, such as misspelled or missing fields, in a customer portal or in the logs.
- **[Streaming](Streaming.md)** of large datasets (such as daily stock or turnover reports), also to a file with indexes for random access seeking.
- Also: [ADO.NET data readers](DataReader.md), and [presets](Compatibility.md#ltmsgpack-presets) for exchanging data with the other two.

**[MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)** when you want:

- **A long track record.** It's widely used in production, and ASP.NET Core SignalR's MessagePack protocol is built on it.
- **Unity support, LZ4 compression or typeless serialization.**
- **The smallest library on .NET 8 and later, and the smallest payloads with decimal values as text** (see the tables below).
- **Fast writes of small messages as maps:** 1.2-1.4x as fast as LtMsgPack in the game benchmark, also against its generated code.

**[Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack)** when you want:

- **NativeAOT and trimming as the main target**, with PolyType's shape model. It added the least to a NativeAOT app here, without trim or AOT warnings.
- **Async serialization** straight to and from pipes and streams.
- **Preserved object references, structural equality, or JSON schemas** for your types.

**Not LtMsgPack (yet) when:**

- **You need a track record.** It has no known production users, and breaking changes are still allowed. Pin the version and check the release notes before upgrading.
- **You target NativeAOT, WebAssembly AOT or iOS and don't want a source generator.** Without it, LtMsgPack fails under NativeAOT on the first object.
- **The other side uses another library's polymorphism format.** LtMsgPack can't read or write their unions. See [what settings cannot match](Compatibility.md#what-settings-cannot-match).
- **You need Unity:** LtMsgPack isn't tested there.

**Whatever you pick,** LsMsgPack's debugging tools help you track down issues with any of these libraries: the standalone [MsgPack Explorer](../README.md#standalone-debugger-for-windows) for Windows, the [Fiddler inspector](../README.md#fiddler-integration), the [Visual Studio debugger visualizer](../README.md#visual-studio-integration), the [VS Code extension](../LsMsgPackVsCode/README.md) and the [MCP server](Mcp.md) for AI agents.

In the tables, each column runs from green (the best value, also in bold) to red (the worst); in the package size table each row does. System.Text.Json is the baseline and isn't colored.

## Package size

What each library adds to an app, with its dependencies. Generated code isn't counted (except where noted), because it depends on your model.

| Target | LtMsgPack | MessagePack-CSharp | Nerdbank.MessagePack |
|---|---|---|---|
| .NET 10 | $\color{#247200}\text{581.1 KB (2 dlls)}$ | $\color{#009600}\textbf{434.3 KB (3)}$ | $\color{#960000}\text{1,048.0 KB (3)}$ |
| .NET 8 | $\color{#207600}\text{581.1 KB (2)}$ | $\color{#009600}\textbf{433.8 KB (3)}$ | $\color{#960000}\text{1,129.0 KB (4)}$ |
| .NET Framework 4.8 | $\color{#009600}\textbf{863.9 KB (6)}$ | $\color{#0F8700}\text{1,035.5 KB (10)}$ | $\color{#960000}\text{2,529.8 KB (15)}$ |
| NativeAOT, .NET 10, linux-x64: growth of the executable, including the generated code of one small class | $\color{#286E00}\text{2.96 MB}$ | $\color{#960000}\text{4.19 MB}$ | $\color{#009600}\textbf{2.52 MB}$ |
| NuGet download, not deployed as a whole | $\color{#009600}\textbf{988.8 KB}$ | $\color{#1A7C00}\text{1,342.8 KB}$ | $\color{#960000}\text{3,017.1 KB}$ |

- **Debug symbols:** on .NET 8 and later, LtMsgPack's dlls include 97.2 KB of embedded debug symbols and the other two libraries' none. Without those LtMsgPack would be about 484 KB.
- **.NET Framework:** much of the difference is framework packages such as `System.Text.Json` and `System.Collections.Immutable`, which your app may already deploy.
- **NativeAOT:** the executable only holds the code an app reaches, so a real app's numbers will differ. MessagePack-CSharp ran correctly but reported trim and AOT warnings at publish.
- **NuGet download:** the packages of the .NET 10 dependency tree, with all their target frameworks, analyzers and generators.

## Benchmark: invoices

100 invoices with strings, decimals, `DateTime`s, a `Guid`, enums, a nested customer and 3-24 lines each. The time is for 20 rounds of `Serialize` to a `byte[]` or `Deserialize` from one, with the speed relative to System.Text.Json.

| Setup | Size | Write | Read |
|---|---|---|---|
| System.Text.Json | 330.6 KB (100%) | 16.40 ms | 28.26 ms |
| **Arrays** |  |  |  |
| MessagePack-CSharp, `[Key(n)]`, generated | $\color{#009600}\textbf{107.4 KB (32\%)}$ | $\color{#821400}\text{11.33 ms (1.4x)}$ | $\color{#583E00}\text{16.73 ms (1.7x)}$ |
| MessagePack-CSharp, also native Guid and decimal | $\color{#356100}\text{200.6 KB (61\%)}$ | $\color{#4A4C00}\text{7.73 ms (2.1x)}$ | $\color{#415500}\text{13.56 ms (2.1x)}$ |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | $\color{#356100}\text{200.6 KB (61\%)}$ | $\color{#336300}\text{6.28 ms (2.6x)}$ | $\color{#4C4A00}\text{15.20 ms (1.9x)}$ |
| LtMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#009600}\textbf{107.4 KB (32\%)}$ | $\color{#108600}\text{4.01 ms (4.1x)}$ | $\color{#0E8800}\text{6.68 ms (4.2x)}$ |
| LtMsgPack, no schema | $\color{#267000}\text{174.4 KB (53\%)}$ | $\color{#009600}\textbf{2.98 ms (5.5x)}$ | $\color{#009600}\textbf{4.71 ms (6.0x)}$ |
| the same, generated |  | $\color{#059100}\text{3.28 ms (5.0x)}$ | $\color{#039300}\text{5.10 ms (5.5x)}$ |
| LtMsgPack, default options (indexed schema) | $\color{#3D5900}\text{214.7 KB (65\%)}$ | $\color{#0A8C00}\text{3.62 ms (4.5x)}$ | $\color{#019500}\text{4.84 ms (5.8x)}$ |
| the same, generated |  | $\color{#069000}\text{3.35 ms (4.9x)}$ | $\color{#049200}\text{5.25 ms (5.4x)}$ |
| **Maps** |  |  |  |
| MessagePack-CSharp, `keyAsPropertyName`, generated | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#960000}\text{12.65 ms (1.3x)}$ | $\color{#7B1B00}\text{21.55 ms (1.3x)}$ |
| Nerdbank.MessagePack, defaults, generated shapes | $\color{#960000}\text{372.1 KB (113\%)}$ | $\color{#672F00}\text{9.62 ms (1.7x)}$ | $\color{#8E0800}\text{24.14 ms (1.2x)}$ |
| LtMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#316500}\text{6.11 ms (2.7x)}$ | $\color{#108600}\text{6.87 ms (4.1x)}$ |
| LtMsgPack, property names | $\color{#752100}\text{314.2 KB (95\%)}$ | $\color{#1F7700}\text{4.96 ms (3.3x)}$ | $\color{#039300}\text{5.16 ms (5.5x)}$ |
| the same, generated |  | $\color{#1A7C00}\text{4.67 ms (3.5x)}$ | $\color{#049200}\text{5.32 ms (5.4x)}$ |
| LtMsgPack, indexed schema | $\color{#435300}\text{226.5 KB (69\%)}$ | $\color{#1B7B00}\text{4.75 ms (3.5x)}$ | $\color{#069000}\text{5.60 ms (5.0x)}$ |
| **Without attributes or generated code (maps)** |  |  |  |
| MessagePack-CSharp, contractless | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#811500}\text{11.31 ms (1.5x)}$ | $\color{#771F00}\text{20.97 ms (1.3x)}$ |
| Nerdbank.MessagePack, reflection | $\color{#960000}\text{372.1 KB (113\%)}$ | $\color{#702600}\text{10.17 ms (1.6x)}$ | $\color{#960000}\text{25.28 ms (1.1x)}$ |

- **Size depends mostly on decimals and Guids.** MessagePack-CSharp's default writes them as text (`"123.45"` is 7 bytes, a binary decimal 18), which makes it the smallest here. LtMsgPack's own arrays are smaller than the native rows because it writes a zero decimal as nil. The indexed schema costs 12 percentage points on these arrays.
- **Writing:** LtMsgPack was the fastest in every layout, with or without generated code: on arrays 2.1x as fast as Nerdbank.MessagePack (2.98 ms against 6.28 ms, with a 13% smaller payload). LtMsgPack compiles its writers at runtime (after 1000 objects of a class, see [the source generator](SourceGenerator.md)), so generated code writes about as fast (within 10% either way).
- **Reading:** LtMsgPack was the fastest, with or without generated code: 2.9-3.2x as fast as the other two on arrays with binary decimals, 4.2x as fast as MessagePack-CSharp on maps keyed by property names. LtMsgPack compiles its readers at runtime too (after 1000 objects of a class), the same code as the generated readers. Here the generated code read arrays 8% slower than LtMsgPack without it: all setups share one process and its JIT profile. Measured one setup per process, both read arrays equally fast.
- **Same bytes:** LtMsgPack's preset wrote MessagePack-CSharp's arrays 2.8x and its maps 2.1x as fast, and read them 2.5x and 3.1x as fast.
- **Allocations** were about the same for all MsgPack setups: roughly the payload when writing, the objects (4.5 KB per invoice) when reading.

## Benchmark: game messages

A 16 player game: each tick a client sends its state (14 numbers, with two `Vec3` struct properties) and receives a snapshot of the 15 other players. The times are per message, for `Serialize` to a new `byte[]` and `Deserialize` from one. Schema reference rows share the schemas once when connecting (`SchemaStore`), and write 18 bytes per message instead of the inline schema.

| Setup | Size: state / snapshot | State: write / read | Snapshot: write / read |
|---|---|---|---|
| System.Text.Json | 190 B / 2,928 B | 1,298 / 1,454 ns | 18.4 / 21.1 µs |
| **Arrays** |  |  |  |
| MessagePack-CSharp, `[Key(n)]`, generated | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#148200}\text{116}$ / $\color{#177F00}\text{187 ns}$ | $\color{#1A7C00}\text{1.42}$ / $\color{#118500}\text{2.37 µs}$ |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#2D6900}\text{143}$ / $\color{#257100}\text{231 ns}$ | $\color{#2C6A00}\text{1.73}$ / $\color{#207600}\text{3.05 µs}$ |
| LtMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#009600}\textbf{94}$ / $\color{#019500}\text{116 ns}$ | $\color{#009600}\textbf{0.99}$ / $\color{#009600}\textbf{1.57 µs}$ |
| LtMsgPack, no schema | $\color{#009600}\textbf{45 B}$ / $\color{#009600}\textbf{690 B}$ | $\color{#0C8A00}\text{107}$ / $\color{#009600}\textbf{114 ns}$ | $\color{#0F8700}\text{1.25}$ / $\color{#009600}\text{1.59 µs}$ |
| the same, generated |  | $\color{#0E8800}\text{109}$ / $\color{#029400}\text{119 ns}$ | $\color{#0F8700}\text{1.25}$ / $\color{#029400}\text{1.65 µs}$ |
| LtMsgPack, schema reference | $\color{#1F7700}\text{63 B}$ / $\color{#029400}\text{708 B}$ | $\color{#217500}\text{130}$ / $\color{#078F00}\text{137 ns}$ | $\color{#128400}\text{1.30}$ / $\color{#029400}\text{1.68 µs}$ |
| the same, generated |  | $\color{#207600}\text{129}$ / $\color{#098D00}\text{142 ns}$ | $\color{#148200}\text{1.33}$ / $\color{#039300}\text{1.73 µs}$ |
| LtMsgPack, default options (indexed schema) | $\color{#960000}\text{131 B}$ / $\color{#118500}\text{826 B}$ | $\color{#3E5800}\text{162}$ / $\color{#078F00}\text{136 ns}$ | $\color{#1E7800}\text{1.50}$ / $\color{#029400}\text{1.68 µs}$ |
| the same, generated |  | $\color{#3D5900}\text{161}$ / $\color{#098D00}\text{142 ns}$ | $\color{#207600}\text{1.52}$ / $\color{#039300}\text{1.73 µs}$ |
| **Maps** |  |  |  |
| MessagePack-CSharp, `keyAsPropertyName`, generated | $\color{#880E00}\text{123 B}$ / $\color{#960000}\text{1,890 B}$ | $\color{#375F00}\text{155}$ / $\color{#504600}\text{369 ns}$ | $\color{#316500}\text{1.82}$ / $\color{#4C4A00}\text{5.08 µs}$ |
| Nerdbank.MessagePack, defaults, generated shapes | $\color{#880E00}\text{123 B}$ / $\color{#960000}\text{1,890 B}$ | $\color{#960000}\text{259}$ / $\color{#960000}\text{590 ns}$ | $\color{#960000}\text{3.51}$ / $\color{#960000}\text{8.50 µs}$ |
| LtMsgPack, property names | $\color{#712500}\text{110 B}$ / $\color{#7F1700}\text{1,703 B}$ | $\color{#5E3800}\text{197}$ / $\color{#098D00}\text{141 ns}$ | $\color{#5D3900}\text{2.55}$ / $\color{#098D00}\text{2.00 µs}$ |
| the same, generated |  | $\color{#524400}\text{184}$ / $\color{#0C8A00}\text{153 ns}$ | $\color{#534300}\text{2.39}$ / $\color{#0C8A00}\text{2.13 µs}$ |

- **Arrays:** LtMsgPack wrote the fastest, with or without generated code: its preset 94 ns per state and 0.99 µs per snapshot against MessagePack-CSharp's 116 ns and 1.42 µs. With the inline schema in every message (default options) LtMsgPack wrote slower than MessagePack-CSharp (162 ns and 1.50 µs). LtMsgPack read faster in every array setup, with or without generated code (114-142 ns per state against 187 ns).
- **Maps:** MessagePack-CSharp wrote the fastest, also against generated LtMsgPack (155 ns against 184 ns per state). LtMsgPack read 2.4-2.6x as fast (141 ns without and 153 ns with generated code, against 369 ns per state).
- **Leave the inline schema out of small messages.** It writes the names in every message: 131 B per state against 45 B. Use arrays without a schema, or a schema reference to keep reading by name across versions.
- **Size:** LtMsgPack's arrays are the smallest because a zero is written as nil. As a side effect, `-0.0` reads back as `0.0`. The other two keep the sign.
- **Allocations:** writing to a reused buffer allocated 0 B in every MsgPack setup (`ArrayBufferWriter<byte>` for the other two, `MemoryStream` for LtMsgPack). Reading allocated the same in all of them (64 B per state, 1,168 B per snapshot). With `Vec3` as a class, LtMsgPack allocated more when reading (120 B and 2,008 B) and took up to 1.4x as long.
- **JSON** took 8-13x as long as MessagePack-CSharp, with payloads 3.7x as large.

## How it was measured

- **Versions:** MessagePack-CSharp 3.1.12, Nerdbank.MessagePack 1.4.95 (PolyType 1.4.1), and LtMsgPack built from this repository, November 2026.
- **Setups:** each library's fastest documented setup that the test covers (LtMsgPack's rows without generated code are measured after it compiled its writers and readers, see [the source generator](SourceGenerator.md)), with default (trusted data) security and no compression. The test checks every round trip, and that the `MessagePackCSharp()` preset writes MessagePack-CSharp's bytes.
- **Timing:** Intel i7-7700K, Linux, .NET 10.0.14, Release build. `Stopwatch` rather than BenchmarkDotNet, the fastest of 7 interleaved passes per run, and the tables have the medians of three runs. Between the runs 76 of the 90 times in the tables varied by less than 10%, the others by up to 35% (Nerdbank.MessagePack writing invoices as maps). All setups share one process and its JIT profile, so running each library alone can give different numbers (LtMsgPack's generated readers: see the invoices).
- **Not measured:** NativeAOT speed, the `IBufferWriter<byte>` and `Stream` overloads for invoices (the other two libraries are built around `IBufferWriter<byte>`), large binary data, deep nesting, polymorphic lists, `UntrustedData` security, and reading from a slice of a larger buffer.
- **Package size:** a minimal console app per library with one small class that writes and reads it, published framework-dependent in Release. The sizes are the dlls of the published folder, without the app's own. The NativeAOT row subtracts the size of an app without a serializer. The apps ran on .NET 8, .NET 10 and NativeAOT; the .NET Framework row counts the published files only. KB and MB are 1,000 and 1,000,000 bytes.

To run the benchmarks:

```bash
dotnet test LsMsgPackInteropTests/LsMsgPackInteropTests.csproj -c Release --filter "FullyQualifiedName~BenchmarkLibraries|FullyQualifiedName~BenchmarkGame" --logger "console;verbosity=detailed"
```
