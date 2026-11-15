# Converters

A converter writes the values of one type as the values of another type, its *surrogate*, and reads them back. Use one when a type can't, or shouldn't, be written as its own properties:

- **Value objects** as their inner value: an `OrderId` as a `long`, a `Money` as `"12.50 EUR"`.
- **Types of another library** (no setters, a factory method, private state) as a class of your own.
- **A built-in format you need to change**, such as `DateTime` as Unix milliseconds for a peer that expects them.

Both serializers (LsMsgPackDebug's `MsgPackSerializer` and LsMsgPack's `LsMsgPackSerializer`) use the same converters and write the same bytes.

## Writing a converter

Derive from `MsgPackConverter<T, TSurrogate>` (namespace `LsMsgPack.Converters`):

```csharp
using LsMsgPack.Converters;

public sealed class MoneyConverter : MsgPackConverter<Money, string>
{
  public override string ToSurrogate(Money value)
  {
    return value.Amount.ToString(CultureInfo.InvariantCulture) + " " + value.Currency;
  }

  public override Money FromSurrogate(string surrogate)
  {
    int space = surrogate.IndexOf(' ');
    return new Money(decimal.Parse(surrogate.Substring(0, space), CultureInfo.InvariantCulture), surrogate.Substring(space + 1));
  }
}
```

Then register it in one of three ways:

- **On your own type**, with an attribute. The converter is created once, by its public parameterless constructor:

  ```csharp
  [MsgPackConverter(typeof(MoneyConverter))]
  public sealed class Money { ... }
  ```

- **In the options**, for types you can't change (or to change a built-in format). These win over an attribute for the same type:

  ```csharp
  LsMsgPackOptions options = new LsMsgPackOptions();
  options.Converters = new MsgPackConverter[] { new MoneyConverter(), new UnixMillisecondsConverter() };
  ```

- **On one property or field**, for that member's value only. Its `T` is the member's type, or the underlying type of a nullable member. It wins over the type's attribute and the options for that member:

  ```csharp
  public class Order
  {
    [MsgPackConverter(typeof(UnixMillisecondsConverter))]
    public DateTime Created { get; set; }      // a long
    public DateTime Shipped { get; set; }      // a timestamp, as usual

    [MsgPackConverter(typeof(UnixMillisecondsConverter))]
    public DateTime? Cancelled { get; set; }   // a long, or nil
  }
  ```

  A member's converter converts the member's own value, not the elements of a collection: a converter on a `List<DateTime>` property must convert `List<DateTime>`. It isn't inherited by the same property of a derived class (`override`), and a value of a class deriving from the member's type is written as an object of its own class. A converter of another type than the member's throws when the member is first written or read.

The writer and the reader need the same converters.

## What is written

The surrogate is written exactly as a value declared as the surrogate type would be. A `Money` property holds the string `"12.50 EUR"`, and a surrogate class gets its own schema entry. The MsgPack explorers, the MCP tools, the TypeScript client and other languages see that string or that object, not opaque bytes.

- **Everywhere a value of the type is:** properties, fields, elements of arrays, lists and sets, dictionary keys and values, the root value.
- **Exactly the type:** a converter for `Label` isn't used for a `SpecialLabel` deriving from it. That value is written as an object of its own class, with a type id, and read back as such.
- **Null is never converted:** a null value is nil, and nil is read as null (the default of a struct) without calling the converter. A surrogate of null is written as nil too.
- **Struct converters cover their nullable:** a converter for `Temperature` also writes and reads `Temperature?`. A converter for `object`, an interface, an abstract class or a `Nullable<T>` is refused.
- **Default values** (`FilterDefaultValues`) are decided on the property's own value, before it's converted: a `Temperature` of 0 is left out like any default struct.
- **Where the declared type differs** (a property declared as `object`), the value is wrapped with its type id, as other values are: `{ "": "Money", "@": "12.50 EUR" }`. The reader finds `Money` by the type id and converts the content.
- **A surrogate type with derived classes** (an abstract `ShapeDto` with `CircleDto` and `SquareDto`) is written with the type id of the surrogate's class, and read back through the converter.
- **Property names and type ids** are never converted, also with a converter for `string` or `int`.
- **Converters may follow each other** (the surrogate type has a converter of its own), but a chain that leads back to a converted type is refused when the options are set.

A converter for a built-in type replaces the built-in format everywhere, also in the arrays of numbers that are otherwise written in bulk and in the generated and compiled code.

## The schema

