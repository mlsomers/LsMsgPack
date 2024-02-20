using NUnit.Framework;
using LsMsgPack;
using System;

namespace LsMsgPackUnitTests
{
  [TestFixture]
  public class MpIntTest
  {

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(65)]
    [TestCase(127)]
    public void RoundTripPositiveFixnum(byte value)
    {
      MsgPackTests.RoundTripTest<MpInt, byte>(value, 1, MsgPackTypeId.MpBytePart);
    }

    [TestCase(-1)]
    [TestCase(-15)]
    [TestCase(-31)]
    public void RoundTripNegativeFixnum(sbyte value)
    {
      MsgPackTests.RoundTripTest<MpInt, sbyte>(value, 1, MsgPackTypeId.MpBytePart);
    }

    [TestCase(128)]
    [TestCase(200)]
    [TestCase(255)]
    public void RoundTripUint8(byte value)
    {
      MsgPackTests.RoundTripTest<MpInt, byte>(value, 2, MsgPackTypeId.MpUByte);
    }

    [TestCase(-32)]
    [TestCase(-100)]
    [TestCase(-128)]
    public void RoundTripInt8(sbyte value)
    {
      MsgPackTests.RoundTripTest<MpInt, sbyte>(value, 2, MsgPackTypeId.MpSByte);
    }

    [TestCase(-129)]
    [TestCase(-1000)]
    [TestCase(short.MinValue)]
    public void RoundTripInt16(short value)
    {
      MsgPackTests.RoundTripTest<MpInt, short>(value, 3, MsgPackTypeId.MpShort);
    }

    [TestCase((ushort)256)]
    [TestCase((ushort)1000)]
    [TestCase(ushort.MaxValue)]
    public void RoundTripUInt16(ushort value)
    {
      MsgPackTests.RoundTripTest<MpInt, ushort>(value, 3, MsgPackTypeId.MpUShort);
    }

    [TestCase(short.MinValue - 1)]
    [TestCase(short.MinValue - 1000)]
    [TestCase(int.MinValue)]
    public void RoundTripInt32(int value)
    {
      MsgPackTests.RoundTripTest<MpInt, int>(value, 5, MsgPackTypeId.MpInt);
    }

    [TestCase(((uint)ushort.MaxValue) + 1)]
    [TestCase((uint)int.MaxValue)]
    [TestCase(uint.MaxValue)]
    public void RoundTripUInt32(uint value)
    {
      MsgPackTests.RoundTripTest<MpInt, uint>(value, 5, MsgPackTypeId.MpUInt);
    }

    [TestCase(((long)int.MinValue) - 1)]
    [TestCase(((long)int.MinValue) - 1000)]
    [TestCase(long.MinValue)]
    public void RoundTripInt64(long value)
    {
      MsgPackTests.RoundTripTest<MpInt, long>(value, 9, MsgPackTypeId.MpLong);
    }

    [TestCase(((ulong)uint.MaxValue) + 1)]
    [TestCase((ulong)long.MaxValue)]
    [TestCase(ulong.MaxValue)]
    public void RoundTripUInt64(ulong value)
    {
      MsgPackTests.RoundTripTest<MpInt, ulong>(value, 9, MsgPackTypeId.MpULong);
    }

    [TestCase(false, (sbyte)50, 1, MsgPackTypeId.MpBytePart)]
    [TestCase(false, (int)128, 2, MsgPackTypeId.MpUByte)]
    [TestCase(false, (short)129, 2, MsgPackTypeId.MpUByte)]
    [TestCase(false, (int)129, 2, MsgPackTypeId.MpUByte)]
    [TestCase(false, (long)129, 2, MsgPackTypeId.MpUByte)]
    [TestCase(false, (int)1234, 3, MsgPackTypeId.MpUShort)]
    [TestCase(false, short.MaxValue, 3, MsgPackTypeId.MpUShort)]
    [TestCase(false, int.MaxValue, 5, MsgPackTypeId.MpUInt)]
    [TestCase(false, long.MaxValue, 9, MsgPackTypeId.MpULong)]

