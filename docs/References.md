# Shared objects and cycles

By default every value is written where it appears. An object that two members refer to is written twice and read back as two copies, and an object that refers back to one containing it (a parent, the object itself) is a cycle: writing it fails with a `MsgPackException` at `MaxDepth` (256 levels). `ReferenceHandling` changes that:

| `ReferenceHandling` | An object met again | Read back |
|---|---|---|
| `None` (default) | written again; a cycle fails | copies |
| `IgnoreCycles` | in a cycle: nil (as System.Text.Json's `ReferenceHandler.IgnoreCycles`); side by side: written again | null in the cycle, copies |
| `Preserve` | a reference to its first occurrence | the same instance |

```csharp
using LsMsgPack;

LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { ReferenceHandling = ReferenceHandling.Preserve });
// LsMsgPackDebug: new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve }

Customer customer = new Customer() { Name = "Ann" };
List<Order> orders = new List<Order>()
{
  new Order() { Id = 1, Customer = customer },
  new Order() { Id = 2, Customer = customer },
};
List<Order> read = serializer.Deserialize<List<Order>>(serializer.Serialize(orders));
// read[0].Customer and read[1].Customer are one object
```

Both serializers (LsMsgPackDebug's `MsgPackSerializer` and LsMsgPack's `LsMsgPackSerializer`) write the same bytes and read the same values. The option is off by default because other MsgPack libraries don't know the reference (see [Other readers](#other-readers)). See [IgnoreCycles](schema.md#cycles) for the other setting.

## What is tracked

Objects with properties (classes) and collections: arrays, lists, sets, dictionaries, any `IEnumerable` written as an array or a map. Not strings and `byte[]`, structs (a value, not an identity), framework types written as one value or a fixed array (`Uri`, `Version`, `BigInteger`, tuples, `KeyValuePair`), multidimensional arrays, and the keys of a dictionary: a key is always written in full and is never the target of a reference.

A value with a [converter](Converters.md) keeps the identity of the original value: the same `Money` written twice is one surrogate, then a reference to it, and read back one `Money`.

## What is written

Nothing is written at the first occurrence. The second time, the value is an extension of type 5 whose content is the **path** to the first occurrence: the steps from the root value down to it, each a MsgPack positive integer, without an array header (the extension's length ends it).

- The root value is the value of the call (after the schema, when there is one). Each `MsgPackStreamWriter.WriteObject` is a root of its own: references never point into another value of a stream. In the body of a [head](Streaming.md#one-schema-for-a-root-value) (`WriteHead<List<Order>>()`, then a `WriteObject` per element) the paths start at the body's root value, the element's position first, so the body is still what `Serialize` of the whole list writes and can be read whole or element by element (`ReadObject`). References still don't point into another element.
- A step into an array is the index of the element. A step into a map is the number of the entry (0 for the first entry, also in a dictionary with keys of any type), which stands for that entry's value. Entries that aren't written don't count (values `FilterDefaultValues` leaves out, nils `TrimTrailingNulls` trims).
- A value with a type id is written as `{ "": typeId, "@": content }`: the path goes through that map as through any other (step 1 is the content), and a path to such a value points to the map.
- The integers are always big-endian in their smallest format, whatever `EndianAction` says.
- The empty path is the root itself.

Example: `List<Order>` in the array layout without the schema, `Order` = `[Id, Customer, Extra]`, and the fourth order's customer is the first order's. The path is `[0, 1]` (element 0, entry 1), written `d5 05 00 01`.

A reference where the declared type differs from the type of its target gets the type id, as the value would: `{ "": typeId, "@": <reference> }`. A reader that has to read the bytes again (below) then knows the type.

## Reading

With `Preserve`, a reader registers each object and collection by its path when it's created, before its members and elements are read, so a cycle back to an object or a collection that contains it resolves: `node.Parent`, `node.Self`, a list that contains itself, a child whose member refers to the list it's in.

- **Made of their values.** An object made by a [constructor that takes values](schema.md#immutable-classes) (records with positional parameters), a struct, and a collection made of its elements (immutable and frozen collections, a stack, a collection without a parameterless constructor and `Add`) don't exist before their values are read: a cycle back through one is an error (as with System.Text.Json's `Preserve`). A reference to one that is complete (shared, not a cycle) is fine.
- **A target that wasn't made**: the path points to a value the class doesn't have (skipped), or to one read as another type. The reader reads the bytes at the path again, so the result is a copy instead of an error. Later references to the same place get that copy.
- **A path to nothing**, or to a value that doesn't come before the reference, is an error that follows [`ReadErrors`](ReadDifferences.md): with `ReportAndContinue` the member is skipped and reported.
- **Extension code 5 is reserved**: with `Preserve`, a [binary converter](Converters.md) or a custom extension with type code 5 is refused (by LsMsgPack when the serializer is created, by LsMsgPackDebug when it writes; without `Preserve` it stays usable, and an extension of type 5 is its value there).

## Other readers

The bytes are valid MsgPack: a reader that doesn't know the reference sees an extension of type 5.

- **LsMsgPack without `Preserve`** (an older client, another setting): every reference is read from the bytes at its path, so shared objects are read as copies, today's behavior. A cycle can't be read that way and fails with a `MsgPackException`.
- **The TypeScript client** (`@lsmsgpack/client`) reads copies by default and the same instances with `references: 'preserve'`, which also makes it write references in request bodies (see [TypeScriptClient.md](TypeScriptClient.md#shared-objects-and-cycles)).
- **Other libraries** see an unknown extension where the second occurrence was. Leave `Preserve` off for data that other libraries read, or for a public API.

## Extension data

[Extension data](ExtensionData.md) keeps the members a class doesn't have and writes them again, at another place in the data (after the class's own members). A path in a kept value would then point to the wrong value, so the readers resolve it when they keep it: every reference in a kept value becomes a copy of what it points to, and the kept data stands on its own. A cycle inside a kept value can't be copied: it is an error that follows `ReadErrors`. A known member that refers into a kept value (its first occurrence is in a member the class doesn't have) is read from the bytes at its path, a copy; written back, it is written in full.

## Callbacks

[Callbacks](Callbacks.md) are called once per object: an object written again as a reference gets no `OnSerializing`/`OnSerialized` there, and a reference read back gives the object already made, which isn't called again.

## Speed

`Preserve` keeps a table of every object and collection written or read in a call, and LsMsgPack writes the paths in one pass over a value that has references. The source generator's code and the compiled readers and writers are used with it as without: they write and read the values that can't be shared themselves (strings, numbers, dates...) and leave the objects and collections to the plans that keep the table. Without the option nothing changes: the same bytes and the same speed.

100 invoices (with shared objects), LsMsgPack on .NET 8, i7-7700K:

| | Write | Read |
|---|---|---|
| Default options | 2.95 ms | 4.5-4.8 ms |
| `Preserve` | 4.9-5.1 ms | 6.9-7.1 ms |
| Map layout with property names | 3.9 ms | 4.8 ms |
| The same with `Preserve` | 6.3-6.4 ms | 7.3-7.5 ms |
| System.Text.Json (without its `Preserve`) | 19.3 ms | 34.1 ms |
