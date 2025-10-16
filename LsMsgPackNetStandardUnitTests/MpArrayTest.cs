using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpArrayTest
  {

    private static Random rnd = new Random();

    [TestMethod]
    [DataRow(0, 1, MsgPackTypeId.MpArray4)]
    [DataRow(1, 2, MsgPackTypeId.MpArray4)]
    [DataRow(15, 16, MsgPackTypeId.MpArray4)]
    [DataRow(200, 203, MsgPackTypeId.MpArray16)]
    [DataRow(256, 259, MsgPackTypeId.MpArray16)]
    [DataRow(ushort.MaxValue, ushort.MaxValue + 3, MsgPackTypeId.MpArray16)]
    [DataRow(ushort.MaxValue + 1, ushort.MaxValue + 6, MsgPackTypeId.MpArray32)]
    // [DataRow(0x7FEFFFF9, 0x7FEFFFF9 + 6, MsgPackTypeId.MpArray32)] // Out of memory on my machine
    public void ArrayLengths(int length, int expectedBytes, MsgPackTypeId expedctedType)
    {
      object[] test = new object[length];
      int additionalBytes = FillArrayWithRandomNumbers(test);
      additionalBytes -= test.Length;
      MsgPackItem item = MsgPackTests.RoundTripTest<MpArray, object[]>(test, expectedBytes + additionalBytes, expedctedType);

      object[] ret = item.GetTypedValue<object[]>();

      Assert.HasCount(length, ret, string.Concat("Expected ", length, " items but got ", ret.Length, " items in the array."));
      for (int t = ret.Length - 1; t >= 0; t--)
      {
        Assert.IsTrue(MsgPackTests.AreEqualish(test[t], ret[t]), string.Concat("Expected ", test[t], " but got ", ret[t], " at index ", t));
      }
    }

    private int FillArrayWithRandomNumbers(object[] test)
    {
      int addSize = 0;
      for (int t = test.Length - 1; t >= 0; t--)
      {
        int newNr = rnd.Next();
        addSize += new MpInt() { Value = newNr }.ToBytes().Length;
        test[t] = newNr;
      }
      return addSize;
    }

    [TestMethod]
    [DataRow(false, 40)]
    [DataRow(true, 47)]
    public void AssortedMix(bool preserveTypes, int expectedLength)
    {
      object[] items = new object[] {
        true,
        false,
        null,
        128,
        -30,
        5.2d,
        900.1f,
        "Hallo!",
        new byte[]{1,2,3 },
        new object[] { true,null,"yes"}
      };

      MsgPackItem item = MsgPackTests.RoundTripTest<MpArray, object[]>(items, expectedLength, MsgPackTypeId.MpArray4, !preserveTypes);

      object[] ret = item.GetTypedValue<object[]>();

      Assert.HasCount(items.Length, ret, string.Concat("Expected ", items.Length, " items but got ", ret.Length, " items in the array."));
      for (int t = ret.Length - 1; t >= 0; t--)
      {
        if (preserveTypes && t != 2) Assert.IsTrue(items[t].GetType() == ret[t].GetType(), string.Concat("Expected type ", items[t].GetType(), " items but got ", ret[t].GetType(), "."));
        Assert.IsTrue(MsgPackTests.AreEqualish(items[t], ret[t]), string.Concat("Expected ", items[t], " but got ", ret[t], " at index ", t));
      }
    }
  }
}
