using K4os.Compression.LZ4;
using System;

namespace LsMsgPack.Compression
{
  /// <summary>
  /// LZ4 compression of the body (<see cref="MsgPackOptions.Compression"/>) with K4os.Compression.LZ4, in the format of MessagePack-CSharp's <c>Lz4Block</c> and <c>Lz4BlockArray</c>:
  /// <code>options.Compression = Lz4Compression.Block;</code>
  /// Readers need the option too (either instance: they take both layouts, and uncompressed bodies). See docs/Compression.md.
  /// </summary>
  public sealed class Lz4Compression : MsgPackCompression
  {
    /// <summary>
    /// The body as one LZ4 block (extension type 99, MessagePack-CSharp's <c>Lz4Block</c>), the fast level, bodies from 64 bytes on.
    /// </summary>
    public static readonly Lz4Compression Block = new Lz4Compression(MsgPackCompressionLayout.Block);

    /// <summary>
    /// The body in LZ4 blocks of at most 64 KiB in an array (extension type 98 first, MessagePack-CSharp's <c>Lz4BlockArray</c>), the fast level, bodies from 64 bytes on.
    /// </summary>
    public static readonly Lz4Compression BlockArray = new Lz4Compression(MsgPackCompressionLayout.BlockArray);

    /// <param name="layout">One block or blocks in an array (readers take both)</param>
    /// <param name="level">K4os's level: <see cref="LZ4Level.L00_FAST"/> (the default, what MessagePack-CSharp uses) or one of the high compression levels, which write smaller and slower (reading is as fast)</param>
    /// <param name="minimumLength">The shortest body that is compressed</param>
    /// <param name="blockSize">The longest block of <see cref="MsgPackCompressionLayout.BlockArray"/></param>
    public Lz4Compression(MsgPackCompressionLayout layout, LZ4Level level = LZ4Level.L00_FAST, int minimumLength = DefaultMinimumLength, int blockSize = DefaultBlockSize)
      : base(layout, minimumLength, blockSize)
    {
      Level = level;
    }

    /// <summary>
    /// The compression level (<see cref="LZ4Level.L00_FAST"/> unless given).
    /// </summary>
    public LZ4Level Level { get; }

    /// <inheritdoc/>
    public override int MaximumCompressedLength(int length)
    {
      return LZ4Codec.MaximumOutputSize(length);
    }

    /// <inheritdoc/>
    public override int Compress(ReadOnlySpan<byte> source, Span<byte> target)
    {
      return LZ4Codec.Encode(source, target, Level);
    }

    /// <inheritdoc/>
    public override int Decompress(ReadOnlySpan<byte> source, Span<byte> target)
    {
      return LZ4Codec.Decode(source, target);
    }
  }
}
