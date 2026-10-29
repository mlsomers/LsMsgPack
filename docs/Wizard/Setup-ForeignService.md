[Setup wizard](README.md) › [Sent](Network.md) › [Other parties](NetworkOthers.md) › Their server

# Setup: calling another service that speaks MsgPack

Your .NET app talks to a server, feed or device someone else built, and its MsgPack format is fixed.

## In short

| | |
|---|---|
| Package | `LtMsgPack` (and `LsMsgPack` while you figure out the format) |
| Settings | the preset of their library, adjusted to their names and layout |
| Transport | `HttpClient` with `ByteArrayContent`, or whatever they use |
| Keep in mind | look at real bytes first; their data is untrusted |

```
dotnet add package LtMsgPack
```

## Step 1: find out what their data looks like

Before writing classes, look at a real response. Save one to a file (`File.WriteAllBytes("sample.msgpack", bytes)`) and open it in MsgPack Explorer, the VS Code extension, or with the MCP server for AI agents ([Add-ons](Extras.md#looking-inside-the-bytes)). You'll see one of two shapes:

- **Maps** (`{"id": 42, "created_at": ..., "items": [...]}`): objects keyed by names, the most common shape. Note how the names are written (`createdAt`, `created_at`, `CreatedAt`).
- **Arrays** (`[42, 1730000000, [...]]`): objects as lists of values by position (MessagePack-CSharp with `[Key(0)]`, Rust's `rmp_serde::to_vec`, Go with `as_array`). You need their documentation for the order.

Also check how they send `Guid`/UUIDs (text or 16 bytes), money (text, float or an extension) and dates (timestamps or text).

## Step 2: the settings

**Maps**, from Python, Go, JavaScript and most others:

```csharp
using LtMsgPack;

LtMsgPackOptions options = LtMsgPackPresets.Generic(); // MessagePackCSharp() or Nerdbank() when they use those
options.PropertyNameCaseInsensitive = true;            // "createdAt" sets CreatedAt
LtMsgPackSerializer serializer = new LtMsgPackSerializer(options);
```

Names in another style (`created_at`) go on the properties, with the attribute names turned on:

```csharp
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;

options.PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new AttributePropertyNameResolver() };

public class Ticket
{
  [JsonPropertyName("id")] public int Id { get; set; }
  [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
}
```

**Arrays** (values by position): declare the properties in their order and read by position:

```csharp
using LsMsgPack;

LtMsgPackOptions options = LtMsgPackPresets.Generic();
options.ObjectLayout = ObjectLayout.Array;          // write objects as arrays too
options.PropertyOrder = PropertyOrder.Declaration;  // the order of the properties in your class (base class first)

public class Quote // [symbol, price, time], in this order
{
  public string Symbol { get; set; }
  public double Price { get; set; }
  public DateTime Time { get; set; }
}
```

`PropertyOrder.Explicit` with `[DataMember(Order = n)]` is the alternative when the declaration order can't follow theirs. The details per library: [Compatibility.md](../Compatibility.md).

## Step 3: call it

Over HTTP, serialize yourself:

```csharp
using LsMsgPack;
using System.Net.Http.Headers;

HttpClient client = new HttpClient { BaseAddress = new Uri("https://service.example.com/") }; // keep one
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MsgPackMediaTypes.MsgPack));

ByteArrayContent content = new ByteArrayContent(serializer.Serialize(newTicket));
content.Headers.ContentType = new MediaTypeHeaderValue(MsgPackMediaTypes.MsgPack); // or what they expect, e.g. application/x-msgpack

HttpResponseMessage response = await client.PostAsync("tickets", content);
response.EnsureSuccessStatusCode();
Ticket created = serializer.Deserialize<Ticket>(await response.Content.ReadAsByteArrayAsync());
```

A socket or feed: hand the received bytes of each message to `serializer.Deserialize<Quote>(bytes)`.

## Why these settings

- **A preset** (`Generic()`, `MessagePackCSharp()`, `Nerdbank()`) switches off what only LsMsgPack understands (the schema, class names) and writes values in the form the other library uses. Reading is forgiving anyway: `Guid`s and decimals are read from text or bytes, and numbers into other numeric types (also from text).
- **Case-insensitive names** cover the usual camelCase/PascalCase difference without an attribute on every property.
- **Their data can change without notice.** Matching by name (maps) keeps working when they add fields: unknown keys are skipped. By position (arrays), a field added in the middle shifts everything after it, so keep an eye on their release notes.

## Find mismatches

When a property stays empty, ask the serializer what it skipped:

```csharp
Ticket ticket = serializer.Deserialize<Ticket>(bytes, out ReadDifferences differences);
if (differences != null)
  logger.LogWarning(differences.GenerateReport()); // e.g. "Ticket.created_at: not a property of the class, skipped"
```

## Watch out

- **Their data is untrusted.** Declare the types you expect (no `object` properties), and see [Add-ons](Extras.md#reading-data-you-dont-trust). Other libraries rarely write LsMsgPack's class names, so the main risk (data that picks your classes) hardly applies, but it still could.
- **Dates**: MsgPack timestamps are UTC moments, read as `DateTime` with `Kind = Utc`. Dates sent as text (`"2026-10-29T12:00:00Z"`) are not parsed: declare such a property as `string` and convert it yourself.
- **Extensions you don't know** (their own type codes) can only be read into a `byte[]` (or `object`) property; another property type throws. For a known one, add a custom extension (`LtExtension<T>`, see [Compatibility.md](../Compatibility.md)).
- Only reading their data and not sure of its shape yet? `MsgPackItem.Unpack(bytes)` of the `LsMsgPack` package gives you the tree of maps, arrays and values to explore in code.

## Next

- Settings per library, and what can't match: [Compatibility.md](../Compatibility.md).
- Reporting differences in detail: [ReadDifferences.md](../ReadDifferences.md).
