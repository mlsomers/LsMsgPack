using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using LsMsgPack;

namespace LsMsgPackUnitTests {
  [TestFixture]
  public class MpNullTest {

    [Test]
    public void RoundTripTest() {
      MsgPackTests.RoundTripTest<MpNull,Object>(null, 1, MsgPackTypeId.MpNull);
    }

  }
}