With the indexed schema, the surrogates' classes have entries and the converted types don't have entries of their own. A converted type is only named in the schema where a type id needs it, without properties. In the described schema (`SchemaVersion.Described`), a converted property (by its type or its own converter) has the surrogate's type: `["Price", "string"]`, and the entry of a converted type that a type id names is `["@", "string"]`. Complete schemas (`CompleteSchemas`) follow the surrogate types, and the classes deriving from a converted class.

## Binary converters and blit converters

A binary converter writes a value as bytes it makes itself: binary data (bin), or an extension when you give it an extension type code (0 to 127). Readers take both: bin, or an extension of that code. Derive from `MsgPackBinaryConverter<T>`:

```csharp
public sealed class RgbConverter : MsgPackBinaryConverter<Rgb>
{
  public override int GetLength(Rgb value) => 3;
  public override void Write(Rgb value, Span<byte> destination) { destination[0] = value.R; destination[1] = value.G; destination[2] = value.B; }
  public override Rgb Read(ReadOnlySpan<byte> data) => new Rgb(data[0], data[1], data[2]);
}
```

`Write` writes exactly `GetLength` bytes. LsMsgPack lets it write straight into its buffer and reads the bytes in place, without an array in between; LsMsgPackDebug writes the same bytes.

The **blit converters** are binary converters that copy the memory of structs without references (`unmanaged`) as it is:

| Converter | Converts |
|---|---|
| `BlitConverter<T>` | one `T` |
| `BlitArrayConverter<T>` | `T[]` |
| `BlitListConverter<T>` | `List<T>` |
| `BlitMemoryConverter<T>` | `Memory<T>` (read into a new array) |
| `BlitReadOnlyMemoryConverter<T>` | `ReadOnlyMemory<T>` (read into a new array) |

```csharp
options.Converters = new MsgPackConverter[] { new BlitArrayConverter<Vector3>() };   // Vector3[] as bin

public class Scan
{
  [MsgPackConverter(typeof(BlitArrayConverter<Sample>))]
  public Sample[] Samples { get; set; }
}
```

An array of 1000 `Vector3`s is then one copy of 12,000 bytes instead of 3,000 encoded floats. Floats and doubles get smaller (4 and 8 bytes instead of 5 and 9), but small integers don't: a `long` of 0 takes 8 bytes instead of 1, and padding between fields is written too.

Measured with LsMsgPack (1000 values per call, .NET 8, one machine):

| 1000 values | Per field | Blit |
|---|---|---|
| struct of 3 floats: write / read | 17.3 / 27.6 µs | 0.78 / 0.77 µs |
| struct of 3 floats: size | 16.0 KB | 12.0 KB |
| struct of a long, an int and a double: write / read | 20.2 / 31.9 µs | 1.7 / 1.6 µs |
| struct of a long, an int and a double: size | 20.0 KB | 24.0 KB |

What you give up:

- **No compatibility across versions of the struct.** Reading only checks that the length is a whole number of values. A field added or removed changes the size and throws, but a change that keeps the size (another field type, another order) reads other values.
- **Byte order.** The memory is written as it is, little-endian on every current .NET platform. On a big-endian machine the blit converters throw.
- **Field values aren't checked.** A `bool` or an enum gets whatever the bytes say. Padding bytes between fields are written as they are in memory (zero for a new array or struct).
- **Other languages and the tools** see binary data (the described schema says `bin`, or `any` for an extension), not the fields.

## Converters or custom extensions?

A custom extension (`LsExtension<T>`, or `ICustomExt` in LsMsgPackDebug) writes a value as raw bytes in a MsgPack extension type, and only in its own serializer. A binary converter does the same in both serializers, with or without an extension type. Both are compact, but other languages and the debugging tools only see bytes. A surrogate converter writes ordinary MsgPack values, so anything that reads MsgPack can read them.

## Source generator, analyzers and TypeScript models

- **The source generator** sees `[MsgPackConverter]` on types and members: it describes the surrogate type instead of the converted type, and leaves a member with its own converter to the converter. It can't see the converters of the options. For NativeAOT, add their surrogate types to your context: `[LsMsgPackSerializable(typeof(MoneyDto))]`.
- **The analyzers** don't check the members of a type with `[MsgPackConverter]` (they aren't written), but they do check its surrogate. For a member with its own converter, the rules about types (LSMP004, LSMP008, LSMP009) are about the surrogate: a `JsonElement` property with a converter to `string` is fine.
- **lsmsgpack-ts** writes the surrogate's TypeScript type for a type or member with `[MsgPackConverter]` (a `Money` property is a `string`, a `DateTime` with a converter to `long` a `number`). Converters of the options aren't known to it.
