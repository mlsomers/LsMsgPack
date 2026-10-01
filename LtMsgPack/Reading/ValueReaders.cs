using LsMsgPack;
using LsMsgPack.Meta;
using LtMsgPack.Extensions;
using LtMsgPack.IO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace LtMsgPack.Reading
{
  /// <summary>
  /// The state of one Deserialize call.
  /// </summary>
  internal sealed class ReadContext
  {
    internal readonly Serializer Serializer;
    internal MsgPackReader R;

    /// <summary>
    /// The property ids are indexes of an indexed schema (<see cref="Bound"/>).
    /// </summary>
    internal bool Schema;
    internal BoundSchema Bound;

    /// <summary>
    /// The settings for the conversions of LsMsgPack.Core (ValueConverter): the options, or in schema mode a copy using the session.
    /// </summary>
    internal MsgPackOptions SlowSettings;
    internal int Depth;

    internal ReadContext(Serializer serializer)
    {
      Serializer = serializer;
    }

    internal void Enter()
    {
      if (++Depth > R.MaxDepth)
        throw R.TooDeep();
    }
  }

  internal abstract class ValueReader
  {
    /// <param name="assignedTo">The property the value is assigned to, null for the root and elements (as LsMsgPack)</param>
    internal abstract object ReadBoxed(ReadContext c, FullPropertyInfo assignedTo);
  }

  /// <summary>
  /// Reads values of a type from the formats it expects. Any other format is read as a plain value and converted by LsMsgPack.Core's ValueConverter, as LsMsgPack does with every value.
  /// </summary>
  internal abstract class ValueReader<T> : ValueReader
  {
    internal abstract T Read(ReadContext c, FullPropertyInfo assignedTo);

    internal override object ReadBoxed(ReadContext c, FullPropertyInfo assignedTo)
    {
      return Read(c, assignedTo);
    }

    /// <summary>
    /// Reads the value at <paramref name="start"/> as LsMsgPack does: unpacked, then converted.
    /// </summary>
    protected static T Slow(ReadContext c, int start, FullPropertyInfo assignedTo)
    {
      c.R.Pos = start;
      object plain = c.R.ReadPlain(c.Depth);
      object value = ValueConverter.ConvertDeserializeValue(plain, typeof(T), c.SlowSettings, assignedTo);
      if (value is null)
        return default(T);
      if (value is T typed)
        return typed;
      if (c.Depth == 0 && assignedTo is null) // the root: LsMsgPack returns what it could convert, only its generic Deserialize casts
        throw new RootValue(value);
      throw new ArgumentException($"Object of type '{value.GetType()}' cannot be converted to type '{typeof(T)}'."); // as reflection reports it when LsMsgPack sets the property
    }
  }

  /// <summary>
  /// The root value could not be converted to the requested type: returned as it is by the non-generic Deserialize (see <see cref="ValueReader{T}"/>).
  /// </summary>
  internal sealed class RootValue : Exception
  {
    internal readonly object Value;

    internal RootValue(object value)
    {
      Value = value;
    }
  }

  /// <summary>
  /// object, interfaces, dictionaries, other collections, structs: read as LsMsgPack does.
  /// </summary>
  internal sealed class BoxedReader<T> : ValueReader<T>
  {
    internal override T Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      return Slow(c, c.R.Pos, assignedTo);
    }
  }

  #region Leaf values

  internal sealed class BoolReader : ValueReader<bool>
  {
    internal override bool Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadBool(out bool value)) return value;
      if (c.R.TryReadNil()) return false;
      return Slow(c, start, assignedTo);
    }
  }

  /// <summary>
  /// The integer types: any integer format, converted with an overflow check (as Convert.ChangeType).
  /// </summary>
  internal sealed class IntegerReader<T> : ValueReader<T>
  {
    private static readonly TypeCode Code = Type.GetTypeCode(typeof(T));

    internal override T Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (Code == TypeCode.UInt64)
      {
        if (c.R.TryReadUInt64(out ulong unsigned))
        {
          ulong u = unsigned;
          return Unsafe.As<ulong, T>(ref u);
        }
      }
      else if (c.R.TryReadInt64(out long value))
      {
        switch (Code)
        {
          case TypeCode.SByte: { sbyte v = checked((sbyte)value); return Unsafe.As<sbyte, T>(ref v); }
          case TypeCode.Int16: { short v = checked((short)value); return Unsafe.As<short, T>(ref v); }
          case TypeCode.Int32: { int v = checked((int)value); return Unsafe.As<int, T>(ref v); }
          case TypeCode.Int64: { return Unsafe.As<long, T>(ref value); }
          case TypeCode.Byte: { byte v = checked((byte)value); return Unsafe.As<byte, T>(ref v); }
          case TypeCode.UInt16: { ushort v = checked((ushort)value); return Unsafe.As<ushort, T>(ref v); }
          case TypeCode.UInt32: { uint v = checked((uint)value); return Unsafe.As<uint, T>(ref v); }
          case TypeCode.Char: { char v = checked((char)value); return Unsafe.As<char, T>(ref v); }
        }
      }
      if (c.R.TryReadNil()) return default(T);
      return Slow(c, start, assignedTo);
    }
  }

  internal sealed class SingleReader : ValueReader<float>
  {
    internal override float Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadSingle(out float value)) return value;
      if (c.R.TryReadNil()) return 0;
      return Slow(c, start, assignedTo);
    }
  }

  internal sealed class DoubleReader : ValueReader<double>
  {
    internal override double Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadDouble(out double value)) return value;
      if (c.R.TryReadNil()) return 0;
      return Slow(c, start, assignedTo);
    }
  }

  internal sealed class StringReader : ValueReader<string>
  {
    internal override string Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadString(out string value)) return value;
      if (c.R.TryReadNil()) return null;
      return Slow(c, start, assignedTo);
    }
  }

  internal sealed class BinReader : ValueReader<byte[]>
  {
    internal override byte[] Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      int length = c.R.TryReadBinHeader();
      if (length >= 0)
      {
        byte[] value = new byte[length];
        Buffer.BlockCopy(c.R.Buf, c.R.Pos, value, 0, length);
        c.R.Pos += length;
        return value;
      }
      if (c.R.TryReadNil()) return null;
      return Slow(c, start, assignedTo);
    }
  }

  internal sealed class GuidReader : ValueReader<Guid>
  {
    internal override Guid Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadGuid(out Guid value)) return value;
      if (c.R.TryReadNil()) return Guid.Empty;
      return Slow(c, start, assignedTo);
    }
  }

  /// <summary>
  /// <see cref="GuidFormat.String"/>: a string, or bin 16 as LsMsgPack writes it.
  /// </summary>
  internal sealed class GuidStringReader : ValueReader<Guid>
  {
    internal override Guid Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadGuid(out Guid value)) return value;
