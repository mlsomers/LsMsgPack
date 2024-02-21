using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpStringTest
  {
    [DataTestMethod]
    [DataRow("Hello world!", 13, MsgPackTypeId.MpStr5)]
    [DataRow("More than 31 characters in this nice string.", 46, MsgPackTypeId.MpStr8)]
    [DataRow("~!@#$%^&*()_+€–¿", 22, MsgPackTypeId.MpStr5)]
    [DataRow("", 1, MsgPackTypeId.MpStr5)]
    public void RoundTripTest(string value, int expectedBytes, MsgPackTypeId expedctedType)
    {
      MsgPackTests.RoundTripTest<MpString, string>(value, expectedBytes, expedctedType);
    }

    [DataTestMethod]
    [DataRow(0, 1, MsgPackTypeId.MpStr5)]
    [DataRow(1, 2, MsgPackTypeId.MpStr5)]
    [DataRow(31, 32, MsgPackTypeId.MpStr5)]
    [DataRow(32, 34, MsgPackTypeId.MpStr8)]
    [DataRow(255, 257, MsgPackTypeId.MpStr8)]
    [DataRow(256, 259, MsgPackTypeId.MpStr16)]
    [DataRow(ushort.MaxValue, ushort.MaxValue + 3, MsgPackTypeId.MpStr16)]
    [DataRow(ushort.MaxValue + 1, ushort.MaxValue + 6, MsgPackTypeId.MpStr32)]
    // [DataRow(0x7FEFFFF9, 0x7FEFFFF9 + 6, MsgPackTypeId.MpStr32)] // Out of memory on my machine
    public void LongerStrings(int length, int expectedBytes, MsgPackTypeId expedctedType)
    {
      string test = new string(' ', length);
      MsgPackTests.RoundTripTest<MpString, string>(test, expectedBytes, expedctedType);
    }

    [TestMethod]
    public void Utf32Test()
    {
      try
      {
        MpString.DefaultEncoding = Encoding.UTF32;
        MsgPackTests.RoundTripTest<MpString, string>("Hello world!", 50, MsgPackTypeId.MpStr8);
      }
      finally
      {
        MpString.DefaultEncoding = Encoding.UTF8;
      }
    }
  }
}
