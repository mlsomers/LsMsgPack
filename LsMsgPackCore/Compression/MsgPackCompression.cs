using System;

namespace LsMsgPack.Compression
{
  /// <summary>
  /// Compresses the body of a payload with LZ4, in the format of MessagePack-CSharp (<c>Lz4Block</c>: extension type <see cref="BlockExtensionType"/>, <c>Lz4BlockArray</c>: an array starting
  /// with extension type <see cref="BlockArrayExtensionType"/>), so each library reads what the other writes. Set an implementation as <see cref="MsgPackOptions.Compression"/>:
  /// <c>Lz4Compression.Block</c> or <c>Lz4Compression.BlockArray</c> of the package LsMsgPack.Lz4, which brings the LZ4 code along; LsMsgPack itself depends on no compression package.
  /// <para>Only the body is compressed: a schema (or the reference to one) stays in front of it as it is. A body shorter than <see cref="MinimumLength"/>, or one that compression would not make shorter,
  /// is written as it is; readers with the option take both. See docs/Compression.md.</para>
  /// <para>An implementation compresses and decompresses raw LZ4 blocks (the block format of lz4, without the frame), is immutable and safe to use from several threads.</para>
  /// </summary>
  public abstract class MsgPackCompression
  {
    /// <summary>
    /// The extension type of a body compressed as one block (MessagePack-CSharp's <c>Lz4Block</c>): an int32 with the length of the body, then the LZ4 block.
    /// </summary>
    public const sbyte BlockExtensionType = 99;

    /// <summary>
    /// The extension type at the start of a body compressed in blocks (MessagePack-CSharp's <c>Lz4BlockArray</c>): an array of this extension, holding the length of each block once
    /// decompressed (a MsgPack integer each), followed by the LZ4 blocks as binary values.
    /// </summary>
    public const sbyte BlockArrayExtensionType = 98;

    /// <summary>
    /// The shortest body that is compressed (64 bytes, as MessagePack-CSharp).
    /// </summary>
    public const int DefaultMinimumLength = 64;

    /// <summary>
    /// The longest block of <see cref="MsgPackCompressionLayout.BlockArray"/> (64 KiB, the default block size of the LZ4 frame format and the size of LZ4's window).
    /// </summary>
    public const int DefaultBlockSize = 64 * 1024;

    /// <param name="layout">One block, or blocks in an array</param>
    /// <param name="minimumLength">The shortest body that is compressed</param>
    /// <param name="blockSize">The longest block of <see cref="MsgPackCompressionLayout.BlockArray"/> (at least 1024 bytes)</param>
    protected MsgPackCompression(MsgPackCompressionLayout layout, int minimumLength = DefaultMinimumLength, int blockSize = DefaultBlockSize)
    {
      if (layout != MsgPackCompressionLayout.Block && layout != MsgPackCompressionLayout.BlockArray)
        throw new ArgumentOutOfRangeException(nameof(layout), layout, null);
      if (minimumLength < 0)
        throw new ArgumentOutOfRangeException(nameof(minimumLength), minimumLength, "The shortest body that is compressed cannot be negative.");
      if (blockSize < 1024)
        throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "A block holds at least 1024 bytes.");
      Layout = layout;
      MinimumLength = minimumLength;
      BlockSize = blockSize;
    }

    /// <summary>
    /// How a body is written: one block (extension type <see cref="BlockExtensionType"/>) or blocks in an array (<see cref="BlockArrayExtensionType"/>). Readers take both.
    /// </summary>
    public MsgPackCompressionLayout Layout { get; }

    /// <summary>
    /// The shortest body that is compressed (<see cref="DefaultMinimumLength"/> by default): a shorter one is written as it is.
    /// </summary>
    public int MinimumLength { get; }

    /// <summary>
    /// The longest block of <see cref="MsgPackCompressionLayout.BlockArray"/> before compression (<see cref="DefaultBlockSize"/> by default).
    /// </summary>
    public int BlockSize { get; }

    /// <summary>
    /// The most bytes <see cref="Compress"/> writes for <paramref name="length"/> bytes (LZ4's worst case: a little more than the input).
    /// </summary>
    public abstract int MaximumCompressedLength(int length);

    /// <summary>
    /// Compresses <paramref name="source"/> into one raw LZ4 block.
    /// </summary>
    /// <param name="target">At least <see cref="MaximumCompressedLength"/> of the source's length</param>
    /// <returns>The bytes written</returns>
    public abstract int Compress(ReadOnlySpan<byte> source, Span<byte> target);

    /// <summary>
    /// Decompresses one raw LZ4 block. It must not write outside <paramref name="target"/>, whatever the source holds (it is data from outside).
    /// </summary>
    /// <param name="target">Exactly the length the block decompresses to (the data says it)</param>
    /// <returns>The bytes written, negative when the block is not valid LZ4</returns>
    public abstract int Decompress(ReadOnlySpan<byte> source, Span<byte> target);
  }

  /// <summary>
  /// How <see cref="MsgPackCompression"/> writes a body (MessagePack-CSharp's <c>MessagePackCompression</c> modes).
  /// </summary>
  public enum MsgPackCompressionLayout
  {
    /// <summary>
    /// One LZ4 block in an extension of type <see cref="MsgPackCompression.BlockExtensionType"/> (MessagePack-CSharp's <c>Lz4Block</c>).
    /// </summary>
    Block = 1,

    /// <summary>
    /// An array: an extension of type <see cref="MsgPackCompression.BlockArrayExtensionType"/> with the length of each block, then the LZ4 blocks of at most
    /// <see cref="MsgPackCompression.BlockSize"/> bytes each as binary values (MessagePack-CSharp's <c>Lz4BlockArray</c>).
    /// </summary>
    BlockArray = 2,
  }
}
