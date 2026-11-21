using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.References;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.Extensions;
using LsMsgPack.IO;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace LsMsgPack.Reading
{
  /// <summary>
  /// The state of one Deserialize call.
  /// </summary>
  /// <para>Public for the code of the source generator (see LsMsgPack.LsMsgPackContext): may change between releases.</para>
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

    /// <summary>
    /// The root value of this call is read into <see cref="Slot"/> instead of a box (a struct of a typed call, see <see cref="RootSlot"/>).
    /// </summary>
    internal bool IntoSlot;

    /// <summary>
    /// The slot of the last struct root type read with this context (it is reused per thread, a nested call has its own).
    /// </summary>
    internal RootSlot Slot;

    /// <summary>
    /// Where the root value of the call starts (the paths of references start there).
    /// </summary>
    internal int BodyStart;

    /// <summary>
    /// The references of the call: made where the root starts with <see cref="ReferenceHandling.Preserve"/>, otherwise at the first reference (read again, as copies). Null otherwise.
    /// </summary>
    internal ByteReferences References;

    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/>: the table of the context's calls, <see cref="References"/> while one runs (emptied at its end).
    /// </summary>
    internal ByteReferences PreservingReferences;

    /// <summary>
    /// A value of the body of a stream head read on its own: where it is in the body's root value (its reference paths start there; null inside a key), asked when the references are made. Null otherwise.
    /// </summary>
    internal Func<int[]> ReferencePrefix;

    /// <summary>
    /// The decompressed body of the call (<see cref="MsgPackOptions.Compression"/>, rented from the array pool), which the reader reads instead of the data from where the body starts; null otherwise.
    /// <see cref="Serializer"/> clears and returns it when the call ends.
    /// </summary>
    internal byte[] Decompressed;
    internal int DecompressedLength;

    /// <summary>
    /// Where the compressed body ends in the caller's data (the position after the payload), while <see cref="Decompressed"/> is set.
    /// </summary>
    internal int CompressedEnd;

    /// <summary>
    /// An existing object the value starting at <see cref="IntoAt"/> is read into instead of a new one (docs/Populate.md): the root of a Populate call, or the object a member holds
    /// (<see cref="PopulatePropReader"/>). Taken by ReadPlan.Create, and by the conversions of LsMsgPack.Core when the value is read there (<see cref="ValueReader{T}"/>.Slow), also when it is read again.
    /// </summary>
    internal object Into;

    /// <summary>
    /// Where the value read into <see cref="Into"/> starts, -1 for none: the objects inside it start further on, so they are created as usual.
    /// </summary>
    internal int IntoAt = -1;

    internal ByteReferences ReferencesOfCall()
    {
      return References ?? (References = new ByteReferences(this, BodyStart, false));
    }

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

    /// <summary>
    /// Reads the root value into the context's <see cref="RootSlot{T}"/> and returns the slot (<see cref="ReadContext.IntoSlot"/>).
    /// </summary>
    internal abstract object ReadIntoSlot(ReadContext c);

    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/>: Core registers what it makes by the plain values (their offsets noted by ReadPlain) and finds what a reference read first.
    /// </summary>
    protected static object ConvertPreserving(object plain, Type type, ReadContext c, FullPropertyInfo assignedTo, ByteReferences references)
    {
      ReferenceResolver outer = ReferenceResolver.Current;
      ReferenceResolver.Current = references;
      try
      {
        return ValueConverter.ConvertDeserializeValue(plain, type, c.ConversionSettings(), assignedTo);
      }
      finally
      {
        ReferenceResolver.Current = outer;
      }
    }
  }

  /// <summary>
  /// Reads values of a type from the formats it expects. Any other format is read as a plain value and converted by LsMsgPack.Core's ValueConverter, as LsMsgPack does with every value.
  /// </summary>
  internal abstract class ValueReader<T> : ValueReader, IPreservableReader
  {
    internal abstract T Read(ReadContext c, FullPropertyInfo assignedTo);

    /// <summary>
    /// Created here, so NativeAOT has the instantiation wherever it has this one.
    /// </summary>
    ValueReader IPreservableReader.Preserving()
    {
      return this is ICollectionContent ? new PreservingCollectionReader<T>(this) : new PreservingReader<T>(this);
    }

    internal override object ReadBoxed(ReadContext c, FullPropertyInfo assignedTo)
    {
      return Read(c, assignedTo);
    }

    internal sealed override object ReadIntoSlot(ReadContext c)
    {
      RootSlot<T> slot = RootSlot<T>.Of(ref c.Slot);
      slot.Value = Read(c, null);
      return slot;
    }

    /// <summary>
    /// <see cref="Slow"/> of a value read into <see cref="ReadContext.Into"/> (Core's ValueConverter.ConvertInto): returns that object, or what the data has instead (a reference to another one).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T SlowInto(ReadContext c, object plain, FullPropertyInfo assignedTo, ByteReferences references)
    {
      MsgPackOptions settings = c.ConversionSettings();
      object value;
      if (references != null && references.Preserves)
      {
        ReferenceResolver outer = ReferenceResolver.Current;
        ReferenceResolver.Current = references;
        try
        {
          value = ValueConverter.ConvertInto(plain, typeof(T), c.Into, settings, assignedTo);
        }
        finally
        {
          ReferenceResolver.Current = outer;
        }
      }
      else
        value = ValueConverter.ConvertInto(plain, typeof(T), c.Into, settings, assignedTo);
      if (value is null)
        return default(T);
      if (value is T typed)
        return typed;
      throw ReadDifferences.NotConverted(settings, new ArgumentException($"Object of type '{value.GetType()}' cannot be converted to type '{typeof(T)}'."));
    }

    /// <summary>
    /// Reads the value at <paramref name="start"/> as LsMsgPack does: unpacked, then converted.
    /// </summary>
    protected static T Slow(ReadContext c, int start, FullPropertyInfo assignedTo)
    {
      if (c.R.Pos > start) // the header was read (and counted for MsgPackOptions.MaxObjects): ReadPlain counts it again
        c.R.Uncount(start);
      c.R.Pos = start;
      object plain = c.R.ReadPlain(c.Depth);
      ByteReferences references = c.References;
      if (c.IntoAt == start && c.Into != null) // read into an existing object (Populate)
        return SlowInto(c, plain, assignedTo, references);
      object value = references != null && references.Preserves
        ? ConvertPreserving(plain, typeof(T), c, assignedTo, references)
        : ValueConverter.ConvertDeserializeValue(plain, typeof(T), c.ConversionSettings(), assignedTo);
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
  /// <see cref="ReferenceHandling.Preserve"/>: a reader of a class whose values are references or may be referred to (LsMsgPackDebug: PlainReferences): a reference is resolved,
  /// what was read is registered where it starts (an object also when it is created, before its values: ReadPlan.Create). A reader of its own, so the others keep their size.
  /// </summary>
  internal class PreservingReader<T> : ValueReader<T>
  {
    protected readonly ValueReader<T> _inner;

    internal PreservingReader(ValueReader<T> inner)
    {
      _inner = inner;
    }

    internal override T Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (ByteReferences.IsReference(c.R))
      {
        object resolved = c.ReferencesOfCall().ReadReference(typeof(T), assignedTo);
        if (resolved is null || resolved is T)
          return (T)resolved;
        throw ReadDifferences.NotConverted(c.ConversionSettings(), new ArgumentException($"Object of type '{resolved.GetType()}' cannot be converted to type '{typeof(T)}'."));
      }
      ByteReferences references = c.References;
      if (references is null)
        return _inner.Read(c, assignedTo);
      if (references.TryGetMade(start, typeof(T), out object made))
      {
        c.R.Skip(c.Depth);
        return (T)made;
      }
      int slot = references.Reserve(start);
      T value = _inner.Read(c, assignedTo);
      references.Fill(slot, start, value); // a collection, a converted value, an object made by its constructor with values (others: also at their creation)
      return value;
    }
  }

  /// <summary>
  /// <see cref="PreservingReader{T}"/> of a collection reader that also reads the content of a wrapped value or one declared as an interface (<see cref="ICollectionContent"/>): registered where the content starts.
  /// </summary>
  internal sealed class PreservingCollectionReader<T> : PreservingReader<T>, ICollectionContent
  {
    internal PreservingCollectionReader(ValueReader<T> inner) : base(inner)
    {
    }

    bool ICollectionContent.TryReadContent(ReadContext c, out object value)
    {
      int start = c.R.Pos;
      ByteReferences references = c.References;
      if (references is null)
        return ((ICollectionContent)_inner).TryReadContent(c, out value);
      if (references.TryGetMade(start, typeof(T), out value))
      {
        c.R.Skip(c.Depth);
        return true;
      }
      int slot = references.Reserve(start);
      if (!((ICollectionContent)_inner).TryReadContent(c, out value))
        return false;
      references.Fill(slot, start, value);
      return true;
    }
  }

  internal interface IPreservableReader
  {
    ValueReader Preserving();
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

  /// <summary>
  /// <see cref="BoxedReader{T}"/> without a type argument: the reader of a value type NativeAOT has no generic reader of (no dynamic code, a type the application and the
  /// source generator did not use as a type argument: <see cref="Serializer.Instantiate"/> returned null). Read as LsMsgPack does, the same steps as <see cref="ValueReader{T}.Slow"/>;
  /// nil is null (a property or argument set by reflection takes it as the default, <see cref="RootSlot{T}.Take"/> too).
  /// Generic code asking for a <see cref="ValueReader{T}"/> of the type gets a BoxedReader of its own (<see cref="Serializer.Reader{T}"/>): it has that instantiation.
  /// </summary>
  internal sealed class ConvertingReader : ValueReader
  {
    private readonly Type _type;

    internal ConvertingReader(Type type)
    {
      _type = type;
    }

    internal override object ReadBoxed(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      object plain = c.R.ReadPlain(c.Depth);
      ByteReferences references = c.References;
      object value;
      MsgPackOptions settings = c.ConversionSettings();
      bool into = c.IntoAt == start && c.Into != null;
      if (into) // read into an existing object (Populate)
      {
        ReferenceResolver outer = ReferenceResolver.Current;
        if (references != null && references.Preserves)
          ReferenceResolver.Current = references;
        try
        {
          value = ValueConverter.ConvertInto(plain, _type, c.Into, settings, assignedTo);
        }
        finally
        {
          ReferenceResolver.Current = outer;
        }
      }
      else
      {
        value = references != null && references.Preserves
          ? ConvertPreserving(plain, _type, c, assignedTo, references)
          : ValueConverter.ConvertDeserializeValue(plain, _type, settings, assignedTo);
      }
      if (value is null || _type.IsInstanceOfType(value))
        return value;
      if (!into && c.Depth == 0 && assignedTo is null) // the root: LsMsgPack returns what it could convert, only its generic Deserialize casts
        throw new RootValue(value);
      throw ReadDifferences.NotConverted(settings, new ArgumentException($"Object of type '{value.GetType()}' cannot be converted to type '{_type}'."));
    }

    internal override object ReadIntoSlot(ReadContext c)
    {
      return ReadBoxed(c, null); // RootSlot<T>.Take takes a boxed value
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
        byte[] value = MsgPackWriter.NewBytes(length); // the length is checked against the data (TryReadBinHeader)
        new ReadOnlySpan<byte>(c.R.Buf, c.R.Pos, length).CopyTo(value);
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
      if (c.R.TryReadGuid(out Guid value) || c.R.TryReadGuidString(out value)) return value;
      return ReadText(c, start, assignedTo);
    }

    /// <summary>
    /// Any other text: decoded and parsed by .NET. Not inlined: the <c>stackalloc</c> gives a method a stack cookie and a larger frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Guid ReadText(ReadContext c, int start, FullPropertyInfo assignedTo)
    {
      Guid value;
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

    internal DecimalStringReader(LsExtension extension)
    {
      _extensionCode = extension is DecimalExtension builtIn && builtIn.TypeCode != -1 ? unchecked((byte)builtIn.TypeCode) : -1;
    }

    internal override decimal Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      MsgPackReader r = c.R;
      int start = r.Pos;
      if (r.TryReadDecimalString(out decimal simple))
        return simple;
      if (r.End - start >= 18 && r.Buf[start] == 0xD8 && r.Buf[start + 1] == _extensionCode)
      {
        decimal value = Unsafe.ReadUnaligned<decimal>(ref r.Buf[start + 2]); // as DecimalReader
        r.Pos = start + 18;
        return value;
      }
      return ReadText(c, start, assignedTo);
    }

    /// <summary>
    /// Any other text: decoded and parsed by .NET. Not inlined: the <c>stackalloc</c> gives a method a stack cookie and a larger frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private decimal ReadText(ReadContext c, int start, FullPropertyInfo assignedTo)
    {
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
    private readonly LsExtension<T> _extension;

    internal ExtensionReader(LsExtension<T> extension)
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
  /// An enum from any integer (Enum.ToObject: values out of range wrap around, as in LsMsgPack), or from a name (<see cref="EnumFormat.Name"/>, whatever the options write):
  /// a written or .NET name matched as UTF-8 bytes in place, anything else (other case, flags "A, B", numbers as text, other encodings) through Core (EnumNames.Parse).
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
      return ReadName(c, start, assignedTo); // not inlined into the integer path: names are the rare form
    }

    private static TEnum ReadName(ReadContext c, int start, FullPropertyInfo assignedTo)
    {
      int length = c.R.TryReadStringHeader();
      if (length >= 0 && EnumNameReading<TEnum>.TryMatch(c.R.Buf, c.R.Pos, length, c.Serializer.Options._enumNamingPolicy, out ulong bits))
      {
        c.R.Pos += length;
        return FromBits(bits);
      }
      c.R.Pos = start;
      return Slow(c, start, assignedTo);
    }

    internal static TEnum FromBits(ulong value)
    {
      switch (Unsafe.SizeOf<TEnum>())
      {
        case 1: { byte b = (byte)value; return Unsafe.As<byte, TEnum>(ref b); }
        case 2: { ushort s = (ushort)value; return Unsafe.As<ushort, TEnum>(ref s); }
        case 4: { uint i = (uint)value; return Unsafe.As<uint, TEnum>(ref i); }
        default: return Unsafe.As<ulong, TEnum>(ref value);
      }
    }
  }

  /// <summary>
  /// The written and .NET names of an enum as UTF-8 bytes (made when the first name is read), compared in place: exact matches only, Core does the rest.
  /// </summary>
  internal static class EnumNameReading<TEnum> where TEnum : struct
  {
    internal static readonly bool Signed = IsSigned();

    private struct Name
    {
      internal byte[] Bytes;
      internal ulong Bits;
    }

    private static Name[] _names; // null until the first name is read (the same table by any thread)

    /// <summary>
    /// The table of the last <see cref="MsgPackOptions.EnumNamingPolicy"/> read with (an application has one, usually), replaced by another policy's.
    /// </summary>
    private static PolicyNames _ofPolicy;

    private sealed class PolicyNames
    {
      internal MsgPackNamingPolicy Policy;
      internal Name[] Names;
    }

    private static bool IsSigned()
    {
      TypeCode code = Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum)));
      return code == TypeCode.SByte || code == TypeCode.Int16 || code == TypeCode.Int32 || code == TypeCode.Int64;
    }

    internal static bool TryMatch(byte[] buffer, int at, int length, MsgPackNamingPolicy policy, out ulong bits)
    {
      Name[] names = policy is null ? _names : OfPolicy(policy);
      if (names is null)
      {
        if (!MsgPackWriter.IsUtf8(MsgPackOptions.StringEncoding))
        {
          bits = 0;
          return false;
        }
        names = Build(policy);
      }
      for (int t = 0; t < names.Length; t++)
      {
        byte[] name = names[t].Bytes;
        if (name.Length == length && new ReadOnlySpan<byte>(buffer, at, length).SequenceEqual(name))
        {
          bits = names[t].Bits;
          return true;
        }
      }
      bits = 0;
      return false;
    }

    private static Name[] OfPolicy(MsgPackNamingPolicy policy)
    {
      PolicyNames last = _ofPolicy;
      return last != null && ReferenceEquals(last.Policy, policy) ? last.Names : null;
    }

    private static Name[] Build(MsgPackNamingPolicy policy)
    {
      List<KeyValuePair<string, ulong>> all = EnumNames.Of(typeof(TEnum), policy).AllNames();
      Name[] names = new Name[all.Count];
      for (int t = 0; t < all.Count; t++)
        names[t] = new Name { Bytes = Encoding.UTF8.GetBytes(all[t].Key), Bits = all[t].Value };
      if (policy is null)
        _names = names;
      else
        _ofPolicy = new PolicyNames { Policy = policy, Names = names };
      return names;
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
      c.References?.Register(start, list); // ReferenceHandling.Preserve: before its elements, so a cycle back to it resolves
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
      c.References?.Register(start, array); // ReferenceHandling.Preserve: before its elements, so a cycle back to it resolves
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
  /// An array of a type read in bulk (see <see cref="Writing.Numbers"/>, Serializer.Bulk): the same values and errors as <see cref="ArrayReader{TItem}"/>.
  /// The values in the formats of the type are read in bulk, any other value (nil, another format, an incomplete one) by the reader of the element type
  /// (1000 values: int 8.0 -> 2.4 us, long 7.7 -> 3.9 us, float 3.0 -> 1.4 us, double 4.2 -> 2.3 us, short 8.3-9.6 -> 3.8 us, uint 7.5-8.6 -> 3.1-3.2 us, bool 2.1-2.7 -> 1.3 us,
  /// Guid 8.8-9.1 -> 2.5-2.8 us, decimal 4.7 -> 2.1 us; mostly allocating the array).
  /// </summary>
  internal sealed class NumberArrayReader<T> : ValueReader<T[]>, ICollectionContent where T : struct
  {
    private readonly ValueReader<T> _item;
    private readonly byte _decimalCode;

    internal NumberArrayReader(ValueReader<T> item, byte decimalCode)
    {
      _item = item;
      _decimalCode = decimalCode;
    }

    internal override T[] Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadNil())
        return null;

      int count = c.R.TryReadArrayHeader();
      if (count < 0)
        return Slow(c, start, assignedTo);

      c.Enter();
#if NET8_0_OR_GREATER
      T[] array = GC.AllocateUninitializedArray<T>(count); // every element is set before it is returned
#else
      T[] array = new T[count];
#endif
      int t = 0;
      while (t < count)
      {
        t += Writing.Numbers.Read(c.R, new Span<T>(array, t, count - t), _decimalCode);
        if (t < count)
          array[t++] = _item.Read(c, null);
      }
      c.Depth--;
      return array;
    }

    bool ICollectionContent.TryReadContent(ReadContext c, out object value)
    {
      value = CollectionRules.IsArray(c.R) ? Read(c, null) : null;
      return value != null;
    }
  }

