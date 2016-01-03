using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using LsMsgPack;

namespace LsMsgPackUnitTests {
  [TestFixture]
  public class MpBoolTest {

    [Test]
    public void RoundTripTestFalse() {
      MsgPackTests.RoundTripTest<MpBool,bool>(false, 1, MsgPackTypeId.MpBoolFalse);
    }

    [Test]
    public void RoundTripTestTrue() {
      MsgPackTests.RoundTripTest<MpBool,bool>(true, 1, MsgPackTypeId.MpBoolTrue);
    }

  }
}
