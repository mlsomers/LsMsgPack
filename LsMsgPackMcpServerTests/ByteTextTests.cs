using LsMsgPackMcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackMcpServerTests
{
  /// <summary>
  /// The same formats as the VS Code extension's bytesFromText.ts.
  /// </summary>
  [TestClass]
  public class ByteTextTests
  {
    private static readonly byte[] Expected = new byte[] { 0x82, 0xA4, 0x4E, 0xFF };

    [TestMethod]
    public void Formats()
    {
      CollectionAssert.AreEqual(Expected, ByteText.Parse("82a44eff"));
      CollectionAssert.AreEqual(Expected, ByteText.Parse("0x82A44EFF"));
      CollectionAssert.AreEqual(Expected, ByteText.Parse("82 A4 4E FF"));
      CollectionAssert.AreEqual(Expected, ByteText.Parse("0x82, 0xa4, 0x4e, 0xff"));
      CollectionAssert.AreEqual(Expected, ByteText.Parse("[130, 164, 78, 255]"));
      CollectionAssert.AreEqual(Expected, ByteText.Parse(Convert.ToBase64String(Expected)));
      CollectionAssert.AreEqual(Expected, ByteText.Parse("gqRO_w")); // base64url without padding
      CollectionAssert.AreEqual(Expected, ByteText.Parse("b'\\x82\\xa4N\\xff'"));
      CollectionAssert.AreEqual(new byte[] { 10, 0, 92, 113 }, ByteText.Parse("b'\\n\\0\\q'"));
      CollectionAssert.AreEqual(new byte[0], ByteText.Parse("  "));
    }

    /// <summary>
    /// Values with 0x are hex also when they are not all written with two digits: "0x92, 1, 2" was read as the decimal values 92, 1 and 2.
    /// </summary>
    [TestMethod]
    public void HexValuesWithoutTwoDigits()
    {
      CollectionAssert.AreEqual(new byte[] { 0x92, 0x01, 0x02 }, ByteText.Parse("[0x92, 1, 2]"));
      CollectionAssert.AreEqual(new byte[] { 0x92, 0x01, 0x0A }, ByteText.Parse("{ 0x92, 0x1, 0xA }"));
      CollectionAssert.AreEqual(new byte[] { 0x92, 0x10 }, ByteText.Parse("0X92 10"));
      Assert.ThrowsExactly<FormatException>(() => ByteText.Parse("0x92, 0x100"));
    }

    /// <summary>
    /// Hex in groups of different lengths, e.g. the header separated from the content: was refused.
    /// </summary>
    [TestMethod]
    public void HexInGroups()
    {
      CollectionAssert.AreEqual(new byte[] { 0x91, 0xC4, 0x02, 0x00, 0x01 }, ByteText.Parse("91 c4 02 0001"));
      CollectionAssert.AreEqual(new byte[] { 0x91, 0xC4, 0x02, 0x00, 0x01 }, ByteText.Parse("91c4 0200 01"));
      CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0x56 }, ByteText.Parse("1234 56")); // 1234 is no decimal byte
      CollectionAssert.AreEqual(new byte[] { 100, 20 }, ByteText.Parse("100, 20")); // still decimal
      Assert.ThrowsExactly<FormatException>(() => ByteText.Parse("91 c4 020"));
    }

    [TestMethod]
    public void MultiLineBase64()
    {
      byte[] bytes = new byte[100];
      for (int t = 0; t < bytes.Length; t++)
        bytes[t] = (byte)(t * 7);
      string base64 = Convert.ToBase64String(bytes);
      CollectionAssert.AreEqual(bytes, ByteText.Parse(base64.Substring(0, 60) + "\n" + base64.Substring(60)));
    }

    [TestMethod]
    public void Errors()
    {
      Assert.ThrowsExactly<FormatException>(() => ByteText.Parse("12, 300"));
      Assert.ThrowsExactly<FormatException>(() => ByteText.Parse("hello world"));
      Assert.ThrowsExactly<FormatException>(() => ByteText.Parse("b'\\xZZ'"));
    }
  }
}
