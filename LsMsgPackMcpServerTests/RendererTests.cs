using LsMsgPack;
using LsMsgPackMcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LsMsgPackMcpServerTests
{
  /// <summary>
  /// What an agent reads: the objects view, the items view, errors, offsets and search.
  /// </summary>
  [TestClass]
  public class RendererTests
  {
    private static string Render(byte[] bytes, RenderOptions options = null, SchemaStore store = null)
    {
      PayloadDocument doc = new PayloadDocument(bytes, "test", new DecodeOptions() { Schemas = store }) { Id = "doc1" };
      return TextRenderer.Render(doc, options ?? new RenderOptions());
    }

    private static void AssertContains(string text, string expected)
    {
      Assert.IsTrue(text.Contains(expected, StringComparison.Ordinal), $"Expected \"{expected}\" in:\n{text}");
    }

    [TestMethod]
    public void InlineSchemaArrayLayout_NamesAndInferredTypes()
    {
      string text = Render(Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true));
      AssertContains(text, "indexed schema (inline, 3 types) followed by the body");
      AssertContains(text, "#0 McpInvoice: Id, Customer, Billing, Lines, Date, Reference, Tags");
      AssertContains(text, "{ // McpInvoice~");
      AssertContains(text, "\"Billing\": { \"Street\": \"Main street\", \"Number\": 12, \"City\": \"Amsterdam\" }, // McpAddress~");
      AssertContains(text, "{ \"Product\": \"Apples\", \"Quantity\": 3, \"Price\": 1.25 }, // McpLine~, Price: decimal");
      AssertContains(text, "\"Date\": \"2026-10-05T12:30:00Z\", // timestamp");
      AssertContains(text, "as Guid " + Payloads.InvoiceReference);
      AssertContains(text, "\"Tags\": [\"fruit\", \"fresh\"] // 2 items");
      AssertContains(text, "No errors");
    }

    [TestMethod]
    public void MapsWithoutSchema_PropertyNames()
    {
      string text = Render(Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Map, false));
      AssertContains(text, "Structure: a single fixmap");
      AssertContains(text, "\"Customer\": \"Alice\"");
      Assert.IsFalse(text.Contains("McpAddress", StringComparison.Ordinal), text); // no schema, no type ids: nothing tells the types
    }

    [TestMethod]
    public void ArraysWithoutSchema_SaysNamesAreMissing()
    {
      string text = Render(Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, false));
      AssertContains(text, "without a schema the property names are not in the data");
      AssertContains(text, "\"Alice\",");
    }

    [TestMethod]
    public void TypeIds()
    {
      string text = Render(Payloads.Serialize(Payloads.Zoo(), ObjectLayout.Map, true));
      AssertContains(text, "// McpDog");
      AssertContains(text, "// McpCat");
      AssertContains(text, "\"Lives\": 9");
    }

    [TestMethod]
    public void SchemaReference_WithAndWithoutStore()
    {
      SchemaStore store = new SchemaStore();
      byte[] bytes = Payloads.SerializeWithReference(Payloads.Invoice(), store);

      string without = Render(bytes);
      AssertContains(without, "(not available)");
      AssertContains(without, "is not available: property ids and type ids show as their index in it");

      string with = Render(bytes, null, store);
      AssertContains(with, "(found)");
      AssertContains(with, "\"Customer\": \"Alice\"");
    }

    [TestMethod]
    public void Truncated_ErrorWithOffset()
    {
      byte[] invoice = Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true);
      byte[] truncated = new byte[invoice.Length - 20];
      Array.Copy(invoice, truncated, truncated.Length);
      string text = Render(truncated);
      // The data ends in the Guid (bin 8, 16 bytes) of Reference, which used to be read with zeros, the error came at Tags
      AssertContains(text, "1 error");
      AssertContains(text, "\"Reference\": \"\", // bin, 0 bytes, ERROR: Error while reading data.");
      AssertContains(text, "Errors:");
      AssertContains(text, "(Reference): Error while reading data. (Unexpected end of data.)");
      Assert.IsFalse(text.Contains('\r'), "Lines end with \\n on every OS (the error messages of the library use Environment.NewLine)");
    }

    /// <summary>
    /// The bytes missing at the end used to read as zeros: "A\0\0" without an error.
    /// </summary>
    [TestMethod]
    public void TruncatedString_IsAnError()
    {
      string text = Render(new byte[] { 0xA3, 0x41 }); // fixstr of 3, 1 byte
      AssertContains(text, "1 error");
      AssertContains(text, "Unexpected end of data.");
      Assert.IsFalse(text.Contains("\\u0000", StringComparison.Ordinal), text);
    }

    /// <summary>
    /// An array holding an error is an MpError item (type "never used"), the structure names the array.
    /// </summary>
    [TestMethod]
    public void StructureOfAnArrayHoldingAnError()
    {
      AssertContains(Render(new byte[] { 0x92, 0xC1, 0xC0 }), "Structure: a single fixarray holding an error");
      AssertContains(Render(new byte[] { 0xA3, 0x41 }), "Structure: a single fixstr that could not be read");
    }

    [TestMethod]
    public void Corrupt_ValuesAfterTheErrorAreMarked()
    {
      byte[] bytes = Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true);
      int at = bytes.Length / 2;
      bytes[at] = 0xC1; // never used
      string text = Render(bytes);
      AssertContains(text, string.Concat("The values after the first error (at 0x", at.ToString("X"), ")"));
      AssertContains(text, "// after the error");

      PayloadDocument doc = new PayloadDocument(bytes, "test", new DecodeOptions());
      string explained = TextRenderer.ExplainOffset(doc, at, new RenderOptions());
      AssertContains(explained, "0xC1, as a type byte: never used (invalid)");
      AssertContains(explained, "ERROR: The specification specifically states that the value 0xC1 should never be used.");
      AssertContains(explained, "[C1]");
      Assert.IsFalse(text.Contains('\r') || explained.Contains('\r'), "Lines end with \\n on every OS");
    }

    [TestMethod]
    public void WithoutContinueOnError_TheArrayEndsAtTheError()
    {
      byte[] bytes = Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true);
      bytes[bytes.Length / 2] = 0xC1;
      PayloadDocument doc = new PayloadDocument(bytes, "test", new DecodeOptions() { ContinueOnError = false });
      string text = TextRenderer.Render(doc, new RenderOptions());
      AssertContains(text, "\"Customer\": null,");
      AssertContains(text, "Error at 0x7B ([0].Id): "); // the rest is read as items that follow the invoice
    }

    [TestMethod]
    public void ItemsView_OffsetsEncodingsAndPaths()
    {
      byte[] bytes = Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true);
      string text = Render(bytes, new RenderOptions() { View = DecodeView.Items });
      AssertContains(text, "0x0000   [0] fixmap, 3 entries // indexed schema");
      AssertContains(text, "uint 16 1234 // Id");
      AssertContains(text, "fixext 16, type 1 (decimal): 1.25 // Lines[0].Price");
      AssertContains(text, "fixext 4, type -1 (timestamp): 2026-10-05T12:30:00Z // Date");
      Assert.IsFalse(text.Contains("Objects", StringComparison.Ordinal), text);

      // Only a range
      PayloadDocument doc = new PayloadDocument(bytes, "test", new DecodeOptions());
      ItemNode date = doc.Items.Find(n => n.Item is MpDateTime);
      string range = TextRenderer.Render(doc, new RenderOptions() { View = DecodeView.Items, From = date.Offset, To = date.Offset });
      AssertContains(range, "(timestamp)");
      Assert.IsFalse(range.Contains("\"Alice\"", StringComparison.Ordinal), range);
    }

    [TestMethod]
    public void PathAndLimits()
    {
      byte[] bytes = Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true);
      string part = Render(bytes, new RenderOptions() { Path = "Lines[1]" });
      AssertContains(part, "Objects at Lines[1]");
      AssertContains(part, "\"Pears\"");
      Assert.IsFalse(part.Contains("Apples", StringComparison.Ordinal), part);

      string missing = Render(bytes, new RenderOptions() { Path = "Nope" });
      AssertContains(missing, "No value at path \"Nope\"");

      string limited = Render(bytes, new RenderOptions() { MaxNodes = 4 });
      AssertContains(limited, "more not shown (maxNodes reached)");

      string offsets = Render(bytes, new RenderOptions() { Offsets = true });
      AssertContains(offsets, "\"Id\": 1234, // @0x7B");
    }

    [TestMethod]
    public void LongStringsAndBinAreCut()
    {
      byte[] bytes = MsgPackSerializer.Serialize(new object[] { new string('x', 1000), new byte[100] }, new MsgPackSettings() { UseInexedSchema = false });
      string text = Render(bytes, new RenderOptions() { MaxString = 10 });
      AssertContains(text, "\"xxxxxxxxxx...\"");
      AssertContains(text, "string of 1000 chars, the first 10 shown");
      AssertContains(text, "bin, 100 bytes");
    }

    [TestMethod]
    public void Dictionaries_KeysKeepTheirKind()
    {
      System.Collections.Generic.Dictionary<int, string> byNumber = new System.Collections.Generic.Dictionary<int, string>() { { 1, "one" }, { 2, "two" } };
      string text = Render(MsgPackSerializer.Serialize(byNumber, new MsgPackSettings() { UseInexedSchema = false }));
      AssertContains(text, "1: \"one\"");
    }

    [TestMethod]
    public void Search()
    {
      PayloadDocument doc = new PayloadDocument(Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true), "test", new DecodeOptions());
      string text = TextRenderer.Search(doc, "pear", false, new RenderOptions());
      AssertContains(text, "1 item holds \"pear\"");
      AssertContains(text, "fixstr \"Pears\" // Lines[1].Product");
    }

    [TestMethod]
    public void EmptyAndUnreadableData()
    {
      AssertContains(Render(new byte[0]), "There is no data.");
      AssertContains(Render(new byte[] { 0x2A }), "\n42\n");
    }

    [TestMethod]
    public void TypeBytes()
    {
      Assert.AreEqual("fixmap of 2 entries", TextRenderer.DescribeTypeByte(0x82));
      Assert.AreEqual("fixstr of 3 bytes", TextRenderer.DescribeTypeByte(0xA3));
      Assert.AreEqual("negative fixint -1", TextRenderer.DescribeTypeByte(0xFF));
      Assert.AreEqual("map 32 (4 byte count follows)", TextRenderer.DescribeTypeByte(0xDF));
    }
  }
}
