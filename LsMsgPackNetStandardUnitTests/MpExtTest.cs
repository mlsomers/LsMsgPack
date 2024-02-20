using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpExtTest
  {
    [DataTestMethod]
    [DataRow(0, 3, MsgPackTypeId.MpExt8)]
    [DataRow(1, 3, MsgPackTypeId.MpFExt1)]
    [DataRow(2, 4, MsgPackTypeId.MpFExt2)]
    [DataRow(3, 6, MsgPackTypeId.MpExt8)]
    [DataRow(4, 6, MsgPackTypeId.MpFExt4)]
    [DataRow(5, 8, MsgPackTypeId.MpExt8)]
    [DataRow(7, 10, MsgPackTypeId.MpExt8)]
    [DataRow(8, 10, MsgPackTypeId.MpFExt8)]
    [DataRow(9, 12, MsgPackTypeId.MpExt8)]
    [DataRow(15, 18, MsgPackTypeId.MpExt8)]
    [DataRow(16, 18, MsgPackTypeId.MpExt16)]
    [DataRow(17, 20, MsgPackTypeId.MpExt8)]
    [DataRow(255, 258, MsgPackTypeId.MpExt8)]
    [DataRow(256, 260, MsgPackTypeId.MpExt16)]
    [DataRow(ushort.MaxValue, ushort.MaxValue + 4, MsgPackTypeId.MpExt16)]
    [DataRow(ushort.MaxValue + 1, ushort.MaxValue + 7, MsgPackTypeId.MpExt32)]
    public void BinaryLengths(int length, int expectedBytes, MsgPackTypeId expedctedType)
    {
      Random rnd = new Random();
      byte[] test = new byte[length];
      rnd.NextBytes(test);
      if (test.Length > 0)
        test[0] = 150; // prevent using implemented extension!
      MsgPackTests.RoundTripTest<MpExt, byte[]>(test, expectedBytes, expedctedType, true, (sbyte)(rnd.Next(255) - 128));
    }

  }
}
