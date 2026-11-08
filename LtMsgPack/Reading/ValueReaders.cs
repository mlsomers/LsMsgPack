using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LtMsgPack.Extensions;
using LtMsgPack.IO;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace LtMsgPack.Reading
{
  /// <summary>
  /// The state of one Deserialize call.
  /// </summary>
  /// <para>Public for the code of the source generator (see LtMsgPack.LtMsgPackContext): may change between releases.</para>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class ReadContext
  {
    internal readonly Serializer Serializer;
    public MsgPackReader R;

    /// <summary>
    /// The property ids are indexes of an indexed schema (<see cref="Bound"/>).
    /// </summary>
    internal bool Schema;
    internal BoundSchema Bound;

    /// <summary>
    /// The settings for the conversions of LsMsgPack.Core (ValueConverter): the options, or in schema mode the session's (see <see cref="ConversionSettings"/>).
    /// </summary>
    internal MsgPackOptions SlowSettings;
    public int Depth;

    /// <summary>
    /// Collects the differences between the data and the classes (null when not asked for). <see cref="ConversionSettings"/> then has them too, for LsMsgPack.Core's conversions.
    /// </summary>
    internal ReadDifferences Differences;

    /// <summary>
    /// <see cref="SlowSettings"/> with the <see cref="Differences"/>, copied when a conversion first needs it (a copy of the options per call otherwise, which data that matches the classes never uses).
    /// </summary>
    private MsgPackOptions _withDifferences;
    private MsgPackOptions _withDifferencesOf;

    /// <summary>
    /// The settings for the conversions of LsMsgPack.Core: <see cref="SlowSettings"/>, with the <see cref="Differences"/> when they are collected.
    /// </summary>
    internal MsgPackOptions ConversionSettings()
    {
      ReadDifferences differences = Differences;
      if (differences is null)
        return SlowSettings;
      MsgPackOptions copy = _withDifferences;
      if (copy is null || !ReferenceEquals(_withDifferencesOf, SlowSettings) || !ReferenceEquals(copy._differences, differences))
      {
        _withDifferences = copy = SlowSettings.WithDifferences(differences);
        _withDifferencesOf = SlowSettings;
      }
      return copy;
    }

    /// <summary>
    /// The call ends: the differences are not kept alive.
    /// </summary>
    internal void EndDifferences()
    {
      Differences = null;
      _withDifferences = null;
      _withDifferencesOf = null;
    }

    /// <summary>
    /// The objects are read as LsMsgPack does (<see cref="Serializer.SlowObjects"/>), or the <see cref="Differences"/> are collected: one check per object for both.
    /// </summary>
    internal bool Unusual;

    /// <summary>
    /// The root type of the previous call on this thread and its reader (the context is reused, see Serializer.RentContext): calls usually read the same root type.
    /// </summary>
    internal Type RootType;
    internal ValueReader RootReader;

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
      object value = ValueConverter.ConvertDeserializeValue(plain, typeof(T), c.ConversionSettings(), assignedTo);
      if (value is null)
        return default(T);
      if (value is T typed)
        return typed;
      if (c.Depth == 0 && assignedTo is null) // the root: LsMsgPack returns what it could convert, only its generic Deserialize casts
        throw new RootValue(value);
      throw ReadDifferences.NotConverted(c.ConversionSettings(), new ArgumentException($"Object of type '{value.GetType()}' cannot be converted to type '{typeof(T)}'.")); // as reflection reports it when LsMsgPack sets the property
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
      else if (c.R.TryReadAnyInt64(out long value))
      {
        switch (Code)
        {
          case TypeCode.SByte: { if (value < sbyte.MinValue || value > sbyte.MaxValue) throw Overflow(c); sbyte v = (sbyte)value; return Unsafe.As<sbyte, T>(ref v); }
          case TypeCode.Int16: { if (value < short.MinValue || value > short.MaxValue) throw Overflow(c); short v = (short)value; return Unsafe.As<short, T>(ref v); }
          case TypeCode.Int32: { if (value < int.MinValue || value > int.MaxValue) throw Overflow(c); int v = (int)value; return Unsafe.As<int, T>(ref v); }
          case TypeCode.Int64: { return Unsafe.As<long, T>(ref value); }
          case TypeCode.Byte: { if (value < byte.MinValue || value > byte.MaxValue) throw Overflow(c); byte v = (byte)value; return Unsafe.As<byte, T>(ref v); }
          case TypeCode.UInt16: { if (value < ushort.MinValue || value > ushort.MaxValue) throw Overflow(c); ushort v = (ushort)value; return Unsafe.As<ushort, T>(ref v); }
          case TypeCode.UInt32: { if (value < uint.MinValue || value > uint.MaxValue) throw Overflow(c); uint v = (uint)value; return Unsafe.As<uint, T>(ref v); }
          case TypeCode.Char: { if (value < char.MinValue || value > char.MaxValue) throw Overflow(c); char v = (char)value; return Unsafe.As<char, T>(ref v); }
        }
      }
      if (c.R.TryReadNil()) return default(T);
      return Slow(c, start, assignedTo);
    }

    /// <summary>
    /// As a checked cast throws it (and Convert.ChangeType in LsMsgPack), marked as a value that was read but not converted.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception Overflow(ReadContext c)
    {
      return ReadDifferences.NotConverted(c.ConversionSettings(), new OverflowException());
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
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      Span<char> chars = stackalloc char[64];
      if (c.R.TryReadShortString(chars, out int count))
      {
        if (Guid.TryParse(chars.Slice(0, count), out value)) return value;
        throw ReadDifferences.NotConverted(c.ConversionSettings(), new MsgPackException($"\"{chars.Slice(0, count).ToString()}\" is not a Guid."));
      }
#endif
      if (c.R.TryReadString(out string text))
      {
        if (Guid.TryParse(text, out value)) return value;
        throw ReadDifferences.NotConverted(c.ConversionSettings(), new MsgPackException($"\"{text}\" is not a Guid."));
      }
      if (c.R.TryReadNil()) return Guid.Empty;
      return Slow(c, start, assignedTo);
    }
  }

  /// <summary>
  /// <see cref="DecimalFormat.String"/>: a string in the invariant culture, the built-in extension in place (clients that write decimals as declared, e.g. to the plain media types of the web formatters), anything else (numbers) as LsMsgPack reads it.
  /// </summary>
  internal sealed class DecimalStringReader : ValueReader<decimal>
  {
    private readonly int _extensionCode; // -1 never equals a byte of the data: no built-in extension, or -1 itself, the timestamp, read as such (as LsMsgPack)

    internal DecimalStringReader(LtExtension extension)
    {
      _extensionCode = extension is DecimalExtension builtIn && builtIn.TypeCode != -1 ? unchecked((byte)builtIn.TypeCode) : -1;
    }

    internal override decimal Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      MsgPackReader r = c.R;
      int start = r.Pos;
      int length = r.TryReadStringHeader();
      if (length >= 0 && MsgPackOptions.StringEncoding is UTF8Encoding && DecimalText.TryParse(r.Buf, r.Pos, length, out decimal simple)) // ASCII, the same bytes in UTF-8
      {
        r.Pos += length;
        return simple;
      }
      r.Pos = start;
      if (length < 0 && r.End - start >= 18 && r.Buf[start] == 0xD8 && r.Buf[start + 1] == _extensionCode)
      {
        decimal value = Unsafe.ReadUnaligned<decimal>(ref r.Buf[start + 2]); // as DecimalReader
        r.Pos = start + 18;
        return value;
      }
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      Span<char> chars = stackalloc char[64];
      if (c.R.TryReadShortString(chars, out int count))
      {
        if (decimal.TryParse(chars.Slice(0, count), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal parsed))
          return parsed;
        return Slow(c, start, assignedTo);
      }
#endif
      if (c.R.TryReadString(out string text) && decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal fromText))
        return fromText;
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

  /// <summary>
  /// <see cref="DateTimeOffsetFormat.TimestampAndOffset"/> (the default) and <see cref="DateTimeOffsetFormat.Timestamp"/>: [the moment as a timestamp, the offset in minutes], or a timestamp.
  /// </summary>
  internal sealed class DateTimeOffsetReader : ValueReader<DateTimeOffset>
  {
    internal override DateTimeOffset Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadArrayHeader() == 2 && c.R.TryReadTimestamp(out DateTime moment) && c.R.TryReadInt64(out long minutes))
        return new DateTimeOffset(moment.Ticks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(minutes));
      c.R.Pos = start;
      if (c.R.TryReadDateTime(out DateTime value)) return MsgPackOptions.OffsetOfTimestamp(value); // the offset was not written: the local one or zero (ReadDateTimeKind)
      if (c.R.TryReadNil()) return default(DateTimeOffset);
      return Slow(c, start, assignedTo);
    }
  }

  /// <summary>
  /// <see cref="DateTimeOffsetFormat.ClockTimeAndOffset"/>: MessagePack-CSharp's [clock time as a UTC timestamp, offset in minutes], or a timestamp.
  /// </summary>
  internal sealed class DateTimeOffsetArrayReader : ValueReader<DateTimeOffset>
  {
    internal override DateTimeOffset Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadArrayHeader() == 2 && c.R.TryReadTimestamp(out DateTime clock) && c.R.TryReadInt64(out long minutes))
        return new DateTimeOffset(clock.Ticks, TimeSpan.FromMinutes(minutes));
      c.R.Pos = start;
      if (c.R.TryReadDateTime(out DateTime value)) return MsgPackOptions.OffsetOfTimestamp(value);
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

  /// <summary>
  /// A reader of a collection type that also reads the content of a value wrapped with a type id, or of one declared as an interface (see <see cref="CollectionInterfaceReader{T}"/>).
  /// </summary>
  internal interface ICollectionContent
  {
    /// <param name="value">The collection, of exactly the handled type</param>
    /// <returns>false when the value is not in the expected format: the depth is as it was, the caller rewinds and reads it as LsMsgPack does</returns>
    bool TryReadContent(ReadContext c, out object value);
  }

  internal sealed class ListReader<TItem> : ValueReader<List<TItem>>, ICollectionContent
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

    bool ICollectionContent.TryReadContent(ReadContext c, out object value)
    {
      value = CollectionRules.IsArray(c.R) ? Read(c, null) : null;
      return value != null;
    }
  }

  internal sealed class ArrayReader<TItem> : ValueReader<TItem[]>, ICollectionContent
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

    bool ICollectionContent.TryReadContent(ReadContext c, out object value)
    {
      value = CollectionRules.IsArray(c.R) ? Read(c, null) : null;
      return value != null;
    }
  }

  /// <summary>
  /// HashSet&lt;T&gt; (not a derived class) from an array.
  /// </summary>
  internal sealed class HashSetReader<TItem> : ValueReader<HashSet<TItem>>, ICollectionContent
  {
    private readonly Serializer _serializer;
    private ValueReader<TItem> _item;

    internal HashSetReader(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override HashSet<TItem> Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadNil())
        return null;

      int count = c.Unusual || CollectionRules.HasEnumerableAttribute(assignedTo) ? -1 : c.R.TryReadArrayHeader();
      if (count < 0)
        return Slow(c, start, assignedTo);
      return ReadItems(c, count);
    }

    bool ICollectionContent.TryReadContent(ReadContext c, out object value)
    {
      int count = c.R.TryReadArrayHeader();
      value = count < 0 ? null : ReadItems(c, count);
      return count >= 0;
    }

    private HashSet<TItem> ReadItems(ReadContext c, int count)
    {
      ValueReader<TItem> item = _item ?? (_item = _serializer.Reader<TItem>());
      c.Enter();
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      HashSet<TItem> set = new HashSet<TItem>(count); // the header's count is checked against the data (MsgPackReader.Count)
#else
      HashSet<TItem> set = new HashSet<TItem>();
#endif
      for (int t = 0; t < count; t++)
        set.Add(item.Read(c, null));
      c.Depth--;
      return set;
    }
  }

  /// <summary>
  /// Dictionary&lt;TKey, TValue&gt; (not a derived class) from a map of entries. Everything that is not simply an entry is read as LsMsgPack does (Core: ValueConverter.ConvertMap, ConvertPairs), from the start of the map:
  /// the reserved keys ("" and "@" make it a wrapped value), a nil key, a number key into string keys, a nil for a value type (these two throw there).
  /// </summary>
  internal sealed class DictionaryReader<TKey, TValue> : ValueReader<Dictionary<TKey, TValue>>, ICollectionContent
  {
    private readonly Serializer _serializer;
    private readonly bool _stringKeys = typeof(TKey) == typeof(string);
    private readonly bool _valueTakesNoNil = typeof(TValue).IsValueType && Nullable.GetUnderlyingType(typeof(TValue)) is null;
    private ValueReader<TKey> _key;
    private ValueReader<TValue> _value;

    internal DictionaryReader(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override Dictionary<TKey, TValue> Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadNil())
        return null;

      if (!c.Unusual && !CollectionRules.HasEnumerableAttribute(assignedTo) && TryRead(c, out Dictionary<TKey, TValue> value))
        return value;
      return Slow(c, start, assignedTo);
    }

    bool ICollectionContent.TryReadContent(ReadContext c, out object value)
    {
      bool read = TryRead(c, out Dictionary<TKey, TValue> dictionary);
      value = dictionary;
      return read;
    }

    private bool TryRead(ReadContext c, out Dictionary<TKey, TValue> result)
    {
      result = null;
      MsgPackReader r = c.R;
      int count = r.TryReadMapHeader();
      if (count < 0)
        return false;

      ValueReader<TKey> keys = _key ?? (_key = _serializer.Reader<TKey>());
      ValueReader<TValue> values = _value ?? (_value = _serializer.Reader<TValue>());
      int depth = c.Depth;
      c.Enter();
      Dictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>(count); // the header's count is checked against the data (MsgPackReader.Count)
      for (int t = 0; t < count; t++)
      {
        if (!CollectionRules.IsEntryKey(r, _stringKeys))
        {
          c.Depth = depth;
          return false;
        }
        TKey key = keys.Read(c, null);
        if (_valueTakesNoNil && r.Peek() == 0xC0)
        {
          c.Depth = depth;
          return false;
        }
        dictionary.Add(key, values.Read(c, null)); // a key twice throws, as there
      }
      c.Depth = depth;
      result = dictionary;
      return true;
    }
  }

  /// <summary>
  /// A property or element declared as a generic interface of a collection (see Serializer.CollectionInterface). The writer wraps a value of another type than the declared one: { "": typeId, "@": content },
  /// the content is read by the typed reader of that type when it has one. Data without a type id (of another library) is read into the interface's concrete type, as Core does (CollectionInfo.ConcreteType).
  /// Anything else is read as LsMsgPack does, from the start.
  /// </summary>
  internal sealed class CollectionInterfaceReader<T> : ValueReader<T> where T : class
  {
    private readonly Serializer _serializer;
    private ICollectionContent _concrete;

    /// <summary>
    /// The type id read last and its reader: a property usually holds one type.
    /// </summary>
    private sealed class Wrapped
    {
      internal readonly Type Type;
      internal readonly ICollectionContent Content;

      internal Wrapped(Type type, ICollectionContent content)
      {
        Type = type;
        Content = content;
      }
    }

    private Wrapped _last;

    internal CollectionInterfaceReader(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override T Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadNil())
        return null;

      if (c.Unusual || CollectionRules.HasEnumerableAttribute(assignedTo))
        return Slow(c, start, assignedTo);

      MsgPackReader r = c.R;
      object value;
      if (r.Peek() == 0x82 && r.End - r.Pos >= 6 && r.Buf[r.Pos + 1] == 0xA0) // a map of two (with the keys, an id and a content at least 6 bytes), the type id (key "") first
      {
        r.Pos += 2;
        Type type = ReadTypeId(c);
        if (type is null || r.Pos + 1 >= r.End || r.Buf[r.Pos] != 0xA1 || r.Buf[r.Pos + 1] != (byte)'@')
          return Slow(c, start, assignedTo);
        r.Pos += 2;

        Wrapped wrapped = _last;
        if (wrapped is null || wrapped.Type != type)
        {
          if (!typeof(T).IsAssignableFrom(type) // Core refuses a type that is not assignable
            || !(_serializer.CanBindDelegates || _serializer.Generated(type) != null) // without dynamic code a type the source generator did not describe has no reader
            || !(_serializer.Reader(type) is ICollectionContent content))
            return Slow(c, start, assignedTo);
          _last = wrapped = new Wrapped(type, content);
        }
        if (_serializer.Options._typeGuard != null) // the type guard, before the instance is created (see docs/security.md)
          TypeResolver.ThrowIfNotAllowed(type, typeof(T), assignedTo, _serializer.Options, type);

        int depth = c.Depth;
        c.Enter();
        bool read = wrapped.Content.TryReadContent(c, out value);
        c.Depth = depth;
        if (!read)
          return Slow(c, start, assignedTo);
      }
      else if (!CollectionRules.IsPlainContent(r) || !Concrete().TryReadContent(c, out value))
        return Slow(c, start, assignedTo);

      return Unsafe.As<T>(value); // assignable: checked above, or the concrete type of the interface
    }

    /// <summary>
    /// When the first value without a wrapper is read, as Core asks for the concrete type.
    /// </summary>
    private ICollectionContent Concrete()
    {
      return _concrete ?? (_concrete = (ICollectionContent)_serializer.Reader(CollectionInfo.Get(typeof(T)).ConcreteType));
    }

    /// <returns>null when the type id is not a plain index or name of a type that is found</returns>
    private Type ReadTypeId(ReadContext c)
    {
      if (c.Schema)
        return c.R.TryReadInt64(out long id) ? c.Bound.TypeById(id) : null;
      if (!c.R.TryReadString(out string name) || string.IsNullOrWhiteSpace(name))
        return null;
      return _serializer.ResolveTypeName(name, typeof(T), false);
    }
  }

  internal static class CollectionRules
  {
    /// <summary>
    /// [SerializeEnumerable] on the property changes what its value looks like in the data (properties, elements left out).
    /// </summary>
    internal static bool HasEnumerableAttribute(FullPropertyInfo assignedTo)
    {
      return assignedTo != null && assignedTo.CustomAttributes != null && assignedTo.CustomAttributes.ContainsKey(nameof(SerializeEnumerableAttribute));
    }

    /// <summary>
    /// The next value is a key of a dictionary entry as the typed readers read it: not nil or a reserved key, a string for string keys.
    /// </summary>
    internal static bool IsEntryKey(MsgPackReader r, bool stringKeys)
    {
      byte first = r.Peek();
      if ((first & 0xE0) == 0xA0) // up to 31 bytes
        return first != 0xA0 && (first != 0xA1 || r.Pos + 1 >= r.End || r.Buf[r.Pos + 1] != (byte)'@');
      if (first >= 0xD9 && first <= 0xDB) // not the shortest form of a reserved key
      {
        int at = r.Pos;
        int length = r.TryReadStringHeader();
        bool reserved = length == 0 || (length == 1 && r.Buf[r.Pos] == (byte)'@');
        r.Pos = at;
        return !reserved;
      }
      return !stringKeys && first != 0xC0;
    }

    /// <summary>
    /// The next value is an array (of any length).
    /// </summary>
    internal static bool IsArray(MsgPackReader r)
    {
      byte first = r.Peek();
      return (first & 0xF0) == 0x90 || first == 0xDC || first == 0xDD;
    }

    /// <summary>
    /// Data without a wrapper: an array or a map of entries. The first key of a map of more entries decides nothing here, the reader of the content checks each key.
    /// </summary>
    internal static bool IsPlainContent(MsgPackReader r)
    {
      byte first = r.Peek();
      return (first & 0xE0) == 0x80 || first == 0xDC || first == 0xDD || first == 0xDE || first == 0xDF; // fixmap, fixarray, array16/32, map16/32
    }
  }

  #endregion
}
