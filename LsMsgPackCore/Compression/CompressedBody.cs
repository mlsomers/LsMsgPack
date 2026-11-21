using System;
using System.Buffers;
using System.Buffers.Binary;

namespace LsMsgPack.Compression
{
  /// <summary>
  /// The format of a compressed body (<see cref="MsgPackCompression"/>), shared by both serializers: MessagePack-CSharp's <c>Lz4Block</c> and <c>Lz4BlockArray</c>, with the headers in the byte order
  /// of the options (big-endian, the specification's, by default; that is MessagePack-CSharp's).
  /// <para>Writing: ext 99 = <c>d2</c> + int32 length + one LZ4 block; or an array of ext 98 (the length of each block, compact unsigned integers) followed by one <c>bin 32</c> per block.
  /// Reading takes any integer and binary format there (other writers may pick them).</para>
  /// <para>Every length the data claims is checked before anything is allocated: against <see cref="MsgPackOptions.MaxDecompressedLength"/> and against what LZ4 can produce from the block
  /// (at most <see cref="MaxExpansion"/> bytes per byte), and the block must decompress to exactly that length.</para>
  /// </summary>
  internal static class CompressedBody
  {
    /// <summary>
    /// The most bytes LZ4 writes per byte of a block: a match length grows by 255 per extension byte (a body of zeros compresses about 250 times).
    /// </summary>
    internal const int MaxExpansion = 255;

    /// <summary>
    /// Allowed beyond <see cref="MaxExpansion"/> per block: the token, offset and final literals of a short block.
    /// </summary>
    private const int ExpansionSlack = 64;

    private const int Int32Length = 5; // d2 + 4 bytes
    private const int Bin32HeaderLength = 5; // c6 + 4 bytes
    private const int MaxExtHeaderLength = 6; // c9 + 4 bytes + type
    private const int MaxArrayHeaderLength = 5; // dd + 4 bytes

    /// <summary>
    /// Whether the headers are written big-endian (the specification's order) with these options.
    /// </summary>
    internal static bool BigEndian(MsgPackOptions options)
    {
      return MsgPackOptions.SwapEndianChoice(options, 2) == BitConverter.IsLittleEndian;
    }

    #region Writing

    /// <summary>
    /// The most bytes <see cref="Write"/> writes for a body of <paramref name="length"/> bytes, -1 when that would not fit in an array (the body is then written as it is).
    /// </summary>
    internal static int MaxLength(MsgPackCompression codec, int length)
    {
      long max;
      if (codec.Layout == MsgPackCompressionLayout.Block)
        max = MaxExtHeaderLength + Int32Length + (long)codec.MaximumCompressedLength(length);
      else
      {
        int blocks = BlockCount(codec, length);
        int last = length - (blocks - 1) * codec.BlockSize;
        max = MaxArrayHeaderLength + MaxExtHeaderLength + blocks * (long)(Int32Length + Bin32HeaderLength)
          + (blocks - 1) * (long)codec.MaximumCompressedLength(codec.BlockSize) + codec.MaximumCompressedLength(last);
      }
      return max > int.MaxValue - 64 ? -1 : (int)max;
    }

    private static int BlockCount(MsgPackCompression codec, int length)
    {
      return length == 0 ? 1 : (int)(((long)length + codec.BlockSize - 1) / codec.BlockSize);
    }

    /// <summary>
    /// Whether a body of <paramref name="length"/> bytes is compressed at all: not shorter than <see cref="MsgPackCompression.MinimumLength"/>.
    /// </summary>
    internal static bool Compresses(MsgPackCompression codec, int length)
    {
      return length >= codec.MinimumLength && length > 0;
    }

    /// <summary>
    /// Writes the compressed body into <paramref name="target"/>.
    /// </summary>
    /// <param name="target">At least <see cref="MaxLength"/> bytes</param>
    /// <returns>The bytes written, 0 when the body is written as it is: compressed it would not be shorter</returns>
    internal static int Write(MsgPackCompression codec, ReadOnlySpan<byte> body, Span<byte> target, bool bigEndian)
    {
      return codec.Layout == MsgPackCompressionLayout.Block ? WriteBlock(codec, body, target, bigEndian) : WriteBlockArray(codec, body, target, bigEndian);
    }

