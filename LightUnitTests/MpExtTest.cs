using LsMsgPack;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace LsMsgPackUnitTests {
  [TestFixture]
  public class MpExtTest {

    [TestCase(0, 3, MsgPackTypeId.MpExt8)]
    [TestCase(1, 3, MsgPackTypeId.MpFExt1)]
    [TestCase(2, 4, MsgPackTypeId.MpFExt2)]
    [TestCase(3, 6, MsgPackTypeId.MpExt8)]
    [TestCase(4, 6, MsgPackTypeId.MpFExt4)]
    [TestCase(5, 8, MsgPackTypeId.MpExt8)]
    [TestCase(7, 10, MsgPackTypeId.MpExt8)]
    [TestCase(8, 10, MsgPackTypeId.MpFExt8)]
    [TestCase(9, 12, MsgPackTypeId.MpExt8)]
    [TestCase(15, 18, MsgPackTypeId.MpExt8)]
    [TestCase(16, 18, MsgPackTypeId.MpExt16)]
    [TestCase(17, 20, MsgPackTypeId.MpExt8)]
    [TestCase(255, 258, MsgPackTypeId.MpExt8)]
    [TestCase(256, 260, MsgPackTypeId.MpExt16)]
    [TestCase(ushort.MaxValue, ushort.MaxValue + 4, MsgPackTypeId.MpExt16)]
    [TestCase(ushort.MaxValue+1, ushort.MaxValue + 7, MsgPackTypeId.MpExt32)]
    public void BinaryLengths(int length, int expectedBytes, MsgPackTypeId expedctedType) {
      Randomizer rnd = new Randomizer();
      byte[] test = new byte[length];
      rnd.NextBytes(test);
      MsgPackTests.RoundTripTest<MpExt, byte[]>(test, expectedBytes, expedctedType, (sbyte)(rnd.Next(255) - 128));
    }

  }
}
