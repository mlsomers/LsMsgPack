using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpRootTests
  {
    [DataTestMethod]
    [DataRow(false, 11, 1, "hello")]
    [DataRow(true, 7, 1, "hello")]
    [DataRow(false, 13, new object[] { (byte)0, new object[] { (short)597, (short)492 }, new object[0], (byte)0, (byte)0, "\u0001" })]
    public void RoundTripTest(bool dynamicallyCompact, int expectedLength, params object[] items)
    {
      MpRoot root = MsgPackItem.PackMultiple(dynamicallyCompact, items);
      byte[] bytes = root.ToBytes();

      Assert.AreEqual(expectedLength, bytes.Length, string.Concat("Expected ", expectedLength, " serialized bytes items but got ", bytes.Length, " bytes."));

      MpRoot result = MsgPackItem.UnpackMultiple(bytes);

      Assert.AreEqual(items.Length, result.Count, string.Concat("Expected ", items.Length, " items but got ", result.Count, " items after round trip."));

      for (int t = 0; t < result.Count; t++)
      {
        object expected = items[t];
        object actual = result[t].Value;

        Assert.IsTrue(MsgPackTests.AreEqualish(expected, actual), "The returned value ", actual, " differs from the input value ", expected);
      }
    }
  }

}
