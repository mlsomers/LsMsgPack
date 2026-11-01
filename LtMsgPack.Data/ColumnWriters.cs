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
    /// <param name="check">IsDBNull before the getter, false: only for a column that had a NULL (see <see cref="NullChecks"/>)</param>
    /// <returns>false when the filters left the value out (nothing was written)</returns>
    internal abstract bool Write(WriteContext c, IDataRecord record, byte[] key, bool check);

    /// <summary>
    /// A NULL: the default value of a reference type or Nullable&lt;T&gt;, left out by the default filter.
    /// </summary>
    protected static bool WriteNull(WriteContext c, byte[] key, bool noFilters)
    {
      if (!noFilters)
        return false;
      if (key != null)
        c.W.Raw(key);
      c.W.Nil();
      return true;
    }

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
      if (type == typeof(byte[])) return new BoxedColumnWriter<byte[]>(serializer, ordinal);
      if (dbReader && type != typeof(object))
      {
        ColumnWriter typed = CreateFieldValue(serializer, type, ordinal);
        if (typed != null)
          return typed;
      }
      return new BoxedColumnWriter<object>(serializer, ordinal);
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

    /// <summary>
    /// The column had a NULL (in any call with this plan): it is checked on every row. Only ever set to true (by any thread).
    /// </summary>
    private bool _hadNull;

    internal ColumnWriter(Serializer serializer, int ordinal, Func<IDataRecord, int, T> get) : base(ordinal)
    {
      _get = get;
      _handler = serializer.Handler<T>(null);
      _info = new FullPropertyInfo(typeof(T));
      _noFilters = serializer.Filters == FilterMode.None;
    }

    internal override bool Write(WriteContext c, IDataRecord record, byte[] key, bool check)
    {
      int ordinal = Ordinal;
      if (!check && !_hadNull)
        return WriteUnchecked(c, record, key, ordinal);
      if (record.IsDBNull(ordinal))
      {
        if (!_hadNull)
          FirstNull(record, ordinal);
        return WriteNull(c, key, _noFilters);
      }
      return WriteValue(c, _get(record, ordinal), key);
    }

    private bool WriteValue(WriteContext c, T value, byte[] key)
    {
      if (value == null) // a reader that gives null for a NULL (not a value type)
        return WriteNull(c, key, _noFilters);
      if (key != null)
        c.W.Raw(key);
      _handler.Write(c, value, _info);
      return true;
    }

    private void FirstNull(IDataRecord record, int ordinal)
    {
      _hadNull = true;
      NullChecks.Learn(record, ordinal, _get);
    }

    /// <summary>
    /// The getter without IsDBNull (apart from Write: a method with a try block is not inlined, and the checked path stays as it was).
    /// </summary>
    private bool WriteUnchecked(WriteContext c, IDataRecord record, byte[] key, int ordinal)
    {
      T value;
      try
      {
        value = _get(record, ordinal);
      }
      catch (Exception) when (record.IsDBNull(ordinal)) // a NULL after all
      {
        _hadNull = true;
        return WriteNull(c, key, _noFilters);
      }
      return WriteValue(c, value, key);
    }
  }

  /// <summary>
  /// A column read with <see cref="IDataRecord.GetValue"/> (object, byte[]): DBNull is the NULL, no IsDBNull needed.
  /// </summary>
  internal sealed class BoxedColumnWriter<T> : ColumnWriter where T : class
  {
    private readonly ValueHandler<T> _handler;
    private readonly FullPropertyInfo _info;
    private readonly bool _noFilters;

    internal BoxedColumnWriter(Serializer serializer, int ordinal) : base(ordinal)
    {
      _handler = serializer.Handler<T>(null);
      _info = new FullPropertyInfo(typeof(T));
      _noFilters = serializer.Filters == FilterMode.None;
    }

    internal override bool Write(WriteContext c, IDataRecord record, byte[] key, bool check)
    {
      object value = record.GetValue(Ordinal);
      if (value is null || value is DBNull)
        return WriteNull(c, key, _noFilters);
      if (key != null)
        c.W.Raw(key);
      _handler.Write(c, (T)value, _info);
      return true;
    }
  }
}
