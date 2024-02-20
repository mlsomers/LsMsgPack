
using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpIntTest
  {
    [DataTestMethod]
    [DataRow((byte)0)]
    [DataRow((byte)1)]
    [DataRow((byte)65)]
    [DataRow((byte)127)]
    public void RoundTripPositiveFixnum(byte value)
    {
      MsgPackTests.RoundTripTest<MpInt, byte>(value, 1, MsgPackTypeId.MpBytePart);
    }

    [DataTestMethod]
    [DataRow((sbyte)-1)]
    [DataRow((sbyte)-15)]
    [DataRow((sbyte)-31)]
    public void RoundTripNegativeFixnum(sbyte value)
    {
      MsgPackTests.RoundTripTest<MpInt, sbyte>(value, 1, MsgPackTypeId.MpBytePart);
    }

    [DataTestMethod]
    [DataRow((byte)128)]
    [DataRow((byte)200)]
    [DataRow((byte)255)]
    public void RoundTripUint8(byte value)
    {
      MsgPackTests.RoundTripTest<MpInt, byte>(value, 2, MsgPackTypeId.MpUByte);
    }

    [DataTestMethod]
    [DataRow((sbyte)-32)]
    [DataRow((sbyte)-100)]
    [DataRow((sbyte)-128)]
    public void RoundTripInt8(sbyte value)
    {
      MsgPackTests.RoundTripTest<MpInt, sbyte>(value, 2, MsgPackTypeId.MpSByte);
    }

    [DataTestMethod]
    [DataRow((short)-129)]
    [DataRow((short)-1000)]
    [DataRow(short.MinValue)]
    public void RoundTripInt16(short value)
    {
      MsgPackTests.RoundTripTest<MpInt, short>(value, 3, MsgPackTypeId.MpShort);
    }

    [DataTestMethod]
    [DataRow((ushort)256)]
    [DataRow((ushort)1000)]
    [DataRow(ushort.MaxValue)]
    public void RoundTripUInt16(ushort value)
    {
      MsgPackTests.RoundTripTest<MpInt, ushort>(value, 3, MsgPackTypeId.MpUShort);
    }

    [DataTestMethod]
    [DataRow(short.MinValue - 1)]
    [DataRow(short.MinValue - 1000)]
    [DataRow(int.MinValue)]
    public void RoundTripInt32(int value)
    {
      MsgPackTests.RoundTripTest<MpInt, int>(value, 5, MsgPackTypeId.MpInt);
    }

    [DataTestMethod]
    [DataRow(((uint)ushort.MaxValue) + 1)]
    [DataRow((uint)int.MaxValue)]
    [DataRow(uint.MaxValue)]
    public void RoundTripUInt32(uint value)
    {
      MsgPackTests.RoundTripTest<MpInt, uint>(value, 5, MsgPackTypeId.MpUInt);
    }

    [DataTestMethod]
    [DataRow(((long)int.MinValue) - 1)]
    [DataRow(((long)int.MinValue) - 1000)]
    [DataRow(long.MinValue)]
    public void RoundTripInt64(long value)
    {
      MsgPackTests.RoundTripTest<MpInt, long>(value, 9, MsgPackTypeId.MpLong);
    }

    [DataTestMethod]
    [DataRow(((ulong)uint.MaxValue) + 1)]
    [DataRow((ulong)long.MaxValue)]
    [DataRow(ulong.MaxValue)]
    public void RoundTripUInt64(ulong value)
    {
      MsgPackTests.RoundTripTest<MpInt, ulong>(value, 9, MsgPackTypeId.MpULong);
    }

    [DataTestMethod]
    [DataRow(false, (sbyte)50, 1, MsgPackTypeId.MpBytePart)]
    [DataRow(false, (int)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow(false, (short)129, 2, MsgPackTypeId.MpUByte)]
    [DataRow(false, (int)129, 2, MsgPackTypeId.MpUByte)]
    [DataRow(false, (long)129, 2, MsgPackTypeId.MpUByte)]
    [DataRow(false, (int)1234, 3, MsgPackTypeId.MpUShort)]
    [DataRow(false, short.MaxValue, 3, MsgPackTypeId.MpUShort)]
    [DataRow(false, int.MaxValue, 5, MsgPackTypeId.MpUInt)]
    [DataRow(false, long.MaxValue, 9, MsgPackTypeId.MpULong)]

    [DataRow(true, (sbyte)50, 2, MsgPackTypeId.MpSByte)]
    [DataRow(true, (int)128, 5, MsgPackTypeId.MpInt)]
    [DataRow(true, (short)129, 3, MsgPackTypeId.MpShort)]
    [DataRow(true, (int)129, 5, MsgPackTypeId.MpInt)]
    [DataRow(true, (long)129, 9, MsgPackTypeId.MpLong)]
    [DataRow(true, (int)1234, 5, MsgPackTypeId.MpInt)]
    [DataRow(true, short.MaxValue, 3, MsgPackTypeId.MpShort)]
    [DataRow(true, int.MaxValue, 5, MsgPackTypeId.MpInt)]
    [DataRow(true, long.MaxValue, 9, MsgPackTypeId.MpLong)]
    public void SignedToUnsigned<T>(bool PreserveType, T value, int expectedLength, MsgPackTypeId expectedType)
    {
      MsgPackTests.RoundTripTest<MpInt, T>(value, expectedLength, expectedType);
    }

    [DataTestMethod]
    [DataRow((ulong)1, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((ulong)127, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((ulong)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow((ulong)255, 2, MsgPackTypeId.MpUByte)]
    [DataRow((ulong)256, 3, MsgPackTypeId.MpUShort)]
    [DataRow((ulong)ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]
    [DataRow((ulong)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]
    [DataRow((ulong)uint.MaxValue, 5, MsgPackTypeId.MpUInt)]
    [DataRow(((ulong)uint.MaxValue) + 1, 9, MsgPackTypeId.MpULong)]
    [DataRow(ulong.MaxValue - 1, 9, MsgPackTypeId.MpULong)]

    [DataRow((uint)1, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((uint)127, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((uint)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow((uint)255, 2, MsgPackTypeId.MpUByte)]
    [DataRow((uint)256, 3, MsgPackTypeId.MpUShort)]
    [DataRow((uint)ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]
    [DataRow((uint)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]
    [DataRow(uint.MaxValue, 5, MsgPackTypeId.MpUInt)]

    [DataRow((ushort)1, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((ushort)127, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((ushort)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow((ushort)255, 2, MsgPackTypeId.MpUByte)]
    [DataRow((ushort)256, 3, MsgPackTypeId.MpUShort)]
    [DataRow(ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]

    [DataRow((byte)1, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((byte)127, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((byte)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow((byte)255, 2, MsgPackTypeId.MpUByte)]

    [DataRow((long)-1, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((long)-31, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((long)-32, 2, MsgPackTypeId.MpSByte)]
    [DataRow((long)-128, 2, MsgPackTypeId.MpSByte)]
    [DataRow((long)-129, 3, MsgPackTypeId.MpShort)]
    [DataRow((long)short.MinValue, 3, MsgPackTypeId.MpShort)]
    [DataRow(((long)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]
    [DataRow((long)int.MinValue, 5, MsgPackTypeId.MpInt)]
    [DataRow(((long)int.MinValue) - 1, 9, MsgPackTypeId.MpLong)]
    [DataRow(long.MinValue, 9, MsgPackTypeId.MpLong)]

    [DataRow((int)-1, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((int)-31, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((int)-32, 2, MsgPackTypeId.MpSByte)]
    [DataRow((int)-128, 2, MsgPackTypeId.MpSByte)]
    [DataRow((int)-129, 3, MsgPackTypeId.MpShort)]
    [DataRow((int)short.MinValue, 3, MsgPackTypeId.MpShort)]
    [DataRow(((int)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]
    [DataRow(int.MinValue, 5, MsgPackTypeId.MpInt)]

    [DataRow((short)-1, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((short)-31, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((short)-32, 2, MsgPackTypeId.MpSByte)]
    [DataRow((short)-128, 2, MsgPackTypeId.MpSByte)]
    [DataRow((short)-129, 3, MsgPackTypeId.MpShort)]
    [DataRow(short.MinValue, 3, MsgPackTypeId.MpShort)]

    [DataRow((sbyte)-1, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((sbyte)-31, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((sbyte)-32, 2, MsgPackTypeId.MpSByte)]
    [DataRow((sbyte)-128, 2, MsgPackTypeId.MpSByte)]

    public void AutoCompactTest<T>(T value, int expectedLength, MsgPackTypeId expectedType)
    {
      MsgPackTests.RoundTripTest<MpInt, T>(value, expectedLength, expectedType);
    }

    #region enum tests

    [DataTestMethod]
    [DataRow(regularInt32.First, MsgPackTypeId.MpUInt)]
    [DataRow(aByteEnum.First, MsgPackTypeId.MpBytePart)]
    [DataRow(aShortEnum.First, MsgPackTypeId.MpUShort)]
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
