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

    /// <summary>
    /// The longest text <see cref="TryParse"/> reads: a sign, 19 digits and a point.
    /// </summary>
    private const int MaxParsedLength = 21;

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

      if (mantissa <= uint.MaxValue)
        return Format32((uint)mantissa, scale, negative, destination, at); // most prices and quantities: the divisions by constants are 32 bit multiplications

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

    /// <summary>
    /// The digits of 0 to 99, two bytes each.
    /// </summary>
    private static readonly byte[] Pairs = CreatePairs();

    private static byte[] CreatePairs()
    {
      byte[] pairs = new byte[200];
      for (int t = 0; t < 100; t++)
      {
        pairs[t * 2] = (byte)('0' + t / 10);
        pairs[t * 2 + 1] = (byte)('0' + t % 10);
      }
      return pairs;
    }

    /// <summary>
    /// <see cref="TryFormat"/> for a mantissa of 32 bits: two digits per division, the number of digits from comparisons (the invoices with decimals as strings write 4-5% faster).
    /// </summary>
    private static int Format32(uint mantissa, int scale, bool negative, byte[] destination, int at)
    {
      int digits = CountDigits(mantissa);
      int length = (negative ? 1 : 0) + (scale == 0 ? digits : (digits > scale ? digits + 1 : scale + 2)); // "0." and leading zeros when there are no more digits than decimals
      byte[] pairs = Pairs;
      int pos = at + length;
      if (scale != 0)
      {
        int decimals = scale; // from the last one
        for (; decimals >= 2; decimals -= 2)
        {
          uint next = mantissa / 100;
          uint pair = (mantissa - next * 100) * 2;
          destination[pos - 1] = pairs[pair + 1];
          destination[pos - 2] = pairs[pair];
          pos -= 2;
          mantissa = next;
        }
        if (decimals != 0)
        {
          uint next = mantissa / 10;
          destination[--pos] = (byte)('0' + (int)(mantissa - next * 10));
          mantissa = next;
        }
        destination[--pos] = (byte)'.';
      }
      while (mantissa >= 100)
      {
        uint next = mantissa / 100;
        uint pair = (mantissa - next * 100) * 2;
        destination[pos - 1] = pairs[pair + 1];
        destination[pos - 2] = pairs[pair];
        pos -= 2;
        mantissa = next;
      }
      if (mantissa >= 10)
      {
        destination[pos - 1] = pairs[mantissa * 2 + 1];
        destination[pos - 2] = pairs[mantissa * 2];
        pos -= 2;
      }
      else
        destination[--pos] = (byte)('0' + (int)mantissa);
      if (negative)
        destination[--pos] = (byte)'-';
      return length;
    }

    private static int CountDigits(uint value)
    {
      if (value < 100) return value < 10 ? 1 : 2;
      if (value < 10000) return value < 1000 ? 3 : 4;
      if (value < 1000000) return value < 100000 ? 5 : 6;
      if (value < 100000000) return value < 10000000 ? 7 : 8;
      return value < 1000000000 ? 9 : 10;
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
      if (length > MaxParsedLength)
        return false;
      int end = at + length;
      int pos = at;
      bool negative = pos < end && buffer[pos] == (byte)'-';
      if (negative)
        pos++;

      // The digits before the point, then the ones after it: two loops without a test per digit for where the point was
      int first = pos;
      ulong mantissa = 0; // 19 digits always fit in 64 bits (more are refused below, whatever it wrapped around to)
      for (; pos < end; pos++)
      {
        uint digit = (uint)(buffer[pos] - (byte)'0');
        if (digit > 9)
          break;
        mantissa = mantissa * 10 + digit;
      }
      int whole = pos - first;
      int scale = 0;
      if (pos < end)
      {
        if (buffer[pos] != (byte)'.')
          return false;
        int point = ++pos;
        for (; pos < end; pos++)
        {
          uint digit = (uint)(buffer[pos] - (byte)'0');
          if (digit > 9)
            return false;
          mantissa = mantissa * 10 + digit;
        }
        scale = pos - point;
        if (scale == 0) // "1." (and ".")
          return false;
      }

      if (whole == 0 || whole + scale > 19 || (negative && mantissa == 0)) // no digit before the point (".5": decimal.Parse reads it, but it is not what TryFormat writes), more than 64 bits, negative zero
        return false;

      value = new decimal(unchecked((int)mantissa), unchecked((int)(mantissa >> 32)), 0, negative, (byte)scale);
      return true;
    }
  }
}
