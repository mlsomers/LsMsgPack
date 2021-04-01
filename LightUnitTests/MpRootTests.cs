using LsMsgPack;
using NUnit.Framework;

namespace LsMsgPackUnitTests {
  [TestFixture]
  public class MpRootTests {

    [TestCase(7, 1, "hello")]
    [TestCase(13, new object[] { (byte)0, new object[] { (short)597, (short)492 }, new object[0], (byte)0, (byte)0, "\u0001" })]
    public void RoundTripTest(int expectedLength, params object[] items) {
      MpRoot root = MsgPackItem.PackMultiple(items);
      byte[] bytes = root.ToBytes();

      Assert.AreEqual(expectedLength, bytes.Length, string.Concat("Expected ", expectedLength, " serialized bytes items but got ", bytes.Length, " bytes."));

      MpRoot result = MsgPackItem.UnpackMultiple(bytes);

      Assert.AreEqual(items.Length, result.Count, string.Concat("Expected ", items.Length, " items but got ", result.Count, " items after round trip."));

      for (int t = 0; t < result.Count; t++) {
        object expected = items[t];
        object actual = result[t].Value;

        Assert.AreEqual(expected, actual, "The returned value ", actual, " differs from the input value ", expected);
      }
    }

  }
}
