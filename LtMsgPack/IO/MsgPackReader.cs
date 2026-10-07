using LsMsgPack;
using LtMsgPack.Extensions;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace LtMsgPack.IO
{
  /// <summary>
  /// Reads MsgPack values from a byte array. Typed reads (<see cref="TryReadInt64"/>, ...) only accept the formats they expect, anything else is left to <see cref="ReadPlain"/>, which returns what LsMsgPack's items unpack to.
  /// <para>Multi-byte values follow <see cref="MsgPackOptions.EndianAction"/>. Lengths and nesting are checked against the data, so hostile data cannot make it allocate more than the data holds.</para>
  /// </summary>
  internal sealed class MsgPackReader
  {
    internal readonly byte[] Buf;
    internal int Pos;
    internal readonly int End;
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
      byte[] b = Buf;
      ushort value = _littleEndian ? (ushort)(b[Pos] | b[Pos + 1] << 8) : (ushort)(b[Pos] << 8 | b[Pos + 1]);
      Pos += 2;
      return value;
    }

    private uint U32()
    {
      Need(4);
      byte[] b = Buf;
      uint value = _littleEndian
        ? (uint)(b[Pos] | b[Pos + 1] << 8 | b[Pos + 2] << 16 | b[Pos + 3] << 24)
        : (uint)(b[Pos] << 24 | b[Pos + 1] << 16 | b[Pos + 2] << 8 | b[Pos + 3]);
      Pos += 4;
      return value;
    }

    private ulong U64()
    {
      Need(8);
      ulong first = U32();
      ulong second = U32();
      return _littleEndian ? second << 32 | first : first << 32 | second;
    }

    private byte[] Bytes(long count)
    {
      if (count > End - Pos)
        throw EndOfData();
      byte[] result = new byte[count];
      Buffer.BlockCopy(Buf, Pos, result, 0, (int)count);
      Pos += (int)count;
      return result;
    }

    /// <summary>
    /// A count of items or entries, which needs at least one byte each.
    /// </summary>
    private int Count(long count, int bytesPerItem)
    {
      if (count > (End - Pos) / bytesPerItem)
        throw new MsgPackException($"The data claims {count} items, more than the remaining {End - Pos} bytes can hold.");
      return (int)count;
    }

    #endregion

    #region Headers

    internal bool TryReadNil()
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

    internal bool TryReadBool(out bool value)
    {
      byte c = Peek();
      if (c == 0xC3) { Pos++; value = true; return true; }
      if (c == 0xC2) { Pos++; value = false; return true; }
      value = false;
      return false;
    }

    /// <summary>
    /// Any integer that fits a long.
    /// </summary>
    internal bool TryReadInt64(out long value)
    {
      byte c = Peek();
      if (c <= 0x7F) { Pos++; value = c; return true; }
      if (c >= 0xE0) { Pos++; value = (sbyte)c; return true; }
      switch (c)
      {
        case 0xCC: Pos++; value = ReadByte(); return true;
        case 0xCD: Pos++; value = U16(); return true;
        case 0xCE: Pos++; value = U32(); return true;
        case 0xCF:
          {
            int start = Pos;
            Pos++;
            ulong u = U64();
            if (u > long.MaxValue) { Pos = start; value = 0; return false; }
            value = (long)u;
            return true;
          }
        case 0xD0: Pos++; value = (sbyte)ReadByte(); return true;
        case 0xD1: Pos++; value = (short)U16(); return true;
        case 0xD2: Pos++; value = (int)U32(); return true;
        case 0xD3: Pos++; value = (long)U64(); return true;
      }
      value = 0;
      return false;
    }

    /// <summary>
    /// Any non-negative integer.
    /// </summary>
    internal bool TryReadUInt64(out ulong value)
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

    internal bool TryReadDouble(out double value)
    {
      byte c = Peek();
      if (c == 0xCB) { Pos++; value = BitConverter.Int64BitsToDouble((long)U64()); return true; }
      if (c == 0xCA) { Pos++; value = ReadSingleBits(); return true; }
      value = 0;
      return false;
    }

    internal bool TryReadSingle(out float value)
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

    internal bool TryReadString(out string value)
    {
      int length = TryReadStringHeader();
      if (length < 0) { value = null; return false; }
      value = length == 0 ? string.Empty : MsgPackOptions.StringEncoding.GetString(Buf, Pos, length);
      Pos += length;
      return true;
    }

#if NETSTANDARD2_1_OR_GREATER
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

    internal bool TryReadGuid(out Guid value)
    {
      if (Peek() == 0xC4 && Pos + 1 < End && Buf[Pos + 1] == 16)
      {
        Need(18);
#if NETSTANDARD2_1_OR_GREATER
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

    /// <summary>
    /// A timestamp (extension type -1), returned with the Kind of <see cref="MsgPackOptions.ReadDateTimeKind"/> (local time by default) like MpDateTime.Value.
    /// </summary>
    internal bool TryReadDateTime(out DateTime value)
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
      if (End - Pos >= 2 && Buf[Pos + 1] == 0xFF) // the 32 and 64 bit formats, without the general extension header
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
      if (format == MsgPackTypeId.MpFExt4)
        return Zero.AddSeconds(U32());

      if (format == MsgPackTypeId.MpFExt8)
      {
        ulong bits = U64();
        return Zero.AddSeconds(bits & 0x3FFFFFFFF).Add(new TimeSpan((long)(bits >> 34) / 100));
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

      throw new MsgPackException("The specification specifically states that the value 0xC1 should never be used.", Pos - 1, MsgPackTypeId.NeverUsed);
    }

    private string ReadStringBody(long length)
    {
      if (length > End - Pos)
        throw EndOfData();
      string value = length == 0 ? string.Empty : MsgPackOptions.StringEncoding.GetString(Buf, Pos, (int)length);
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
    internal void Skip(int depth)
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
      throw new MsgPackException("The specification specifically states that the value 0xC1 should never be used.", Pos - 1, MsgPackTypeId.NeverUsed);
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
