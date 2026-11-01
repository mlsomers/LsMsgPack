using LsMsgPack;
using LtMsgPack.IO;
using LtMsgPack.Writing;
using System;
using System.Data;
using System.IO;

namespace LtMsgPack.Data
{
  /// <summary>
  /// Rows as one payload: the bytes of <c>Serialize(List&lt;T&gt;)</c> of a class with a property per column (the schema, then an array of known length). The rows are buffered for the array header.
  /// </summary>
  internal static class RowSerializer
  {
    [ThreadStatic]
    private static MsgPackWriter _body;

    internal static int Serialize(Serializer serializer, IDataReader reader, string typeName, Stream target, out byte[] result)
    {
      LtMsgPackOptions options = serializer.Options;
      if (serializer.Filters == FilterMode.Custom)
        throw new NotSupportedException("Rows are written with the default dynamic filter (FilterDefaultValues) or none, not with custom dynamic filters.");

      MsgPackWriter body = _body;
      _body = null; // a nested call (from a value) gets its own
      if (body is null)
        body = new MsgPackWriter(16 * 1024);
      try
      {
        body.Reset(options);
        WriteContext c = new WriteContext(serializer) { W = body, Mode = IdMode.Names, IdSettings = options };
        DataCache cache = DataCache.Of(serializer);
        RowPlan plan = null;
        int count = 0;
        while (reader.Read())
        {
          if (plan is null) // the columns are decided by the first row (a provider may give the field types of the current row)
            plan = cache.Plan(serializer, reader, typeName);
          plan.WriteRow(c, reader);
          count++;
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
          body.CopyTo(target);
        }
        else
        {
          result = new byte[head.Pos + body.Pos];
          Buffer.BlockCopy(head.Buf, 0, result, 0, head.Pos);
          Buffer.BlockCopy(body.Buf, 0, result, head.Pos, body.Pos);
        }
        return count;
      }
      finally
      {
        if (body.Buf.Length <= 1024 * 1024) // do not keep a huge buffer alive
          _body = body;
      }
    }

    /// <summary>
    /// The schema inline, or with <see cref="MsgPackOptions.WriteSchemaReference"/> a reference to it (the schema is added to the options' store).
    /// </summary>
    private static byte[] Schema(LtMsgPackOptions options, RowPlan plan)
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
