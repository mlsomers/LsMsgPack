using LsMsgPack.Converters;
using LsMsgPack.IO;
using LsMsgPack.Meta;
using LsMsgPack.References;
using System;
using System.Runtime.CompilerServices;

namespace LsMsgPack.Writing
{
  /// <summary>
  /// A converted value written by <see cref="Serializer.WriteBoxed(WriteContext, object, FullPropertyInfo)"/> (declared as another type, or every value with <c>AddTypeIdOption.Always</c>).
  /// </summary>
  internal interface IConvertedWriter
  {
    /// <param name="value">Not null, of exactly the converted type</param>
    void WriteBoxed(WriteContext c, object value);
  }

  /// <summary>
  /// The values of a type with a converter (<see cref="MsgPackOptions.Converters"/>, <see cref="MsgPackConverterAttribute"/>): their surrogates, written by the typed handler of the surrogate type as values declared as that type
  /// (LsMsgPackDebug: SerializeConverted). A value of a class deriving from <typeparamref name="T"/> is written as an object of its own type (converters are for one exact type).
  /// </summary>
  internal sealed class ConverterHandler<T, TSurrogate> : ValueHandler<T>, IConvertedWriter
  {
    private readonly Serializer _serializer;
    private readonly MsgPackConverter<T, TSurrogate> _converter;
    private readonly FullPropertyInfo _surrogateInfo;
    private ValueHandler<TSurrogate> _surrogate;
    private RecentTypes _recent;

    /// <summary>
    /// <c>AddTypeIdOption.Always</c>: the handler of a member's own converter wraps the surrogate with the type id as the boxed values are (the type's converters are written boxed then).
    /// </summary>
    private readonly bool _always;

    internal ConverterHandler(Serializer serializer, MsgPackConverter<T, TSurrogate> converter)
    {
      _serializer = serializer;
      _converter = converter;
      _surrogateInfo = converter.SurrogateInfo;
      _always = serializer.AlwaysTypeId;
    }

    internal override void Write(WriteContext c, T value, FullPropertyInfo assignedTo)
    {
      if (!typeof(T).IsValueType && value.GetType() != typeof(T)) // the JIT drops the test for structs
      {
        WriteDerived(c, value, assignedTo);
        return;
      }
      if (_always)
      {
        _serializer.WriteConverted(c, value, typeof(T), this, assignedTo);
        return;
      }
      WriteSurrogate(c, value);
    }

    private void WriteSurrogate(WriteContext c, T value)
    {
      TSurrogate surrogate = _converter.ToSurrogate(value);
      if (surrogate == null)
      {
        c.W.Nil();
        return;
      }
      ValueHandler<TSurrogate> handler = _surrogate ?? (_surrogate = _serializer.Handler<TSurrogate>(null));
      if (!typeof(TSurrogate).IsValueType && c.References != null && _serializer.PreserveReferences) // the converted value keeps the identity, not its surrogate (LsMsgPackDebug: SerializeSurrogate)
        c.References.Surrogate = surrogate;
      handler.Write(c, surrogate, _surrogateInfo);
    }

    void IConvertedWriter.WriteBoxed(WriteContext c, object value)
    {
      WriteSurrogate(c, (T)value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteDerived(WriteContext c, object value, FullPropertyInfo assignedTo)
    {
      Type type = value.GetType();
      RecentTypes recent = _recent ?? (_recent = new RecentTypes());
      _serializer.WriteBoxedAs(c, value, type, recent.Get(_serializer, type), assignedTo);
    }

    /// <summary>
    /// FilterDefaultValues compares the member's own value (LsMsgPackDebug asks the filters before it converts it).
    /// </summary>
    internal override bool IncludeByDefault(T value, FullPropertyInfo info)
    {
      if (typeof(T).IsValueType)
        return !StructDefault<T>.IsDefault(ref value);
      return DefaultFilter.Instance.IncludeProperty(info, value);
    }
  }

  /// <summary>
  /// The values of a type with a binary converter (<see cref="MsgPackBinaryConverter{T}"/>, the blit converters): the converter writes its bytes straight into the buffer after the header of bin
  /// (or of its extension), no array in between (LsMsgPackDebug: SerializeConverted, the same bytes). A value of a class deriving from <typeparamref name="T"/> is written as an object of its own type.
  /// </summary>
  internal sealed class BinaryConverterHandler<T> : ValueHandler<T>, IConvertedWriter
  {
    private readonly Serializer _serializer;
    private readonly MsgPackBinaryConverter<T> _converter;
    private readonly int _extension; // the extension type code, -1 for bin
    private readonly bool _always; // see ConverterHandler
    private RecentTypes _recent;

    internal BinaryConverterHandler(Serializer serializer, MsgPackBinaryConverter<T> converter)
    {
      _serializer = serializer;
      _converter = converter;
      _extension = converter.ExtensionTypeCode ?? -1;
      _always = serializer.AlwaysTypeId;
      ReservedCodes.ThrowIfUsed(converter, serializer.Options); // also one of [MsgPackConverter] on a type
    }

    internal override void Write(WriteContext c, T value, FullPropertyInfo assignedTo)
    {
      if (!typeof(T).IsValueType && value.GetType() != typeof(T)) // the JIT drops the test for structs
      {
        WriteDerived(c, value, assignedTo);
        return;
      }
      if (_always)
      {
        _serializer.WriteConverted(c, value, typeof(T), this, assignedTo);
        return;
      }
      WriteBytes(c, value);
    }

    private void WriteBytes(WriteContext c, T value)
    {
      int length = _converter.GetLength(value);
      MsgPackWriter w = c.W;
      if (_extension < 0)
        w.BinHeader(length);
      else
        w.ExtHeader((sbyte)_extension, length);
      w.Ensure(length);
      _converter.Write(value, new Span<byte>(w.Buf, w.Pos, length));
      w.Pos += length;
    }

    void IConvertedWriter.WriteBoxed(WriteContext c, object value)
    {
      WriteBytes(c, (T)value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteDerived(WriteContext c, object value, FullPropertyInfo assignedTo)
    {
      Type type = value.GetType();
      RecentTypes recent = _recent ?? (_recent = new RecentTypes());
      _serializer.WriteBoxedAs(c, value, type, recent.Get(_serializer, type), assignedTo);
    }

    /// <summary>
    /// FilterDefaultValues compares the member's own value (as ConverterHandler).
    /// </summary>
    internal override bool IncludeByDefault(T value, FullPropertyInfo info)
    {
      if (typeof(T).IsValueType)
        return !StructDefault<T>.IsDefault(ref value);
      return DefaultFilter.Instance.IncludeProperty(info, value);
    }
  }
}
