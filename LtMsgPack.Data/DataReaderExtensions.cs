using System;
using System.Collections.Generic;
using System.Data;
using System.IO;

namespace LtMsgPack.Data
{
  /// <summary>
  /// Rows of a data reader as one MsgPack payload (the bytes of a <c>List&lt;T&gt;</c>, for any reader of that list), and rows into objects (a mapper like Dapper's, with the serializer's rules for properties).
  /// For rows written while they are read (no buffering), several result sets and table dumps with an index, see <see cref="StreamWriterDataExtensions"/>.
  /// </summary>
  public static class DataReaderExtensions
  {
    /// <summary>
    /// Writes the rows of the reader (from its position to the end of the current result set) as a list of objects with a property per column: the same bytes as
    /// <c>Serialize(List&lt;T&gt;)</c> of a class <paramref name="typeName"/> with those properties (the column names, the field types, Nullable for value types, in the column order),
    /// so <c>Deserialize&lt;List&lt;T&gt;&gt;</c> reads them. The rows are buffered (the array header needs their number). DBNull is nil, a 0 stays a 0.
    /// </summary>
    /// <param name="typeName">The name of the rows' entry in the indexed schema (readers pair the root's class with it whatever its name)</param>
    /// <returns>The number of rows</returns>
    /// <exception cref="NotSupportedException">With custom dynamic filters</exception>
    public static int SerializeRows(this LtMsgPackSerializer serializer, IDataReader reader, Stream target, string typeName = MsgPackDataStreams.DefaultTypeName)
    {
      Check(serializer, reader);
      if (target is null)
        throw new ArgumentNullException(nameof(target));
      return RowSerializer.Serialize(serializer.Implementation, reader, typeName ?? MsgPackDataStreams.DefaultTypeName, target, out byte[] result);
    }

    /// <inheritdoc cref="SerializeRows(LtMsgPackSerializer, IDataReader, Stream, string)"/>
    public static byte[] SerializeRows(this LtMsgPackSerializer serializer, IDataReader reader, string typeName = MsgPackDataStreams.DefaultTypeName)
    {
      Check(serializer, reader);
      RowSerializer.Serialize(serializer.Implementation, reader, typeName ?? MsgPackDataStreams.DefaultTypeName, null, out byte[] result);
      return result;
    }

    /// <summary>
    /// <see cref="SerializeRows(LtMsgPackSerializer, IDataReader, string)"/> with the entry named as the schema names <typeparamref name="T"/>.
    /// </summary>
    public static byte[] SerializeRows<T>(this LtMsgPackSerializer serializer, IDataReader reader)
    {
      Check(serializer, reader);
      RowSerializer.Serialize(serializer.Implementation, reader, serializer.Implementation.GetSchemaInfo(typeof(T)).Name, null, out byte[] result);
      return result;
    }

    /// <inheritdoc cref="SerializeRows{T}(LtMsgPackSerializer, IDataReader)"/>
    public static int SerializeRows<T>(this LtMsgPackSerializer serializer, IDataReader reader, Stream target)
    {
      Check(serializer, reader);
      if (target is null)
        throw new ArgumentNullException(nameof(target));
      return RowSerializer.Serialize(serializer.Implementation, reader, serializer.Implementation.GetSchemaInfo(typeof(T)).Name, target, out byte[] result);
    }

    /// <summary>
    /// Reads the rows of the reader (from its position to the end of the current result set) into objects of <typeparamref name="T"/>: the properties the serializer reads,
    /// matched to the columns by <c>[Column(Name = ...)]</c> or the property id, then by name ignoring case, then ignoring case and underscores; not the ones with <c>[NotMapped]</c>.
    /// Columns without a property are not read, DBNull leaves the property as the constructor made it, a column of another type is converted as the serializer converts values.
    /// </summary>
    public static List<T> ReadRows<T>(this LtMsgPackSerializer serializer, IDataReader reader) where T : class
    {
      Check(serializer, reader);
      Serializer implementation = serializer.Implementation;
      List<T> rows = new List<T>();
      RowMapper<T> mapper = null;
      bool check = true;
      while (reader.Read())
      {
        if (mapper is null)
          mapper = DataCache.Of(implementation).Mapper<T>(implementation, reader);
        else if (rows.Count == NullChecks.CheckedRows)
          check = !NullChecks.CanSkip(reader);
        rows.Add(mapper.Map(reader, check));
      }
      return rows;
    }

    /// <summary>
    /// <see cref="ReadRows{T}"/> one row at a time (the reader moves on as the rows are enumerated).
    /// </summary>
    public static IEnumerable<T> EnumerateRows<T>(this LtMsgPackSerializer serializer, IDataReader reader) where T : class
    {
      Check(serializer, reader);
      return Enumerate<T>(serializer.Implementation, reader);
    }

    private static IEnumerable<T> Enumerate<T>(Serializer serializer, IDataReader reader) where T : class
    {
      RowMapper<T> mapper = null;
      bool check = true;
      int count = 0;
      while (reader.Read())
      {
        if (mapper is null)
          mapper = DataCache.Of(serializer).Mapper<T>(serializer, reader);
        else if (count == NullChecks.CheckedRows)
          check = !NullChecks.CanSkip(reader);
        count++;
        yield return mapper.Map(reader, check);
      }
    }

    private static void Check(LtMsgPackSerializer serializer, IDataReader reader)
    {
      if (serializer is null)
        throw new ArgumentNullException(nameof(serializer));
      if (reader is null)
        throw new ArgumentNullException(nameof(reader));
    }
  }
}
