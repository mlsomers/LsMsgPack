# LtMsgPack vs MessagePack-CSharp vs Nerdbank.MessagePack

All three serialize .NET classes to MsgPack. This page compares speed, payload size and features, and says when each one is the better choice. It's written by LtMsgPack's author. The benchmarks are in this repository (`LsMsgPackInteropTests/BenchmarkLibraries.cs` for business data, `BenchmarkGame.cs` for the small messages of a multiplayer game): run them yourself, and open an issue if one of the other libraries can be set up faster than it is here.

## When to pick which

**Pick LtMsgPack when** you want one or more of these:

- **Polymorphism without attributes.** A `List<Shape>` holding circles and squares, or a property declared as an interface or `object`, round trips out of the box: a type id is written where a value's type differs from the declared type. MessagePack-CSharp needs `[Union(key, typeof(Derived))]` on the base type for every derived class, and Nerdbank.MessagePack needs `[DerivedTypeShape]` attributes or a `DerivedTypeUnion` registered at runtime. Type ids let the data choose which classes are created, so add a [type guard](security.md) for data you don't trust.
- **A schema in the payload.** The indexed schema (the default) lists each type's property names once per message, then refers to them by index. Payloads stay small and still carry the names, so data written by older or newer versions of your classes loads by name. With the other two you pick one: arrays (compact, but the order of the `[Key]`s is the contract) or maps (with every name in every object). See [schema.md](schema.md).
- **A built-in debugger.** MsgPack Explorer (Windows, also as a Fiddler inspector and a Visual Studio debugger visualizer), the VS Code extension, and an [MCP server](Mcp.md) for AI agents. They show the types and property names from the schema and type ids, and the offset of any error. The other two can convert MsgPack to JSON in code (`ConvertToJson`), but don't ship such tools.
- **A TypeScript client for your ASP.NET backend.** [@lsmsgpack/client](TypeScriptClient.md) reads the schema-based responses of the [web formatters](WebFormatters.md) into complete objects, including polymorphic values and the properties the server left out. [lsmsgpack-ts](TypeScriptModels.md) generates the TypeScript models. With the other libraries you use a generic JavaScript MsgPack decoder and map the arrays or maps to your types yourself.
- **Plain classes, no setup, on a JIT.** On .NET and .NET Framework, LtMsgPack needs no attributes, no `partial` classes and no source generator. Without generated code it was faster than both other libraries with theirs in the invoice benchmark (below). Its source generator is optional there: it made writing 1.4-2x as fast here, reading about the same. Struct values are written and read in place, without allocations (see [game messages](#benchmark-game-messages)).
- Also: [ReadDifferences](ReadDifferences.md) (what didn't match between the data and your classes), [streaming](Streaming.md) with an index for seeking, [ADO.NET data readers](DataReader.md), and [presets](Compatibility.md#ltmsgpack-presets) that write the same bytes as MessagePack-CSharp, or what Nerdbank.MessagePack reads.

**Pick MessagePack-CSharp when** you need a library with a long track record: it is widely used in production (ASP.NET Core SignalR's MessagePack protocol is built on it), supports Unity, and has LZ4 compression and typeless serialization.

**Pick Nerdbank.MessagePack when** NativeAOT and trimming are the main target and you like PolyType's shape model. It also has async serialization straight to and from pipes and streams, preserving object references, structural equality, and JSON schemas for your types. It's by one of MessagePack-CSharp's two main contributors.

**Don't pick LtMsgPack (yet) when:**

- **You need a track record.** LtMsgPack has no known production users yet, and until it does, breaking changes to its API and defaults are still allowed. Pin the version, and check the release notes before you upgrade.
- **You target NativeAOT, WebAssembly AOT or iOS but don't want a source generator.** LtMsgPack needs its generator there: one context class or one assembly attribute, which can pick up classes you already marked `[Serializable]`, `[DataContract]` or `[JsonSerializable]` (see [SourceGenerator.md](SourceGenerator.md)). Without it, LtMsgPack fails under NativeAOT on the first object. The NativeAOT speed of the three libraries was not compared.
- **The other side expects another library's polymorphism format.** MessagePack-CSharp's and Nerdbank.MessagePack's unions (an array of a key and the object) can't be read or written by LtMsgPack. Most other data works with the right settings (see [Compatibility.md](Compatibility.md) and its list of what [cannot match](Compatibility.md#what-settings-cannot-match)).
- **Your structs have fields instead of properties.** Vector types with public fields and no setters (`System.Numerics.Vector3`) are refused with an exception. The other two libraries write their fields. Structs with properties are fine, also under NativeAOT with the source generator (see [game messages](#benchmark-game-messages)).
- **You need Unity**: LtMsgPack is not tested there.

## Benchmark

The model is the 100 invoices of the repository's other benchmarks: strings, decimals, `DateTime`s, a `Guid`, enums, a nested customer with two addresses and 3-24 lines each. The time is for 20 rounds over all 100 invoices: `Serialize` to a `byte[]`, and `Deserialize` from one. Speed is relative to System.Text.Json with its default options, the baseline of this repository. "One address" is the cost of a single call with a small object (6 strings).

Every library runs in its fastest documented setup:

- **MessagePack-CSharp 3.1.10:** `[MessagePackObject]` classes with the formatters of its source generator (`GeneratedMessagePackResolver`). Integer keys (`[Key(n)]`, arrays, its recommendation) or `keyAsPropertyName: true` (maps). "Native" adds `NativeGuidResolver` and `NativeDecimalResolver`, which write binary instead of strings, as LtMsgPack does. Default (trusted data) security, no compression.
- **Nerdbank.MessagePack 1.3.88** (PolyType 1.4.1): shapes from PolyType's source generator (`[GenerateShapeFor<Invoice>]`). Arrays with `PerfOverSchemaStability = true`, maps with the defaults.
- **LtMsgPack:** the plain classes without attributes, with the plans it builds at runtime and with the code of its source generator ("generated").

Before timing, the test checks that each setup reads back every invoice unchanged. It also checks that LtMsgPack's `MessagePackCSharp()` preset writes exactly MessagePack-CSharp's bytes (both the arrays and the maps), so those rows compare the same output.

### Objects as arrays

| Setup | Size (100 invoices) | Write | Read | One address, write / read |
|---|---|---|---|---|
| System.Text.Json (baseline) | 330.6 KB (100%) | 23.2 ms | 41.4 ms | 420 / 661 ns |
| MessagePack-CSharp, `[Key(n)]`, generated | 107.4 KB (32%) | 15.2 ms (1.5x) | 22.0 ms (1.9x) | 276 / 473 ns |
| MessagePack-CSharp, `[Key(n)]`, generated, native Guid and decimal | 200.6 KB (61%) | 10.0 ms (2.3x) | 18.2 ms (2.3x) | 286 / 478 ns |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | 200.6 KB (61%) | 9.2 ms (2.5x) | 23.6 ms (1.8x) | 235 / 533 ns |
| LtMsgPack, `MessagePackCSharp()` preset as arrays (the bytes of MessagePack-CSharp's row) | 107.4 KB (32%) | 6.0 ms (3.9x) | 11.3 ms (3.7x) | 127 / 230 ns |
| LtMsgPack, arrays without the schema | 174.4 KB (53%) | 5.4 ms (4.3x) | 7.0 ms (5.9x) | 154 / 247 ns |
| the same, generated | | 2.7 ms (8.6x) | 6.0 ms (6.9x) | 123 / 230 ns |
| LtMsgPack, default options (arrays with the indexed schema) | 214.7 KB (65%) | 6.0 ms (3.9x) | 7.2 ms (5.8x) | 204 / 265 ns |
| the same, generated | | 3.3 ms (7.0x) | 6.0 ms (6.9x) | 163 / 249 ns |

### Objects as maps keyed by property names

| Setup | Size (100 invoices) | Write | Read | One address, write / read |
|---|---|---|---|---|
| MessagePack-CSharp, `keyAsPropertyName`, generated | 278.9 KB (84%) | 18.2 ms (1.3x) | 28.8 ms (1.4x) | 321 / 586 ns |
| Nerdbank.MessagePack, defaults, generated shapes | 372.1 KB (113%) | 12.3 ms (1.9x) | 40.3 ms (1.0x) | 310 / 828 ns |
| LtMsgPack, `MessagePackCSharp()` preset (the bytes of MessagePack-CSharp's row) | 278.9 KB (84%) | 9.8 ms (2.4x) | 13.1 ms (3.2x) | 225 / 294 ns |
| LtMsgPack, `Nerdbank()` preset | 372.1 KB (113%) | 8.1 ms (2.9x) | 10.5 ms (4.0x) | 207 / 300 ns |
| LtMsgPack, property names | 314.2 KB (95%) | 8.2 ms (2.8x) | 9.3 ms (4.5x) | 216 / 288 ns |
| the same, generated | | 6.0 ms (3.9x) | 10.4 ms (4.0x) | 189 / 315 ns |
| LtMsgPack, maps with the indexed schema | 226.5 KB (69%) | 7.0 ms (3.3x) | 7.9 ms (5.3x) | 225 / 275 ns |

### Plain classes without attributes or generated code

| Setup | Size (100 invoices) | Write | Read | One address, write / read |
|---|---|---|---|---|
| MessagePack-CSharp, contractless (code emitted at runtime) | 278.9 KB (84%) | 16.6 ms (1.4x) | 26.9 ms (1.5x) | 316 / 565 ns |
| Nerdbank.MessagePack, PolyType's reflection provider | 372.1 KB (113%) | 14.9 ms (1.6x) | 42.0 ms (1.0x) | 315 / 795 ns |
| LtMsgPack | the rows above without "generated" | | | |

### Reading the numbers

- **Payload size depends mostly on how decimals and Guids are written.** MessagePack-CSharp's defaults write them as strings: `"123.45"` takes 7 bytes, the binary decimal of the "native" rows 18. That makes its default arrays the smallest payload here (32%). LtMsgPack writes the same with `DecimalFormat.String` (its preset rows). LtMsgPack's own arrays are smaller than the native rows (53% against 61%) because it writes a zero decimal as nil (1 byte). The indexed schema adds the property names once per payload: 65% with arrays, 69% with maps.
- **Same bytes, different speed.** Writing MessagePack-CSharp's exact bytes, the preset was 2.5x (arrays) and 1.9x (maps) as fast as MessagePack-CSharp at writing, and 1.9x and 2.2x as fast at reading. The preset is slower than LtMsgPack's own format (reading 11.3 ms against 7.0 ms for the arrays) because decimals and Guids are text there.
- **Allocations** were the same for all MsgPack setups: about the payload size when writing, and the objects themselves (4.5 KB per invoice) when reading.
- **Nerdbank.MessagePack** wrote arrays about as fast as MessagePack-CSharp with the same binary Guids and decimals (within the noise between runs), and maps faster. It read maps about as fast as System.Text.Json, and arrays a bit slower than MessagePack-CSharp.

### How it was measured

- One machine (Intel i7-7700K, Linux, .NET 8.0.33 with tiered PGO, Release build), November 2026. Each setup was warmed up for 0.5 s, then the fastest of 7 interleaved passes was kept. Between runs, most rows varied by less than 5%, a few LtMsgPack map rows by up to 15%.
- A standalone check, with each library in its own process looping for 8 s, gave the same picture: MessagePack-CSharp with `[Key(n)]` and native Guid and decimal took 8.5 ms to write and 17.8 ms to read, Nerdbank.MessagePack 8.1 / 23.3 ms, LtMsgPack arrays without the schema 4.5 / 6.3 ms, and the preset 5.9 / 8.5 ms. MessagePack-CSharp's default (string decimals) wrote in 12.0 ms there, faster than in the test (15-16 ms), so its first row may be on the slow side.
- The benchmark uses `Stopwatch`, not BenchmarkDotNet, and `byte[]` in and out for every library. The `IBufferWriter<byte>` and `Stream` overloads, which the other two libraries are built around, were not measured. Neither were other shapes of data (large binary data, deep nesting, polymorphic lists; small messages of numbers are the [game messages](#benchmark-game-messages)), NativeAOT, or MessagePack-CSharp's `UntrustedData` security mode.

To run it:

```bash
dotnet test LsMsgPackInteropTests/LsMsgPackInteropTests.csproj -c Release --filter "FullyQualifiedName~BenchmarkLibraries" --logger "console;verbosity=detailed"
```

## Benchmark: game messages

A multiplayer game with 16 players. Each tick, a client sends its own state and receives a snapshot of the other 15 players. Lots of small messages, so the cost per call and the size of every message count. The model:

```csharp
public struct Vec3 { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }

public class PlayerState // what a client sends: 8 values, 14 numbers
{
  public int PlayerId { get; set; }
  public uint Sequence { get; set; }
  public Vec3 Position { get; set; }
  public Vec3 Velocity { get; set; }
  public float Yaw { get; set; }
  public float Pitch { get; set; }
  public PlayerAction Action { get; set; } // enum
  public byte Health { get; set; }
}

public class WorldSnapshot // what it receives
{
  public uint Tick { get; set; }
  public uint LastProcessedSequence { get; set; }
  public List<PlayerState> Players { get; set; } // the 15 others
}
```

The data is 64 simulated ticks: arbitrary float coordinates, about 1 in 7 players standing still (velocity 0, 0, 0). The times are per message. "Write" is `Serialize` to a new `byte[]`. "Send" writes to a reused buffer, as a game loop would to keep the garbage collector quiet: an `ArrayBufferWriter<byte>` for the other libraries, a `MemoryStream` for LtMsgPack (it has no `IBufferWriter<byte>` overload). "Read" is `Deserialize` from a `byte[]`. The setups are those of the invoice benchmark. The schema reference rows hand the writer's schemas to the reader's `SchemaStore` once, as a handshake would when connecting (`SchemaStore.Export` / `Import`). The test checks the round trip of every message, and that the `MessagePackCSharp()` preset writes MessagePack-CSharp's bytes.

### With `Vec3` as a struct

| Setup | Size: state / snapshot | State: write / read / send | Snapshot: write / read / send |
|---|---|---|---|
| System.Text.Json (baseline) | 190 B / 2,928 B | 1,459 / 1,532 / 1,442 ns | 20.7 / 22.3 / 21.0 µs |
| MessagePack-CSharp, `[Key(n)]`, generated | 51 B / 775 B | 123 / 195 / 106 ns | 1.50 / 2.46 / 1.34 µs |
| Nerdbank.MessagePack, `PerfOverSchemaStability`, generated shapes | 51 B / 775 B | 143 / 242 / 127 ns | 2.16 / 3.23 / 1.66 µs |
| LtMsgPack, `MessagePackCSharp()` preset as arrays | 51 B / 775 B | 149 / 161 / 141 ns | 1.74 / 2.34 / 1.63 µs |
| LtMsgPack, arrays without the schema | 45 B / 690 B | 179 / 157 / 172 ns | 2.26 / 2.25 / 2.15 µs |
| the same, generated | | 111 / 133 / 106 ns | 1.21 / 1.87 / 1.10 µs |
| LtMsgPack, schema reference (arrays) | 63 B / 708 B | 202 / 182 / 199 ns | 2.29 / 2.40 / 2.18 µs |
| the same, generated | | 131 / 157 / 131 ns | 1.30 / 1.97 / 1.21 µs |
| LtMsgPack, default options (arrays with the indexed schema) | 131 B / 826 B | 242 / 183 / 230 ns | 2.51 / 2.42 / 2.42 µs |
| the same, generated | | 170 / 158 / 159 ns | 1.57 / 1.95 / 1.44 µs |
| MessagePack-CSharp, `keyAsPropertyName` (maps), generated | 123 B / 1,890 B | 167 / 382 / 140 ns | 1.95 / 5.34 / 1.66 µs |
| Nerdbank.MessagePack, maps, generated shapes | 123 B / 1,890 B | 277 / 622 / 243 ns | 3.78 / 9.03 / 3.46 µs |
| LtMsgPack, property names (maps) | 110 B / 1,703 B | 282 / 263 / 267 ns | 3.74 / 3.75 / 3.49 µs |
| the same, generated | | 227 / 226 / 214 ns | 2.98 / 3.28 / 2.75 µs |

### LtMsgPack with `Vec3` as a class

The same data, with `class Vec3` for LtMsgPack (the other libraries keep the struct):

| Setup | Size: state / snapshot | State: write / read / send | Snapshot: write / read / send |
|---|---|---|---|
| LtMsgPack, `MessagePackCSharp()` preset as arrays (still MessagePack-CSharp's bytes) | 51 B / 775 B | 193 / 212 / 187 ns | 2.50 / 3.06 / 2.37 µs |
| LtMsgPack, arrays without the schema | 46 B / 699 B | 202 / 196 / 193 ns | 2.55 / 2.81 / 2.45 µs |
| the same, generated | | 128 / 160 / 123 ns | 1.53 / 2.27 / 1.44 µs |
| LtMsgPack, schema reference (arrays), generated | 64 B / 717 B | 148 / 185 / 149 ns | 1.58 / 2.38 / 1.47 µs |
| LtMsgPack, default options | 134 B / 838 B | 261 / 219 / 250 ns | 2.88 / 2.88 / 2.72 µs |
| the same, generated | | 186 / 187 / 176 ns | 1.81 / 2.40 / 1.70 µs |

### Allocated per message

| Setup | State: write / read / send | Snapshot: write / read / send |
|---|---|---|
| System.Text.Json | 594 / 600 / 376 B | 4,227 / 2,752 / 1,272 B |
| MessagePack-CSharp and Nerdbank.MessagePack, arrays | 80 / 64 / 0 B | 800 / 1,168 / 0 B |
| LtMsgPack, arrays without the schema, `Vec3` struct | 72 / 64 / 0 B | 718 / 1,168 / 0 B |
| LtMsgPack, default options, `Vec3` struct | 160 / 64 / 0 B | 854 / 1,168 / 0 B |
| LtMsgPack, arrays without the schema, `Vec3` class | 72 / 120 / 0 B | 726 / 2,008 / 0 B |
| LtMsgPack, default options, `Vec3` class | 160 / 120 / 0 B | 866 / 2,008 / 0 B |

Reading allocates the objects. With a class, those are two more `Vec3` objects per player. With the struct, LtMsgPack writes and reads the `Vec3` values in place, so it allocates what the other two do. Writing to a new `byte[]` allocates the payload. The generated rows allocate the same as the runtime ones.

### Findings

- **LtMsgPack writes and reads struct values in place.** It passes a struct by reference and reads and sets its properties through typed delegates. So `Vec3` values allocate nothing: written into a reused buffer, states and snapshots allocated 0 B, as with MessagePack-CSharp and Nerdbank.MessagePack. Without generated code the struct was also faster than the class: arrays without the schema wrote a state in 179 ns and read it in 157 ns, against 202 / 196 ns with `Vec3` as a class. A struct as the root value (`Serialize<Vec3>`, `Deserialize<Vec3>`) is still boxed once per call.
- **The source generator covers structs.** Its code takes the struct by reference too, and writes and reads the `Vec3` values inline. With generated code, struct vectors were faster than class vectors: arrays without the schema wrote a state in 111 ns (class vectors 128 ns) and read it in 133 ns (160 ns), wrote a snapshot in 1.21 µs (1.53 µs) and read it in 1.87 µs (2.27 µs). Without dynamic code (NativeAOT) the generated accessors and default checks keep struct values in place too: the repository's NativeAOT smoke test writes its structs into a reused stream with nothing allocated. The speed under NativeAOT was not measured.
- **With generated code, LtMsgPack was the fastest here.** Arrays without the schema, on the struct model, wrote a state in 111 ns and read it in 133 ns (MessagePack-CSharp: 123 / 195 ns). Snapshots were written 19% faster (1.21 µs against 1.50 µs, sending 1.10 µs against 1.34 µs) and read 24% faster (1.87 µs against 2.46 µs). Without generated code, LtMsgPack wrote states 1.45x slower and read them faster (179 / 157 ns), and wrote snapshots 1.5x slower and read them 1.09x faster. With property names, MessagePack-CSharp wrote faster, also against generated code (states 167 ns against 227 ns, snapshots 1.95 µs against 2.98 µs), and LtMsgPack read faster, also without it (states 263 ns against 382 ns, snapshots 3.75 µs against 5.34 µs).
- **Vector types with fields.** `System.Numerics.Vector3` has public fields and no setters. MessagePack-CSharp (contractless) and Nerdbank.MessagePack write the fields. LtMsgPack refuses it with a `MsgPackException`, so it doesn't silently write an empty map. Use your own type with properties, or a custom extension (`LtMsgPackOptions.Extensions`). Unity's `Vector3` was not tested.
- **Leave the inline schema out of small messages.** The indexed schema (the default) writes the property names in every message: 131 B per state, against 45 B without it. Snapshots grow less (826 B against 690 B, +20%) because they hold the names once for 15 players. When both sides have the same classes, use arrays without the schema. Use a schema reference (`WriteSchemaReference` with a `SchemaStore`, 18 B per message) to keep reading by name across versions, with the schemas sent once when connecting.
- **LtMsgPack's arrays are the smallest:** 45 B against MessagePack-CSharp's 51 B per state. A zero (the velocity of a player standing still) is a default value, written as nil (1 byte) instead of a 5 byte float. A side effect: `-0.0` is a default value too, so it reads back as `0.0`. The other two libraries keep the sign.
- **Writing to a new `byte[]` costs little more than sending.** The difference was up to 20% in most setups, the copy of the payload, plus its allocation (the payload size + 24 B).
- **Per tick.** A server that reads 16 states and sends 16 snapshots each tick spent ~25 µs on it with MessagePack-CSharp, ~20 µs with generated LtMsgPack on the struct model and ~26 µs on classes. MessagePack-CSharp and LtMsgPack on the struct model both made ~1 KB of garbage per tick: the 16 states they read. A client sends one state and reads one snapshot per tick: 2-10 µs for any of the MsgPack setups, a small part of a 16.7 ms frame.
- **JSON** took 8-16x as long as MessagePack-CSharp, and its payloads were 3.7x larger.
- **Nerdbank.MessagePack** took 1.2x as long as MessagePack-CSharp for states and 1.2-1.4x for snapshots, with the same bytes and allocations.

### How it was measured

The machine and runtime were the same as for the invoices. Each operation was warmed up for 0.5 s per setup. A pass ran 200,000 states and 20,000 snapshots per operation, cycling through the 1,024 states and 64 snapshots of the data. The fastest of 7 interleaved passes was kept. The tables have the medians of three runs. Between the runs most values differed by less than 5%, some by up to 17%. Not measured: reading from a slice of a larger receive buffer (`Deserialize(byte[], offset, count)`, `ReadOnlyMemory<byte>`), reusing objects when reading, larger matches, delta compression, NativeAOT and Unity.

To run it:

```bash
dotnet test LsMsgPackInteropTests/LsMsgPackInteropTests.csproj -c Release --filter "FullyQualifiedName~BenchmarkGame" --logger "console;verbosity=detailed"
```
