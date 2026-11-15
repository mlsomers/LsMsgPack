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

  /// <summary>
  /// The values of a type with a binary converter: the bytes of bin (or of an extension of the converter's type code) handed to the converter in place; nil is the default.
  /// Anything else goes to LsMsgPack.Core (<see cref="ValueReader{T}.Slow"/>: a type id, or the error).
  /// </summary>
  internal sealed class BinaryConverterReader<T> : ValueReader<T>
  {
    private readonly MsgPackBinaryConverter<T> _converter;
    private readonly int _extension; // the extension type code, -1 for none

    internal BinaryConverterReader(Serializer serializer, MsgPackBinaryConverter<T> converter)
    {
      _converter = converter;
      _extension = converter.ExtensionTypeCode ?? -1;
    }

    internal override T Read(ReadContext c, FullPropertyInfo assignedTo)
    {
      MsgPackReader r = c.R;
      int start = r.Pos;
      if (r.TryReadNil())
        return default(T);
      int length = r.TryReadBinHeader();
      if (length < 0)
      {
        if (_extension < 0 || !r.TryReadExtHeader(out sbyte typeCode, out length, out LsMsgPack.MsgPackTypeId format) || typeCode != _extension)
          return Slow(c, start, assignedTo); // the position is rewound there
      }
      T value = _converter.Read(new System.ReadOnlySpan<byte>(r.Buf, r.Pos, length)); // the lengths are checked against the data
      r.Pos += length;
      return value;
    }
  }
}
