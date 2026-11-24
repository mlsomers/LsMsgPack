using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPackUnitTests;
using LsMsgPackDebug.Types.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyType;
using System;
using NB = Nerdbank.MessagePack;

namespace LsMsgPackInteropTests
{
  [GenerateShapeFor<Invoice>]
  [GenerateShapeFor<NerdbankFrameworkProbe>]
  [GenerateShapeFor<NerdbankEnums>]
  [GenerateShapeFor<NerdbankNames>]
  public partial class NerdbankShapes
  {
  }

  public enum NerdbankSize { Small, [EnumMemberShape(Name = "XL")] Large }

  /// <summary>
  /// Enums by name: Nerdbank's SerializeEnumValuesByName with PolyType's custom names (<c>[EnumMemberShape(Name = ...)]</c>) and LsMsgPack's EnumFormat.Name.
  /// </summary>
  public class NerdbankEnums
  {
    public NerdbankSize Size { get; set; }
    public UnitOfMeasure Unit { get; set; }
  }

  /// <summary>
  /// Names a naming policy converts (Nerdbank's PropertyNamingPolicy, LsMsgPack's PropertyNamingPolicy).
  /// </summary>
  public class NerdbankNames
  {
    public string FirstName { get; set; }
    public int Count { get; set; }
    public bool IsActive { get; set; }
  }

  /// <summary>
  /// Framework types both libraries write the same way.
  /// </summary>
  public class NerdbankFrameworkProbe
  {
    public Half Small { get; set; }
    public Version Release { get; set; }
    public System.Text.Rune Letter { get; set; }
    public System.Globalization.CultureInfo Culture { get; set; }
    public Memory<byte> Memory { get; set; }
    public ReadOnlyMemory<byte> ReadOnly { get; set; }
    public (int, string) Pair { get; set; }
    public Tuple<int, string> Tuple { get; set; }
    public DateTimeOffset When { get; set; }
    public System.Numerics.BigInteger Big { get; set; }
    public Int128 Wide { get; set; }
  }

  /// <summary>
  /// What Nerdbank.MessagePack (by the maintainer of MessagePack-CSharp) reads of LsMsgPack's output, and the other way around.
  /// <para>Like LsMsgPack without the schema it writes objects as maps keyed by property names (by default), the differences:</para>
  /// <list type="bullet">
  /// <item>Guid: extension type 2 in big-endian (RFC 4122) byte order. It also reads bin 16 in the order of Guid.ToByteArray(), what LsMsgPack writes.</item>
  /// <item>decimal: extension type 4 with the same 16 bytes as LsMsgPack's extension type 1 (Nerdbank uses type 1 for object references).</item>
  /// </list>
  /// </summary>
  public abstract class NerdbankTests
  {
    protected abstract LsMsgPackUnitTests.ISerializerUnderTest Serializer { get; }

    private static readonly NB.MessagePackSerializer Nerdbank = new NB.MessagePackSerializer();

    private static readonly NB.MessagePackSerializer NerdbankLsMsgPackDecimal = new NB.MessagePackSerializer()
    {
      LibraryExtensionTypeCodes = NB.LibraryReservedMessagePackExtensionTypeCode.Default with { Decimal = 1, ObjectReference = 10 }
    };

    private static readonly MsgPackSettings Named = new MsgPackSettings() { UseIndexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp, ObjectLayout = ObjectLayout.Map }; // Nerdbank reads maps keyed by names

    /// <summary>
    /// Also reads Nerdbank's Guid (extension type 2) and decimal (extension type 4), decimals are still written as extension type 1
    /// </summary>
    private static readonly MsgPackSettings NamedNerdbankExtensions = new MsgPackSettings()
    {
      UseIndexedSchema = false,
      DateTimeFormat = DateTimeFormat.Timestamp, // other libraries read timestamps
      ObjectLayout = ObjectLayout.Map,
      CustomExtensionTypes = new ICustomExt[] { new MpDecimal((MsgPackSettings)null), new MpDecimal((MsgPackSettings)null) { TypeSpecifier = 4 }, new NerdbankGuidExtension() }
    };

    private static readonly Invoice[] AllInvoices = Invoices.Create(100);

