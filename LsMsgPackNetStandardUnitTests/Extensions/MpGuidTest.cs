using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using System;

namespace LsMsgPackUnitTests.Extensions
{
  [TestFixture]
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