    private static int WriteBlock(MsgPackCompression codec, ReadOnlySpan<byte> body, Span<byte> target, bool bigEndian)
    {
      // Compressed where the largest header that can still pay off ends (an extension shorter than the body), moved down when its header is shorter
      int reserved = ExtHeaderLength(body.Length) + Int32Length;
      int compressed = codec.Compress(body, target.Slice(reserved));
      if (compressed <= 0)
        return 0;
      int extLength = Int32Length + compressed;
      int header = ExtHeaderLength(extLength);
      int total = header + extLength;
      if (total >= body.Length)
        return 0;
      if (header + Int32Length != reserved)
        target.Slice(reserved, compressed).CopyTo(target.Slice(header + Int32Length)); // overlapping: CopyTo copies as if through a temporary buffer
      WriteExtHeader(target, extLength, MsgPackCompression.BlockExtensionType, bigEndian);
      target[header] = 0xD2;
      WriteUInt32(target.Slice(header + 1), (uint)body.Length, bigEndian);
      return total;
    }

    private static int WriteBlockArray(MsgPackCompression codec, ReadOnlySpan<byte> body, Span<byte> target, bool bigEndian)
    {
      int blockSize = codec.BlockSize;
      int blocks = BlockCount(codec, body.Length);
      int pos = WriteArrayHeader(target, blocks + 1, bigEndian);

      int lengths = 0;
      for (int b = 0; b < blocks; b++)
        lengths += UIntLength(Math.Min(blockSize, body.Length - b * blockSize));
      pos += WriteExtHeader(target.Slice(pos), lengths, MsgPackCompression.BlockArrayExtensionType, bigEndian);
      for (int b = 0; b < blocks; b++)
        pos += WriteUInt(target.Slice(pos), Math.Min(blockSize, body.Length - b * blockSize), bigEndian);

      for (int b = 0; b < blocks; b++)
      {
        int start = b * blockSize;
        ReadOnlySpan<byte> block = body.Slice(start, Math.Min(blockSize, body.Length - start));
        int compressed = codec.Compress(block, target.Slice(pos + Bin32HeaderLength));
        if (compressed <= 0)
          return 0;
        target[pos] = 0xC6;
        WriteUInt32(target.Slice(pos + 1), (uint)compressed, bigEndian);
        pos += Bin32HeaderLength + compressed;
        if (pos >= body.Length) // longer than the body already
          return 0;
      }
      return pos;
    }

    /// <summary>
    /// The compressed body as a new array, null when it is written as it is (too short, or not shorter compressed). For LsMsgPackDebug, which writes into a stream.
    /// </summary>
    internal static byte[] Compress(MsgPackCompression codec, ReadOnlySpan<byte> body, MsgPackOptions options)
    {
      if (!Compresses(codec, body.Length))
        return null;
      int max = MaxLength(codec, body.Length);
      if (max < 0)
        return null;
      byte[] buffer = ArrayPool<byte>.Shared.Rent(max);
      try
      {
        int written = Write(codec, body, buffer, BigEndian(options));
        return written == 0 ? null : new ReadOnlySpan<byte>(buffer, 0, written).ToArray();
      }
      finally
      {
        ArrayPool<byte>.Shared.Return(buffer);
      }
    }

    private static int ExtHeaderLength(int length)
    {
      if (length == 1 || length == 2 || length == 4 || length == 8 || length == 16)
        return 2;
      if (length <= byte.MaxValue)
        return 3;
      if (length <= ushort.MaxValue)
        return 4;
      return 6;
    }