    [TestMethod]
    public void LsMsgPackPropertyNames_ReadByNerdbank()
    {
      foreach (Invoice invoice in AllInvoices)
        Same.AssertEqual(invoice, NerdbankLsMsgPackDecimal.Deserialize<Invoice, NerdbankShapes>(Serializer.Serialize(invoice, Named)), invoice.InvoiceNumber);

      // With its own extension type codes Nerdbank does not read LsMsgPack's decimal
      Assert.Throws<NB.MessagePackSerializationException>(() => Nerdbank.Deserialize<Invoice, NerdbankShapes>(Serializer.Serialize(AllInvoices[1], Named)));
    }

    [TestMethod]
    public void Nerdbank_ReadByLsMsgPack()
    {
      foreach (Invoice invoice in AllInvoices)
        Same.AssertEqual(invoice, Serializer.Deserialize<Invoice>(Nerdbank.Serialize<Invoice, NerdbankShapes>(invoice), NamedNerdbankExtensions), invoice.InvoiceNumber);
    }

    /// <summary>
    /// Without a custom extension for type 2 LsMsgPack refuses Nerdbank's Guid (it used to read the big-endian bytes as Guid.ToByteArray(): a different Guid, without an error).
    /// </summary>
    [TestMethod]
    public void NerdbankGuid_NeedsAnExtension()
    {
      byte[] bytes = NerdbankLsMsgPackDecimal.Serialize<Invoice, NerdbankShapes>(AllInvoices[1]); // only the Guid differs from what LsMsgPack reads

      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<Invoice>(bytes, Named));
      StringAssert.Contains(ex.Message, "extension type 2");
    }

    [TestMethod]
    public void SameBytes_FrameworkTypes()
    {
      NerdbankFrameworkProbe probe = new NerdbankFrameworkProbe()
      {
        Small = (Half)(-2.5),
        Release = new Version(4, 5, 6),
        Letter = new System.Text.Rune(0x1F600),
        Culture = System.Globalization.CultureInfo.GetCultureInfo("nl-NL"),
        Memory = new byte[] { 1, 2 },
        ReadOnly = new byte[] { 3 },
        Pair = (1, "one"),
        Tuple = System.Tuple.Create(2, "two"),
        When = new DateTimeOffset(2026, 6, 1, 10, 0, 0, 250, TimeSpan.FromMinutes(-330)), // [the moment, the offset in minutes], as LsMsgPack's default
        Big = -1234567, // integers when they fit in 64 bits (beyond: Nerdbank's extension type 3, LsMsgPack's -2)
        Wide = long.MaxValue
      };
      byte[] ls = Serializer.Serialize(probe, Named);
      byte[] nb = Nerdbank.Serialize<NerdbankFrameworkProbe, NerdbankShapes>(probe);
      CollectionAssert.AreEqual(nb, ls, $"LsMsgPack {Convert.ToHexString(ls)}, Nerdbank {Convert.ToHexString(nb)}");

      AssertProbe(probe, Serializer.Deserialize<NerdbankFrameworkProbe>(nb, Named));
      AssertProbe(probe, Nerdbank.Deserialize<NerdbankFrameworkProbe, NerdbankShapes>(ls));
    }

    private static void AssertProbe(NerdbankFrameworkProbe expected, NerdbankFrameworkProbe actual)
    {
      Assert.AreEqual(expected.Small, actual.Small);
      Assert.AreEqual(expected.Release, actual.Release);
      Assert.AreEqual(expected.Letter, actual.Letter);
      Assert.AreEqual(expected.Culture, actual.Culture);
      CollectionAssert.AreEqual(expected.Memory.ToArray(), actual.Memory.ToArray());
      CollectionAssert.AreEqual(expected.ReadOnly.ToArray(), actual.ReadOnly.ToArray());
      Assert.AreEqual(expected.Pair, actual.Pair);
      Assert.AreEqual(expected.Tuple, actual.Tuple);
      Assert.AreEqual(expected.When, actual.When);
      Assert.AreEqual(expected.When.Offset, actual.When.Offset);
      Assert.AreEqual(expected.Big, actual.Big);
      Assert.AreEqual(expected.Wide, actual.Wide);
    }

