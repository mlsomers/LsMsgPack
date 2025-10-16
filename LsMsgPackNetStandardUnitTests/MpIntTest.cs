
using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using System;

namespace LsMsgPackUnitTests
{
  [TestFixture]
  [TestClass]
  public class MpIntTest
  {
    [TestMethod]
    [DataRow((byte)0)]   
    [DataRow((byte)1)]   
    [DataRow((byte)65)]  
    [DataRow((byte)127)] 
    public void RoundTripPositiveFixnum(byte value)
    {
      MsgPackTests.RoundTripTest<MpInt, byte>(value, 1, MsgPackTypeId.MpBytePart);
    }

    [TestMethod]
    [DataRow((sbyte)-1)]   
    [DataRow((sbyte)-15)]  
    [DataRow((sbyte)-31)]  
    public void RoundTripNegativeFixnum(sbyte value)
    {
      MsgPackTests.RoundTripTest<MpInt, sbyte>(value, 1, MsgPackTypeId.MpBytePart);
    }

    [TestMethod]
    [DataRow((byte)128)]  
    [DataRow((byte)200)]  
    [DataRow((byte)255)]  
    public void RoundTripUint8(byte value)
    {
      MsgPackTests.RoundTripTest<MpInt, byte>(value, 2, MsgPackTypeId.MpUByte);
    }

    [TestMethod]
    [DataRow((sbyte)-32)]   
    [DataRow((sbyte)-100)]  
    [DataRow((sbyte)-128)]  
    public void RoundTripInt8(sbyte value)
    {
      MsgPackTests.RoundTripTest<MpInt, sbyte>(value, 2, MsgPackTypeId.MpSByte);
    }

    [TestMethod]
    [DataRow((short)-129)]    
    [DataRow((short)-1000)]   
    [DataRow(short.MinValue)] 
    public void RoundTripInt16(short value)
    {
      MsgPackTests.RoundTripTest<MpInt, short>(value, 3, MsgPackTypeId.MpShort);
    }

    [TestMethod]
    [DataRow((ushort)256)]
    [DataRow((ushort)1000)]
    [DataRow(ushort.MaxValue)]
    public void RoundTripUInt16(ushort value)
    {
      MsgPackTests.RoundTripTest<MpInt, ushort>(value, 3, MsgPackTypeId.MpUShort);
    }

    [TestMethod]
    [DataRow(short.MinValue - 1)]
    [DataRow(short.MinValue - 1000)]
    [DataRow(int.MinValue)]
    public void RoundTripInt32(int value)
    {
      MsgPackTests.RoundTripTest<MpInt, int>(value, 5, MsgPackTypeId.MpInt);
    }

    [TestMethod]
    [DataRow(((uint)ushort.MaxValue) + 1)]
    [DataRow((uint)int.MaxValue)]
    [DataRow(uint.MaxValue)]
    public void RoundTripUInt32(uint value)
    {
      MsgPackTests.RoundTripTest<MpInt, uint>(value, 5, MsgPackTypeId.MpUInt);
    }

    [TestMethod]
    [DataRow(((long)int.MinValue) - 1)]
    [DataRow(((long)int.MinValue) - 1000)]
    [DataRow(long.MinValue)]
    public void RoundTripInt64(long value)
    {
      MsgPackTests.RoundTripTest<MpInt, long>(value, 9, MsgPackTypeId.MpLong);
    }

    [TestMethod]
    [DataRow(((ulong)uint.MaxValue) + 1)]
    [DataRow((ulong)long.MaxValue)]
    [DataRow(ulong.MaxValue)]
    public void RoundTripUInt64(ulong value)
    {
      MsgPackTests.RoundTripTest<MpInt, ulong>(value, 9, MsgPackTypeId.MpULong);
    }

    // Since these tests are ignored by MsTest (left attributes) due to the Generic method, we need to run them in NUnit (right attributes)

