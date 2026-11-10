# The source generator of LtMsgPack

LtMsgPack builds a plan per class the first time it meets the class: typed property accessors, generic handlers and readers, created with reflection. The source generator writes that code at compile time instead, for the classes you name:

- **NativeAOT, WebAssembly AOT (Blazor), iOS:** code that can't be generated at runtime. Without the generator, LtMsgPack fails under NativeAOT on the first object. With it, the generated code runs compiled where the runtime plans would be interpreted.
- **Trimming:** the generated code refers to your classes and their properties, so the trimmer keeps them (.NET 5 and later). A serializer created with the context has no trim or AOT warnings (see [Trimming and NativeAOT](#trimming-and-nativeaot)).
- **Speed:** with a JIT, LtMsgPack compiles the same writers and readers at runtime once a type has been written or read 1000 times (`LtMsgPackOptions.CompileWritersAfter`, `CompileReadersAfter`; 0 compiles at the first object, -1 never), after which both are about as fast. So the generated code gains on the first objects of each type, and reads maps keyed by property names about 1.3x as fast (those readers aren't compiled at runtime). Under WebAssembly AOT writing is 7-8x and reading about 3x as fast (measurements below).

The data doesn't change: the generated code writes the same bytes and reads the same values as LtMsgPack does without it, with the same options. Anything it doesn't cover is done by the runtime as before, so you can switch it on and off without touching stored data or peers.

The generator ships in the LtMsgPack package. It needs an SDK-style project (`PackageReference`): projects with `packages.config` don't run source generators.

## Telling it which classes

### A context class

```csharp
using LtMsgPack;

[LtMsgPackSerializable(typeof(Invoice))]
[LtMsgPackSerializable(typeof(Circle))] // subclasses only met through type ids at runtime
[LtMsgPackSerializable(typeof(Square))]
public partial class AppMsgPackContext : LtMsgPackContext { }
```

The generator writes the rest of the class: a parameterless constructor and a static `AppMsgPackContext.Default`. The types a root reaches are included: the types of its properties, the elements of lists, arrays and dictionaries, base classes, the underlying types of nullables.

Types that are only known at runtime need their own attribute: implementations assigned to an `object` or interface property, or subclasses of a declared base class.

### Without a context class: the classes you already marked

To use the generator without marking anything again, let it take the classes from markers they already have. In the project file:

```xml
<PropertyGroup>
  <LtMsgPackGenerate>Serializable;DataContract;JsonSerializable</LtMsgPackGenerate>
</PropertyGroup>
```

or in code:

```csharp
[assembly: LtMsgPackGenerate(Include = LtMsgPackInclude.Serializable | LtMsgPackInclude.DataContract | LtMsgPackInclude.JsonSerializable)]
```

| `Include` | The roots |
|---|---|
| `AllTypes` | Every class and struct of the assembly with public properties, that the generated code can access (public and internal) |
| `Serializable` | Classes and structs marked `[Serializable]` (as for the XmlSerializer's sgen and the binary formatters), not exceptions |
| `DataContract` | Classes and structs marked `[DataContract]` |
| `JsonSerializable` | The types of System.Text.Json's source generation: every `[JsonSerializable(typeof(...))]` in the assembly |

These go into one generated context per assembly: `GeneratedMsgPackContext` in the project's root namespace. Name it yourself with `<LtMsgPackContextName>MyApp.Serialization.MsgPackContext</LtMsgPackContextName>` or `ContextName`. The assembly attribute wins over the MSBuild properties.

A marker only selects the classes. Which properties are written, and under which ids, is still decided by the options: `[IgnoreDataMember]` and `[JsonIgnore]` through the default static filters, `[DataMember(Order = n)]` with `PropertyOrder.Explicit`. The generated code follows whatever the options decide.

### Non-public properties

The runtime serializes [non-public properties](schema.md#non-public-properties) when `FilterNonSettable` is given a `NonPublicProperties`. The generator covers the ones with an opt-in attribute (`[JsonInclude]`, `[DataMember]`...) by default, `All` covers every non-public property (which only adds code: the options still decide what is serialized):

```xml
<LtMsgPackNonPublicProperties>All</LtMsgPackNonPublicProperties> <!-- None, Attributed (the default) or All -->
```

or `[assembly: LtMsgPackGenerate(NonPublicProperties = NonPublicProperties.All)]`. The generated code calls the getters and setters it can't access through `[UnsafeAccessor]` (.NET 8 and later), not for generic or virtual properties. Without it (.NET Standard, .NET Framework) the runtime uses reflection for them (info `LTMP004` for the setters). For trimming and NativeAOT, the classes with such properties keep their non-public properties (`DynamicDependency`).

### Fields

The generated code covers the [fields](schema.md#fields) the runtime serializes (`SerializedFields` of `FilterNonSettable`), written after the properties like the runtime does. The mode of the generator is `Attributed` unless you set it: fields with an opt-in attribute (`[DataMember]`, `[JsonInclude]`...), public or not. It must be the mode of the runtime's static filters, otherwise the generated code describes other fields than the runtime writes (the [analyzers](Analyzers.md) read the same setting):

```xml
<LsMsgPackFields>Public</LsMsgPackFields> <!-- None, Attributed (the default), Public or All -->
```

or `[assembly: LtMsgPackGenerate(Fields = SerializedFields.Public)]`, which wins over the property. The property is declared by LsMsgPack.Core's build props, which the LtMsgPack package brings along.

- Private fields are read and set through `[UnsafeAccessor]` (.NET 8 and later), not for fields of generic classes: reflection (the runtime's, as for non-public properties). A `readonly` field has no setter: it is written, not set (see [schema.md](schema.md#fields)).
- Framework structs whose state is public fields (`Vector3`, `Quaternion`, `Matrix4x4`...) are always described by their public fields, whatever the mode, also for NativeAOT.
- For trimming and NativeAOT, the classes with public fields keep their public fields, and those with described non-public fields keep them too (`DynamicDependency`).

## Using it

Three ways, and they can be combined:

- **The serializer's constructor** (recommended, and the one for trimmed and NativeAOT apps, see below):

```csharp
LtMsgPackSerializer serializer = new LtMsgPackSerializer(AppMsgPackContext.Default, new LtMsgPackOptions());
```

- **Registered for the process (C# 9 and later):** the generated module initializer registers the contexts of the assembly when it is loaded. Every `LtMsgPackSerializer` then uses them, without code changes. LsMsgPack uses their property accessors and constructor calls too. Switch it off with `<LtMsgPackAutoRegister>false</LtMsgPackAutoRegister>` or `[assembly: LtMsgPackGenerate(AutoRegister = false)]`.
- **In the options**, which also works for C# 7.3 and .NET Framework projects:

```csharp
LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions
{
  Contexts = new LtMsgPackContext[] { AppMsgPackContext.Default }
});
```

The context of the constructor is asked first, then the options' contexts, then the registered ones (unless `UseRegisteredContexts = false`). Within each, the first context that describes a type is used for it. The web formatters take the context where they create their serializer: `new LtMsgPackHttpSerializer(AppMsgPackContext.Default, options)`, ASP.NET Core `AddLsMsgPackSerializerFormatters(AppMsgPackContext.Default, ...)`, or through their `LtMsgPackOptions` (`LtMsgPackHttpOptions.XLsMsgPack`, `Plain`).

## What it writes, and what it leaves to the runtime

For each class and struct:

1. **Accessors and creators:** a typed getter and setter per property, `new T()` for an accessible parameterless constructor, and the generic handlers and readers of each type (enums, nullables, lists, arrays, objects) created without `MakeGenericType`. The runtime uses them wherever it would have used reflection, with every option.
2. **Writers and readers per class and struct:** the loop over the properties, with the values of simple types written and read inline: strings, numbers, `bool`, `char`, enums, `Guid`, `DateTime`, `TimeSpan`, `decimal`, and nullables of those. When the runtime's property order is the one the generator predicted (the default `PropertyOrder.Reflection`), objects are written by straight-line code, in the array and in the map layout (also with `TrimTrailingNulls`). `Guid`s and `decimal`s are written and read in the format of the options (`GuidFormat.String` and `DecimalFormat.String` too, as the `Generic()` preset sets them). Otherwise a loop maps each property position to its generated member, so every `PropertyOrder` works; where the runtime compiles code (not NativeAOT or WebAssembly) it replaces that loop by a writer compiled for the order after `CompileWritersAfter` objects of the class. Where the runtime compiles code it also compiles readers for classes without generated code, after `CompileReadersAfter` objects of a class: the same code as the generated readers, for arrays and for maps with the indexed schema (maps keyed by property names keep the runtime's loop).

The runtime checks once per class and serializer whether the generated code fits the options. Where it doesn't, the runtime does that part itself, as it does without the generator:

- Formats of other libraries when reading (`GuidFormat.String`, `DecimalFormat.String`), and custom extensions for a type. These affect only the properties of that type.
- Custom dynamic filters, `AddTypeIdOption.Always`, and custom property id or type resolvers (the whole class).
- Properties with `[DefaultValue]`, nested objects and collections (written and read by the runtime, which then uses the generated code of their class).
- Values in another format than expected (an `int` written as a string, a number out of range): the runtime converts them, or throws, exactly as without the generator.
- Collecting `ReadDifferences` and `ReadErrors` other than `FailFast`.
- **Debug builds.** The generated code is compiled into your assembly, so in a Debug build it isn't optimized, while LtMsgPack's package is: it can be slower than without the generator (1.2-1.9x measured), reported as info `LTMP011`. Measure performance in Release.
- Properties the generated code can't reach: `init`-only setters and non-public accessors without `UnsafeAccessor` (before .NET 8, of generic classes, virtual properties), non-public accessors that `NonPublicProperties` doesn't cover (set by reflection, reported as info `LTMP004`), private types. On .NET 8 and later, `init`-only setters of other properties are called through `[UnsafeAccessor]`, whatever `NonPublicProperties` says.
- A class that changed since its code was generated (a model in another assembly that was updated on its own): a property is only used when its declaring type, name and type still match. New properties are done by reflection.

**Structs** are written and read in place: the generated accessors and code take the struct by reference, so a struct value is neither copied nor boxed, also in trimmed and NativeAOT apps (without the generator those box each struct value and use reflection). A struct as the root value of a call is still boxed once. A struct without a declared parameterless constructor is read into its default value; one that declares a parameterless constructor (C# 10) is created by it, in a box. The `init` properties of a struct (a `readonly record struct`) are set through `[UnsafeAccessor]` by reference as well; before .NET 8 by reflection, in a box (info `LTMP004`).

## Versions

Generated code says which version of the generated code's contract it was written for. A runtime uses it when that version is between `GeneratedContract.MinSupported` and `GeneratedContract.Current`. Otherwise it ignores the code and builds its own plans: slower, never broken. This matters for generated code compiled into a library (a contracts package) that is used with a newer LtMsgPack. A new feature of the generator raises `Current` only: the newer runtime still uses older generated code, just without the new feature. `LtMsgPackContext.IsUsable` says whether a context is used.

## Diagnostics

| Id | Severity | |
|---|---|---|
| LTMP001 | Error | The context class (or a class it is nested in) is not `partial` |
| LTMP002 | Error | A class with `[LtMsgPackSerializable]` doesn't derive from `LtMsgPackContext` |
| LTMP003 | Warning | A root type is not accessible to the generated code |
| LTMP004 | Info | A property is set by reflection (an `init`-only or non-public setter the generated code can't call) |
| LTMP005 | Error | The context class is generic, nested in a generic type, abstract or static |
| LTMP006 | Error | The context of the assembly can't get its name (taken, or not a valid class name) |
| LTMP007 | Error | The context class declares a parameterless constructor (the generator writes it) |
| LTMP008 | Warning | An open generic type: list its constructed types (`typeof(Wrapper<int>)`) |
| LTMP009 | Info | A context is not registered for the process (C# 7.3, or not accessible): pass it in `Contexts` |
| LTMP010 | Warning | Only in projects that are trimmed or compiled ahead of time (`PublishTrimmed`, `PublishAot`, `IsAotCompatible`, `IsTrimmable`): a property holds values of an interface, an abstract class or `object` (also as the elements of a collection or the values of a dictionary), and no class of the context fits it. List the classes it holds with `[LtMsgPackSerializable]`. An `object` property is always reported: where it only holds primitives and strings, set `<NoWarn>$(NoWarn);LTMP010</NoWarn>` or `dotnet_diagnostic.LTMP010.severity = none` in `.editorconfig` |
| LTMP011 | Info | The generated code is compiled without optimizations (a Debug build), so it can be slower than without the generator: measure performance in Release |

To see the generated code, set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` in the project: it lands in `obj/.../generated/LtMsgPack.Generator`.

## Trimming and NativeAOT

Create the serializer with the context:

```csharp
LtMsgPackSerializer serializer = new LtMsgPackSerializer(AppMsgPackContext.Default, options);
```

The constructors without a context (`new LtMsgPackSerializer()`, `new LtMsgPackSerializer(options)`, `LtMsgPackSerializer.Default`) are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, like System.Text.Json's reflection-based methods. In a trimmed or NativeAOT project the analyzers warn where such a serializer is created (IL2026, IL3050), once, not on every `Serialize`/`Deserialize` call. The same goes for the HTTP serializer and the formatters: `new LtMsgPackHttpSerializer(AppMsgPackContext.Default, options)`, ASP.NET Core `AddLsMsgPackSerializerFormatters(AppMsgPackContext.Default, ...)`, and for Web API / HttpClient `new LsMsgPackMediaTypeFormatter(new LtMsgPackHttpSerializer(AppMsgPackContext.Default, options))`.

The libraries (LsMsgPack.Core and LtMsgPack, with a `net8.0` build marked `IsAotCompatible`) report no trim or AOT warnings of their own. Their reflection is either kept by `DynamicDependency` attributes (framework types found by name: `DateOnly`, `TimeOnly`, `Half`, `Rune`, `Int128`, `UInt128` and their members, tuples, `KeyValuePair`) or justified by the generated code. The justifications are in `LsMsgPack.Meta.TrimReasons`.

What the generated code does for a trimmed or NativeAOT app:

- It keeps the properties and constructors of your classes (`DynamicDependency`, .NET 5 and later; also the non-public properties of the classes that have some it covers) and creates the generic handlers and readers for them.
- For the collections it meets, it also describes what the runtime makes for them: the array of their elements, the class created for an interface (`List<T>` for `IList<T>`, `HashSet<T>` for `ISet<T>`, `ImmutableHashSet<T>` for `IImmutableSet<T>`...). For immutable and frozen collections it writes the call of their factory (`ImmutableArray.CreateRange<T>`, `FrozenSet.ToFrozenSet<T>`...), which NativeAOT can't instantiate at runtime for value types.
- `HashSet<T>`, `Dictionary<TKey, TValue>` and generic collection interfaces (`IList<T>`, `IEnumerable<T>`, `ISet<T>`, `IDictionary<TKey, TValue>`...) get typed code in the runtime, so their elements aren't boxed, also under NativeAOT. Other collections are filled through reflection there.
- A serializer created with a context registers it for the process (unless `UseRegisteredContexts = false`), as the module initializer does: LsMsgPack.Core creates collections and objects with the registered code.

Classes the generated code doesn't know of are not kept. In a trimmed or AOT project, `LTMP010` points at the properties whose values are only known at runtime (interfaces, abstract classes, `object`), so you can list their classes. Type ids name classes. After trimming, only the classes the app keeps can be found, which includes those of the context.

The NativeAOT smoke test (`LtMsgPackAotSmokeTest/run.sh`, CI job `aot-smoke`) publishes with every warning reported separately (`TrimmerSingleWarn=false`) and fails on any trim or AOT warning. It checks an invoice model with polymorphic properties, framework types (`DateOnly`, `Half`, `Int128`, `BigInteger`, `Complex`, `Rune`, tuples, `KeyValuePair`) and collections (`IList<int>`, `IReadOnlyList<T>`, `ImmutableArray<int>`, `IImmutableSet<string>`, `FrozenSet<string>`, `List<KeyValuePair<string, double>>`) in six option sets, with the context registered by the module initializer and only passed to the constructor.

## Measurements

The invoice model of the benchmarks (100 invoices × 20 rounds), one machine: on a JIT in November 2026 (the medians of three runs, see [Comparison.md](Comparison.md#how-it-was-measured)), on WebAssembly in October 2026 (the fastest round). Speed is relative to System.Text.Json with its default options.

| .NET 10, JIT | write | read |
|---|---|---|
| LtMsgPack, default options (indexed schema, arrays) | 3.71 ms (4.6x) | 5.03 ms (5.8x) |
| the same with the generated code | 3.41 ms (5.0x) | 5.42 ms (5.3x) |
| LtMsgPack, arrays without the schema | 3.08 ms (5.5x) | 4.97 ms (5.8x) |
| the same with the generated code | 3.19 ms (5.3x) | 5.28 ms (5.5x) |
| LtMsgPack, maps with property names | 5.04 ms (3.4x) | 8.86 ms (3.3x) |
| the same with the generated code | 4.76 ms (3.6x) | 7.03 ms (4.1x) |

| .NET 10, WebAssembly (node) | interpreter write | read | AOT write | read |
|---|---|---|---|---|
| LtMsgPack, default options | 183 ms (3.0x) | 235 ms (3.5x) | 78 ms (2.8x) | 69 ms (3.1x) |
| the same with the generated code | 92 ms (5.8x) | 196 ms (4.2x) | 10.5 ms (20x) | 24 ms (8.7x) |

With a JIT, LtMsgPack compiles its writers and readers at runtime (after 1000 objects of a class, as in these rows). Writing is about as fast without the generated code (within 8% either way), and so is reading arrays: in this table, where all setups share one process and its JIT profile, the generated code read them 6-8% slower, measured one setup per process equally fast. Maps keyed by property names read 1.3x as fast with the generated code. Decoding strings takes most of the time of reading, with or without generated code. On WebAssembly so did converting timestamps to local time (the default `ReadDateTimeKind` when that was measured, now UTC).
