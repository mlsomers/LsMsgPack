using LsMsgPack;
using LsMsgPack.Types.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;

namespace LsMsgPackUnitTests.Extensions
{
  [TestFixture]
  [TestClass]
  public class MpDecimalTest
  {
    [TestMethod]
    [DataRow(1)]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(short.MaxValue)]
    [DataRow(short.MinValue)]
    [DataRow(double.Tau)]
    [DataRow(double.Pi)]
    [DataRow(long.MaxValue)]
    [DataRow(long.MinValue)]
    public void RoundTripMpDecimal(double value)
    {
      MsgPackTests.RoundTripTest<MpDecimal, decimal>((decimal)value, 18, MsgPackTypeId.MpExt16, true, 1);
    }

    /// <summary>
    /// These values are not allowed in attribute arguments because they are not primitives
    /// </summary>
    [TestMethod]
    public void RoundTripMpDecimalMaxValues()
    {
      MsgPackTests.RoundTripTest<MpDecimal, decimal>(decimal.MaxValue, 18, MsgPackTypeId.MpExt16);
      MsgPackTests.RoundTripTest<MpDecimal, decimal>(decimal.MinValue, 18, MsgPackTypeId.MpExt16);
      MsgPackTests.RoundTripTest<MpDecimal, decimal>(decimal.Zero, 18, MsgPackTypeId.MpExt16);
      MsgPackTests.RoundTripTest<MpDecimal, decimal>(decimal.One, 18, MsgPackTypeId.MpExt16);
      MsgPackTests.RoundTripTest<MpDecimal, decimal>(decimal.MinusOne, 18, MsgPackTypeId.MpExt16);
      MsgPackTests.RoundTripTest<MpDecimal, decimal>(19.95m, 18, MsgPackTypeId.MpExt16);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(short.MaxValue)]
    [DataRow(short.MinValue)]
    [DataRow(double.Tau)]
    [DataRow(double.Pi)]
    [DataRow(long.MaxValue)]
    [DataRow(long.MinValue)]
    public void RoundTripMpDecimalOtherId(double value)
    {
      MpDecimal.Default_TypeSpecifier = 9;
      MsgPackSettings.Default_CustomExtentionTypes = new ICustomExt[]
      {
        new MpDecimal((MsgPackSettings)null),
      };
      MsgPackTests.RoundTripTest<MpDecimal, decimal>((decimal)value, 18, MsgPackTypeId.MpExt16, true, 9);

      // restore for other test
      MpDecimal.Default_TypeSpecifier = 1;
      MsgPackSettings.Default_CustomExtentionTypes = new ICustomExt[]
      {
        new MpDecimal((MsgPackSettings)null),
      };
    }
  }
}
