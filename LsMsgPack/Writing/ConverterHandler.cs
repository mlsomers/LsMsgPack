using LsMsgPack.Converters;
using LsMsgPack.Meta;
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

    internal ConverterHandler(Serializer serializer, MsgPackConverter<T, TSurrogate> converter)
    {
      _serializer = serializer;
      _converter = converter;
      _surrogateInfo = converter.SurrogateInfo;
    }

    internal override void Write(WriteContext c, T value, FullPropertyInfo assignedTo)
    {
      if (!typeof(T).IsValueType && value.GetType() != typeof(T)) // the JIT drops the test for structs
      {
        WriteDerived(c, value, assignedTo);
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
}
