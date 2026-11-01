using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.Streaming;
using LtMsgPack.IO;
using LtMsgPack.Streaming;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace LtMsgPack.Data
{
  /// <summary>
  /// Reads tables of a stream (written by <see cref="StreamWriterDataExtensions"/>: a head with the schema of the columns, then the rows) as a data reader,
  /// e.g. to restore a dump with <c>SqlBulkCopy.WriteToServer(reader)</c>. Each table is a result set (<see cref="NextResult"/>).
  /// <para>The column names come from the schema. The column types from the table's index entry (<see cref="MsgPackDataStreams.ColumnsKey"/>, when the reader was opened at the entry),
  /// otherwise from schema version 2 (its portable kinds: <c>int</c> is an Int32, <c>float</c> a Double), otherwise <see cref="object"/> with the values as the serializer reads them into object.</para>
  /// </summary>
  public sealed class MsgPackDataReader : DbDataReader
  {
    private readonly MsgPackStreamReader _reader;
    private readonly bool _leaveOpen;
    private readonly bool _single; // opened at an index entry: that table only
    private readonly LtMsgPackOptions _options;
    private MsgPackStreamEntry _entry;
    private string _tableName;
    private string[] _names = new string[0];
    private Type[] _types = new Type[0];
    private string[] _dataTypeNames = new string[0];
    private Dictionary<string, int> _ordinals;
    private object[] _values;
    private bool _inTable; // the rows' array is open
    private bool _hasRows;
    private bool _onRow;
    private bool _closed;

    /// <summary>
    /// Reads the tables from the reader's position on (at <see cref="MsgPackStreamReader.Depth"/> 0, before a table's head): the first one is the current result set.
    /// </summary>
    /// <param name="leaveOpen">Do not dispose the stream reader when this one closes</param>
    public MsgPackDataReader(MsgPackStreamReader reader, bool leaveOpen = false) : this(reader, null, leaveOpen, true)
    {
    }

    /// <summary>
    /// Reads the table of an index entry (<see cref="MsgPackStreamReader.ReadIndexes"/>), with the column types of its metadata: the reader seeks to it (it needs a stream that seeks).
    /// </summary>
    public MsgPackDataReader(MsgPackStreamReader reader, MsgPackStreamEntry entry, bool leaveOpen = false) : this(reader, entry ?? throw new ArgumentNullException(nameof(entry)), leaveOpen, true)
    {
    }

    private MsgPackDataReader(MsgPackStreamReader reader, MsgPackStreamEntry entry, bool leaveOpen, bool open)
    {
      _reader = reader ?? throw new ArgumentNullException(nameof(reader));
      _leaveOpen = leaveOpen;
      _options = reader.Implementation.Options;
      _entry = entry;
      _single = entry != null;
      if (!open)
        return;
      if (entry != null)
        reader.Seek(entry);
      if (entry != null || !reader.AtEnd)
      {
        reader.ReadHead(typeof(object));
        Begin(reader.Read());
        _hasRows = !reader.AtEnd;
      }
    }

    /// <summary>
    /// <see cref="MsgPackDataReader(MsgPackStreamReader, bool)"/> with asynchronous reads (an HTTP response, a network stream).
    /// </summary>
    public static async Task<MsgPackDataReader> OpenAsync(MsgPackStreamReader reader, bool leaveOpen = false, CancellationToken cancellationToken = default(CancellationToken))
    {
      MsgPackDataReader data = new MsgPackDataReader(reader, null, leaveOpen, false);
      await data.OpenNextAsync(cancellationToken).ConfigureAwait(false);
      return data;
    }

    /// <summary>
    /// The name of the current table: the identifier of its index entry when opened at one, otherwise the name of its rows in the schema.
    /// </summary>
    public string TableName { get { return _tableName; } }

    #region Tables

    /// <summary>
    /// The head was read, <paramref name="body"/>: the token after it was read.
    /// </summary>
    private void Begin(bool body)
    {
      if (!body || (_reader.TokenType != MsgPackTokenType.ArrayStart))
        throw new MsgPackException($"A table is a head followed by an array of rows, the head is followed by {(body ? _reader.TokenType.ToString() : "the end of the stream")}.", _reader.TokenStart);
      Columns();
      _inTable = true;
      _onRow = false;
    }

    /// <summary>
    /// The columns of the head's schema (the entry of the rows: the root's elements in version 2, otherwise the first entry), with their types.
    /// </summary>
    private void Columns()
    {
      _tableName = _entry?.Identifier as string ?? (_entry?.Identifier is null ? null : Convert.ToString(_entry.Identifier, CultureInfo.InvariantCulture));
      _names = new string[0];
      _types = new Type[0];
      _dataTypeNames = new string[0];
      _ordinals = null;
      if (!_reader.TryGetHeadSchema(out byte[] schema, out MsgPackOptions byteOrder))
        return;

      LtMsgPackOptions plain = new LtMsgPackOptions() { EndianAction = byteOrder.EndianAction, UseInexedSchema = false };
      KeyValuePair<object, object>[] entries = new MsgPackReader(schema, 0, schema.Length, plain).ReadPlain(0) as KeyValuePair<object, object>[];
      if (entries is null || entries.Length == 0)
        return;

      int rows = 0;
      for (int t = 0; t < entries.Length; t++) // version 2: the root entry "" says which entry the rows have: [["@", ["list", n]]]
        if ("".Equals(entries[t].Key) && entries[t].Value is object[] root && root.Length == 1 && root[0] is object[] content && content.Length == 2
          && content[1] is object[] list && list.Length == 2 && "list".Equals(list[0]) && IsInteger(list[1]))
          rows = Convert.ToInt32(list[1], CultureInfo.InvariantCulture);
      if (rows >= entries.Length || !(entries[rows].Value is object[] items))
        return;

      _tableName = _tableName ?? entries[rows].Key as string;
      List<string> names = new List<string>(items.Length);
      List<Type> types = new List<Type>(items.Length);
      for (int t = 0; t < items.Length; t++)
      {
        if (items[t] is string name) // version 1
        {
          names.Add(name);
          types.Add(typeof(object));
        }
        else if (items[t] is object[] described && described.Length >= 2 && described[0] is string describedName && describedName != MsgPackOptions.ContentKey)
        {
          names.Add(describedName);
          types.Add(ColumnTypes.FromKind(described[1]));
        }
      }
      _names = names.ToArray();
      _types = types.ToArray();
      _dataTypeNames = new string[_names.Length];
      FromMetadata();
    }

    /// <summary>
    /// The exact types and the provider's type names of the columns, from the index entry the reader was opened at.
    /// </summary>
    private void FromMetadata()
    {
      if (_entry?.Metadata is null || !_entry.Metadata.TryGetValue(MsgPackDataStreams.ColumnsKey, out object described) || !(described is IList columns) || columns.Count != _names.Length)
        return;
      for (int t = 0; t < _names.Length; t++)
      {
        if (!(columns[t] is IList column) || column.Count < 2 || !_names[t].Equals(column[0]))
          continue;
        Type type = ColumnTypes.FromName(column[1]);
        if (type != null)
          _types[t] = type;
        if (column.Count > 2)
          _dataTypeNames[t] = column[2] as string;
      }
    }

    private static bool IsInteger(object value)
    {
      return value is long || value is ulong || value is int || value is uint || value is short || value is ushort || value is byte || value is sbyte;
    }

    /// <summary>
    /// Passes the rows that were not read and the end of the current table.
    /// </summary>
    private void EndTable()
    {
      if (!_inTable)
        return;
      while (!_reader.AtEnd)
      {
        _reader.Read();
        _reader.Skip();
      }
      _reader.Read(); // the end of the rows' array
      _inTable = false;
      _onRow = false;
    }

    private async Task EndTableAsync(CancellationToken cancellationToken)
    {
      if (!_inTable)
        return;
      while (!await _reader.AtEndAsync(cancellationToken).ConfigureAwait(false))
      {
        await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        await _reader.SkipAsync(cancellationToken).ConfigureAwait(false);
      }
      await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
      _inTable = false;
      _onRow = false;
    }

    private async Task<bool> OpenNextAsync(CancellationToken cancellationToken)
    {
      if (await _reader.AtEndAsync(cancellationToken).ConfigureAwait(false))
        return false;
      await _reader.ReadHeadAsync(typeof(object), cancellationToken).ConfigureAwait(false);
      Begin(await _reader.ReadAsync(cancellationToken).ConfigureAwait(false));
      _hasRows = !await _reader.AtEndAsync(cancellationToken).ConfigureAwait(false);
      return true;
    }

    public override bool NextResult()
    {
      ThrowIfClosed();
      EndTable();
      if (_single || _reader.AtEnd)
      {
        Clear();
        return false;
      }
      _reader.ReadHead(typeof(object));
      Begin(_reader.Read());
      _hasRows = !_reader.AtEnd;
      return true;
    }

    public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
      ThrowIfClosed();
      await EndTableAsync(cancellationToken).ConfigureAwait(false);
      if (_single || !await OpenNextAsync(cancellationToken).ConfigureAwait(false))
      {
        Clear();
        return false;
      }
      return true;
    }

    private void Clear()
    {
      _names = new string[0];
      _types = new Type[0];
      _dataTypeNames = new string[0];
      _ordinals = null;
      _hasRows = false;
      _tableName = null;
    }

    #endregion

    #region Rows

    public override bool Read()
    {
      ThrowIfClosed();
      if (!_inTable)
        return false;
      if (_reader.AtEnd)
      {
        EndTable();
        return false;
      }
      _reader.Read();
      SetRow(_reader.GetValue());
      return true;
    }

    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
      ThrowIfClosed();
      if (!_inTable)
        return false;
      if (await _reader.AtEndAsync(cancellationToken).ConfigureAwait(false))
      {
        await EndTableAsync(cancellationToken).ConfigureAwait(false);
        return false;
      }
      await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
      SetRow(await _reader.GetValueAsync(cancellationToken).ConfigureAwait(false));
      return true;
    }

    /// <summary>
    /// The values of a row (an array in the column order, or a map of positions or names), converted to the column types.
    /// </summary>
    private void SetRow(object row)
    {
      int count = _names.Length;
      if (_values is null || _values.Length != count)
        _values = new object[count];
      for (int t = 0; t < count; t++)
        _values[t] = DBNull.Value;

      if (row is object[] items)
      {
        for (int t = Math.Min(count, items.Length) - 1; t >= 0; t--) // trimmed nils at the end stay DBNull
          _values[t] = Converted(items[t], t);
      }
      else if (row is KeyValuePair<object, object>[] pairs)
      {
        for (int t = 0; t < pairs.Length; t++)
        {
          int ordinal = pairs[t].Key is string name ? IndexOf(name) : IsInteger(pairs[t].Key) ? Convert.ToInt32(pairs[t].Key, CultureInfo.InvariantCulture) : -1;
          if (ordinal >= 0 && ordinal < count)
            _values[ordinal] = Converted(pairs[t].Value, ordinal);
        }
      }
      else if (row != null)
        throw new MsgPackException($"A row is an array or a map, not a {row.GetType().Name}.", _reader.TokenStart);
      _onRow = true;
    }

    private object Converted(object value, int ordinal)
    {
      if (value is null)
        return DBNull.Value;
      Type type = _types[ordinal];
      if (type == typeof(object) || type.IsInstanceOfType(value))
        return value;
      return ValueConverter.ConvertDeserializeValue(value, type, _options, null);
    }

    private int IndexOf(string name)
    {
      for (int t = 0; t < _names.Length; t++)
        if (string.Equals(_names[t], name, StringComparison.Ordinal))
          return t;
      return -1;
    }

    private object Value(int ordinal)
    {
      if (!_onRow)
        throw new InvalidOperationException("The reader is not on a row: call Read first.");
      if ((uint)ordinal >= (uint)_values.Length)
        throw new IndexOutOfRangeException($"There is no column {ordinal}, the table has {_values.Length}.");
      return _values[ordinal];
    }

    /// <summary>
    /// The value as <typeparamref name="T"/>: the column's value, or converted as the serializer converts values.
    /// </summary>
    private T As<T>(int ordinal)
    {
      object value = Value(ordinal);
      if (value is T typed)
        return typed;
      if (value is DBNull)
        throw new InvalidCastException($"Column {ordinal} ({_names[ordinal]}) is null.");
      return (T)ValueConverter.ConvertDeserializeValue(value, typeof(T), _options, null);
    }

    public override object GetValue(int ordinal) { return Value(ordinal); }

    public override int GetValues(object[] values)
    {
      int count = Math.Min(values.Length, _names.Length);
      for (int t = 0; t < count; t++)
        values[t] = Value(t);
      return count;
    }

    public override bool IsDBNull(int ordinal) { return Value(ordinal) is DBNull; }
    public override T GetFieldValue<T>(int ordinal) { return As<T>(ordinal); }
    public override bool GetBoolean(int ordinal) { return As<bool>(ordinal); }
    public override byte GetByte(int ordinal) { return As<byte>(ordinal); }
    public override char GetChar(int ordinal) { return As<char>(ordinal); }
    public override DateTime GetDateTime(int ordinal) { return As<DateTime>(ordinal); }
    public override decimal GetDecimal(int ordinal) { return As<decimal>(ordinal); }
    public override double GetDouble(int ordinal) { return As<double>(ordinal); }
    public override float GetFloat(int ordinal) { return As<float>(ordinal); }
    public override Guid GetGuid(int ordinal) { return As<Guid>(ordinal); }
    public override short GetInt16(int ordinal) { return As<short>(ordinal); }
    public override int GetInt32(int ordinal) { return As<int>(ordinal); }
    public override long GetInt64(int ordinal) { return As<long>(ordinal); }
    public override string GetString(int ordinal) { return As<string>(ordinal); }

    public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length)
    {
      byte[] bytes = As<byte[]>(ordinal);
      if (buffer is null)
        return bytes.Length;
      int count = (int)Math.Max(0, Math.Min(length, bytes.Length - dataOffset));
      Buffer.BlockCopy(bytes, (int)dataOffset, buffer, bufferOffset, count);
      return count;
    }

    public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length)
    {
      string text = As<string>(ordinal);
      if (buffer is null)
        return text.Length;
      int count = (int)Math.Max(0, Math.Min(length, text.Length - dataOffset));
      text.CopyTo((int)dataOffset, buffer, bufferOffset, count);
      return count;
    }

    #endregion

    #region Columns

    public override int FieldCount { get { return _names.Length; } }
    public override string GetName(int ordinal) { return _names[ordinal]; }
