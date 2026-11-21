# LsMsgPack.Lz4

LZ4 compression of the body for [LsMsgPack](https://www.nuget.org/packages/LsMsgPack) and [LsMsgPackDebug](https://www.nuget.org/packages/LsMsgPackDebug), in the format of MessagePack-CSharp's `Lz4Block` and `Lz4BlockArray`: each library reads what the other writes. It brings [K4os.Compression.LZ4](https://www.nuget.org/packages/K4os.Compression.LZ4) along; the serializers themselves depend on no compression package.

```csharp
using LsMsgPack;
using LsMsgPack.Compression;

LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions() { Compression = Lz4Compression.Block });

byte[] bytes = serializer.Serialize(invoices);
List<Invoice> read = serializer.Deserialize<List<Invoice>>(bytes);
```

- **Readers need the option too.** With it they read both layouts and uncompressed bodies.
- **Only the body is compressed.** A schema, or the reference to a cached one, stays in front of it. Bodies under 64 bytes, and bodies that compression would not make shorter, are written as they are.
- **Layouts.** `Lz4Compression.Block` writes one block (extension type 99). `Lz4Compression.BlockArray` writes blocks of at most 64 KiB in an array (extension type 98 first). `new Lz4Compression(layout, LZ4Level.L09_HC)` compresses harder: it writes slower and smaller, and reading is as fast.
- **Data from outside.** A claimed length is checked against what the blocks can produce, and against `MaxDecompressedLength` (`MsgPackOptions`, unlimited by default), before anything is allocated.

100 invoices with the indexed schema: 175,755 bytes become 59,751 bytes. Writing takes 0.43 ms instead of 0.18 ms, reading 0.32 ms instead of 0.25 ms (.NET 10, i7-7700K).

More: [docs/Compression.md](https://github.com/mlsomers/LsMsgPack/blob/master/docs/Compression.md).
