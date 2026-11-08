using System;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif

namespace LtMsgPack.IO
{
  /// <summary>
  /// A Guid from its text as UTF-8 (<see cref="GuidFormat.String"/>) without decoding it to chars first: <c>Guid.TryParse</c> of the chars took 37 ns per value, <c>Utf8Parser</c> as long,
  /// this 9.5 ns (net8.0, Vector128) or 21.5 ns (a table per digit).
  /// <para>Only the format the writers write ("D": 36 characters, hex digits of either case, dashes at 8, 13, 18 and 23). Anything else returns false and is left to <c>Guid.TryParse</c>,
  /// which takes more (other formats, white space, the '+' and "0x" it accepts in the groups) and reports what it does not take.</para>
  /// </summary>
  internal static class GuidText
  {
    internal const int Length = 36;

    /// <summary>
    /// Parses the 36 bytes at <paramref name="pos"/> (the caller checked the length of the string).
    /// </summary>
    /// <returns>false when they are not a Guid in the format "D" (then <paramref name="value"/> is empty)</returns>
    internal static bool TryParse(byte[] buf, int pos, out Guid value)
    {
      if (!BitConverter.IsLittleEndian || (uint)pos > (uint)buf.Length || buf.Length - pos < Length) // the bytes are put into the Guid's memory as little-endian lays it out
      {
        value = default(Guid);
        return false;
      }
      ref byte s = ref buf[pos];
      if (Unsafe.Add(ref s, 8) != (byte)'-' || Unsafe.Add(ref s, 13) != (byte)'-' || Unsafe.Add(ref s, 18) != (byte)'-' || Unsafe.Add(ref s, 23) != (byte)'-')
      {
        value = default(Guid);
        return false;
      }
#if NET8_0_OR_GREATER
      if (Vector128.IsHardwareAccelerated)
        return TryParseVector(ref s, out value);
#endif
      return TryParseDigits(ref s, out value);
    }

#if NET8_0_OR_GREATER
    private static bool TryParseVector(ref byte s, out Guid value)
    {
      Vector128<byte> a = Vector128.LoadUnsafe(ref s); // 0..15
      Vector128<byte> b = Vector128.LoadUnsafe(ref s, 2); // 2..17
      Vector128<byte> c = Vector128.LoadUnsafe(ref s, 19); // 19..34
      Vector128<byte> d = Vector128.LoadUnsafe(ref s, 20); // 20..35
      // The 32 digits in order (an index out of range gives 0): 0-7 and 9-12 from a, 14-17 from b; 19-22 and 24-34 from c, 35 from d
      Vector128<byte> first = Vector128.Shuffle(a, Vector128.Create((byte)0, 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 255, 255, 255, 255))
        | Vector128.Shuffle(b, Vector128.Create((byte)255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 12, 13, 14, 15));
      Vector128<byte> second = Vector128.Shuffle(c, Vector128.Create((byte)0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 255))
        | Vector128.Shuffle(d, Vector128.Create((byte)255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 15));
      if (!Nibbles(ref first) | !Nibbles(ref second))
      {
        value = default(Guid);
        return false;
      }
      // Pairs of digits into bytes: as ushort (little-endian) a pair is high | low << 8, the byte (high << 4) | low
      Vector128<ushort> f = first.AsUInt16();
      Vector128<ushort> g = second.AsUInt16();
      Vector128<ushort> mask = Vector128.Create((ushort)0xFF);
      Vector128<byte> bytes = Vector128.Narrow(((f << 4) | (f >> 8)) & mask, ((g << 4) | (g >> 8)) & mask);
      // The text has a, b and c big-endian, the Guid stores them little-endian
      bytes = Vector128.Shuffle(bytes, Vector128.Create((byte)3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15));
      value = Unsafe.As<Vector128<byte>, Guid>(ref bytes);
      return true;
    }

    /// <summary>
    /// Hex digits (either case) into their values.
    /// </summary>
    /// <returns>false when one of them is not a hex digit</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Nibbles(ref Vector128<byte> v)
    {
      Vector128<byte> digit = v - Vector128.Create((byte)'0');
      Vector128<byte> letter = (v | Vector128.Create((byte)0x20)) - Vector128.Create((byte)'a');
      Vector128<byte> isDigit = Vector128.LessThan(digit, Vector128.Create((byte)10));
      Vector128<byte> isLetter = Vector128.LessThan(letter, Vector128.Create((byte)6));
      if ((isDigit | isLetter) != Vector128<byte>.AllBitsSet)
        return false;
      v = Vector128.ConditionalSelect(isDigit, digit, letter + Vector128.Create((byte)10));
      return true;
    }
#endif

    /// <summary>
    /// The value of each hex digit, 255 for any other byte.
    /// </summary>
    private static ReadOnlySpan<byte> HexValues => new byte[256]
    {
      255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
      255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 255, 255, 255, 255, 255, 255,
      255, 10, 11, 12, 13, 14, 15, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
      255, 10, 11, 12, 13, 14, 15, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
      255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
      255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
      255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
      255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255
    };

    /// <summary>
    /// The position of the first digit of each byte of the Guid in the text, in the order of the text.
    /// </summary>
    private static ReadOnlySpan<byte> DigitsAt => new byte[] { 0, 2, 4, 6, 9, 11, 14, 16, 19, 21, 24, 26, 28, 30, 32, 34 };

    /// <summary>
    /// Where each byte of the text goes in the Guid's memory (a, b and c are big-endian in the text, little-endian in the Guid).
    /// </summary>
    private static ReadOnlySpan<byte> ByteTo => new byte[] { 3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15 };

    private static bool TryParseDigits(ref byte s, out Guid value)
    {
      value = default(Guid);
      ReadOnlySpan<byte> hex = HexValues;
      ReadOnlySpan<byte> at = DigitsAt;
      ReadOnlySpan<byte> to = ByteTo;
      ref byte target = ref Unsafe.As<Guid, byte>(ref value);
      int bad = 0;
      for (int t = 0; t < 16; t++)
      {
        int x = at[t];
        int high = hex[Unsafe.Add(ref s, x)];
        int low = hex[Unsafe.Add(ref s, x + 1)];
        bad |= high | low;
        Unsafe.Add(ref target, to[t]) = (byte)((high << 4) | low);
      }
      if (bad > 15)
      {
        value = default(Guid);
        return false;
      }
      return true;
    }
  }
}