#if NET8_0_OR_GREATER
  /// <summary>
  /// List&lt;T&gt; of a type read in bulk, as <see cref="NumberArrayReader{T}"/>: read into the list's own array.
  /// </summary>
  internal sealed class NumberListReader<T> : ValueReader<List<T>>, ICollectionContent where T : struct
  {
    private readonly ValueReader<T> _item;
    private readonly byte _decimalCode;

    internal NumberListReader(ValueReader<T> item, byte decimalCode)
    {
      _item = item;
      _decimalCode = decimalCode;
    }

    internal override List<T> Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadNil())
        return null;

      int count = c.R.TryReadArrayHeader();
      if (count < 0)
        return Slow(c, start, assignedTo);

      c.Enter();
      List<T> list = new List<T>(count);
      System.Runtime.InteropServices.CollectionsMarshal.SetCount(list, count);
      Span<T> items = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
      int t = 0;
      while (t < count)
      {
        t += Writing.Numbers.Read(c.R, items.Slice(t), _decimalCode);
        if (t < count)
          items[t++] = _item.Read(c, null);
      }
      c.Depth--;
      return list;
    }

    bool ICollectionContent.TryReadContent(ReadContext c, out object value)
    {
      value = CollectionRules.IsArray(c.R) ? Read(c, null) : null;
      return value != null;
    }
  }