#if NET5_0_OR_GREATER
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    [UnconditionalSuppressMessage("Trimming", "IL2063", Justification = "The column types are framework types of a fixed list (ColumnTypes), never types the data names.")]
#endif
    public override Type GetFieldType(int ordinal) { return _types[ordinal]; }
    public override string GetDataTypeName(int ordinal) { return _dataTypeNames[ordinal] ?? _types[ordinal].Name; }

    public override int GetOrdinal(string name)
    {
      if (_ordinals is null)
      {
        Dictionary<string, int> ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int t = _names.Length - 1; t >= 0; t--) // the first of names that differ only in case
          ordinals[_names[t]] = t;
        _ordinals = ordinals;
      }
      int exact = IndexOf(name);
      if (exact >= 0)
        return exact;
      if (_ordinals.TryGetValue(name, out int ordinal))
        return ordinal;
      throw new IndexOutOfRangeException($"The table has no column \"{name}\".");
    }

    public override object this[int ordinal] { get { return GetValue(ordinal); } }
    public override object this[string name] { get { return GetValue(GetOrdinal(name)); } }

    /// <summary>
    /// The columns as ADO.NET describes them (ColumnName, ColumnOrdinal, DataType, DataTypeName, AllowDBNull), e.g. for SqlBulkCopy.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2111", Justification = "A column of type Type in the schema table, as ADO.NET's own readers have it: its values are the framework types of the columns.")]
    public override DataTable GetSchemaTable()
    {
      DataTable table = new DataTable("SchemaTable");
      table.Columns.Add("ColumnName", typeof(string));
      table.Columns.Add("ColumnOrdinal", typeof(int));
      table.Columns.Add("ColumnSize", typeof(int));
      table.Columns.Add("DataType", typeof(Type));
      table.Columns.Add("DataTypeName", typeof(string));
      table.Columns.Add("AllowDBNull", typeof(bool));
      for (int t = 0; t < _names.Length; t++)
        table.Rows.Add(_names[t], t, -1, _types[t], GetDataTypeName(t), true);
      return table;
    }

    #endregion

    #region State

    public override int Depth { get { return 0; } }
    public override bool HasRows { get { return _hasRows; } }
    public override bool IsClosed { get { return _closed; } }
    public override int RecordsAffected { get { return -1; } }
    public override IEnumerator GetEnumerator() { return new DbEnumerator(this); }

    public override void Close()
    {
      if (_closed)
        return;
      _closed = true;
      if (!_leaveOpen)
        _reader.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        Close();
      base.Dispose(disposing);
    }

    private void ThrowIfClosed()
    {
      if (_closed)
        throw new InvalidOperationException("The reader is closed.");
    }

    #endregion
  }
}