    private static int WriteExtHeader(Span<byte> target, int length, sbyte type, bool bigEndian)
    {
      int pos;
      switch (length)
      {
        case 1: target[0] = 0xD4; pos = 1; break;
        case 2: target[0] = 0xD5; pos = 1; break;
        case 4: target[0] = 0xD6; pos = 1; break;
        case 8: target[0] = 0xD7; pos = 1; break;
        case 16: target[0] = 0xD8; pos = 1; break;
        default:
          if (length <= byte.MaxValue)
          {
            target[0] = 0xC7;
            target[1] = (byte)length;
            pos = 2;
          }
          else if (length <= ushort.MaxValue)
          {
            target[0] = 0xC8;
            WriteUInt16(target.Slice(1), (ushort)length, bigEndian);
            pos = 3;
          }
          else
          {
            target[0] = 0xC9;
            WriteUInt32(target.Slice(1), (uint)length, bigEndian);
            pos = 5;
          }
          break;
      }
      target[pos] = unchecked((byte)type);
      return pos + 1;
    }

    private static int WriteArrayHeader(Span<byte> target, int count, bool bigEndian)
    {
      if (count <= 15)
      {
        target[0] = (byte)(0x90 | count);
        return 1;
      }
      if (count <= ushort.MaxValue)
      {
        target[0] = 0xDC;
        WriteUInt16(target.Slice(1), (ushort)count, bigEndian);
        return 3;
      }
      target[0] = 0xDD;
      WriteUInt32(target.Slice(1), (uint)count, bigEndian);
      return 5;
    }

    /// <summary>
    /// The length of a non-negative integer as MessagePack-CSharp writes it (<c>Write(int)</c>): a positive fixint, uint 8, 16 or 32.
    /// </summary>
    private static int UIntLength(int value)
    {
      if (value <= 0x7F)
        return 1;
      if (value <= byte.MaxValue)
        return 2;
      if (value <= ushort.MaxValue)
        return 3;
      return 5;
    }

    private static int WriteUInt(Span<byte> target, int value, bool bigEndian)
    {
      if (value <= 0x7F)
      {
        target[0] = (byte)value;
        return 1;
      }
      if (value <= byte.MaxValue)
      {
        target[0] = 0xCC;
        target[1] = (byte)value;
        return 2;
      }
      if (value <= ushort.MaxValue)
      {
        target[0] = 0xCD;
        WriteUInt16(target.Slice(1), (ushort)value, bigEndian);
        return 3;
      }
      target[0] = 0xCE;
      WriteUInt32(target.Slice(1), (uint)value, bigEndian);
      return 5;
    }

    private static void WriteUInt16(Span<byte> target, ushort value, bool bigEndian)
    {
      if (bigEndian)
        BinaryPrimitives.WriteUInt16BigEndian(target, value);
      else
        BinaryPrimitives.WriteUInt16LittleEndian(target, value);
    }

    private static void WriteUInt32(Span<byte> target, uint value, bool bigEndian)
    {
      if (bigEndian)
        BinaryPrimitives.WriteUInt32BigEndian(target, value);
      else
        BinaryPrimitives.WriteUInt32LittleEndian(target, value);
    }

    #endregion

    #region Reading

    /// <summary>
    /// Whether <paramref name="data"/> starts with a compressed body: an extension of type <see cref="MsgPackCompression.BlockExtensionType"/>, or an array whose first element is an extension of type
    /// <see cref="MsgPackCompression.BlockArrayExtensionType"/>. Only the headers are looked at.
    /// </summary>
    internal static bool StartsCompressed(ReadOnlySpan<byte> data)
    {
      if (data.Length < 2)
        return false;
      int type = ExtTypeAt(data);
      if (type >= 0)
        return type == MsgPackCompression.BlockExtensionType;
      int header = ArrayHeaderLength(data[0]);
      return header > 0 && data.Length > header && ExtTypeAt(data.Slice(header)) == MsgPackCompression.BlockArrayExtensionType;
    }

    /// <returns>The extension type of the extension header at the start, -1 when there is none (or it is cut off)</returns>
    private static int ExtTypeAt(ReadOnlySpan<byte> data)
    {
      int at;
      switch (data[0])
      {
        case 0xD4: case 0xD5: case 0xD6: case 0xD7: case 0xD8: at = 1; break;
        case 0xC7: at = 2; break;
        case 0xC8: at = 3; break;
        case 0xC9: at = 5; break;
        default: return -1;
      }
      return data.Length > at ? unchecked((sbyte)data[at]) : -1;
    }

