# Extension data: keeping the members a class doesn't have

When a class reads data that has a member the class doesn't have, the value is skipped, as it is with the JSON serializers. That's fine until a service reads an object, changes it and writes it back: the members it didn't know are gone. With rolling upgrades this happens all the time. The older version of a service reads what the newer one wrote, and the newer fields are lost.

Give the class a member that keeps them, and they are written again:

```csharp
using LsMsgPack;

public class Order
{
  public int Id { get; set; }
  public string Customer { get; set; }

  public MsgPackExtensionData Extra { get; set; } // what this version doesn't know
}
```

The newer version can have any members:

```csharp
public class Order
{
  public int Id { get; set; }
  public string Customer { get; set; }
  public decimal Discount { get; set; }
  public Address ShipTo { get; set; }
  public List<string> Tags { get; set; }
}
```

An older service that reads such an order, changes `Customer` and writes it back keeps `Discount`, `ShipTo` and `Tags` in `Extra`. The newer service reads them back. Both serializers (LsMsgPackDebug's `MsgPackSerializer` and LsMsgPack's `LsMsgPackSerializer`) do this and write the same bytes.

## The two forms

- **`MsgPackExtensionData`**: found by its type (a public property or field; mark a non-public one with `[MsgPackExtensionData]`). It keeps each value as its raw MsgPack bytes. The bytes are written back as they were read: numbers keep their width, strings, binary data, extensions and timestamps their format.

  ```csharp
  if (order.Extra != null && order.Extra.TryGetRaw("Discount", out byte[] raw))
    discount = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false }).Deserialize<decimal>(raw);
  ```

  The raw values are in the byte order of the MsgPack specification and never use the indexed schema: objects in them are maps keyed by their names, type ids are type names. So any reader can read them, also one without the schema. `Names`, `Positions`, `Count`, `SetRaw`, `Remove` and `Clear` do the rest. `SetRaw` takes exactly one MsgPack value.

- **A dictionary**, as with System.Text.Json: an `IDictionary<string, object>` (or `Dictionary<string, object>`, or a class implementing it with a parameterless constructor) marked with `[MsgPackExtensionData]`. System.Text.Json's and Json.NET's `[JsonExtensionData]` are recognized too, so the same class works with both serializers.

  ```csharp
  [MsgPackExtensionData]
  public Dictionary<string, object> Extra { get; set; }
  ```

  The dictionary gets plain .NET values: the numbers, strings, `byte[]`, `DateTime` and extension values the readers unpack, an array as an `object[]`, a map as a `Dictionary<string, object>` (or a `Dictionary<object, object>` when a key isn't a string). They're written back as such values: numbers in the smallest format, whatever width they were read with. You can put values in yourself, objects of your own classes too: those are written as maps of their names.

A class has at most one such member (two throw a `MsgPackException`, also when one is in a base class; analyzer [LSMP011](Analyzers.md#lsmp011-invalid-extension-data-member) reports it while you type). It is never a property in the data itself. When the member is null, reading creates the container and sets it. Without a setter, the values are only kept when the constructor creates the container (`{ get; } = new ...`).

`[JsonExtensionData]` on another type, such as System.Text.Json's `Dictionary<string, JsonElement>` or `JsonObject`, throws a `MsgPackException` naming the member (LSMP011): LsMsgPack can't fill those.

## What is kept

A value is kept by its name (the key of a map, or the name in the indexed schema), or by its position:

| How the data was written | Kept |
|---|---|
| Maps with names (`ObjectLayout.Map`, no indexed schema) | Every key the class doesn't have, by name. |
| Arrays without the indexed schema (`ObjectLayout.Array`, `UseIndexedSchema = false`) | The values after the class's last property, by position (`Positions`). A dictionary keeps no positions: they are reported as `ExtraValue`. |
| Indexed schema, version 2 (`SchemaVersion.Described`), any layout | Every name the class doesn't have. The objects in the values are made maps of their names, the type ids type names: the schema says which class each object is. |
| Indexed schema, version 1 (`SchemaVersion.Names`, the default) | Only values without arrays or maps (numbers, strings, binary data, extensions, timestamps, booleans). |

Version 1 of the schema only has the names of the properties. For a property the class doesn't have, nothing in the data says what its value holds. In the array layout `["Main st", "Town"]` can be a list of strings or an `Address` written by position, and a map with number keys can be a `Dictionary<int, ...>` or an object. Written back under another schema, such a value could be read as other properties than it had. So with version 1 an unknown member whose value is an array or a map is an error, which follows `ReadErrors` ([ReadDifferences.md](ReadDifferences.md#values-that-cant-be-read-readerrors)): `FailFast` (the default) throws a `MsgPackException`, the other modes skip the value and report it as `InvalidValue`. To keep such values, write the data with `SchemaVersion = SchemaVersion.Described` (see [schema.md](schema.md#version-2-the-declared-types)).

Nil is never kept: nil is how a value is left out. Not kept either: the properties of a collection written with `[SerializeEnumerable(SerializeProperties = true)]`, and keys that are numbers without the indexed schema (custom property id resolvers that give numbers): those are skipped as before.

## What is written

The kept values are written after the class's own properties:

- A name the class has itself is not written again: the property wins.
- With the indexed schema, the kept names are added to the class's entry, after its own names. In the array layout an object writes nil for a kept name it doesn't have, up to its last kept value (and trims as `TrimTrailingNulls` says). A new name adds to the schema of the session: with `WriteSchemaReference` that is a new schema id.
- Without the schema, a map gets the values kept by name, an array the values kept by position. Values that have no place in the layout (names in an array without the schema, positions in a map or with the schema) are not written.
- The keys `""` and `"@"` (type ids, wrapped content) are never written from a dictionary.

## The TypeScript client

The [TypeScript client](TypeScriptClient.md#the-models) does the same for the generated models without a member for it: a property the models don't have is an own property of the instance, and it is written back with its .NET name and the type the described schema gave it. Objects of classes the models don't have are written with their .NET names and kinds, values with a type id keep it. With the plain schema the names are known but not the types: the values are written as they were read.

## Reporting

Kept members are differences, not errors: `ReadDifferences` reports them as `DifferenceKind.KeptProperty` ("not a property of the class, kept as extension data") instead of `UnknownProperty`, with the same hints for misspelled names. See [ReadDifferences.md](ReadDifferences.md).

## Speed

Classes without an extension data member are written and read as before. LsMsgPack writes a class with one by its compiled writers (after `CompileWritersAfter` objects, as any class), then the kept values. It reads it with its usual readers (not the compiled ones) until an object has a member the class doesn't have. That object is read again by the conversions LsMsgPackDebug uses (the values unpacked first), which keep the values. The source generator writes no code for such a class.

One object of 8 properties, LsMsgPack, .NET 8 (default options / maps with names):

| | Write | Read |
|---|---|---|
| Without an extension data member | 127 / 151 ns | 187 / 178 ns |
| With one, nothing kept | 142 / 176 ns | 217 / 263 ns |
| With 4 of the 8 values kept | 474 / 307 ns | 1258 / 2195 ns |

The source generator leaves such classes to the runtime (it still generates their accessors for NativeAOT), and the analyzers don't check the extension data member.
