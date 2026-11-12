using LsMsgPack;
using LsMsgPack.IO;
using LsMsgPack.Writing;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.IO;

namespace LsMsgPack.Data
{
  /// <summary>
  /// Rows as one payload: the bytes of <c>Serialize(List&lt;T&gt;)</c> of a class with a property per column (the schema, then an array of known length). The rows are buffered for the array header:
  /// in the body writer, which moves to pooled chunks every <see cref="ChunkSize"/> bytes, so a large result does not grow (and drop) one buffer.
  /// </summary>
  internal static class RowSerializer
  {
    private const int ChunkSize = 64 * 1024;

    [ThreadStatic]
    private static MsgPackWriter _body;

    internal static int Serialize(Serializer serializer, IDataReader reader, string typeName, Stream target, out byte[] result)
    {
      LsMsgPackOptions options = serializer.Options;
      if (serializer.Filters == FilterMode.Custom)
        throw new NotSupportedException("Rows are written with the default dynamic filter (FilterDefaultValues) or none, not with custom dynamic filters.");

      MsgPackWriter body = _body;
      _body = null; // a nested call (from a value) gets its own
      if (body is null)
        body = new MsgPackWriter(16 * 1024);
      Chunks chunks = null;
      try
      {
        body.Reset(options);
        WriteContext c = new WriteContext(serializer) { W = body, Mode = IdMode.Names, IdSettings = options };
        DataCache cache = DataCache.Of(serializer);
        RowPlan plan = null;
        int count = 0;
        bool check = true;
        while (reader.Read())
        {
          if (plan is null) // the columns are decided by the first row (a provider may give the field types of the current row)
            plan = cache.Plan(serializer, reader, typeName);
          else if (count == NullChecks.CheckedRows)
            check = !NullChecks.CanSkip(reader);
          plan.WriteRow(c, reader, check);
          count++;
          if (body.Pos >= ChunkSize)
          {
            if (chunks is null)
              chunks = new Chunks();
            chunks.Add(body.Buf, body.Pos);
            body.Pos = 0;
          }
        }

        // The schema and the array header come before the rows (an empty list has no entry: the schema of an empty list)
        MsgPackWriter head = new MsgPackWriter(256);
        head.Reset(options);
        if (options._useInexedSchema)
          head.Raw(Schema(options, plan));
        head.ArrayHeader(count);
        if (target != null)
        {
          result = null;
          target.Write(head.Buf, 0, head.Pos);
          chunks?.CopyTo(target);
          body.CopyTo(target);
        }
        else
        {
          int moved = chunks is null ? 0 : chunks.Length;
          result = new byte[head.Pos + moved + body.Pos];
          Buffer.BlockCopy(head.Buf, 0, result, 0, head.Pos);
          chunks?.CopyTo(result, head.Pos);
          Buffer.BlockCopy(body.Buf, 0, result, head.Pos + moved, body.Pos);
        }
        return count;
      }
      finally
      {
        chunks?.Return();
        if (body.Buf.Length <= 1024 * 1024) // do not keep a huge buffer alive
          _body = body;
      }
    }

    /// <summary>
    /// The bytes of the rows moved out of the body writer, in chunks of <see cref="ChunkSize"/> from the shared array pool (each filled before the next).
    /// </summary>
    private sealed class Chunks
    {
      private readonly List<byte[]> _chunks = new List<byte[]>();
      internal int Length;

      internal void Add(byte[] source, int count)
      {
        int at = 0;
        while (at < count)
        {
          int used = Length % ChunkSize;
          if (used == 0)
            _chunks.Add(ArrayPool<byte>.Shared.Rent(ChunkSize));
          int part = Math.Min(ChunkSize - used, count - at);
          Buffer.BlockCopy(source, at, _chunks[_chunks.Count - 1], used, part);
          at += part;
          Length = checked(Length + part);
        }
      }

      internal void CopyTo(Stream target)
      {
        for (int t = 0; t < _chunks.Count; t++)
          target.Write(_chunks[t], 0, Math.Min(ChunkSize, Length - t * ChunkSize));
      }

      internal void CopyTo(byte[] result, int offset)
      {
        for (int t = 0; t < _chunks.Count; t++)
          Buffer.BlockCopy(_chunks[t], 0, result, offset + t * ChunkSize, Math.Min(ChunkSize, Length - t * ChunkSize));
      }

      internal void Return()
      {
        for (int t = _chunks.Count - 1; t >= 0; t--)
          ArrayPool<byte>.Shared.Return(_chunks[t]);
        _chunks.Clear();
      }
    }

    /// <summary>
    /// The schema inline, or with <see cref="MsgPackOptions.WriteSchemaReference"/> a reference to it (the schema is added to the options' store).
    /// </summary>
    private static byte[] Schema(LsMsgPackOptions options, RowPlan plan)
    {
      if (plan is null)
        return RowPlan.SchemaOf(options, null, null, null, options);
      if (!options._writeSchemaReference)
        return plan.PayloadSchema;
      if (options._schemaStore is null)
        throw new MsgPackException("MsgPackSettings.WriteSchemaReference needs a MsgPackSettings.SchemaStore to keep the schema in.");
      return SchemaStore.WriteReference(options._schemaStore.AddWritten(plan.CanonicalSchema, options._schemaVersion));
    }
  }
}
