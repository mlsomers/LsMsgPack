using LsMsgPack;
using LsMsgPackMcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace LsMsgPackMcpServerTests
{
  /// <summary>
  /// Writes what the tools answer for the sample payloads to LSMSGPACK_MCP_SAMPLES (a directory), to read the output as an agent would. Does nothing without it.
  /// </summary>
  [TestClass]
  public class SampleOutput
  {
    [TestMethod]
    public void WriteSamples()
    {
      string dir = Environment.GetEnvironmentVariable("LSMSGPACK_MCP_SAMPLES");
      if (string.IsNullOrEmpty(dir))
        return;
      Directory.CreateDirectory(dir);

      Write(dir, "invoice-array-schema", Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true), new RenderOptions());
      Write(dir, "invoice-map-schema", Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Map, true), new RenderOptions());
      Write(dir, "invoice-map-plain", Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Map, false), new RenderOptions());
      Write(dir, "invoice-array-plain", Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, false), new RenderOptions());
      Write(dir, "zoo-array-schema", Payloads.Serialize(Payloads.Zoo(), ObjectLayout.Array, true), new RenderOptions());
      Write(dir, "zoo-map-plain", Payloads.Serialize(Payloads.Zoo(), ObjectLayout.Map, false), new RenderOptions());
      byte[] invoice = Payloads.Serialize(Payloads.Invoice(), ObjectLayout.Array, true);
      Write(dir, "invoice-items", invoice, new RenderOptions() { View = DecodeView.Items, Issues = IssueLevel.All });
      Write(dir, "invoice-offsets", invoice, new RenderOptions() { Offsets = true });
      Write(dir, "invoice-path", invoice, new RenderOptions() { Path = "Lines[1]" });
      byte[] truncated = new byte[invoice.Length - 20];
      Array.Copy(invoice, truncated, truncated.Length);
      Write(dir, "invoice-truncated", truncated, new RenderOptions() { View = DecodeView.Both });
      byte[] corrupt = (byte[])invoice.Clone();
      corrupt[corrupt.Length / 2] = 0xC1;
      Write(dir, "invoice-corrupt", corrupt, new RenderOptions());
      SchemaStore store = new SchemaStore();
      byte[] reference = Payloads.SerializeWithReference(Payloads.Invoice(), store);
      Write(dir, "invoice-reference-missing", reference, new RenderOptions());
      Write(dir, "invoice-reference-found", reference, new RenderOptions(), store);
      Write(dir, "number", MsgPackSerializer.Serialize(42), new RenderOptions());
      System.Collections.Generic.List<McpInvoice> many = new System.Collections.Generic.List<McpInvoice>();
      for (int t = 0; t < 20000; t++)
        many.Add(Payloads.Invoice());
      File.WriteAllBytes(Path.Combine(dir, "invoices-20000.msgpack"), Payloads.Serialize(many, ObjectLayout.Array, true));

      PayloadDocument doc = new PayloadDocument(corrupt, "corrupt", new DecodeOptions());
      File.WriteAllText(Path.Combine(dir, "explain-corrupt.txt"), TextRenderer.ExplainOffset(doc, corrupt.Length / 2, new RenderOptions()));
      PayloadDocument found = new PayloadDocument(invoice, "invoice", new DecodeOptions());
      File.WriteAllText(Path.Combine(dir, "search-apples.txt"), TextRenderer.Search(found, "apples", false, new RenderOptions()));
      File.WriteAllText(Path.Combine(dir, "search-3.txt"), TextRenderer.Search(found, "3", false, new RenderOptions()));
    }

    private static void Write(string dir, string name, byte[] bytes, RenderOptions options, SchemaStore store = null)
    {
      PayloadDocument doc = new PayloadDocument(bytes, name, new DecodeOptions() { Schemas = store }) { Id = "doc1" };
      File.WriteAllText(Path.Combine(dir, name + ".txt"), TextRenderer.Render(doc, options));
      File.WriteAllBytes(Path.Combine(dir, name + ".msgpack"), bytes);
    }
  }
}