    private static int ArrayHeaderLength(byte first)
    {
      if ((first & 0xF0) == 0x90)
        return 1;
      if (first == 0xDC)
        return 3;
      if (first == 0xDD)
        return 5;
      return 0;
    }

    /// <summary>
    /// Decompresses the body at the start of <paramref name="data"/> (<see cref="StartsCompressed"/> said it is one) into an array rented from <see cref="ArrayPool{T}.Shared"/>:
    /// clear its first <paramref name="length"/> bytes and return it when the body is read.
    /// </summary>
    /// <param name="length">The length of the body</param>
    /// <param name="consumed">The bytes of the compressed body in <paramref name="data"/></param>
    /// <exception cref="MsgPackException">The compressed body is not valid, or longer than <see cref="MsgPackOptions.MaxDecompressedLength"/></exception>
    internal static byte[] Read(MsgPackCompression codec, ReadOnlySpan<byte> data, MsgPackOptions options, out int length, out int consumed)
    {
      Cursor c = new Cursor(data, BigEndian(options));
      int arrayCount = c.ArrayHeader();
      long extLength = c.ExtHeader(out sbyte type);
      if (type != (arrayCount < 0 ? MsgPackCompression.BlockExtensionType : MsgPackCompression.BlockArrayExtensionType))
        throw Invalid($"extension type {type} where the compressed data starts");
      ReadOnlySpan<byte> content = c.Take(extLength);
      if (arrayCount < 0)
      {
        byte[] body = DecompressBlock(codec, content, options, true, out length);
        consumed = c.Pos;
        return body;
      }

      int[] lengths = BlockLengths(content, arrayCount - 1, options, out length);
      // Every block checked against what it can produce before the body is allocated (the lengths come first in the data, the blocks after them)
      int blocksStart = c.Pos;
      for (int b = 0; b < lengths.Length; b++)
        CheckExpansion(lengths[b], c.Take(c.BinHeader()).Length);
      consumed = c.Pos;
      c.Pos = blocksStart;
      byte[] result = ArrayPool<byte>.Shared.Rent(length);
      try
      {
        int at = 0;
        for (int b = 0; b < lengths.Length; b++)
        {
          ReadOnlySpan<byte> block = c.Take(c.BinHeader());
          DecompressInto(codec, block, new Span<byte>(result, at, lengths[b]));
          at += lengths[b];
        }
      }
      catch
      {
        new Span<byte>(result, 0, length).Clear();
        ArrayPool<byte>.Shared.Return(result);
        throw;
      }
      return result;
    }

    /// <summary>
    /// The decompressed body when the plain value of a body (LsMsgPackDebug's unpacked tree: an unknown extension stays an <see cref="IMsgPackExtension"/>, an array is an <c>object[]</c>,
    /// bin a <c>byte[]</c>) is a compressed one, otherwise null.
    /// </summary>
    internal static byte[] DecompressPlain(MsgPackCompression codec, object plain, MsgPackOptions options)
    {
      IMsgPackExtension ext = plain as IMsgPackExtension;
      if (ext != null)
        return ext.TypeSpecifier == MsgPackCompression.BlockExtensionType ? DecompressBlock(codec, ext.Data, options, false, out int unused) : null;

      object[] array = plain as object[];
      if (array is null || array.Length == 0)
        return null;
      ext = array[0] as IMsgPackExtension;
      if (ext is null || ext.TypeSpecifier != MsgPackCompression.BlockArrayExtensionType)
        return null;
      int[] lengths = BlockLengths(ext.Data, array.Length - 1, options, out int total);
      for (int b = 0; b < lengths.Length; b++) // before the body is allocated
      {
        byte[] block = array[b + 1] as byte[];
        if (block is null)
          throw Invalid("a block that is no binary value");
        CheckExpansion(lengths[b], block.Length);
      }
      byte[] body = new byte[total];
      int at = 0;
      for (int b = 0; b < lengths.Length; b++)
      {
        DecompressInto(codec, (byte[])array[b + 1], new Span<byte>(body, at, lengths[b]));
        at += lengths[b];
      }
      return body;
    }

