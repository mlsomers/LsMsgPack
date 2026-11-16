# Serialization callbacks

A class can run code of its own while the serializers write or read it: compute a value just before it's written, set up state before the members are filled in, or validate the object once it's read. Implement one or more of these interfaces (namespace `LsMsgPack`, package LsMsgPack.Core):

| Interface | Called |
|---|---|
| `IMsgPackOnSerializing` | before the members of the object are written |
| `IMsgPackOnSerialized` | after the members of the object are written (and those of the objects below it) |
| `IMsgPackOnDeserializing` | when the object is created, before the members of the data are set |
| `IMsgPackOnDeserialized` | after every member is set (and the objects below it are done) |

```csharp
using LsMsgPack;

public class Order : IMsgPackOnSerializing, IMsgPackOnDeserialized
{
  public int Id { get; set; }
  public List<OrderLine> Lines { get; set; }
  public decimal Total { get; set; }

  public void OnSerializing()
  {
    Total = Lines?.Sum(l => l.Amount) ?? 0; // written with the object
  }

  public void OnDeserialized()
  {
    if (Id <= 0)
      throw new InvalidDataException("An order needs an id.");
  }
}
```

Both serializers (LsMsgPackDebug's `MsgPackSerializer` and LsMsgPack's `LsMsgPackSerializer`) call them, and so do the web formatters, the stream writer and reader, and `ReadRows`/`EnumerateRows` of LsMsgPack.Data (which call `OnDeserializing` and `OnDeserialized` on each object they make from a row). The source generator's code and the compiled readers and writers run between the callbacks, so a class with callbacks keeps its speed.

## System.Text.Json's interfaces

A class that implements System.Text.Json's `IJsonOnSerializing`, `IJsonOnSerialized`, `IJsonOnDeserializing` or `IJsonOnDeserialized` (namespace `System.Text.Json.Serialization`) gets those called the same way, so models shared with System.Text.Json work unchanged. When a class implements both the LsMsgPack interface and System.Text.Json's for the same callback, only the LsMsgPack one is called.

System.Runtime.Serialization's `[OnSerializing]`/`[OnDeserialized]` attribute methods and `IDeserializationCallback` aren't called.

## Validating what you read

`OnDeserialized` is the place to check an object read from data you don't trust (see [security.md](security.md)). An exception thrown there is passed on as it is: reading stops with it, whatever `ReadErrors` says (it's never skipped and reported like a value that doesn't convert).

For required members, ranges, lengths and patterns, use the attributes of `System.ComponentModel.DataAnnotations` and validate them in `OnDeserialized`:

```csharp
using System.ComponentModel.DataAnnotations;

public class Review : IMsgPackOnDeserialized
{
  [Required, StringLength(100)]
  public string Author { get; set; }

  [Range(1, 5)]
  public int Rating { get; set; }

  public void OnDeserialized()
  {
    Validator.ValidateObject(this, new ValidationContext(this), validateAllProperties: true); // throws a ValidationException
  }
}
```

In ASP.NET Core, model validation already checks these attributes after the formatter has read the body (`[ApiController]` returns 400 on its own), so a class only read by the web formatters doesn't need this.

## Details

- **Objects only.** Callbacks are called for objects with properties (classes and structs), not for collections or dictionaries, even when they implement the interfaces (as System.Text.Json).
- **Order.** A parent's `OnSerializing` comes before, its `OnSerialized` after the callbacks of the objects below it. When reading, the parent's `OnDeserializing` comes first, its `OnDeserialized` last. The objects next to each other (the values of the properties, the elements of a list) are called in the order the serializer writes or sets them: LsMsgPackDebug sets properties in another order than LsMsgPack.
- **Constructors that take values** ([immutable classes](schema.md#immutable-classes), records with positional parameters): their values are read before the constructor runs, so the objects passed to it are done first. `OnDeserializing` is called right after the constructor, before the other members are set.
- **Extension data** ([ExtensionData.md](ExtensionData.md)): the kept members are in the extension data member when `OnDeserialized` is called.
- **Structs.** `OnSerializing` is called on a copy of the struct, which is then written: what it sets is written, but your own variable doesn't change. When reading, the callbacks are called on the struct that is being filled, so what `OnDeserialized` sets is in the value you get.
- **Polymorphism.** A value whose type differs from the declared type gets the callbacks of its own type.
- **Cycles.** With `IgnoreCycles`, an object that is written as nil (because it's already being written) gets no callbacks.
- **Once per object, also when a write is repeated.** With a cached schema (`WriteSchemaReference`), a write that needs a class or member the schema doesn't have yet is done again with the grown schema. The callbacks the first attempt called aren't called again: each object gets `OnSerializing` and `OnSerialized` once (keep them deterministic: the second attempt writes the values the first one's `OnSerializing` set). A read that's repeated for the same reason creates its objects again; the objects you get were called once each, the ones thrown away may have had their callbacks called too.
- **Values left out.** A property whose value `FilterDefaultValues` leaves out (null, or the default) has no object, so no callbacks. An object that's skipped while reading (a class the schema doesn't have, a type id that isn't found, reported in [ReadDifferences](ReadDifferences.md)) gets none either.
- **NativeAOT and trimming.** The LsMsgPack interfaces are plain interface calls. System.Text.Json's are found by name and called through the interface: on .NET 8 and later directly, on .NET Framework and .NET Standard by reflection.
