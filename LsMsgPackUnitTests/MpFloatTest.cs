using LsMsgPack;
using NUnit.Framework;

namespace LsMsgPackUnitTests {
  [TestFixture]
  public class MpFloatTest {

    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(-1f)]
    [TestCase(float.MinValue)]
    [TestCase(float.MaxValue)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.Epsilon)]
    [TestCase(-float.Epsilon)]
    public void RoundTripFloat32(float value) {
      MsgPackTests.RoundTripTest<MpFloat, float>(value, 5, MsgPackTypeId.MpFloat);
    }

    [TestCase(0d)]
    [TestCase(1d)]
    [TestCase(-1d)]
    [TestCase(double.MinValue)]
    [TestCase(double.MaxValue)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.Epsilon)]
    [TestCase(-double.Epsilon)]
    public void RoundTripFloat64(double value) {
      MsgPackTests.RoundTripTest<MpFloat, double>(value, 9, MsgPackTypeId.MpDouble);
    }

  }
}