    /// <summary>
    /// Decompresses the content of an extension of type <see cref="MsgPackCompression.BlockExtensionType"/>: an integer with the length of the body, then the LZ4 block.
    /// </summary>
    /// <param name="rent">The array from <see cref="ArrayPool{T}.Shared"/> (longer than the body), otherwise a new one of exactly <paramref name="length"/> bytes</param>
    internal static byte[] DecompressBlock(MsgPackCompression codec, ReadOnlySpan<byte> content, MsgPackOptions options, bool rent, out int length)
    {
      Cursor c = new Cursor(content, BigEndian(options));
      long claimed = c.Integer();
      ReadOnlySpan<byte> block = content.Slice(c.Pos);
      length = Claimed(claimed, block.Length, options);
      byte[] body = rent ? ArrayPool<byte>.Shared.Rent(length) : new byte[length];
      try
      {
        DecompressInto(codec, block, new Span<byte>(body, 0, length));
      }
      catch
      {
        if (rent)
        {
          new Span<byte>(body, 0, length).Clear();
          ArrayPool<byte>.Shared.Return(body);
        }
        throw;
      }
      return body;
    }

    /// <summary>
    /// The lengths of the blocks after decompression in the content of an extension of type <see cref="MsgPackCompression.BlockArrayExtensionType"/>, checked: one per block, their sum within
    /// <see cref="MsgPackOptions.MaxDecompressedLength"/>. Each block is checked against what it can produce by <see cref="DecompressInto"/>.
    /// </summary>
    /// <param name="blocks">The binary values after the extension in the array</param>
    internal static int[] BlockLengths(ReadOnlySpan<byte> content, int blocks, MsgPackOptions options, out int total)
    {
      if (blocks < 1)
        throw Invalid("an array of blocks without a block");
      if (blocks > content.Length) // at least one byte per length
        throw Invalid("more blocks than lengths");
      Cursor c = new Cursor(content, BigEndian(options));
      int[] lengths = new int[blocks];
      long sum = 0;
      for (int b = 0; b < blocks; b++)
      {
        long length = c.Integer();
        if (length < 0)
          throw Invalid("a negative length");
        lengths[b] = (int)Math.Min(length, int.MaxValue);
        sum += length;
        if (sum > options._maxDecompressedLength)
          throw TooLong(sum, options);
      }
      if (c.Pos != content.Length)
        throw Invalid("more lengths than blocks");
      total = (int)sum;
      return lengths;
    }

    /// <summary>
    /// Decompresses one LZ4 block into exactly <paramref name="target"/>, after checking that the block can produce that many bytes.
    /// </summary>
    internal static void DecompressInto(MsgPackCompression codec, ReadOnlySpan<byte> block, Span<byte> target)
    {
      CheckExpansion(target.Length, block.Length);
      int written = codec.Decompress(block, target);
      if (written != target.Length)
        throw Invalid(written < 0 ? "a block that is not valid LZ4" : $"a block of {written} bytes where {target.Length} were claimed");
    }

    private static int Claimed(long claimed, int blockLength, MsgPackOptions options)
    {
      if (claimed < 0)
        throw Invalid("a negative length");
      if (claimed > options._maxDecompressedLength)
        throw TooLong(claimed, options);
      CheckExpansion(claimed, blockLength);
      return (int)claimed;
    }

    /// <summary>
    /// A block of <paramref name="blockLength"/> bytes cannot decompress to more than <see cref="MaxExpansion"/> times its length (data that claims more is refused before anything is allocated).
    /// </summary>
    private static void CheckExpansion(long claimed, int blockLength)
    {
      if (claimed > (long)blockLength * MaxExpansion + ExpansionSlack)
        throw Invalid($"a block of {blockLength} bytes claiming {claimed} bytes, more than LZ4 can produce from it");
    }

    private static MsgPackException Invalid(string what)
    {
      return new MsgPackException($"The compressed body is not valid: {what}.");
    }

    private static MsgPackException TooLong(long length, MsgPackOptions options)
    {
      return MsgPackOptions.TooLong("a compressed body that decompresses to", length, options._maxDecompressedLength, nameof(MsgPackOptions) + "." + nameof(MsgPackOptions.MaxDecompressedLength));
    }

