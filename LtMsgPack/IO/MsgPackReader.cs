using LsMsgPack;
using LtMsgPack.Extensions;
using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace LtMsgPack.IO
{
  /// <summary>
  /// Reads MsgPack values from a byte array. Typed reads (<see cref="TryReadInt64"/>, ...) only accept the formats they expect, anything else is left to <see cref="ReadPlain"/>, which returns what LsMsgPack's items unpack to.
  /// <para>Multi-byte values follow <see cref="MsgPackOptions.EndianAction"/>. Lengths and nesting are checked against the data, so hostile data cannot make it allocate more than the data holds.</para>
  /// </summary>
  /// <para>Public for the code of the source generator (see LtMsgPack.LtMsgPackContext): may change between releases.</para>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class MsgPackReader
  {
    public byte[] Buf; // not readonly: the reader of a thread is reused (Reset)
    public int Pos;
    public int End;
    private readonly bool _littleEndian;
    private readonly int _maxDepth;
    private readonly LtExtension[] _extensions;
    private readonly DateTimeKind _dateTimeKind;

    internal MsgPackReader(byte[] buffer, int offset, int end, LtMsgPackOptions options)
    {
      Buf = buffer;
      Pos = offset;
      End = end;
      _littleEndian = BitConverter.IsLittleEndian != MsgPackOptions.SwapEndianChoice(options, 2);
      _maxDepth = options._maxDepth;
      _extensions = options._extensions;
      _dateTimeKind = options._readDateTimeKind;
    }

    /// <summary>
    /// The reader reads other data (the options stay the same).
    /// </summary>
    internal void Reset(byte[] buffer, int offset, int end)
    {
      Buf = buffer;
      Pos = offset;
      End = end;
    }

    internal int MaxDepth { get { return _maxDepth; } }

    #region Bytes

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Need(int count)
    {
      if (count < 0 || End - Pos < count)
        throw EndOfData();
    }

    internal static MsgPackException EndOfData()
    {
      return new MsgPackException("Unexpected end of data.", 0, MsgPackTypeId.NeverUsed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal byte Peek()
    {
      if (Pos >= End)
        throw EndOfData();
      return Buf[Pos];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal byte ReadByte()
    {
      if (Pos >= End)
        throw EndOfData();
      return Buf[Pos++];
    }

    private ushort U16()
    {
      Need(2);
      ReadOnlySpan<byte> source = new ReadOnlySpan<byte>(Buf, Pos, 2);
      Pos += 2;
      return U16(source);
    }

    private uint U32()
    {
      Need(4);
      ReadOnlySpan<byte> source = new ReadOnlySpan<byte>(Buf, Pos, 4);
      Pos += 4;
      return U32(source);
    }

    private ulong U64()
    {
      Need(8);
      ReadOnlySpan<byte> source = new ReadOnlySpan<byte>(Buf, Pos, 8);
      Pos += 8;
      return U64(source);
    }

    private byte[] Bytes(long count)
    {
      if (count > End - Pos)
        throw EndOfData();
      byte[] result = MsgPackWriter.NewBytes((int)count);
      new ReadOnlySpan<byte>(Buf, Pos, (int)count).CopyTo(result);
      Pos += (int)count;
      return result;
    }

    /// <summary>
    /// A count of items or entries, which needs at least one byte each.
    /// </summary>
    private int Count(long count, int bytesPerItem)
    {
      if (count > (End - Pos) / bytesPerItem)
        throw TooMany(count);
      return (int)count;
    }

    private MsgPackException TooMany(long count) // apart: Count is inlined into the readers of arrays and maps
    {
      return new MsgPackException($"The data claims {count} items, more than the remaining {End - Pos} bytes can hold.");
    }

    #endregion

    #region Headers

    public bool TryReadNil()
    {
      if (Peek() != 0xC0)
        return false;
      Pos++;
      return true;
    }

    /// <returns>-1 when the next value is not an array</returns>
    internal int TryReadArrayHeader()
    {
      byte c = Peek();
      if ((c & 0xF0) == 0x90) { Pos++; return Count(c & 0x0F, 1); }
      if (c == 0xDC) { Pos++; return Count(U16(), 1); }
      if (c == 0xDD) { Pos++; return Count(U32(), 1); }
      return -1;
    }

    /// <returns>-1 when the next value is not a map</returns>
    internal int TryReadMapHeader()
    {
      byte c = Peek();
      if ((c & 0xF0) == 0x80) { Pos++; return Count(c & 0x0F, 2); }
      if (c == 0xDE) { Pos++; return Count(U16(), 2); }
      if (c == 0xDF) { Pos++; return Count(U32(), 2); }
      return -1;
    }

    /// <returns>-1 when the next value is not a string (the position is unchanged), otherwise the length (the position is at the first byte)</returns>
    internal int TryReadStringHeader()
    {
      byte c = Peek();
      long length;
      if ((c & 0xE0) == 0xA0) { Pos++; length = c & 0x1F; }
      else if (c == 0xD9) { Pos++; length = ReadByte(); }
      else if (c == 0xDA) { Pos++; length = U16(); }
      else if (c == 0xDB) { Pos++; length = U32(); }
      else return -1;

      if (length > End - Pos)
        throw EndOfData();
      return (int)length;
    }

    #endregion

    #region Typed reads (false: another format, left to ReadPlain and the conversions)

    public bool TryReadBool(out bool value)
    {
      byte c = Peek();
      if (c == 0xC3) { Pos++; value = true; return true; }
      if (c == 0xC2) { Pos++; value = false; return true; }
      value = false;
      return false;
    }

    /// <summary>
    /// Any integer that fits a long. Small enough for the JIT to inline where it pays (the indexes of the schema's keys are positive fixints); AggressiveInlining made the key loops
    /// larger, names maps read 2-3% slower.
    /// </summary>
    public bool TryReadInt64(out long value)
    {
      int pos = Pos;
      if (pos >= End)
        throw EndOfData();
      byte[] buffer = Buf;
      byte c = buffer[pos];
      if (c <= 0x7F)
      {
        Pos = pos + 1;
        value = c;
        return true;
      }
      return TryReadInt64Formats(buffer, pos, c, out value);
    }

    /// <summary>
    /// <see cref="TryReadInt64"/> in one call (the readers of integers: a value of more than one byte would take two calls, inlining it made their readers larger, names maps read 2% slower).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal bool TryReadAnyInt64(out long value)
    {
      int pos = Pos;
      if (pos >= End)
        throw EndOfData();
      byte[] buffer = Buf;
      byte c = buffer[pos];
      if (c <= 0x7F)
      {
        Pos = pos + 1;
        value = c;
        return true;
      }
      return Int64Formats(buffer, pos, c, out value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryReadInt64Formats(byte[] buffer, int pos, byte c, out long value)
    {
      return Int64Formats(buffer, pos, c, out value);
    }

    /// <summary>
    /// The formats other than a positive fixint <paramref name="c"/> at <paramref name="pos"/> (the buffer and position are passed: loading them again made <c>int[]</c> reads 10% slower).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Int64Formats(byte[] buffer, int pos, byte c, out long value)
    {
      if (c >= 0xE0) { Pos = pos + 1; value = (sbyte)c; return true; }
      switch (c)
      {
        case 0xCC: value = AfterFormat(buffer, pos, 1)[0]; Pos = pos + 2; return true;
        case 0xCD: value = U16(AfterFormat(buffer, pos, 2)); Pos = pos + 3; return true;
        case 0xCE: value = U32(AfterFormat(buffer, pos, 4)); Pos = pos + 5; return true;
        case 0xCF:
          {
            ulong u = U64(AfterFormat(buffer, pos, 8));
            if (u > long.MaxValue) { value = 0; return false; }
            value = (long)u;
            Pos = pos + 9;
            return true;
          }
        case 0xD0: value = (sbyte)AfterFormat(buffer, pos, 1)[0]; Pos = pos + 2; return true;
        case 0xD1: value = (short)U16(AfterFormat(buffer, pos, 2)); Pos = pos + 3; return true;
        case 0xD2: value = (int)U32(AfterFormat(buffer, pos, 4)); Pos = pos + 5; return true;
        case 0xD3: value = (long)U64(AfterFormat(buffer, pos, 8)); Pos = pos + 9; return true;
      }
      value = 0;
      return false;
    }

    /// <summary>
    /// The <paramref name="count"/> bytes after the format byte at <paramref name="pos"/> (checked against <see cref="End"/>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> AfterFormat(byte[] buffer, int pos, int count)
    {
      if (End - pos <= count)
        throw EndOfData();
      return new ReadOnlySpan<byte>(buffer, pos + 1, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort U16(ReadOnlySpan<byte> source)
    {
      return _littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(source) : BinaryPrimitives.ReadUInt16BigEndian(source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint U32(ReadOnlySpan<byte> source)
    {
      return _littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(source) : BinaryPrimitives.ReadUInt32BigEndian(source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong U64(ReadOnlySpan<byte> source)
    {
      return _littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(source) : BinaryPrimitives.ReadUInt64BigEndian(source);
    }

    /// <summary>
    /// Any non-negative integer.
    /// </summary>
    public bool TryReadUInt64(out ulong value)
    {
      byte c = Peek();
      if (c <= 0x7F) { Pos++; value = c; return true; }
      switch (c)
      {
        case 0xCC: Pos++; value = ReadByte(); return true;
        case 0xCD: Pos++; value = U16(); return true;
        case 0xCE: Pos++; value = U32(); return true;
        case 0xCF: Pos++; value = U64(); return true;
      }
      int start = Pos;
      if (TryReadInt64(out long signed) && signed >= 0) { value = (ulong)signed; return true; }
      Pos = start;
      value = 0;
      return false;
    }

    public bool TryReadDouble(out double value)
    {
      byte c = Peek();
      if (c == 0xCB) { Pos++; value = BitConverter.Int64BitsToDouble((long)U64()); return true; }
      if (c == 0xCA) { Pos++; value = ReadSingleBits(); return true; }
      value = 0;
      return false;
    }

    public bool TryReadSingle(out float value)
    {
      if (Peek() == 0xCA) { Pos++; value = ReadSingleBits(); return true; }
      value = 0;
      return false;
    }

    private float ReadSingleBits()
    {
      return new SingleBits(U32()).Value;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct SingleBits
    {
      [System.Runtime.InteropServices.FieldOffset(0)] public readonly float Value;
      [System.Runtime.InteropServices.FieldOffset(0)] private readonly uint _bits;

      public SingleBits(uint bits)
      {
        Value = 0;
        _bits = bits;
      }
    }

    #region Numbers in bulk

    // Arrays and lists of numbers (NumberArrayReader, NumberListReader): the values in the formats of their type are read in one loop with the position in a local, instead of a call per value.
    // Each stops at the first value that is not in those formats or not complete: the reader of the element type reads that one (nil, other formats, conversions, the errors).
    // The data is read by reference: the end is checked against the buffer once, each value against the end.

    /// <returns>The number of values read into <paramref name="target"/> (from its start)</returns>
    internal int ReadInt32s(Span<int> target)
    {
      byte[] buffer = Buf;
      int pos = Pos;
      int end = End;
      if (pos >= end || (uint)end > (uint)buffer.Length)
        return 0;
      ref byte data = ref buffer[0];
      ref int to = ref MemoryMarshal.GetReference(target);
      bool swap = _littleEndian != BitConverter.IsLittleEndian;
      int t = 0;
      while (t < target.Length && pos < end)
      {
        ref byte at = ref Unsafe.Add(ref data, pos);
        ref byte next = ref Unsafe.Add(ref at, 1);
        byte c = at;
        int left = end - pos;
        int value;
        if (c <= 0x7F) { value = c; pos++; }
        else if (c >= 0xE0) { value = (sbyte)c; pos++; }
        else if (c == 0xCD && left >= 3) { value = U16(ref next, swap); pos += 3; }
        else if (c == 0xCC && left >= 2) { value = next; pos += 2; }
        else if (c == 0xD2 && left >= 5) { value = (int)U32(ref next, swap); pos += 5; }
        else if (c == 0xD1 && left >= 3) { value = (short)U16(ref next, swap); pos += 3; }
        else if (c == 0xD0 && left >= 2) { value = (sbyte)next; pos += 2; }
        else if (c == 0xCE && left >= 5)
        {
          uint unsigned = U32(ref next, swap);
          if (unsigned > int.MaxValue)
            break; // the reader of the type reports the overflow
          value = (int)unsigned;
          pos += 5;
        }
        else
          break;
        Unsafe.Add(ref to, t++) = value;
      }
      Pos = pos;
      return t;
    }

    /// <inheritdoc cref="ReadInt32s"/>
    internal int ReadInt64s(Span<long> target)
    {
      byte[] buffer = Buf;
      int pos = Pos;
      int end = End;
      if (pos >= end || (uint)end > (uint)buffer.Length)
        return 0;
      ref byte data = ref buffer[0];
      ref long to = ref MemoryMarshal.GetReference(target);
      bool swap = _littleEndian != BitConverter.IsLittleEndian;
      int t = 0;
      while (t < target.Length && pos < end)
      {
        ref byte at = ref Unsafe.Add(ref data, pos);
        ref byte next = ref Unsafe.Add(ref at, 1);
        byte c = at;
        int left = end - pos;
        long value;
        if (c <= 0x7F) { value = c; pos++; }
        else if (c >= 0xE0) { value = (sbyte)c; pos++; }
        else if (c == 0xCE && left >= 5) { value = U32(ref next, swap); pos += 5; }
        else if (c == 0xCD && left >= 3) { value = U16(ref next, swap); pos += 3; }
        else if (c == 0xCC && left >= 2) { value = next; pos += 2; }
        else if (c == 0xD3 && left >= 9) { value = (long)U64(ref next, swap); pos += 9; }
        else if (c == 0xD2 && left >= 5) { value = (int)U32(ref next, swap); pos += 5; }
        else if (c == 0xD1 && left >= 3) { value = (short)U16(ref next, swap); pos += 3; }
        else if (c == 0xD0 && left >= 2) { value = (sbyte)next; pos += 2; }
        else if (c == 0xCF && left >= 9)
        {
          ulong unsigned = U64(ref next, swap);
          if (unsigned > long.MaxValue)
            break; // the reader of the type converts or refuses it
          value = (long)unsigned;
          pos += 9;
        }
        else
          break;
        Unsafe.Add(ref to, t++) = value;
      }
      Pos = pos;
      return t;
    }

    /// <inheritdoc cref="ReadInt32s"/>
    internal int ReadSingles(Span<float> target)
    {
      byte[] buffer = Buf;
      int pos = Pos;
      int end = End;
      if (pos >= end || (uint)end > (uint)buffer.Length)
        return 0;
      ref byte data = ref buffer[0];
      ref uint to = ref Unsafe.As<float, uint>(ref MemoryMarshal.GetReference(target)); // the bits as they are
      bool swap = _littleEndian != BitConverter.IsLittleEndian;
      int t = 0;
      while (t < target.Length && end - pos >= 5 && Unsafe.Add(ref data, pos) == 0xCA)
      {
        Unsafe.Add(ref to, t++) = U32(ref Unsafe.Add(ref data, pos + 1), swap);
        pos += 5;
      }
      Pos = pos;
      return t;
    }

    /// <inheritdoc cref="ReadInt32s"/>
    internal int ReadDoubles(Span<double> target)
    {
      byte[] buffer = Buf;
      int pos = Pos;
      int end = End;
      if (pos >= end || (uint)end > (uint)buffer.Length)
        return 0;
      ref byte data = ref buffer[0];
      ref double to = ref MemoryMarshal.GetReference(target);
      bool swap = _littleEndian != BitConverter.IsLittleEndian;
      int t = 0;
      while (t < target.Length && pos < end)
      {
        ref byte at = ref Unsafe.Add(ref data, pos);
        int left = end - pos;
        if (at == 0xCB && left >= 9)
        {
          Unsafe.As<double, ulong>(ref Unsafe.Add(ref to, t++)) = U64(ref Unsafe.Add(ref at, 1), swap);
          pos += 9;
        }
        else if (at == 0xCA && left >= 5) // a float, as TryReadDouble takes it
        {
          Unsafe.Add(ref to, t++) = new SingleBits(U32(ref Unsafe.Add(ref at, 1), swap)).Value;
          pos += 5;
        }
        else
          break;
      }
      Pos = pos;
      return t;
    }

    /// <summary>
    /// sbyte, short, ushort, uint and ulong: the integer formats with a value that fits the type (IntegerReader: <see cref="TryReadAnyInt64"/>, for ulong <see cref="TryReadUInt64"/>, then the range of the type).
    /// A value that does not fit stops the loop: the reader of the type reports the overflow.
    /// </summary>
    /// <inheritdoc cref="ReadInt32s"/>
    internal int ReadIntegers<T>(Span<T> target) where T : struct
    {
      byte[] buffer = Buf;
      int pos = Pos;
      int end = End;
      if (pos >= end || (uint)end > (uint)buffer.Length)
        return 0;
      ref byte data = ref buffer[0];
      ref T to = ref MemoryMarshal.GetReference(target);
      bool swap = _littleEndian != BitConverter.IsLittleEndian;
      int t = 0;
      while (t < target.Length && pos < end)
      {
        ref byte at = ref Unsafe.Add(ref data, pos);
        ref byte next = ref Unsafe.Add(ref at, 1);
        byte c = at;
        int left = end - pos;
        long value;
        int length;
        if (c <= 0x7F) { value = c; length = 1; }
        else if (c >= 0xE0) { value = (sbyte)c; length = 1; }
        else if (c == 0xCC && left >= 2) { value = next; length = 2; }
        else if (c == 0xCD && left >= 3) { value = U16(ref next, swap); length = 3; }
        else if (c == 0xCE && left >= 5) { value = U32(ref next, swap); length = 5; }
        else if (c == 0xD0 && left >= 2) { value = (sbyte)next; length = 2; }
        else if (c == 0xD1 && left >= 3) { value = (short)U16(ref next, swap); length = 3; }
        else if (c == 0xD2 && left >= 5) { value = (int)U32(ref next, swap); length = 5; }
        else if (c == 0xD3 && left >= 9) { value = (long)U64(ref next, swap); length = 9; }
        else if (c == 0xCF && left >= 9)
        {
          ulong unsigned = U64(ref next, swap);
          if (typeof(T) == typeof(ulong))
          {
            Unsafe.As<T, ulong>(ref Unsafe.Add(ref to, t++)) = unsigned;
            pos += 9;
            continue;
          }
          if (unsigned > long.MaxValue)
            break;
          value = (long)unsigned;
          length = 9;
        }
        else
          break;

        ref T item = ref Unsafe.Add(ref to, t);
        if (typeof(T) == typeof(sbyte))
        {
          if ((ulong)(value - sbyte.MinValue) > byte.MaxValue)
            break;
          Unsafe.As<T, sbyte>(ref item) = (sbyte)value;
        }
        else if (typeof(T) == typeof(short))
        {
          if ((ulong)(value - short.MinValue) > ushort.MaxValue)
            break;
          Unsafe.As<T, short>(ref item) = (short)value;
        }
        else if (typeof(T) == typeof(ushort))
        {
          if ((ulong)value > ushort.MaxValue)
            break;
          Unsafe.As<T, ushort>(ref item) = (ushort)value;
        }
        else if (typeof(T) == typeof(uint))
        {
          if ((ulong)value > uint.MaxValue)
            break;
          Unsafe.As<T, uint>(ref item) = (uint)value;
        }
        else
        {
          if (value < 0)
            break;
          Unsafe.As<T, ulong>(ref item) = (ulong)value;
        }
        t++;
        pos += length;
      }
      Pos = pos;
      return t;
    }

    /// <summary>
    /// true and false (<see cref="TryReadBool"/>).
    /// </summary>
    /// <inheritdoc cref="ReadInt32s"/>
    internal int ReadBools(Span<bool> target)
    {
      byte[] buffer = Buf;
      int pos = Pos;
      int end = End;
      if (pos >= end || (uint)end > (uint)buffer.Length)
        return 0;
      ref byte data = ref buffer[0];
      ref bool to = ref MemoryMarshal.GetReference(target);
      int t = 0;
      while (t < target.Length && pos < end)
      {
        byte c = Unsafe.Add(ref data, pos);
        if (c == 0xC3) Unsafe.Add(ref to, t) = true;
        else if (c == 0xC2) Unsafe.Add(ref to, t) = false;
        else break;
        t++;
        pos++;
      }
      Pos = pos;
      return t;
    }

    /// <summary>
    /// Guids as bin 16 (<see cref="TryReadGuid"/>): the bytes are in the order of <see cref="Guid.ToByteArray"/>, which is the order of a Guid in memory on a little-endian machine.
    /// </summary>
    /// <inheritdoc cref="ReadInt32s"/>
    internal int ReadGuids(Span<Guid> target)
    {
      byte[] buffer = Buf;
      int pos = Pos;
      int end = End;
      if (!BitConverter.IsLittleEndian || pos >= end || (uint)end > (uint)buffer.Length) // big-endian: TryReadGuid per value
        return 0;
      ref byte data = ref buffer[0];
      ref Guid to = ref MemoryMarshal.GetReference(target);
      int t = 0;
      while (t < target.Length && end - pos >= 18 && Unsafe.Add(ref data, pos) == 0xC4 && Unsafe.Add(ref data, pos + 1) == 16)
      {
        Unsafe.Add(ref to, t++) = Unsafe.ReadUnaligned<Guid>(ref Unsafe.Add(ref data, pos + 2));
        pos += 18;
      }
      Pos = pos;
      return t;
    }

    /// <summary>
    /// Decimals as the built-in DecimalExtension writes them (DecimalReader): fixext 16 of <paramref name="typeCode"/> with the 16 bytes of the decimal as they are in memory.
    /// </summary>
    /// <inheritdoc cref="ReadInt32s"/>
    internal int ReadDecimals(Span<decimal> target, byte typeCode)
    {
      byte[] buffer = Buf;
      int pos = Pos;
      int end = End;
      if (pos >= end || (uint)end > (uint)buffer.Length)
        return 0;
      ref byte data = ref buffer[0];
      ref decimal to = ref MemoryMarshal.GetReference(target);
      int t = 0;
      while (t < target.Length && end - pos >= 18 && Unsafe.Add(ref data, pos) == 0xD8 && Unsafe.Add(ref data, pos + 1) == typeCode)
      {
        Unsafe.Add(ref to, t++) = Unsafe.ReadUnaligned<decimal>(ref Unsafe.Add(ref data, pos + 2));
        pos += 18;
      }
      Pos = pos;
      return t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort U16(ref byte source, bool swap)
    {
      ushort value = Unsafe.ReadUnaligned<ushort>(ref source);
      return swap ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint U32(ref byte source, bool swap)
    {
      uint value = Unsafe.ReadUnaligned<uint>(ref source);
      return swap ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong U64(ref byte source, bool swap)
    {
      ulong value = Unsafe.ReadUnaligned<ulong>(ref source);
      return swap ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    #endregion

    public bool TryReadString(out string value)
    {
      int length = TryReadStringHeader();
      if (length < 0) { value = null; return false; }
      value = Decode(length);
      Pos += length;
      return true;
    }

    /// <summary>
    /// The string of <paramref name="length"/> bytes at the position (checked by the caller), the position is not moved.
    /// </summary>
    private string Decode(int length)
    {
      if (length == 0)
        return string.Empty;
      Encoding encoding = MsgPackOptions.StringEncoding;
#if NET8_0_OR_GREATER
      if (MsgPackWriter.IsUtf8(encoding) && Ascii.IsValid(new ReadOnlySpan<byte>(Buf, Pos, length))) // ASCII text: the same chars in Latin-1, which widens the bytes without validating them again (UTF8Encoding.GetString takes 30-40% longer)
        return Encoding.Latin1.GetString(Buf, Pos, length);
#endif
      return encoding.GetString(Buf, Pos, length);
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// A string of at most <paramref name="chars"/>.Length bytes, decoded into <paramref name="chars"/> (formatted values: no string is made).
    /// </summary>
    /// <returns>false when the next value is not such a string (the position is unchanged)</returns>
    internal bool TryReadShortString(Span<char> chars, out int count)
    {
      int start = Pos;
      Encoding encoding = MsgPackOptions.StringEncoding;
      int length = TryReadStringHeader();
      if (length < 0 || length > chars.Length || !(encoding is UTF8Encoding)) // UTF-8 has no more chars than bytes
      {
        Pos = start;
        count = 0;
        return false;
      }
      count = encoding.GetChars(new ReadOnlySpan<byte>(Buf, Pos, length), chars);
      Pos += length;
      return true;
    }
#endif

    /// <returns>-1 when the next value is not binary data, otherwise the length (the position is at the first byte)</returns>
    internal int TryReadBinHeader()
    {
      byte c = Peek();
      long length;
      if (c == 0xC4) { Pos++; length = ReadByte(); }
      else if (c == 0xC5) { Pos++; length = U16(); }
      else if (c == 0xC6) { Pos++; length = U32(); }
      else return -1;

      if (length > End - Pos)
        throw EndOfData();
      return (int)length;
    }

    public bool TryReadGuid(out Guid value)
    {
      if (Peek() == 0xC4 && Pos + 1 < End && Buf[Pos + 1] == 16)
      {
        Need(18);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        value = new Guid(new ReadOnlySpan<byte>(Buf, Pos + 2, 16));
#else
        byte[] bytes = new byte[16];
        Buffer.BlockCopy(Buf, Pos + 2, bytes, 0, 16);
        value = new Guid(bytes);
#endif
        Pos += 18;
        return true;
      }
      value = default(Guid);
      return false;
    }

    /// <summary>
    /// A Guid as a string of 36 characters in the format "D" (<see cref="GuidFormat.String"/>, what the writers write), parsed from UTF-8 (<see cref="GuidText"/>).
    /// </summary>
    /// <returns>false for any other value or text (the position is unchanged): left to <c>Guid.TryParse</c> (GuidStringReader), which takes other formats and reports what is not a Guid</returns>
    internal bool TryReadGuidString(out Guid value)
    {
      int at = Pos;
      if (End - at >= 2 + GuidText.Length && Buf[at] == 0xD9 && Buf[at + 1] == GuidText.Length && MsgPackWriter.IsUtf8(MsgPackOptions.StringEncoding) && GuidText.TryParse(Buf, at + 2, out value)) // str 8, as the writers write it
      {
        Pos = at + 2 + GuidText.Length;
        return true;
      }
      value = default(Guid);
      return false;
    }

    /// <summary>
    /// A decimal as a string (<see cref="DecimalFormat.String"/>) that <see cref="DecimalText"/> parses (a mantissa of 64 bits, the text of the invariant culture).
    /// </summary>
    /// <returns>false for any other value or text (the position is unchanged): left to DecimalStringReader</returns>
    internal bool TryReadDecimalString(out decimal value)
    {
      int start = Pos;
      int length = TryReadStringHeader();
      if (length >= 0 && MsgPackWriter.IsUtf8(MsgPackOptions.StringEncoding) && DecimalText.TryParse(Buf, Pos, length, out value)) // ASCII, the same bytes in UTF-8
      {
        Pos += length;
        return true;
      }
      Pos = start;
      value = 0m;
      return false;
    }

    /// <summary>
    /// The header of an extension.
    /// </summary>
    /// <returns>false when the next value is not an extension (the position is unchanged)</returns>
    internal bool TryReadExtHeader(out sbyte typeCode, out int length, out MsgPackTypeId format)
    {
      byte c = Peek();
      int start = Pos;
      long len;
      switch (c)
      {
        case 0xD4: Pos++; len = 1; break;
        case 0xD5: Pos++; len = 2; break;
        case 0xD6: Pos++; len = 4; break;
        case 0xD7: Pos++; len = 8; break;
        case 0xD8: Pos++; len = 16; break;
        case 0xC7: Pos++; len = ReadByte(); break;
        case 0xC8: Pos++; len = U16(); break;
        case 0xC9: Pos++; len = U32(); break;
        default:
          typeCode = 0;
          length = 0;
          format = MsgPackTypeId.NeverUsed;
          return false;
      }
      typeCode = unchecked((sbyte)ReadByte());
      if (len > End - Pos)
        throw EndOfData();
      length = (int)len;
      format = (MsgPackTypeId)c;
      return true;
    }

    private static readonly DateTime Zero = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    private const long ZeroTicks = 621355968000000000; // of Zero

    /// <summary>
    /// A timestamp (extension type -1), returned with the Kind of <see cref="MsgPackOptions.ReadDateTimeKind"/> (UTC by default) like MpDateTime.Value.
    /// </summary>
    public bool TryReadDateTime(out DateTime value)
    {
      if (!TryReadTimestamp(out DateTime utc))
      {
        value = default(DateTime);
        return false;
      }
      value = MsgPackOptions.FromTimestamp(utc, _dateTimeKind);
      return true;
    }

    /// <summary>
    /// A timestamp (extension type -1) in UTC.
    /// </summary>
    internal bool TryReadTimestamp(out DateTime value)
    {
      int pos = Pos;
      byte[] buffer = Buf;
      int left = End - pos;
      if (left >= 6 && buffer[pos + 1] == 0xFF) // the 32 and 64 bit formats complete: in ticks (see ReadTimestamp), without a call per part
      {
        byte code = buffer[pos];
        if (code == 0xD7 && left >= 10)
        {
          ulong bits = U64(new ReadOnlySpan<byte>(buffer, pos + 2, 8));
          Pos = pos + 10;
          value = new DateTime(ZeroTicks + (long)(bits & 0x3FFFFFFFF) * TimeSpan.TicksPerSecond + (long)(bits >> 34) / 100, DateTimeKind.Utc);
          return true;
        }
        if (code == 0xD6)
        {
          uint seconds = U32(new ReadOnlySpan<byte>(buffer, pos + 2, 4));
          Pos = pos + 6;
          value = new DateTime(ZeroTicks + seconds * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
          return true;
        }
      }

      if (End - Pos >= 2 && Buf[Pos + 1] == 0xFF) // the 32 and 64 bit formats, without the general extension header (data that ends early: the same error)
      {
        byte c = Buf[Pos];
        if (c == 0xD6) { Pos += 2; value = ReadTimestamp(MsgPackTypeId.MpFExt4, 4); return true; }
        if (c == 0xD7) { Pos += 2; value = ReadTimestamp(MsgPackTypeId.MpFExt8, 8); return true; }
      }

      int start = Pos;
      if (!TryReadExtHeader(out sbyte typeCode, out int length, out MsgPackTypeId format) || typeCode != -1)
      {
        Pos = start;
        value = default(DateTime);
        return false;
      }
      value = ReadTimestamp(format, length);
      return true;
    }

    /// <summary>
    /// The data of a timestamp (after its header) in UTC, as MpDateTime.ConvertExt.
    /// </summary>
    private DateTime ReadTimestamp(MsgPackTypeId format, int length)
    {
      // The formats of 32 and 64 bits in ticks: always in the range of DateTime (up to the years 2106 and 2514), the same value as AddSeconds gives, which calculates with a double
      if (format == MsgPackTypeId.MpFExt4)
        return new DateTime(ZeroTicks + U32() * TimeSpan.TicksPerSecond, DateTimeKind.Utc);

      if (format == MsgPackTypeId.MpFExt8)
      {
        ulong bits = U64();
        return new DateTime(ZeroTicks + (long)(bits & 0x3FFFFFFFF) * TimeSpan.TicksPerSecond + (long)(bits >> 34) / 100, DateTimeKind.Utc);
      }

      if (format == MsgPackTypeId.MpExt8 && length == 12)
      {
        uint nanoseconds = U32();
        long seconds = (long)U64();
        return Zero.AddSeconds(seconds) + TimeSpan.FromTicks(nanoseconds / 100); // the nanoseconds are added, also before 1970
      }

      throw new MsgPackException($"The extension type -1 with base type {format} and {length} bytes is not recognised as a DatTime or TimeStamp.", 0, format);
    }

    #endregion

    #region Plain values (as LsMsgPack unpacks them: MsgPackItem.UnpackedValue)

    /// <summary>
    /// Reads any value: null, bool, the integer type of the format (byte for positive fixints, sbyte for negative ones, ...), float or double, string, byte[], a DateTime (timestamps, see MsgPackOptions.ReadDateTimeKind),
    /// the value of a custom extension, <see cref="MsgPackExtension"/> (other extensions), object[] (arrays) or KeyValuePair&lt;object, object&gt;[] (maps).
    /// </summary>
    internal object ReadPlain(int depth)
    {
      byte c = ReadByte();
      if (c <= 0x7F) return c;
      if (c >= 0xE0) return (sbyte)c;
      if ((c & 0xE0) == 0xA0) return ReadStringBody(c & 0x1F);
      if ((c & 0xF0) == 0x90) return ReadArrayBody(Count(c & 0x0F, 1), depth);
      if ((c & 0xF0) == 0x80) return ReadMapBody(Count(c & 0x0F, 2), depth);

      switch (c)
      {
        case 0xC0: return null;
        case 0xC2: return false;
        case 0xC3: return true;
        case 0xCC: return ReadByte();
        case 0xCD: return U16();
        case 0xCE: return U32();
        case 0xCF: return U64();
        case 0xD0: return (sbyte)ReadByte();
        case 0xD1: return (short)U16();
        case 0xD2: return (int)U32();
        case 0xD3: return (long)U64();
        case 0xCA: return ReadSingleBits();
        case 0xCB: return BitConverter.Int64BitsToDouble((long)U64());
        case 0xD9: return ReadStringBody(ReadByte());
        case 0xDA: return ReadStringBody(U16());
        case 0xDB: return ReadStringBody(U32());
        case 0xC4: return Bytes(ReadByte());
        case 0xC5: return Bytes(U16());
        case 0xC6: return Bytes(U32());
        case 0xDC: return ReadArrayBody(Count(U16(), 1), depth);
        case 0xDD: return ReadArrayBody(Count(U32(), 1), depth);
        case 0xDE: return ReadMapBody(Count(U16(), 2), depth);
        case 0xDF: return ReadMapBody(Count(U32(), 2), depth);
        case 0xD4: case 0xD5: case 0xD6: case 0xD7: case 0xD8: case 0xC7: case 0xC8: case 0xC9:
          Pos--;
          return ReadPlainExtension();
      }

      throw LsMsgPack.Streaming.Escape.NotSupported(Pos - 1);
    }

    private string ReadStringBody(long length)
    {
      if (length > End - Pos)
        throw EndOfData();
      string value = Decode((int)length);
      Pos += (int)length;
      return value;
    }

    private object[] ReadArrayBody(int count, int depth)
    {
      if (depth >= _maxDepth)
        throw TooDeep();
      object[] items = new object[count];
      for (int t = 0; t < count; t++)
        items[t] = ReadPlain(depth + 1);
      return items;
    }

    private KeyValuePair<object, object>[] ReadMapBody(int count, int depth)
    {
      if (depth >= _maxDepth)
        throw TooDeep();
      KeyValuePair<object, object>[] entries = new KeyValuePair<object, object>[count];
      for (int t = 0; t < count; t++)
      {
        object key = ReadPlain(depth + 1);
        entries[t] = new KeyValuePair<object, object>(key, ReadPlain(depth + 1));
      }
      return entries;
    }

    internal MsgPackException TooDeep()
    {
      return new MsgPackException($"The data is nested deeper than {nameof(LtMsgPackOptions)}.{nameof(LtMsgPackOptions.MaxDepth)} ({_maxDepth}).");
    }

    private object ReadPlainExtension()
    {
      TryReadExtHeader(out sbyte typeCode, out int length, out MsgPackTypeId format);
      if (typeCode == -1) // timestamps first, as MpExt.Read does
        return MsgPackOptions.FromTimestamp(ReadTimestamp(format, length), _dateTimeKind);

      LtExtension[] extensions = _extensions;
      for (int t = 0; t < extensions.Length; t++)
      {
        LtExtension extension = extensions[t];
        if (extension != null && extension.TypeCode == typeCode)
        {
          object value = extension.ReadBoxed(new ReadOnlySpan<byte>(Buf, Pos, length));
          Pos += length;
          return value;
        }
      }

      return new MsgPackExtension(typeCode, Bytes(length), format);
    }

    /// <summary>
    /// Skips the next value.
    /// </summary>
    public void Skip(int depth)
    {
      byte c = ReadByte();
      if (c <= 0x7F || c >= 0xE0) return;
      if ((c & 0xE0) == 0xA0) { Advance(c & 0x1F); return; }
      if ((c & 0xF0) == 0x90) { SkipItems(c & 0x0F, depth); return; }
      if ((c & 0xF0) == 0x80) { SkipItems(2L * (c & 0x0F), depth); return; }
      switch (c)
      {
        case 0xC0: case 0xC2: case 0xC3: return;
        case 0xCC: case 0xD0: Advance(1); return;
        case 0xCD: case 0xD1: Advance(2); return;
        case 0xCE: case 0xD2: case 0xCA: Advance(4); return;
        case 0xCF: case 0xD3: case 0xCB: Advance(8); return;
        case 0xD9: case 0xC4: Advance(ReadByte()); return;
        case 0xDA: case 0xC5: Advance(U16()); return;
        case 0xDB: case 0xC6: Advance(U32()); return;
        case 0xDC: SkipItems(U16(), depth); return;
        case 0xDD: SkipItems(U32(), depth); return;
        case 0xDE: SkipItems(2L * U16(), depth); return;
        case 0xDF: SkipItems(2L * U32(), depth); return;
        case 0xD4: Advance(2); return;
        case 0xD5: Advance(3); return;
        case 0xD6: Advance(5); return;
        case 0xD7: Advance(9); return;
        case 0xD8: Advance(17); return;
        case 0xC7: Advance(1L + ReadByte()); return;
        case 0xC8: Advance(1L + U16()); return;
        case 0xC9: Advance(1L + U32()); return;
      }
      throw LsMsgPack.Streaming.Escape.NotSupported(Pos - 1);
    }

    private void Advance(long count)
    {
      if (count > End - Pos)
        throw EndOfData();
      Pos += (int)count;
    }

    private void SkipItems(long count, int depth)
    {
      if (depth >= _maxDepth)
        throw TooDeep();
      if (count > End - Pos)
        throw EndOfData();
      for (long t = 0; t < count; t++)
        Skip(depth + 1);
    }

    #endregion
  }
}
