using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpBinTest
  {

    [DataTestMethod]
    [DataRow(0, 2, MsgPackTypeId.MpBin8)]
    [DataRow(1, 3, MsgPackTypeId.MpBin8)]
    [DataRow(32, 34, MsgPackTypeId.MpBin8)]
    [DataRow(255, 257, MsgPackTypeId.MpBin8)]
    [DataRow(256, 259, MsgPackTypeId.MpBin16)]
    [DataRow(ushort.MaxValue, ushort.MaxValue + 3, MsgPackTypeId.MpBin16)]
    [DataRow(ushort.MaxValue + 1, ushort.MaxValue + 6, MsgPackTypeId.MpBin32)]
    // [DataRow(0x7FEFFFF9, 0x7FEFFFF9 + 6, MsgPackTypeId.MpBin32)] // Out of memory on my machine
    public void BinaryLengths(int length, int expectedBytes, MsgPackTypeId expedctedType)
    {
      Random rnd = new Random();
      byte[] test = new byte[length];
      rnd.NextBytes(test);
      MsgPackTests.RoundTripTest<MpBin, byte[]>(test, expectedBytes, expedctedType);
    }
  }
}
