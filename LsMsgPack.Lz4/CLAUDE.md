# LsMsgPack.Lz4

The LZ4 codec of `MsgPackOptions.Compression` (docs/Compression.md; gaps checklist "Compression", maintainer's decisions 2026-11-21: MessagePack-CSharp's ext 98/99, the body only, the serializers without a package dependency). `Lz4Compression : MsgPackCompression` (Core's public seam) over K4os.Compression.LZ4 (`LZ4Codec.Encode`/`Decode`/`MaximumOutputSize`, raw blocks, no frame); references only LsMsgPack.Core, so it works with both serializers and needs no exact version dependency (public API only). `Block`/`BlockArray` instances, `Level` (K4os's, `L00_FAST` default).

- The format lives in Core (`LsMsgPackCore/Compression/CompressedBody.cs`), not here: another implementation of `MsgPackCompression` (e.g. an embedded port) must produce and consume raw LZ4 blocks.
- MessagePack-CSharp's own LZ4 code and K4os write other blocks for some bodies (measured 2026-11-21: 22 of 26 bodies equal, K4os smaller in the others, 122,541 vs 133,479 B), both valid LZ4: interop is "each reads the other", not equal bytes (`CompressionInteropTests`).
- The tools reference it (MCP server, VS Code inspector, MsgPack Explorer, Fiddler inspector, Visual Studio visualizer); the Windows explorer control loads it in a `NoInlining` method behind a catch, so a host without the assemblies shows compressed bodies as they are.