    /// <summary>
    /// Reads the headers of a compressed body, every length checked against the data.
    /// </summary>
    private ref struct Cursor
    {
      private readonly ReadOnlySpan<byte> _data;
      private readonly bool _bigEndian;
      internal int Pos;

      internal Cursor(ReadOnlySpan<byte> data, bool bigEndian)
      {
        _data = data;
        _bigEndian = bigEndian;
        Pos = 0;
      }

      private byte Byte()
      {
        if (Pos >= _data.Length)
          throw EndOfData();
        return _data[Pos++];
      }

      private ReadOnlySpan<byte> Bytes(int count)
      {
        if (count > _data.Length - Pos)
          throw EndOfData();
        ReadOnlySpan<byte> bytes = _data.Slice(Pos, count);
        Pos += count;
        return bytes;
      }

      private ushort UInt16()
      {
        ReadOnlySpan<byte> b = Bytes(2);
        return _bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(b) : BinaryPrimitives.ReadUInt16LittleEndian(b);
      }

      private uint UInt32()
      {
        ReadOnlySpan<byte> b = Bytes(4);
        return _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(b) : BinaryPrimitives.ReadUInt32LittleEndian(b);
      }

      private ulong UInt64()
      {
        ReadOnlySpan<byte> b = Bytes(8);
        return _bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(b) : BinaryPrimitives.ReadUInt64LittleEndian(b);
      }

      internal ReadOnlySpan<byte> Take(long count)
      {
        if (count > _data.Length - Pos)
          throw EndOfData();
        return Bytes((int)count);
      }

      /// <returns>The count of an array header, -1 when the next value is no array (the reader stays where it was)</returns>
      internal int ArrayHeader()
      {
        if (Pos >= _data.Length)
          throw EndOfData();
        byte first = _data[Pos];
        if ((first & 0xF0) == 0x90)
        {
          Pos++;
          return first & 0x0F;
        }
        if (first == 0xDC)
        {
          Pos++;
          return UInt16();
        }
        if (first == 0xDD)
        {
          Pos++;
          uint count = UInt32();
          return count > int.MaxValue ? int.MaxValue : (int)count;
        }
        return -1;
      }

      internal long ExtHeader(out sbyte type)
      {
        long length;
        switch (Byte())
        {
          case 0xD4: length = 1; break;
          case 0xD5: length = 2; break;
          case 0xD6: length = 4; break;
          case 0xD7: length = 8; break;
          case 0xD8: length = 16; break;
          case 0xC7: length = Byte(); break;
          case 0xC8: length = UInt16(); break;
          case 0xC9: length = UInt32(); break;
          default: throw Invalid("no extension where the compressed data starts");
        }
        type = unchecked((sbyte)Byte());
        return length;
      }

      internal long BinHeader()
      {
        switch (Byte())
        {
          case 0xC4: return Byte();
          case 0xC5: return UInt16();
          case 0xC6: return UInt32();
          default: throw Invalid("a block that is no binary value");
        }
      }

      /// <summary>
      /// An integer in any format (lengths are written compact by MessagePack-CSharp, the length of a block as int32).
      /// </summary>
      internal long Integer()
      {
        byte first = Byte();
        if (first <= 0x7F)
          return first;
        if (first >= 0xE0)
          return unchecked((sbyte)first);
        switch (first)
        {
          case 0xCC: return Byte();
          case 0xCD: return UInt16();
          case 0xCE: return UInt32();
          case 0xCF: { ulong v = UInt64(); return v > long.MaxValue ? long.MaxValue : (long)v; }
          case 0xD0: return unchecked((sbyte)Byte());
          case 0xD1: return unchecked((short)UInt16());
          case 0xD2: return unchecked((int)UInt32());
          case 0xD3: return unchecked((long)UInt64());
          default: throw Invalid("a length that is no integer");
        }
      }

      private static MsgPackException EndOfData()
      {
        return Invalid("it ends early");
      }
    }

    #endregion
  }
}
