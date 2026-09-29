# Compatibility with other MsgPack libraries

MsgPack standardizes the value types (integers, strings, binary data, arrays, maps, extensions), their bytes and one extension: the timestamp. How a .NET class, a `Guid` or a `decimal` is written is up to each library (see the proposal [msgpack/msgpack#334](https://github.com/msgpack/msgpack/pull/334)). So every library can read LsMsgPack's bytes, but whether it gets the objects back depends on the settings below.

*Tested* means checked by the `LsMsgPackInteropTests` project (MessagePack-CSharp 3.1.10 and Nerdbank.MessagePack 1.3.88, with the invoice model of the benchmark), or by hand for Python (msgpack 1.2.2) and JavaScript (@msgpack/msgpack 3.1.3).

| Library | LsMsgPack writes, the library reads | The library writes, LsMsgPack reads |
|---|---|---|
| [MessagePack-CSharp](#messagepack-csharp) | contractless resolver, `NativeGuidResolver` and a formatter for LsMsgPack's decimal | maps (contractless) and `NativeGuidResolver` |
| [Nerdbank.MessagePack](#nerdbankmessagepack) | decimal extension type code 1 | two extensions registered in LsMsgPack (Guid and decimal) |
| [Python, JavaScript](#python-and-javascript) | decode Guids and decimals (snippets below) | Guids as bytes in .NET order, decimals as strings |
| [Other libraries](#other-libraries-not-tested) | maps keyed by property names | maps keyed by property names |

LsMsgPack always needs `UseInexedSchema = false`, see below.

LtMsgPack (`LtMsgPackSerializer`) writes the same bytes as LsMsgPack with the same settings and reads the same data, so everything here applies to it too (the interop tests run against both) (custom extensions are `LtExtension<T>` there, with the same bytes as LsMsgPack's `ICustomExt` for the same type code).

## LsMsgPack settings

```csharp
using LsMsgPack;
using LsMsgPack.TypeResolving.Interfaces;

MsgPackSettings compatible = new MsgPackSettings()
{
  UseInexedSchema = false, // required: the indexed schema is LsMsgPack's own format
  DynamicFilters = new IMsgPackPropertyIncludeDynamically[0], // optional: also write null, "", 0 and false
  AddTypeIdOptions = AddTypeIdOption.Never // optional: no type ids, only when nothing is polymorphic
};

byte[] bytes = MsgPackSerializer.Serialize(invoice, compatible);
Invoice read = MsgPackSerializer.Deserialize<Invoice>(bytes, compatible);
```

`MsgPackSettings.Default_UseInexedSchema = false` changes the default of all new settings.

- **`UseInexedSchema = false`** (required, for reading and writing). With the indexed schema (the default) LsMsgPack writes two objects: a map of type names with their property names, then the object, with the positions of those names as keys. Other libraries read one object, the schema: MessagePack-CSharp fails to read it as a class but reads it as `object` without an error (the rest is ignored), Python's `unpackb` and JavaScript's `decode` fail with "extra data". Without the schema an object is a map keyed by its property names, the most common layout. LsMsgPack with the schema also expects the schema when reading, so data from other libraries needs this setting too.
- **Schema references** (`WriteSchemaReference` with a `SchemaStore`). Instead of the schema, the data then starts with a reference to it: an extension (fixext16, type 2) holding the first 16 bytes of the SHA-256 hash of the schema. Only LsMsgPack readers that hold the schema in their `SchemaStore` can read it, other libraries read the extension and stop. Not tested with other libraries, it needs the indexed schema anyway.
- **Default values** (`DynamicFilters`). By default LsMsgPack leaves out values that equal the default of their type (`FilterDefaultValues`): null, `""`, 0, `false`, `Guid.Empty` and so on. A reader keeps what the constructor set for a missing value, so a class with other initial values does not round trip: `public int Retries { get; set; } = 3;` written as 0 is read as 3 (by LsMsgPack too), and `""` is read as null. The other libraries write every value. Without the filter LsMsgPack writes them all, and then writes the same bytes as MessagePack-CSharp for a class of strings, numbers and booleans (tested). Leaving them out is safe when the classes of all readers start with the default values of the types.
- **Type ids** (`AddTypeIdOptions`). LsMsgPack adds a type id when a value's type differs from the declared type: an extra key `""` in an object's map, or a map `{ "": "DateTime", "@": value }` around other values (e.g. a `DateTime` in a property of type `object`). Other libraries skip the `""` key of an object, but read a wrapped value as a map. `AddTypeIdOption.Never` writes only the value (tested), then LsMsgPack can no longer restore the types of polymorphic members.
- **`DateTime`**: use `DateTimeKind.Utc` (or `Local`) values. A timestamp is a moment in UTC. For `Unspecified` LsMsgPack assumes local time (like `ToUniversalTime()`), MessagePack-CSharp assumes UTC (a different moment, unless the machine's time zone is UTC) and Nerdbank.MessagePack refuses it. LsMsgPack reads timestamps as local time, the other libraries as UTC: the same moment.
- **Names** must match exactly (they are case-sensitive).

## How values are written

| .NET | LsMsgPack | MessagePack-CSharp (default) | Result |
|---|---|---|---|
| integers, `float`, `double`, `bool`, `string`, `byte[]`, null | the formats of the spec, integers in the smallest one | the same bytes | tested |
| enums | their number | the same bytes | tested |
| arrays and lists, dictionaries | array, map | the same bytes | tested |
| `DateTime` | timestamp (extension type -1) | the same bytes (except `Unspecified`, see above) | tested |
| `char`, `TimeSpan`, `DateOnly`, `TimeOnly`, `Uri` | number, ticks, day number, ticks, string | the same bytes | tested |
| `Guid` | bin 16 in the byte order of `Guid.ToByteArray()` | a string of 36 characters | configure MessagePack-CSharp (`NativeGuidResolver` writes the same bytes as LsMsgPack) |
| `decimal` | extension type 1: the 16 bytes of `System.Decimal` | a string (`"1234.50"`) | LsMsgPack reads the string, MessagePack-CSharp needs a formatter for the extension |
| `DateTimeOffset` | timestamp of the moment (the offset is lost) | array of the local time and the offset in minutes | incompatible |
| class | map keyed by property names (or the indexed schema) | array (`[Key(0)]`), or map keyed by names (`[Key("Name")]`, contractless) | maps only, LsMsgPack cannot read arrays |
| polymorphic value | type id in the object's map (key `""`) | `[Union]`: array of the union key and the object | incompatible |
| default values | left out | written | see above |

## MessagePack-CSharp

Tested with 3.1.10: all 100 invoices of the benchmark read back identical in both directions.

**LsMsgPack writes, MessagePack-CSharp reads.** Read objects as maps keyed by property names (the contractless resolver, or `[MessagePackObject(keyAsPropertyName: true)]`), Guids as binary (`NativeGuidResolver`) and add a formatter for LsMsgPack's decimal:

```csharp
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(
  new IMessagePackFormatter[] { LsMsgPackDecimalFormatter.Instance },
  new IFormatterResolver[] { NativeGuidResolver.Instance, ContractlessStandardResolver.Instance }));

Invoice invoice = MessagePackSerializer.Deserialize<Invoice>(bytes, options);
```

```csharp
using System.Buffers;
using System.Runtime.InteropServices;

/// <summary>
/// LsMsgPack's decimal: extension type 1 with the 16 bytes of System.Decimal (little-endian)
/// </summary>
public sealed class LsMsgPackDecimalFormatter : IMessagePackFormatter<decimal>
{
  public static readonly LsMsgPackDecimalFormatter Instance = new LsMsgPackDecimalFormatter();

  public void Serialize(ref MessagePackWriter writer, decimal value, MessagePackSerializerOptions options)
  {
    byte[] bytes = new byte[16];
    MemoryMarshal.Write(bytes, in value);
    writer.WriteExtensionFormat(new ExtensionResult(1, bytes));
  }

  public decimal Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
  {
    ExtensionResult ext = reader.ReadExtensionFormat();
    if (ext.TypeCode != 1 || ext.Data.Length != 16)
      throw new MessagePackSerializationException($"Expected an LsMsgPack decimal (extension type 1, 16 bytes) but found extension type {ext.TypeCode} ({ext.Data.Length} bytes)");
    return MemoryMarshal.Read<decimal>(ext.Data.ToArray());
  }
}
```

With these options MessagePack-CSharp also writes what LsMsgPack reads.

**MessagePack-CSharp writes, LsMsgPack reads.**

- Write objects as maps keyed by property names (contractless, `keyAsPropertyName: true`, or string keys equal to the property names). Integer keys write arrays, LsMsgPack only reads objects from maps.
- Write Guids with `NativeGuidResolver`, LsMsgPack does not read the default string.
- Decimals: LsMsgPack reads the default string.
- Do not use LZ4 compression (`WithCompression`, extension types 98 and 99) or the typeless serializer (extension type 100).

```csharp
MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithResolver(
  CompositeResolver.Create(NativeGuidResolver.Instance, ContractlessStandardResolver.Instance));

byte[] bytes = MessagePackSerializer.Serialize(invoice, options);
Invoice read = MsgPackSerializer.Deserialize<Invoice>(bytes, new MsgPackSettings() { UseInexedSchema = false });
```

**Incompatible**: integer keys (arrays), `[Union]`, `DateTimeOffset` and `DateTimeKind.Unspecified` (see the settings above).

## Nerdbank.MessagePack

Nerdbank.MessagePack is the newer library of MessagePack-CSharp's maintainer. It writes objects as maps keyed by property names and writes every value, like LsMsgPack with the settings above. Tested with 1.3.88: all 100 invoices read back identical in both directions. The differences:

- `Guid`: extension type 2 with the bytes in big-endian (RFC 4122) order. It also reads LsMsgPack's bin 16.
- `decimal`: extension type 4 with the same 16 bytes as LsMsgPack's extension type 1. Its extension type 1 is an object reference.

**LsMsgPack writes, Nerdbank reads.** Tell Nerdbank that decimals are extension type 1, and move its object references to a free type code:

```csharp
using Nerdbank.MessagePack;
using PolyType;

MessagePackSerializer serializer = new MessagePackSerializer()
{
  LibraryExtensionTypeCodes = LibraryReservedMessagePackExtensionTypeCode.Default with { Decimal = 1, ObjectReference = 10 }
};

Invoice invoice = serializer.Deserialize<Invoice, Shapes>(bytes);

[GenerateShapeFor<Invoice>]
partial class Shapes { }
```

Instead, LsMsgPack can write decimals as extension type 4: set `MpDecimal.Default_TypeSpecifier = 4` when the application starts, before anything is serialized or deserialized. This applies to all settings, and LsMsgPack then reads extension type 4 instead of 1.

**Nerdbank writes, LsMsgPack reads.** Register an extension for Nerdbank's Guid, and a second decimal for extension type 4:

```csharp
using LsMsgPack;
using LsMsgPack.Types.Extensions;

MsgPackSettings fromNerdbank = new MsgPackSettings()
{
  UseInexedSchema = false,
  CustomExtentionTypes = new ICustomExt[]
  {
    new MpDecimal((MsgPackSettings)null), // extension type 1, used for writing
    new MpDecimal((MsgPackSettings)null) { TypeSpecifier = 4 }, // reads Nerdbank's decimal
    new NerdbankGuidExtension() // reads Nerdbank's Guid
  }
};

/// <summary>
/// Nerdbank.MessagePack's Guid: extension type 2 with the bytes in big-endian order (LsMsgPack keeps writing bin 16)
/// </summary>
public class NerdbankGuidExtension : BaseCustomExt<NerdbankGuidExtension, Guid>
{
  public NerdbankGuidExtension() : base() { }

  public NerdbankGuidExtension(MsgPackSettings settings) : base(settings) { }

  protected override sbyte DefaultTypeSpecifier { get { return 2; } }

  public override Guid FromBytes(byte[] bytes)
  {
    return new Guid(bytes, bigEndian: true);
  }

  public override byte[] GetBytes(Guid item)
  {
    byte[] bytes = new byte[16];
    item.TryWriteBytes(bytes, bigEndian: true, out _);
    return bytes;
  }
}
```

Without the Guid extension LsMsgPack throws a `MsgPackException` that names extension type 2.

**Incompatible**: unions (a two element array, or a map with the type as key), `[Key]` members (an array or a map with integer keys), a `PropertyNamingPolicy` that changes the names (e.g. camelCase), and `DateTimeKind.Unspecified`, which Nerdbank refuses by default.

## Python and JavaScript

Tested by hand with msgpack 1.2.2 (Python) and @msgpack/msgpack 3.1.3 (JavaScript), and LsMsgPack with `UseInexedSchema = false`. These libraries have no classes: an object is a dict or a plain object keyed by property names.

**Reading LsMsgPack's output:**

- Timestamps: `unpackb(data, timestamp=3)` returns a `datetime` in UTC, `decode` a `Date`.
- `Guid`: 16 bytes in .NET's byte order. In Python use `uuid.UUID(bytes_le=value)`, `bytes=` gives a different UUID.
- `decimal`: `ExtType(code=1, ...)` in Python and `ExtData { type: 1, ... }` in JavaScript, decode them with the functions below.
- An indexed schema payload fails in `unpackb` and `decode` ("extra data"). `Unpacker` and `decodeMulti` read both objects, but the keys are positions and nested objects do not say which type's names they use.

**Writing for LsMsgPack:**

- A dict or object keyed by the property names. LsMsgPack ignores keys it does not know.
- `Guid`: the 16 bytes in .NET's order: `uuid.bytes_le` in Python, `dotnetGuidBytes` below in JavaScript.
- `decimal`: a string (`"64764.60"`), LsMsgPack parses it.
- `DateTime`: `packb(value, datetime=True)` with timezone-aware datetimes in Python, a `Date` in JavaScript.
- Enums: their number.

```python
import msgpack, struct, uuid
from datetime import datetime, timezone
from decimal import Decimal

def dotnet_decimal(data):
    """LsMsgPack's decimal (extension type 1): flags (sign and scale), high 32 bits and low 64 bits, little-endian"""
    flags, hi, lo = struct.unpack('<iIQ', data)
    digits = tuple(int(d) for d in str((hi << 64) | lo))
    return Decimal((1 if flags < 0 else 0, digits, -((flags >> 16) & 0xFF)))

invoice = msgpack.unpackb(data, timestamp=3)
invoice_id = uuid.UUID(bytes_le=invoice['Id'])
subtotal = dotnet_decimal(invoice['SubTotal'].data)

data = msgpack.packb({'Id': invoice_id.bytes_le, 'SubTotal': '64764.60', 'InvoiceDate': datetime.now(timezone.utc)}, datetime=True)
```

```javascript
import { decode, encode } from '@msgpack/msgpack';

// LsMsgPack's Guid: bin 16 in .NET's byte order (the first three groups are little-endian)
function dotnetGuid(b) {
  const order = [3, 2, 1, 0, -1, 5, 4, -1, 7, 6, -1, 8, 9, -1, 10, 11, 12, 13, 14, 15];
  return order.map(i => i < 0 ? '-' : b[i].toString(16).padStart(2, '0')).join('');
}

function dotnetGuidBytes(text) {
  const b = Uint8Array.from(text.replace(/-/g, '').match(/../g).map(h => parseInt(h, 16)));
  return Uint8Array.from([b[3], b[2], b[1], b[0], b[5], b[4], b[7], b[6], ...b.slice(8)]);
}

// LsMsgPack's decimal (extension type 1): flags (sign and scale), high 32 bits and low 64 bits, little-endian
function dotnetDecimal(ext) {
  const v = new DataView(ext.data.buffer, ext.data.byteOffset, 16);
  const flags = v.getInt32(0, true);
  const mantissa = (BigInt(v.getUint32(4, true)) << 64n) | v.getBigUint64(8, true);
  const scale = (flags >> 16) & 0xff;
  const digits = mantissa.toString().padStart(scale + 1, '0');
  const text = scale ? digits.slice(0, -scale) + '.' + digits.slice(-scale) : digits;
  return (flags < 0 ? '-' : '') + text;
}

const invoice = decode(data);
const id = dotnetGuid(invoice.Id);
const subtotal = dotnetDecimal(invoice.SubTotal); // a string, keeps all digits

const bytes = encode({ Id: dotnetGuidBytes(id), SubTotal: '64764.60', InvoiceDate: new Date() });
```

## Other libraries (not tested)

The same rules apply: read and write objects as maps keyed by the property names, Guids as 16 bytes in .NET's order, decimals as strings (or LsMsgPack's extension type 1), dates as timestamps.

- Go, [vmihailenco/msgpack](https://github.com/vmihailenco/msgpack): writes structs as maps keyed by field names (or `msgpack` tags) by default. `UseArrayEncodedStructs` and the `as_array` tag write arrays, which LsMsgPack cannot read.
- Rust, [rmp-serde](https://github.com/3Hren/msgpack-rust): `to_vec` writes structs as arrays, use `to_vec_named` (maps).
- [MsgPack.Cli](https://github.com/msgpack/msgpack-cli), the older .NET library: writes arrays and enums by name by default. Use `SerializationMethod.Map` and `EnumSerializationMethod.ByUnderlyingValue`.

## What settings cannot match

- The indexed schema: only LsMsgPack reads it.
- Objects written as arrays (MessagePack-CSharp's integer keys, rmp-serde's `to_vec`, MsgPack.Cli's default): LsMsgPack reads objects from maps only.
- Polymorphism: each library has its own convention (LsMsgPack a `""` key in the object, MessagePack-CSharp and Nerdbank.MessagePack an array of a key and the object).
- `DateTimeOffset` with MessagePack-CSharp: write a UTC `DateTime` (and the offset separately when it matters).
- Guids as strings (MessagePack-CSharp's default): configure the writer to write them as binary.
