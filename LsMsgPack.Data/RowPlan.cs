using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.IO;
using LsMsgPack.Writing;
using System;
using System.Data;
using System.Data.Common;

namespace LsMsgPack.Data
{
  /// <summary>
  /// How the rows of a result set are written: per column a writer for its field type, the property ids, and the schema (one entry with the column names).
  /// The bytes of a row are the ones of an object of a class with a property per column (its name, its field type, Nullable for a value type), in the order of the columns.
  /// </summary>
  internal sealed class RowPlan
  {
    internal readonly string TypeName;
    internal readonly string[] Names;
    internal readonly Type[] Types;

    /// <summary>
    /// The provider's names of the column types (<see cref="IDataRecord.GetDataTypeName"/>), null where it has none.
    /// </summary>
    internal readonly string[] DataTypeNames;
    private readonly ColumnWriter[] _columns;
    private readonly LsMsgPackOptions _options;

    /// <summary>
    /// The encoded property ids for the map layout (the names, or with the indexed schema the positions), null for the array layout.
    /// </summary>
    private readonly byte[][] _keys;
    private readonly bool _trim;
    private byte[] _payloadSchema;
    private byte[] _canonicalSchema;

    private RowPlan(Serializer serializer, IDataRecord record, string typeName)
    {
      LsMsgPackOptions options = serializer.Options;
      _options = options;
      TypeName = typeName;
      int count = record.FieldCount;
      Names = new string[count];
      Types = new Type[count];
      DataTypeNames = new string[count];
      _columns = new ColumnWriter[count];
      bool dbReader = record is DbDataReader;
      for (int t = 0; t < count; t++)
      {
        Names[t] = record.GetName(t);
        Types[t] = FieldType(record, t);
        DataTypeNames[t] = DataTypeName(record, t);
        _columns[t] = ColumnWriter.Create(serializer, Types[t], t, dbReader);
      }
      ThrowIfNamesNotUnique(Names);

      if (options._objectLayout == ObjectLayout.Map)
      {
        _keys = new byte[count][];
        for (int t = 0; t < count; t++)
          _keys[t] = options._useInexedSchema ? serializer.EncodeKey(t) : serializer.EncodeKey(Names[t]);
      }
      _trim = options._trimTrailingNulls;
    }

    /// <summary>
    /// The plan of the columns of <paramref name="record"/>, <paramref name="last"/> when it has the same ones.
    /// </summary>
    /// <param name="record">On its first row when it has one: some providers give the field types of the current row (SQLite)</param>
    internal static RowPlan For(Serializer serializer, IDataRecord record, string typeName, RowPlan last)
    {
      if (last != null && last.Matches(record, typeName))
        return last;
      return new RowPlan(serializer, record, typeName);
    }

    private bool Matches(IDataRecord record, string typeName)
    {
      if (!string.Equals(TypeName, typeName, StringComparison.Ordinal) || record.FieldCount != Names.Length)
        return false;
      for (int t = 0; t < Names.Length; t++)
        if (!string.Equals(record.GetName(t), Names[t], StringComparison.Ordinal) || FieldType(record, t) != Types[t])
          return false;
      return true;
    }

    /// <summary>
    /// The field type, object when the provider cannot tell it (some do not before the first row).
    /// </summary>
    private static Type FieldType(IDataRecord record, int ordinal)
    {
      try
      {
        return record.GetFieldType(ordinal) ?? typeof(object);
      }
      catch (InvalidOperationException)
      {
        return typeof(object);
      }
    }

    private static string DataTypeName(IDataRecord record, int ordinal)
    {
      try
      {
        return record.GetDataTypeName(ordinal);
      }
      catch (Exception) // a provider that has none, or not before the first row
      {
        return null;
      }
    }

    internal SchemaVersion Version { get { return _options._schemaVersion; } }

    /// <summary>
    /// The schema in the byte order of the options (inline in a payload of <see cref="RowSerializer"/>).
    /// </summary>
    internal byte[] PayloadSchema
    {
      get { return _payloadSchema ?? (_payloadSchema = SchemaOf(_options, TypeName, Names, Types, _options)); }
    }

