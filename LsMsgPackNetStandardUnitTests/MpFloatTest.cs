using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;


namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpFloatTest
  {
    [TestMethod]
    [DataRow(0f)]
    [DataRow(1f)]
    [DataRow(-1f)]
    [DataRow(float.MinValue)]
    [DataRow(float.MaxValue)]
    [DataRow(float.NegativeInfinity)]
    [DataRow(float.PositiveInfinity)]
    [DataRow(float.Epsilon)]
    [DataRow(-float.Epsilon)]
    public void RoundTripFloat32(float value)
    {
      MsgPackTests.RoundTripTest<MpFloat, float>(value, 5, MsgPackTypeId.MpFloat);
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(1d)]
    [DataRow(-1d)]
    [DataRow(double.MinValue)]
    [DataRow(double.MaxValue)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.Epsilon)]
    [DataRow(-double.Epsilon)]
    public void RoundTripFloat64(double value)
    {
      MsgPackTests.RoundTripTest<MpFloat, double>(value, 9, MsgPackTypeId.MpDouble);
    }

  }
}
