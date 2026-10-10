using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpExtTest
  {
#if KEEPTRACK
    /// <summary>
    /// The data ends in the content of an extension: the explorers show the item that was read, its text threw a NullReferenceException.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CutOffInTheContent_TextSaysSo(bool continueOnError)
    {
      MsgPackSettings settings = new MsgPackSettings() { PreservePackages = true, ContinueProcessingOnBreakingError = continueOnError };
      MsgPackItem item = MsgPackItem.Unpack(new System.IO.MemoryStream(new byte[] { 0xC7, 0x05, 0x01, 0xAA }), settings); // ext 8 of 5 bytes, 1 there
      MpError error = item as MpError;
      Assert.IsNotNull(error, item?.GetType().Name);
      MpExt partial = error.PartialItem as MpExt;
      Assert.IsNotNull(partial, error.PartialItem?.GetType().Name);
      StringAssert.Contains(partial.ToString(), "the data ended before its content");
      Assert.AreEqual(0, partial.Count);
    }
#endif

    [TestMethod]
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
      MsgPackTests.RoundTripTest<MpExt, byte[]>(test, expectedBytes, expedctedType, true, 124);
    }

  }
}
