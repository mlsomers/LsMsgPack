# The source generator of LsMsgPack

LsMsgPack builds a plan per class the first time it meets the class: typed property accessors, generic handlers and readers, created with reflection. The source generator writes that code at compile time instead, for the classes you name:

- **NativeAOT, WebAssembly AOT (Blazor), iOS:** code that can't be generated at runtime. Without the generator, LsMsgPack fails under NativeAOT on the first object. With it, the generated code runs compiled where the runtime plans would be interpreted.
- **Trimming:** the generated code refers to your classes and their properties, so the trimmer keeps them (.NET 5 and later). A serializer created with the context has no trim or AOT warnings (see [Trimming and NativeAOT](#trimming-and-nativeaot)).
- **Speed:** with a JIT, LsMsgPack compiles the same writers and readers at runtime once a type has been written or read 1000 times (`LsMsgPackOptions.CompileWritersAfter`, `CompileReadersAfter`; 0 compiles at the first object, -1 never), after which both are about as fast. So the generated code gains on the first objects of each type. Under WebAssembly AOT writing is 7-8x and reading about 3x as fast (measurements below).

The data doesn't change: the generated code writes the same bytes and reads the same values as LsMsgPack does without it, with the same options. Anything it doesn't cover is done by the runtime as before, so you can switch it on and off without touching stored data or peers.

The generator ships in the LsMsgPack package. It needs an SDK-style project (`PackageReference`): projects with `packages.config` don't run source generators.

## Telling it which classes

### A context class

```csharp
using LsMsgPack;

[LsMsgPackSerializable(typeof(Invoice))]
[LsMsgPackSerializable(typeof(Circle))] // subclasses only met through type ids at runtime
[LsMsgPackSerializable(typeof(Square))]
public partial class AppMsgPackContext : LsMsgPackContext { }
```

The generator writes the rest of the class: a parameterless constructor and a static `AppMsgPackContext.Default`. The types a root reaches are included: the types of its properties, the elements of lists, arrays and dictionaries, base classes, the underlying types of nullables.

Types that are only known at runtime need their own attribute: implementations assigned to an `object` or interface property, or subclasses of a declared base class.

### Without a context class: the classes you already marked

To use the generator without marking anything again, let it take the classes from markers they already have. In the project file:

```xml
<PropertyGroup>
  <LsMsgPackGenerate>Serializable;DataContract;JsonSerializable</LsMsgPackGenerate>
</PropertyGroup>
```

or in code:

```csharp
[assembly: LsMsgPackGenerate(Include = LsMsgPackInclude.Serializable | LsMsgPackInclude.DataContract | LsMsgPackInclude.JsonSerializable)]
```

| `Include` | The roots |
|---|---|
| `AllTypes` | Every class and struct of the assembly with public properties, that the generated code can access (public and internal) |
| `Serializable` | Classes and structs marked `[Serializable]` (as for the XmlSerializer's sgen and the binary formatters), not exceptions |
| `DataContract` | Classes and structs marked `[DataContract]` |
| `JsonSerializable` | The types of System.Text.Json's source generation: every `[JsonSerializable(typeof(...))]` in the assembly |

These go into one generated context per assembly: `GeneratedMsgPackContext` in the project's root namespace. Name it yourself with `<LsMsgPackContextName>MyApp.Serialization.MsgPackContext</LsMsgPackContextName>` or `ContextName`. The assembly attribute wins over the MSBuild properties.

A marker only selects the classes. Which properties are written, and under which ids, is still decided by the options: `[IgnoreDataMember]` and `[JsonIgnore]` through the default static filters, `[DataMember(Order = n)]` with `PropertyOrder.Explicit`. The generated code follows whatever the options decide.

### Non-public properties

The runtime serializes [non-public properties](schema.md#non-public-properties) when `FilterNonSettable` is given a `NonPublicProperties`. The generator covers the ones with an opt-in attribute (`[JsonInclude]`, `[DataMember]`...) by default, `All` covers every non-public property (which only adds code: the options still decide what is serialized):

```xml
<LsMsgPackNonPublicProperties>All</LsMsgPackNonPublicProperties> <!-- None, Attributed (the default) or All -->
```

or `[assembly: LsMsgPackGenerate(NonPublicProperties = NonPublicProperties.All)]`. The generated code calls the getters and setters it can't access through `[UnsafeAccessor]` (.NET 8 and later), not for generic or virtual properties. Without it (.NET Standard, .NET Framework) the runtime uses reflection for them (info `LSMP104` for the setters). For trimming and NativeAOT, the classes with such properties keep their non-public properties (`DynamicDependency`).

### Fields

The generated code covers the [fields](schema.md#fields) the runtime serializes (`SerializedFields` of `FilterNonSettable`), written after the properties like the runtime does. The mode of the generator is `Attributed` unless you set it: fields with an opt-in attribute (`[DataMember]`, `[JsonInclude]`...), public or not. It must be the mode of the runtime's static filters, otherwise the generated code describes other fields than the runtime writes (the [analyzers](Analyzers.md) read the same setting):

```xml
<LsMsgPackFields>Public</LsMsgPackFields> <!-- None, Attributed (the default), Public or All -->
```

or `[assembly: LsMsgPackGenerate(Fields = SerializedFields.Public)]`, which wins over the property. The property is declared by LsMsgPack.Core's build props, which the LsMsgPack package brings along.

- Private fields are read and set through `[UnsafeAccessor]` (.NET 8 and later), not for fields of generic classes: reflection (the runtime's, as for non-public properties). A `readonly` field has no setter: it is written, not set (see [schema.md](schema.md#fields)).
- Framework structs whose state is public fields (`Vector3`, `Quaternion`, `Matrix4x4`...) are always described by their public fields, whatever the mode, also for NativeAOT.
- For trimming and NativeAOT, the classes with public fields keep their public fields, and those with described non-public fields keep them too (`DynamicDependency`).

## Using it

Three ways, and they can be combined:

- **The serializer's constructor** (recommended, and the one for trimmed and NativeAOT apps, see below):

```csharp
LsMsgPackSerializer serializer = new LsMsgPackSerializer(AppMsgPackContext.Default, new LsMsgPackOptions());
```

- **Registered for the process (C# 9 and later):** the generated module initializer registers the contexts of the assembly when it is loaded. Every `LsMsgPackSerializer` then uses them, without code changes. LsMsgPackDebug uses their property accessors and constructor calls too. Switch it off with `<LsMsgPackAutoRegister>false</LsMsgPackAutoRegister>` or `[assembly: LsMsgPackGenerate(AutoRegister = false)]`.
- **In the options**, which also works for C# 7.3 and .NET Framework projects:

```csharp
LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions
{
  Contexts = new LsMsgPackContext[] { AppMsgPackContext.Default }
});
```

The context of the constructor is asked first, then the options' contexts, then the registered ones (unless `UseRegisteredContexts = false`). Within each, the first context that describes a type is used for it. The web formatters take the context where they create their serializer: `new LsMsgPackHttpSerializer(AppMsgPackContext.Default, options)`, ASP.NET Core `AddLsMsgPackSerializerFormatters(AppMsgPackContext.Default, ...)`, or through their `LsMsgPackOptions` (`LsMsgPackHttpOptions.XLsMsgPack`, `Plain`).

## What it writes, and what it leaves to the runtime

For each class and struct:

1. **Accessors and creators:** a typed getter and setter per property, `new T()` for an accessible parameterless constructor, and the generic handlers and readers of each type (enums, nullables, lists, arrays, objects) created without `MakeGenericType`. The runtime uses them wherever it would have used reflection, with every option.
2. **Writers and readers per class and struct:** the loop over the properties, with the values of simple types written and read inline: strings, numbers, `bool`, `char`, enums, `Guid`, `DateTime`, `TimeSpan`, `decimal`, and nullables of those. When the runtime's property order is the one the generator predicted (the default `PropertyOrder.Reflection`), objects are written by straight-line code, in the array and in the map layout (also with `TrimTrailingNulls`). `Guid`s and `decimal`s are written and read in the format of the options (`GuidFormat.String` and `DecimalFormat.String` too, as the `Generic()` preset sets them). Otherwise a loop maps each property position to its generated member, so every `PropertyOrder` works; where the runtime compiles code (not NativeAOT or WebAssembly) it replaces that loop by a writer compiled for the order after `CompileWritersAfter` objects of the class. Where the runtime compiles code it also compiles readers for classes without generated code, after `CompileReadersAfter` objects of a class: the same code as the generated readers for arrays and for maps with the indexed schema; maps keyed by property names straight-line, each key compared with the name of the next property (not with `PropertyNameCaseInsensitive`). The generated readers read maps keyed by names the same way when the runtime's property order is the predicted one; keys in another order, or names the class does not have, go on in the loop.

The runtime checks once per class and serializer whether the generated code fits the options. Where it doesn't, the runtime does that part itself, as it does without the generator:

- Formats of other libraries when reading (`GuidFormat.String`, `DecimalFormat.String`), and custom extensions for a type. These affect only the properties of that type.
- Custom dynamic filters, `AddTypeIdOption.Always`, and custom property id or type resolvers (the whole class).
- Properties with `[DefaultValue]`, nested objects and collections (written and read by the runtime, which then uses the generated code of their class).
- Values in another format than expected (an `int` written as a string, a number out of range): the runtime converts them, or throws, exactly as without the generator.
- Collecting `ReadDifferences` and `ReadErrors` other than `FailFast`.
- **Debug builds.** The generated code is compiled into your assembly, so in a Debug build it isn't optimized, while LsMsgPack's package is: it can be slower than without the generator (1.2-1.9x measured), reported as info `LSMP111`. Measure performance in Release.
- Properties the generated code can't reach: `init`-only setters and non-public accessors without `UnsafeAccessor` (before .NET 8, of generic classes, virtual properties), non-public accessors that `NonPublicProperties` doesn't cover (set by reflection, reported as info `LSMP104`), private types. On .NET 8 and later, `init`-only setters of other properties are called through `[UnsafeAccessor]`, whatever `NonPublicProperties` says.
- A class that changed since its code was generated (a model in another assembly that was updated on its own): a property is only used when its declaring type, name and type still match. New properties are done by reflection.

**Structs** are written and read in place: the generated accessors and code take the struct by reference, so a struct value is neither copied nor boxed, also in trimmed and NativeAOT apps (without the generator those box each struct value and use reflection). A struct as the root value of a call is still boxed once. A struct without a declared parameterless constructor is read into its default value; one that declares a parameterless constructor (C# 10) is created by it, in a box. The `init` properties of a struct (a `readonly record struct`) are set through `[UnsafeAccessor]` by reference as well; before .NET 8 by reflection, in a box (info `LSMP104`).

## Versions

Generated code says which version of the generated code's contract it was written for. A runtime uses it when that version is between `GeneratedContract.MinSupported` and `GeneratedContract.Current`. Otherwise it ignores the code and builds its own plans: slower, never broken. This matters for generated code compiled into a library (a contracts package) that is used with a newer LsMsgPack. A new feature of the generator raises `Current` only: the newer runtime still uses older generated code, just without the new feature. `LsMsgPackContext.IsUsable` says whether a context is used.

## Diagnostics

| Id | Severity | |
|---|---|---|
| LSMP101 | Error | The context class (or a class it is nested in) is not `partial` |
| LSMP102 | Error | A class with `[LsMsgPackSerializable]` doesn't derive from `LsMsgPackContext` |
| LSMP103 | Warning | A root type is not accessible to the generated code |
| LSMP104 | Info | A property is set by reflection (an `init`-only or non-public setter the generated code can't call) |
| LSMP105 | Error | The context class is generic, nested in a generic type, abstract or static |
| LSMP106 | Error | The context of the assembly can't get its name (taken, or not a valid class name) |
| LSMP107 | Error | The context class declares a parameterless constructor (the generator writes it) |
| LSMP108 | Warning | An open generic type: list its constructed types (`typeof(Wrapper<int>)`) |
| LSMP109 | Info | A context is not registered for the process (C# 7.3, or not accessible): pass it in `Contexts` |
| LSMP110 | Warning | Only in projects that are trimmed or compiled ahead of time (`PublishTrimmed`, `PublishAot`, `IsAotCompatible`, `IsTrimmable`): a property holds values of an interface, an abstract class or `object` (also as the elements of a collection or the values of a dictionary), and no class of the context fits it. List the classes it holds with `[LsMsgPackSerializable]`. An `object` property is always reported: where it only holds primitives and strings, set `<NoWarn>$(NoWarn);LSMP110</NoWarn>` or `dotnet_diagnostic.LSMP110.severity = none` in `.editorconfig` |
| LSMP111 | Info | The generated code is compiled without optimizations (a Debug build), so it can be slower than without the generator: measure performance in Release |

To see the generated code, set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` in the project: it lands in `obj/.../generated/LsMsgPack.Generator`.

## Trimming and NativeAOT

Create the serializer with the context:

```csharp
LsMsgPackSerializer serializer = new LsMsgPackSerializer(AppMsgPackContext.Default, options);
```

The constructors without a context (`new LsMsgPackSerializer()`, `new LsMsgPackSerializer(options)`, `LsMsgPackSerializer.Default`) are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, like System.Text.Json's reflection-based methods. In a trimmed or NativeAOT project the analyzers warn where such a serializer is created (IL2026, IL3050), once, not on every `Serialize`/`Deserialize` call. The same goes for the HTTP serializer and the formatters: `new LsMsgPackHttpSerializer(AppMsgPackContext.Default, options)`, ASP.NET Core `AddLsMsgPackSerializerFormatters(AppMsgPackContext.Default, ...)`, and for Web API / HttpClient `new LsMsgPackMediaTypeFormatter(new LsMsgPackHttpSerializer(AppMsgPackContext.Default, options))`.

The libraries (LsMsgPack.Core and LsMsgPack, with a `net8.0` build marked `IsAotCompatible`) report no trim or AOT warnings of their own. Their reflection is either kept by `DynamicDependency` attributes (framework types found by name: `DateOnly`, `TimeOnly`, `Half`, `Rune`, `Int128`, `UInt128` and their members, tuples, `KeyValuePair`) or justified by the generated code. The justifications are in `LsMsgPack.Meta.TrimReasons`.

What the generated code does for a trimmed or NativeAOT app:

- It keeps the properties and constructors of your classes (`DynamicDependency`, .NET 5 and later; also the non-public properties of the classes that have some it covers) and creates the generic handlers and readers for them.
- For the collections it meets, it also describes what the runtime makes for them: the array of their elements, the class created for an interface (`List<T>` for `IList<T>`, `HashSet<T>` for `ISet<T>`, `ImmutableHashSet<T>` for `IImmutableSet<T>`...). For immutable and frozen collections it writes the call of their factory (`ImmutableArray.CreateRange<T>`, `FrozenSet.ToFrozenSet<T>`...), which NativeAOT can't instantiate at runtime for value types.
- Arrays, `List<T>` and `HashSet<T>` of `int`, `long`, `float`, `double`, `sbyte` (lists and sets), `short`, `ushort`, `uint`, `ulong`, `bool`, `Guid` and `decimal` are written and read in bulk (a loop over the values, 2-3x faster than a call per value). Under NativeAOT only those the generated code describes are: the app only holds that code for the element types and kinds of collection your classes have (an app with none of them is about 200 KB smaller than with all of them). The bytes are the same either way.
- `HashSet<T>`, `Dictionary<TKey, TValue>` and generic collection interfaces (`IList<T>`, `IEnumerable<T>`, `ISet<T>`, `IDictionary<TKey, TValue>`...) get typed code in the runtime, so their elements aren't boxed, also under NativeAOT. Other collections are filled through reflection there.
- A serializer created with a context registers it for the process (unless `UseRegisteredContexts = false`), as the module initializer does: LsMsgPack.Core creates collections and objects with the registered code.

Classes the generated code doesn't know of are not kept. In a trimmed or AOT project, `LSMP110` points at the properties whose values are only known at runtime (interfaces, abstract classes, `object`), so you can list their classes. Type ids name classes. After trimming, only the classes the app keeps can be found, which includes those of the context.

The NativeAOT smoke test (`LsMsgPackAotSmokeTest/run.sh`, CI job `aot-smoke`) publishes with every warning reported separately (`TrimmerSingleWarn=false`) and fails on any trim or AOT warning. It checks an invoice model with polymorphic properties, framework types (`DateOnly`, `Half`, `Int128`, `BigInteger`, `Complex`, `Rune`, tuples, `KeyValuePair`) and collections (`IList<int>`, `IReadOnlyList<T>`, `ImmutableArray<int>`, `IImmutableSet<string>`, `FrozenSet<string>`, `List<KeyValuePair<string, double>>`, collections in bulk) in six option sets, with the context registered by the module initializer and only passed to the constructor.

## Measurements

The invoice model of the benchmarks (100 invoices × 20 rounds), one machine: on a JIT in November 2026 (the medians of three runs, see [Comparison.md](Comparison.md#how-it-was-measured)), on WebAssembly in October 2026 (the fastest round). Speed is relative to System.Text.Json with its default options.

| .NET 10, JIT | write | read |
|---|---|---|
| LsMsgPack, default options (indexed schema, arrays) | 3.62 ms (4.5x) | 4.84 ms (5.8x) |
| the same with the generated code | 3.35 ms (4.9x) | 5.25 ms (5.4x) |
| LsMsgPack, arrays without the schema | 2.98 ms (5.5x) | 4.71 ms (6.0x) |
| the same with the generated code | 3.28 ms (5.0x) | 5.10 ms (5.5x) |
| LsMsgPack, maps with property names | 4.96 ms (3.3x) | 5.16 ms (5.5x) |
| the same with the generated code | 4.67 ms (3.5x) | 5.32 ms (5.4x) |

| .NET 10, WebAssembly (node) | interpreter write | read | AOT write | read |
|---|---|---|---|---|
| LsMsgPack, default options | 183 ms (3.0x) | 235 ms (3.5x) | 78 ms (2.8x) | 69 ms (3.1x) |
| the same with the generated code | 92 ms (5.8x) | 196 ms (4.2x) | 10.5 ms (20x) | 24 ms (8.7x) |

With a JIT, LsMsgPack compiles its writers and readers at runtime (after 1000 objects of a class, as in these rows). Writing is about as fast without the generated code (within 10% either way), and so is reading arrays: in this table, where all setups share one process and its JIT profile, the generated code read them 8% slower, measured one setup per process equally fast. Maps keyed by property names are read by the same code with and without the generated code: each key compared with the next property's name as constants. Measured one setup per process, the generated code read them as fast as the readers compiled at runtime (invoices 4.6 against 4.7 ms on .NET 8), and under NativeAOT, where nothing is compiled at runtime, 1.7x as fast as before it did so (a class of 120 properties: 4.7 -> 2.7 µs per object). It costs JIT time when a class is first read by names: about 75 µs per property on the machine of these tables. Decoding strings takes most of the time of reading, with or without generated code. On WebAssembly so did converting timestamps to local time (the default `ReadDateTimeKind` when that was measured, now UTC).