    [TestCase(true, (sbyte)50, 2, MsgPackTypeId.MpSByte)]
    [TestCase(true, (int)128, 5, MsgPackTypeId.MpInt)]
    [TestCase(true, (short)129, 3, MsgPackTypeId.MpShort)]
    [TestCase(true, (int)129, 5, MsgPackTypeId.MpInt)]
    [TestCase(true, (long)129, 9, MsgPackTypeId.MpLong)]
    [TestCase(true, (int)1234, 5, MsgPackTypeId.MpInt)]
    [TestCase(true, short.MaxValue, 3, MsgPackTypeId.MpShort)]
    [TestCase(true, int.MaxValue, 5, MsgPackTypeId.MpInt)]
    [TestCase(true, long.MaxValue, 9, MsgPackTypeId.MpLong)]
    public void SignedToUnsigned<T>(bool PreserveType, T value, int expectedLength, MsgPackTypeId expectedType)
    {
      if (PreserveType) MsgPackTests.DynamicallyCompactValue = false;
      try
      {
        MsgPackTests.RoundTripTest<MpInt, T>(value, expectedLength, expectedType);
      }
      finally
      {
        MsgPackTests.DynamicallyCompactValue = true;
      }
    }

    [TestCase((ulong)1, 1, MsgPackTypeId.MpBytePart)]
    [TestCase((ulong)127, 1, MsgPackTypeId.MpBytePart)]
    [TestCase((ulong)128, 2, MsgPackTypeId.MpUByte)]
    [TestCase((ulong)255, 2, MsgPackTypeId.MpUByte)]
    [TestCase((ulong)256, 3, MsgPackTypeId.MpUShort)]
    [TestCase((ulong)ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]
    [TestCase((ulong)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]
    [TestCase((ulong)uint.MaxValue, 5, MsgPackTypeId.MpUInt)]
    [TestCase(((ulong)uint.MaxValue) + 1, 9, MsgPackTypeId.MpULong)]
    [TestCase(ulong.MaxValue - 1, 9, MsgPackTypeId.MpULong)]

    [TestCase((uint)1, 1, MsgPackTypeId.MpBytePart)]
    [TestCase((uint)127, 1, MsgPackTypeId.MpBytePart)]
    [TestCase((uint)128, 2, MsgPackTypeId.MpUByte)]
    [TestCase((uint)255, 2, MsgPackTypeId.MpUByte)]
    [TestCase((uint)256, 3, MsgPackTypeId.MpUShort)]
    [TestCase((uint)ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]
    [TestCase((uint)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]
    [TestCase(uint.MaxValue, 5, MsgPackTypeId.MpUInt)]

    [TestCase((ushort)1, 1, MsgPackTypeId.MpBytePart)]
    [TestCase((ushort)127, 1, MsgPackTypeId.MpBytePart)]
    [TestCase((ushort)128, 2, MsgPackTypeId.MpUByte)]
    [TestCase((ushort)255, 2, MsgPackTypeId.MpUByte)]
    [TestCase((ushort)256, 3, MsgPackTypeId.MpUShort)]
    [TestCase(ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]

    [TestCase((byte)1, 1, MsgPackTypeId.MpBytePart)]
    [TestCase((byte)127, 1, MsgPackTypeId.MpBytePart)]
    [TestCase((byte)128, 2, MsgPackTypeId.MpUByte)]
    [TestCase((byte)255, 2, MsgPackTypeId.MpUByte)]

    [TestCase((long)-1, 1, MsgPackTypeId.MpSBytePart)]
    [TestCase((long)-31, 1, MsgPackTypeId.MpSBytePart)]
    [TestCase((long)-32, 2, MsgPackTypeId.MpSByte)]
    [TestCase((long)-128, 2, MsgPackTypeId.MpSByte)]
    [TestCase((long)-129, 3, MsgPackTypeId.MpShort)]
    [TestCase((long)short.MinValue, 3, MsgPackTypeId.MpShort)]
    [TestCase(((long)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]
    [TestCase((long)int.MinValue, 5, MsgPackTypeId.MpInt)]
    [TestCase(((long)int.MinValue) - 1, 9, MsgPackTypeId.MpLong)]
    [TestCase(long.MinValue, 9, MsgPackTypeId.MpLong)]

    [TestCase((int)-1, 1, MsgPackTypeId.MpSBytePart)]
    [TestCase((int)-31, 1, MsgPackTypeId.MpSBytePart)]
    [TestCase((int)-32, 2, MsgPackTypeId.MpSByte)]
    [TestCase((int)-128, 2, MsgPackTypeId.MpSByte)]
    [TestCase((int)-129, 3, MsgPackTypeId.MpShort)]
    [TestCase((int)short.MinValue, 3, MsgPackTypeId.MpShort)]
    [TestCase(((int)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]
    [TestCase(int.MinValue, 5, MsgPackTypeId.MpInt)]

    [TestCase((short)-1, 1, MsgPackTypeId.MpSBytePart)]
    [TestCase((short)-31, 1, MsgPackTypeId.MpSBytePart)]
    [TestCase((short)-32, 2, MsgPackTypeId.MpSByte)]
    [TestCase((short)-128, 2, MsgPackTypeId.MpSByte)]
    [TestCase((short)-129, 3, MsgPackTypeId.MpShort)]
    [TestCase(short.MinValue, 3, MsgPackTypeId.MpShort)]

    [TestCase((sbyte)-1, 1, MsgPackTypeId.MpSBytePart)]
    [TestCase((sbyte)-31, 1, MsgPackTypeId.MpSBytePart)]
    [TestCase((sbyte)-32, 2, MsgPackTypeId.MpSByte)]
    [TestCase((sbyte)-128, 2, MsgPackTypeId.MpSByte)]

    public void AutoCompactTest<T>(T value, int expectedLength, MsgPackTypeId expectedType)
    {
      MsgPackTests.RoundTripTest<MpInt, T>(value, expectedLength, expectedType);
    }

    [TestCase((ulong)1, 9, MsgPackTypeId.MpULong)]
    [TestCase((ulong)127, 9, MsgPackTypeId.MpULong)]
    [TestCase((ulong)128, 9, MsgPackTypeId.MpULong)]
    [TestCase((ulong)255, 9, MsgPackTypeId.MpULong)]
    [TestCase((ulong)256, 9, MsgPackTypeId.MpULong)]
    [TestCase((ulong)ushort.MaxValue, 9, MsgPackTypeId.MpULong)]
    [TestCase((ulong)ushort.MaxValue + 1, 9, MsgPackTypeId.MpULong)]
    [TestCase((ulong)uint.MaxValue, 9, MsgPackTypeId.MpULong)]
    [TestCase(((ulong)uint.MaxValue) + 1, 9, MsgPackTypeId.MpULong)]
    [TestCase(ulong.MaxValue - 1, 9, MsgPackTypeId.MpULong)]

    [TestCase((uint)1, 5, MsgPackTypeId.MpUInt)]
    [TestCase((uint)127, 5, MsgPackTypeId.MpUInt)]
    [TestCase((uint)128, 5, MsgPackTypeId.MpUInt)]
    [TestCase((uint)255, 5, MsgPackTypeId.MpUInt)]
    [TestCase((uint)256, 5, MsgPackTypeId.MpUInt)]
    [TestCase((uint)ushort.MaxValue, 5, MsgPackTypeId.MpUInt)]
    [TestCase((uint)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]
    [TestCase(uint.MaxValue, 5, MsgPackTypeId.MpUInt)]

    [TestCase((ushort)1, 3, MsgPackTypeId.MpUShort)]
    [TestCase((ushort)127, 3, MsgPackTypeId.MpUShort)]
    [TestCase((ushort)128, 3, MsgPackTypeId.MpUShort)]
    [TestCase((ushort)255, 3, MsgPackTypeId.MpUShort)]
    [TestCase((ushort)256, 3, MsgPackTypeId.MpUShort)]
    [TestCase(ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]

    [TestCase((byte)128, 2, MsgPackTypeId.MpUByte)]
    [TestCase((byte)255, 2, MsgPackTypeId.MpUByte)]

    [TestCase((long)-1, 9, MsgPackTypeId.MpLong)]
    [TestCase((long)-31, 9, MsgPackTypeId.MpLong)]
    [TestCase((long)-32, 9, MsgPackTypeId.MpLong)]
    [TestCase((long)-128, 9, MsgPackTypeId.MpLong)]
    [TestCase((long)-129, 9, MsgPackTypeId.MpLong)]
    [TestCase((long)short.MinValue, 9, MsgPackTypeId.MpLong)]
    [TestCase(((long)short.MinValue) - 1, 9, MsgPackTypeId.MpLong)]
    [TestCase((long)int.MinValue, 9, MsgPackTypeId.MpLong)]
    [TestCase(((long)int.MinValue) - 1, 9, MsgPackTypeId.MpLong)]
    [TestCase(long.MinValue, 9, MsgPackTypeId.MpLong)]

    [TestCase((int)-1, 5, MsgPackTypeId.MpInt)]
    [TestCase((int)-31, 5, MsgPackTypeId.MpInt)]
    [TestCase((int)-32, 5, MsgPackTypeId.MpInt)]
    [TestCase((int)-128, 5, MsgPackTypeId.MpInt)]
    [TestCase((int)-129, 5, MsgPackTypeId.MpInt)]
    [TestCase((int)short.MinValue, 5, MsgPackTypeId.MpInt)]
    [TestCase(((int)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]
    [TestCase(int.MinValue, 5, MsgPackTypeId.MpInt)]

    [TestCase((short)-1, 3, MsgPackTypeId.MpShort)]
    [TestCase((short)-31, 3, MsgPackTypeId.MpShort)]
    [TestCase((short)-32, 3, MsgPackTypeId.MpShort)]
    [TestCase((short)-128, 3, MsgPackTypeId.MpShort)]
    [TestCase((short)-129, 3, MsgPackTypeId.MpShort)]
    [TestCase(short.MinValue, 3, MsgPackTypeId.MpShort)]

    [TestCase((sbyte)-32, 2, MsgPackTypeId.MpSByte)]
    [TestCase((sbyte)-128, 2, MsgPackTypeId.MpSByte)]

    public void PreserveTypeTest<T>(T value, int expectedLength, MsgPackTypeId expectedType)
    {
      MsgPackTests.DynamicallyCompactValue = false;
      try
      {
        MsgPackTests.RoundTripTest<MpInt, T>(value, expectedLength, expectedType);
      }
      finally
      {
        MsgPackTests.DynamicallyCompactValue = true;
      }
    }

    #region enum tests

    [TestCase(regularInt32.First, MsgPackTypeId.MpUInt)]
    [TestCase(aByteEnum.First, MsgPackTypeId.MpBytePart)]
    [TestCase(aShortEnum.First, MsgPackTypeId.MpUShort)]
    public void EnumTest<T>(T enumDef, MsgPackTypeId expectedType)
    {
      var vals = Enum.GetValues(typeof(T));
      for (int t = vals.Length - 1; t >= 0; t--)
      {
        MsgPackTests.RoundTripTest<MpInt, T>((T)vals.GetValue(t), -1, expectedType);
      }
    }

    public enum regularInt32
    {
      First = ushort.MaxValue + 1,
      Seccond = ushort.MaxValue + 2,
      Last = ushort.MaxValue + 3
    }

    public enum aByteEnum : byte
    {
      First = 253,
      Seccond = 254,
      Last = 255
    }

    public enum aShortEnum : short
    {
      First = 256,
      Seccond = 257,
      Last = 258
    }

    #endregion

  }
}
