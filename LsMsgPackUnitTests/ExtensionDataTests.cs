using LsMsgPack;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Extension data (docs/ExtensionData.md): an older class reads the members of a newer one it does not have into its MsgPackExtensionData or [MsgPackExtensionData] dictionary
  /// and writes them again, so the newer class reads them back.
  /// </summary>
  public abstract class ExtensionDataTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    #region Model

    public class XdAddress
    {
      public string Street { get; set; }
      public string City { get; set; }
    }

    public abstract class XdShape
    {
      public string Color { get; set; }
    }

    public class XdCircle : XdShape
    {
      public double Radius { get; set; }
    }

    /// <summary>The newer version.</summary>
    public class XdOrder
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      public decimal Discount { get; set; }
      public List<string> Tags { get; set; }
      public XdAddress ShipTo { get; set; }
      public List<XdAddress> Stops { get; set; }
      public XdShape Shape { get; set; }
      public double Weight { get; set; }
      public float Ratio { get; set; }
      public long Big { get; set; }
      public DateTime Due { get; set; }
      public byte[] Blob { get; set; }
      public Dictionary<string, int> Counts { get; set; }
    }

    /// <summary>The older version: the first two properties.</summary>
    public class XdOrderOld
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      public MsgPackExtensionData Extra { get; set; }
    }

    public class XdOrderOldDictionary
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      [MsgPackExtensionData]
      public Dictionary<string, object> Extra { get; set; }
    }

    public class XdOrderOldJson
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      [System.Text.Json.Serialization.JsonExtensionData]
      public IDictionary<string, object> Extra { get; } = new Dictionary<string, object>();
    }

    /// <summary>Leaves only (schema version 1 keeps those).</summary>
    public class XdFlat
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      public decimal Discount { get; set; }
      public double Weight { get; set; }
      public DateTime Due { get; set; }
      public byte[] Blob { get; set; }
      public string Note { get; set; }
    }

    public class XdFlatOld
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      public MsgPackExtensionData Extra { get; set; }
    }

    public class XdTwoMembers
    {
      public int Id { get; set; }
      public MsgPackExtensionData One { get; set; }
      [MsgPackExtensionData]
      public Dictionary<string, object> Two { get; set; }
    }

    public class XdDocument
    {
      public int Id { get; set; }
      public MsgPackExtensionData Kept { get; set; }
    }

    /// <summary>A second member next to the base class's (analyzer LSMP011).</summary>
    public class XdInvoice : XdDocument
    {
      public MsgPackExtensionData More { get; set; }
    }

    /// <summary>A marked member of a type the runtime can't fill (analyzer LSMP011).</summary>
    public class XdJsonElements
    {
      public int Id { get; set; }
      [System.Text.Json.Serialization.JsonExtensionData]
      public Dictionary<string, System.Text.Json.JsonElement> Extra { get; set; }
    }

    public class XdBag : Dictionary<string, object> { }

    /// <summary>A struct keeps values too (written boxed once).</summary>
    public struct XdPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
      public MsgPackExtensionData Extra { get; set; }
    }

    public struct XdPoint3
    {
      public int X { get; set; }
      public int Y { get; set; }
      public string Label { get; set; }
    }

    /// <summary>A class of its own implementing the dictionary: created when the member is null.</summary>
    public class XdOrderOldBag
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      [MsgPackExtensionData]
      public XdBag Extra { get; set; }
    }

    /// <summary>Several objects with kept values (other names per object), one declared as object (a type id).</summary>
    public class XdBatch
    {
      public List<XdOrderOld> Orders { get; set; }
      public object Any { get; set; }
      public XdFlatOld Flat { get; set; }
    }

    #endregion

    /// <summary>
    /// Objects with kept values for CrossLibraryTests: read by LsMsgPackDebug from data written by names and without the schema (by position), values set in code.
    /// </summary>
    public static IEnumerable<object> Corpus()
    {
      XdOrder order = NewOrder();
      MsgPackSettings names = Settings("names", ObjectLayout.Map);
      MsgPackSettings positions = Settings("none", ObjectLayout.Array);
      XdOrderOld byName = MsgPackSerializer.Deserialize<XdOrderOld>(MsgPackSerializer.Serialize(order, names), names);
      XdOrderOld byPosition = MsgPackSerializer.Deserialize<XdOrderOld>(MsgPackSerializer.Serialize(order, positions), positions);
      XdOrderOldDictionary dictionary = MsgPackSerializer.Deserialize<XdOrderOldDictionary>(MsgPackSerializer.Serialize(order, names), names);
      XdOrderOld other = new XdOrderOld { Id = 2, Customer = "Cy", Extra = new MsgPackExtensionData() };
      other.Extra.SetRaw("Note", new byte[] { 0xA2, (byte)'h', (byte)'i' });
      other.Extra.SetRaw("Discount", new byte[] { 0x05 });
      XdFlatOld flat = MsgPackSerializer.Deserialize<XdFlatOld>(MsgPackSerializer.Serialize(new XdFlat { Id = 1, Customer = "Ann", Discount = 1.25m, Weight = 2.5, Due = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Blob = new byte[] { 9 }, Note = "n" }, names), names);

      yield return byName;
      yield return byPosition;
      yield return dictionary;
      yield return other;
      yield return flat;
      yield return new XdOrderOldDictionary { Id = 4, Customer = "Dee", Extra = new Dictionary<string, object> { { "Discount", 2.5m }, { "Tags", new List<string> { "x" } }, { "ShipTo", new XdAddress { Street = "Elm" } }, { "Weight", 1.5 }, { "Id", 9 }, { "Big", 300 } } };
      yield return new XdBatch { Orders = new List<XdOrderOld> { byName, other, byPosition, new XdOrderOld { Id = 5 } }, Any = other, Flat = flat };
    }


    public static XdOrder NewOrder()
    {
      return new XdOrder
      {
        Id = 7,
        Customer = "Ann",
        Discount = 12.5m,
        Tags = new List<string> { "a", "b" },
        ShipTo = new XdAddress { Street = "Main st 1", City = "Town" },
        Stops = new List<XdAddress> { new XdAddress { Street = "S1", City = "C1" }, new XdAddress { Street = "S2" } },
        Shape = new XdCircle { Color = "red", Radius = 2.5 },
        Weight = 3.25,
        Ratio = 0.5f,
        Big = long.MaxValue - 5,
        Due = new DateTime(2026, 11, 15, 10, 30, 0, 123, DateTimeKind.Utc),
        Blob = new byte[] { 1, 2, 3 },
        Counts = new Dictionary<string, int> { { "x", 1 }, { "y", 300 } }
      };
    }

    private static void AssertSameExceptCustomer(XdOrder expected, XdOrder actual, string customer)
    {
      Assert.AreEqual(expected.Id, actual.Id);
      Assert.AreEqual(customer, actual.Customer);
      Assert.AreEqual(expected.Discount, actual.Discount);
      CollectionAssert.AreEqual(expected.Tags, actual.Tags);
      Assert.AreEqual(expected.ShipTo.Street, actual.ShipTo.Street);
      Assert.AreEqual(expected.ShipTo.City, actual.ShipTo.City);
      Assert.HasCount(expected.Stops.Count, actual.Stops);
      for (int t = 0; t < expected.Stops.Count; t++)
      {
        Assert.AreEqual(expected.Stops[t].Street, actual.Stops[t].Street);
        Assert.AreEqual(expected.Stops[t].City, actual.Stops[t].City);
      }
      Assert.IsInstanceOfType<XdCircle>(actual.Shape);
      Assert.AreEqual("red", actual.Shape.Color);
      Assert.AreEqual(2.5, ((XdCircle)actual.Shape).Radius);
      Assert.AreEqual(expected.Weight, actual.Weight);
      Assert.AreEqual(expected.Ratio, actual.Ratio);
      Assert.AreEqual(expected.Big, actual.Big);
      Assert.AreEqual(expected.Due, actual.Due.ToUniversalTime());
      CollectionAssert.AreEqual(expected.Blob, actual.Blob);
      CollectionAssert.AreEquivalent(expected.Counts.ToList(), actual.Counts.ToList());
    }

    public static IEnumerable<object[]> FullModes()
    {
      yield return new object[] { "names", ObjectLayout.Map };
      yield return new object[] { "none", ObjectLayout.Array };
      yield return new object[] { "v2", ObjectLayout.Map };
      yield return new object[] { "v2", ObjectLayout.Array };
      yield return new object[] { "v2-reference", ObjectLayout.Map };
      yield return new object[] { "v2-reference", ObjectLayout.Array };
    }

    public static MsgPackSettings Settings(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = new MsgPackSettings { ObjectLayout = layout };
      if (mode == "names" || mode == "none")
        settings.UseIndexedSchema = false;
      else
      {
        settings.SchemaVersion = mode.StartsWith("v1", StringComparison.Ordinal) ? SchemaVersion.Names : SchemaVersion.Described;
        if (mode.EndsWith("-reference", StringComparison.Ordinal))
        {
          settings.SchemaStore = new SchemaStore();
          settings.WriteSchemaReference = true;
        }
      }
      return settings;
    }

    /// <summary>
    /// The older class keeps what it does not have, and writes it again: the newer class reads everything back (the older one changed its own property).
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(FullModes))]
    public void OlderClass_KeepsAndWritesBack(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      XdOrder order = NewOrder();
      byte[] data = Serializer.Serialize(order, settings);

      XdOrderOld old = Serializer.Deserialize<XdOrderOld>(data, settings);
      Assert.AreEqual(7, old.Id);
      Assert.IsNotNull(old.Extra);
      Assert.AreEqual(11, old.Extra.Count);
      if (mode == "none")
        CollectionAssert.AreEqual(Enumerable.Range(2, 11).ToList(), old.Extra.Positions.ToList());
      else
        CollectionAssert.AreEqual(new[] { "Discount", "Tags", "ShipTo", "Stops", "Shape", "Weight", "Ratio", "Big", "Due", "Blob", "Counts" }, old.Extra.Names.ToList());

      old.Customer = "Bob";
      byte[] again = Serializer.Serialize(old, settings);
      XdOrder back = Serializer.Deserialize<XdOrder>(again, settings);
      AssertSameExceptCustomer(order, back, "Bob");
    }

    /// <summary>
    /// Kept values pass through several older services: the second one reads the objects the first one wrote by names (with a type id that is a name, not an entry), and the newer class still reads them.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(FullModes))]
    public void OlderClass_TwoHops(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      XdOrder order = NewOrder();
      XdOrderOld first = Serializer.Deserialize<XdOrderOld>(Serializer.Serialize(order, settings), settings);
      first.Customer = "Bob";
      XdOrderOld second = Serializer.Deserialize<XdOrderOld>(Serializer.Serialize(first, settings), settings);
      Assert.AreEqual(11, second.Extra.Count);
      second.Customer = "Cy";
      XdOrder back = Serializer.Deserialize<XdOrder>(Serializer.Serialize(second, settings), settings);
      AssertSameExceptCustomer(order, back, "Cy");
    }

    [TestMethod]
    [DynamicData(nameof(FullModes))]
    public void OlderClass_Differences_KeptNotUnknown(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      byte[] data = Serializer.Serialize(NewOrder(), settings);

      XdOrderOld old = Serializer.Deserialize<XdOrderOld>(data, settings, out ReadDifferences differences);
      Assert.IsNotNull(differences);
      Assert.AreEqual(0, differences.Differences.Count(d => d.Kind != DifferenceKind.KeptProperty), differences.GenerateReport());
      Assert.AreEqual(11, differences.Differences.Count(d => d.Kind == DifferenceKind.KeptProperty));
      Assert.AreEqual(11, old.Extra.Count);
      StringAssert.Contains(differences.GenerateReport(), "kept as extension data");
    }

    public static IEnumerable<object[]> NamedModes()
    {
      return FullModes().Where(m => (string)m[0] != "none");
    }

    /// <summary>
    /// A dictionary gets plain values (objects as dictionaries of their names) and writes them again.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(NamedModes))]
    public void Dictionary_KeepsAndWritesBack(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      XdOrder order = NewOrder();
      byte[] data = Serializer.Serialize(order, settings);

      XdOrderOldDictionary old = Serializer.Deserialize<XdOrderOldDictionary>(data, settings);
      Assert.HasCount(11, old.Extra);
      Assert.AreEqual("Town", ((Dictionary<string, object>)old.Extra["ShipTo"])["City"]);
      CollectionAssert.AreEqual(new object[] { "a", "b" }, (object[])old.Extra["Tags"]);

      old.Customer = "Bob";
      XdOrder back = Serializer.Deserialize<XdOrder>(Serializer.Serialize(old, settings), settings);
      AssertSameExceptCustomer(order, back, "Bob");

      XdOrderOldJson json = Serializer.Deserialize<XdOrderOldJson>(data, settings);
      Assert.HasCount(11, json.Extra);
      json.Customer = "Cy";
      back = Serializer.Deserialize<XdOrder>(Serializer.Serialize(json, settings), settings);
      AssertSameExceptCustomer(order, back, "Cy");
    }

    /// <summary>
    /// Values set in code are written like any value: a number in the smallest format, a class by its names.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(NamedModes))]
    public void Dictionary_ValuesSetInCode(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      XdOrderOldDictionary old = new XdOrderOldDictionary
      {
        Id = 3,
        Customer = "Dee",
        Extra = new Dictionary<string, object>
        {
          { "Discount", 2.5m },
          { "Tags", new List<string> { "x" } },
          { "ShipTo", new XdAddress { Street = "Elm", City = "Ville" } },
          { "Weight", 1.5 },
          { "Id", 99 }, // a property of the class: it wins
          { "", "reserved" }
        }
      };
      XdOrder read = Serializer.Deserialize<XdOrder>(Serializer.Serialize(old, settings), settings);
      Assert.AreEqual(3, read.Id);
      Assert.AreEqual(2.5m, read.Discount);
      CollectionAssert.AreEqual(new[] { "x" }, read.Tags);
      Assert.AreEqual("Ville", read.ShipTo.City);
      Assert.AreEqual(1.5, read.Weight);
    }

    /// <summary>
    /// Version 1 of the schema (the default) keeps leaves; an array or a map is an error (its objects cannot be told apart), which ReadErrors decides on.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void SchemaVersion1_KeepsLeaves(ObjectLayout layout)
    {
      MsgPackSettings settings = Settings("v1", layout);
      XdFlat flat = new XdFlat { Id = 1, Customer = "Ann", Discount = 1.25m, Weight = 2.5, Due = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Blob = new byte[] { 9 }, Note = "n" };
      XdFlatOld old = Serializer.Deserialize<XdFlatOld>(Serializer.Serialize(flat, settings), settings);
      Assert.AreEqual(5, old.Extra.Count);
      old.Customer = "Bob";
      XdFlat back = Serializer.Deserialize<XdFlat>(Serializer.Serialize(old, settings), settings);
      Assert.AreEqual("Bob", back.Customer);
      Assert.AreEqual(1.25m, back.Discount);
      Assert.AreEqual(2.5, back.Weight);
      Assert.AreEqual(flat.Due, back.Due.ToUniversalTime());
      CollectionAssert.AreEqual(flat.Blob, back.Blob);
      Assert.AreEqual("n", back.Note);
    }

    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void SchemaVersion1_ArrayOrMap_IsAnError(ObjectLayout layout)
    {
      MsgPackSettings settings = Settings("v1", layout);
      byte[] data = Serializer.Serialize(NewOrder(), settings);
      Exception error = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<XdOrderOld>(data, settings));
      StringAssert.Contains(error.Message, "SchemaVersion.Described");

      settings.ReadErrors = ReadErrorHandling.ReportAndContinue;
      XdOrderOld old = Serializer.Deserialize<XdOrderOld>(data, settings, out ReadDifferences differences);
      CollectionAssert.AreEqual(new[] { "Discount", "Weight", "Ratio", "Big", "Due", "Blob" }, old.Extra.Names.ToList());
      CollectionAssert.AreEquivalent(new[] { "Tags", "ShipTo", "Stops", "Shape", "Counts" }, differences.Differences.Where(d => d.Kind == DifferenceKind.InvalidValue).Select(d => d.Name).ToList());
    }

    /// <summary>
    /// What the analyzer reports as LSMP011 (LsMsgPack.AnalyzerTests ExtensionData_InvalidMembers) the runtime refuses, what it doesn't report works.
    /// </summary>
    [TestMethod]
    public void InvalidMembers_Throw()
    {
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new XdTwoMembers { Id = 1 }, new MsgPackSettings()));
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new XdInvoice { Id = 1 }, new MsgPackSettings()));
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new XdJsonElements { Id = 1 }, new MsgPackSettings()));
      Assert.AreEqual(1, Serializer.Deserialize<XdDocument>(Serializer.Serialize(new XdDocument { Id = 1 }, new MsgPackSettings()), new MsgPackSettings()).Id);

      MsgPackSettings settings = Settings("names", ObjectLayout.Map);
      XdOrderOldBag bag = Serializer.Deserialize<XdOrderOldBag>(Serializer.Serialize(NewOrder(), settings), settings);
      Assert.IsInstanceOfType<XdBag>(bag.Extra);
      Assert.HasCount(11, bag.Extra);
    }

    /// <summary>
    /// Raw values set in code are written as they are; a value that is not exactly one MsgPack value is refused.
    /// </summary>
    [TestMethod]
    public void SetRaw_WrittenAsItIs()
    {
      MsgPackSettings settings = Settings("names", ObjectLayout.Map);
      XdOrderOld old = new XdOrderOld { Id = 1, Customer = "Ann", Extra = new MsgPackExtensionData() };
      old.Extra.SetRaw("Weight", new byte[] { 0xCB, 0x40, 0x04, 0, 0, 0, 0, 0, 0 }); // 2.5
      old.Extra.SetRaw("Tags", new byte[] { 0x91, 0xA1, (byte)'q' });
      Assert.ThrowsExactly<ArgumentException>(() => old.Extra.SetRaw("Note", new byte[] { 0x92, 0x01 }));
      Assert.ThrowsExactly<ArgumentException>(() => old.Extra.SetRaw("Note", new byte[] { 0x01, 0x02 }));
      Assert.ThrowsExactly<ArgumentException>(() => old.Extra.SetRaw("@", new byte[] { 0x01 }));
      XdOrder read = Serializer.Deserialize<XdOrder>(Serializer.Serialize(old, settings), settings);
      Assert.AreEqual(2.5, read.Weight);
      CollectionAssert.AreEqual(new[] { "q" }, read.Tags);
    }

    /// <summary>
    /// The values keep their width (an int32 stays 5 bytes when it was written that way), so the bytes written again equal the bytes read.
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Values_KeepTheirFormat(bool compact)
    {
      MsgPackSettings settings = Settings("names", ObjectLayout.Map);
      settings.DynamicallyCompact = compact;
      XdFlat flat = new XdFlat { Id = 1, Customer = "Ann", Discount = 1.25m, Weight = 2.5, Due = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Blob = new byte[] { 9 }, Note = "n" };
      byte[] data = Serializer.Serialize(flat, settings);
      XdFlatOld old = Serializer.Deserialize<XdFlatOld>(data, settings);
      byte[] again = Serializer.Serialize(old, settings);
      CollectionAssert.AreEqual(data, again);
    }
  }

  [TestClass]
  public class DebugExtensionDataTests : ExtensionDataTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsExtensionDataTests : ExtensionDataTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }

    public static IEnumerable<object[]> Layouts()
    {
      foreach (string mode in new[] { "names", "none", "v2", "v1" })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Map, ObjectLayout.Array })
          foreach (bool trim in new[] { false, true })
            yield return new object[] { mode, layout, trim };
    }

    /// <summary>
    /// A class (and a struct) with extension data is written by its compiled writer after the threshold (counted by ObjectPlan.WriteExtended), then the kept values: the same bytes as the runtime's writers.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void CompiledWriter_UsedForExtensionData(string mode, ObjectLayout layout, bool trim)
    {
      MsgPackSettings settings = Settings(mode, layout);
      settings.TrimTrailingNulls = trim;
      LsMsgPackOptions options = new LsMsgPackOptions { ObjectLayout = layout, UseIndexedSchema = settings.UseIndexedSchema, SchemaVersion = settings.SchemaVersion, TrimTrailingNulls = trim, UseRegisteredContexts = false, CompileWritersAfter = 3 };
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(options);
      XdFlat flat = new XdFlat { Id = 1, Customer = "Ann", Discount = 1.25m, Weight = 2.5, Due = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Note = "n" };
      XdFlatOld old = serializer.Deserialize<XdFlatOld>(serializer.Serialize(flat));
      old.Customer = null; // a nil before the kept values (trimmed arrays)
      XdPoint point = serializer.Deserialize<XdPoint>(serializer.Serialize(new XdPoint3 { X = 1, Y = 0, Label = "p" }));

      byte[] runtime = serializer.Serialize(old);
      byte[] runtimePoint = serializer.Serialize(point);
      bool names = !settings.UseIndexedSchema;
      for (int t = 0; t < 5 && (CompiledWriterTests.WriterOf(serializer, typeof(XdFlatOld), names) is null || CompiledWriterTests.WriterOf(serializer, typeof(XdPoint), names) is null); t++)
      {
        serializer.Serialize(old);
        serializer.Serialize(point);
      }
      System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
      while ((CompiledWriterTests.WriterOf(serializer, typeof(XdFlatOld), names) is null || CompiledWriterTests.WriterOf(serializer, typeof(XdPoint), names) is null) && waited.ElapsedMilliseconds < 10000)
        System.Threading.Thread.Sleep(5);
      Assert.AreEqual("CompiledObjectCode", CompiledWriterTests.WriterOf(serializer, typeof(XdFlatOld), names));
      Assert.AreEqual("CompiledStructCode", CompiledWriterTests.WriterOf(serializer, typeof(XdPoint), names));
      CollectionAssert.AreEqual(runtime, serializer.Serialize(old));
      CollectionAssert.AreEqual(runtimePoint, serializer.Serialize(point));
      CollectionAssert.AreEqual(MsgPackSerializer.Serialize(old, settings), serializer.Serialize(old), "LsMsgPackDebug's bytes");

      XdFlat back = serializer.Deserialize<XdFlat>(serializer.Serialize(old));
      Assert.AreEqual(1.25m, back.Discount);
      Assert.AreEqual("n", back.Note);
      Assert.IsNull(back.Customer);
      if (mode != "none")
        Assert.AreEqual("p", serializer.Deserialize<XdPoint3>(serializer.Serialize(point)).Label);
    }
  }

  [TestClass]
  public class GenExtensionDataTests : ExtensionDataTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
