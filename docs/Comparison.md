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
- **Unity support or typeless serialization.**
- **The smallest library, except under NativeAOT, and the smallest payloads with decimal values as text** (see the tables below).
- **Fast writes of small messages as maps:** 1.1-1.4x as fast as LsMsgPack in the game benchmark, also against its generated code.

**[Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack)** when you want:

- **NativeAOT and trimming as the main target**, with PolyType's shape model. It added the least to a NativeAOT app here, without trim or AOT warnings.
- **Async serialization** straight to and from pipes and streams.
- **Structural equality or JSON schemas** for your types.

**Not LsMsgPack (yet) when:**

- **You need a track record.** It has no known production users, and breaking changes are still allowed. Pin the version and check the release notes before upgrading.
- **You target NativeAOT, WebAssembly AOT or iOS and don't want a source generator.** Without it, LsMsgPack runs under NativeAOT by reflection only when the app keeps its model classes from the trimmer, and the app has to keep some collections itself (see [Without the generator](SourceGenerator.md#without-the-generator)).
- **The other side uses another library's polymorphism format.** LsMsgPack can't read or write their unions. See [what settings cannot match](Compatibility.md#what-settings-cannot-match).
- **You need Unity:** LsMsgPack isn't tested there.

**Whatever you pick,** LsMsgPack's debugging tools help you track down issues with any of these libraries: the standalone [MsgPack Explorer](../README.md#standalone-debugger-for-windows) for Windows, the [Fiddler inspector](../README.md#fiddler-integration), the [Visual Studio debugger visualizer](../README.md#visual-studio-integration), the [VS Code extension](../LsMsgPackVsCode/README.md) and the [MCP server](Mcp.md) for AI agents.

In the tables, each column runs from green (the best value, also in bold) to red (the worst); in the package size table each row does. System.Text.Json is the baseline and isn't colored.

## Package size

What each library adds to an app, with its dependencies. Generated code isn't counted (except where noted), because it depends on your model.

| Target | LsMsgPack | MessagePack-CSharp | Nerdbank.MessagePack |
|---|---|---|---|
| .NET 10 | $\color{#5B3B00}\text{808.4 KB (2 dlls)}$ | $\color{#009600}\textbf{434.3 KB (3)}$ | $\color{#960000}\text{1,049.5 KB (3)}$ |
| .NET 8 | $\color{#514500}\text{808.4 KB (2)}$ | $\color{#009600}\textbf{433.8 KB (3)}$ | $\color{#960000}\text{1,130.6 KB (4)}$ |
| .NET Framework 4.8 | $\color{#059100}\text{1,087.1 KB (6)}$ | $\color{#009600}\textbf{1,035.5 KB (10)}$ | $\color{#960000}\text{2,530.8 KB (15)}$ |
| NativeAOT, .NET 10, linux-x64: growth of the executable, including the generated code of one small class | $\color{#484E00}\text{3.28 MB}$ | $\color{#960000}\text{4.20 MB}$ | $\color{#009600}\textbf{2.42 MB}$ |
| NuGet download, not deployed as a whole | $\color{#009600}\text{1,346.4 KB}$ | $\color{#009600}\textbf{1,342.8 KB}$ | $\color{#960000}\text{3,020.7 KB}$ |

- **Debug symbols:** on .NET 8 and later, LsMsgPack's dlls include 131.6 KB of embedded debug symbols and the other two libraries' none. Without those LsMsgPack would be about 677 KB.
- **.NET Framework:** much of the difference is framework packages such as `System.Text.Json` and `System.Collections.Immutable`, which your app may already deploy.
- **NativeAOT:** the executable only holds the code an app reaches, so a real app's numbers will differ. MessagePack-CSharp ran correctly but reported trim and AOT warnings at publish.
- **NuGet download:** the packages of the .NET 10 dependency tree, with all their target frameworks, analyzers and generators.

## Benchmark: invoices

100 invoices with strings, decimals, `DateTime`s, a `Guid`, enums, a nested customer and 3-24 lines each. The time is for 20 rounds of `Serialize` to a `byte[]` or `Deserialize` from one, with the speed relative to System.Text.Json.

| Setup | Size | Write | Read |
|---|---|---|---|
| System.Text.Json | 330.6 KB (100%) | 16.46 ms | 28.06 ms |
| **Arrays** |  |  |  |
| MessagePack-CSharp, `[Key(n)]`, generated | $\color{#009600}\textbf{107.4 KB (32\%)}$ | $\color{#7D1900}\text{11.18 ms (1.5x)}$ | $\color{#564000}\text{16.89 ms (1.7x)}$ |
| MessagePack-CSharp, also native Guid and decimal | $\color{#356100}\text{200.6 KB (61\%)}$ | $\color{#4A4C00}\text{7.79 ms (2.1x)}$ | $\color{#3F5700}\text{13.67 ms (2.1x)}$ |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | $\color{#356100}\text{200.6 KB (61\%)}$ | $\color{#316500}\text{6.16 ms (2.7x)}$ | $\color{#4E4800}\text{15.79 ms (1.8x)}$ |
| LsMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#009600}\textbf{107.4 KB (32\%)}$ | $\color{#0E8800}\text{3.84 ms (4.3x)}$ | $\color{#0D8900}\text{6.64 ms (4.2x)}$ |
| LsMsgPack, no schema | $\color{#267000}\text{175.3 KB (53\%)}$ | $\color{#019500}\text{2.96 ms (5.6x)}$ | $\color{#009600}\textbf{4.78 ms (5.9x)}$ |
| the same, generated |  | $\color{#009600}\textbf{2.92 ms (5.6x)}$ | $\color{#019500}\text{4.96 ms (5.7x)}$ |
| LsMsgPack, default options (indexed schema) | $\color{#3D5900}\text{215.6 KB (65\%)}$ | $\color{#0A8C00}\text{3.60 ms (4.6x)}$ | $\color{#009600}\text{4.83 ms (5.8x)}$ |
| the same, generated |  | $\color{#088E00}\text{3.42 ms (4.8x)}$ | $\color{#029400}\text{5.09 ms (5.5x)}$ |
| **Maps** |  |  |  |
| MessagePack-CSharp, `keyAsPropertyName`, generated | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#960000}\text{12.83 ms (1.3x)}$ | $\color{#771F00}\text{21.56 ms (1.3x)}$ |
| Nerdbank.MessagePack, defaults, generated shapes | $\color{#960000}\text{372.1 KB (113\%)}$ | $\color{#554100}\text{8.52 ms (1.9x)}$ | $\color{#8A0C00}\text{24.30 ms (1.2x)}$ |
| LsMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#326400}\text{6.25 ms (2.6x)}$ | $\color{#0F8700}\text{6.96 ms (4.0x)}$ |
| LsMsgPack, property names | $\color{#762000}\text{315.1 KB (95\%)}$ | $\color{#1E7800}\text{4.91 ms (3.4x)}$ | $\color{#039300}\text{5.26 ms (5.3x)}$ |
| the same, generated |  | $\color{#1A7C00}\text{4.62 ms (3.6x)}$ | $\color{#049200}\text{5.28 ms (5.3x)}$ |
| LsMsgPack, indexed schema | $\color{#445200}\text{227.4 KB (69\%)}$ | $\color{#207600}\text{5.04 ms (3.3x)}$ | $\color{#069000}\text{5.58 ms (5.0x)}$ |
| **Without attributes or generated code (maps)** |  |  |  |
| MessagePack-CSharp, contractless | $\color{#613500}\text{278.9 KB (84\%)}$ | $\color{#7E1800}\text{11.23 ms (1.5x)}$ | $\color{#722400}\text{20.88 ms (1.3x)}$ |
| Nerdbank.MessagePack, reflection | $\color{#960000}\text{372.1 KB (113\%)}$ | $\color{#5D3900}\text{9.05 ms (1.8x)}$ | $\color{#960000}\text{25.95 ms (1.1x)}$ |

- **Size depends mostly on decimals and Guids.** MessagePack-CSharp's default writes them as text (`"123.45"` is 7 bytes, a binary decimal 18), which makes it the smallest here. LsMsgPack's own arrays are smaller than the native rows because it writes a zero decimal as nil. The indexed schema costs 12 percentage points on these arrays.
- **Writing:** LsMsgPack was the fastest in every layout, with or without generated code: on arrays 2.1x as fast as Nerdbank.MessagePack (2.96 ms against 6.16 ms, with a 13% smaller payload). LsMsgPack compiles its writers at runtime (after 1000 objects of a class, see [the source generator](SourceGenerator.md)), so generated code writes about as fast (within 10% either way).
- **Reading:** LsMsgPack was the fastest, with or without generated code: 2.8-3.3x as fast as the other two on arrays with binary decimals, 4.1x as fast as MessagePack-CSharp on maps keyed by property names. LsMsgPack compiles its readers at runtime too (after 1000 objects of a class), the same code as the generated readers. Here the generated code read arrays 4-5% slower than LsMsgPack without it: all setups share one process and its JIT profile. Measured one setup per process, both read arrays equally fast.
- **Same bytes:** LsMsgPack's preset wrote MessagePack-CSharp's arrays 2.9x and its maps 2.1x as fast, and read them 2.5x and 3.1x as fast.
- **Allocations** were about the same for all MsgPack setups: roughly the payload when writing, the objects (4.5 KB per invoice) when reading.

## Benchmark: game messages

A 16 player game: each tick a client sends its state (14 numbers, with two `Vec3` struct properties) and receives a snapshot of the 15 other players. The times are per message, for `Serialize` to a new `byte[]` and `Deserialize` from one. Schema reference rows share the schemas once when connecting (`SchemaStore`), and write 18 bytes per message instead of the inline schema.

| Setup | Size: state / snapshot | State: write / read | Snapshot: write / read |
|---|---|---|---|
| System.Text.Json | 190 B / 2,928 B | 1,297 / 1,447 ns | 18.44 / 21.20 µs |
| **Arrays** |  |  |  |
| MessagePack-CSharp, `[Key(n)]`, generated | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#1A7C00}\text{122}$ / $\color{#177F00}\text{194 ns}$ | $\color{#1D7900}\text{1.50}$ / $\color{#138300}\text{2.47 µs}$ |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#286E00}\text{137}$ / $\color{#237300}\text{232 ns}$ | $\color{#3D5900}\text{2.03}$ / $\color{#227400}\text{3.15 µs}$ |
| LsMsgPack, `MessagePackCSharp()` preset (MessagePack-CSharp's bytes) | $\color{#0A8C00}\text{51 B}$ / $\color{#0B8B00}\text{775 B}$ | $\color{#009600}\textbf{94}$ / $\color{#019500}\text{127 ns}$ | $\color{#009600}\textbf{1.03}$ / $\color{#029400}\text{1.66 µs}$ |
| LsMsgPack, no schema | $\color{#009600}\textbf{45 B}$ / $\color{#009600}\textbf{690 B}$ | $\color{#0A8C00}\text{105}$ / $\color{#009600}\textbf{123 ns}$ | $\color{#0B8B00}\text{1.21}$ / $\color{#009600}\textbf{1.57 µs}$ |
| the same, generated |  | $\color{#0A8C00}\text{105}$ / $\color{#029400}\text{130 ns}$ | $\color{#0B8B00}\text{1.21}$ / $\color{#039300}\text{1.69 µs}$ |
| LsMsgPack, schema reference | $\color{#1E7800}\text{63 B}$ / $\color{#029400}\text{708 B}$ | $\color{#1C7A00}\text{124}$ / $\color{#078F00}\text{146 ns}$ | $\color{#0E8800}\text{1.26}$ / $\color{#029400}\text{1.66 µs}$ |
| the same, generated |  | $\color{#1D7900}\text{125}$ / $\color{#098D00}\text{151 ns}$ | $\color{#0F8700}\text{1.27}$ / $\color{#049200}\text{1.76 µs}$ |
| LsMsgPack, default options (indexed schema) | $\color{#8F0700}\text{131 B}$ / $\color{#118500}\text{826 B}$ | $\color{#415500}\text{164}$ / $\color{#088E00}\text{147 ns}$ | $\color{#1E7800}\text{1.51}$ / $\color{#029400}\text{1.68 µs}$ |
| the same, generated |  | $\color{#3E5800}\text{161}$ / $\color{#098D00}\text{152 ns}$ | $\color{#1C7A00}\text{1.49}$ / $\color{#049200}\text{1.77 µs}$ |
| LsMsgPack, default options, `System.Numerics.Vector3` properties | $\color{#960000}\text{135 B}$ / $\color{#128400}\text{831 B}$ | $\color{#465000}\text{169}$ / $\color{#088E00}\text{147 ns}$ | $\color{#1E7800}\text{1.52}$ / $\color{#039300}\text{1.70 µs}$ |
| the same, generated |  | $\color{#415500}\text{164}$ / $\color{#0A8C00}\text{153 ns}$ | $\color{#1D7900}\text{1.50}$ / $\color{#059100}\text{1.79 µs}$ |
| LsMsgPack, default options, `[DataMember]` fields | $\color{#930300}\text{133 B}$ / $\color{#118500}\text{829 B}$ | $\color{#455100}\text{168}$ / $\color{#088E00}\text{148 ns}$ | $\color{#207600}\text{1.55}$ / $\color{#039300}\text{1.71 µs}$ |
| the same, generated |  | $\color{#3F5700}\text{162}$ / $\color{#0A8C00}\text{153 ns}$ | $\color{#1B7B00}\text{1.47}$ / $\color{#059100}\text{1.80 µs}$ |
| **Maps** |  |  |  |
| MessagePack-CSharp, `keyAsPropertyName`, generated | $\color{#821400}\text{123 B}$ / $\color{#960000}\text{1,890 B}$ | $\color{#3B5B00}\text{157}$ / $\color{#504600}\text{373 ns}$ | $\color{#316500}\text{1.83}$ / $\color{#4C4A00}\text{5.10 µs}$ |
| Nerdbank.MessagePack, defaults, generated shapes | $\color{#821400}\text{123 B}$ / $\color{#960000}\text{1,890 B}$ | $\color{#960000}\text{255}$ / $\color{#960000}\text{590 ns}$ | $\color{#960000}\text{3.47}$ / $\color{#960000}\text{8.50 µs}$ |
| LsMsgPack, property names | $\color{#6C2A00}\text{110 B}$ / $\color{#7F1700}\text{1,703 B}$ | $\color{#663000}\text{203}$ / $\color{#0B8B00}\text{158 ns}$ | $\color{#5F3700}\text{2.57}$ / $\color{#0B8B00}\text{2.06 µs}$ |
| the same, generated |  | $\color{#504600}\text{180}$ / $\color{#0D8900}\text{163 ns}$ | $\color{#4E4800}\text{2.30}$ / $\color{#0D8900}\text{2.17 µs}$ |
| LsMsgPack, property names, `[DataMember]` fields | $\color{#6C2A00}\text{110 B}$ / $\color{#7F1700}\text{1,703 B}$ | $\color{#603600}\text{197}$ / $\color{#0C8A00}\text{160 ns}$ | $\color{#5D3900}\text{2.54}$ / $\color{#0B8B00}\text{2.08 µs}$ |

- **Arrays:** LsMsgPack wrote the fastest, with or without generated code: its preset 94 ns per state and 1.03 µs per snapshot against MessagePack-CSharp's 122 ns and 1.50 µs. With the inline schema in every message (default options) LsMsgPack wrote states slower than MessagePack-CSharp (164 ns) and snapshots about as fast (1.51 µs). LsMsgPack read faster in every array setup, with or without generated code (123-153 ns per state against 194 ns).
- **Maps:** MessagePack-CSharp wrote the fastest, also against generated LsMsgPack (157 ns against 180 ns per state). LsMsgPack read 2.3-2.4x as fast (158 ns without and 163 ns with generated code, against 373 ns per state).
- **`Vector3` and fields:** with `System.Numerics.Vector3` (written by its public fields) or `[DataMember]` fields instead of properties, LsMsgPack wrote and read as fast as with the `Vec3` properties (within 3%). Their sizes differ only by the type names in the indexed schema. Keyed by property names, the fields are the same bytes as the properties.
- **Leave the inline schema out of small messages.** It writes the names in every message: 131 B per state against 45 B. Use arrays without a schema, or a schema reference to keep reading by name across versions.
- **Size:** LsMsgPack's arrays are the smallest because a zero is written as nil. As a side effect, `-0.0` reads back as `0.0`. The other two keep the sign.
- **Allocations:** writing to a reused buffer allocated 0 B in every MsgPack setup (an `ArrayBufferWriter<byte>`). Reading allocated the same in all of them (64 B per state, 1,168 B per snapshot). With `Vec3` as a class, LsMsgPack allocated more when reading (120 B and 2,008 B) and took up to 1.4x as long.
- **JSON** took 8-12x as long as MessagePack-CSharp, with payloads 3.7x as large.

## How it was measured

- **Versions:** MessagePack-CSharp 3.1.12, Nerdbank.MessagePack 1.4.104 (PolyType 1.4.1), and LsMsgPack built from this repository, November 2026.
- **Setups:** each library's fastest documented setup that the test covers (LsMsgPack's rows without generated code are measured after it compiled its writers and readers, see [the source generator](SourceGenerator.md)), with default (trusted data) security and no compression. The test checks every round trip, and that the `MessagePackCSharp()` preset writes MessagePack-CSharp's bytes.
- **Timing:** Intel i7-7700K, Linux, .NET 10.0.15, Release build. `Stopwatch` rather than BenchmarkDotNet, the fastest of 7 interleaved passes per run, and the tables have the medians of three runs. Between the runs most times varied by less than 10%, three invoice times by 11-16% (MessagePack-CSharp writing maps the most). All setups share one process and its JIT profile, so running each library alone can give different numbers (LsMsgPack's generated readers: see the invoices).
- **Not measured:** NativeAOT speed, the `IBufferWriter<byte>` and `Stream` overloads for invoices (the other two libraries are built around `IBufferWriter<byte>`), large binary data, deep nesting, polymorphic lists, `UntrustedData` security, and reading from a slice of a larger buffer.
- **Package size:** a minimal console app per library with one small class that writes and reads it, published framework-dependent in Release. The sizes are the dlls of the published folder, without the app's own. The NativeAOT row subtracts the size of an app without a serializer. The apps ran on .NET 8, .NET 10 and NativeAOT; the .NET Framework row counts the published files only. KB and MB are 1,000 and 1,000,000 bytes.

To run the benchmarks:

```bash
dotnet test LsMsgPackInteropTests/LsMsgPackInteropTests.csproj -c Release --filter "FullyQualifiedName~BenchmarkLibraries|FullyQualifiedName~BenchmarkGame" --logger "console;verbosity=detailed"
```