    /// <summary>
    /// The schema in the byte order of the specification (kept by a <see cref="SchemaStore"/>, the stream writer writes it as its options say).
    /// </summary>
    internal byte[] CanonicalSchema
    {
      get { return _canonicalSchema ?? (_canonicalSchema = SchemaOf(_options, TypeName, Names, Types, SchemaBytes.Canonical)); }
    }

    /// <summary>
    /// The schema of the rows: <c>{ typeName: [column, ...] }</c> (version 2: <c>[column, type]</c> and the root entry), the schema of an empty list when <paramref name="typeName"/> is null (no row).
    /// </summary>
    /// <param name="types">The field types of the columns (version 2 only)</param>
    /// <param name="byteOrder">The <see cref="MsgPackOptions.EndianAction"/> of the lengths</param>
    internal static byte[] SchemaOf(LsMsgPackOptions options, string typeName, string[] names, Type[] types, MsgPackOptions byteOrder)
    {
      ByteWriter bytes = new ByteWriter();
      if (options._schemaVersion == SchemaVersion.Described)
      {
        DescribedSchema.WriteColumns(bytes, typeName, names, types, options, byteOrder);
        return bytes.ToArray();
      }

      if (typeName is null)
      {
        bytes.WriteMapHeader(0, byteOrder);
        return bytes.ToArray();
      }
      bytes.WriteMapHeader(1, byteOrder);
      bytes.WriteString(typeName, byteOrder);
      bytes.WriteArrayHeader(names.Length, byteOrder);
      for (int t = 0; t < names.Length; t++)
        bytes.WriteString(names[t], byteOrder);
      return bytes.ToArray();
    }

    private static void ThrowIfNamesNotUnique(string[] names)
    {
      for (int t = 0; t < names.Length; t++)
      {
        if (string.IsNullOrEmpty(names[t]) || names[t] == MsgPackOptions.ContentKey)
          throw new MsgPackException($"Column {t} has the name \"{names[t]}\", which cannot be a property id: give it a name (an alias in the query).");
        for (int u = t + 1; u < names.Length; u++)
          if (string.Equals(names[t], names[u], StringComparison.Ordinal))
            throw new MsgPackException($"Columns {t} and {u} are both called \"{names[t]}\": property ids must be unique, give one of them an alias in the query.");
      }
    }

    /// <summary>
    /// Writes the current row into <c>c.W</c> (ObjectPlan.Write of an object without a type id).
    /// </summary>
    /// <param name="check">IsDBNull on every column (see <see cref="NullChecks"/>)</param>
    internal void WriteRow(WriteContext c, IDataRecord record, bool check)
    {
      MsgPackWriter w = c.W;
      ColumnWriter[] columns = _columns;
      if (_keys != null)
      {
        int at = w.ReserveMapHeader(columns.Length);
        int written = 0;
        for (int t = 0; t < columns.Length; t++)
          if (columns[t].Write(c, record, _keys[t], check))
            written++;
        w.PatchMapHeader(at, columns.Length, written);
        return;
      }

      if (!_trim)
      {
        w.ArrayHeader(columns.Length);
        for (int t = 0; t < columns.Length; t++)
          if (!columns[t].Write(c, record, null, check))
            w.Nil();
        return;
      }

      // TrimTrailingNulls (ObjectPlan.WriteTrimmed): the nils after the last other value are taken back
      int header = w.ReserveArrayHeader(columns.Length);
      int count = 0;
      int end = w.Pos;
      for (int t = 0; t < columns.Length; t++)
      {
        int start = w.Pos;
        if (!columns[t].Write(c, record, null, check))
          w.Nil();
        if (w.Pos != start + 1 || w.Buf[start] != 0xC0)
        {
          count = t + 1;
          end = w.Pos;
        }
      }
      w.Pos = end;
      w.PatchArrayHeader(header, columns.Length, count);
    }
  }
}
