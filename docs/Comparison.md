# LsMsgPack vs MessagePack-CSharp vs Nerdbank.MessagePack

All three serialize .NET classes to MsgPack. This page compares their features, speed and size. It's written by LsMsgPack's author, and the benchmarks are in this repository ([how to run them](#how-it-was-measured)). Open an issue if one of the other libraries can be set up faster than it is here.

> **Benchmark your own use case.** These numbers come from two data models on one machine. MessagePack-CSharp and Nerdbank.MessagePack will probably always have scenarios where they excel, and some of those aren't measured here. Before you choose, run a benchmark with your own data, on your own target platform.

## When to pick which

**LsMsgPack** when you want:

- **Polymorphism without attributes.** A `List<Shape>`, or a property declared as an interface or `object`, round trips out of the box through type ids. The other two need `[Union]`, or `[DerivedTypeShape]` / `DerivedTypeUnion`. Add a [type guard](security.md) for data you don't trust.
- **A schema in the payload.** The default [indexed schema](schema.md) lists property names once per message, so data from older or newer versions of your classes loads by name. With the other two you choose between arrays (the `[Key]` order is the contract) and maps (every name in every object).
- **F# types without setup.** Options as their values, unions with their case names (as System.Text.Json writes them), lists and maps: see [FSharp.md](FSharp.md). MessagePack-CSharp needs its F# extension package and writes cases by number.
- **A TypeScript client** for your ASP.NET backend: [@lsmsgpack/client](TypeScriptClient.md) with generated [models](TypeScriptModels.md).
- **Plain classes on a JIT:** no attributes, `partial` classes or source generator. The [generator](SourceGenerator.md) is optional there: LsMsgPack compiles the same writers and readers at runtime, so it writes and reads about as fast without it (maps keyed by property names it reads faster without it).
- **[ReadDifferences](ReadDifferences.md)** to relieve your helpdesk: show the detailed differences, such as misspelled or missing fields, in a customer portal or in the logs.
- **[Streaming](Streaming.md)** of large datasets (such as daily stock or turnover reports), also to a file with indexes for random access seeking.
- **A SignalR hub protocol** ([LsMsgPack.SignalR](SignalR.md)): about twice as fast as Microsoft's MessagePack protocol, which is built on MessagePack-CSharp. Registered as "messagepack" it writes that protocol's bytes, so existing clients keep working.
- Also: [ADO.NET data readers](DataReader.md), and [presets](Compatibility.md#lsmsgpack-presets) for exchanging data with the other two.

**[MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)** when you want:

- **A long track record.** It's widely used in production, and ASP.NET Core SignalR's MessagePack protocol is built on it.
- **Unity on every platform, or typeless serialization.** LsMsgPack is tested with Unity 6 on desktop (Mono and IL2CPP), WebGL and Android, not yet on iOS or consoles.
- **The smallest library, except under NativeAOT, and the smallest payloads with decimal values as text** (see the tables below).
- **Fast writes of small messages as maps:** 1.2-1.4x as fast as LsMsgPack in the game benchmark, also against its generated code.

**[Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack)** when you want:

- **NativeAOT and trimming as the main target**, with PolyType's shape model. It added the least to a NativeAOT app here, without trim or AOT warnings.
- **Async serialization** straight to and from pipes and streams.
- **Structural equality or JSON schemas** for your types.

**Not LsMsgPack (yet) when:**

- **You need a track record.** It has no known production users, and breaking changes are still allowed. Pin the version and check the release notes before upgrading.
- **You target NativeAOT, WebAssembly AOT or iOS and don't want a source generator.** Without it, LsMsgPack runs under NativeAOT by reflection only when the app keeps its model classes from the trimmer, and the app has to keep some collections itself (see [Without the generator](SourceGenerator.md#without-the-generator)).
- **The other side uses another library's polymorphism format.** LsMsgPack can't read or write their unions. See [what settings cannot match](Compatibility.md#what-settings-cannot-match).
- **You need Unity on iOS or consoles:** LsMsgPack is tested with Unity 6 in the editor, in Mono and IL2CPP players on Linux, in WebGL players and on Android, not on those platforms yet (see [Unity](Unity.md)).

**Whatever you pick,** LsMsgPack's debugging tools help you track down issues with any of these libraries: the standalone [MsgPack Explorer](../README.md#standalone-debugger-for-windows) for Windows, the [Fiddler inspector](../README.md#fiddler-integration), the [Visual Studio debugger visualizer](../README.md#visual-studio-integration), the [VS Code extension](../LsMsgPackVsCode/README.md) and the [MCP server](Mcp.md) for AI agents.

In the tables, each column runs from green (the best value, also in bold) to red (the worst); in the package size table each row does. System.Text.Json is the baseline and isn't colored.

## Package size

What each library adds to an app, with its dependencies. Generated code isn't counted (except where noted), because it depends on your model.

| Target | LsMsgPack | MessagePack-CSharp | Nerdbank.MessagePack |
|---|---|---|---|
| .NET 10 | $\color{#5F3700}\text{824.8 KB (2 dlls)}$ | $\color{#009600}\textbf{434.3 KB (3)}$ | $\color{#960000}\text{1,049.5 KB (3)}$ |
| .NET 8 | $\color{#544200}\text{824.8 KB (2)}$ | $\color{#009600}\textbf{433.8 KB (3)}$ | $\color{#960000}\text{1,130.6 KB (4)}$ |
| .NET Framework 4.8 | $\color{#078F00}\text{1,101.9 KB (6)}$ | $\color{#009600}\textbf{1,035.5 KB (10)}$ | $\color{#960000}\text{2,530.8 KB (15)}$ |
| NativeAOT, .NET 10, linux-x64: growth of the executable, including the generated code of one small class | $\color{#504600}\text{3.37 MB}$ | $\color{#960000}\text{4.20 MB}$ | $\color{#009600}\textbf{2.42 MB}$ |
| NuGet download, not deployed as a whole | $\color{#029400}\text{1,370.2 KB}$ | $\color{#009600}\textbf{1,342.8 KB}$ | $\color{#960000}\text{3,020.7 KB}$ |

- **Debug symbols:** on .NET 8 and later, LsMsgPack's dlls include 133.6 KB of embedded debug symbols and the other two libraries' none. Without those LsMsgPack would be about 691 KB.
- **.NET Framework:** much of the difference is framework packages such as `System.Text.Json` and `System.Collections.Immutable`, which your app may already deploy.
- **NativeAOT:** the executable only holds the code an app reaches, so a real app's numbers will differ. MessagePack-CSharp ran correctly but reported trim and AOT warnings at publish.
- **NuGet download:** the packages of the .NET 10 dependency tree, with all their target frameworks, analyzers and generators.

## Benchmark: invoices

100 invoices with strings, decimals, `DateTime`s, a `Guid`, enums, a nested customer and 3-24 lines each. The time is for 20 rounds of `Serialize` to a `byte[]` or `Deserialize` from one, with the speed relative to System.Text.Json.

| Setup | Size | Write | Read |
|---|---|---|---|
| System.Text.Json | 330.6 KB (100%) | 17.40 ms | 29.29 ms |
| **Arrays** |  |  |  |
| MessagePack-CSharp, `[Key(n)]`, generated | $\color{#009600}\textbf{107.4 KB (32\%)}$ | $\color{#7F1700}\text{11.85 ms (1.5x)}$ | $\color{#573F00}\text{18.01 ms (1.6x)}$ |
| MessagePack-CSharp, also native Guid and decimal | $\color{#356100}\text{200.6 KB (61\%)}$ | $\color{#484E00}\text{8.03 ms (2.2x)}$ | $\color{#3D5900}\text{14.16 ms (2.1x)}$ |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | $\color{#356100}\text{200.6 KB (61\%)}$ | $\color{#2D6900}\text{6.15 ms (2.8x)}$ | $\color{#494D00}\text{15.95 ms (1.8x)}$ |
| LsMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#009600}\textbf{107.4 KB (32\%)}$ | $\color{#108600}\text{4.12 ms (4.2x)}$ | $\color{#0D8900}\text{6.98 ms (4.2x)}$ |
| LsMsgPack, no schema | $\color{#267000}\text{175.3 KB (53\%)}$ | $\color{#009600}\textbf{3.03 ms (5.7x)}$ | $\color{#009600}\textbf{5.03 ms (5.8x)}$ |
| the same, generated |  | $\color{#019500}\text{3.09 ms (5.6x)}$ | $\color{#019500}\text{5.20 ms (5.6x)}$ |
| LsMsgPack, default options (indexed schema) | $\color{#3D5900}\text{215.6 KB (65\%)}$ | $\color{#0C8A00}\text{3.85 ms (4.5x)}$ | $\color{#019500}\text{5.18 ms (5.7x)}$ |
| the same, generated |  | $\color{#098D00}\text{3.65 ms (4.8x)}$ | $\color{#029400}\text{5.35 ms (5.5x)}$ |
| **Maps** |  |  |  |
| MessagePack-CSharp, `keyAsPropertyName`, generated | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#960000}\text{13.44 ms (1.3x)}$ | $\color{#771F00}\text{22.73 ms (1.3x)}$ |
| Nerdbank.MessagePack, defaults, generated shapes | $\color{#960000}\text{372.1 KB (113\%)}$ | $\color{#554100}\text{8.96 ms (1.9x)}$ | $\color{#960000}\text{27.33 ms (1.1x)}$ |
| LsMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#336300}\text{6.59 ms (2.6x)}$ | $\color{#108600}\text{7.42 ms (3.9x)}$ |
| LsMsgPack, property names | $\color{#762000}\text{315.1 KB (95\%)}$ | $\color{#217500}\text{5.35 ms (3.3x)}$ | $\color{#049200}\text{5.56 ms (5.3x)}$ |
| the same, generated |  | $\color{#1C7A00}\text{4.97 ms (3.5x)}$ | $\color{#039300}\text{5.54 ms (5.3x)}$ |
| LsMsgPack, indexed schema | $\color{#445200}\text{227.4 KB (69\%)}$ | $\color{#237300}\text{5.44 ms (3.2x)}$ | $\color{#069000}\text{5.93 ms (4.9x)}$ |
| **Without attributes or generated code (maps)** |  |  |  |
| MessagePack-CSharp, contractless | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#861000}\text{12.36 ms (1.4x)}$ | $\color{#722400}\text{22.01 ms (1.3x)}$ |
| Nerdbank.MessagePack, reflection | $\color{#960000}\text{372.1 KB (113\%)}$ | $\color{#5B3B00}\text{9.33 ms (1.9x)}$ | $\color{#910500}\text{26.57 ms (1.1x)}$ |

- **Size depends mostly on decimals and Guids.** MessagePack-CSharp's default writes them as text (`"123.45"` is 7 bytes, a binary decimal 18), which makes it the smallest here. LsMsgPack's own arrays are smaller than the native rows because it writes a zero decimal as nil. The indexed schema costs 12 percentage points on these arrays.
- **Writing:** LsMsgPack was the fastest in every layout, with or without generated code: on arrays 2.0x as fast as Nerdbank.MessagePack (3.03 ms against 6.15 ms, with a 13% smaller payload). LsMsgPack compiles its writers at runtime (after 1000 objects of a class, see [the source generator](SourceGenerator.md)), so generated code writes about as fast (within 10% either way).
- **Reading:** LsMsgPack was the fastest, with or without generated code: 2.6-3.2x as fast as the other two on arrays with binary decimals, 4.1x as fast as MessagePack-CSharp on maps keyed by property names. LsMsgPack compiles its readers at runtime too (after 1000 objects of a class), the same code as the generated readers. Here the generated code read arrays about 3% slower than LsMsgPack without it: all setups share one process and its JIT profile. Measured one setup per process, both read arrays equally fast.
- **Same bytes:** LsMsgPack's preset wrote MessagePack-CSharp's arrays 2.9x and its maps 2.0x as fast, and read them 2.6x and 3.1x as fast.
- **Allocations** were about the same for all MsgPack setups: roughly the payload when writing, the objects (4.5 KB per invoice) when reading.

## Benchmark: game messages

A 16 player game: each tick a client sends its state (14 numbers, with two `Vec3` struct properties) and receives a snapshot of the 15 other players. The times are per message, for `Serialize` to a new `byte[]` and `Deserialize` from one. Schema reference rows share the schemas once when connecting (`SchemaStore`), and write 18 bytes per message instead of the inline schema.

| Setup | Size: state / snapshot | State: write / read | Snapshot: write / read |
|---|---|---|---|
| System.Text.Json | 190 B / 2,928 B | 1,378 / 1,517 ns | 19.37 / 22.13 µs |
| **Arrays** |  |  |  |
| MessagePack-CSharp, `[Key(n)]`, generated | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#177F00}\text{129}$ / $\color{#158100}\text{200 ns}$ | $\color{#1A7C00}\text{1.55}$ / $\color{#118500}\text{2.54 µs}$ |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#326400}\text{161}$ / $\color{#217500}\text{238 ns}$ | $\color{#3A5C00}\text{2.11}$ / $\color{#1F7700}\text{3.21 µs}$ |
| LsMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#009600}\textbf{102}$ / $\color{#019500}\text{133 ns}$ | $\color{#009600}\textbf{1.11}$ / $\color{#009600}\text{1.74 µs}$ |
| LsMsgPack, no schema | $\color{#009600}\textbf{45 B}$ / $\color{#009600}\textbf{690 B}$ | $\color{#098D00}\text{113}$ / $\color{#009600}\textbf{131 ns}$ | $\color{#0A8C00}\text{1.29}$ / $\color{#009600}\textbf{1.73 µs}$ |
| the same, generated |  | $\color{#098D00}\text{112}$ / $\color{#019500}\text{135 ns}$ | $\color{#098D00}\text{1.26}$ / $\color{#019500}\text{1.76 µs}$ |
| LsMsgPack, schema reference | $\color{#1E7800}\text{63 B}$ / $\color{#029400}\text{708 B}$ | $\color{#197D00}\text{131}$ / $\color{#078F00}\text{155 ns}$ | $\color{#0C8A00}\text{1.31}$ / $\color{#029400}\text{1.82 µs}$ |
| the same, generated |  | $\color{#1A7C00}\text{133}$ / $\color{#088E00}\text{158 ns}$ | $\color{#0C8A00}\text{1.31}$ / $\color{#029400}\text{1.84 µs}$ |
| LsMsgPack, default options (indexed schema) | $\color{#8F0700}\text{131 B}$ / $\color{#118500}\text{826 B}$ | $\color{#3D5900}\text{173}$ / $\color{#088E00}\text{156 ns}$ | $\color{#1C7A00}\text{1.60}$ / $\color{#029400}\text{1.83 µs}$ |
| the same, generated |  | $\color{#3D5900}\text{173}$ / $\color{#088E00}\text{158 ns}$ | $\color{#1B7B00}\text{1.57}$ / $\color{#039300}\text{1.86 µs}$ |
| LsMsgPack, default options, `System.Numerics.Vector3` properties | $\color{#960000}\text{135 B}$ / $\color{#128400}\text{831 B}$ | $\color{#425400}\text{179}$ / $\color{#088E00}\text{158 ns}$ | $\color{#1D7900}\text{1.61}$ / $\color{#019500}\text{1.80 µs}$ |
| the same, generated |  | $\color{#415500}\text{178}$ / $\color{#088E00}\text{158 ns}$ | $\color{#1A7C00}\text{1.56}$ / $\color{#039300}\text{1.88 µs}$ |
| LsMsgPack, default options, `[DataMember]` fields | $\color{#930300}\text{133 B}$ / $\color{#118500}\text{829 B}$ | $\color{#425400}\text{180}$ / $\color{#078F00}\text{154 ns}$ | $\color{#1E7800}\text{1.62}$ / $\color{#019500}\text{1.80 µs}$ |
| the same, generated |  | $\color{#3D5900}\text{173}$ / $\color{#088E00}\text{158 ns}$ | $\color{#197D00}\text{1.54}$ / $\color{#039300}\text{1.89 µs}$ |
| **Maps** |  |  |  |
| MessagePack-CSharp, `keyAsPropertyName`, generated | $\color{#821400}\text{123 B}$ / $\color{#960000}\text{1,890 B}$ | $\color{#336300}\text{162}$ / $\color{#4F4700}\text{385 ns}$ | $\color{#316500}\text{1.95}$ / $\color{#4B4B00}\text{5.33 µs}$ |
| Nerdbank.MessagePack, defaults, generated shapes | $\color{#821400}\text{123 B}$ / $\color{#960000}\text{1,890 B}$ | $\color{#960000}\text{278}$ / $\color{#960000}\text{615 ns}$ | $\color{#960000}\text{3.69}$ / $\color{#960000}\text{8.90 µs}$ |
| LsMsgPack, property names | $\color{#6C2A00}\text{110 B}$ / $\color{#7F1700}\text{1,703 B}$ | $\color{#623400}\text{217}$ / $\color{#0B8B00}\text{165 ns}$ | $\color{#613500}\text{2.78}$ / $\color{#0A8C00}\text{2.21 µs}$ |
| the same, generated |  | $\color{#4E4800}\text{194}$ / $\color{#0D8900}\text{172 ns}$ | $\color{#514500}\text{2.51}$ / $\color{#0C8A00}\text{2.31 µs}$ |
| LsMsgPack, property names, `[DataMember]` fields | $\color{#6C2A00}\text{110 B}$ / $\color{#7F1700}\text{1,703 B}$ | $\color{#5C3A00}\text{210}$ / $\color{#0A8C00}\text{163 ns}$ | $\color{#5D3900}\text{2.71}$ / $\color{#0A8C00}\text{2.21 µs}$ |

- **Arrays:** LsMsgPack wrote the fastest, with or without generated code: its preset 102 ns per state and 1.11 µs per snapshot against MessagePack-CSharp's 129 ns and 1.55 µs. With the inline schema in every message (default options) LsMsgPack wrote states slower than MessagePack-CSharp (173 ns) and snapshots slightly slower (1.57-1.60 µs). LsMsgPack read faster in every array setup, with or without generated code (131-158 ns per state against 200 ns).
- **Maps:** MessagePack-CSharp wrote the fastest, also against generated LsMsgPack (162 ns against 194 ns per state). LsMsgPack read 2.2-2.3x as fast (165 ns without and 172 ns with generated code, against 385 ns per state).
- **`Vector3` and fields:** with `System.Numerics.Vector3` (written by its public fields) or `[DataMember]` fields instead of properties, LsMsgPack wrote and read as fast as with the `Vec3` properties (within 4%). Their sizes differ only by the type names in the indexed schema. Keyed by property names, the fields are the same bytes as the properties.
- **Leave the inline schema out of small messages.** It writes the names in every message: 131 B per state against 45 B. Use arrays without a schema, or a schema reference to keep reading by name across versions.
- **Size:** LsMsgPack's arrays are the smallest because a zero is written as nil. As a side effect, `-0.0` reads back as `0.0`. The other two keep the sign.
- **Allocations:** writing to a reused buffer allocated 0 B in every MsgPack setup (an `ArrayBufferWriter<byte>`). Reading allocated the same in all of them (64 B per state, 1,168 B per snapshot). With `Vec3` as a class, LsMsgPack allocated more when reading (120 B and 2,008 B) and took up to 1.4x as long.
- **JSON** took 8-13x as long as MessagePack-CSharp, with payloads 3.7x as large.

## How it was measured

- **Versions:** MessagePack-CSharp 3.1.12, Nerdbank.MessagePack 1.4.104 (PolyType 1.4.1), and LsMsgPack built from this repository, November 2026.
- **Setups:** each library's fastest documented setup that the test covers (LsMsgPack's rows without generated code are measured after it compiled its writers and readers, see [the source generator](SourceGenerator.md)), with default (trusted data) security and no compression. The test checks every round trip, and that the `MessagePackCSharp()` preset writes MessagePack-CSharp's bytes.
- **Timing:** Intel i7-7700K, Linux, .NET 10.0.15, Release build. `Stopwatch` rather than BenchmarkDotNet, the fastest of 7 interleaved passes per run, and the tables have the medians of three runs. Between the runs most times varied by less than 10%, a few by 11-14% (the third run read the invoices about 10% slower in every LsMsgPack setup), and MessagePack-CSharp's native arrays wrote 18% faster in one run. LsMsgPack's snapshot reads without generated code took between 1.65 and 1.88 µs per run, with the same build: placement in memory, which can move such times by 5-10%. All setups share one process and its JIT profile, so running each library alone can give different numbers (LsMsgPack's generated readers: see the invoices).
- **Not measured:** the other libraries under NativeAOT (LsMsgPack's NativeAOT speed: [SourceGenerator.md](SourceGenerator.md#measurements)), the `IBufferWriter<byte>` and `Stream` overloads for invoices (the other two libraries are built around `IBufferWriter<byte>`), large binary data, deep nesting, polymorphic lists, `UntrustedData` security, and reading from a slice of a larger buffer.
- **Package size:** a minimal console app per library with one small class that writes and reads it, published framework-dependent in Release. The sizes are the dlls of the published folder, without the app's own. The NativeAOT row subtracts the size of an app without a serializer. The apps ran on .NET 8, .NET 10 and NativeAOT; the .NET Framework row counts the published files only. KB and MB are 1,000 and 1,000,000 bytes.

To run the benchmarks:

```bash
dotnet test LsMsgPackInteropTests/LsMsgPackInteropTests.csproj -c Release --filter "FullyQualifiedName~BenchmarkLibraries|FullyQualifiedName~BenchmarkGame" --logger "console;verbosity=detailed"
```
