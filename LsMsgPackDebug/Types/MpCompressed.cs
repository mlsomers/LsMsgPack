using LsMsgPack;
using LsMsgPack.Compression;
using System.IO;

#if KEEPTRACK
using System.ComponentModel;
using System.Xml.Serialization;
#endif

namespace LsMsgPackDebug
{
  /// <summary>
  /// A body compressed as one LZ4 block (extension type <see cref="MsgPackCompression.BlockExtensionType"/>, MessagePack-CSharp's <c>Lz4Block</c>), read with <see cref="MsgPackOptions.Compression"/>
  /// where a payload's body starts (depth 0): the extension as it is, and the decompressed body as an item tree (<see cref="Body"/>; its offsets count in <see cref="Decompressed"/>, not in the data).
  /// </summary>
  public class MpCompressed : MpExt, IMpCompressed
  {
    internal MpCompressed(MpExt ext) : base(ext.Settings)
    {
      CopyBaseDataFrom(ext);
      _depth = ext._depth;
      Decompressed = CompressedItems.Decompress(this, Settings);
      Body = CompressedItems.Unpack(Decompressed, Settings, _depth + 1); // one level down: the body is not read as a compressed body again (LsMsgPack decompresses once)
    }

    /// <inheritdoc/>
#if KEEPTRACK
    [XmlIgnore]
    [Category("Compression")]
    [DisplayName("Body")]
    [Description("The decompressed body (its offsets count in the decompressed bytes).")]
#endif
    public MsgPackItem Body { get; private set; }

    /// <inheritdoc/>
#if KEEPTRACK
    [XmlIgnore]
    [Browsable(false)]
#endif
    public byte[] Decompressed { get; private set; }

    /// <inheritdoc/>
#if KEEPTRACK
    [XmlIgnore]
    [Category("Compression")]
    [DisplayName("Layout")]
    [Description("One LZ4 block (MessagePack-CSharp's Lz4Block).")]
#endif
    public MsgPackCompressionLayout Layout { get { return MsgPackCompressionLayout.Block; } }

    public override string ToString()
    {
      return $"LZ4 compressed body (extension type {TypeSpecifier}, one block): {Count} bytes, {Decompressed.Length} decompressed.";
    }
  }

  /// <summary>
  /// A body compressed in LZ4 blocks (an array whose first element is extension type <see cref="MsgPackCompression.BlockArrayExtensionType"/>, MessagePack-CSharp's <c>Lz4BlockArray</c>), read with
  /// <see cref="MsgPackOptions.Compression"/> where a payload's body starts (depth 0): the array as it is, and the decompressed body as an item tree (<see cref="Body"/>; its offsets count in
  /// <see cref="Decompressed"/>, not in the data).
  /// </summary>
  public class MpCompressedBlocks : MpArray, IMpCompressed
  {
    internal MpCompressedBlocks(MpArray array) : base(array.Settings)
    {
      CopyFrom(array);
      Decompressed = CompressedItems.Decompress((object[])array.Value, Settings);
      Body = CompressedItems.Unpack(Decompressed, Settings, _depth + 1); // one level down: the body is not read as a compressed body again (LsMsgPack decompresses once)
    }

    /// <inheritdoc/>
#if KEEPTRACK
    [XmlIgnore]
    [Category("Compression")]
    [DisplayName("Body")]
    [Description("The decompressed body (its offsets count in the decompressed bytes).")]
#endif
    public MsgPackItem Body { get; private set; }

    /// <inheritdoc/>
#if KEEPTRACK
    [XmlIgnore]
    [Browsable(false)]
#endif
    public byte[] Decompressed { get; private set; }

    /// <inheritdoc/>
#if KEEPTRACK
    [XmlIgnore]
    [Category("Compression")]
    [DisplayName("Layout")]
    [Description("LZ4 blocks in an array (MessagePack-CSharp's Lz4BlockArray).")]
#endif
    public MsgPackCompressionLayout Layout { get { return MsgPackCompressionLayout.BlockArray; } }

    /// <summary>
    /// The number of LZ4 blocks (the elements after the extension with their lengths).
    /// </summary>
    public int BlockCount { get { return ((object[])Value).Length - 1; } }

    public override string ToString()
    {
      return $"LZ4 compressed body (extension type {MsgPackCompression.BlockArrayExtensionType}, {BlockCount} block{(BlockCount == 1 ? "" : "s")}): {Decompressed.Length} bytes decompressed.";
    }
  }

  /// <summary>
  /// A compressed body (<see cref="MpCompressed"/>, <see cref="MpCompressedBlocks"/>): the item as it is in the data, with the decompressed body.
  /// </summary>
  public interface IMpCompressed
  {
    /// <summary>
    /// The decompressed body as an item tree (under KEEPTRACK an <see cref="MpError"/> when it is not valid MsgPack). Its offsets count in <see cref="Decompressed"/>.
    /// </summary>
    MsgPackItem Body { get; }

    /// <summary>
    /// The decompressed bytes of the body.
    /// </summary>
    byte[] Decompressed { get; }

    /// <summary>
    /// One block or blocks in an array.
    /// </summary>
    MsgPackCompressionLayout Layout { get; }
  }

  internal static class CompressedItems
  {
    /// <summary>
    /// The decompressed body of the plain value (the extension, or the elements of the array). Errors are passed on as they are (<see cref="MsgPackItem.PassedOn"/>: data from outside).
    /// </summary>
    internal static byte[] Decompress(object plain, MsgPackSettings settings)
    {
      try
      {
        return CompressedBody.DecompressPlain(settings._compression, plain, settings);
      }
      catch (MsgPackException ex)
      {
        throw MsgPackItem.PassedOn(ex);
      }
    }

    internal static MsgPackItem Unpack(byte[] body, MsgPackSettings settings, int depth)
    {
      using (MemoryStream stream = new MemoryStream(body, false))
        return MsgPackItem.Unpack(stream, settings, depth);
    }
  }
}
