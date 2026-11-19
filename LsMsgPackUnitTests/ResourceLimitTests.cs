using LsMsgPack;
using LsMsgPack.Http;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The limits for untrusted data beyond MaxDepth and claimed lengths (gaps checklist item 9, maintainer's decisions 2026-11-19): MaxObjects, MaxItems, MaxStringLength, MaxBinLength,
  /// unlimited by default, on in the HTTP serializer. An empty map is one byte that becomes a whole object: the size of the data alone does not limit the memory a read takes.
  /// </summary>
  public abstract class ResourceLimitTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    /// <summary>
    /// LsMsgPackDebug reads every value into its items, the skipped ones too: they count there.
    /// </summary>
    protected virtual bool CountsSkippedValues { get { return false; } }

    public class RlBig
    {
      public int A { get; set; }
      public string B { get; set; }
      public List<int> C { get; set; } = new List<int>();
    }

    public class RlHolder
    {
      public string Name { get; set; }
      public List<int> Numbers { get; set; }
    }

    public class RlHolderOld
    {
      public string Name { get; set; }
    }

    public class RlText
    {
      public string Text { get; set; }
      public byte[] Data { get; set; }
    }

    private static bool KeepTrack
    {
      get { return typeof(MsgPackSettings).GetProperty("PreservePackages") != null; }
    }

    /// <summary>
    /// The data is refused: a MsgPackException (LsMsgPackDebug under KEEPTRACK: an MpError item, as for MaxDepth).
    /// </summary>
    private void Refused<T>(byte[] bytes, MsgPackSettings settings, string message = null)
    {
      if (KeepTrack && Serializer is DebugSerializer) // errors become MpError items instead of exceptions
      {
        if (!settings.UseInexedSchema) // the schema is the first item there
          Assert.AreEqual("MpError", MsgPackItem.Unpack(new System.IO.MemoryStream(bytes), settings).GetType().Name);
        return;
      }
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<T>(bytes, settings));
      if (message != null)
        StringAssert.Contains(ex.Message, message);
    }

    private static MsgPackSettings Settings(bool useSchema = false)
    {
      return new MsgPackSettings() { UseInexedSchema = useSchema, ObjectLayout = ObjectLayout.Map };
    }

    /// <summary>
    /// An array of <paramref name="count"/> empty maps: 5 + count bytes.
    /// </summary>
    protected static byte[] EmptyMaps(int count)
    {
      byte[] bytes = new byte[5 + count];
      bytes[0] = 0xDD;
      bytes[1] = (byte)(count >> 24);
      bytes[2] = (byte)(count >> 16);
      bytes[3] = (byte)(count >> 8);
      bytes[4] = (byte)count;
      for (int t = 5; t < bytes.Length; t++)
        bytes[t] = 0x80;
      return bytes;
    }

    [TestMethod]
    public void Defaults_Unlimited()
    {
      MsgPackSettings settings = new MsgPackSettings();
      Assert.AreEqual(int.MaxValue, settings.MaxObjects);
      Assert.AreEqual(int.MaxValue, settings.MaxItems);
      Assert.AreEqual(int.MaxValue, settings.MaxStringLength);
      Assert.AreEqual(int.MaxValue, settings.MaxBinLength);
      Assert.AreEqual(int.MaxValue, new LsMsgPackOptions().MaxObjects);
      Assert.AreEqual(20000, Serializer.Deserialize<List<RlBig>>(EmptyMaps(20000), Settings()).Count);
    }

    /// <summary>
    /// Every array and map counts: the list and its 1000 objects are 1001.
    /// </summary>
    [TestMethod]
    public void MaxObjects_EmptyMapsAreObjects()
    {
      byte[] bytes = EmptyMaps(1000);
      MsgPackSettings settings = Settings();
      settings.MaxObjects = 1000;
      Refused<List<RlBig>>(bytes, settings, "MaxObjects (1000)");

      settings.MaxObjects = 1001;
      Assert.AreEqual(1000, Serializer.Deserialize<List<RlBig>>(bytes, settings).Count);
      Assert.AreEqual(1000, Serializer.Deserialize<List<RlBig>>(bytes, settings).Count, "per call");
      Assert.AreEqual(1000, ((List<object>)Serializer.Deserialize<List<object>>(bytes, settings)).Count, "untyped");
    }

    /// <summary>
    /// A limit for untrusted data is never skipped: ReadErrors does not apply.
    /// </summary>
    [TestMethod]
    public void MaxObjects_NotSkippedByReadErrors()
    {
      MsgPackSettings settings = Settings();
      settings.MaxObjects = 10;
      settings.ReadErrors = ReadErrorHandling.ReportAndContinue;
      Refused<List<RlBig>>(EmptyMaps(100), settings);
    }

    /// <summary>
    /// With the indexed schema the body counts, not the schema.
    /// </summary>
    [TestMethod]
    public void MaxObjects_BodyNotSchema()
    {
      List<RlBig> list = new List<RlBig>();
      for (int t = 0; t < 10; t++)
        list.Add(new RlBig() { A = t + 1, C = null });
      foreach (bool useSchema in new[] { false, true })
      {
        MsgPackSettings settings = Settings(useSchema);
        byte[] bytes = Serializer.Serialize(list, settings);
        settings.MaxObjects = 11;
        Assert.AreEqual(10, Serializer.Deserialize<List<RlBig>>(bytes, settings).Count, "schema " + useSchema);
        settings.MaxObjects = 10;
        Refused<List<RlBig>>(bytes, settings);
      }
    }

    [TestMethod]
    public void MaxItems_ElementsAndEntries()
    {
      MsgPackSettings settings = Settings();
      byte[] numbers = Serializer.Serialize(new List<int>(new int[100]), settings);
      byte[] entries = Serializer.Serialize(Entries(50), settings);
      settings.MaxItems = 99;
      Refused<List<int>>(numbers, settings, "MaxItems (99)");
      settings.MaxItems = 49;
      Refused<Dictionary<string, int>>(entries, settings);

      settings.MaxItems = 100;
      Assert.AreEqual(100, Serializer.Deserialize<List<int>>(numbers, settings).Count);
      settings.MaxItems = 50;
      Assert.AreEqual(50, Serializer.Deserialize<Dictionary<string, int>>(entries, settings).Count);
    }

    private static Dictionary<string, int> Entries(int count)
    {
      Dictionary<string, int> entries = new Dictionary<string, int>();
      for (int t = 0; t < count; t++)
        entries.Add("k" + t, t);
      return entries;
    }

    /// <summary>
    /// LsMsgPack does not count values it skips (members the class does not have): they make nothing.
    /// </summary>
    [TestMethod]
    public void MaxItems_SkippedValues()
    {
      MsgPackSettings settings = Settings();
      byte[] bytes = Serializer.Serialize(new RlHolder() { Name = "n", Numbers = new List<int>(new int[1000]) }, settings);
      settings.MaxItems = 10;
      if (CountsSkippedValues)
        Refused<RlHolderOld>(bytes, settings);
      else
        Assert.AreEqual("n", Serializer.Deserialize<RlHolderOld>(bytes, settings).Name);
    }

    [TestMethod]
    public void MaxStringLength_InBytes()
    {
      MsgPackSettings settings = Settings();
      byte[] bytes = Serializer.Serialize(new RlText() { Text = "ééééé" }, settings); // 10 bytes
      settings.MaxStringLength = 9;
      Refused<RlText>(bytes, settings, "MaxStringLength (9)");
      settings.MaxStringLength = 10;
      Assert.AreEqual("ééééé", Serializer.Deserialize<RlText>(bytes, settings).Text);
      Assert.AreEqual("ééééé", Serializer.Deserialize<string>(Serializer.Serialize("ééééé", settings), settings));

      settings.MaxStringLength = 3;
      Refused<object>(Serializer.Serialize("abcd", Settings()), settings);
      Refused<Dictionary<string, int>>(Serializer.Serialize(new Dictionary<string, int>() { { "abcd", 1 } }, Settings()), settings);
    }

    [TestMethod]
    public void MaxBinLength_BinAndExtensions()
    {
      MsgPackSettings settings = Settings();
      byte[] bytes = Serializer.Serialize(new RlText() { Data = new byte[10] }, settings);
      settings.MaxBinLength = 9;
      Refused<RlText>(bytes, settings, "MaxBinLength (9)");
      settings.MaxBinLength = 10;
      Assert.AreEqual(10, Serializer.Deserialize<RlText>(bytes, settings).Data.Length);
      Assert.AreEqual(10, ((byte[])Serializer.Deserialize<object>(Serializer.Serialize(new byte[10], Settings()), settings)).Length);

      settings.MaxBinLength = 0; // extensions of a fixed size are not checked: a DateTime is one of 8 bytes (DateTimeFormat.Native), a decimal of 16
      Assert.AreEqual(2026, Serializer.Deserialize<DateTime>(Serializer.Serialize(new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc), Settings()), settings).Year);
      Assert.AreEqual(1.5m, Serializer.Deserialize<decimal>(Serializer.Serialize(1.5m, Settings()), settings));
      Assert.AreEqual(1.5m, Serializer.Deserialize<object>(Serializer.Serialize<object>(1.5m, Settings()), settings));

      byte[] extension = new byte[] { 0xC7, 10, 42, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // ext 8 of 10 bytes, an unknown type
      settings.MaxBinLength = 9;
      Refused<object>(extension, settings);
      settings.MaxBinLength = 10;
      Assert.IsTrue(Serializer.IsUnknownExtension(Serializer.Deserialize<object>(extension, settings), out sbyte code, out byte[] data) && code == 42 && data.Length == 10);
    }

    /// <summary>
    /// LsMsgPack reads the copies of kept extension data again from the bytes: they count. LsMsgPackDebug copies its unpacked values (read once, counted once).
    /// </summary>
    protected virtual bool CountsCopies { get { return true; } }

    [TestMethod]
    public void MaxObjects_CopiesOfKeptValues()
    {
      if (KeepTrack && Serializer is DebugSerializer)
        return; // errors become MpError items
      MsgPackSettings settings = new MsgPackSettings() { ObjectLayout = ObjectLayout.Map, UseInexedSchema = false, ReferenceHandling = ReferenceHandling.Preserve };
      byte[] bytes = Serializer.Serialize(new ReferenceTests.RfChained() { Name = "c", Chain = ReferenceTests.Chain(4) }, settings); // 16 copies of 100 numbers when kept
      int shared = LeastMaxObjects<ReferenceTests.RfChained>(bytes, settings);
      int kept = LeastMaxObjects<ReferenceTests.RfChainedOld>(bytes, settings);
      if (CountsCopies)
        Assert.IsTrue(kept > shared + 10, shared + " " + kept);
      else
        Assert.IsTrue(kept >= shared, shared + " " + kept);
    }

    private int LeastMaxObjects<T>(byte[] bytes, MsgPackSettings settings)
    {
      for (int max = 1; max < 1000; max++)
      {
        settings.MaxObjects = max;
        try
        {
          Serializer.Deserialize<T>(bytes, settings);
          return max;
        }
        catch (MsgPackException ex) when (ex.Message.Contains("MaxObjects"))
        {
        }
      }
      throw new AssertFailedException("not read with 1000");
    }
  }

  [TestClass]
  public class DebugResourceLimitTests : ResourceLimitTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }

    protected override bool CountsSkippedValues { get { return true; } }

    protected override bool CountsCopies { get { return false; } }
  }

  [TestClass]
  public class LsResourceLimitTests : ResourceLimitTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }

    /// <summary>
    /// The HTTP serializer limits request bodies by default (maintainer's decision 2026-11-19): 1,000,000 arrays and maps, 10,000,000 elements and entries.
    /// </summary>
    [TestMethod]
    public void HttpSerializer_LimitsByDefault()
    {
      LsMsgPackHttpOptions options = new LsMsgPackHttpOptions();
      Assert.AreEqual(1000000, options.MaxObjects);
      Assert.AreEqual(10000000, options.MaxItems);
      Assert.AreEqual(int.MaxValue, options.MaxStringLength);
      LsMsgPackHttpSerializer http = new LsMsgPackHttpSerializer(options);
      LsMsgPackSerializer plain = http.For(MsgPackMediaTypes.MsgPack);
      Assert.AreEqual(999999, plain.Deserialize<List<RlBig>>(EmptyMaps(999999)).Count); // and the list: 1,000,000
      MsgPackException ex = Assert.Throws<MsgPackException>(() => plain.Deserialize<List<RlBig>>(EmptyMaps(1000000)));
      StringAssert.Contains(ex.Message, "MaxObjects (1000000)");

      LsMsgPackSerializer ls = http.For(MsgPackMediaTypes.XLsMsgPack); // the indexed schema
      List<RlBig> many = new List<RlBig>();
      for (int t = 0; t < 1000000; t++)
        many.Add(new RlBig() { C = null });
      Assert.Throws<MsgPackException>(() => ls.Deserialize<List<RlBig>>(ls.Serialize(many)));
      many.RemoveAt(0);
      Assert.AreEqual(999999, ls.Deserialize<List<RlBig>>(ls.Serialize(many)).Count);

      LsMsgPackSerializer unlimited = new LsMsgPackHttpSerializer(new LsMsgPackHttpOptions() { MaxObjects = int.MaxValue }).For(MsgPackMediaTypes.MsgPack);
      Assert.AreEqual(1000000, unlimited.Deserialize<List<RlBig>>(EmptyMaps(1000000)).Count);
    }
  }

  [TestClass]
  public class GenResourceLimitTests : ResourceLimitTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
