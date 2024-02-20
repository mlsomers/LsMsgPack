using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpBoolTest
  {

    [TestMethod]
    public void RoundTripTestFalse()
    {
      MsgPackTests.RoundTripTest<MpBool, bool>(false, 1, MsgPackTypeId.MpBoolFalse);
    }

    [TestMethod]
    public void RoundTripTestTrue()
    {
      MsgPackTests.RoundTripTest<MpBool, bool>(true, 1, MsgPackTypeId.MpBoolTrue);
    }

  }
}
