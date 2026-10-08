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
      // What could not be read is null (the item holds 0 bytes), the error at the start of the item as in the list of errors
      AssertContains(text, "\"Reference\": null // ERROR: Error while reading data. (Unexpected end of data.) (bin 8 at 0xD8)");
      AssertContains(text, "McpInvoice~, 1 not read: the reading stopped at the error"); // Tags
      AssertContains(text, "Errors:");
      AssertContains(text, "Error at 0xD8 (Reference): Error while reading data. (Unexpected end of data.)");
      Assert.IsFalse(text.Contains("were read on at the next byte", StringComparison.Ordinal), "Nothing was read after the error");
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
      // The reading stops at the error: the rest was read as items that follow the invoice, the values after Id as null
      AssertContains(text, "Bytes 0x7C to 0xF6 were not read: the reading stopped at the first error (continueOnError is false).");
      AssertContains(text, "{ // McpInvoice~, 6 not read: the reading stopped at the error\n  \"Id\": null // ERROR: ");
      AssertContains(text, "Error at 0x7B (Id): ");
      Assert.IsFalse(text.Contains("\"Customer\"", StringComparison.Ordinal), text);
      Assert.IsFalse(text.Contains("were read on at the next byte", StringComparison.Ordinal), text);
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

    /// <summary>
    /// 301 bytes nested 300 levels deep were 220 KB of text: the message of every level around the error, and the indentation.
    /// </summary>
    [TestMethod]
    public void DeepNesting_StaysSmall()
    {
      byte[] bytes = new byte[301];
      for (int t = 0; t < 300; t++)
        bytes[t] = 0x91; // fixarray of 1
      bytes[300] = 0x01;
      string text = Render(bytes);
      Assert.IsLessThan(20000, text.Length, text.Substring(0, 2000));
      AssertContains(text, "nested deeper than 32 levels: ask for the path \"[0][0]");
      AssertContains(text, "ERROR: A nested item contains an error.");

      string items = Render(bytes, new RenderOptions() { View = DecodeView.Items });
      Assert.IsLessThan(100000, items.Length);
      AssertContains(items, "(level 33) ");
    }

    /// <summary>
    /// A header claiming more than the data holds: an error at the header (no slots allocated for the claimed count), the values that are there are shown.
    /// </summary>
    [TestMethod]
    public void ClaimedCount_ShownUpToTheEnd()
    {
      string text = Render(new byte[] { 0xDD, 0x08, 0x00, 0x00, 0x00, 0x01 }); // array32 of 134217728 items, 1 there
      AssertContains(text, "1 error");
      AssertContains(text, "Structure: a single array 32 holding an error");
      AssertContains(text, "[ // 2 of 134217728 items (claimed by the header)\n  1,\n");
    }

    /// <summary>
    /// The encoding of an item is the one in the data: it was computed from the value (a str 16 that ends early was a fixstr, an array 16 of 3 items a fixarray).
    /// </summary>
    [TestMethod]
    public void Encodings_AsInTheData()
    {
      string text = Render(new byte[] { 0xDC, 0x00, 0x03, 0x01, 0xDA, 0x00, 0x01, 0x41, 0xDE, 0x00, 0x00 }, new RenderOptions() { View = DecodeView.Items, Issues = IssueLevel.None });
      AssertContains(text, "0x0000 array 16, 3 items\n");
      AssertContains(text, "[1] str 16 \"A\"");
      AssertContains(text, "[2] map 16, 0 entries");

      string cut = Render(new byte[] { 0xDA, 0x00, 0x05, 0x41, 0x42 }, new RenderOptions() { View = DecodeView.Both });
      AssertContains(cut, "Structure: a single str 16 that could not be read");
      AssertContains(cut, "\nnull // ERROR: Error while reading data. (Unexpected end of data.) (str 16 at 0x0)\n");
      AssertContains(cut, "0x0000   partial str 16 of 5 bytes, 2 bytes left in the data: not read");

      string array = Render(new byte[] { 0xDC, 0x00, 0x05, 0x01, 0x02 }, new RenderOptions() { View = DecodeView.Items });
      AssertContains(array, "partial array 16, 3 of 5 items (claimed by the header)");
    }

    /// <summary>
    /// A value that ends early: the error was at the last byte read in the items view and at the end of the data in the list, its value (0) was shown
    /// without the error and validated ("smaller encodings would save 2 bytes"), and the values "after the error" announced with none after it.
    /// </summary>
    [TestMethod]
    public void CutOffValue_OneErrorOffset()
    {
      string text = Render(new byte[] { 0xCD, 0x01 }, new RenderOptions() { View = DecodeView.Both, Issues = IssueLevel.All });
      AssertContains(text, "1 error, 0 warnings or comments.");
      AssertContains(text, "\nnull // ERROR: Error while reading data. (Unexpected end of data.) (uint 16 at 0x0)\n");
      AssertContains(text, "0x0000 ERROR: Error while reading data. (Unexpected end of data.)\n0x0000   partial uint 16: not read");
      AssertContains(text, "Error at 0x0: Error while reading data.");
      Assert.IsFalse(text.Contains("were read on at the next byte", StringComparison.Ordinal), text);
      Assert.IsFalse(text.Contains("smaller", StringComparison.Ordinal), text);

      PayloadDocument doc = new PayloadDocument(new byte[] { 0xCD, 0x01 }, "test", new DecodeOptions());
      AssertContains(TextRenderer.ExplainOffset(doc, 1, new RenderOptions()), "  0x0 (2 bytes) partial uint 16: not read\n");
      AssertContains(TextRenderer.ExplainOffset(doc, -1, new RenderOptions()), "Offset -1 is negative");
    }

    [TestMethod]
    public void BoolKeysAndCounts()
    {
      string text = Render(new byte[] { 0x82, 0x01, 0xA1, 0x61, 0xC3, 0x92, 0x01, 0x02 });
      AssertContains(text, "  true: [1, 2] // 2 items\n");
      AssertContains(Render(new byte[] { 0x91, 0x01 }), "[1] // 1 item\n");
    }

    /// <summary>
    /// The validation of an extension whose data ended before its content failed with a NullReferenceException.
    /// </summary>
    [TestMethod]
    public void TruncatedExtension_ValidatesWithoutFailing()
    {
      string text = Render(new byte[] { 0xC9, 0x7F, 0x00, 0x00, 0x00, 0x01 }, new RenderOptions() { Issues = IssueLevel.All });
      AssertContains(text, "Unexpected end of data.");
      Assert.IsFalse(text.Contains("Validation failed", StringComparison.Ordinal), text);
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
