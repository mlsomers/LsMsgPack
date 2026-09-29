using LsMsgPack;
using LsMsgPack.Types.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyType;
using NB = Nerdbank.MessagePack;

namespace LsMsgPackInteropTests
{
  [GenerateShapeFor<Invoice>]
  public partial class NerdbankShapes
  {
  }

  /// <summary>
  /// What Nerdbank.MessagePack (by the maintainer of MessagePack-CSharp) reads of LsMsgPack's output, and the other way around.
  /// <para>Like LsMsgPack without the schema it writes objects as maps keyed by property names (by default), the differences:</para>
  /// <list type="bullet">
  /// <item>Guid: extension type 2 in big-endian (RFC 4122) byte order. It also reads bin 16 in the order of Guid.ToByteArray(), what LsMsgPack writes.</item>
  /// <item>decimal: extension type 4 with the same 16 bytes as LsMsgPack's extension type 1 (Nerdbank uses type 1 for object references).</item>
  /// </list>
  /// </summary>
  [TestClass]
  public class NerdbankTests
  {
    private static readonly NB.MessagePackSerializer Nerdbank = new NB.MessagePackSerializer();

    private static readonly NB.MessagePackSerializer NerdbankLsMsgPackDecimal = new NB.MessagePackSerializer()
    {
      LibraryExtensionTypeCodes = NB.LibraryReservedMessagePackExtensionTypeCode.Default with { Decimal = 1, ObjectReference = 10 }
    };

    private static readonly MsgPackSettings Named = new MsgPackSettings() { UseInexedSchema = false };

    /// <summary>
    /// Also reads Nerdbank's Guid (extension type 2) and decimal (extension type 4), decimals are still written as extension type 1
    /// </summary>
    private static readonly MsgPackSettings NamedNerdbankExtensions = new MsgPackSettings()
    {
      UseInexedSchema = false,
      CustomExtentionTypes = new ICustomExt[] { new MpDecimal((MsgPackSettings)null), new MpDecimal((MsgPackSettings)null) { TypeSpecifier = 4 }, new NerdbankGuidExtension() }
    };

    private static readonly Invoice[] AllInvoices = Invoices.Create(100);

    [TestMethod]
    public void LsMsgPackPropertyNames_ReadByNerdbank()
    {
      foreach (Invoice invoice in AllInvoices)
        Same.AssertEqual(invoice, NerdbankLsMsgPackDecimal.Deserialize<Invoice, NerdbankShapes>(MsgPackSerializer.Serialize(invoice, Named)), invoice.InvoiceNumber);

      // With its own extension type codes Nerdbank does not read LsMsgPack's decimal
      Assert.Throws<NB.MessagePackSerializationException>(() => Nerdbank.Deserialize<Invoice, NerdbankShapes>(MsgPackSerializer.Serialize(AllInvoices[1], Named)));
    }

    [TestMethod]
    public void Nerdbank_ReadByLsMsgPack()
    {
      foreach (Invoice invoice in AllInvoices)
        Same.AssertEqual(invoice, MsgPackSerializer.Deserialize<Invoice>(Nerdbank.Serialize<Invoice, NerdbankShapes>(invoice), NamedNerdbankExtensions), invoice.InvoiceNumber);
    }

    /// <summary>
    /// Without a custom extension for type 2 LsMsgPack refuses Nerdbank's Guid (it used to read the big-endian bytes as Guid.ToByteArray(): a different Guid, without an error).
    /// </summary>
    [TestMethod]
    public void NerdbankGuid_NeedsAnExtension()
    {
      byte[] bytes = NerdbankLsMsgPackDecimal.Serialize<Invoice, NerdbankShapes>(AllInvoices[1]); // only the Guid differs from what LsMsgPack reads

      MsgPackException ex = Assert.Throws<MsgPackException>(() => MsgPackSerializer.Deserialize<Invoice>(bytes, Named));
      StringAssert.Contains(ex.Message, "extension type 2");
    }

    [TestMethod]
    public void IndexedSchema_NotReadable()
    {
      byte[] bytes = MsgPackSerializer.Serialize(AllInvoices[1], new MsgPackSettings() { UseInexedSchema = true });
      Assert.Throws<NB.MessagePackSerializationException>(() => NerdbankLsMsgPackDecimal.Deserialize<Invoice, NerdbankShapes>(bytes));
    }
  }
}
