# LsMsgPack

MsgPack serializer for .NET classes (like the xml and json serializers) with an optional indexed schema and type ids for polymorphic object models, plus a MsgPack explorer/debugging tool (Windows). The library is `LsMsgPackNetStandard/` (assembly and package `LsMsgPack`). Not published as a NuGet package yet, so format changes are still acceptable when they are deliberate and called out.

## Build and test

```bash
dotnet build LsMsgPack.slnf -c DEBUG                  # what CI does (.github/workflows/dotnet.yml), cross-platform projects only
dotnet test LsMsgPack.slnf --no-build -c DEBUG
dotnet test LsMsgPackNetStandardUnitTests/LsMsgPackUnitTests.csproj -c Release --filter "TestCategory!=Benchmark"
dotnet test LsMsgPackNetStandardUnitTests/LsMsgPackUnitTests.csproj -c DebugKeepTrack --filter "TestCategory!=Benchmark"
```

- `LsMsgPack.sln` also has Windows-only projects (MsgPackExplorer, Fiddler inspector, Visual Studio plugin: old-style .NET Framework projects). They reference the library project but cannot be built on Linux, mention it when a change could affect them (e.g. target frameworks, public API).
- Configurations: `Debug`, `Release`, `DebugKeepTrack`, `ReleaseKeepTrack`. KeepTrack defines `KEEPTRACK`: items remember offsets and lengths, errors become `MpError` items instead of exceptions (`ContinueProcessingOnBreakingError`), and `PreservePackages` keeps the item trees. The explorer tools use it. Code in `#if KEEPTRACK` blocks must keep compiling, so build a KeepTrack configuration after touching `MsgPackItem` or the `Types/`.
- The benchmark tests (`TestCategory=Benchmark`, `BenchmarkInvoices`) compare with Json.NET and report through `TestContext`: run them with `--logger "console;verbosity=detailed"`. They are Inconclusive under KEEPTRACK.
- `MicroFramework/` has its own old copies of the sources, it does not link the library files.
- The version comes from `CommonAssemblyInfo.cs` (read by `Packaging.props`).

## Language and targets

- The library targets `netstandard2.0;netstandard2.1`. 2.1 only exists to ask `RuntimeFeature.IsDynamicCodeCompiled` (`#if NETSTANDARD2_1_OR_GREATER`), .NET Framework picks 2.0, .NET (Core) picks 2.1.
- netstandard2.0 compiles as **C# 7.3**: no `??=`, switch expressions, `is not`, ranges, `using` declarations or nullable annotations. `Dictionary.TryAdd` comes from `Meta/MissingExtensions.cs`.
- Style: 2-space indentation, `_camelCase` fields, explicit types rather than `var`, comments explain why. Some files use a different brace style, match the file. Most files are UTF-8 with BOM, keep it when editing with scripts (e.g. Python `encoding='utf-8-sig'`).

## How it works

- **Two phases.** Writing: object → `MsgPackItem` tree (`MsgPackSerilaizer_Pack.cs`, note the typo in the name: `SerializeObject`, `SerializeCollection`, `AddProperties`) → bytes (`MsgPackItem.WriteTo(ByteWriter)`, containers write their items into the same buffer). Reading: bytes → `MsgPackItem` tree (`MsgPackItem.Unpack`, each `Types/Mp*.Read`) → plain values (`object[]` for arrays, `KeyValuePair<object, object>[]` for maps) → objects (`MsgPackSerializer_Unpack.cs`: `ConvertDeserializeValue`, `ConvertMap`, `SetProperties`).
- **Items** (`Types/`): `MpInt` (keeps the original integer type, `DynamicallyCompact` picks the smallest encoding), `MpString`, `MpBin` (also `Guid`), `MpFloat`, `MpBool`, `MpNull`, `MpArray`, `MpMap`, `MpExt` → `MpDateTime` (type -1, timestamps) and custom extensions via `AbstractCustomExt<TSelf>` (`MpDecimal`, type 1, registered in `MsgPackSettings.CustomExtentionTypes`), `MpRoot` (several items in sequence), `MpError` (KEEPTRACK).
- **Objects** are maps from property id to value. The property id is the name, or what an `IMsgPackPropertyIdResolver` returns (`PropertyNameResolvers`), or with the indexed schema the index of the name in the schema. Filters: static ones decide per property (`FilterNonSettable`, `FilterIgnoredAttribute`), dynamic ones per value (`FilterDefaultValues`: default values are omitted).
- **Reserved map keys**: `""` holds the type id and `"@"` the content of a wrapped value (`MsgPackSerializer.TypeIdKey` / `ContentKey`). A collection is written as an array (a dictionary as a map), and only wrapped in a map when it needs a type id or properties (`SerializeEnumerableAttribute.SerializeProperties`).
- **Type ids** are added when the value's type differs from the declared type (`AddTypeIdOption.IfAmbiguious`, default). They are short type names by default (`AddTypeIdOption.FullName` for full names), and resolved again by name when reading (`Meta/TypeResolver.cs`).
- **Indexed schema** (`UseInexedSchema`, on by default): each `Serialize`/`Deserialize` call is a **session** with its own `IndexedSchemaTypeResolver` and a clone of the settings (`MsgPackSerializer.WithSchema`). The payload is a map `{ typeName: [propertyName, ...] }` followed by the body, in which property keys and type ids are indexes into that map. The resolver is both a type resolver and a property id resolver. Resolvers are consulted from **last to first**: the schema is added last to the type resolvers (asked first) and first to the property id resolvers (asked last, so custom property ids win). Readers look property ids up by name in the schema of the payload, so schema contents may change between versions.
- **Resolving the properties of a type** (`FullPropertyInfo.GetSerializedProps`): in a session, `GetStaticallyIncludedProps` creates the properties once and runs the static filters (with `PropertyId` = name), then the ids are resolved for the kept ones. `IndexedSchemaTypeResolver.GetComplex` takes the names from the same list. Without a session (no schema) the old order is kept: the id is resolved first, then the static filters.

