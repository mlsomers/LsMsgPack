# The source generator of LtMsgPack

LtMsgPack builds a plan per class the first time it meets the class: typed property accessors, generic handlers and readers, created with reflection. The source generator writes that code at compile time instead, for the classes you name:

- **NativeAOT, WebAssembly AOT (Blazor), iOS:** code that can't be generated at runtime. Without the generator, LtMsgPack fails under NativeAOT on the first object. With it, the generated code runs compiled where the runtime plans would be interpreted.
- **Trimming:** the generated code refers to your classes and their properties, so the trimmer keeps them (.NET 5 and later). A serializer created with the context has no trim or AOT warnings (see [Trimming and NativeAOT](#trimming-and-nativeaot)).
- **Speed:** writing is about 1.6x and reading about 1.15x as fast with a JIT. Under WebAssembly AOT writing is 7-8x and reading about 3x as fast (measurements below).

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
| `AllTypes` | Every class of the assembly with public properties, that the generated code can access (public and internal) |
| `Serializable` | Classes marked `[Serializable]` (as for the XmlSerializer's sgen and the binary formatters), not exceptions |
| `DataContract` | Classes marked `[DataContract]` |
| `JsonSerializable` | The types of System.Text.Json's source generation: every `[JsonSerializable(typeof(...))]` in the assembly |

These go into one generated context per assembly: `GeneratedMsgPackContext` in the project's root namespace. Name it yourself with `<LtMsgPackContextName>MyApp.Serialization.MsgPackContext</LtMsgPackContextName>` or `ContextName`. The assembly attribute wins over the MSBuild properties.

A marker only selects the classes. Which properties are written, and under which ids, is still decided by the options: `[IgnoreDataMember]` and `[JsonIgnore]` through the default static filters, `[DataMember(Order = n)]` with `PropertyOrder.Explicit`. The generated code follows whatever the options decide.

### Non-public properties

The runtime serializes [non-public properties](schema.md#non-public-properties) when `FilterNonSettable` is given a `NonPublicProperties`. The generator covers the ones with an opt-in attribute (`[JsonInclude]`, `[DataMember]`...) by default, `All` covers every non-public property (which only adds code: the options still decide what is serialized):

```xml
<LtMsgPackNonPublicProperties>All</LtMsgPackNonPublicProperties> <!-- None, Attributed (the default) or All -->
```

or `[assembly: LtMsgPackGenerate(NonPublicProperties = NonPublicProperties.All)]`. The generated code calls the getters and setters it can't access through `[UnsafeAccessor]` (.NET 8 and later), not for generic or virtual properties. Without it (.NET Standard, .NET Framework) the runtime uses reflection for them (info `LTMP004` for the setters). For trimming and NativeAOT, the classes with such properties keep their non-public properties (`DynamicDependency`).

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

For each class:

1. **Accessors and creators:** a typed getter and setter per property, `new T()` for an accessible parameterless constructor, and the generic handlers and readers of each type (enums, nullables, lists, arrays, objects) created without `MakeGenericType`. The runtime uses them wherever it would have used reflection, with every option.
2. **Writers and readers per class:** the loop over the properties, with the values of simple types written and read inline: strings, numbers, `bool`, `char`, enums, `Guid`, `DateTime`, `TimeSpan`, `decimal`, and nullables of those. When the runtime's property order is the one the generator predicted (the default `PropertyOrder.Reflection`), objects in the array layout are written by straight-line code. Otherwise a loop maps each property position to its generated member, so every `PropertyOrder` works.

The runtime checks once per class and serializer whether the generated code fits the options. Where it doesn't, the runtime does that part itself, as it does without the generator:

- Formats of other libraries: `GuidFormat.String`, `DecimalFormat.String`, and custom extensions for a type. These affect only the properties of that type.
- Custom dynamic filters, `AddTypeIdOption.Always`, and custom property id or type resolvers (the whole class).
- Properties with `[DefaultValue]`, nested objects and collections (written and read by the runtime, which then uses the generated code of their class).
- Values in another format than expected (an `int` written as a string, a number out of range): the runtime converts them, or throws, exactly as without the generator.
- Collecting `ReadDifferences` and `ReadErrors` other than `FailFast`.
- Properties the generated code can't reach: `init`-only setters, non-public accessors without `UnsafeAccessor` (before .NET 8, of generic classes, virtual properties) or that `NonPublicProperties` doesn't cover (set by reflection, reported as info `LTMP004`), private types, structs (written and read boxed).
- A class that changed since its code was generated (a model in another assembly that was updated on its own): a property is only used when its declaring type, name and type still match. New properties are done by reflection.

## Versions

Generated code says which version of the generated code's contract it was written for. A runtime uses it when that version is between `GeneratedContract.MinSupported` and `GeneratedContract.Current`. Otherwise it ignores the code and builds its own plans: slower, never broken. This matters for generated code compiled into a library (a contracts package) that is used with a newer LtMsgPack. A new feature of the generator raises `Current` only: the newer runtime still uses older generated code, just without the new feature. `LtMsgPackContext.IsUsable` says whether a context is used.

## Diagnostics

| Id | Severity | |
|---|---|---|
| LTMP001 | Error | The context class (or a class it is nested in) is not `partial` |
| LTMP002 | Error | A class with `[LtMsgPackSerializable]` doesn't derive from `LtMsgPackContext` |
| LTMP003 | Warning | A root type is not accessible to the generated code |
| LTMP004 | Info | A property is set by reflection (`init`-only or non-public setter) |
| LTMP005 | Error | The context class is generic, nested in a generic type, abstract or static |
| LTMP006 | Error | The context of the assembly can't get its name (taken, or not a valid class name) |
| LTMP007 | Error | The context class declares a parameterless constructor (the generator writes it) |
| LTMP008 | Warning | An open generic type: list its constructed types (`typeof(Wrapper<int>)`) |
| LTMP009 | Info | A context is not registered for the process (C# 7.3, or not accessible): pass it in `Contexts` |
| LTMP010 | Warning | Only in projects that are trimmed or compiled ahead of time (`PublishTrimmed`, `PublishAot`, `IsAotCompatible`, `IsTrimmable`): a property holds values of an interface, an abstract class or `object` (also as the elements of a collection or the values of a dictionary), and no class of the context fits it. List the classes it holds with `[LtMsgPackSerializable]`. An `object` property is always reported: where it only holds primitives and strings, set `<NoWarn>$(NoWarn);LTMP010</NoWarn>` or `dotnet_diagnostic.LTMP010.severity = none` in `.editorconfig` |

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
- A serializer created with a context registers it for the process (unless `UseRegisteredContexts = false`), as the module initializer does: LsMsgPack.Core creates collections and objects with the registered code.

Classes the generated code doesn't know of are not kept. In a trimmed or AOT project, `LTMP010` points at the properties whose values are only known at runtime (interfaces, abstract classes, `object`), so you can list their classes. Type ids name classes. After trimming, only the classes the app keeps can be found, which includes those of the context.

The NativeAOT smoke test (`LtMsgPackAotSmokeTest/run.sh`, CI job `aot-smoke`) publishes with every warning reported separately (`TrimmerSingleWarn=false`) and fails on any trim or AOT warning. It checks an invoice model with polymorphic properties, framework types (`DateOnly`, `Half`, `Int128`, `BigInteger`, `Complex`, `Rune`, tuples, `KeyValuePair`) and collections (`IList<int>`, `IReadOnlyList<T>`, `ImmutableArray<int>`, `IImmutableSet<string>`, `FrozenSet<string>`, `List<KeyValuePair<string, double>>`) in six option sets, with the context registered by the module initializer and only passed to the constructor.

## Measurements

The invoice model of the benchmarks (100 invoices × 20 rounds, the fastest round), October 2026, one machine. Speed is relative to System.Text.Json with its default options.

| .NET 8, JIT | write | read |
|---|---|---|
| LtMsgPack, default options (indexed schema, arrays) | 5.3 ms (3.5x) | 7.3 ms (4.5x) |
| the same with the generated code | 3.3 ms (5.5x) | 6.4 ms (5.1x) |
| LtMsgPack, arrays without the schema | 4.7 ms (3.9x) | 7.0 ms (4.7x) |
| the same with the generated code | 2.9 ms (6.3x) | 6.2 ms (5.3x) |
| LtMsgPack, maps with property names | 7.4 ms (2.5x) | 9.5 ms (3.4x) |
| the same with the generated code | 5.8 ms (3.2x) | 9.4 ms (3.5x) |

| .NET 10, WebAssembly (node) | interpreter write | read | AOT write | read |
|---|---|---|---|---|
| LtMsgPack, default options | 183 ms (3.0x) | 235 ms (3.5x) | 78 ms (2.8x) | 69 ms (3.1x) |
| the same with the generated code | 92 ms (5.8x) | 196 ms (4.2x) | 10.5 ms (20x) | 24 ms (8.7x) |

Reading gains less than writing: decoding strings and converting timestamps to local time take most of the time, the same with or without generated code.
