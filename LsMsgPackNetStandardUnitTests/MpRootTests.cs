using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LsMsgPackUnitTests
{
  [NUnit.Framework.TestFixture]
  [TestClass]
  public class MpRootTests
  {
    [TestMethod]
    [DataRow(false, 11, 1, "hello")]
    [DataRow(true, 7, 1, "hello")]

    // MsTest is converting everything to Int32 su we'll have to revert to NUnit to run this one...
    // [DataRow(false, 13, new object[] { (byte)0, new object[] { (short)597, (short)492 }, new object[0], (byte)0, (byte)0, "\u0001" })]
    [NUnit.Framework.TestCase(false, 13, new object[] { (byte)0, new object[] { (short)597, (short)492 }, new object[0], (byte)0, (byte)0, "\u0001" })]
    public void RoundTripTest(bool dynamicallyCompact, int expectedLength, params object[] items)
    {
      MpRoot root = MsgPackItem.PackMultiple(dynamicallyCompact, items);
      byte[] bytes = root.ToBytes();

      Assert.HasCount(expectedLength, bytes, string.Concat("Expected ", expectedLength, " serialized bytes items but got ", bytes.Length, " bytes."));

      MpRoot result = MsgPackItem.UnpackMultiple(bytes);

      Assert.AreEqual(items.Length, result.Count, string.Concat("Expected ", items.Length, " items but got ", result.Count, " items after round trip."));

      for (int t = 0; t < result.Count; t++)
      {
        object expected = items[t];
        object actual = result[t].Value;

        Assert.IsTrue(MsgPackTests.AreEqualish(expected, actual), string.Concat("The returned value ", actual, " differs from the input value ", expected));
      }
    }

    [TestMethod]
    public void LastItemOfOneByte()
    {
      // Two positive fixints, the last byte used to be left unread
      MpRoot result = MsgPackItem.UnpackMultiple(new byte[] { 0x01, 0x02 });
      Assert.AreEqual(2, result.Count);
      Assert.AreEqual(2, System.Convert.ToInt32(result[1].Value));

      Assert.AreEqual(1, MsgPackItem.UnpackMultiple(new byte[] { 0xC0 }).Count);
      Assert.AreEqual(0, MsgPackItem.UnpackMultiple(new byte[0]).Count);
    }
  }

}
