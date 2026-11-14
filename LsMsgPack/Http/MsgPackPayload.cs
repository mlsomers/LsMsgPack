using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPack.Http
{
  /// <summary>
  /// A serialized body in (at most) two parts: the head (the schema, or the reference to it) and the rest of the data. Write both, in this order.
  /// </summary>
  public struct MsgPackPayload
  {
    internal MsgPackPayload(ArraySegment<byte> schema, ArraySegment<byte> body, string schemaId, bool isReference)
    {
      Schema = schema;
      Body = body;
      SchemaId = schemaId;
      IsReference = isReference;
    }

    /// <summary>
    /// The head: the schema sent inline, or the reference to the schema the client holds (<see cref="IsReference"/>). Empty when the data has no schema (property names, null).
    /// </summary>
    public ArraySegment<byte> Schema { get; }

    /// <summary>
    /// The data (after the head).
    /// </summary>
    public ArraySegment<byte> Body { get; }

    /// <summary>
    /// The id of the schema (for the response header <see cref="LsMsgPackHttpSerializer.SchemaHeader"/>), null when the data has no cached schema.
    /// </summary>
    public string SchemaId { get; }

    /// <summary>
    /// The data refers to its schema: only readers holding it can read it (the response varies with <see cref="LsMsgPackHttpSerializer.SchemasHeader"/>).
    /// </summary>
    public bool IsReference { get; }

    /// <summary>
    /// The number of bytes of the head and the data.
    /// </summary>
    public int Length
    {
      get { return (Schema.Array is null ? 0 : Schema.Count) + Body.Count; }
    }

    public async Task WriteToAsync(Stream target, CancellationToken cancellationToken)
    {
      if (Schema.Array != null && Schema.Count > 0)
        await target.WriteAsync(Schema.Array, Schema.Offset, Schema.Count, cancellationToken).ConfigureAwait(false);
      await target.WriteAsync(Body.Array, Body.Offset, Body.Count, cancellationToken).ConfigureAwait(false);
    }

    public void WriteTo(Stream target)
    {
      if (Schema.Array != null && Schema.Count > 0)
        target.Write(Schema.Array, Schema.Offset, Schema.Count);
      target.Write(Body.Array, Body.Offset, Body.Count);
    }

    public byte[] ToArray()
    {
      byte[] all = IO.MsgPackWriter.NewBytes(Length); // filled below
      int schemaLength = Schema.Array is null ? 0 : Schema.Count;
      if (schemaLength > 0)
        Buffer.BlockCopy(Schema.Array, Schema.Offset, all, 0, schemaLength);
      Buffer.BlockCopy(Body.Array, Body.Offset, all, schemaLength, Body.Count);
      return all;
    }
  }
}