## Caches and their scope

| Cache | Key | Scope |
|---|---|---|
| `FullPropertyInfo.Cache` | PropertyInfo | global, only without property id resolvers. `StaticallyIgnored` is cached on these shared instances, so the static filters of the first settings win |
| `FullPropertyInfo.SerializedPropsCache` | Type | global, only without property id resolvers and without a session (follows from the above) |
| `FullPropertyInfo.AttributesCache`, `GetProperties` cache | PropertyInfo / Type | global, static metadata |
| `MsgPackSettings._serializedPropsCache`, `_staticPropsCache` | Type | one session (only set by `WithSchema`, not copied by `Clone()`). Property ids are schema indexes, never share them between sessions |
| `PropertyAccessor.Cache` | PropertyInfo | global. Reflection for the first 100 calls, then typed delegates (`Delegate.CreateDelegate` into `TypedAccessor<TTarget, TValue>`), unless the runtime does not compile code or `MsgPackSettings.CompilePropertyAccessors` is off |
| `CollectionInfo`, `HasParameterlessConstructor`, `FilterDefaultValues.DefaultInstances` | Type | global |
| `TypeResolver` name caches | name / Type | global, guarded by one (re-entrant) lock |

Custom property id resolvers without the schema are consulted on every call (by design, they may have their own cache).

## Invariants and gotchas

- **Keep the output stable.** Performance changes must not change the bytes or the round trip results: verify with the `lsmsgpack-perf` skill (A/B equivalence and benchmark against master).
- **Leaf items write themselves** through `WriteTo` only when `GetType() == typeof(ThatType)`, because a subclass (e.g. a user's custom extension) may override `ToBytes`, which must still be used. `ToBytes` and `WriteTo` share one private writer per type, so they cannot drift apart.
- **Property ids must be unique per type** and must not be `""` or `"@"`: `FullPropertyInfo.ThrowIfIdsNotUnique` checks this once per type, the map entries are then collected in arrays without checks.
- **Exceptions of property getters/setters** are rethrown as they are (not as `TargetInvocationException`), whether the accessor still uses reflection or a bound delegate.
- The parameterless `MsgPackItem()` constructor allocates a new `MsgPackSettings` (50-70 ns). Use the constructors taking the settings in hot paths (see `AbstractCustomExt.CreateNew`).
- **Culture-sensitive string APIs are slow on Linux** (ICU): always pass `StringComparison.Ordinal` (`EndsWith`, `StartsWith`, `IndexOf(string)`, `Compare`).
- Reading from a `Stream`: use `ReadExactly` (`MsgPackItem`), `Stream.Read` may return fewer bytes than asked (network streams).
- Endianness: MsgPack is big-endian, `EndianAction` can override it. `ByteWriter.WriteEndian` writes the same bytes as `BitConverter.GetBytes` followed by `SwapEndianChoice`.
- `DateTime` values are returned as local time (`MpDateTime.Value` calls `ToLocalTime`), MsgPack timestamps do not keep the `DateTimeKind`: compare with `ToUniversalTime()` in tests. `DateTimeOffset` is written as its UTC time, the offset is lost. Timestamps before 1970 round the seconds down, the nanoseconds are always added (`MpDateTime.DateTimeToEpoch`), as the spec requires.
- Reading maps uses `MapConversionEqualityComparer`: numbers are equal across types (`(byte)3` equals the property id `(int)3`), strings and integers take a fast path.
- **Type names are short by default**, so the classes of an assembly that is searched for types need unique names: two test classes both called `Assorted` (nested in different test classes) make deserializing either of them throw "Type assignment dilamma". Give test classes unique names.
- `Deserialize<List<T>>` only registers the assembly of `List<T>`. When `T` lives elsewhere (and is not reachable from the root type), call `MsgPackSerializer.CacheAssemblyTypes(typeof(T))` first.
- Known issue: `Type.GetProperties()` without binding flags also returns **public static properties**, so settable static properties are serialized (and set again when deserializing). Not fixed yet, changing it changes the output of such types.

## Performance

State after the optimization sessions (the `BenchmarkInvoices` test: 100 invoices × 20 rounds, Release, speed relative to Json.NET): indexed schema write ~0.5x and read ~0.57x, property names write ~0.66x and read ~0.67x (it started at 0.17x/0.26x and 0.26x/0.52x). The payload is 67% (indexed) or 93% (names) of Json.NET's.

Ideas not done yet:
- Caching the schema between sessions (planned by the maintainer): most of the remaining indexed-vs-names difference is per session work (~10 µs for a 3-type object).
- Reading: `ConvertMap` builds a dictionary per object to look up property ids, with the schema the keys are indexes that could address an array directly. Every value still becomes an `MsgPackItem` and then a boxed value.
- A source generator that writes (de)serializers at compile time (like System.Text.Json source generation / sgen.exe for xml).

Profiling on Linux: see the `lsmsgpack-perf` skill (`dotnet-trace` stacks were unreliable, `perf` needs the user to lower `kernel.perf_event_paranoid`).

## Working with the maintainer

- Create branches from `origin/master` with `--no-track` (`git switch --no-track -c <branch> origin/master`), then check `git status -sb` shows no upstream. With a tracking branch the maintainer's push landed on master.
- Commit, push and open pull requests only when asked. Commit messages: a short subject, a wrapped body explaining what and why, then the attribution line the harness provides.
- Report measured numbers (A/B, same session) and state behavior changes explicitly, the maintainer decides on format and behavior changes.
