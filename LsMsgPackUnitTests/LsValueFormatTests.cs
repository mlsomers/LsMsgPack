using LsMsgPack;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The values LsMsgPack formats, parses and converts with its own code instead of the framework's (decimals as text, timestamps): many random values against the framework and LsMsgPack.
  /// </summary>
  [TestClass]
  public class LsValueFormatTests
  {
    private static readonly LsMsgPackSerializer AsText = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false, DecimalFormat = DecimalFormat.String, UseRegisteredContexts = false });
    private static readonly LsMsgPackSerializer Plain = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false, UseRegisteredContexts = false });

    private static IEnumerable<decimal> Decimals()
    {
      yield return 0m;
      yield return 0.0m;
      yield return -0.0m; // negative zero: the framework writes "0.0"
      yield return new decimal(0, 0, 0, true, 0);
      yield return 1m;
      yield return -1m;
      yield return 0.5m;
      yield return -0.05m;
      yield return 12.50m;
      yield return uint.MaxValue;
      yield return uint.MaxValue + 1m;
      yield return ulong.MaxValue;
      yield return ulong.MaxValue + 1m;
      yield return decimal.MaxValue;
      yield return decimal.MinValue;
      yield return 0.0000000000000000000000000001m;
      yield return new decimal(unchecked((int)uint.MaxValue), 0, 0, false, 28);
      yield return new decimal(unchecked((int)uint.MaxValue), 0, 0, true, 10);
      yield return new decimal(-1, -1, 0, false, 19);
      yield return new decimal(-1, -1, 0, true, 28);
      for (int scale = 0; scale <= 28; scale++) // every number of digits at every scale
      {
        ulong mantissa = 0;
        for (int digits = 1; digits <= 19; digits++)
        {
          mantissa = mantissa * 10 + (ulong)(digits % 10);
          yield return new decimal(unchecked((int)mantissa), unchecked((int)(mantissa >> 32)), 0, digits % 2 == 0, (byte)scale);
          ulong power = 1;
          for (int t = 1; t < digits; t++)
            power *= 10;
          yield return new decimal(unchecked((int)power), unchecked((int)(power >> 32)), 0, false, (byte)scale); // 1000...
          yield return new decimal(unchecked((int)(power * 10 - 1)), unchecked((int)((power * 10 - 1) >> 32)), 0, false, (byte)scale); // 999...
        }
      }
      Random random = new Random(20261110);
      for (int t = 0; t < 20000; t++)
      {
        int bits = random.Next(1, 97);
        int lo = bits >= 32 ? random.Next(int.MinValue, int.MaxValue) : random.Next(0, 1 << Math.Min(bits, 30));
        int mid = bits > 64 ? random.Next(int.MinValue, int.MaxValue) : bits > 32 ? random.Next(0, 1 << Math.Min(bits - 32, 30)) : 0;
        int hi = bits > 64 ? random.Next(0, 1 << Math.Min(bits - 64, 30)) : 0;
        yield return new decimal(lo, mid, hi, random.Next(2) == 0, (byte)random.Next(0, 29));
      }
    }

    /// <summary>
    /// DecimalFormat.String: the text is what decimal.ToString writes in the invariant culture (also from LsMsgPack's own formatter, for mantissas up to 64 bits), and reads back to the same value and scale.
    /// </summary>
    [TestMethod]
    public void DecimalsAsText_AsTheFrameworkFormatsAndParsesThem()
    {
      int count = 0;
      foreach (decimal value in Decimals())
      {
        count++;
        string expected = value.ToString(CultureInfo.InvariantCulture);
        byte[] data = AsText.Serialize(value);
        Assert.AreEqual(expected, Plain.Deserialize<string>(data), "written");
        decimal back = AsText.Deserialize<decimal>(data);
        CollectionAssert.AreEqual(decimal.GetBits(decimal.Parse(expected, CultureInfo.InvariantCulture)), decimal.GetBits(back), "read " + expected);
      }
      Assert.IsGreaterThan(20000, count);
    }

    /// <summary>
    /// Texts of other writers: read to what decimal.Parse gives (value and scale), by LsMsgPack's own parser or the framework's.
    /// </summary>
    [TestMethod]
    public void DecimalTexts_ReadAsTheFrameworkParsesThem()
    {
      List<string> texts = new List<string>
      {
        "0", "-0", "0.0", "-0.0", "00", "007", "00.50", "1.", ".5", "-.5", "+1", "+1.5", " 1", "1 ", " 1.5 ", "1e3", "1E-3", "1.5e2", "1,000", "1,000.5", "1.2.3", "--1", "-", ".", "", "1-", "0x10", "١٢",
        "1234567890123456789", "12345678901234567890", "123456789012345678901", "9999999999999999999", "18446744073709551615", "18446744073709551616", "-9223372036854775808",
        "0.1234567890123456789", "0.12345678901234567890", "1.000000000000000000", "1.0000000000000000000", "0.0000000000000000000000000001", "0.00000000000000000000000000001",
        "79228162514264337593543950335", "79228162514264337593543950336", "-79228162514264337593543950335", "1234567890.123456789", "123456789012.3456789", "1234567890123456789.0"
      };
      Random random = new Random(42);
      const string chars = "0123456789.-+ e,";
      for (int t = 0; t < 20000; t++)
      {
        int length = random.Next(1, 24);
        char[] text = new char[length];
        for (int u = 0; u < length; u++)
          text[u] = random.Next(10) < 8 ? (char)('0' + random.Next(10)) : chars[random.Next(chars.Length)]; // mostly digits
        if (random.Next(3) == 0 && length > 2)
          text[random.Next(1, length - 1)] = '.';
        texts.Add(new string(text));
      }

      int parsed = 0;
      foreach (string text in texts)
      {
        byte[] data = Plain.Serialize(text);
        bool valid = decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal expected);
        decimal read;
        try
        {
          read = AsText.Deserialize<decimal>(data);
        }
        catch (Exception ex)
        {
          Assert.IsFalse(valid, $"\"{text}\" is {expected} but reading it threw {ex.GetType().Name}: {ex.Message}");
          continue;
        }
        if (!valid)
          continue; // LsMsgPack.Core's conversion takes some texts the styles above do not
        parsed++;
        CollectionAssert.AreEqual(decimal.GetBits(expected), decimal.GetBits(read), $"\"{text}\"");
      }
      Assert.IsGreaterThan(5000, parsed);
    }

    private static IEnumerable<DateTime> Moments()
    {
      DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      yield return epoch;
      yield return epoch.AddTicks(-1);
      yield return epoch.AddTicks(1);
      yield return epoch.AddSeconds(-1);
      yield return epoch.AddSeconds(uint.MaxValue); // the last of the 32 bit format
      yield return epoch.AddSeconds(uint.MaxValue).AddTicks(1);
      yield return epoch.AddSeconds(uint.MaxValue + 1L);
      yield return epoch.AddSeconds((1L << 34) - 1); // the last second of the 64 bit format
      yield return epoch.AddSeconds((1L << 34) - 1).AddTicks(9999999);
      yield return epoch.AddSeconds(1L << 34);
      yield return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
      yield return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
      yield return new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Local);
      yield return new DateTime(2026, 1, 1, 12, 0, 0, 5, DateTimeKind.Unspecified);
      Random random = new Random(7);
      for (int t = 0; t < 20000; t++)
      {
        long ticks = (long)(random.NextDouble() * DateTime.MaxValue.Ticks);
        if (t % 3 == 0)
          ticks -= ticks % TimeSpan.TicksPerSecond; // whole seconds: the 32 bit format where it fits
        yield return new DateTime(ticks, t % 7 == 0 ? DateTimeKind.Local : DateTimeKind.Utc);
      }
    }

    /// <summary>
    /// Timestamps: the bytes LsMsgPack writes and the values it reads, for every ReadDateTimeKind (LsMsgPack calculates the 32 and 64 bit formats in ticks).
    /// </summary>
    [TestMethod]
    public void Timestamps_AsLsMsgPackWritesAndReadsThem()
    {
      foreach (EndianAction endian in new[] { EndianAction.SwapIfCurrentSystemIsLittleEndian, EndianAction.NeverSwap })
      {
        foreach (DateTimeKind kind in new[] { DateTimeKind.Utc, DateTimeKind.Local, DateTimeKind.Unspecified })
        {
          MsgPackSettings ls = new MsgPackSettings { UseInexedSchema = false, EndianAction = endian, ReadDateTimeKind = kind };
          LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { UseInexedSchema = false, EndianAction = endian, ReadDateTimeKind = kind, UseRegisteredContexts = false });
          foreach (DateTime moment in Moments())
          {
            byte[] expected = MsgPackSerializer.Serialize(moment, typeof(DateTime), ls);
            byte[] data = lt.Serialize(moment);
            CollectionAssert.AreEqual(expected, data, $"{moment:O} ({moment.Kind}), {endian}");
            DateTime lsRead = (DateTime)MsgPackSerializer.Deserialize(typeof(DateTime), data, ls);
            DateTime ltRead = lt.Deserialize<DateTime>(data);
            Assert.AreEqual(lsRead.Ticks, ltRead.Ticks, $"{moment:O} read as {kind}, {endian}");
            Assert.AreEqual(lsRead.Kind, ltRead.Kind, $"{moment:O} read as {kind}, {endian}");
          }
        }
      }
    }
  }
}
