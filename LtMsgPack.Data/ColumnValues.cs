using LtMsgPack.Reading;
using System;

namespace LtMsgPack.Data
{
  /// <summary>
  /// The value of one column in the current row of <see cref="MsgPackDataReader"/>, read typed: with the serializer's reader of the column type (the same formats and conversions as a property of that type), not boxed.
  /// </summary>
  internal abstract class ColumnValue
  {
    /// <summary>
    /// nil, or not in the row (a trimmed nil at the end of an array, a key a map does not have).
    /// </summary>
    internal bool IsNull;

    /// <summary>
    /// Reads the value at <c>c.R</c> (not nil).
    /// </summary>
    internal abstract void Read(ReadContext c);

    /// <summary>
    /// The value (not null) as <see cref="System.Data.IDataRecord.GetValue"/> returns it.
    /// </summary>
    internal abstract object Boxed();

    /// <param name="type">A type of <see cref="ColumnTypes"/>, or object</param>
    internal static ColumnValue Create(Serializer serializer, Type type)
    {
      if (type == typeof(int)) return new ColumnValue<int>(serializer.Reader<int>());
      if (type == typeof(long)) return new ColumnValue<long>(serializer.Reader<long>());
      if (type == typeof(string)) return new ColumnValue<string>(serializer.Reader<string>());
      if (type == typeof(decimal)) return new ColumnValue<decimal>(serializer.Reader<decimal>());
      if (type == typeof(DateTime)) return new ColumnValue<DateTime>(serializer.Reader<DateTime>());
      if (type == typeof(bool)) return new ColumnValue<bool>(serializer.Reader<bool>());
      if (type == typeof(double)) return new ColumnValue<double>(serializer.Reader<double>());
      if (type == typeof(Guid)) return new ColumnValue<Guid>(serializer.Reader<Guid>());
      if (type == typeof(short)) return new ColumnValue<short>(serializer.Reader<short>());
      if (type == typeof(byte)) return new ColumnValue<byte>(serializer.Reader<byte>());
      if (type == typeof(float)) return new ColumnValue<float>(serializer.Reader<float>());
      if (type == typeof(byte[])) return new ColumnValue<byte[]>(serializer.Reader<byte[]>());
      if (type == typeof(DateTimeOffset)) return new ColumnValue<DateTimeOffset>(serializer.Reader<DateTimeOffset>());
      if (type == typeof(TimeSpan)) return new ColumnValue<TimeSpan>(serializer.Reader<TimeSpan>());
      if (type == typeof(char)) return new ColumnValue<char>(serializer.Reader<char>());
      if (type == typeof(sbyte)) return new ColumnValue<sbyte>(serializer.Reader<sbyte>());
      if (type == typeof(ushort)) return new ColumnValue<ushort>(serializer.Reader<ushort>());
      if (type == typeof(uint)) return new ColumnValue<uint>(serializer.Reader<uint>());
      if (type == typeof(ulong)) return new ColumnValue<ulong>(serializer.Reader<ulong>());
#if NET6_0_OR_GREATER
      if (type == typeof(DateOnly)) return new ColumnValue<DateOnly>(serializer.Reader<DateOnly>());
      if (type == typeof(TimeOnly)) return new ColumnValue<TimeOnly>(serializer.Reader<TimeOnly>());
#endif
      if (type == typeof(object))
        return new PlainColumnValue();
      return new BoxedColumnValue(serializer.Reader(type)); // DateOnly and TimeOnly in the .NET Standard builds
    }
  }

  internal sealed class ColumnValue<T> : ColumnValue
  {
    private readonly ValueReader<T> _reader;
    internal T Value;

    internal ColumnValue(ValueReader<T> reader)
    {
      _reader = reader;
    }

    internal override void Read(ReadContext c)
    {
      Value = _reader.Read(c, null);
    }

    internal override object Boxed()
    {
      return Value;
    }
  }

  /// <summary>
  /// A column of type object: the value as the serializer reads it into object.
  /// </summary>
  internal sealed class PlainColumnValue : ColumnValue
  {
    private object _value;

    internal override void Read(ReadContext c)
    {
      _value = c.R.ReadPlain(c.Depth);
    }

    internal override object Boxed()
    {
      return _value;
    }
  }

  internal sealed class BoxedColumnValue : ColumnValue
  {
    private readonly ValueReader _reader;
    private object _value;

    internal BoxedColumnValue(ValueReader reader)
    {
      _reader = reader;
    }

    internal override void Read(ReadContext c)
    {
      _value = _reader.ReadBoxed(c, null);
    }

    internal override object Boxed()
    {
      return _value;
    }
  }
}
