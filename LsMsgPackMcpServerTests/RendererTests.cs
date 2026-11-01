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

    /// <summary>
    /// Version 2 of the schema (SchemaVersion.Described): the declared types, nothing inferred, the defaults of values that were left out.
    /// </summary>
    [TestMethod]
    public void DescribedSchema_DeclaredTypes()
    {
      McpInvoice invoice = Payloads.Invoice();
      invoice.Id = 0;
      string text = Render(MsgPackSerializer.Serialize(invoice, new MsgPackSettings() { SchemaVersion = SchemaVersion.Described }));
      AssertContains(text, "indexed schema (inline, 3 types, version 2 with the declared types) followed by the body");
      AssertContains(text, "No errors");
      AssertContains(text, "  root: McpInvoice\n");
      AssertContains(text, "#0 McpInvoice: Id: int, Customer: string, Billing: McpAddress, Lines: list<McpLine>, Date: datetime, Reference: guid, Tags: list<string>");
      AssertContains(text, "{ // McpInvoice\n");
      AssertContains(text, "\"Id\": null, // int, left out: 0");
      AssertContains(text, "\"Lines\": [ // list<McpLine>, ");
      Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text, @"Mcp\w+~"), text); // nothing inferred
    }

    /// <summary>
    /// { "Process": ["FileName"] } followed by { "": 0, 0: "calc" }: a schema made to attack a reader, listed with the errors by default.
    /// </summary>
    [TestMethod]
    public void SchemaNamingAGadget_SecurityVulnerability()
    {
      string text = Render(Convert.FromHexString("81a750726f6365737391a846696c654e616d6582a00000a463616c63"));
      AssertContains(text, "No errors, 1 security vulnerability");
      AssertContains(text, "Errors and security vulnerabilities:");
      AssertContains(text, "Security vulnerability at 0x1: The schema names the type \"Process\"");
      AssertContains(text, "deserialization gadget");
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
    /// A map cut off by the end of the data: its unread slots made it a dictionary, and the inferred types moved one entry of the schema up.
    /// </summary>
    [TestMethod]
    public void TruncatedMap_TypesInferredFromWhatWasRead()
    {
      byte[] invoice = Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Map, true);
      byte[] truncated = new byte[invoice.Length - 20]; // in Reference: Tags is not read
      Array.Copy(invoice, truncated, truncated.Length);
      string text = Render(truncated);
      AssertContains(text, "{ // McpInvoice~, 1 not read: the reading stopped at the error");
      AssertContains(text, "\"Billing\": { \"Street\": \"Main street\", \"Number\": 12, \"City\": \"Amsterdam\" }, // McpAddress~");
      AssertContains(text, "// McpLine~, Price: decimal");
      Assert.IsFalse(text.Contains("no value matched", StringComparison.Ordinal), text);
    }

    /// <summary>
    /// AlwaysSwap is the specification's byte order on a little-endian machine: the note said it was not.
    /// </summary>
    [TestMethod]
    public void EndianNote_RelativeToTheMachine()
    {
      StringAssert.Contains(TextRenderer.EndianNote(EndianAction.AlwaysSwap, true), "big-endian on this little-endian machine, the byte order of the specification");
      StringAssert.Contains(TextRenderer.EndianNote(EndianAction.NeverSwap, true), "little-endian on this little-endian machine, not the byte order of the specification");
      StringAssert.Contains(TextRenderer.EndianNote(EndianAction.NeverSwap, false), "big-endian on this big-endian machine, the byte order of the specification");
      StringAssert.Contains(TextRenderer.EndianNote(EndianAction.AlwaysSwap, false), "little-endian on this big-endian machine, not the byte order");

      PayloadDocument doc = new PayloadDocument(new byte[] { 0x91, 0xCD, 0x01, 0x00 }, "test", new DecodeOptions() { Endian = EndianAction.NeverSwap }) { Id = "doc1" };
      string text = TextRenderer.Render(doc, new RenderOptions());
      AssertContains(text, "Read with endian NeverSwap: ");
      AssertContains(text, BitConverter.IsLittleEndian ? "[1]" : "[256]");
    }

    /// <summary>
    /// [timestamp, offset in minutes] read as the DateTimeOffset it may be.
    /// </summary>
    [TestMethod]
    public void DateTimeOffsetShape_ShowsTheMomentAtItsOffset()
    {
      DateTimeOffset sent = new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(2));
      string text = Render(MsgPackSerializer.Serialize(new System.Collections.Generic.List<DateTimeOffset>() { sent }));
      AssertContains(text, "[\"2026-10-06T07:00:00Z\", 120] // 2 items, as DateTimeOffset 2026-10-06T09:00:00+02:00 (DateTimeOffsetFormat.TimestampAndOffset)");
      // Not for other arrays of a timestamp and a number
      Assert.IsFalse(Render(new byte[] { 0x92, 0xD6, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xCD, 0x10, 0x00 }).Contains("as DateTimeOffset", StringComparison.Ordinal)); // 4096 minutes
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
      // The decoder reads streams: 0xC1 and the byte after it are an escape sequence (here a reserved one)
      AssertContains(explained, "0xC1, as a type byte: 0xC1, never used by the specification; streams start an escape sequence with it (msgpack/msgpack#347)");
      AssertContains(explained, "ERROR: The escape sequence 0xC1 0x");
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
      // The reading stops at the error (0xC1 and the code byte after it, a reserved escape sequence): the values after Id are null
      AssertContains(text, "Bytes 0x7D to 0xF6 were not read: the reading stopped at the first error (continueOnError is false).");
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
      AssertContains(text, "[ // 1 item, error inside");
      Assert.IsFalse(text.Contains("ERROR: A nested item contains an error.", StringComparison.Ordinal), "The levels above the error only say that they hold it");
      AssertContains(text, "Error at 0x100 ([0]×257): ");
      Assert.AreEqual("Lines[2]×4.Name[0]", TextRenderer.ShortPath("Lines[2][2][2][2].Name[0]"));
      Assert.AreEqual("[0][0][0]", TextRenderer.ShortPath("[0][0][0]"));

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

    /// <summary>
    /// A stream of MsgPackStreamWriter: values of unknown length, the schema written once and referenced, a head with its body, the index with its entries and its tail.
    /// </summary>
    [TestMethod]
    public void Stream_IndexEntriesAndValuesOfUnknownLength()
    {
      byte[] bytes = Payloads.Stream();
      string text = Render(bytes, new RenderOptions() { Issues = IssueLevel.All });
      AssertContains(text, "Structure: a stream (as MsgPackStreamWriter writes it) with 1 value of unknown length (0xC1 escape sequences, msgpack/msgpack#347) and an index of 4 entries; 4 payloads that follow each other");
      AssertContains(text, ": Complete, written by LtMsgPack ");
      AssertContains(text, ", CRC-32 matches, 4 entries, 2 schemas:");
      AssertContains(text, "  entry \"invoice 2\" at 0xF5 (141 bytes), application/x-invoice\n");
      AssertContains(text, "  entry \"lines\" at ");
      AssertContains(text, "3 elements, 2 points");
      Assert.AreEqual(1, Occurrences(text, "#0 McpInvoice: Id, Customer, Billing, Lines, Date, Reference, Tags"), "the schema of the invoices once: the others refer to it");
      AssertContains(text, "{ // entry \"invoice 3\", McpInvoice~\n    \"Id\": 3,");
      AssertContains(text, "[ // entry \"lines\", 3 items\n    { \"Product\": \"Apples\", \"Quantity\": 3, \"Price\": 1.25 }, // McpLine~, Price: decimal");
      AssertContains(text, "{ \"Product\": \"Plums\", \"Quantity\": 7, \"Price\": 2.5 }");
      AssertContains(text, "No errors, 1 warning or comment.");
      AssertContains(text, "Written without its length (0xC1 escape sequences");
      Assert.IsFalse(text.Contains("smaller container", StringComparison.Ordinal), "the tail has fixed types: " + text);

      string items = Render(bytes, new RenderOptions() { View = DecodeView.Items });
      AssertContains(items, " array of unknown length (C1 90 ... C1 C0), 3 items // [3]");
      AssertContains(items, " ext 8, type 3 (stream index): Complete, 4 entries, CRC-32 matches\n");
      AssertContains(items, "     content fixarray, 7 items\n");
      AssertContains(items, "       entries fixarray, 4 items\n");
      AssertContains(items, "           identifier fixstr \"invoice 1\"\n");
      AssertContains(items, "               element positive fixint 2\n");
      AssertContains(items, " tail of the stream index (fixarray, 4 items): the index at ");
      AssertContains(items, " matches\n");

      PayloadDocument doc = new PayloadDocument(bytes, "test", new DecodeOptions());
      int escape = Array.IndexOf(bytes, (byte)0xC1);
      string explained = TextRenderer.ExplainOffset(doc, escape, new RenderOptions());
      AssertContains(explained, "C1 90 starts an array of unknown length (ended by C1 C0)");
      AssertContains(explained, "Bytes of the innermost item (header | content): C1 90 | 93 A6 41 70");
      AssertContains(explained, "Object: [3] = [ // entry \"lines\", 3 items");
      string end = TextRenderer.ExplainOffset(doc, Array.LastIndexOf(bytes, (byte)0xC1, bytes.Length - 236), new RenderOptions());
      AssertContains(end, "C1 C0 ends the innermost value of unknown length");

      string found = TextRenderer.Search(doc, "invoice 2", false, new RenderOptions());
      AssertContains(found, "1 item holds \"invoice 2\"");
    }

    [TestMethod]
    public void Stream_ChunksAndADamagedIndex()
    {
      // "abc" in two chunks, then a bin of unknown length
      string text = Render(new byte[] { 0x92, 0xC1, 0xA0, 0xA2, 0x61, 0x62, 0xA1, 0x63, 0xC1, 0xC0, 0xC1, 0xC4, 0xC4, 0x01, 0x07, 0xC1, 0xC0 }, new RenderOptions() { View = DecodeView.Both });
      AssertContains(text, "[\"abc\", \"07\"]");
      AssertContains(text, "0x0001   [0] str of unknown length in 2 chunks (C1 A0 ... C1 C0) \"abc\" // [0]\n");
      AssertContains(text, "0x0003     chunk fixstr \"ab\"\n");
      AssertContains(text, "0x0006     chunk fixstr \"c\"\n");
      AssertContains(text, "0x000A   [1] bin of unknown length in 1 chunk (C1 C4 ... C1 C0), 1 byte: 07 // [1]\n");
      AssertContains(text, "0x000C     chunk bin 8, 1 byte: 07\n");

      byte[] bytes = Payloads.Stream();
      int writer = IndexOf(bytes, System.Text.Encoding.ASCII.GetBytes("LtMsgPack"));
      bytes[writer] = (byte)'X';
      string damaged = Render(bytes);
      AssertContains(damaged, "CRC-32 does NOT match (readers that seek do not use it)");
      AssertContains(damaged, "does not match the CRC-32 in its tail");
    }

    private static int Occurrences(string text, string part)
    {
      int count = 0;
      for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        count++;
      return count;
    }

    private static int IndexOf(byte[] bytes, byte[] part)
    {
      for (int t = 0; t <= bytes.Length - part.Length; t++)
      {
        int p = 0;
        while (p < part.Length && bytes[t + p] == part[p])
          p++;
        if (p == part.Length)
          return t;
      }
      return -1;
    }
  }
}
