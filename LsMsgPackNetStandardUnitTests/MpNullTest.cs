using System;
using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpNullTest
  {

    [TestMethod]
    public void RoundTripTest()
    {
      MsgPackTests.RoundTripTest<MpNull, Object>(null, 1, MsgPackTypeId.MpNull);
    }

  }
}
