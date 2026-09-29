using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The encodings of integers and timestamps through the serializer (the item tests, MpIntTest and MpDateTimeTest, check the items of LsMsgPack).
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
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false };

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
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false };

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
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false };
      Assert.AreEqual(dt, Serializer.Deserialize<DateTime>(Serializer.Serialize(dt, withoutSchema), withoutSchema).ToUniversalTime());
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
  public class LsSerializingPrimitives : SerializingPrimitives
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingPrimitives : SerializingPrimitives
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
