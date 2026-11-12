using LsMsgPack.Streaming;
using LsMsgPack.IO;
using System.Collections.Generic;
using System.Data;

namespace LsMsgPack.Data
{
  /// <summary>
  /// One result set as a root value of a stream: its index entry (named, with the columns), a head with the schema of the columns, and an array of unknown length of the rows.
  /// </summary>
  internal sealed class TableWriter
  {
    private readonly MsgPackStreamWriter _writer;
    private readonly IDataReader _reader;
    private readonly RowPlan _plan;
    private readonly int _point = -1;
    private object _lastKey;
    private long _rows;
    private bool _check = true;

    /// <param name="reader">On its first row when the result set has one (it decides the field types of some providers)</param>
    internal TableWriter(MsgPackStreamWriter writer, IDataReader reader, object identifier, string name, MsgPackTableOptions options)
    {
      _writer = writer;
      _reader = reader;
      Serializer serializer = writer.Implementation;
      string typeName = options?.RowTypeName ?? name ?? MsgPackDataStreams.DefaultTypeName;
      _plan = DataCache.Of(serializer).Plan(serializer, reader, typeName);

      if (writer.KeepsIndex)
      {
        MsgPackStreamEntry entry = writer.BeginEntry(identifier, MsgPackDataStreams.ContentType);
        if (options is null || options.ColumnMetadata)
          entry.Metadata = new Dictionary<string, object> { { MsgPackDataStreams.ColumnsKey, ColumnTypes.Describe(_plan) } };
        if (options?.PointColumn != null)
          _point = reader.GetOrdinal(options.PointColumn);
      }

      writer.WriteHead(_plan.CanonicalSchema, _plan.Version);
      writer.WriteArrayStart();
    }

    internal long Rows { get { return _rows; } }

    /// <summary>
    /// Writes the current row (one value: the checks and the index see it as an element of the array).
    /// </summary>
    internal void WriteRow()
    {
      if (_point >= 0)
      {
        object key = _reader.GetValue(_point);
        if (_rows == 0 || !Equals(key, _lastKey))
        {
          _writer.AddPoint(ColumnTypes.PlainKey(key));
          _lastKey = key;
        }
      }

      if (_rows == NullChecks.CheckedRows)
        _check = !NullChecks.CanSkip(_reader);
      MsgPackWriter w = _writer.BeginValue();
      int start = w.Pos;
      try
      {
        _plan.WriteRow(_writer.Context, _reader, _check);
      }
      catch
      {
        w.Pos = start; // nothing of the row is written
        throw;
      }
      _writer.EndValue();
      _rows++;
    }

    internal void End()
    {
      _writer.WriteEnd();
    }
  }
}
