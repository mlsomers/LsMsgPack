using LsMsgPack;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The encodings of integers, DateTimes and timestamps through the serializer (the item tests, MpIntTest and MpDateTimeTest, check the items of LsMsgPack).
  /// </summary>
  public abstract class SerializingPrimitives
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    /// <summary>
    /// The negative fixint holds -32 to -1, every integer type uses it when dynamically compacting.
    /// </summary>
    [TestMethod]
    public void MinusThirtyTwoIsANegativeFixint()
    {
      MsgPackSettings compact = new MsgPackSettings() { UseInexedSchema = false };
      foreach (object value in new object[] { (sbyte)-32, (short)-32, -32, -32L })
      {
        CollectionAssert.AreEqual(new byte[] { 0xE0 }, Serializer.Serialize(value, value.GetType(), compact), value.GetType().Name);
        Assert.AreEqual(value, Serializer.Deserialize(value.GetType(), new byte[] { 0xE0 }, compact));
      }
    }

    /// <summary>
    /// Without compacting, small sbytes are written as a fixint: 0 to 31 as a positive one.
    /// </summary>
    [TestMethod]
    [DataRow((sbyte)0, new byte[] { 0x00 })]
    [DataRow((sbyte)1, new byte[] { 0x01 })]
    [DataRow((sbyte)31, new byte[] { 0x1F })]
    [DataRow((sbyte)32, new byte[] { 0xD0, 0x20 })]
    [DataRow((sbyte)-1, new byte[] { 0xFF })]
    [DataRow((sbyte)-32, new byte[] { 0xE0 })]
    [DataRow((sbyte)-33, new byte[] { 0xD0, 0xDF })]
    public void SByteWithoutCompacting(sbyte value, byte[] expected)
    {
      MsgPackSettings settings = new MsgPackSettings() { DynamicallyCompact = false, UseInexedSchema = false };
      CollectionAssert.AreEqual(expected, Serializer.Serialize(value, settings));
      Assert.AreEqual(value, Serializer.Deserialize<sbyte>(Serializer.Serialize(value, settings), settings));
    }

    [TestMethod]
    public void FractionalSecondsAreBigEndian()
    {
      DateTime dt = new DateTime(2021, 1, 1, 0, 0, 0, 500, DateTimeKind.Utc);
      // Timestamp 64 from the spec: 30 bits nanoseconds (500000000) and 34 bits seconds (1609459200) in one big-endian 64 bit value
      byte[] expected = new byte[] { (byte)MsgPackTypeId.MpFExt8, 0xFF, 0x77, 0x35, 0x94, 0x00, 0x5F, 0xEE, 0x66, 0x00 };
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp };

      CollectionAssert.AreEqual(expected, Serializer.Serialize(dt, withoutSchema));
      Assert.AreEqual(dt, Serializer.Deserialize<DateTime>(expected, withoutSchema).ToUniversalTime());
    }

    /// <summary>
    /// Timestamp 96 stores seconds (signed) plus nanoseconds (always positive), so before 1970 the seconds are rounded down: -1.5 seconds is -2 seconds plus 0.5 seconds.
    /// </summary>
    [TestMethod]
    public void FractionalSecondsBefore1970()
    {
      DateTime dt = new DateTime(1969, 12, 31, 23, 59, 58, 500, DateTimeKind.Utc);
      byte[] expected = new byte[] { (byte)MsgPackTypeId.MpExt8, 12, 0xFF, 0x1D, 0xCD, 0x65, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE };
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp };

      CollectionAssert.AreEqual(expected, Serializer.Serialize(dt, withoutSchema));
      Assert.AreEqual(dt, Serializer.Deserialize<DateTime>(expected, withoutSchema).ToUniversalTime());
    }

    [TestMethod]
    [DataRow(1969, 12, 31, 23, 59, 59, 999)]
    [DataRow(1969, 1, 1, 0, 0, 0, 1)]
    [DataRow(1601, 1, 1, 0, 0, 0, 250)]
    [DataRow(1, 1, 1, 0, 0, 0, 1)]
    public void FractionalSecondsBefore1970RoundTrip(int year, int month, int day, int hour, int minute, int second, int millisecond)
    {
      DateTime dt = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc).AddTicks(7);
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp };
      Assert.AreEqual(dt, Serializer.Deserialize<DateTime>(Serializer.Serialize(dt, withoutSchema), withoutSchema).ToUniversalTime());
      withoutSchema.DateTimeFormat = DateTimeFormat.Native;
      Assert.AreEqual(dt, Serializer.Deserialize<DateTime>(Serializer.Serialize(dt, withoutSchema), withoutSchema));
    }

    public class PrimitivesWithDateTimeOffset
    {
      public DateTimeOffset When { get; set; }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DateTimeOffsetRoundTrip(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      DateTimeOffset when = new DateTimeOffset(2021, 1, 1, 12, 30, 15, 250, TimeSpan.FromHours(3));
      byte[] buffer = Serializer.Serialize(new PrimitivesWithDateTimeOffset() { When = when }, settings);
      PrimitivesWithDateTimeOffset ret = Serializer.Deserialize<PrimitivesWithDateTimeOffset>(buffer, settings);
      Assert.AreEqual(when, ret.When); // the same moment (the offset is not stored)
    }

    public class PrimitivesWithDates
    {
      public DateTime When { get; set; }
      public object Boxed { get; set; }
      public DateTimeOffset Offset { get; set; }
    }

    /// <summary>
    /// Timestamps: Unspecified written as UTC and read as Unspecified: the clock time comes back unchanged in any time zone (as JSON writes it, without an offset).
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnspecifiedClockTimeRoundTrip(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, UnspecifiedDateTimeKind = DateTimeKind.Utc, ReadDateTimeKind = DateTimeKind.Unspecified, DateTimeFormat = DateTimeFormat.Timestamp };
      DateTime noon = new DateTime(2026, 10, 7, 12, 0, 0, 250, DateTimeKind.Unspecified);
      PrimitivesWithDates back = Serializer.Deserialize<PrimitivesWithDates>(Serializer.Serialize(new PrimitivesWithDates() { When = noon, Boxed = noon }, settings), settings);
      Assert.AreEqual(noon, back.When);
      Assert.AreEqual(DateTimeKind.Unspecified, back.When.Kind);
      Assert.AreEqual(noon, (DateTime)back.Boxed);
      Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)back.Boxed).Kind);

      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false, UnspecifiedDateTimeKind = DateTimeKind.Utc, DateTimeFormat = DateTimeFormat.Timestamp };
      CollectionAssert.AreEqual(Serializer.Serialize(DateTime.SpecifyKind(noon, DateTimeKind.Utc), withoutSchema), Serializer.Serialize(noon, withoutSchema), "written as the same clock time in UTC");
    }

    /// <summary>
    /// The default (<see cref="DateTimeFormat.Native"/>): every value comes back as it was written, as Json.NET reads its text: Utc and Unspecified with their ticks and Kind,
    /// a Local value as the same moment in local time (the reader's time zone). With timestamps a Local value came back as UTC, an hour early on a clock in Amsterdam.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DefaultsKeepTheValueAndKind(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      Assert.AreEqual(DateTimeFormat.Native, settings.DateTimeFormat);

      DateTime midnight = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Unspecified);
      DateTime utc = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc).AddTicks(7);
      DateTime local = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Local);
      foreach (DateTime value in new[] { midnight, utc, local, DateTime.MinValue.AddTicks(1), DateTime.MaxValue, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) })
      {
        PrimitivesWithDates back = Serializer.Deserialize<PrimitivesWithDates>(Serializer.Serialize(new PrimitivesWithDates() { When = value, Boxed = value }, settings), settings);
        Assert.AreEqual(value, back.When, value.Kind.ToString());
        Assert.AreEqual(value.Kind, back.When.Kind);
        Assert.AreEqual(value, (DateTime)back.Boxed, value.Kind.ToString());
        Assert.AreEqual(value.Kind, ((DateTime)back.Boxed).Kind);
      }
    }

    /// <summary>
    /// The native extension: fixext 8 of type 6 with DateTime.ToBinary() big-endian (in the byte order of the settings); a Local value holds the ticks in UTC with the top bit set.
    /// </summary>
    [TestMethod]
    public void NativeDateTimeBytes()
    {
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false };
      DateTime utc = new DateTime(2021, 1, 1, 0, 0, 0, 500, DateTimeKind.Utc);
      byte[] expected = new byte[] { (byte)MsgPackTypeId.MpFExt8, 6, 0x48, 0xD8, 0xAD, 0xE8, 0x2E, 0xF8, 0xCB, 0x40 }; // 0x40... the Kind Utc, then the ticks 0x08D8ADE82EF8CB40
      CollectionAssert.AreEqual(expected, Serializer.Serialize(utc, withoutSchema));
      DateTime back = Serializer.Deserialize<DateTime>(expected, withoutSchema);
      Assert.AreEqual(utc, back);
      Assert.AreEqual(DateTimeKind.Utc, back.Kind);

      DateTime local = new DateTime(2021, 7, 1, 12, 0, 0, DateTimeKind.Local);
      byte[] bytes = Serializer.Serialize(local, withoutSchema);
      ulong bits = 0;
      for (int t = 2; t < 10; t++)
        bits = (bits << 8) | bytes[t];
      Assert.AreEqual(0x8000000000000000UL | (ulong)local.ToUniversalTime().Ticks, bits, "the ticks in UTC with the Local flag");

      MsgPackSettings neverSwap = new MsgPackSettings() { UseInexedSchema = false, EndianAction = EndianAction.NeverSwap };
      byte[] native = Serializer.Serialize(utc, neverSwap);
      Assert.AreEqual(utc.ToBinary(), BitConverter.ToInt64(native, 2), "in the byte order of the machine");
      Assert.AreEqual(utc, Serializer.Deserialize<DateTime>(native, neverSwap));
    }

    /// <summary>
    /// Readers take both formats, whatever <see cref="MsgPackOptions.DateTimeFormat"/> says (data of other libraries, or of older writers, is timestamps).
    /// </summary>
    [TestMethod]
    public void BothFormatsRead()
    {
      DateTime utc = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
      MsgPackSettings native = new MsgPackSettings() { UseInexedSchema = false };
      MsgPackSettings timestamps = new MsgPackSettings() { UseInexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp };
      Assert.AreEqual(utc, Serializer.Deserialize<DateTime>(Serializer.Serialize(utc, timestamps), native));
      Assert.AreEqual(utc, Serializer.Deserialize<DateTime>(Serializer.Serialize(utc, native), timestamps));
      Assert.AreEqual(utc, (DateTime)Serializer.Deserialize<object>(Serializer.Serialize(utc, native), timestamps));
      Assert.AreEqual(new DateTimeOffset(utc), Serializer.Deserialize<DateTimeOffset>(Serializer.Serialize(utc, native), native));
    }

    /// <summary>
    /// Data in the native extension that holds no DateTime (ticks beyond DateTime.MaxValue) throws a MsgPackException, not the ArgumentException of DateTime.FromBinary.
    /// </summary>
    [TestMethod]
    public void NativeDateTimeBeyondTheRange()
    {
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false };
      byte[] tooLate = new byte[] { (byte)MsgPackTypeId.MpFExt8, 6, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
#if KEEPTRACK
      Assert.Throws<Exception>(() => Serializer.Deserialize<DateTime>(tooLate, withoutSchema)); // LsMsgPackDebug keeps the error as an MpError item (ContinueProcessingOnBreakingError), which is no DateTime
#else
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<DateTime>(tooLate, withoutSchema));
#endif
    }

    /// <summary>
    /// Timestamps (<see cref="DateTimeFormat.Timestamp"/>) keep the clock time of Utc and Unspecified values in any time zone, as System.Text.Json does (the Kind becomes Utc: a timestamp
    /// does not keep it), and a Local value is the same moment in UTC. Before October 2026 an Unspecified midnight written in New York was read as 06:00 in Amsterdam.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TimestampsKeepTheClockTimeAsJson(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, DateTimeFormat = DateTimeFormat.Timestamp };
      Assert.AreEqual(DateTimeKind.Utc, settings.UnspecifiedDateTimeKind);
      Assert.AreEqual(DateTimeKind.Utc, settings.ReadDateTimeKind);

      DateTime midnight = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Unspecified);
      DateTime utc = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
      DateTime local = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Local);
      foreach (DateTime value in new[] { midnight, utc, local })
      {
        PrimitivesWithDates back = Serializer.Deserialize<PrimitivesWithDates>(Serializer.Serialize(new PrimitivesWithDates() { When = value, Boxed = value }, settings), settings);
        DateTime expected = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        Assert.AreEqual(expected, back.When, value.Kind.ToString());
        Assert.AreEqual(DateTimeKind.Utc, back.When.Kind);
        Assert.AreEqual(expected, (DateTime)back.Boxed, value.Kind.ToString());
        Assert.AreEqual(DateTimeKind.Utc, ((DateTime)back.Boxed).Kind);
      }
    }

    [TestMethod]
    [DataRow(DateTimeKind.Local)]
    [DataRow(DateTimeKind.Utc)]
    [DataRow(DateTimeKind.Unspecified)]
    public void ReadDateTimeKind(DateTimeKind kind)
    {
      MsgPackSettings settings = new MsgPackSettings() { ReadDateTimeKind = kind, DateTimeOffsetFormat = DateTimeOffsetFormat.Timestamp, DateTimeFormat = DateTimeFormat.Timestamp }; // the offset is not written
      DateTime utc = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
      DateTimeOffset offset = new DateTimeOffset(2026, 10, 7, 14, 0, 0, TimeSpan.FromHours(2));
      PrimitivesWithDates back = Serializer.Deserialize<PrimitivesWithDates>(Serializer.Serialize(new PrimitivesWithDates() { When = utc, Boxed = utc, Offset = offset }, settings), settings);

      DateTime expected = kind == DateTimeKind.Local ? utc.ToLocalTime() : DateTime.SpecifyKind(utc, kind);
      Assert.AreEqual(expected, back.When);
      Assert.AreEqual(kind, back.When.Kind);
      Assert.AreEqual(expected, (DateTime)back.Boxed);
      Assert.AreEqual(kind, ((DateTime)back.Boxed).Kind);

      Assert.AreEqual(offset.UtcDateTime, back.Offset.UtcDateTime, "the same moment");
      Assert.AreEqual(kind == DateTimeKind.Local ? TimeZoneInfo.Local.GetUtcOffset(utc) : TimeSpan.Zero, back.Offset.Offset);

      settings.DateTimeOffsetFormat = DateTimeOffsetFormat.TimestampAndOffset; // the default: the offset is written
      back = Serializer.Deserialize<PrimitivesWithDates>(Serializer.Serialize(new PrimitivesWithDates() { Offset = offset }, settings), settings);
      Assert.AreEqual(offset, back.Offset);
      Assert.AreEqual(offset.Offset, back.Offset.Offset);
    }

    [TestMethod]
    public void DecimalExtension()
    {
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false };
      foreach (decimal value in new[] { 0m, 1.5m, -79228162514264337593543950335m, 0.0000001m })
      {
        byte[] bytes = Serializer.Serialize(value, withoutSchema);
        Assert.AreEqual((byte)MsgPackTypeId.MpFExt16, bytes[0]);
        Assert.AreEqual((byte)1, bytes[1]);
        Assert.AreEqual(value, Serializer.Deserialize<decimal>(bytes, withoutSchema));
      }
    }

    [TestMethod]
    public void GuidIsBinaryData()
    {
      Guid id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
      byte[] bytes = Serializer.Serialize(id, new MsgPackSettings() { UseInexedSchema = false });
      Assert.AreEqual((byte)MsgPackTypeId.MpBin8, bytes[0]);
      Assert.AreEqual((byte)16, bytes[1]);
      Assert.AreEqual(id, Serializer.Deserialize<Guid>(bytes, new MsgPackSettings() { UseInexedSchema = false }));
    }
  }

  [TestClass]
  public class DebugSerializingPrimitives : SerializingPrimitives
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsSerializingPrimitives : SerializingPrimitives
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class GenSerializingPrimitives : SerializingPrimitives
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
