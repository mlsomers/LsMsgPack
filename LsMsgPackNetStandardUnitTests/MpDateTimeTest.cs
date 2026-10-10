using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class MpDateTimeTest
  {

    [TestMethod]
    public void EpochTest()
    {
      DateTime dt = new DateTime(1985, 6, 22, 17, 30, 10);
      long epoch = MpDateTime.DateTimeToEpoch(dt.ToUniversalTime());
      DateTime dt2 = MpDateTime.EpochToLocalDateTime(epoch).ToLocalTime();
      Assert.AreEqual(dt, dt2);
    }

    [TestMethod]
    public void RoundTripTestThen()
    {
      RoundTripWithLocalAndUniversalTime(new DateTime(1985, 6, 22, 17, 30, 10), MsgPackTypeId.MpFExt4);
    }

    [TestMethod]
    public void RoundTripTestNow()
    {
      RoundTripWithLocalAndUniversalTime(DateTime.Now, MsgPackTypeId.MpFExt8);// this test is not future proof :-)
    }

    [TestMethod]
    public void RoundTripTestMinValue()
    {
      RoundTripWithLocalAndUniversalTime(DateTime.MinValue.AddDays(1), MsgPackTypeId.MpExt8);
    }

    [TestMethod]
    public void RoundTripTestMaxValue()
    {
      RoundTripWithLocalAndUniversalTime(DateTime.MaxValue.AddDays(-1), MsgPackTypeId.MpExt8);
    }

    [TestMethod]
    public void RoundTripTestmaxFExt4()
    {
      RoundTripWithLocalAndUniversalTime(MpDateTime.MaxFExt4, MsgPackTypeId.MpFExt4);
    }

    [TestMethod]
    public void RoundTripTestmaxFExt8()
    {
      RoundTripWithLocalAndUniversalTime(MpDateTime.MaxFExt8, MsgPackTypeId.MpFExt8);
    }

    [TestMethod]
    public void NullableDateTime()
    {
      DateTime? dt = null;
      MsgPackTests.RoundTripTest<MpNull, DateTime?>(dt, 1, MsgPackTypeId.MpNull);

      dt = DateTime.Now;
      MsgPackTests.RoundTripTest<MpDateTime, DateTime?>(dt, 10, MsgPackTypeId.MpFExt8);
    }

    [TestMethod]
    [DataRow(2106, 2, 6, 6, 28, 16, 0, MsgPackTypeId.MpFExt4)]
    [DataRow(2106, 2, 8, 6, 28, 16, 0, MsgPackTypeId.MpFExt8)]
    [DataRow(2514, 5, 29, 1, 53, 04, 0, MsgPackTypeId.MpFExt8)]
    [DataRow(2514, 5, 31, 1, 53, 04, 0, MsgPackTypeId.MpExt8)]
    public void MiscRoundTripTests(int year, int month, int day, int hour, int minute, int second, int milisecond, MsgPackTypeId typeId)
    {
      RoundTripWithLocalAndUniversalTime(new DateTime(year, month, day, hour, minute, second, milisecond), typeId);
    }

    private void RoundTripWithLocalAndUniversalTime(DateTime dt, MsgPackTypeId expectedType)
    {

      int expectedSize;
      switch (expectedType)
      {
        case MsgPackTypeId.MpFExt4: expectedSize = 6; break;
        case MsgPackTypeId.MpFExt8: expectedSize = 10; break;
        case MsgPackTypeId.MpExt8: expectedSize = 15; break;
        case MsgPackTypeId.MpNull: expectedSize = 1; break;
        default: throw new NotImplementedException();
      }

      DateTime local = new DateTime(dt.Ticks, DateTimeKind.Local);
      DateTime utc = new DateTime(dt.Ticks, DateTimeKind.Utc);

      MsgPackTests.RoundTripTest<MpDateTime, DateTime>(utc, expectedSize, expectedType);
      MsgPackTests.RoundTripTest<MpDateTime, DateTime>(local, expectedSize, expectedType);
    }

    [TestMethod]
    public void FractionalSecondsAreBigEndian()
    {
      DateTime dt = new DateTime(2021, 1, 1, 0, 0, 0, 500, DateTimeKind.Utc);
      // Timestamp 64 from the spec: 30 bits nanoseconds (500000000) and 34 bits seconds (1609459200) in one big-endian 64 bit value
      byte[] expected = new byte[] { (byte)MsgPackTypeId.MpFExt8, 0xFF, 0x77, 0x35, 0x94, 0x00, 0x5F, 0xEE, 0x66, 0x00 };

      CollectionAssert.AreEqual(expected, MsgPackItem.Pack(dt).ToBytes());
      Assert.AreEqual(dt, ((DateTime)MsgPackItem.Unpack(expected).Value).ToUniversalTime());
    }

    /// <summary>
    /// Timestamp 96 stores seconds (signed) plus nanoseconds (always positive), so before 1970 the seconds are rounded down: -1.5 seconds is -2 seconds plus 0.5 seconds.
    /// </summary>
    [TestMethod]
    public void FractionalSecondsBefore1970()
    {
      DateTime dt = new DateTime(1969, 12, 31, 23, 59, 58, 500, DateTimeKind.Utc);
      byte[] expected = new byte[] { (byte)MsgPackTypeId.MpExt8, 12, 0xFF, 0x1D, 0xCD, 0x65, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE };

      CollectionAssert.AreEqual(expected, MsgPackItem.Pack(dt).ToBytes());
      Assert.AreEqual(dt, ((DateTime)MsgPackItem.Unpack(expected).Value).ToUniversalTime());
      Assert.AreEqual(-2, MpDateTime.DateTimeToEpoch(dt));
    }

    /// <summary>
    /// A DateTimeOffset is packed as [its UTC time, the offset in minutes], or with DateTimeOffsetFormat.Timestamp as its UTC time (the round trip through the serializer: SerializingPrimitives).
    /// </summary>
    [TestMethod]
    public void DateTimeOffsetIsPackedAsUtcAndOffset()
    {
      DateTimeOffset when = new DateTimeOffset(2021, 1, 1, 12, 30, 15, 250, TimeSpan.FromHours(3));
      MsgPackItem[] items = (MsgPackItem[])MsgPackItem.Pack(when).Value;
      Assert.AreEqual(when.UtcDateTime, ((DateTime)items[0].Value).ToUniversalTime());
      Assert.AreEqual(180, Convert.ToInt32(items[1].Value));

      MsgPackItem item = MsgPackItem.Pack(when, new MsgPackSettings() { DateTimeOffsetFormat = DateTimeOffsetFormat.Timestamp });
      Assert.AreEqual(when.UtcDateTime, ((DateTime)item.Value).ToUniversalTime());
    }

    [TestMethod]
    public void TestTheory()
    {
      DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

      DateTime MaxFExt4 = epoch.AddSeconds(uint.MaxValue);
      Console.WriteLine(string.Concat("Timestamp 32 : ", epoch.ToString("yyyy-MM-dd HH:mm:ss.fffffff"), " - ", MaxFExt4.ToString("yyyy-MM-dd HH:mm:ss.fffffff")));

      long maxSecs = ((long)uint.MaxValue << 2) | 3; // 17179869183
      long maxNanoSec = 999999999; // not uint.MaxValue
      TimeSpan maxNanoSecSpan = new TimeSpan(maxNanoSec / 100); // 1 tick = 100 nanosec.
      DateTime MaxFExt8 = epoch.AddSeconds(maxSecs).Add(maxNanoSecSpan);
      Console.WriteLine(string.Concat("Timestamp 64 : ", epoch.ToString("yyyy-MM-dd HH:mm:ss.fffffff"), " - ", MaxFExt8.ToString("yyyy-MM-dd HH:mm:ss.fffffff")));

      // DateTime MaxExt8 = Zero.AddSeconds(long.MaxValue).Add(maxNanoSecSpan); // System.ArgumentOutOfRangeException
      DateTime MinExt8 = DateTime.MinValue;
      // DateTime MinExt8 = Zero.AddSeconds(long.MinValue).Add(-maxNanoSecSpan); // System.ArgumentOutOfRangeException
      DateTime MaxExt8 = DateTime.MaxValue;
      Console.WriteLine(string.Concat("Timestamp 96 : ", MinExt8.ToString("yyyy-MM-dd HH:mm:ss.fffffff"), " - ", MaxExt8.ToString("yyyy-MM-dd HH:mm:ss.fffffff")));

    }
  }
}
