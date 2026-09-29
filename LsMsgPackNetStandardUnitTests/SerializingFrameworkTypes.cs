using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Framework types without settable properties (they used to be serialized as an empty map and read as their default value).
  /// <para>They are written with the same encodings as MessagePack-CSharp: char as an integer, TimeSpan and TimeOnly as ticks, DateOnly as its day number and Uri as its original string.</para>
  /// </summary>
  [TestClass]
  public class SerializingFrameworkTypes
  {
    public SerializingFrameworkTypes()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(FrameworkTypesHolder));
    }

    public class FrameworkTypesHolder
    {
      public char Letter { get; set; }
      public char? MaybeLetter { get; set; }
      public TimeSpan Duration { get; set; }
      public TimeSpan? MaybeDuration { get; set; }
      public DateOnly Day { get; set; }
      public TimeOnly Time { get; set; }
      public Uri Link { get; set; }
      public Uri RelativeLink { get; set; }
      public List<TimeSpan> Durations { get; set; }
      public Dictionary<DateOnly, Uri> Links { get; set; }
    }

    private static FrameworkTypesHolder CreateHolder()
    {
      return new FrameworkTypesHolder()
      {
        Letter = 'é',
        MaybeLetter = 'A',
        Duration = TimeSpan.FromMinutes(90),
        MaybeDuration = TimeSpan.FromTicks(-12345),
        Day = new DateOnly(2026, 9, 29),
        Time = new TimeOnly(13, 45, 10, 5),
        Link = new Uri("https://example.com/path?q=1"),
        RelativeLink = new Uri("../relative/path", UriKind.Relative),
        Durations = new List<TimeSpan>() { TimeSpan.MaxValue, TimeSpan.MinValue, TimeSpan.FromDays(1) },
        Links = new Dictionary<DateOnly, Uri>() { { DateOnly.MinValue, new Uri("https://example.com/min") }, { DateOnly.MaxValue, new Uri("urn:isbn:0451450523") } }
      };
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PropertiesRoundTrip(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      FrameworkTypesHolder holder = CreateHolder();

      FrameworkTypesHolder ret = MsgPackSerializer.Deserialize<FrameworkTypesHolder>(MsgPackSerializer.Serialize(holder, settings), settings);

      Assert.AreEqual(holder.Letter, ret.Letter);
      Assert.AreEqual(holder.MaybeLetter, ret.MaybeLetter);
      Assert.AreEqual(holder.Duration, ret.Duration);
      Assert.AreEqual(holder.MaybeDuration, ret.MaybeDuration);
      Assert.AreEqual(holder.Day, ret.Day);
      Assert.AreEqual(holder.Time, ret.Time);
      Assert.AreEqual(holder.Link, ret.Link);
      Assert.AreEqual(holder.Link.OriginalString, ret.Link.OriginalString);
      Assert.AreEqual(holder.RelativeLink.OriginalString, ret.RelativeLink.OriginalString);
      Assert.IsFalse(ret.RelativeLink.IsAbsoluteUri);
      CollectionAssert.AreEqual(holder.Durations, ret.Durations);
      CollectionAssert.AreEqual(holder.Links, ret.Links);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DefaultValuesAreOmitted(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      FrameworkTypesHolder empty = new FrameworkTypesHolder();

      FrameworkTypesHolder ret = MsgPackSerializer.Deserialize<FrameworkTypesHolder>(MsgPackSerializer.Serialize(empty, settings), settings);

      Assert.AreEqual(default(char), ret.Letter);
      Assert.IsNull(ret.MaybeLetter);
      Assert.AreEqual(TimeSpan.Zero, ret.Duration);
      Assert.IsNull(ret.MaybeDuration);
      Assert.AreEqual(default(DateOnly), ret.Day);
      Assert.IsNull(ret.Link);
    }

    /// <summary>
    /// The same bytes as MessagePack-CSharp writes (checked in LsMsgPackInteropTests).
    /// </summary>
    [TestMethod]
    public void Encodings()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false };

      CollectionAssert.AreEqual(new byte[] { 0x41 }, MsgPackSerializer.Serialize('A', settings));
      CollectionAssert.AreEqual(new byte[] { 0xCD, 0x20, 0xAC }, MsgPackSerializer.Serialize('€', settings));
      CollectionAssert.AreEqual(new byte[] { 0xCF, 0x00, 0x00, 0x00, 0x0C, 0x92, 0xA6, 0x9C, 0x00 }, MsgPackSerializer.Serialize(TimeSpan.FromMinutes(90), settings));
      CollectionAssert.AreEqual(new byte[] { 0xD1, 0xCF, 0xC7 }, MsgPackSerializer.Serialize(TimeSpan.FromTicks(-12345), settings));
      CollectionAssert.AreEqual(new byte[] { 0xCE, 0x00, 0x0B, 0x4A, 0x2F }, MsgPackSerializer.Serialize(new DateOnly(2026, 9, 29), settings));
      CollectionAssert.AreEqual(new byte[] { 0xCF, 0x00, 0x00, 0x00, 0x73, 0x46, 0x43, 0x3A, 0x50 }, MsgPackSerializer.Serialize(new TimeOnly(13, 45, 10, 5), settings));
      CollectionAssert.AreEqual(new byte[] { 0xA5, (byte)'a', (byte)'/', (byte)'b', (byte)'?', (byte)'c' }, MsgPackSerializer.Serialize(new Uri("a/b?c", UriKind.Relative), settings));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RootValues(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };

      Assert.AreEqual('x', MsgPackSerializer.Deserialize<char>(MsgPackSerializer.Serialize('x', settings), settings));
      Assert.AreEqual(TimeSpan.FromSeconds(-1.5), MsgPackSerializer.Deserialize<TimeSpan>(MsgPackSerializer.Serialize(TimeSpan.FromSeconds(-1.5), settings), settings));
      Assert.AreEqual(new DateOnly(1999, 12, 31), MsgPackSerializer.Deserialize<DateOnly>(MsgPackSerializer.Serialize(new DateOnly(1999, 12, 31), settings), settings));
      Assert.AreEqual(TimeOnly.MaxValue, MsgPackSerializer.Deserialize<TimeOnly>(MsgPackSerializer.Serialize(TimeOnly.MaxValue, settings), settings));
      Assert.AreEqual(new Uri("https://example.com/"), MsgPackSerializer.Deserialize<Uri>(MsgPackSerializer.Serialize(new Uri("https://example.com/"), settings), settings));
    }

    public class FrameworkTypesBoxed
    {
      public object Duration { get; set; }
      public List<object> Values { get; set; }
    }

    /// <summary>
    /// Assigned to object they get a type id (TimeSpan and DateOnly are found by their short name).
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BoxedValues(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      FrameworkTypesBoxed boxed = new FrameworkTypesBoxed()
      {
        Duration = TimeSpan.FromHours(1),
        Values = new List<object>() { TimeSpan.FromMinutes(1), new DateOnly(2000, 1, 1), new TimeOnly(12, 0) }
      };

      FrameworkTypesBoxed ret = MsgPackSerializer.Deserialize<FrameworkTypesBoxed>(MsgPackSerializer.Serialize(boxed, settings), settings);

      Assert.AreEqual(boxed.Duration, ret.Duration);
      CollectionAssert.AreEqual(boxed.Values, ret.Values);
    }
  }
}
