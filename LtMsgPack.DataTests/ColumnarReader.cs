using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;

namespace LtMsgPack.DataTests
{
  /// <summary>
  /// An in-memory DbDataReader over typed column arrays: the typed getters do not box, GetValue does (as the providers do), so the mappers' own costs show.
  /// The typed getters give the array's value for a NULL (default or null), see <see cref="ThrowingColumnarReader"/> for one that throws as the providers do.
  /// </summary>
  public class ColumnarReader : DbDataReader
  {
    private readonly string[] _names;
    private readonly Column[] _columns;
    private readonly int _rows;
    private readonly bool _throwsOnNull;
    private int _row = -1;

    public abstract class Column
    {
      internal abstract Type Type { get; }
      internal abstract object Boxed(int row);
      internal abstract bool IsNull(int row);
    }

    public sealed class Column<T> : Column
    {
      internal readonly T[] Values;
      internal readonly bool[] Nulls;

      public Column(T[] values, bool[] nulls = null)
      {
        Values = values;
        Nulls = nulls;
      }

      internal override Type Type { get { return typeof(T); } }
      internal override object Boxed(int row) { return IsNull(row) ? (object)DBNull.Value : Values[row]; }
      internal override bool IsNull(int row) { return (Nulls != null && Nulls[row]) || Values[row] == null; }
    }

    public ColumnarReader(string[] names, Column[] columns, int rows) : this(names, columns, rows, false) { }

    protected ColumnarReader(string[] names, Column[] columns, int rows, bool throwsOnNull)
    {
      _names = names;
      _columns = columns;
      _rows = rows;
      _throwsOnNull = throwsOnNull;
    }

    public void Reset() { _row = -1; }

    /// <summary>
    /// The row whose values (of the columns from <see cref="FailFromColumn"/> on) throw, as a connection that breaks in the middle of a row; -1: none.
    /// </summary>
    public int FailAtRow { get; set; } = -1;

    public int FailFromColumn { get; set; }

    private T Get<T>(int ordinal)
    {
      if (_row == FailAtRow && ordinal >= FailFromColumn)
        throw new IOException("The connection broke.");
      if (_throwsOnNull && _columns[ordinal].IsNull(_row))
        throw new InvalidCastException($"Column {ordinal} is NULL, check with IsDBNull first.");
      Column<T> column = _columns[ordinal] as Column<T>;
      if (column is null)
        throw new InvalidCastException($"Column {ordinal} is a {_columns[ordinal].Type.Name}, not a {typeof(T).Name}.");
      return column.Values[_row];
    }

    public override bool Read() { return ++_row < _rows; }
    public override int FieldCount { get { return _columns.Length; } }
    public override string GetName(int ordinal) { return _names[ordinal]; }
    public override Type GetFieldType(int ordinal) { return _columns[ordinal].Type; }
    public override bool IsDBNull(int ordinal) { return _columns[ordinal].IsNull(_row); }
    public override object GetValue(int ordinal) { return _columns[ordinal].Boxed(_row); }
    public override T GetFieldValue<T>(int ordinal) { return Get<T>(ordinal); }
    public override bool GetBoolean(int ordinal) { return Get<bool>(ordinal); }
    public override byte GetByte(int ordinal) { return Get<byte>(ordinal); }
    public override char GetChar(int ordinal) { return Get<char>(ordinal); }
    public override DateTime GetDateTime(int ordinal) { return Get<DateTime>(ordinal); }
    public override decimal GetDecimal(int ordinal) { return Get<decimal>(ordinal); }
    public override double GetDouble(int ordinal) { return Get<double>(ordinal); }
    public override float GetFloat(int ordinal) { return Get<float>(ordinal); }
    public override Guid GetGuid(int ordinal) { return Get<Guid>(ordinal); }
    public override short GetInt16(int ordinal) { return Get<short>(ordinal); }
    public override int GetInt32(int ordinal) { return Get<int>(ordinal); }
    public override long GetInt64(int ordinal) { return Get<long>(ordinal); }
    public override string GetString(int ordinal) { return Get<string>(ordinal); }
    public override object this[int ordinal] { get { return GetValue(ordinal); } }
    public override object this[string name] { get { return GetValue(GetOrdinal(name)); } }
    public override int GetOrdinal(string name) { return Array.IndexOf(_names, name); }
    public override string GetDataTypeName(int ordinal) { return _columns[ordinal].Type.Name; }
    public override int GetValues(object[] values)
    {
      int count = Math.Min(values.Length, _columns.Length);
      for (int t = 0; t < count; t++)
        values[t] = GetValue(t);
      return count;
    }
    public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) { throw new NotSupportedException(); }
    public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) { throw new NotSupportedException(); }
    public override int Depth { get { return 0; } }
    public override bool HasRows { get { return _rows > 0; } }
    public override bool IsClosed { get { return false; } }
    public override int RecordsAffected { get { return -1; } }
    public override bool NextResult() { return false; }
    public override IEnumerator GetEnumerator() { return new DbEnumerator(this); }
  }

  /// <summary>
  /// A <see cref="ColumnarReader"/> whose typed getters throw for a NULL, as the providers' do (a type of its own: the add-on learns per reader type whether they throw).
  /// </summary>
  public sealed class ThrowingColumnarReader : ColumnarReader
  {
    public ThrowingColumnarReader(string[] names, Column[] columns, int rows) : base(names, columns, rows, true) { }

    public int IsDBNullCalls { get; private set; }

    public override bool IsDBNull(int ordinal)
    {
      IsDBNullCalls++;
      return base.IsDBNull(ordinal);
    }
  }
}
