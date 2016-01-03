using LsMsgPack;
using NUnit.Framework;

namespace LsMsgPackUnitTests {
  [TestFixture]
  public class MpBinTest {

    [TestCase(0, 2, MsgPackTypeId.MpBin8)]
    [TestCase(1, 3, MsgPackTypeId.MpBin8)]
    [TestCase(32, 34, MsgPackTypeId.MpBin8)]
    [TestCase(255, 257, MsgPackTypeId.MpBin8)]
    [TestCase(256, 259, MsgPackTypeId.MpBin16)]
    [TestCase(ushort.MaxValue, ushort.MaxValue + 3, MsgPackTypeId.MpBin16)]
    [TestCase(ushort.MaxValue + 1, ushort.MaxValue + 6, MsgPackTypeId.MpBin32)]
    // [TestCase(0x7FEFFFF9, 0x7FEFFFF9 + 6, MsgPackTypeId.MpBin32)] // Out of memory on my machine
    public void BinaryLengths(int length, int expectedBytes, MsgPackTypeId expedctedType) {
      Randomizer rnd = new Randomizer();
      byte[] test = new byte[length];
      rnd.NextBytes(test);
      MsgPackTests.RoundTripTest<MpBin, byte[]>(test, expectedBytes, expedctedType);
    }
  }
}
