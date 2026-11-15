using LsMsgPack.Converters;
using LsMsgPack.IO;
using LsMsgPack.Meta;

namespace LsMsgPack.Reading
{
  /// <summary>
  /// The values of a type with a converter: read by the reader of the surrogate type, then converted back (nil is the default, the converter is not called).
  /// A map with a type id goes to LsMsgPack.Core (<see cref="ValueReader{T}.Slow"/>), which tells a surrogate of a derived surrogate class from an object of a class deriving from <typeparamref name="T"/>.
  /// </summary>
  internal sealed class ConverterReader<T, TSurrogate> : ValueReader<T>
  {
    private readonly Serializer _serializer;
    private readonly MsgPackConverter<T, TSurrogate> _converter;
    private readonly FullPropertyInfo _surrogateInfo;
    private ValueReader<TSurrogate> _surrogate;

    internal ConverterReader(Serializer serializer, MsgPackConverter<T, TSurrogate> converter)
    {
      _serializer = serializer;
      _converter = converter;
      _surrogateInfo = converter.SurrogateInfo;
    }

    internal override T Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      int start = c.R.Pos;
      if (c.R.TryReadNil())
        return default(T);
      if (StartsWithTypeId(c.R))
        return Slow(c, start, assignedTo);
      ValueReader<TSurrogate> reader = _surrogate ?? (_surrogate = _serializer.Reader<TSurrogate>());
      TSurrogate surrogate = reader.Read(c, _surrogateInfo);
      if (surrogate == null)
        return default(T);
      return _converter.FromSurrogate(surrogate);
    }

    /// <summary>
    /// A map whose first key is the type id ("", as both serializers write it first).
    /// </summary>
    private static bool StartsWithTypeId(MsgPackReader r)
    {
      byte[] b = r.Buf;
      int at = r.Pos;
      if (at >= r.End)
        return false;
      byte format = b[at];
      int key;
      if (format >= 0x81 && format <= 0x8F)
        key = at + 1;
      else if (format == 0xDE && at + 2 < r.End && (b[at + 1] | b[at + 2]) != 0)
        key = at + 3;
      else if (format == 0xDF && at + 4 < r.End && (b[at + 1] | b[at + 2] | b[at + 3] | b[at + 4]) != 0)
        key = at + 5;
      else
        return false;
      return key < r.End && b[key] == 0xA0;
    }
  }
}
