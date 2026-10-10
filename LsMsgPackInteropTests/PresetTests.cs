using LtMsgPack;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyType;
using System;
using System.Collections.Generic;
using System.Globalization;
using NB = Nerdbank.MessagePack;

namespace LsMsgPackInteropTests
{
  public class Meeting
  {
    public string Subject { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTime Created { get; set; }
    public Guid Room { get; set; }
    public decimal Cost { get; set; }
    public int Attendees { get; set; }
  }

  public class BigNumbers
  {
    public System.Numerics.BigInteger Big { get; set; }
    public System.Numerics.BigInteger Small { get; set; }
    public Int128 Signed { get; set; }
    public Int128 Negative { get; set; }
    public UInt128 Unsigned { get; set; }
  }

  /// <summary>
  /// LtMsgPack's presets (<see cref="LtMsgPackPresets"/>) against the other libraries with their default settings: nothing configured on their side except reading objects as maps (contractless).
  /// </summary>
  [TestClass]
  public class PresetTests
  {
    private static readonly Invoice[] AllInvoices = Invoices.Create(100);

    /// <summary>
    /// MessagePack-CSharp's own default for classes without attributes (no Guid or decimal formatters added).
    /// </summary>
    private static readonly MessagePackSerializerOptions Contractless = ContractlessStandardResolver.Options;

    private static readonly NB.MessagePackSerializer Nerdbank = new NB.MessagePackSerializer();

    private static Meeting SampleMeeting()
    {
      return new Meeting()
      {
        Subject = "Planning",
        Start = new DateTimeOffset(2026, 9, 29, 14, 30, 0, TimeSpan.FromHours(2)),
        Created = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        Room = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
        Cost = 1234.50m,
        Attendees = 0
      };
    }

    [TestMethod]
    public void MessagePackCSharp_SameBytes()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.MessagePackCSharp());
      foreach (Invoice invoice in AllInvoices)
        CollectionAssert.AreEqual(MessagePackSerializer.Serialize(invoice, Contractless), lt.Serialize(invoice), invoice.InvoiceNumber);
      CollectionAssert.AreEqual(MessagePackSerializer.Serialize(SampleMeeting(), Contractless), lt.Serialize(SampleMeeting()));
    }

    [TestMethod]
    public void MessagePackCSharp_BothWays()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.MessagePackCSharp());
      foreach (Invoice invoice in AllInvoices)
      {
        Same.AssertEqual(invoice, MessagePackSerializer.Deserialize<Invoice>(lt.Serialize(invoice), Contractless), invoice.InvoiceNumber);
        Same.AssertEqual(invoice, lt.Deserialize<Invoice>(MessagePackSerializer.Serialize(invoice, Contractless)), invoice.InvoiceNumber);
      }

      Meeting meeting = SampleMeeting();
      Meeting theirs = MessagePackSerializer.Deserialize<Meeting>(lt.Serialize(meeting), Contractless);
      Meeting ours = lt.Deserialize<Meeting>(MessagePackSerializer.Serialize(meeting, Contractless));
      foreach (Meeting read in new[] { theirs, ours })
      {
        Assert.AreEqual(meeting.Start, read.Start);
        Assert.AreEqual(meeting.Start.Offset, read.Start.Offset, "The offset is kept");
        Assert.AreEqual(meeting.Created, read.Created.ToUniversalTime());
        Assert.AreEqual(meeting.Room, read.Room);
        Assert.AreEqual(meeting.Cost, read.Cost);
      }
    }

    /// <summary>
    /// BigInteger, Int128 and UInt128 as MessagePack-CSharp's bin (little-endian two's complement).
    /// </summary>
    [TestMethod]
    public void MessagePackCSharp_BigIntegers()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.MessagePackCSharp());
      BigNumbers numbers = new BigNumbers()
      {
        Big = System.Numerics.BigInteger.Parse("-123456789012345678901234567890", CultureInfo.InvariantCulture),
        Small = 5,
        Signed = Int128.MaxValue,
        Negative = -2,
        Unsigned = UInt128.MaxValue
      };
      CollectionAssert.AreEqual(MessagePackSerializer.Serialize(numbers, Contractless), lt.Serialize(numbers));

      foreach (BigNumbers read in new[] { MessagePackSerializer.Deserialize<BigNumbers>(lt.Serialize(numbers), Contractless), lt.Deserialize<BigNumbers>(MessagePackSerializer.Serialize(numbers, Contractless)) })
      {
        Assert.AreEqual(numbers.Big, read.Big);
        Assert.AreEqual(numbers.Small, read.Small);
        Assert.AreEqual(numbers.Signed, read.Signed);
        Assert.AreEqual(numbers.Negative, read.Negative);
        Assert.AreEqual(numbers.Unsigned, read.Unsigned);
      }
    }

    /// <summary>
    /// MessagePack-CSharp takes DateTimeKind.Unspecified as UTC, so does the preset (LsMsgPack takes it as local time).
    /// </summary>
    [TestMethod]
    public void MessagePackCSharp_UnspecifiedIsUtc()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.MessagePackCSharp());
      DateTime unspecified = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Unspecified);
      CollectionAssert.AreEqual(MessagePackSerializer.Serialize(unspecified, Contractless), lt.Serialize(unspecified));
    }

    [TestMethod]
    public void Nerdbank_BothWays()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.Nerdbank());
      foreach (Invoice invoice in AllInvoices)
      {
        Same.AssertEqual(invoice, Nerdbank.Deserialize<Invoice, NerdbankShapes>(lt.Serialize(invoice)), invoice.InvoiceNumber);
        Same.AssertEqual(invoice, lt.Deserialize<Invoice>(Nerdbank.Serialize<Invoice, NerdbankShapes>(invoice)), invoice.InvoiceNumber);
      }
    }

    /// <summary>
    /// Libraries without .NET types get Guids and decimals as strings, the rest as the specification describes it.
    /// </summary>
    [TestMethod]
    public void Generic_StringsForGuidAndDecimal()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(LtMsgPackPresets.Generic());
      Meeting meeting = SampleMeeting();
      byte[] bytes = lt.Serialize(meeting);

      Dictionary<object, object> map = MessagePackSerializer.Deserialize<Dictionary<object, object>>(bytes, Contractless);
      Assert.AreEqual("0f8fad5b-d9cb-469f-a165-70867728950e", map["Room"]);
      Assert.AreEqual("1234.50", map["Cost"]);
      Assert.AreEqual(meeting.Start.UtcDateTime, (DateTime)map["Start"]); // a timestamp of the moment
      Assert.AreEqual((byte)0, Convert.ToByte(map["Attendees"], CultureInfo.InvariantCulture), "Default values are written");

      Meeting back = lt.Deserialize<Meeting>(bytes);
      Assert.AreEqual(meeting.Room, back.Room);
      Assert.AreEqual(meeting.Cost, back.Cost);
    }
  }
}
