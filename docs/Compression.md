# Compression

LsMsgPack can compress the body of a payload with LZ4, in the format MessagePack-CSharp uses (`Lz4Block` and `Lz4BlockArray`), so either library reads what the other writes. The LZ4 code comes in its own package, so the serializers themselves depend on no compression library:

```
dotnet add package LsMsgPack.Lz4
```

```csharp
using LsMsgPack;
using LsMsgPack.Compression;

LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { Compression = Lz4Compression.Block });
// LsMsgPackDebug: new MsgPackSettings() { Compression = Lz4Compression.Block }

byte[] bytes = serializer.Serialize(invoices);
List<Invoice> read = serializer.Deserialize<List<Invoice>>(bytes);
```

Readers need the option too. With it they read compressed bodies in either layout and uncompressed bodies. Without it, a compressed body is just an extension value.

Use it where size matters more than a little time: files, caches, message queues, data kept in a database. Over HTTP, use the response compression of the web server instead: browsers and other clients understand that, and decompress faster. The [TypeScript client](TypeScriptClient.md#compressed-bodies) reads LZ4 with the optional `@lsmsgpack/client/lz4`.

## What it costs and saves

100 invoices (the interop tests' model, Release, .NET 10, one run on an i7-7700K):

| | Bytes | Write | Read |
|---|---:|---:|---:|
| LsMsgPack, indexed schema | 175,755 | 0.18 ms | 0.25 ms |
| `Lz4Compression.Block` | 59,751 | 0.43 ms | 0.32 ms |
| `Lz4Compression.BlockArray` | 61,142 | 0.47 ms | 0.32 ms |
| `Block` with `LZ4Level.L09_HC` | 41,727 | 6.2 ms | 0.31 ms |
| MessagePack-CSharp, `Lz4BlockArray` | 83,744 | 0.90 ms | 1.28 ms |
| System.Text.Json | 330,710 | 0.94 ms | 1.50 ms |

Repetitive data compresses well: strings that recur (names, product codes, units), numbers in a small range, the keys of maps written with names. Random data (hashes, encrypted or already compressed bytes) does not: then the body is written as it is.

## What is written

Only the body is compressed. A schema, or the reference to a cached one, stays in front of it as it is, so the schema cache and [schema negotiation](WebFormatters.md) keep working.

- `Lz4Compression.Block` (MessagePack-CSharp's `Lz4Block`): the body is one LZ4 block in an extension of type 99, whose content is the length of the body (`int 32`) followed by the block.
- `Lz4Compression.BlockArray` (MessagePack-CSharp's `Lz4BlockArray`): an array whose first element is an extension of type 98 holding the length of each block (MsgPack integers), followed by one binary value per block. The blocks hold at most 64 KiB of the body each (LZ4's window and the default block size of its frame format).
- A body shorter than 64 bytes is written as it is (MessagePack-CSharp does the same), and so is a body that compression would not make shorter. `new Lz4Compression(layout, level, minimumLength, blockSize)` changes these settings. The level is K4os's: `L00_FAST` by default, the high-compression levels `L03_HC` to `L12_MAX` write smaller and slower, and reading stays as fast.
- The headers follow `EndianAction`, like every other header. The default is big-endian, which is MessagePack-CSharp's byte order.

Both serializers write the same bytes. The LZ4 blocks themselves can differ from what MessagePack-CSharp writes for the same body: its own LZ4 code and K4os's can find different matches (K4os often finds more). Both are valid LZ4 and decompress to the same body.

## Streams

`MsgPackStreamWriter.WriteObject` writes a payload per value, so each value's body is compressed on its own, and `MsgPackStreamReader.ReadObject` decompresses it. The body of a [head](Streaming.md#one-schema-for-a-root-value), written value by value after `WriteHead`, is not compressed. Neither is an array or map of unknown length.

## Reading data from outside

A compressed body says how long it is once decompressed. LZ4 can turn a few bytes into hundreds of times as many, so a small payload can claim gigabytes. Before anything is allocated, two checks run:

- **What a block can produce.** LZ4 makes at most about 255 bytes of each byte of a block. A length that claims more is refused as invalid data, whatever the options say.
- **`MaxDecompressedLength`** (`MsgPackOptions`, unlimited by default, 64 MiB in the HTTP serializer): the longest body a compressed body may become. Over it, a `MsgPackException` is thrown.

A block must decompress to exactly the length it claims. The [other limits](security.md#what-else-to-keep-in-mind) (`MaxObjects`, `MaxItems`, `MaxStringLength`, `MaxBinLength`) apply to the decompressed body.

## Other readers

- **MessagePack-CSharp** reads both layouts with `options.WithCompression(MessagePackCompression.Lz4Block)` (or `Lz4BlockArray`: its readers also take both), and LsMsgPack reads what it writes in either mode. For the rest of the format see [Compatibility](Compatibility.md#messagepack-csharp).
- **The TypeScript client** reads both layouts with `compression: lz4` from `@lsmsgpack/client/lz4` (reading only), see [TypeScriptClient.md](TypeScriptClient.md#compressed-bodies).
- **Readers without LZ4** (other libraries, the TypeScript client without the option) see an extension of type 99, or an array that starts with an extension of type 98.
- **Extension types 98 and 99** mean a compressed body only where a body starts, and only for readers with the option. Elsewhere they are ordinary extension values. Still, don't use them for your own extensions when you use compression.

## Debugging

The MsgPack explorers (Windows, VS Code) and the [MCP server](Mcp.md) show a compressed body as it is in the data, and its decompressed body below it, with the objects. Items of a decompressed body have offsets in the decompressed bytes: the MCP server writes them as `d+0x...`, and the explorers select the compressed body in the hex view.
