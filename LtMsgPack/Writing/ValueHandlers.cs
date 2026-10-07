using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Filters;
using LtMsgPack.Extensions;
using LtMsgPack.IO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace LtMsgPack.Writing
{
  /// <summary>
  /// Writes values of a declared type without boxing them. Values of another runtime type (polymorphism) go through <see cref="Serializer.WriteBoxed"/>, which adds the type id.
  /// </summary>
  internal abstract class ValueHandler<T>
  {
    /// <param name="value">Not null</param>
    /// <param name="assignedTo">The property or element the value is assigned to</param>
    internal abstract void Write(WriteContext c, T value, FullPropertyInfo assignedTo);

    /// <summary>
    /// FilterDefaultValues for a property of this type (without [DefaultValue], that one is asked boxed): false for the default value of the type.
    /// </summary>
    internal abstract bool IncludeByDefault(T value, FullPropertyInfo info);
  }

  internal static class DefaultFilter
  {
    internal static readonly FilterDefaultValues Instance = new FilterDefaultValues();
  }

  /// <summary>
  /// Any value, boxed: object and interface types, dictionaries and other collections, structs with properties, ...
  /// </summary>
  internal sealed class BoxedHandler<T> : ValueHandler<T>
  {
    private readonly Serializer _serializer;

    internal BoxedHandler(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override void Write(WriteContext c, T value, FullPropertyInfo assignedTo)
    {
      _serializer.WriteBoxed(c, value, assignedTo);
    }

    internal override bool IncludeByDefault(T value, FullPropertyInfo info)
    {
      return DefaultFilter.Instance.IncludeProperty(info, value);
    }
  }

  #region Leaf values (their declared type is their runtime type)

  internal sealed class BoolHandler : ValueHandler<bool>
  {
    internal override void Write(WriteContext c, bool value, FullPropertyInfo assignedTo) { c.W.Bool(value); }
    internal override bool IncludeByDefault(bool value, FullPropertyInfo info) { return value; }
  }

  internal sealed class SByteHandler : ValueHandler<sbyte>
  {
    internal override void Write(WriteContext c, sbyte value, FullPropertyInfo assignedTo) { c.W.SByte(value); }
    internal override bool IncludeByDefault(sbyte value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class Int16Handler : ValueHandler<short>
  {
    internal override void Write(WriteContext c, short value, FullPropertyInfo assignedTo) { c.W.Int16(value); }
    internal override bool IncludeByDefault(short value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class Int32Handler : ValueHandler<int>
  {
    internal override void Write(WriteContext c, int value, FullPropertyInfo assignedTo) { c.W.Int32(value); }
    internal override bool IncludeByDefault(int value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class Int64Handler : ValueHandler<long>
  {
    internal override void Write(WriteContext c, long value, FullPropertyInfo assignedTo) { c.W.Int64(value); }
    internal override bool IncludeByDefault(long value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class ByteHandler : ValueHandler<byte>
  {
    internal override void Write(WriteContext c, byte value, FullPropertyInfo assignedTo) { c.W.UInt8(value); }
    internal override bool IncludeByDefault(byte value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class UInt16Handler : ValueHandler<ushort>
  {
    internal override void Write(WriteContext c, ushort value, FullPropertyInfo assignedTo) { c.W.UInt16(value); }
    internal override bool IncludeByDefault(ushort value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class UInt32Handler : ValueHandler<uint>
  {
    internal override void Write(WriteContext c, uint value, FullPropertyInfo assignedTo) { c.W.UInt32(value); }
    internal override bool IncludeByDefault(uint value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class UInt64Handler : ValueHandler<ulong>
  {
    internal override void Write(WriteContext c, ulong value, FullPropertyInfo assignedTo) { c.W.UInt64(value); }
    internal override bool IncludeByDefault(ulong value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class SingleHandler : ValueHandler<float>
  {
    internal override void Write(WriteContext c, float value, FullPropertyInfo assignedTo) { c.W.Single(value); }
    internal override bool IncludeByDefault(float value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class DoubleHandler : ValueHandler<double>
  {
    internal override void Write(WriteContext c, double value, FullPropertyInfo assignedTo) { c.W.Double(value); }
    internal override bool IncludeByDefault(double value, FullPropertyInfo info) { return value != 0; }
  }

  internal sealed class CharHandler : ValueHandler<char>
  {
    internal override void Write(WriteContext c, char value, FullPropertyInfo assignedTo) { c.W.UInt16(value); } // as an unsigned 16 bit integer, like MessagePack-CSharp
    internal override bool IncludeByDefault(char value, FullPropertyInfo info) { return value != '\0'; }
  }

  internal sealed class TimeSpanHandler : ValueHandler<TimeSpan>
  {
    internal override void Write(WriteContext c, TimeSpan value, FullPropertyInfo assignedTo) { c.W.Int64(value.Ticks); }
    internal override bool IncludeByDefault(TimeSpan value, FullPropertyInfo info) { return value.Ticks != 0; }
  }

  internal sealed class StringHandler : ValueHandler<string>
  {
    internal override void Write(WriteContext c, string value, FullPropertyInfo assignedTo) { c.W.String(value); }
    internal override bool IncludeByDefault(string value, FullPropertyInfo info) { return value != null; } // an empty string is not a default value (FilterDefaultValues)
  }

  internal sealed class BinHandler : ValueHandler<byte[]>
  {
    internal override void Write(WriteContext c, byte[] value, FullPropertyInfo assignedTo) { c.W.Bin(value); }
    internal override bool IncludeByDefault(byte[] value, FullPropertyInfo info) { return value != null; }
  }

  internal sealed class GuidHandler : ValueHandler<Guid>
  {
    internal override void Write(WriteContext c, Guid value, FullPropertyInfo assignedTo) { c.W.Guid(value); }
    internal override bool IncludeByDefault(Guid value, FullPropertyInfo info) { return value != Guid.Empty; }
  }

  /// <summary>
  /// A Guid as a string of 36 characters (<see cref="GuidFormat.String"/>).
  /// </summary>
  internal sealed class GuidStringHandler : ValueHandler<Guid>
  {
    internal override void Write(WriteContext c, Guid value, FullPropertyInfo assignedTo) { c.W.GuidString(value); }
    internal override bool IncludeByDefault(Guid value, FullPropertyInfo info) { return value != Guid.Empty; }
  }

  /// <summary>
  /// A decimal as a string in the invariant culture (<see cref="DecimalFormat.String"/>).
  /// </summary>
  internal sealed class DecimalStringHandler : ValueHandler<decimal>
  {
    internal override void Write(WriteContext c, decimal value, FullPropertyInfo assignedTo) { c.W.DecimalString(value); }
    internal override bool IncludeByDefault(decimal value, FullPropertyInfo info) { return !DecimalHandler.IsZero(value); }
  }

  internal sealed class DateTimeHandler : ValueHandler<DateTime>
  {
    private readonly bool _unspecifiedIsUtc;

    internal DateTimeHandler(bool unspecifiedIsUtc)
    {
      _unspecifiedIsUtc = unspecifiedIsUtc;
    }

    internal override void Write(WriteContext c, DateTime value, FullPropertyInfo assignedTo) { c.W.DateTime(Utc(value, _unspecifiedIsUtc)); }
    internal override bool IncludeByDefault(DateTime value, FullPropertyInfo info) { return value.Ticks != 0; } // DateTime.Equals ignores the Kind

    /// <summary>
    /// The writer takes Unspecified as local time, unless it should be UTC (<see cref="LtMsgPackOptions.UnspecifiedDateTimeKind"/>).
    /// </summary>
    internal static DateTime Utc(DateTime value, bool unspecifiedIsUtc)
    {
      return unspecifiedIsUtc && value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value;
    }
  }

  internal sealed class DateTimeOffsetHandler : ValueHandler<DateTimeOffset>
  {
    internal override void Write(WriteContext c, DateTimeOffset value, FullPropertyInfo assignedTo) { c.W.DateTime(value.UtcDateTime); } // the offset is lost, as in LsMsgPack
    internal override bool IncludeByDefault(DateTimeOffset value, FullPropertyInfo info) { return !value.Equals(default(DateTimeOffset)); }
  }

  /// <summary>
  /// MessagePack-CSharp's DateTimeOffset (<see cref="DateTimeOffsetFormat.ClockTimeAndOffset"/>): [the clock time as a UTC timestamp, the offset in minutes].
  /// </summary>
  internal sealed class DateTimeOffsetArrayHandler : ValueHandler<DateTimeOffset>
  {
    internal override void Write(WriteContext c, DateTimeOffset value, FullPropertyInfo assignedTo) { WriteArray(c.W, value); }
    internal override bool IncludeByDefault(DateTimeOffset value, FullPropertyInfo info) { return !value.Equals(default(DateTimeOffset)); }

    internal static void WriteArray(MsgPackWriter w, DateTimeOffset value)
    {
      w.ArrayHeader(2);
      w.DateTime(new DateTime(value.Ticks, DateTimeKind.Utc));
      w.Int16((short)value.Offset.TotalMinutes);
    }
  }

  internal sealed class DecimalHandler : ValueHandler<decimal>
  {
    private readonly LtExtension<decimal> _extension;

    /// <summary>
    /// The built-in <see cref="DecimalExtension"/>: the 16 bytes of the value, written without asking the extension.
    /// </summary>
    private readonly bool _builtIn;
    private readonly byte _typeCode;

    internal DecimalHandler(LtExtension<decimal> extension)
    {
      _extension = extension;
      _builtIn = extension.GetType() == typeof(DecimalExtension);
      _typeCode = unchecked((byte)extension.TypeCode);
    }

    internal override void Write(WriteContext c, decimal value, FullPropertyInfo assignedTo)
    {
      if (_builtIn)
      {
        MsgPackWriter w = c.W;
        w.Ensure(18);
        byte[] b = w.Buf;
        int at = w.Pos;
        b[at] = 0xD8; // fixext16
        b[at + 1] = _typeCode;
        Unsafe.WriteUnaligned(ref b[at + 2], value); // as DecimalExtension.Write (MemoryMarshal.Write)
        w.Pos = at + 18;
        return;
      }

      int max = _extension.GetMaxLength(value);
      if (max == 16)
      {
        c.W.Ensure(18);
        int header = c.W.Pos;
        c.W.ExtHeader(_extension.TypeCode, 16);
        if (_extension.Write(value, new Span<byte>(c.W.Buf, c.W.Pos, 16)) == 16)
        {
          c.W.Pos += 16;
          return;
        }
        c.W.Pos = header; // another length after all
      }
      ExtensionHandler<decimal>.WriteExtension(c, _extension, value);
    }

    internal override bool IncludeByDefault(decimal value, FullPropertyInfo info) { return !IsZero(value); }

    /// <summary>
    /// value == 0m (also -0m and zeros with a scale) without the call: the flags (sign and scale) come first in the memory of a decimal, the 96 bit integer is the rest.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsZero(decimal value)
    {
      DecimalBits bits = Unsafe.As<decimal, DecimalBits>(ref value);
      return (bits.High | bits.Low64) == 0;
    }

    private struct DecimalBits
    {
#pragma warning disable CS0649 // only read
      internal uint Flags;
      internal uint High;
      internal ulong Low64;
#pragma warning restore CS0649
    }
  }

  /// <summary>
  /// A custom extension of the declared type.
  /// </summary>
  internal sealed class ExtensionHandler<T> : ValueHandler<T>
  {
    private readonly LtExtension _extension;

    internal ExtensionHandler(LtExtension extension)
    {
      _extension = extension;
    }

    internal override void Write(WriteContext c, T value, FullPropertyInfo assignedTo)
    {
      if (_extension is LtExtension<T> typed)
        WriteExtension(c, typed, value);
      else
        WriteExtensionBoxed(c, _extension, value);
    }

    internal static void WriteExtension(WriteContext c, LtExtension<T> extension, T value)
    {
      int max = extension.GetMaxLength(value);
      byte[] data = new byte[max];
      int length = extension.Write(value, data);
      c.W.ExtHeader(extension.TypeCode, length);
      c.W.Raw(data, 0, length);
    }

    internal static void WriteExtensionBoxed(WriteContext c, LtExtension extension, object value)
    {
      int max = extension.GetMaxLength(value);
      byte[] data = new byte[max];
      int length = extension.WriteBoxed(value, data);
      c.W.ExtHeader(extension.TypeCode, length);
      c.W.Raw(data, 0, length);
    }

    internal override bool IncludeByDefault(T value, FullPropertyInfo info)
    {
      return DefaultFilter.Instance.IncludeProperty(info, value);
    }
  }

  /// <summary>
  /// An enum as its underlying integer type (like MpInt.SetEnumVal).
  /// </summary>
  internal sealed class EnumHandler<TEnum> : ValueHandler<TEnum> where TEnum : struct
  {
    private static readonly TypeCode Underlying = Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum)));

    internal override void Write(WriteContext c, TEnum value, FullPropertyInfo assignedTo)
    {
      switch (Underlying)
      {
        case TypeCode.SByte: c.W.SByte(Unsafe.As<TEnum, sbyte>(ref value)); return;
        case TypeCode.Int16: c.W.Int16(Unsafe.As<TEnum, short>(ref value)); return;
        case TypeCode.Int32: c.W.Int32(Unsafe.As<TEnum, int>(ref value)); return;
        case TypeCode.Int64: c.W.Int64(Unsafe.As<TEnum, long>(ref value)); return;
        case TypeCode.Byte: c.W.UInt8(Unsafe.As<TEnum, byte>(ref value)); return;
        case TypeCode.UInt16: c.W.UInt16(Unsafe.As<TEnum, ushort>(ref value)); return;
        case TypeCode.UInt32: c.W.UInt32(Unsafe.As<TEnum, uint>(ref value)); return;
        default: c.W.UInt64(Unsafe.As<TEnum, ulong>(ref value)); return;
      }
    }

    internal override bool IncludeByDefault(TEnum value, FullPropertyInfo info)
    {
      return !EqualityComparer<TEnum>.Default.Equals(value, default(TEnum));
    }
  }

  /// <summary>
  /// Nullable&lt;T&gt;: the value itself when it has one (its type id would be the underlying type, which equals the assigned type).
  /// </summary>
  internal sealed class NullableHandler<T> : ValueHandler<T?> where T : struct
  {
    private readonly ValueHandler<T> _inner;

    internal NullableHandler(ValueHandler<T> inner)
    {
      _inner = inner;
    }

    internal override void Write(WriteContext c, T? value, FullPropertyInfo assignedTo)
    {
      _inner.Write(c, value.GetValueOrDefault(), assignedTo);
    }

    internal override bool IncludeByDefault(T? value, FullPropertyInfo info)
    {
      return value.HasValue; // FilterDefaultValues keeps every value of a Nullable property
    }
  }

  #endregion

  #region Objects and collections (the runtime type may differ: then written boxed, with a type id)

  internal sealed class ObjectHandler<T> : ValueHandler<T> where T : class
  {
    private readonly Serializer _serializer;
    private ObjectPlan _names;
    private ObjectPlan _schema;

    internal ObjectHandler(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override void Write(WriteContext c, T value, FullPropertyInfo assignedTo)
    {
      if (value.GetType() != typeof(T))
      {
        _serializer.WriteBoxed(c, value, assignedTo);
        return;
      }

      ObjectPlan plan = c.Mode == IdMode.Names
        ? _names ?? (_names = _serializer.Plan(typeof(T), true))
        : _schema ?? (_schema = _serializer.Plan(typeof(T), false));
      plan.Write(c, value, false, assignedTo);
    }

    internal override bool IncludeByDefault(T value, FullPropertyInfo info)
    {
      return value != null;
    }
  }

  /// <summary>
  /// List&lt;T&gt; (not a derived type) as an array of its items.
  /// </summary>
  internal sealed class ListHandler<TItem> : ValueHandler<List<TItem>>
  {
    private readonly Serializer _serializer;
    private readonly FullPropertyInfo _itemInfo = new FullPropertyInfo(typeof(TItem));
    private ValueHandler<TItem> _item;

    internal ListHandler(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override void Write(WriteContext c, List<TItem> value, FullPropertyInfo assignedTo)
    {
      if (value.GetType() != typeof(List<TItem>))
      {
        _serializer.WriteBoxed(c, value, assignedTo);
        return;
      }

      ValueHandler<TItem> item = _item ?? (_item = _serializer.Handler<TItem>(null));
      c.EnterContainer();
      int count = value.Count;
      c.W.ArrayHeader(count);
      for (int t = 0; t < count; t++)
      {
        TItem element = value[t];
        if (element == null)
          c.W.Nil();
        else
          item.Write(c, element, _itemInfo);
      }
      c.LeaveContainer();
    }

    internal override bool IncludeByDefault(List<TItem> value, FullPropertyInfo info)
    {
      return value != null;
    }
  }

  /// <summary>
  /// T[] (not an array of a derived type) as an array.
  /// </summary>
  internal sealed class ArrayHandler<TItem> : ValueHandler<TItem[]>
  {
    private readonly Serializer _serializer;
    private readonly FullPropertyInfo _itemInfo = new FullPropertyInfo(typeof(TItem));
    private ValueHandler<TItem> _item;

    internal ArrayHandler(Serializer serializer)
    {
      _serializer = serializer;
    }

    internal override void Write(WriteContext c, TItem[] value, FullPropertyInfo assignedTo)
    {
      if (value.GetType() != typeof(TItem[]))
      {
        _serializer.WriteBoxed(c, value, assignedTo);
        return;
      }

      ValueHandler<TItem> item = _item ?? (_item = _serializer.Handler<TItem>(null));
      c.EnterContainer();
      c.W.ArrayHeader(value.Length);
      for (int t = 0; t < value.Length; t++)
      {
        TItem element = value[t];
        if (element == null)
          c.W.Nil();
        else
          item.Write(c, element, _itemInfo);
      }
      c.LeaveContainer();
    }

    internal override bool IncludeByDefault(TItem[] value, FullPropertyInfo info)
    {
      return value != null;
    }
  }

  #endregion
}
