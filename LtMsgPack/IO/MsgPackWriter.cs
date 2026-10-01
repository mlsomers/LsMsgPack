using LsMsgPack;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace LtMsgPack.IO
{
  /// <summary>
  /// Writes MsgPack values into a growable buffer, with the same encodings as LsMsgPack's items (MpInt, MpString, MpBin, MpFloat, MpDateTime, MpExt, MpArray, MpMap).
  /// <para>Integers follow <see cref="MsgPackOptions.DynamicallyCompact"/>: the smallest format for the value, or the format of the .NET type. Multi-byte values follow <see cref="MsgPackOptions.EndianAction"/>.</para>
  /// </summary>
  internal sealed class MsgPackWriter
  {
    internal byte[] Buf;
    internal int Pos;
    private bool _compact;
    private bool _littleEndian; // the byte order of multi-byte values on the wire (big-endian unless EndianAction says otherwise)

    internal MsgPackWriter(int capacity)
    {
      Buf = new byte[capacity];
    }

    internal void Reset(MsgPackOptions options)
    {
      Pos = 0;
      _compact = options._dynamicallyCompact;
      _littleEndian = BitConverter.IsLittleEndian != MsgPackOptions.SwapEndianChoice(options, 2);
    }

    internal bool Compact { get { return _compact; } }

    /// <summary>
    /// An empty writer with the same settings (byte order, compact integers).
    /// </summary>
    internal MsgPackWriter Like(int capacity)
    {
      return new MsgPackWriter(capacity) { _compact = _compact, _littleEndian = _littleEndian };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Ensure(int count)
    {
      if (Pos + count > Buf.Length)
        Grow(count);
    }

    private void Grow(int count)
    {
      Array.Resize(ref Buf, Math.Max(Buf.Length * 2, Pos + count));
    }

    internal void Byte(byte value)
    {
      if (Pos == Buf.Length)
        Grow(1);
      Buf[Pos++] = value;
    }

    internal void Raw(byte[] bytes)
    {
      Raw(bytes, 0, bytes.Length);
    }

    internal void Raw(byte[] bytes, int offset, int count)
    {
      Ensure(count);
      if (count == 1)
        Buf[Pos++] = bytes[offset];
      else
      {
        new ReadOnlySpan<byte>(bytes, offset, count).CopyTo(new Span<byte>(Buf, Pos, count)); // Buffer.BlockCopy checks the array types first, which costs more than copying a short key
        Pos += count;
      }
    }

    internal byte[] ToArray()
    {
      byte[] result = new byte[Pos];
      Buffer.BlockCopy(Buf, 0, result, 0, Pos);
      return result;
    }

    internal void CopyTo(Stream target)
    {
      target.Write(Buf, 0, Pos);
    }

    #region Numbers in the wire byte order

    // Callers ensure the space
    private void U16(ushort value)
    {
      byte[] b = Buf;
      if (_littleEndian) { b[Pos] = (byte)value; b[Pos + 1] = (byte)(value >> 8); }
      else { b[Pos] = (byte)(value >> 8); b[Pos + 1] = (byte)value; }
      Pos += 2;
    }

    private void U32(uint value)
    {
      byte[] b = Buf;
      if (_littleEndian) { b[Pos] = (byte)value; b[Pos + 1] = (byte)(value >> 8); b[Pos + 2] = (byte)(value >> 16); b[Pos + 3] = (byte)(value >> 24); }
      else { b[Pos] = (byte)(value >> 24); b[Pos + 1] = (byte)(value >> 16); b[Pos + 2] = (byte)(value >> 8); b[Pos + 3] = (byte)value; }
      Pos += 4;
    }

    private void U64(ulong value)
    {
      if (_littleEndian)
      {
        U32((uint)value);
        U32((uint)(value >> 32));
      }
      else
      {
        U32((uint)(value >> 32));
        U32((uint)value);
      }
    }

    #endregion

    #region Values

    internal void Nil()
    {
      Byte(0xC0);
    }

    internal void Bool(bool value)
    {
      Byte(value ? (byte)0xC3 : (byte)0xC2);
    }

    /// <summary>
    /// A signed value in the smallest format (DynamicallyCompact): non-negative values use the unsigned formats.
    /// </summary>
    internal void CompactSigned(long value)
    {
      if (value >= 0)
      {
        CompactUnsigned((ulong)value);
        return;
      }

      Ensure(9);
      byte[] b = Buf;
      if (value >= -32) b[Pos++] = (byte)value; // negative fixint
      else if (value >= sbyte.MinValue) { b[Pos++] = 0xD0; b[Pos++] = (byte)value; }
      else if (value >= short.MinValue) { b[Pos++] = 0xD1; U16((ushort)value); }
      else if (value >= int.MinValue) { b[Pos++] = 0xD2; U32((uint)value); }
      else { b[Pos++] = 0xD3; U64((ulong)value); }
    }

    internal void CompactUnsigned(ulong value)
    {
      Ensure(9);
      byte[] b = Buf;
      if (value <= 0x7F) b[Pos++] = (byte)value;
      else if (value <= 0xFF) { b[Pos++] = 0xCC; b[Pos++] = (byte)value; }
      else if (value <= 0xFFFF) { b[Pos++] = 0xCD; U16((ushort)value); }
      else if (value <= 0xFFFFFFFF) { b[Pos++] = 0xCE; U32((uint)value); }
      else { b[Pos++] = 0xCF; U64(value); }
    }

    // Without DynamicallyCompact the format of the .NET type is kept (only fixints stay single bytes), like MpInt

    internal void SByte(sbyte value)
    {
      if (_compact || (value >= -32 && value <= 31)) { CompactSigned(value); return; }
      Ensure(2);
      Buf[Pos++] = 0xD0;
      Buf[Pos++] = (byte)value;
    }

    internal void Int16(short value)
    {
      if (_compact) { CompactSigned(value); return; }
      Ensure(3);
      Buf[Pos++] = 0xD1;
      U16((ushort)value);
    }

    internal void Int32(int value)
    {
      if (_compact) { CompactSigned(value); return; }
      Ensure(5);
      Buf[Pos++] = 0xD2;
      U32((uint)value);
    }

    internal void Int64(long value)
    {
      if (_compact) { CompactSigned(value); return; }
      Ensure(9);
      Buf[Pos++] = 0xD3;
      U64((ulong)value);
    }

    internal void UInt8(byte value)
    {
      CompactUnsigned(value); // the same in both modes: a fixint up to 127, then 0xCC
    }

    internal void UInt16(ushort value)
    {
      if (_compact) { CompactUnsigned(value); return; }
      Ensure(3);
      Buf[Pos++] = 0xCD;
      U16(value);
    }

    internal void UInt32(uint value)
    {
      if (_compact) { CompactUnsigned(value); return; }
      Ensure(5);
      Buf[Pos++] = 0xCE;
      U32(value);
    }

    internal void UInt64(ulong value)
    {
      if (_compact) { CompactUnsigned(value); return; }
      Ensure(9);
      Buf[Pos++] = 0xCF;
      U64(value);
    }

    internal void Single(float value)
    {
      Ensure(5);
      Buf[Pos++] = 0xCA;
      U32(new SingleBits(value).Bits);
    }

    internal void Double(double value)
    {
      Ensure(9);
      Buf[Pos++] = 0xCB;
      U64((ulong)BitConverter.DoubleToInt64Bits(value));
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct SingleBits
    {
      [System.Runtime.InteropServices.FieldOffset(0)] private readonly float _value;
      [System.Runtime.InteropServices.FieldOffset(0)] public readonly uint Bits;

      public SingleBits(float value)
      {
        Bits = 0;
        _value = value;
      }
    }

    internal void String(string value)
    {
      Encoding encoding = MsgPackOptions.StringEncoding;
      int length = value.Length;
      if (length <= MaxOnePassLength && IsUtf8(encoding))
      {
        if (length <= MaxAsciiLoopLength && Ascii(value))
          return;

        // One pass: UTF-8 has at least one byte per char, so the header is reserved for that many bytes and moved when the string turns out longer (only non-ASCII text near a boundary)
        Ensure(encoding.GetMaxByteCount(length) + 5);
        int reserved = StringHeaderSize(length);
#if NETSTANDARD2_1_OR_GREATER
        int count = encoding.GetBytes(value.AsSpan(), new Span<byte>(Buf, Pos + reserved, Buf.Length - Pos - reserved));
#else
        int count = encoding.GetBytes(value, 0, length, Buf, Pos + reserved);
#endif
        int needed = StringHeaderSize(count);
        if (needed != reserved)
          Buffer.BlockCopy(Buf, Pos + reserved, Buf, Pos + needed, count);
        StringHeader(count);
        Pos += count;
        return;
      }

      int byteCount = encoding.GetByteCount(value);
      Ensure(byteCount + 5);
      StringHeader(byteCount);
      Pos += encoding.GetBytes(value, 0, length, Buf, Pos);
    }

    /// <summary>
    /// Longer strings are measured first, so the buffer does not grow to three times their length.
    /// </summary>
    private const int MaxOnePassLength = 1 << 16;

    /// <summary>
    /// Up to this length a loop over the chars is faster than the encoder (which costs 10-15 ns per call).
    /// </summary>
    private const int MaxAsciiLoopLength = 16;

    private static Encoding _utf8; // the StringEncoding that was found to be UTF-8 (the type check is a call)

    private static bool IsUtf8(Encoding encoding)
    {
      if (ReferenceEquals(encoding, _utf8))
        return true;
      if (!(encoding is UTF8Encoding))
        return false;
      _utf8 = encoding;
      return true;
    }

    /// <summary>
    /// Writes a short string of ASCII chars (a fixstr: one byte per char), false (nothing written) when it has other chars.
    /// </summary>
    private bool Ascii(string value)
    {
      int length = value.Length;
      Ensure(length + 1);
      byte[] b = Buf;
      int at = Pos + 1;
      for (int t = 0; t < length; t++)
      {
        char c = value[t];
        if (c >= 0x80)
          return false;
        b[at + t] = (byte)c;
      }
      b[Pos] = (byte)(0xA0 | length);
      Pos = at + length;
      return true;
    }

    private static int StringHeaderSize(int byteCount)
    {
      return byteCount < 32 ? 1 : byteCount < 256 ? 2 : byteCount <= ushort.MaxValue ? 3 : 5;
    }

    // Callers ensure the space
    private void StringHeader(int byteCount)
    {
      byte[] b = Buf;
      if (byteCount < 32) b[Pos++] = (byte)(0xA0 | byteCount);
      else if (byteCount < 256) { b[Pos++] = 0xD9; b[Pos++] = (byte)byteCount; }
      else if (byteCount <= ushort.MaxValue) { b[Pos++] = 0xDA; U16((ushort)byteCount); }
      else { b[Pos++] = 0xDB; U32((uint)byteCount); }
    }

    /// <summary>
    /// A decimal as a string in the invariant culture (<see cref="DecimalFormat.String"/>), formatted in place.
    /// </summary>
    internal void DecimalString(decimal value)
    {
#if NETSTANDARD2_1_OR_GREATER
      Span<char> chars = stackalloc char[32]; // at most 31: sign, 29 digits and the point
      if (IsUtf8(MsgPackOptions.StringEncoding) && value.TryFormat(chars, out int length, default, System.Globalization.CultureInfo.InvariantCulture)) // ASCII, the same bytes in UTF-8
      {
        AsciiString(chars.Slice(0, length));
        return;
      }
#endif
      String(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A Guid as a string of 36 characters (<see cref="GuidFormat.String"/>), formatted in place.
    /// </summary>
    internal void GuidString(Guid value)
    {
#if NETSTANDARD2_1_OR_GREATER
      Span<char> chars = stackalloc char[36];
      if (IsUtf8(MsgPackOptions.StringEncoding) && value.TryFormat(chars, out int length, "D")) // ASCII, the same bytes in UTF-8
      {
        AsciiString(chars.Slice(0, length));
        return;
      }
#endif
      String(value.ToString("D"));
    }

#if NETSTANDARD2_1_OR_GREATER
    /// <summary>
    /// A string of ASCII chars shorter than 256 (formatted numbers, Guids): one byte per char.
    /// </summary>
    private void AsciiString(ReadOnlySpan<char> chars)
    {
      int length = chars.Length;
      Ensure(length + 2);
      StringHeader(length);
      byte[] b = Buf;
      for (int t = 0; t < length; t++)
        b[Pos + t] = (byte)chars[t];
      Pos += length;
    }
#endif

    internal void BinHeader(int length)
    {
      Ensure(5);
      if (length < 256) { Buf[Pos++] = 0xC4; Buf[Pos++] = (byte)length; }
      else if (length <= ushort.MaxValue) { Buf[Pos++] = 0xC5; U16((ushort)length); }
      else { Buf[Pos++] = 0xC6; U32((uint)length); }
    }

    internal void Bin(byte[] value)
    {
      BinHeader(value.Length);
      Raw(value);
    }

    /// <summary>
    /// A Guid is binary data of 16 bytes, in the order of <see cref="System.Guid.ToByteArray"/> (as MpBin).
    /// </summary>
    internal void Guid(Guid value)
    {
      Ensure(18);
      Buf[Pos++] = 0xC4;
      Buf[Pos++] = 16;
#if NETSTANDARD2_1_OR_GREATER
      value.TryWriteBytes(new Span<byte>(Buf, Pos, 16));
#else
      Buffer.BlockCopy(value.ToByteArray(), 0, Buf, Pos, 16);
#endif
      Pos += 16;
    }

    /// <summary>
    /// The header of an extension with <paramref name="length"/> bytes of data (as MpExt: fixext when the length allows, otherwise ext8/16/32), the data should follow.
    /// </summary>
    internal void ExtHeader(sbyte typeCode, int length)
    {
      MsgPackTypeId format = Extensions.MsgPackExtension.FormatOf(length);
      Ensure(6);
      Buf[Pos++] = (byte)format;
      if (format == MsgPackTypeId.MpExt8) Buf[Pos++] = (byte)length;
      else if (format == MsgPackTypeId.MpExt16) U16((ushort)length);
      else if (format == MsgPackTypeId.MpExt32) U32((uint)length);
      Buf[Pos++] = unchecked((byte)typeCode);
    }

    internal void Extension(sbyte typeCode, byte[] data)
    {
      ExtHeader(typeCode, data.Length);
      Raw(data);
    }

    private static readonly DateTime Zero = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MaxFExt4 = Zero.AddSeconds(uint.MaxValue);
    private static readonly DateTime MaxFExt8 = new DateTime(2514, 5, 30, 1, 53, 03, DateTimeKind.Utc).Add(TimeSpan.FromTicks(9999999));

    /// <summary>
    /// A timestamp (extension type -1) in UTC, the smallest of the three formats (as MpDateTime): 32 bit seconds, 64 bit with nanoseconds, 96 bit before 1970 or after 2514.
    /// </summary>
    internal void DateTime(DateTime value)
    {
      DateTime utc = value.ToUniversalTime(); // Unspecified is taken as local time, as MpDateTime does
      long seconds = utc.Ticks / TimeSpan.TicksPerSecond - Zero.Ticks / TimeSpan.TicksPerSecond; // rounded down, also before 1970
      long fraction = utc.Ticks % TimeSpan.TicksPerSecond;
      Ensure(15);
      if (utc < Zero || utc > MaxFExt8)
      {
        Buf[Pos++] = 0xC7;
        Buf[Pos++] = 12;
        Buf[Pos++] = 0xFF;
        U32((uint)fraction * 100);
        U64((ulong)seconds);
      }
      else if (utc > MaxFExt4 || fraction != 0)
      {
        Buf[Pos++] = 0xD7;
        Buf[Pos++] = 0xFF;
        U64(((ulong)fraction * 100) << 34 | (ulong)seconds);
      }
      else
      {
        Buf[Pos++] = 0xD6;
        Buf[Pos++] = 0xFF;
        U32((uint)seconds);
      }
    }

    internal void ArrayHeader(int count)
    {
      Ensure(5);
      if (count < 16) Buf[Pos++] = (byte)(0x90 | count);
      else if (count <= ushort.MaxValue) { Buf[Pos++] = 0xDC; U16((ushort)count); }
      else { Buf[Pos++] = 0xDD; U32((uint)count); }
    }

    internal void MapHeader(int count)
    {
      Ensure(5);
      if (count < 16) Buf[Pos++] = (byte)(0x80 | count);
      else if (count <= ushort.MaxValue) { Buf[Pos++] = 0xDE; U16((ushort)count); }
      else { Buf[Pos++] = 0xDF; U32((uint)count); }
    }

    // The same for arrays and maps
    private static int MapHeaderSize(int count)
    {
      return count < 16 ? 1 : count <= ushort.MaxValue ? 3 : 5;
    }

    /// <summary>
    /// The number of entries of a map is known after writing them (values can be left out by the filters): reserve the header for at most <paramref name="maxCount"/> entries, then <see cref="PatchMapHeader"/>.
    /// </summary>
    /// <returns>The position of the header</returns>
    internal int ReserveMapHeader(int maxCount)
    {
      int at = Pos;
      int size = MapHeaderSize(maxCount);
      Ensure(size);
      Pos += size;
      return at;
    }

    internal void PatchMapHeader(int at, int maxCount, int count)
    {
      PatchHeader(at, maxCount, count, true);
    }

    /// <summary>
    /// <see cref="ReserveMapHeader"/> for an array whose number of items is known after writing them (see <see cref="MsgPackOptions.TrimTrailingNulls"/>).
    /// </summary>
    internal int ReserveArrayHeader(int maxCount)
    {
      return ReserveMapHeader(maxCount);
    }

    internal void PatchArrayHeader(int at, int maxCount, int count)
    {
      PatchHeader(at, maxCount, count, false);
    }

    private void PatchHeader(int at, int maxCount, int count, bool map)
    {
      int reserved = MapHeaderSize(maxCount);
      int needed = MapHeaderSize(count);
      if (needed != reserved) // move the entries (fewer were written than the maximum)
      {
        int entries = Pos - at - reserved;
        Ensure(needed - reserved);
        Buffer.BlockCopy(Buf, at + reserved, Buf, at + needed, entries);
        Pos += needed - reserved;
      }

      int end = Pos;
      Pos = at;
      if (map)
        MapHeader(count);
      else
        ArrayHeader(count);
      Pos = end;
    }

    #endregion
  }
}
