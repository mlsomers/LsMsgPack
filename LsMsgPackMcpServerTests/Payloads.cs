using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.Streaming;
using System;
using System.Collections.Generic;
using System.IO;

namespace LsMsgPackMcpServerTests
{
  public class McpAddress
  {
    public string Street { get; set; }
    public int Number { get; set; }
    public string City { get; set; }
  }

  public class McpLine
  {
    public string Product { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
  }

  public class McpInvoice
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public McpAddress Billing { get; set; }
    public List<McpLine> Lines { get; set; }
    public DateTime Date { get; set; }
    public Guid Reference { get; set; }
    public List<string> Tags { get; set; }
  }

  public abstract class McpAnimal
  {
    public string Name { get; set; }
  }

  public class McpDog : McpAnimal
  {
    public bool Barks { get; set; }
  }

  public class McpCat : McpAnimal
  {
    public int Lives { get; set; }
  }

  public class McpZoo
  {
    public List<McpAnimal> Animals { get; set; }
  }

  /// <summary>
  /// Payloads written by LsMsgPack in its different settings, the dates as timestamps (the offsets the tests name were taken with them; the native DateTime: RendererTests.NativeDateTime_WithItsKind).
  /// </summary>
  public static class Payloads
  {
    public static readonly DateTime InvoiceDate = new DateTime(2026, 10, 5, 12, 30, 0, DateTimeKind.Utc);
    public static readonly Guid InvoiceReference = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

    public static McpInvoice Invoice()
    {
      return new McpInvoice()
      {
        Id = 1234,
        Customer = "Alice",
        Billing = new McpAddress() { Street = "Main street", Number = 12, City = "Amsterdam" },
        Lines = new List<McpLine>()
        {
          new McpLine() { Product = "Apples", Quantity = 3, Price = 1.25m },
          new McpLine() { Product = "Pears", Quantity = 2, Price = 0.99m }
        },
        Date = InvoiceDate,
        Reference = InvoiceReference,
        Tags = new List<string>() { "fruit", "fresh" }
      };
    }

    public static McpZoo Zoo()
    {
      return new McpZoo() { Animals = new List<McpAnimal>() { new McpDog() { Name = "Rex", Barks = true }, new McpCat() { Name = "Tom", Lives = 9 } } };
    }

    public static byte[] Serialize<T>(T value, ObjectLayout layout, bool schema)
    {
      return MsgPackSerializer.Serialize(value, new MsgPackSettings() { ObjectLayout = layout, UseIndexedSchema = schema, DateTimeFormat = DateTimeFormat.Timestamp });
    }

    /// <summary>
    /// With a reference to the schema (<c>WriteSchemaReference</c>), the store holds it.
    /// </summary>
    public static byte[] SerializeWithReference<T>(T value, SchemaStore store)
    {
      MsgPackSettings settings = new MsgPackSettings() { ObjectLayout = ObjectLayout.Array, UseIndexedSchema = true, SchemaStore = store, WriteSchemaReference = true, DateTimeFormat = DateTimeFormat.Timestamp };
      return MsgPackSerializer.Serialize(value, settings);
    }

    /// <summary>
    /// A stream as LsMsgPack's MsgPackStreamWriter writes it: three invoices (each an entry, the schema inline once, then referenced), a table (a head and an array of unknown length of lines), and the index.
    /// </summary>
    public static byte[] Stream()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions { WriteSchemaReference = true, SchemaStore = new SchemaStore(), DateTimeFormat = DateTimeFormat.Timestamp });
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer, MsgPackStreamSchemas.PerStream, true, new MsgPackStreamIndexOptions { PointInterval = 2 }))
      {
        for (int t = 1; t <= 3; t++)
        {
          McpInvoice invoice = Invoice();
          invoice.Id = t;
          writer.BeginEntry("invoice " + t, "application/x-invoice");
          writer.WriteObject(invoice);
        }
        writer.BeginEntry("lines");
        writer.WriteHead<List<McpLine>>();
        writer.WriteArrayStart();
        foreach (McpLine line in Invoice().Lines)
          writer.WriteObject(line);
        writer.WriteObject(new McpLine() { Product = "Plums", Quantity = 7, Price = 2.5m });
        writer.WriteEnd();
      }
      return ms.ToArray();
    }

    /// <summary>
    /// A capture of a SignalR connection as LsMsgPack.SignalR and Microsoft's MessagePack protocol write it: the client's handshake, then each message a VarInt length prefix and an array:
    /// an invocation of "Send" with an invoice (more than 127 bytes: a prefix of 2 bytes), its completion with a result, a completion with an error and a ping.
    /// </summary>
    public static byte[] HubCapture()
    {
      LsMsgPackSerializer values = new LsMsgPackSerializer(LsMsgPackPresets.Generic());
      MemoryStream ms = new MemoryStream();
      byte[] handshake = System.Text.Encoding.UTF8.GetBytes("{\"protocol\":\"messagepack\",\"version\":2}\u001e");
      ms.Write(handshake, 0, handshake.Length);
      Dictionary<string, string> none = new Dictionary<string, string>();
      WriteFrame(ms, values.Serialize(new object[] { 1, new Dictionary<string, string>() { { "trace", "abc" } }, "1", "Send", new object[] { Invoice(), 7 }, new string[0] }));
      WriteFrame(ms, values.Serialize(new object[] { 3, none, "1", 3, Invoice() }));
      WriteFrame(ms, values.Serialize(new object[] { 3, none, "2", 1, "it failed" }));
      WriteFrame(ms, values.Serialize(new object[] { 6 }));
      return ms.ToArray();
    }

    /// <summary>
    /// "lsmsgpack-indexed" of LsMsgPack.SignalR, without the handshake: an invocation whose invoice is [schema, body] (the described schema) and a stream item [nil, value] (the TypeScript client).
    /// </summary>
    public static byte[] IndexedHubCapture()
    {
      LsMsgPackOptions options = LsMsgPackPresets.LsMsgPack();
      options.SchemaVersion = SchemaVersion.Described;
      LsMsgPackSerializer schema = new LsMsgPackSerializer(options);
      LsMsgPackSerializer plain = new LsMsgPackSerializer(LsMsgPackPresets.Generic());
      MemoryStream ms = new MemoryStream();
      // [1, {}, "1", "Send", [[schema, invoice]], []]
      MemoryStream invocation = new MemoryStream();
      invocation.Write(new byte[] { 0x96, 0x01, 0x80, 0xA1, (byte)'1', 0xA4, (byte)'S', (byte)'e', (byte)'n', (byte)'d', 0x91, 0x92 }, 0, 12);
      byte[] invoice = schema.Serialize(Invoice());
      invocation.Write(invoice, 0, invoice.Length);
      invocation.WriteByte(0x90);
      WriteFrame(ms, invocation.ToArray());
      // [2, {}, "s", [nil, {"Product": "Kiwi"}]]
      MemoryStream item = new MemoryStream();
      item.Write(new byte[] { 0x94, 0x02, 0x80, 0xA1, (byte)'s', 0x92, 0xC0 }, 0, 7);
      byte[] value = plain.Serialize(new Dictionary<string, object>() { { "Product", "Kiwi" } });
      item.Write(value, 0, value.Length);
      WriteFrame(ms, item.ToArray());
      return ms.ToArray();
    }

    private static void WriteFrame(MemoryStream target, byte[] message)
    {
      long length = message.Length;
      do
      {
        byte b = (byte)(length & 0x7F);
        length >>= 7;
        if (length > 0)
          b |= 0x80;
        target.WriteByte(b);
      }
      while (length > 0);
      target.Write(message, 0, message.Length);
    }
  }
}