#if NETSTANDARD2_1_OR_GREATER
      Span<char> chars = stackalloc char[64];
      if (c.R.TryReadShortString(chars, out int count))
      {
        if (Guid.TryParse(chars.Slice(0, count), out value)) return value;
        throw new MsgPackException($"\"{chars.Slice(0, count).ToString()}\" is not a Guid.");
      }
#endif
      if (c.R.TryReadString(out string text))
      {
        if (Guid.TryParse(text, out value)) return value;
        throw new MsgPackException($"\"{text}\" is not a Guid.");
      }
      if (c.R.TryReadNil()) return Guid.Empty;
      return Slow(c, start, assignedTo);
    }
  }

  /// <summary>
  /// <see cref="DecimalFormat.String"/>: a string in the invariant culture, anything else (the decimal extension, numbers) as LsMsgPack reads it.
  /// </summary>
  internal sealed class DecimalStringReader : ValueReader<decimal>
  {
    internal override decimal Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
#if NETSTANDARD2_1_OR_GREATER
      Span<char> chars = stackalloc char[64];
      if (c.R.TryReadShortString(chars, out int count))
      {
        if (decimal.TryParse(chars.Slice(0, count), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal parsed))
          return parsed;
        return Slow(c, start, assignedTo);
      }
#endif
      if (c.R.TryReadString(out string text) && decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal value))
        return value;
      return Slow(c, start, assignedTo);
    }
  }

  internal sealed class DateTimeReader : ValueReader<DateTime>
  {
    internal override DateTime Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadDateTime(out DateTime value)) return value;
      if (c.R.TryReadNil()) return default(DateTime);
      return Slow(c, start, assignedTo);
    }
  }

  internal sealed class DateTimeOffsetReader : ValueReader<DateTimeOffset>
  {
    internal override DateTimeOffset Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadDateTime(out DateTime value)) return new DateTimeOffset(value); // local time, the offset was not written
      if (c.R.TryReadNil()) return default(DateTimeOffset);
      return Slow(c, start, assignedTo);
    }
  }

  /// <summary>
  /// <see cref="DateTimeOffsetFormat.ClockTimeAndOffset"/>: MessagePack-CSharp's [clock time as a UTC timestamp, offset in minutes], or a timestamp as LsMsgPack writes it.
  /// </summary>
  internal sealed class DateTimeOffsetArrayReader : ValueReader<DateTimeOffset>
  {
    internal override DateTimeOffset Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadArrayHeader() == 2 && c.R.TryReadDateTime(out DateTime clock) && c.R.TryReadInt64(out long minutes))
        return new DateTimeOffset(clock.ToUniversalTime().Ticks, TimeSpan.FromMinutes(minutes));
      c.R.Pos = start;
      if (c.R.TryReadDateTime(out DateTime value)) return new DateTimeOffset(value);
      if (c.R.TryReadNil()) return default(DateTimeOffset);
      return Slow(c, start, assignedTo);
    }
  }

  internal sealed class TimeSpanReader : ValueReader<TimeSpan>
  {
    internal override TimeSpan Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadInt64(out long ticks)) return new TimeSpan(ticks);
      if (c.R.TryReadNil()) return default(TimeSpan);
      return Slow(c, start, assignedTo);
    }
  }

  /// <summary>
  /// A custom extension of the type (e.g. decimal).
  /// </summary>
  internal sealed class ExtensionReader<T> : ValueReader<T>
  {
    private readonly LtExtension<T> _extension;

    internal ExtensionReader(LtExtension<T> extension)
    {
      _extension = extension;
    }

    internal override T Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadExtHeader(out sbyte typeCode, out int length, out MsgPackTypeId format))
      {
        if (typeCode == _extension.TypeCode && typeCode != -1)
        {
          T value = _extension.Read(new ReadOnlySpan<byte>(c.R.Buf, c.R.Pos, length));
          c.R.Pos += length;
          return value;
        }
        c.R.Pos = start;
      }
      else if (c.R.TryReadNil())
        return default(T);
      return Slow(c, start, assignedTo);
    }
  }

  /// <summary>
  /// decimal with the built-in <see cref="DecimalExtension"/>: fixext16 with the 16 bytes of the value, read in place.
  /// </summary>
  internal sealed class DecimalReader : ValueReader<decimal>
  {
    private readonly byte _typeCode;
    private readonly ExtensionReader<decimal> _other;

    internal DecimalReader(DecimalExtension extension)
    {
      _typeCode = unchecked((byte)extension.TypeCode);
      _other = new ExtensionReader<decimal>(extension);
    }

    internal override decimal Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      MsgPackReader r = c.R;
      int at = r.Pos;
      if (r.End - at >= 18 && r.Buf[at] == 0xD8 && r.Buf[at + 1] == _typeCode)
      {
        decimal value = Unsafe.ReadUnaligned<decimal>(ref r.Buf[at + 2]); // as DecimalExtension.Read (MemoryMarshal.Read)
        r.Pos = at + 18;
        return value;
      }
      return _other.Read(c, assignedTo);
    }
  }

  /// <summary>
  /// An enum from any integer (Enum.ToObject: values out of range wrap around, as in LsMsgPack).
  /// </summary>
  internal sealed class EnumReader<TEnum> : ValueReader<TEnum> where TEnum : struct
  {
    internal override TEnum Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadInt64(out long value))
      {
        switch (Unsafe.SizeOf<TEnum>())
        {
          case 1: { byte b = (byte)value; return Unsafe.As<byte, TEnum>(ref b); }
          case 2: { ushort s = (ushort)value; return Unsafe.As<ushort, TEnum>(ref s); }
          case 4: { uint i = (uint)value; return Unsafe.As<uint, TEnum>(ref i); }
          default: return Unsafe.As<long, TEnum>(ref value);
        }
      }
      if (c.R.TryReadNil()) return default(TEnum);
      return Slow(c, start, assignedTo);
    }
  }

  internal sealed class NullableReader<T> : ValueReader<T?> where T : struct
  {
    private readonly ValueReader<T> _inner;

    internal NullableReader(ValueReader<T> inner)
    {
      _inner = inner;
    }

    internal override T? Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      if (c.R.TryReadNil())
        return null;
      return _inner.Read(c, assignedTo);
    }
  }

  #endregion

  #region Collections

  internal sealed class ListReader<TItem> : ValueReader<List<TItem>>
  {
    private readonly Serializer _serializer;
    private ValueReader<TItem> _item;

    internal ListReader(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override List<TItem> Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadNil())
        return null;

      int count = c.R.TryReadArrayHeader();
      if (count < 0)
        return Slow(c, start, assignedTo);

      ValueReader<TItem> item = _item ?? (_item = _serializer.Reader<TItem>());
      c.Enter();
      List<TItem> list = new List<TItem>(count);
      for (int t = 0; t < count; t++)
        list.Add(item.Read(c, null));
      c.Depth--;
      return list;
    }
  }

  internal sealed class ArrayReader<TItem> : ValueReader<TItem[]>
  {
    private readonly Serializer _serializer;
    private ValueReader<TItem> _item;

    internal ArrayReader(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override TItem[] Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadNil())
        return null;

      int count = c.R.TryReadArrayHeader();
      if (count < 0)
        return Slow(c, start, assignedTo);

      ValueReader<TItem> item = _item ?? (_item = _serializer.Reader<TItem>());
      c.Enter();
      TItem[] array = new TItem[count];
      for (int t = 0; t < count; t++)
        array[t] = item.Read(c, null);
      c.Depth--;
      return array;
    }
  }

  #endregion
}
