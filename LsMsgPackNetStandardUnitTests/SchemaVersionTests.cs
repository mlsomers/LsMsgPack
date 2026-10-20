using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Version 2 of the indexed schema (<see cref="SchemaVersion.Described"/>): the declared type and default of each property and the type of the root, for readers without the classes.
  /// Readers take both versions.
  /// </summary>
  public abstract class SchemaVersionTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public SchemaVersionTests()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(SvShape));
    }

    public abstract class SvShape { public string Name { get; set; } }

    public class SvCircle : SvShape { public double Radius { get; set; } }

    public struct SvPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public class SvLine
    {
      public int Qty { get; set; }
      public string Text { get; set; }
    }

    public class SvOrder
    {
      public Guid Id { get; set; }
      public DateTime? Due { get; set; }
      public decimal Total { get; set; }
      public List<SvLine> Lines { get; set; }
      public SvPoint At { get; set; }
      [DefaultValue("NL")]
      public string Country { get; set; } = "NL";
      public SvShape Shape { get; set; }
      public Dictionary<string, int> Counts { get; set; }
      public object Anything { get; set; }
    }

    public class SvTiny
    {
      public int A { get; set; }
      public string B { get; set; }
      [DefaultValue(3)]
      public int C { get; set; } = 3;
    }

    private static readonly JsonSerializerSettings Utc = new JsonSerializerSettings() { DateTimeZoneHandling = DateTimeZoneHandling.Utc };

    private static SvOrder CreateOrder()
    {
      return new SvOrder()
      {
        Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
        Due = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc),
        Total = 12.5m,
        Lines = new List<SvLine>() { new SvLine() { Qty = 2, Text = "a" }, new SvLine() { Text = "b" } },
        At = new SvPoint() { X = 1, Y = 2 },
        Country = "BE",
        Shape = new SvCircle() { Name = "c", Radius = 1.5 },
        Counts = new Dictionary<string, int>() { { "k", 3 } },
        Anything = new List<int>() { 1, 2 }
      };
    }

    private static MsgPackSettings Described(ObjectLayout layout = ObjectLayout.Array)
    {
      return new MsgPackSettings() { SchemaVersion = SchemaVersion.Described, ObjectLayout = layout };
    }

    private static string Hex(byte[] bytes)
    {
      return BitConverter.ToString(bytes).Replace("-", " ");
    }

    /// <summary>
    /// Pins the format: <c>{ "SvTiny": [["A", "int"], ["B", "string"], ["C", "int", 3]], "": [["@", 0]] }</c> followed by the body.
    /// </summary>
    [TestMethod]
    public void Described_Format()
    {
      byte[] bytes = Serializer.Serialize(new SvTiny() { A = 1, B = "x" }, Described());
      string schema = "82 A6 53 76 54 69 6E 79 93 92 A1 41 A3 69 6E 74 92 A1 42 A6 73 74 72 69 6E 67 93 A1 43 A3 69 6E 74 03 A0 91 92 A1 40 00";
      string body = "93 01 A1 78 C0"; // C is 3, its default: left out
      Assert.AreEqual(schema + " " + body, Hex(bytes));
    }

    [TestMethod]
    public void Names_FormatUnchanged()
    {
      byte[] bytes = Serializer.Serialize(new SvTiny() { A = 1, B = "x" }, new MsgPackSettings());
      Assert.AreEqual("81 A6 53 76 54 69 6E 79 93 A1 41 A1 42 A1 43 93 01 A1 78 C0", Hex(bytes));
    }

    [TestMethod]
    [DataRow(ObjectLayout.Array)]
    [DataRow(ObjectLayout.Map)]
    public void Described_RoundTrip(ObjectLayout layout)
    {
      SvOrder order = CreateOrder();
      MsgPackSettings settings = Described(layout);
      SvOrder read = Serializer.Deserialize<SvOrder>(Serializer.Serialize(order, settings), settings);
      Assert.AreEqual(JsonConvert.SerializeObject(order, Utc), JsonConvert.SerializeObject(read, Utc));

      List<SvOrder> list = new List<SvOrder>() { order, new SvOrder() };
      List<SvOrder> readList = Serializer.Deserialize<List<SvOrder>>(Serializer.Serialize(list, settings), settings);
      Assert.AreEqual(JsonConvert.SerializeObject(list, Utc), JsonConvert.SerializeObject(readList, Utc));
    }

    /// <summary>
    /// The version only decides what is written: either is read with any settings.
    /// </summary>
    [TestMethod]
    public void ReadsBothVersions()
    {
      SvOrder order = CreateOrder();
      string expected = JsonConvert.SerializeObject(order, Utc);
      byte[] names = Serializer.Serialize(order, new MsgPackSettings());
      byte[] described = Serializer.Serialize(order, Described());
      Assert.AreNotEqual(Hex(names), Hex(described));

      Assert.AreEqual(expected, JsonConvert.SerializeObject(Serializer.Deserialize<SvOrder>(described, new MsgPackSettings()), Utc));
      Assert.AreEqual(expected, JsonConvert.SerializeObject(Serializer.Deserialize<SvOrder>(names, Described()), Utc));

      // The same store caches both inline schemas (different bytes)
      SchemaStore store = new SchemaStore();
      MsgPackSettings reader = new MsgPackSettings() { SchemaStore = store };
      for (int t = 0; t < 2; t++)
      {
        Assert.AreEqual(expected, JsonConvert.SerializeObject(Serializer.Deserialize<SvOrder>(described, reader), Utc));
        Assert.AreEqual(expected, JsonConvert.SerializeObject(Serializer.Deserialize<SvOrder>(names, reader), Utc));
      }
    }

    /// <summary>
    /// A schema reference: each version has its own schema (and id), a reader gets either from the writer's store.
    /// </summary>
    [TestMethod]
    public void Described_Reference()
    {
      SvOrder order = CreateOrder();
      SchemaStore writerStore = new SchemaStore();
      byte[] described = Serializer.Serialize(order, new MsgPackSettings() { SchemaVersion = SchemaVersion.Described, SchemaStore = writerStore, WriteSchemaReference = true });
      byte[] names = Serializer.Serialize(order, new MsgPackSettings() { SchemaStore = writerStore, WriteSchemaReference = true });
      Assert.AreEqual(2, writerStore.Count);

      SchemaId describedId = new SchemaId(described.Skip(2).Take(SchemaId.Length).ToArray());
      SchemaId namesId = new SchemaId(names.Skip(2).Take(SchemaId.Length).ToArray());
      Assert.AreNotEqual(describedId, namesId);
      Assert.AreEqual(describedId, SchemaId.Compute(writerStore.GetSchema(describedId)), "the version is read from the bytes");
      Assert.AreEqual(namesId, SchemaId.Compute(writerStore.GetSchema(namesId)));

      SchemaStore readerStore = new SchemaStore() { SchemaProvider = id => writerStore.GetSchema(id) };
      MsgPackSettings reader = new MsgPackSettings() { SchemaStore = readerStore };
      string expected = JsonConvert.SerializeObject(order, Utc);
      Assert.AreEqual(expected, JsonConvert.SerializeObject(Serializer.Deserialize<SvOrder>(described, reader), Utc));
      Assert.AreEqual(expected, JsonConvert.SerializeObject(Serializer.Deserialize<SvOrder>(names, reader), Utc));

      // Export and import keep the version (the ids are computed again)
      MemoryStream export = new MemoryStream();
      writerStore.Export(export);
      SchemaStore imported = new SchemaStore();
      export.Position = 0;
      Assert.AreEqual(2, imported.Import(export));
      Assert.IsTrue(imported.Contains(describedId));
      Assert.IsTrue(imported.Contains(namesId));
    }

    /// <summary>
    /// The same schema inline (lengths and numbers in the byte order of the settings) as the store has it in the byte order of the specification.
    /// </summary>
    [TestMethod]
    [DataRow(EndianAction.NeverSwap)]
    [DataRow(EndianAction.AlwaysSwap)]
    public void Described_OtherByteOrder(EndianAction endian)
    {
      SvTimed timed = new SvTimed() { When = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc), Ratio = 0.5 };
      MsgPackSettings inline = new MsgPackSettings() { SchemaVersion = SchemaVersion.Described, EndianAction = endian };
      byte[] inlineBytes = Serializer.Serialize(timed, inline);
      Assert.AreEqual(JsonConvert.SerializeObject(timed, Utc), JsonConvert.SerializeObject(Serializer.Deserialize<SvTimed>(inlineBytes, inline), Utc));

      SchemaStore store = new SchemaStore();
      byte[] referenced = Serializer.Serialize(timed, new MsgPackSettings() { SchemaVersion = SchemaVersion.Described, EndianAction = endian, SchemaStore = store, WriteSchemaReference = true });
      SchemaId id = new SchemaId(referenced.Skip(2).Take(SchemaId.Length).ToArray());
      byte[] schema = store.GetInlineSchema(id, inline);
      Assert.AreEqual(Hex(inlineBytes.Take(schema.Length).ToArray()), Hex(schema));

      SchemaStore other = new SchemaStore();
      Assert.AreEqual(id, other.RegisterInline(inlineBytes, 0, inlineBytes.Length, inline), "back in the byte order of the specification: the same id");
    }

    public class SvTimed
    {
      [DefaultValue(typeof(DateTime), "2026-01-02T03:04:05Z")]
      public DateTime When { get; set; } = DateTime.Parse("2026-01-02T03:04:05Z", System.Globalization.CultureInfo.InvariantCulture); // as the attribute converts it
      [DefaultValue(1234567.5)]
      public double Ratio { get; set; } = 1234567.5;
      [DefaultValue(70000)]
      public int Value { get; set; } = 70000; // left out, a reader keeps what the constructor sets
    }

    [TestMethod]
    [DataRow("81 A6 53 76 54 69 6E 79 91 92 A1 41 A3 69 6E 74 C0", DisplayName = "described property without the root entry")]
    [DataRow("82 A0 91 92 A1 40 00 A6 53 76 54 69 6E 79 91 A1 41 C0", DisplayName = "root entry not last")]
    [DataRow("82 A6 53 76 54 69 6E 79 91 91 A1 41 A0 91 92 A1 40 00 C0", DisplayName = "property without a type")]
    [DataRow("82 A6 53 76 54 69 6E 79 92 A1 41 92 A1 40 A3 69 6E 74 A0 91 92 A1 40 00 C0", DisplayName = "content item not first")]
    [DataRow("82 A6 53 76 54 69 6E 79 91 A1 41 A0 91 A1 40 C0", DisplayName = "root entry without its type")]
    public void Described_InvalidSchema_IsRefused(string hex)
    {
      byte[] bytes = hex.Split(' ').Select(h => Convert.ToByte(h, 16)).ToArray();
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<SvTiny>(bytes, new MsgPackSettings()));
    }
  }

  [TestClass]
  public class LsSchemaVersionTests : SchemaVersionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSchemaVersionTests : SchemaVersionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenSchemaVersionTests : SchemaVersionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
