using LsMsgPack;
using LsMsgPack.Streaming;
using LtMsgPack;
using LtMsgPack.Streaming;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LsMsgPackTsTestServer
{
  /// <summary>
  /// Streams of MsgPackStreamWriter for the stream tests of the TypeScript client (clients/typescript/test/stream-server.test.mjs), with range processing for the index,
  /// and a reader of the streams the TypeScript writer writes.
  /// </summary>
  [ApiController]
  [Route("stream")]
  public class StreamController : ControllerBase
  {
    /// <summary>
    /// A stream of the invoices of /expected/invoices: <c>table</c> a head (List&lt;Invoice&gt;, the described schema, or the plain one with <paramref name="plain"/>) and its body,
    /// an array of unknown length; <c>objects</c> one invoice per root value with its schema inline the first time, then referred to (PerStream);
    /// <c>escaped</c> maps, str and bin of unknown length. Then an entry with the count. With an index (points every 25 invoices, a checkpoint per entry with <paramref name="checkpoints"/>).
    /// </summary>
    [HttpGet("{name}")]
    public IActionResult Get(string name, [FromQuery] int count = 10, [FromQuery] bool plain = false, [FromQuery] bool checkpoints = false)
    {
      LtMsgPackOptions options = LtMsgPackPresets.LsMsgPack();
      options.SchemaVersion = plain ? SchemaVersion.Names : SchemaVersion.Described;
      options.SchemaStore = new SchemaStore();
      options.WriteSchemaReference = true;
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(options);
      Invoice[] invoices = Data.Invoices(count);
      MsgPackStreamIndexOptions index = new MsgPackStreamIndexOptions { PointInterval = 25, CheckpointInterval = checkpoints ? 1 : 0 };

      MemoryStream stream = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(stream, serializer, MsgPackStreamSchemas.PerStream, true, index))
      {
        writer.BeginEntry("invoices", "application/x-invoices");
        switch (name)
        {
          case "table":
            writer.WriteHead<List<Invoice>>();
            writer.WriteArrayStart();
            for (int t = 0; t < invoices.Length; t++)
              writer.WriteObject(invoices[t]);
            writer.WriteEnd();
            break;
          case "objects":
            for (int t = 0; t < invoices.Length; t++)
            {
              if (t > 0)
                writer.BeginEntry(t);
              writer.WriteObject(invoices[t]);
            }
            break;
          case "escaped":
            writer.WriteArrayStart();
            for (int t = 0; t < invoices.Length; t++)
            {
              writer.WriteMapStart();
              writer.Write("number");
              writer.Write(invoices[t].InvoiceNumber);
              writer.Write("customer");
              writer.WriteStringStart();
              string customer = invoices[t].Customer.CompanyName;
              for (int c = 0; c < customer.Length; c += 3)
                writer.WriteStringChunk(customer.AsSpan(c, Math.Min(3, customer.Length - c)));
              writer.WriteEnd();
              writer.Write("bytes");
              writer.WriteBinaryStart();
              writer.WriteBinary(new byte[] { (byte)t });
              writer.WriteBinary(new byte[] { (byte)(t + 1), (byte)(t + 2) });
              writer.WriteEnd();
              writer.WriteEnd();
            }
            writer.WriteEnd();
            break;
          default:
            return NotFound();
        }
        writer.BeginEntry("count");
        writer.Write(count);
      }
      return File(stream.ToArray(), "application/octet-stream", enableRangeProcessing: true);
    }

    /// <summary>
    /// Reads a stream of the TypeScript writer with MsgPackStreamReader (plain MsgPack: no indexed schema, names ignoring case): an array of invoices, then a str,
    /// and answers what it read, with the entries of its index.
    /// </summary>
    [HttpPost("echo")]
    public async Task<IActionResult> Echo()
    {
      MemoryStream stream = new MemoryStream();
      await Request.Body.CopyToAsync(stream);
      stream.Position = 0;
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, PropertyNameCaseInsensitive = true });
      using (MsgPackStreamReader reader = new MsgPackStreamReader(stream, serializer))
      {
        IReadOnlyList<MsgPackStreamIndex> indexes = reader.ReadIndexes();
        List<Invoice> invoices = new List<Invoice>();
        reader.Read(); // ArrayStart
        while (!reader.AtEnd)
          invoices.Add(reader.ReadObject<Invoice>());
        reader.Read(); // ArrayEnd
        reader.Read();
        string text = reader.GetString();
        MsgPackStreamEntry last = indexes.SelectMany(i => i.Entries).Last();
        reader.Seek(indexes[0].Entries[0]);
        reader.Read();
        reader.SeekElement(indexes[0].Entries[0], invoices.Count - 1);
        Invoice sought = reader.ReadObject<Invoice>();
        return new JsonResult(new
        {
          Invoices = invoices,
          Text = text,
          Sought = sought.InvoiceNumber,
          Indexes = indexes.Select(i => new
          {
            Status = i.Status.ToString(),
            i.Writer,
            Entries = i.Entries.Select(e => new { Identifier = e.Identifier?.ToString(), e.ContentType, e.Offset, e.Length, e.ElementCount, Points = e.Points?.Select(p => p.Element), e.Metadata })
          })
        });
      }
    }
  }
}