    /// <summary>
    /// Nerdbank's camelCase policy and LsMsgPack's: the same bytes, invoices read both ways.
    /// </summary>
    [TestMethod]
    public void SameBytes_CamelCaseNames()
    {
      NB.MessagePackSerializer camel = new NB.MessagePackSerializer() { PropertyNamingPolicy = NB.MessagePackNamingPolicy.CamelCase };
      MsgPackSettings names = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.CamelCase };
      NerdbankNames value = new NerdbankNames() { FirstName = "Ann", Count = 3, IsActive = true };
      byte[] nb = camel.Serialize<NerdbankNames, NerdbankShapes>(value);
      byte[] ls = Serializer.Serialize(value, names);
      CollectionAssert.AreEqual(nb, ls, $"LsMsgPack {Convert.ToHexString(ls)}, Nerdbank {Convert.ToHexString(nb)}");

      NB.MessagePackSerializer camelLsMsgPackDecimal = NerdbankLsMsgPackDecimal with { PropertyNamingPolicy = NB.MessagePackNamingPolicy.CamelCase };
      MsgPackSettings camelNamed = new MsgPackSettings() { UseIndexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.CamelCase };
      MsgPackSettings camelNerdbankExtensions = new MsgPackSettings()
      {
        UseIndexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp, ObjectLayout = ObjectLayout.Map, PropertyNamingPolicy = MsgPackNamingPolicy.CamelCase,
        CustomExtensionTypes = NamedNerdbankExtensions.CustomExtensionTypes
      };
      foreach (Invoice invoice in AllInvoices)
      {
        Same.AssertEqual(invoice, camelLsMsgPackDecimal.Deserialize<Invoice, NerdbankShapes>(Serializer.Serialize(invoice, camelNamed)), invoice.InvoiceNumber);
        Same.AssertEqual(invoice, Serializer.Deserialize<Invoice>(camel.Serialize<Invoice, NerdbankShapes>(invoice), camelNerdbankExtensions), invoice.InvoiceNumber);
      }
    }

    [TestMethod]
    public void SameBytes_EnumsByName()
    {
      NB.MessagePackSerializer byName = new NB.MessagePackSerializer() { SerializeEnumValuesByName = true };
      MsgPackSettings names = new MsgPackSettings() { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map, EnumFormat = EnumFormat.Name };
      NerdbankEnums value = new NerdbankEnums() { Size = NerdbankSize.Large, Unit = UnitOfMeasure.Hour };
      byte[] nb = byName.Serialize<NerdbankEnums, NerdbankShapes>(value);
      byte[] ls = Serializer.Serialize(value, names);
      CollectionAssert.AreEqual(nb, ls, $"LsMsgPack {Convert.ToHexString(ls)}, Nerdbank {Convert.ToHexString(nb)}");
      NerdbankEnums back = Serializer.Deserialize<NerdbankEnums>(nb, names);
      Assert.AreEqual(NerdbankSize.Large, back.Size);
      Assert.AreEqual(UnitOfMeasure.Hour, back.Unit);
      back = byName.Deserialize<NerdbankEnums, NerdbankShapes>(ls);
      Assert.AreEqual(NerdbankSize.Large, back.Size);
      Assert.AreEqual(UnitOfMeasure.Hour, back.Unit);
    }

    [TestMethod]
    public void IndexedSchema_NotReadable()
    {
      byte[] bytes = Serializer.Serialize(AllInvoices[1], new MsgPackSettings() { UseIndexedSchema = true });
      Assert.Throws<NB.MessagePackSerializationException>(() => NerdbankLsMsgPackDecimal.Deserialize<Invoice, NerdbankShapes>(bytes));
    }
  }

  [TestClass]
  public class DebugNerdbankTests : NerdbankTests
  {
    protected override LsMsgPackUnitTests.ISerializerUnderTest Serializer { get { return LsMsgPackUnitTests.Serializers.Debug; } }
  }

  [TestClass]
  public class LsNerdbankTests : NerdbankTests
  {
    protected override LsMsgPackUnitTests.ISerializerUnderTest Serializer { get { return LsMsgPackUnitTests.Serializers.Ls; } }
  }

  [TestClass]
  public class GenNerdbankTests : NerdbankTests
  {
    protected override LsMsgPackUnitTests.ISerializerUnderTest Serializer { get { return LsMsgPackUnitTests.Serializers.Gen; } }
  }
}
