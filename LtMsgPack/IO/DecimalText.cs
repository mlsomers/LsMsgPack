using System;
using System.Runtime.CompilerServices;

namespace LtMsgPack.IO
{
  /// <summary>
  /// decimal as invariant text (<see cref="DecimalFormat.String"/>) without the general number formatting and parsing of .NET, which took most of the time of these values.
  /// <para>Only for values whose 96 bit mantissa fits in 64 bits (prices, quantities...), with the same text as <c>decimal.ToString(CultureInfo.InvariantCulture)</c> and the same value (and scale) as <c>decimal.Parse</c>.
  /// Anything else returns false and is left to them.</para>
  /// </summary>
  internal static class DecimalText
  {
    /// <summary>
    /// The longest text: a sign, "0." and 28 decimals (scale 28), or 20 digits with a sign and a point.
    /// </summary>
    internal const int MaxLength = 31;

    // The layout of decimal in memory: flags (sign and scale), the high 32 bits, the low 64 bits (.NET Framework: flags, hi, lo, mid; the same bytes on little-endian machines)
#pragma warning disable CS0649 // only read, through Unsafe.As
    private struct Bits
    {
      internal int Flags;
      internal uint Hi;
      internal ulong Lo;
    }
#pragma warning restore CS0649

    /// <summary>
    /// Whether the memory layout is the one of <see cref="Bits"/> (checked once, otherwise the formatting of .NET is used).
    /// </summary>
    private static readonly bool LayoutKnown = CheckLayout();

    private static bool CheckLayout()
    {
      try
      {
        decimal sample = new decimal(unchecked((int)0x89ABCDEF), 0x01234567, 0x0A, true, 5);
        Bits bits = Unsafe.As<decimal, Bits>(ref sample);
        return Unsafe.SizeOf<decimal>() == Unsafe.SizeOf<Bits>() && bits.Flags == unchecked((int)0x80050000) && bits.Hi == 0x0A && bits.Lo == 0x0123456789ABCDEFUL;
      }
      catch (Exception)
      {
        return false;
      }
    }

    /// <summary>
    /// Writes the text of the value as ASCII bytes.
    /// </summary>
    /// <param name="destination">At least <see cref="MaxLength"/> bytes from <paramref name="at"/></param>
    /// <returns>The number of bytes, -1 when the value is left to <c>decimal.ToString</c> (a mantissa over 64 bits, negative zero)</returns>
    internal static int TryFormat(decimal value, byte[] destination, int at)
    {
      if (!LayoutKnown)
        return -1;
      Bits bits = Unsafe.As<decimal, Bits>(ref value);
      ulong mantissa = bits.Lo;
      bool negative = bits.Flags < 0;
      if (bits.Hi != 0 || (negative && mantissa == 0))
        return -1;
      int scale = (bits.Flags >> 16) & 0xFF;

      int digits = CountDigits(mantissa);
      int length = (negative ? 1 : 0) + (scale == 0 ? digits : (digits > scale ? digits + 1 : scale + 2)); // "0." and leading zeros when there are no more digits than decimals
      int end = at + length;
      int pos = end;
      for (int t = 0; t < scale; t++) // the decimals, from the last one
      {
        ulong next = mantissa / 10;
        destination[--pos] = (byte)('0' + (int)(mantissa - next * 10));
        mantissa = next;
      }
      if (scale != 0)
        destination[--pos] = (byte)'.';
      do
      {
        ulong next = mantissa / 10;
        destination[--pos] = (byte)('0' + (int)(mantissa - next * 10));
        mantissa = next;
      } while (mantissa != 0);
      if (negative)
        destination[--pos] = (byte)'-';
      return length;
    }

    private static int CountDigits(ulong value)
    {
      int digits = 1;
      while (value >= 10)
      {
        value /= 10;
        digits++;
      }
      return digits;
    }

    /// <summary>
    /// Reads an optional minus sign, digits and optionally a point and more digits (at most 19 digits): what <see cref="TryFormat"/> writes and most other writers do.
    /// </summary>
    /// <returns>False for anything else (a plus sign, exponents, white space, separators, more digits, negative zero): left to <c>decimal.Parse</c></returns>
    internal static bool TryParse(byte[] buffer, int at, int length, out decimal value)
    {
      value = default(decimal);
      int end = at + length;
      int pos = at;
      bool negative = pos < end && buffer[pos] == (byte)'-';
      if (negative)
        pos++;

      ulong mantissa = 0;
      int digits = 0;
      int scale = -1; // no point yet
      for (; pos < end; pos++)
      {
        uint digit = (uint)(buffer[pos] - (byte)'0');
        if (digit <= 9)
        {
          if (++digits > 19) // 19 digits always fit in 64 bits
            return false;
          mantissa = mantissa * 10 + digit;
          if (scale >= 0)
            scale++;
        }
        else if (buffer[pos] == (byte)'.' && scale < 0)
          scale = 0;
        else
          return false;
      }

      if (scale < 0)
        scale = 0;
      else if (scale == 0) // "1." (and ".")
        return false;
      if (digits == 0 || scale > 28 || (negative && mantissa == 0))
        return false;
      if (scale == digits) // ".5": decimal.Parse reads it, but without a digit before the point it is not what TryFormat writes
        return false;

      value = new decimal(unchecked((int)mantissa), unchecked((int)(mantissa >> 32)), 0, negative, (byte)scale);
      return true;
    }
  }
}