#endif

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
      return ReadItems(c, start, count);
    }

    bool ICollectionContent.TryReadContent(ReadContext c, out object value)
    {
      int start = c.R.Pos;
      int count = c.R.TryReadArrayHeader();
      value = count < 0 ? null : ReadItems(c, start, count);
      return count >= 0;
    }

    private HashSet<TItem> ReadItems(ReadContext c, int start, int count)
    {
      ValueReader<TItem> item = _item ?? (_item = _serializer.Reader<TItem>());
      c.Enter();
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      HashSet<TItem> set = new HashSet<TItem>(count); // the header's count is checked against the data (MsgPackReader.Count)
#else
      HashSet<TItem> set = new HashSet<TItem>();
#endif
      c.References?.Register(start, set); // ReferenceHandling.Preserve: before its elements, so a cycle back to it resolves
      for (int t = 0; t < count; t++)
        set.Add(item.Read(c, null));
      c.Depth--;
      return set;
    }
  }

  /// <summary>
  /// HashSet&lt;T&gt; of a type read in bulk (see <see cref="Writing.Numbers"/>), as <see cref="HashSetReader{TItem}"/>: a block of values at a time, then added to the set;
  /// any other value (nil, another format, an incomplete one) by the reader of the element type.
  /// </summary>
  internal sealed class NumberSetReader<T> : ValueReader<HashSet<T>>, ICollectionContent where T : unmanaged
  {
    private const int Block = 32;
    private readonly ValueReader<T> _item;
    private readonly byte _decimalCode;

    internal NumberSetReader(ValueReader<T> item, byte decimalCode)
    {
      _item = item;
      _decimalCode = decimalCode;
    }

    internal override HashSet<T> Read(ReadContext c, FullPropertyInfo assignedTo)
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

    private HashSet<T> ReadItems(ReadContext c, int count)
    {
      c.Enter();
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      HashSet<T> set = new HashSet<T>(count); // the header's count is checked against the data (MsgPackReader.Count)
#else
      HashSet<T> set = new HashSet<T>();
#endif
      Span<T> block = stackalloc T[Block];
      int t = 0;
      while (t < count)
      {
        int wanted = Math.Min(Block, count - t);
        int read = Writing.Numbers.Read(c.R, block.Slice(0, wanted), _decimalCode);
        for (int i = 0; i < read; i++)
          set.Add(block[i]);
        t += read;
        if (read < wanted)
        {
          set.Add(_item.Read(c, null));
          t++;
        }
      }
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
      int start = r.Pos;
      int count = r.TryReadMapHeader();
      if (count < 0)
        return false;

      ValueReader<TKey> keys = _key ?? (_key = _serializer.Reader<TKey>());
      ValueReader<TValue> values = _value ?? (_value = _serializer.Reader<TValue>());
      int depth = c.Depth;
      c.Enter();
      Dictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>(count); // the header's count is checked against the data (MsgPackReader.Count)
      c.References?.Register(start, dictionary); // ReferenceHandling.Preserve: before its entries, so a cycle back to it resolves
      for (int t = 0; t < count; t++)
      {
        if (!CollectionRules.IsEntryKey(r, _stringKeys))
        {
          c.Depth = depth;
          c.References?.Unregister(start); // read again from its start by Core (Slow)
          return false;
        }
        TKey key = keys.Read(c, null);
        if (_valueTakesNoNil && r.Peek() == 0xC0)
        {
          c.Depth = depth;
          c.References?.Unregister(start);
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
