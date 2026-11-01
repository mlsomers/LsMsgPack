using LsMsgPack;
using LsMsgPack.Meta;
using LtMsgPack.Writing;
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LtMsgPack.Data
{
  /// <summary>
  /// Writes the value of one column of the current row, as the serializer writes a property of the column's type (the same handler, the same filters).
  /// </summary>
  internal abstract class ColumnWriter
  {
    internal readonly int Ordinal;

    protected ColumnWriter(int ordinal)
    {
      Ordinal = ordinal;
    }

    /// <param name="key">The encoded property id, null for the array layout (only the value)</param>
    /// <returns>false when the filters left the value out (nothing was written)</returns>
    internal abstract bool Write(WriteContext c, IDataRecord record, byte[] key);

    /// <param name="type">The field type of the column (<see cref="IDataRecord.GetFieldType"/>)</param>
    internal static ColumnWriter Create(Serializer serializer, Type type, int ordinal, bool dbReader)
    {
      // The typed getters of IDataRecord: no boxing (DbDataReader.GetFieldValue<T> is typed for the other types, its base implementation boxes)
      if (type == typeof(int)) return new ColumnWriter<int>(serializer, ordinal, (r, i) => r.GetInt32(i));
      if (type == typeof(long)) return new ColumnWriter<long>(serializer, ordinal, (r, i) => r.GetInt64(i));
      if (type == typeof(string)) return new ColumnWriter<string>(serializer, ordinal, (r, i) => r.GetString(i));
      if (type == typeof(short)) return new ColumnWriter<short>(serializer, ordinal, (r, i) => r.GetInt16(i));
      if (type == typeof(byte)) return new ColumnWriter<byte>(serializer, ordinal, (r, i) => r.GetByte(i));
      if (type == typeof(bool)) return new ColumnWriter<bool>(serializer, ordinal, (r, i) => r.GetBoolean(i));
      if (type == typeof(double)) return new ColumnWriter<double>(serializer, ordinal, (r, i) => r.GetDouble(i));
      if (type == typeof(float)) return new ColumnWriter<float>(serializer, ordinal, (r, i) => r.GetFloat(i));
      if (type == typeof(decimal)) return new ColumnWriter<decimal>(serializer, ordinal, (r, i) => r.GetDecimal(i));
      if (type == typeof(DateTime)) return new ColumnWriter<DateTime>(serializer, ordinal, (r, i) => r.GetDateTime(i));
      if (type == typeof(Guid)) return new ColumnWriter<Guid>(serializer, ordinal, (r, i) => r.GetGuid(i));
      if (type == typeof(char)) return new ColumnWriter<char>(serializer, ordinal, (r, i) => r.GetChar(i));
      if (type == typeof(byte[])) return new ColumnWriter<byte[]>(serializer, ordinal, (r, i) => (byte[])r.GetValue(i));
      if (dbReader && type != typeof(object))
      {
        ColumnWriter typed = CreateFieldValue(serializer, type, ordinal);
        if (typed != null)
          return typed;
      }
      return new ColumnWriter<object>(serializer, ordinal, (r, i) => r.GetValue(i));
    }

    /// <summary>
    /// Other types (DateTimeOffset, TimeSpan, provider types...) through <see cref="DbDataReader.GetFieldValue{T}"/>, null when the generic code cannot be made (NativeAOT: the value is written boxed).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = TrimReasons.Fallback)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.Fallback)]
    private static ColumnWriter CreateFieldValue(Serializer serializer, Type type, int ordinal)
    {
      try
      {
        MethodInfo factory = typeof(ColumnWriter).GetMethod(nameof(FieldValueWriter), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(type);
        return (ColumnWriter)factory.Invoke(null, new object[] { serializer, ordinal });
      }
      catch (Exception) // e.g. a runtime that cannot create the generic code
      {
        return null;
      }
    }

    private static ColumnWriter FieldValueWriter<T>(Serializer serializer, int ordinal)
    {
      return new ColumnWriter<T>(serializer, ordinal, (r, i) => ((DbDataReader)r).GetFieldValue<T>(i));
    }
  }

  /// <summary>
  /// A column of type <typeparamref name="T"/>, written as a property of type T (or Nullable&lt;T&gt; for a value type: the default filter only leaves out DBNull, a 0 stays a 0,
  /// so a reader into a Nullable property gets the value the database has).
  /// </summary>
  internal sealed class ColumnWriter<T> : ColumnWriter
  {
    private readonly Func<IDataRecord, int, T> _get;
    private readonly ValueHandler<T> _handler;
    private readonly FullPropertyInfo _info;
    private readonly bool _noFilters;

    internal ColumnWriter(Serializer serializer, int ordinal, Func<IDataRecord, int, T> get) : base(ordinal)
    {
      _get = get;
      _handler = serializer.Handler<T>(null);
      _info = new FullPropertyInfo(typeof(T));
      _noFilters = serializer.Filters == FilterMode.None;
    }

    internal override bool Write(WriteContext c, IDataRecord record, byte[] key)
    {
      int ordinal = Ordinal;
      if (record.IsDBNull(ordinal)) // null: the default value of a reference type or Nullable<T>
      {
        if (!_noFilters)
          return false;
        if (key != null)
          c.W.Raw(key);
        c.W.Nil();
        return true;
      }

      T value = _get(record, ordinal);
      if (key != null)
        c.W.Raw(key);
      if (value == null)
        c.W.Nil();
      else
        _handler.Write(c, value, _info);
      return true;
    }
  }
}
