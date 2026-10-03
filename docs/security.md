# Security: type injection

LsMsgPack and LtMsgPack support polymorphic object models out of the box: when a value's type differs from its declared type, a type id is written, and when reading, that type id picks the class to create (see [Schema, type ids and polymorphic class hierarchies](schema.md)). This document explains what that means when you read data from a source you don't trust, what the serializers do to protect you, and what you should do yourself.

- [The threat](#the-threat)
- [What the schema hashes do and don't protect](#what-the-schema-hashes-do-and-dont-protect)
- [Protection built in](#protection-built-in)
- [The type guard](#the-type-guard)
- [Declare the types you expect](#declare-the-types-you-expect)
- [How objects are created](#how-objects-are-created)
- [What else to keep in mind](#what-else-to-keep-in-mind)

The threat
----------

When data picks the type, whoever writes the data picks the code that runs while it's read. Creating an instance runs the type's constructor, and filling in its properties runs their setters. Some framework and library types do dangerous things in those places: they start processes, load assemblies, open files or call methods named by their properties. These are known as *gadgets*. This is the same class of vulnerability as `BinaryFormatter`, Json.NET with `TypeNameHandling`, or `XmlSerializer` with types from the input.

Two things decide how exposed you are:

1. **Which types the data can name.** A type id is resolved by name (see [Polymorphic class-hierarchy support](schema.md#polymorphic-class-hierarchy-support)): the framework's core library, the framework's collection assemblies, and every assembly that has been cached so far (your root types' assemblies, the assemblies of declared types, and what you registered with `CacheAssemblyTypes`). `WildGooseChaseResolver` extends this to every assembly loaded in the process.
2. **Where the data can use them.** A type id is only consulted where a value is read, and the declared type of that place (a property, a collection element or the root you deserialize) limits what fits there.

What the schema hashes do and don't protect
-------------------------------------------

A [cached schema](schema.md#the-indexed-schema) is identified by a hash of its contents (`SchemaId`, a truncated SHA-256). A schema that's cached under an id can't be swapped for another schema with the same id, so a client can't make the server read later messages with a schema it planted (*schema poisoning*).

The hash says nothing about whether the data can be trusted. Anyone can send a schema inline and name any type in it, and anyone can write a type id into the body. The protections below apply to every way a type gets picked: type names written without a schema, type names in an inline or cached schema, and types returned by your own type resolvers (including [resolving by signature](schema.md#resolving-by-signature)).

Protection built in
-------------------

These checks are always on, in both serializers. They have no settings.

### The type must fit where it goes

A type picked by the data must be assignable to the declared type. If it isn't, deserializing throws a `MsgPackException` **before** an instance is created:

```
The type id "Process" resolves to System.Diagnostics.Process, which cannot be assigned to MyApp.IShape. Deserializing stops before creating it (see docs/security.md).
```

If this check did not exist, the type would be created and its setters would be called, and only assigning the finished object to the property would fail. By then, the constructor and setters of a gadget would have already run.

This makes your declared types the boundary: a property declared as `IShape` only accepts implementations of `IShape`. For a property declared as `object`, an interface that many types implement, or a base class that isn't yours, this check doesn't help much. Use [the type guard](#the-type-guard) for those.

### The data can't load assemblies

Type names that contain an assembly name or generic arguments in brackets (`System.Diagnostics.Process, System.Diagnostics.Process` or ``List`1[[MyApp.Foo, MyApp]]``) are refused with a `MsgPackException`. `Type.GetType` and `Assembly.GetType` would load the assembly they name, and loading an assembly runs its module initializer. The serializers never write such names: they write `Name`, `Namespace.Name` (`AddTypeIdOption.FullName`), `List<Foo>` and `Foo[]`. This applies to names in a schema as well, which are resolved when the schema is read.

If your own type resolver writes and reads names like these, it still can: custom resolvers are asked before the built-in name lookup.

The type guard
--------------

```csharp
settings.TypeGuard = new AllowedTypesGuard()
  .AllowAssemblyOf(typeof(IShape))   // all types in your model's assembly
  .Allow(typeof(PluginShape));       // and single types from elsewhere
```

`MsgPackOptions.TypeGuard` (`IMsgPackTypeGuard`, null by default) decides which types the data may pick. It's consulted after the type resolvers and the indexed schema have resolved the type, and before anything creates it. If the guard refuses the type, deserializing throws a `MsgPackException` that names the type and the property. It's part of the options, so it works the same in LsMsgPack (`MsgPackSettings`), LtMsgPack (`LtMsgPackOptions`) and the web formatters. Use `MsgPackOptions.Default_TypeGuard` to set it for all settings created afterwards.

The guard is only asked when the data picks a different type than the declared one. Values of their declared type are never checked, even with `AddTypeIdOption.Always`.

### AllowedTypesGuard

The included allow-list allows:

- the types you pass to `Allow`, and all types of the assemblies you pass to `AllowAssembly` / `AllowAssemblyOf`;
- the values the serializers write themselves: primitives, enums, `string`, `decimal`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `Uri`, `DateOnly`, `TimeOnly`, and `object`;
- the framework's collections (`List<T>`, `Dictionary<TKey, TValue>`, `HashSet<T>`, `Queue<T>`, the concurrent collections, `ObservableCollection<T>`, ..., and `ArrayList` and `Hashtable`).

**Generic types and arrays are only allowed when their arguments are.** A type id `List<Process>` on a property declared as `object` is refused unless `Process` is allowed. The elements of that list are created as its declared element type, without type ids of their own, so the list's type is the only chance to stop them. `Allow(typeof(Envelope<>))` allows `Envelope<T>` for every allowed `T`. `Allow(typeof(Envelope<Foo>))` allows exactly that type.

Decisions are cached per type, so a polymorphic value costs one dictionary lookup. Configure the guard before you use it. Adding types later is safe (it clears the cache), and one guard can be shared by several settings and threads. To add rules of your own, override `Decide`.

### Your own guard

```csharp
public class ShapeGuard : IMsgPackTypeGuard
{
  public bool IsAllowed(Type type, Type assignedTo, FullPropertyInfo assignedToProp, MsgPackOptions settings)
  {
    return type.Namespace == "MyApp.Shapes" && type.Assembly == typeof(IShape).Assembly;
  }
}
```

`assignedTo` is the declared type, and `assignedToProp` is the property (null for the root, collection elements and dictionary entries). The type is always assignable to `assignedTo`, because that's checked before the guard is asked.

### When to use a guard

- The data comes from outside your trust boundary: request bodies of a web API, messages from clients, files that users upload, queues that others can write to.
- And your model has places where the declared type accepts more than you want: `object`, `List<object>`, `Dictionary<string, object>`, non-generic collections, broad interfaces (`IEnumerable`, `IDisposable`, `ISerializable`...), or base classes of other libraries.
- Or you use `WildGooseChaseResolver`, which can find types in every loaded assembly.

Data that only your own code writes and reads, such as files, caches or queues that nobody else can write to, doesn't need a guard.

### Web formatters

The web formatters read request bodies, so they read data from clients. Set the guard on the options of the media types you accept:

```csharp
AllowedTypesGuard guard = new AllowedTypesGuard().AllowAssemblyOf(typeof(Invoice));
LtMsgPackHttpOptions options = new LtMsgPackHttpOptions();
options.XLsMsgPack.TypeGuard = guard;
options.Plain.TypeGuard = guard;
```

Alternatively, set `MsgPackOptions.Default_TypeGuard` before the formatters are created.

Declare the types you expect
----------------------------

The declared types of your model are your first line of defense, and they cost nothing:

- **Avoid `object`, `List<object>`, `object[]` and `Dictionary<string, object>` for data you don't trust.** Every element of a `List<object>` can carry its own type id, and every type the resolvers can find is assignable to `object`. Each element is then created as whatever type the sender named. Declaring `List<IShape>` limits the elements to implementations of `IShape`. Declaring `List<Square>` doesn't accept type ids for other types at all.
- The same goes for the root: `Deserialize<object>(bytes)` lets the data choose the type of the root. Deserialize the type you expect.
- Prefer your own interfaces and base classes over framework interfaces. Many unrelated framework types implement `IEnumerable` or `IDisposable`.
- If you do need `object` (e.g. a property bag), add a [type guard](#the-type-guard).

How objects are created
-----------------------

`MsgPackOptions.ObjectCreation` decides how both serializers create the objects they fill in:

| Choice | Constructor | Without a parameterless constructor |
|---|---|---|
| `Constructor` | The parameterless one (public or not), its exceptions are passed on | `MsgPackException` |
| `ConstructorOrUninitialized` (**default**) | The parameterless one, its exceptions are passed on | Created uninitialized |
| `Uninitialized` | Never | Created uninitialized |

The finalizer of an object created uninitialized is always suppressed (`GC.SuppressFinalize`).

An *uninitialized* object (`RuntimeHelpers.GetUninitializedObject`) has all its fields zero. Its constructor doesn't run, and neither do the initializers of its fields and properties, so a property that isn't in the data stays `null` or `0` instead of getting the value the class gives it. DataContractSerializer creates `[DataContract]` types this way, while System.Text.Json, Json.NET and XmlSerializer always run a constructor.

Why the finalizer is suppressed: an object that was never constructed still gets finalized, and some finalizers fail on such an object. `System.Threading.PeriodicTimer` throws a `NullReferenceException` in its finalizer, which ends the process. It's found by its short name, so before this setting, a 23-byte message to a model with an `object` property ended the process at the next garbage collection (tested with both serializers on .NET 8).

Weak references (`WeakReference`, `WeakReference<T>` and classes derived from them) are never created without their constructor, in any mode. The garbage collector cleans them up itself, ignores `GC.SuppressFinalize`, and crashes the process on an uninitialized one (a segmentation fault on .NET 8, 9 and 10). Reading one throws a `MsgPackException`.

A [type guard](#the-type-guard) refuses all such framework types before they're created. Of the 12 public CoreLib classes with a finalizer, these were the only ones that ended the process when created uninitialized.

Collections and dictionaries always use their constructor when they have one, in every mode, because an uninitialized collection doesn't work. With `Constructor`, a collection without a parameterless constructor (and without a constructor taking its elements) is refused too.

What else to keep in mind
-------------------------

- **Your own setters run with the sender's values.** Allowing a type means its constructor and setters may run with any values. Validate in setters that do more than store a value, or validate the object after deserializing.
- **Types without a parameterless constructor** are created without running a constructor, by default without their finalizer. See [How objects are created](#how-objects-are-created).
- **Static properties** are left out by `FilterStatic`, one of the default `StaticFilters`, so the data can't change state that the whole application shares. If you replace the static filters, keep `FilterStatic` in the list. Without it, settable public static properties are written with every instance and set again when reading.
- **Deep nesting**: both serializers refuse data with arrays and maps nested deeper than `MsgPackOptions.MaxDepth` (256 by default) and throw a `MsgPackException`. Without a limit, a few kilobytes of nested arrays exhaust the stack, and a stack overflow ends the process. Writing stops at the same depth, so an object graph with a cycle throws instead of overflowing the stack.
- **Other resource use** (such as very large lengths) is a separate topic. A `SchemaStore` caches at most `MaxSchemas` received schemas.
- **Custom type resolvers** are your code: a resolver that maps names to types decides what the data can reach. The guard is still asked about the types it returns.
