using LsMsgPack;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackUnitTests.Extensions
{
  [TestClass]
  public class MpGuidTest
  {

    [TestMethod]
    public void RoundTripMpGuid()
    {
      MsgPackTests.RoundTripTest<MpBin, Guid>(Guid.NewGuid(), 18, MsgPackTypeId.MpBin8);
    }
  }
}
