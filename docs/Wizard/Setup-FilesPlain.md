[Setup wizard](README.md) › [Saved](Files.md) › [Other programs](FilesOtherPrograms.md) › Python, Go, other libraries

# Setup: plain MsgPack files for other programs

Files that a program in another language, or another MsgPack library, reads (and maybe writes): Python, Go, Rust, JavaScript with any MsgPack library, MessagePack-CSharp, Nerdbank.MessagePack.

## In short

| | |
|---|---|
| Package | `LtMsgPack` |
| Settings | a preset: `LtMsgPackPresets.Generic()`, `MessagePackCSharp()` or `Nerdbank()` |
| Format | each object a map of property names and values, like JSON; every value written; no schema, no type ids |
| Keep in mind | names must match on both sides; no automatic class hierarchies |

```
dotnet add package LtMsgPack
```

## Pick the preset of the other side

| The other program uses | Preset | What it changes |
|---|---|---|
| Python (`msgpack`), JavaScript (`@msgpack/msgpack`), Go, Rust, or you don't know | `LtMsgPackPresets.Generic()` | `Guid` and `decimal` as text, dates as standard timestamps |
| MessagePack-CSharp (contractless, or `[MessagePackObject(true)]`) | `LtMsgPackPresets.MessagePackCSharp()` | the same bytes as its default formatters |
| Nerdbank.MessagePack | `LtMsgPackPresets.Nerdbank()` | its decimal and Guid extensions |

```csharp
using LtMsgPack;

LtMsgPackSerializer serializer = new LtMsgPackSerializer(LtMsgPackPresets.Generic()); // keep one for the whole app

await File.WriteAllBytesAsync("measurements.msgpack", serializer.Serialize(measurements));

// and files the other program wrote:
Measurements read = serializer.Deserialize<Measurements>(await File.ReadAllBytesAsync("from-python.msgpack"));
```

## Why these settings

The default format of LtMsgPack starts every file with a schema, which only LtMsgPack and LsMsgPack understand: Python's `unpackb` reads that schema as the whole file and stops with "extra data". The presets switch to what every MsgPack library reads:

- **Objects as maps of names.** `{"Title": "Run 1", "Count": 3}`, exactly like JSON but binary. Any library turns that into a dictionary (Python `dict`, JavaScript object, Go `map` or struct).
- **Every value is written**, also `0`, `false` and `null`. The default format leaves those out to save space, but a Python script would then find keys missing.
- **Values in their most common form.** MsgPack has no `Guid` or `decimal`. `Generic()` writes them as text (`"0f8fad5b-d9cb-469f-a165-70867728950e"`, `"64764.60"`), which every language can parse; the other presets write what those libraries expect.
- **No type ids.** Other libraries have their own conventions for class hierarchies (or none), see below.

## The other side

Python:

```python
import msgpack
from datetime import datetime, timezone

with open('measurements.msgpack', 'rb') as f:
    measurements = msgpack.unpackb(f.read(), timestamp=3)  # timestamp=3: dates as datetime (UTC)

print(measurements['Title'], measurements['Started'])

with open('from-python.msgpack', 'wb') as f:
    f.write(msgpack.packb({'Title': 'Run 2', 'Started': datetime.now(timezone.utc), 'Count': 3}, datetime=True))
```

JavaScript (`npm install @msgpack/msgpack`, or `decodePlain` of `@lsmsgpack/client`):

```js
import { decode, encode } from '@msgpack/msgpack';

const measurements = decode(bytes);           // DateTime values become Date objects
const bytesForDotNet = encode({ Title: 'Run 2', Started: new Date(), Count: 3 });
```

Other libraries, and what to watch with each (Go structs, Rust's `to_vec_named`, MsgPack.Cli): [Compatibility.md](../Compatibility.md#other-libraries-not-tested).

## Property names

The keys are the .NET property names (`Title`, `StartedAt`), and they must match exactly when read back. Two ways to adapt to the other side's style:

- **Different case only** (`title` from JavaScript): `options.PropertyNameCaseInsensitive = true` on the preset matches keys ignoring case.
- **Other names** (`started_at` from Python): put the name on the property and turn on the attribute names:

```csharp
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;

LtMsgPackOptions options = LtMsgPackPresets.Generic();
options.PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new AttributePropertyNameResolver() };

public class Measurements
{
  [JsonPropertyName("started_at")] public DateTime StartedAt { get; set; }
}
```

## Class hierarchies

Without type ids, a property declared as an interface or abstract class (`List<IShape>`) can't be read back: the reader doesn't know whether to create a `Circle` or a `Square`. For shared files, prefer concrete types. When you do need variants, add a property that says which one it is (a "discriminator"), which every language understands:

```csharp
public class Shape
{
  public string Kind { get; set; }    // "circle" or "square"
  public double? Radius { get; set; } // circles
  public double? Side { get; set; }   // squares
}
```

Recognizing a class by its properties is also possible, with a custom type resolver: [schema.md](../schema.md#resolving-by-signature).

## Changing your classes later

Values are matched by name, so adding, removing and reordering properties keeps old files readable on both sides (a reader ignores keys it doesn't know). Renaming a property breaks that, unless you keep the old name with `[JsonPropertyName]` as above. Tell the other side when you add or remove keys: a Python script that does `data['Fax']` fails once `Fax` is gone, so they should use `data.get('Fax')`.

## Watch out

- **Files are about as large as compact JSON**: names are repeated in every object. Compress them (`GZipStream`) if size matters; every language can unzip.
- **`DateTimeOffset`** is written as the moment only with `Generic()`, the offset is lost. Store the offset in a property of its own if you need it.
- **Dates are UTC.** A `DateTime` with `Kind = Local` is stored as the same moment in UTC.
- Files written by others are untrusted data, see [Add-ons](Extras.md#reading-data-you-dont-trust). Without type ids the files can't pick classes, which already removes the main risk.

## Next

- Every setting per library, tested byte for byte: [Compatibility.md](../Compatibility.md).
- Decoding `Guid` and `decimal` in Python and JavaScript with the default (non-text) formats: [Compatibility.md](../Compatibility.md#python-and-javascript).