    [TestMethod]
    [DataRow(false, (sbyte)50, 1, MsgPackTypeId.MpBytePart)]      [TestCase(false, (sbyte)50, 1, MsgPackTypeId.MpBytePart)]
    [DataRow(false, (int)128, 2, MsgPackTypeId.MpUByte)]          [TestCase(false, (int)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow(false, (short)129, 2, MsgPackTypeId.MpUByte)]        [TestCase(false, (short)129, 2, MsgPackTypeId.MpUByte)]
    [DataRow(false, (int)129, 2, MsgPackTypeId.MpUByte)]          [TestCase(false, (int)129, 2, MsgPackTypeId.MpUByte)]
    [DataRow(false, (long)129, 2, MsgPackTypeId.MpUByte)]         [TestCase(false, (long)129, 2, MsgPackTypeId.MpUByte)]
    [DataRow(false, (int)1234, 3, MsgPackTypeId.MpUShort)]        [TestCase(false, (int)1234, 3, MsgPackTypeId.MpUShort)]
    [DataRow(false, short.MaxValue, 3, MsgPackTypeId.MpUShort)]   [TestCase(false, short.MaxValue, 3, MsgPackTypeId.MpUShort)]
    [DataRow(false, int.MaxValue, 5, MsgPackTypeId.MpUInt)]       [TestCase(false, int.MaxValue, 5, MsgPackTypeId.MpUInt)]
    [DataRow(false, long.MaxValue, 9, MsgPackTypeId.MpULong)]     [TestCase(false, long.MaxValue, 9, MsgPackTypeId.MpULong)]

    [DataRow(true, (sbyte)50, 2, MsgPackTypeId.MpSByte)]          [TestCase(true, (sbyte)50, 2, MsgPackTypeId.MpSByte)]
    [DataRow(true, (int)128, 5, MsgPackTypeId.MpInt)]             [TestCase(true, (int)128, 5, MsgPackTypeId.MpInt)]
    [DataRow(true, (short)129, 3, MsgPackTypeId.MpShort)]         [TestCase(true, (short)129, 3, MsgPackTypeId.MpShort)]
    [DataRow(true, (int)129, 5, MsgPackTypeId.MpInt)]             [TestCase(true, (int)129, 5, MsgPackTypeId.MpInt)]
    [DataRow(true, (long)129, 9, MsgPackTypeId.MpLong)]           [TestCase(true, (long)129, 9, MsgPackTypeId.MpLong)]
    [DataRow(true, (int)1234, 5, MsgPackTypeId.MpInt)]            [TestCase(true, (int)1234, 5, MsgPackTypeId.MpInt)]
    [DataRow(true, short.MaxValue, 3, MsgPackTypeId.MpShort)]     [TestCase(true, short.MaxValue, 3, MsgPackTypeId.MpShort)]
    [DataRow(true, int.MaxValue, 5, MsgPackTypeId.MpInt)]         [TestCase(true, int.MaxValue, 5, MsgPackTypeId.MpInt)]
    [DataRow(true, long.MaxValue, 9, MsgPackTypeId.MpLong)]       [TestCase(true, long.MaxValue, 9, MsgPackTypeId.MpLong)]
    public void SignedToUnsigned<T>(bool PreserveType, T value, int expectedLength, MsgPackTypeId expectedType)
    {
      MsgPackTests.RoundTripTest<MpInt, T>(value, expectedLength, expectedType, !PreserveType);
    }

    // Since these tests are ignored by MsTest (left attributes) due to the Generic method, we need to run them in NUnit (right attributes)

    [TestMethod]
    [DataRow((ulong)1, 1, MsgPackTypeId.MpBytePart)]                    [TestCase((ulong)1, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((ulong)127, 1, MsgPackTypeId.MpBytePart)]                  [TestCase((ulong)127, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((ulong)128, 2, MsgPackTypeId.MpUByte)]                     [TestCase((ulong)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow((ulong)255, 2, MsgPackTypeId.MpUByte)]                     [TestCase((ulong)255, 2, MsgPackTypeId.MpUByte)]
    [DataRow((ulong)256, 3, MsgPackTypeId.MpUShort)]                    [TestCase((ulong)256, 3, MsgPackTypeId.MpUShort)]
    [DataRow((ulong)ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]        [TestCase((ulong)ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]
    [DataRow((ulong)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]      [TestCase((ulong)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]
    [DataRow((ulong)uint.MaxValue, 5, MsgPackTypeId.MpUInt)]            [TestCase((ulong)uint.MaxValue, 5, MsgPackTypeId.MpUInt)]
    [DataRow(((ulong)uint.MaxValue) + 1, 9, MsgPackTypeId.MpULong)]     [TestCase(((ulong)uint.MaxValue) + 1, 9, MsgPackTypeId.MpULong)]
    [DataRow(ulong.MaxValue - 1, 9, MsgPackTypeId.MpULong)]             [TestCase(ulong.MaxValue - 1, 9, MsgPackTypeId.MpULong)]

    [DataRow((uint)1, 1, MsgPackTypeId.MpBytePart)]                     [TestCase((uint)1, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((uint)127, 1, MsgPackTypeId.MpBytePart)]                   [TestCase((uint)127, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((uint)128, 2, MsgPackTypeId.MpUByte)]                      [TestCase((uint)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow((uint)255, 2, MsgPackTypeId.MpUByte)]                      [TestCase((uint)255, 2, MsgPackTypeId.MpUByte)]
    [DataRow((uint)256, 3, MsgPackTypeId.MpUShort)]                     [TestCase((uint)256, 3, MsgPackTypeId.MpUShort)]
    [DataRow((uint)ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]         [TestCase((uint)ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]
    [DataRow((uint)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]       [TestCase((uint)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]
    [DataRow(uint.MaxValue, 5, MsgPackTypeId.MpUInt)]                   [TestCase(uint.MaxValue, 5, MsgPackTypeId.MpUInt)]

    [DataRow((ushort)1, 1, MsgPackTypeId.MpBytePart)]                   [TestCase((ushort)1, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((ushort)127, 1, MsgPackTypeId.MpBytePart)]                 [TestCase((ushort)127, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((ushort)128, 2, MsgPackTypeId.MpUByte)]                    [TestCase((ushort)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow((ushort)255, 2, MsgPackTypeId.MpUByte)]                    [TestCase((ushort)255, 2, MsgPackTypeId.MpUByte)]
    [DataRow((ushort)256, 3, MsgPackTypeId.MpUShort)]                   [TestCase((ushort)256, 3, MsgPackTypeId.MpUShort)]
    [DataRow(ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]               [TestCase(ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]

    [DataRow((byte)1, 1, MsgPackTypeId.MpBytePart)]                     [TestCase((byte)1, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((byte)127, 1, MsgPackTypeId.MpBytePart)]                   [TestCase((byte)127, 1, MsgPackTypeId.MpBytePart)]
    [DataRow((byte)128, 2, MsgPackTypeId.MpUByte)]                      [TestCase((byte)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow((byte)255, 2, MsgPackTypeId.MpUByte)]                      [TestCase((byte)255, 2, MsgPackTypeId.MpUByte)]

    [DataRow((long)-1, 1, MsgPackTypeId.MpSBytePart)]                   [TestCase((long)-1, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((long)-31, 1, MsgPackTypeId.MpSBytePart)]                  [TestCase((long)-31, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((long)-32, 2, MsgPackTypeId.MpSByte)]                      [TestCase((long)-32, 2, MsgPackTypeId.MpSByte)]
    [DataRow((long)-128, 2, MsgPackTypeId.MpSByte)]                     [TestCase((long)-128, 2, MsgPackTypeId.MpSByte)]
    [DataRow((long)-129, 3, MsgPackTypeId.MpShort)]                     [TestCase((long)-129, 3, MsgPackTypeId.MpShort)]
    [DataRow((long)short.MinValue, 3, MsgPackTypeId.MpShort)]           [TestCase((long)short.MinValue, 3, MsgPackTypeId.MpShort)]
    [DataRow(((long)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]       [TestCase(((long)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]
    [DataRow((long)int.MinValue, 5, MsgPackTypeId.MpInt)]               [TestCase((long)int.MinValue, 5, MsgPackTypeId.MpInt)]
    [DataRow(((long)int.MinValue) - 1, 9, MsgPackTypeId.MpLong)]        [TestCase(((long)int.MinValue) - 1, 9, MsgPackTypeId.MpLong)]
    [DataRow(long.MinValue, 9, MsgPackTypeId.MpLong)]                   [TestCase(long.MinValue, 9, MsgPackTypeId.MpLong)]

    [DataRow((int)-1, 1, MsgPackTypeId.MpSBytePart)]                    [TestCase((int)-1, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((int)-31, 1, MsgPackTypeId.MpSBytePart)]                   [TestCase((int)-31, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((int)-32, 2, MsgPackTypeId.MpSByte)]                       [TestCase((int)-32, 2, MsgPackTypeId.MpSByte)]
    [DataRow((int)-128, 2, MsgPackTypeId.MpSByte)]                      [TestCase((int)-128, 2, MsgPackTypeId.MpSByte)]
    [DataRow((int)-129, 3, MsgPackTypeId.MpShort)]                      [TestCase((int)-129, 3, MsgPackTypeId.MpShort)]
    [DataRow((int)short.MinValue, 3, MsgPackTypeId.MpShort)]            [TestCase((int)short.MinValue, 3, MsgPackTypeId.MpShort)]
    [DataRow(((int)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]        [TestCase(((int)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]
    [DataRow(int.MinValue, 5, MsgPackTypeId.MpInt)]                     [TestCase(int.MinValue, 5, MsgPackTypeId.MpInt)]

    [DataRow((short)-1, 1, MsgPackTypeId.MpSBytePart)]                  [TestCase((short)-1, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((short)-31, 1, MsgPackTypeId.MpSBytePart)]                 [TestCase((short)-31, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((short)-32, 2, MsgPackTypeId.MpSByte)]                     [TestCase((short)-32, 2, MsgPackTypeId.MpSByte)]
    [DataRow((short)-128, 2, MsgPackTypeId.MpSByte)]                    [TestCase((short)-128, 2, MsgPackTypeId.MpSByte)]
    [DataRow((short)-129, 3, MsgPackTypeId.MpShort)]                    [TestCase((short)-129, 3, MsgPackTypeId.MpShort)]
    [DataRow(short.MinValue, 3, MsgPackTypeId.MpShort)]                 [TestCase(short.MinValue, 3, MsgPackTypeId.MpShort)]

    [DataRow((sbyte)-1, 1, MsgPackTypeId.MpSBytePart)]                  [TestCase((sbyte)-1, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((sbyte)-31, 1, MsgPackTypeId.MpSBytePart)]                 [TestCase((sbyte)-31, 1, MsgPackTypeId.MpSBytePart)]
    [DataRow((sbyte)-32, 2, MsgPackTypeId.MpSByte)]                     [TestCase((sbyte)-32, 2, MsgPackTypeId.MpSByte)]
    [DataRow((sbyte)-128, 2, MsgPackTypeId.MpSByte)]                    [TestCase((sbyte)-128, 2, MsgPackTypeId.MpSByte)]
    public void AutoCompactTest<T>(T value, int expectedLength, MsgPackTypeId expectedType)
    {
      MsgPackTests.RoundTripTest<MpInt, T>(value, expectedLength, expectedType);
    }

    // Since these tests are ignored by MsTest (left attributes) due to the Generic method, we need to run them in NUnit (right attributes)

    [TestMethod]
    [DataRow((ulong)1, 9, MsgPackTypeId.MpULong)]                     [TestCase((ulong)1, 9, MsgPackTypeId.MpULong)]
    [DataRow((ulong)127, 9, MsgPackTypeId.MpULong)]                   [TestCase((ulong)127, 9, MsgPackTypeId.MpULong)]
    [DataRow((ulong)128, 9, MsgPackTypeId.MpULong)]                   [TestCase((ulong)128, 9, MsgPackTypeId.MpULong)]
    [DataRow((ulong)255, 9, MsgPackTypeId.MpULong)]                   [TestCase((ulong)255, 9, MsgPackTypeId.MpULong)]
    [DataRow((ulong)256, 9, MsgPackTypeId.MpULong)]                   [TestCase((ulong)256, 9, MsgPackTypeId.MpULong)]
    [DataRow((ulong)ushort.MaxValue, 9, MsgPackTypeId.MpULong)]       [TestCase((ulong)ushort.MaxValue, 9, MsgPackTypeId.MpULong)]
    [DataRow((ulong)ushort.MaxValue + 1, 9, MsgPackTypeId.MpULong)]   [TestCase((ulong)ushort.MaxValue + 1, 9, MsgPackTypeId.MpULong)]
    [DataRow((ulong)uint.MaxValue, 9, MsgPackTypeId.MpULong)]         [TestCase((ulong)uint.MaxValue, 9, MsgPackTypeId.MpULong)]
    [DataRow(((ulong)uint.MaxValue) + 1, 9, MsgPackTypeId.MpULong)]   [TestCase(((ulong)uint.MaxValue) + 1, 9, MsgPackTypeId.MpULong)]
    [DataRow(ulong.MaxValue - 1, 9, MsgPackTypeId.MpULong)]           [TestCase(ulong.MaxValue - 1, 9, MsgPackTypeId.MpULong)]

    [DataRow((uint)1, 5, MsgPackTypeId.MpUInt)]                       [TestCase((uint)1, 5, MsgPackTypeId.MpUInt)]
    [DataRow((uint)127, 5, MsgPackTypeId.MpUInt)]                     [TestCase((uint)127, 5, MsgPackTypeId.MpUInt)]
    [DataRow((uint)128, 5, MsgPackTypeId.MpUInt)]                     [TestCase((uint)128, 5, MsgPackTypeId.MpUInt)]
    [DataRow((uint)255, 5, MsgPackTypeId.MpUInt)]                     [TestCase((uint)255, 5, MsgPackTypeId.MpUInt)]
    [DataRow((uint)256, 5, MsgPackTypeId.MpUInt)]                     [TestCase((uint)256, 5, MsgPackTypeId.MpUInt)]
    [DataRow((uint)ushort.MaxValue, 5, MsgPackTypeId.MpUInt)]         [TestCase((uint)ushort.MaxValue, 5, MsgPackTypeId.MpUInt)]
    [DataRow((uint)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]     [TestCase((uint)ushort.MaxValue + 1, 5, MsgPackTypeId.MpUInt)]
    [DataRow(uint.MaxValue, 5, MsgPackTypeId.MpUInt)]                 [TestCase(uint.MaxValue, 5, MsgPackTypeId.MpUInt)]

    [DataRow((ushort)1, 3, MsgPackTypeId.MpUShort)]                   [TestCase((ushort)1, 3, MsgPackTypeId.MpUShort)]
    [DataRow((ushort)127, 3, MsgPackTypeId.MpUShort)]                 [TestCase((ushort)127, 3, MsgPackTypeId.MpUShort)]
    [DataRow((ushort)128, 3, MsgPackTypeId.MpUShort)]                 [TestCase((ushort)128, 3, MsgPackTypeId.MpUShort)]
    [DataRow((ushort)255, 3, MsgPackTypeId.MpUShort)]                 [TestCase((ushort)255, 3, MsgPackTypeId.MpUShort)]
    [DataRow((ushort)256, 3, MsgPackTypeId.MpUShort)]                 [TestCase((ushort)256, 3, MsgPackTypeId.MpUShort)]
    [DataRow(ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]             [TestCase(ushort.MaxValue, 3, MsgPackTypeId.MpUShort)]

    [DataRow((byte)128, 2, MsgPackTypeId.MpUByte)]                    [TestCase((byte)128, 2, MsgPackTypeId.MpUByte)]
    [DataRow((byte)255, 2, MsgPackTypeId.MpUByte)]                    [TestCase((byte)255, 2, MsgPackTypeId.MpUByte)]

    [DataRow((long)-1, 9, MsgPackTypeId.MpLong)]                      [TestCase((long)-1, 9, MsgPackTypeId.MpLong)]
    [DataRow((long)-31, 9, MsgPackTypeId.MpLong)]                     [TestCase((long)-31, 9, MsgPackTypeId.MpLong)]
    [DataRow((long)-32, 9, MsgPackTypeId.MpLong)]                     [TestCase((long)-32, 9, MsgPackTypeId.MpLong)]
    [DataRow((long)-128, 9, MsgPackTypeId.MpLong)]                    [TestCase((long)-128, 9, MsgPackTypeId.MpLong)]
    [DataRow((long)-129, 9, MsgPackTypeId.MpLong)]                    [TestCase((long)-129, 9, MsgPackTypeId.MpLong)]
    [DataRow((long)short.MinValue, 9, MsgPackTypeId.MpLong)]          [TestCase((long)short.MinValue, 9, MsgPackTypeId.MpLong)]
    [DataRow(((long)short.MinValue) - 1, 9, MsgPackTypeId.MpLong)]    [TestCase(((long)short.MinValue) - 1, 9, MsgPackTypeId.MpLong)]
    [DataRow((long)int.MinValue, 9, MsgPackTypeId.MpLong)]            [TestCase((long)int.MinValue, 9, MsgPackTypeId.MpLong)]
    [DataRow(((long)int.MinValue) - 1, 9, MsgPackTypeId.MpLong)]      [TestCase(((long)int.MinValue) - 1, 9, MsgPackTypeId.MpLong)]
    [DataRow(long.MinValue, 9, MsgPackTypeId.MpLong)]                 [TestCase(long.MinValue, 9, MsgPackTypeId.MpLong)]

    [DataRow((int)-1, 5, MsgPackTypeId.MpInt)]                        [TestCase((int)-1, 5, MsgPackTypeId.MpInt)]
    [DataRow((int)-31, 5, MsgPackTypeId.MpInt)]                       [TestCase((int)-31, 5, MsgPackTypeId.MpInt)]
    [DataRow((int)-32, 5, MsgPackTypeId.MpInt)]                       [TestCase((int)-32, 5, MsgPackTypeId.MpInt)]
    [DataRow((int)-128, 5, MsgPackTypeId.MpInt)]                      [TestCase((int)-128, 5, MsgPackTypeId.MpInt)]
    [DataRow((int)-129, 5, MsgPackTypeId.MpInt)]                      [TestCase((int)-129, 5, MsgPackTypeId.MpInt)]
    [DataRow((int)short.MinValue, 5, MsgPackTypeId.MpInt)]            [TestCase((int)short.MinValue, 5, MsgPackTypeId.MpInt)]
    [DataRow(((int)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]      [TestCase(((int)short.MinValue) - 1, 5, MsgPackTypeId.MpInt)]
    [DataRow(int.MinValue, 5, MsgPackTypeId.MpInt)]                   [TestCase(int.MinValue, 5, MsgPackTypeId.MpInt)]

    [DataRow((short)-1, 3, MsgPackTypeId.MpShort)]                    [TestCase((short)-1, 3, MsgPackTypeId.MpShort)]
    [DataRow((short)-31, 3, MsgPackTypeId.MpShort)]                   [TestCase((short)-31, 3, MsgPackTypeId.MpShort)]
    [DataRow((short)-32, 3, MsgPackTypeId.MpShort)]                   [TestCase((short)-32, 3, MsgPackTypeId.MpShort)]
    [DataRow((short)-128, 3, MsgPackTypeId.MpShort)]                  [TestCase((short)-128, 3, MsgPackTypeId.MpShort)]
    [DataRow((short)-129, 3, MsgPackTypeId.MpShort)]                  [TestCase((short)-129, 3, MsgPackTypeId.MpShort)]
    [DataRow(short.MinValue, 3, MsgPackTypeId.MpShort)]               [TestCase(short.MinValue, 3, MsgPackTypeId.MpShort)]

    [DataRow((sbyte)-32, 2, MsgPackTypeId.MpSByte)]                   [TestCase((sbyte)-32, 2, MsgPackTypeId.MpSByte)]
    [DataRow((sbyte)-128, 2, MsgPackTypeId.MpSByte)]                  [TestCase((sbyte)-128, 2, MsgPackTypeId.MpSByte)]
    public void PreserveTypeTest<T>(T value, int expectedLength, MsgPackTypeId expectedType)
    {
        MsgPackTests.RoundTripTest<MpInt, T>(value, expectedLength, expectedType, false);
    }

    #region enum tests

    // Since these tests are ignored by MsTest (left attributes) due to the Generic method, we need to run them in NUnit (right attributes)

    [TestMethod]
    [DataRow(regularInt32.First, MsgPackTypeId.MpUInt)]      [TestCase(regularInt32.First, MsgPackTypeId.MpUInt)]
    [DataRow(aByteEnum.First, MsgPackTypeId.MpBytePart)]     [TestCase(aByteEnum.First, MsgPackTypeId.MpBytePart)]
    [DataRow(aShortEnum.First, MsgPackTypeId.MpUShort)]      [TestCase(aShortEnum.First, MsgPackTypeId.MpUShort)]
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


