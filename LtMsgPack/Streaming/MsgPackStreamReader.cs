using LsMsgPack;
using LsMsgPack.Streaming;
using LtMsgPack.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace LtMsgPack.Streaming
{
  /// <summary>
  /// Reads MsgPack values one after another from a stream, as tokens (<see cref="Read"/>) or whole objects with their schema (<see cref="ReadObject{T}"/>, or one schema for a whole root value: <see cref="ReadHead{TRoot}"/>), also arrays, maps, str and bin of unknown length
  /// (the escape sequences of msgpack/msgpack#347, see <see cref="MsgPackStreamWriter"/>). The stream does not have to seek: it is read in blocks into a buffer.
  /// <para>The buffer holds the current token and grows only as far as the data that arrives (a header claiming more than the stream holds costs no more than the stream holds).
  /// A value that is read whole (<see cref="ReadObject{T}"/>, <see cref="GetValue"/>, <see cref="GetString"/>...) is buffered whole.</para>
  /// <para>The index of a stream (<see cref="MsgPackStreamIndex"/>: extension values of types 3 and 4 between the root values, and the tails after them) is skipped. When the stream seeks,
  /// <see cref="ReadIndexes"/> reads it and <see cref="Seek(MsgPackStreamEntry)"/> / <see cref="SeekElement"/> go to an entry or an element of its array.</para>
  /// <para>Not thread-safe.</para>
  /// </summary>
  public sealed class MsgPackStreamReader : IDisposable
  {
    private const int InitialCapacity = 16 * 1024;
    private const int MaxBufferLength = 0x7FFFFFC7; // Array.MaxLength
    private const long MaxUnknownCount = uint.MaxValue;

    private readonly Stream _source;
    private readonly long _origin; // where the reader started in a stream that seeks (positions are relative to it), 0 otherwise
    private readonly bool _leaveOpen;
    private readonly Serializer _serializer;
    private readonly LtMsgPackOptions _options;
    private readonly bool _littleEndian;
    private readonly int _maxDepth;
    private readonly MsgPackReader _r; // decodes buffered values as the serializer does (strings, timestamps, extensions, plain values)
    private MsgPackWriter _normalized; // a value without escape sequences (see Normalize)

    private byte[] _buf = new byte[InitialCapacity];
    private int _pos;
    private int _end;
    private long _base; // the offset of _buf[0] in the stream (relative to where the reader started)
    private bool _eof;
    private bool _disposed;

    /// <summary>
    /// The buffer is kept from here: the start of a value that is read whole, -1 for none (then only the current token is kept).
    /// </summary>
    private int _mark = -1;
    private bool _escaped; // escape sequences since the mark
    private readonly List<long> _counts = new List<long>(); // per value of unknown length since the mark (in the order of their starts): its number of elements, entries or bytes

    private MsgPackTokenType _token;
    private int _tokenStart;
    private long _length;
    private bool _payloadPending; // the data of a String, Binary or Extension token (from _pos, _length bytes) has not been passed yet
    private ulong _integer;
    private bool _signed; // _integer is a long
    private double _float;
    private bool _bool;
    private sbyte _extensionType;
    private bool _isKey;

    private Frame[] _frames = new Frame[8];
    private int _depth;

    private readonly List<byte[]> _registered = new List<byte[]>(); // the inline schemas registered in the store (PerStream: later objects refer to them)

    private HeadState _headState;
    private Serializer.SchemaHead _head; // null without the indexed schema
    private Type _headRoot;

    /// <summary>
    /// A reader with the default options of <see cref="LtMsgPackSerializer.Default"/>. The stream is closed by <see cref="Dispose"/>.
    /// </summary>
    [RequiresUnreferencedCode(LtMsgPackSerializer.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LtMsgPackSerializer.RequiresDynamicCodeMessage)]
    public MsgPackStreamReader(Stream source) : this(source, LtMsgPackSerializer.Default) { }

    /// <param name="serializer">Reads the objects, and its options decide how values are read (byte order, extensions, DateTime kinds, <see cref="MsgPackOptions.MaxDepth"/>).
    /// For objects that refer to their schema it needs a <see cref="SchemaStore"/>: the schemas the stream holds inline are registered in it (<see cref="MsgPackStreamSchemas.PerStream"/>).</param>
    /// <param name="leaveOpen">Do not close the stream in <see cref="Dispose"/></param>
    public MsgPackStreamReader(Stream source, LtMsgPackSerializer serializer, bool leaveOpen = false)
    {
      if (source is null)
        throw new ArgumentNullException(nameof(source));
      if (serializer is null)
        throw new ArgumentNullException(nameof(serializer));
      if (!source.CanRead)
        throw new ArgumentException("The stream cannot be read.", nameof(source));

      _source = source;
      _origin = source.CanSeek ? source.Position : 0;
      _leaveOpen = leaveOpen;
      _serializer = serializer.Implementation;
      _options = _serializer.Options;
      _littleEndian = BitConverter.IsLittleEndian != MsgPackOptions.SwapEndianChoice(_options, 2);
      _maxDepth = _options._maxDepth;
      _r = new MsgPackReader(null, 0, 0, _options);
    }

    private enum FrameKind : byte
    {
      Array,
      Map,
      String,
      Binary
    }

    private struct Frame
    {
      internal FrameKind Kind;
      internal bool Unknown;

      /// <summary>
      /// Known length: the elements still to come (keys and values for a map).
      /// Unknown length: the elements read (keys and values for a map), the bytes for a str or bin.
      /// </summary>
      internal long Count;

      /// <summary>
      /// Where its count goes in _counts (read whole since the mark), -1 for none.
      /// </summary>
      internal int CountIndex;
    }

    private enum HeadState : byte
    {
      None,

      /// <summary>
      /// A head was read, the next root value is its body.
      /// </summary>
      AwaitingBody,

      /// <summary>
      /// In the body of the head (an array or map that is still open).
      /// </summary>
      InBody
    }

    #region State

    public MsgPackTokenType TokenType { get { return _token; } }

    /// <summary>
    /// The number of elements of an <see cref="MsgPackTokenType.ArrayStart"/>, the number of entries of a <see cref="MsgPackTokenType.MapStart"/>, -1 for the ones of unknown length;
    /// the number of bytes of a <see cref="MsgPackTokenType.String"/>, <see cref="MsgPackTokenType.Binary"/> or <see cref="MsgPackTokenType.Extension"/> (its data); otherwise 0.
    /// </summary>
    public long Length { get { return _length; } }

    /// <summary>
    /// The current token starts an array, map, str or bin of unknown length.
    /// </summary>
    public bool IsUnknownLength { get { return _length < 0; } }

    /// <summary>
    /// The number of open arrays, maps, str and bin (after the current token): 0 between the root values of the stream.
    /// </summary>
    public int Depth { get { return _depth; } }

    /// <summary>
    /// The current token is (the start of) a key of a map.
    /// </summary>
    public bool IsKey { get { return _isKey; } }

    /// <summary>
    /// Where the current token starts in the stream, relative to where the reader started.
    /// </summary>
    public long TokenStart { get { return _base + _tokenStart; } }

    /// <summary>
    /// Where the current token ends (with the data of a str, bin or extension) in the stream, relative to where the reader started: after a root value, where the next one starts.
    /// </summary>
    public long Position { get { return _base + _pos + (_payloadPending ? _length : 0); } }

    /// <summary>
    /// There are no more values here: the innermost open array, map, str or bin ends with the next token (its end sequence, or it has all its elements),
    /// or at <see cref="Depth"/> 0 the stream ends. For loops over the root values of a stream or the elements of an array of unknown length (<see cref="ReadObject{T}"/>); reads ahead when needed.
    /// </summary>
    public bool AtEnd
    {
      get
      {
        ThrowIfDisposed();
        if (_depth > 0 && !_frames[_depth - 1].Unknown)
          return _frames[_depth - 1].Count == 0;
        if (_payloadPending && _length >= MaxBufferLength - 2)
          return false;
        int next = _payloadPending ? (int)_length : 0; // after the data of the current token
        if (_depth == 0)
        {
          if (!_payloadPending && _mark < 0)
          {
            SkipStreamValues();
            return !TryEnsure(1);
          }
          return !TryEnsure(PastStreamValues(next) + 1);
        }
        return TryEnsure(next + 2) && _buf[_pos + next] == Escape.Byte && _buf[_pos + next + 1] == Escape.End;
      }
    }

    #endregion

    #region Tokens

    /// <summary>
    /// Moves to the next token (the data of the current one is passed without being buffered).
    /// </summary>
    /// <returns>false at the end of the stream between root values</returns>
    /// <exception cref="MsgPackException">The data is not valid (also an escape sequence where it does not belong), or the stream ends inside a value</exception>
    public bool Read()
    {
      if (_headState == HeadState.None)
        return ReadToken();

      int depth = _depth;
      bool read = ReadToken();
      if (_headState == HeadState.AwaitingBody)
      {
        if (read && depth == 0)
          _headState = _depth > 0 ? HeadState.InBody : HeadState.None; // the body starts (a scalar is all of it)
      }
      else if (_depth == 0)
        _headState = HeadState.None; // the end of the body
      if (_headState == HeadState.None)
        _head = null;
      return read;
    }

    private bool ReadToken()
    {
      ThrowIfDisposed();
      PassPayload();
      _length = 0;
      _isKey = false;
      _tokenStart = _pos;

      if (_depth > 0 && !_frames[_depth - 1].Unknown && _frames[_depth - 1].Count == 0) // after the last element of an array or map of known length
      {
        _token = _frames[_depth - 1].Kind == FrameKind.Map ? MsgPackTokenType.MapEnd : MsgPackTokenType.ArrayEnd;
        _depth--;
        return true;
      }

      if (_depth == 0 && _mark < 0)
      {
        SkipStreamValues();
        _tokenStart = _pos;
      }
      if (!TryEnsure(1))
      {
        if (_depth > 0)
          throw EndOfData();
        _token = MsgPackTokenType.None;
        return false;
      }

      byte b = _buf[_pos];
      if (b == Escape.Byte)
        ReadEscape();
      else
        ReadFormat(b);
      return true;
    }

    private void ReadEscape()
    {
      if (!TryEnsure(2))
        throw Escape.AtEndOfData(_base + _pos);
      byte code = _buf[_pos + 1];
      if (_mark >= 0)
        _escaped = true;

      if (code == Escape.End)
      {
        if (_depth == 0)
          throw Escape.MisplacedEnd("is outside of any value of unknown length", _base + _pos);
        Frame frame = _frames[_depth - 1];
        if (!frame.Unknown)
          throw Escape.MisplacedEnd($"is in {(frame.Kind == FrameKind.Map ? "a map" : "an array")} of known length", _base + _pos);
        if (frame.Kind == FrameKind.Map && (frame.Count & 1) != 0)
          throw Escape.MisplacedEnd("is where the value of a key of the map comes", _base + _pos);

        if (frame.CountIndex >= 0)
          _counts[frame.CountIndex] = frame.Kind == FrameKind.Map ? frame.Count / 2 : frame.Count;
        _pos += 2;
        _depth--;
        switch (frame.Kind)
        {
          case FrameKind.Array: _token = MsgPackTokenType.ArrayEnd; break;
          case FrameKind.Map: _token = MsgPackTokenType.MapEnd; break;
          case FrameKind.String: _token = MsgPackTokenType.StringEnd; break;
          default: _token = MsgPackTokenType.BinaryEnd; break;
        }
        return;
      }

      FrameKind kind;
      switch (code)
      {
        case Escape.ArrayStart: kind = FrameKind.Array; _token = MsgPackTokenType.ArrayStart; break;
        case Escape.MapStart: kind = FrameKind.Map; _token = MsgPackTokenType.MapStart; break;
        case Escape.StringStart: kind = FrameKind.String; _token = MsgPackTokenType.StringStart; break;
        case Escape.BinaryStart: kind = FrameKind.Binary; _token = MsgPackTokenType.BinaryStart; break;
        default: throw Escape.Reserved(code, _base + _pos);
      }

      ThrowIfInChunks(Escape.Byte);
      Element();
      _length = -1;
      _pos += 2;
      Push(kind, true, 0);
    }

    private void ReadFormat(byte b)
    {
      ThrowIfInChunks(b);
      if (_depth > 0 && (_frames[_depth - 1].Kind == FrameKind.String || _frames[_depth - 1].Kind == FrameKind.Binary))
      {
        Chunk(b);
        return;
      }
      Element();

      if (b <= 0x7F) { Integer(b, false, 1); return; }
      if (b >= 0xE0) { Integer(unchecked((ulong)(long)(sbyte)b), true, 1); return; }
      if ((b & 0xE0) == 0xA0) { Payload(MsgPackTokenType.String, 1, b & 0x1F); return; }
      if ((b & 0xF0) == 0x90) { Container(FrameKind.Array, 1, b & 0x0F); return; }
      if ((b & 0xF0) == 0x80) { Container(FrameKind.Map, 1, b & 0x0F); return; }

      switch (b)
      {
        case 0xC0: _token = MsgPackTokenType.Nil; _pos++; return;
        case 0xC2: case 0xC3: _token = MsgPackTokenType.Boolean; _bool = b == 0xC3; _pos++; return;
        case 0xCC: Need(2); Integer(_buf[_pos + 1], false, 2); return;
        case 0xCD: Need(3); Integer(U16(_pos + 1), false, 3); return;
        case 0xCE: Need(5); Integer(U32(_pos + 1), false, 5); return;
        case 0xCF: Need(9); Integer(U64(_pos + 1), false, 9); return;
        case 0xD0: Need(2); Integer(unchecked((ulong)(long)(sbyte)_buf[_pos + 1]), true, 2); return;
        case 0xD1: Need(3); Integer(unchecked((ulong)(long)(short)U16(_pos + 1)), true, 3); return;
        case 0xD2: Need(5); Integer(unchecked((ulong)(long)(int)U32(_pos + 1)), true, 5); return;
        case 0xD3: Need(9); Integer(U64(_pos + 1), true, 9); return;
        case 0xCA: Need(5); Float(new SingleBits(U32(_pos + 1)).Value, 5); return;
        case 0xCB: Need(9); Float(BitConverter.Int64BitsToDouble(unchecked((long)U64(_pos + 1))), 9); return;
        case 0xD9: Need(2); Payload(MsgPackTokenType.String, 2, _buf[_pos + 1]); return;
        case 0xDA: Need(3); Payload(MsgPackTokenType.String, 3, U16(_pos + 1)); return;
        case 0xDB: Need(5); Payload(MsgPackTokenType.String, 5, U32(_pos + 1)); return;
        case 0xC4: Need(2); Payload(MsgPackTokenType.Binary, 2, _buf[_pos + 1]); return;
        case 0xC5: Need(3); Payload(MsgPackTokenType.Binary, 3, U16(_pos + 1)); return;
        case 0xC6: Need(5); Payload(MsgPackTokenType.Binary, 5, U32(_pos + 1)); return;
        case 0xDC: Need(3); Container(FrameKind.Array, 3, U16(_pos + 1)); return;
        case 0xDD: Need(5); Container(FrameKind.Array, 5, U32(_pos + 1)); return;
        case 0xDE: Need(3); Container(FrameKind.Map, 3, U16(_pos + 1)); return;
        case 0xDF: Need(5); Container(FrameKind.Map, 5, U32(_pos + 1)); return;
        case 0xD4: Extension(2, 1); return;
        case 0xD5: Extension(2, 2); return;
        case 0xD6: Extension(2, 4); return;
        case 0xD7: Extension(2, 8); return;
        case 0xD8: Extension(2, 16); return;
        case 0xC7: Need(2); Extension(3, _buf[_pos + 1]); return;
        case 0xC8: Need(3); Extension(4, U16(_pos + 1)); return;
        case 0xC9: Need(5); Extension(6, U32(_pos + 1)); return;
      }
      throw Escape.NotSupported(_base + _pos); // unreachable: 0xC1 is the escape byte
    }

    /// <summary>
    /// A chunk of a str or bin of unknown length: a str or bin, nothing else.
    /// </summary>
    private void Chunk(byte b)
    {
      int header;
      long length;
      if (_frames[_depth - 1].Kind == FrameKind.String)
      {
        if ((b & 0xE0) == 0xA0) { header = 1; length = b & 0x1F; }
        else if (b == 0xD9) { Need(2); header = 2; length = _buf[_pos + 1]; }
        else if (b == 0xDA) { Need(3); header = 3; length = U16(_pos + 1); }
        else if (b == 0xDB) { Need(5); header = 5; length = U32(_pos + 1); }
        else throw new MsgPackException($"A str of unknown length holds only str chunks, found the format 0x{b:X2}.", _base + _pos, (MsgPackTypeId)b);
      }
      else
      {
        if (b == 0xC4) { Need(2); header = 2; length = _buf[_pos + 1]; }
        else if (b == 0xC5) { Need(3); header = 3; length = U16(_pos + 1); }
        else if (b == 0xC6) { Need(5); header = 5; length = U32(_pos + 1); }
        else throw new MsgPackException($"A bin of unknown length holds only bin chunks, found the format 0x{b:X2}.", _base + _pos, (MsgPackTypeId)b);
      }

      if (_frames[_depth - 1].Count + length > MaxUnknownCount)
        throw new MsgPackException($"A {(_frames[_depth - 1].Kind == FrameKind.String ? "str" : "bin")} of unknown length holds at most {MaxUnknownCount} bytes.", _base + _pos);
      _frames[_depth - 1].Count += length;
      Payload(_frames[_depth - 1].Kind == FrameKind.String ? MsgPackTokenType.String : MsgPackTokenType.Binary, header, length);
    }

    private void ThrowIfInChunks(byte b)
    {
      if (b == Escape.Byte && _depth > 0 && (_frames[_depth - 1].Kind == FrameKind.String || _frames[_depth - 1].Kind == FrameKind.Binary))
        throw new MsgPackException($"A {(_frames[_depth - 1].Kind == FrameKind.String ? "str" : "bin")} of unknown length holds only {(_frames[_depth - 1].Kind == FrameKind.String ? "str" : "bin")} chunks, not values of unknown length.", _base + _pos);
    }

    /// <summary>
    /// The token starts an element of the open array or map.
    /// </summary>
    private void Element()
    {
      if (_depth == 0)
        return;
      Frame frame = _frames[_depth - 1];
      _isKey = frame.Kind == FrameKind.Map && (frame.Count & 1) == 0; // known: the keys and values still to come, unknown: the ones read
      if (!frame.Unknown)
        _frames[_depth - 1].Count--;
      else
      {
        if (frame.Count >= (frame.Kind == FrameKind.Map ? 2 * MaxUnknownCount : MaxUnknownCount))
          throw new MsgPackException($"{(frame.Kind == FrameKind.Map ? "A map" : "An array")} of unknown length holds at most {MaxUnknownCount} {(frame.Kind == FrameKind.Map ? "entries" : "elements")}.", _base + _pos);
        _frames[_depth - 1].Count++;
      }
    }

    private void Integer(ulong value, bool signed, int size)
    {
      _token = MsgPackTokenType.Integer;
      _integer = value;
      _signed = signed;
      _pos += size;
    }

    private void Float(double value, int size)
    {
      _token = MsgPackTokenType.Float;
      _float = value;
      _pos += size;
    }

    private void Payload(MsgPackTokenType token, int header, long length)
    {
      _token = token;
      _pos += header;
      _length = length;
      _payloadPending = length > 0;
    }

    private void Extension(int header, long length)
    {
      Need(header);
      _extensionType = unchecked((sbyte)_buf[_pos + header - 1]);
      Payload(MsgPackTokenType.Extension, header, length);
    }

    private void Container(FrameKind kind, int header, long count)
    {
      _token = kind == FrameKind.Map ? MsgPackTokenType.MapStart : MsgPackTokenType.ArrayStart;
      _length = count;
      _pos += header;
      Push(kind, false, kind == FrameKind.Map ? 2 * count : count);
    }

    private void Push(FrameKind kind, bool unknown, long count)
    {
      if (_depth >= _maxDepth)
        throw new MsgPackException($"The data is nested deeper than {nameof(LtMsgPackOptions)}.{nameof(LtMsgPackOptions.MaxDepth)} ({_maxDepth}).", TokenStart);
      if (_depth == _frames.Length)
        Array.Resize(ref _frames, _frames.Length * 2);
      int countIndex = -1;
      if (unknown && _mark >= 0)
      {
        countIndex = _counts.Count;
        _counts.Add(0);
      }
      _frames[_depth++] = new Frame { Kind = kind, Unknown = unknown, Count = count, CountIndex = countIndex };
    }

    /// <summary>
    /// Skips the current value: an array, map, str or bin to its end (without buffering it), anything else is passed by the next <see cref="Read"/> anyway.
    /// </summary>
    public void Skip()
    {
      ThrowIfDisposed();
      if (!IsStart(_token))
        return;
      int depth = _depth;
      while (_depth >= depth)
        Read();
    }

    private static bool IsStart(MsgPackTokenType token)
    {
      return token == MsgPackTokenType.ArrayStart || token == MsgPackTokenType.MapStart || token == MsgPackTokenType.StringStart || token == MsgPackTokenType.BinaryStart;
    }

    #endregion

    #region Values of the current token

    public bool GetBoolean()
    {
      Expect(MsgPackTokenType.Boolean);
      return _bool;
    }

    /// <exception cref="OverflowException">An unsigned value above long.MaxValue</exception>
    public long GetInt64()
    {
      Expect(MsgPackTokenType.Integer);
      if (!_signed && _integer > long.MaxValue)
        throw new OverflowException($"The value {_integer} does not fit in a long.");
      return unchecked((long)_integer);
    }

    /// <exception cref="OverflowException">A negative value</exception>
    public ulong GetUInt64()
    {
      Expect(MsgPackTokenType.Integer);
      if (_signed && unchecked((long)_integer) < 0)
        throw new OverflowException($"The value {unchecked((long)_integer)} is negative.");
      return _integer;
    }

    /// <summary>
    /// A float or an integer.
    /// </summary>
    public double GetDouble()
    {
      if (_token == MsgPackTokenType.Integer)
        return _signed ? unchecked((long)_integer) : (double)_integer;
      Expect(MsgPackTokenType.Float);
      return _float;
    }

    /// <summary>
    /// A str, a str of unknown length (all its chunks: the reader is then at its <see cref="MsgPackTokenType.StringEnd"/>) or nil (null).
    /// </summary>
    public string GetString()
    {
      ThrowIfDisposed();
      if (_token == MsgPackTokenType.Nil)
        return null;
      if (_token == MsgPackTokenType.StringStart)
        return (string)ReadWhole();
      Expect(MsgPackTokenType.String);
      BufferPayload();
      return _length == 0 ? string.Empty : MsgPackOptions.StringEncoding.GetString(_buf, _pos, (int)_length);
    }

    /// <summary>
    /// A bin, a bin of unknown length (all its chunks: the reader is then at its <see cref="MsgPackTokenType.BinaryEnd"/>) or nil (null).
    /// </summary>
    public byte[] GetBytes()
    {
      ThrowIfDisposed();
      if (_token == MsgPackTokenType.Nil)
        return null;
      if (_token == MsgPackTokenType.BinaryStart)
        return (byte[])ReadWhole();
      Expect(MsgPackTokenType.Binary);
      return CopyPayload();
    }

    /// <summary>
    /// The data of an extension value.
    /// </summary>
    public byte[] GetExtension(out sbyte typeCode)
    {
      Expect(MsgPackTokenType.Extension);
      typeCode = _extensionType;
      return CopyPayload();
    }

    /// <summary>
    /// A timestamp (extension type -1), as the serializer reads it (see <see cref="MsgPackOptions.ReadDateTimeKind"/>).
    /// </summary>
    public DateTime GetDateTime()
    {
      Expect(MsgPackTokenType.Extension);
      BufferPayload();
      _r.Reset(_buf, _tokenStart, _pos + (int)_length);
      if (_extensionType != -1 || !_r.TryReadDateTime(out DateTime value))
        throw new InvalidOperationException($"The extension of type {_extensionType} is not a timestamp.");
      return value;
    }

    /// <summary>
    /// The current value as the serializer reads it into object: null, bool, the integer type of the format, float or double, string, byte[], DateTime (timestamps), the value of a registered extension,
    /// MsgPackExtension, object[] (arrays) or KeyValuePair&lt;object, object&gt;[] (maps). At the start of an array, map, str or bin it reads all of it: the reader is then at its end.
    /// </summary>
    public object GetValue()
    {
      ThrowIfDisposed();
      switch (_token)
      {
        case MsgPackTokenType.None:
        case MsgPackTokenType.ArrayEnd:
        case MsgPackTokenType.MapEnd:
        case MsgPackTokenType.StringEnd:
        case MsgPackTokenType.BinaryEnd:
          throw new InvalidOperationException($"The current token ({_token}) is not a value.");
      }
      if (IsStart(_token))
        return ReadWhole();
      BufferPayload();
      _r.Reset(_buf, _tokenStart, _pos + (int)_length);
      return _r.ReadPlain(0);
    }

    private void Expect(MsgPackTokenType token)
    {
      ThrowIfDisposed();
      if (_token != token)
        throw new InvalidOperationException($"The current token is {_token}, not {token}.");
    }

    /// <summary>
    /// The array, map, str or bin that starts at the current token, read whole (as <see cref="GetValue"/>): the reader is then at its end.
    /// </summary>
    private object ReadWhole()
    {
      int depth = _depth;
      _mark = _tokenStart;
      _escaped = _frames[_depth - 1].Unknown;
      _counts.Clear();
      if (_frames[_depth - 1].Unknown) // started before the mark
      {
        _frames[_depth - 1].CountIndex = 0;
        _counts.Add(0);
      }

      try
      {
        while (_depth >= depth)
          Read();
        Plain(_mark, _pos, out byte[] data, out int offset, out int end);
        _r.Reset(data, offset, end);
        return _r.ReadPlain(0);
      }
      finally
      {
        _mark = -1;
      }
    }

    #endregion

    #region Objects

    /// <summary>
    /// Reads the next value as <see cref="LtMsgPackSerializer.Deserialize{T}(byte[])"/> does, with its schema (inline or a reference, see <see cref="MsgPackStreamSchemas"/>).
    /// <para>With the indexed schema an object is two values (its schema or a reference to it, then the object), so it is only read as a root value of the stream (<see cref="Depth"/> 0),
    /// unless it is part of the body of a head (<see cref="ReadHead{TRoot}"/>): then only the value is read, with the schema of the head, at any depth. Without the indexed schema, anywhere (e.g. the elements of an array).
    /// Arrays, maps, str and bin of unknown length in it are read too.</para>
    /// </summary>
    /// <typeparam name="T">The type to read the value as. In the body of a head, the type of the place in the root type (e.g. the element type of the list).</typeparam>
    /// <exception cref="MsgPackException">The stream ends (see <see cref="AtEnd"/>), or the data is not valid</exception>
    public T ReadObject<T>()
    {
      object result = ReadObject(typeof(T));
      return result is null ? default(T) : (T)result;
    }

    /// <summary>
    /// <see cref="ReadObject{T}"/> for a type known at runtime.
    /// </summary>
    public object ReadObject(Type type)
    {
      if (type is null)
        throw new ArgumentNullException(nameof(type));
      ThrowIfDisposed();
      PassPayload();
      bool withSchema = _options._useInexedSchema;
      bool body = _depth == 0 ? _headState == HeadState.AwaitingBody : _headState == HeadState.InBody;
      Serializer.SchemaHead head = body ? _head : null;
      Type root = _headRoot;
      if (_depth > 0)
      {
        Frame frame = _frames[_depth - 1];
        if (frame.Kind == FrameKind.String || frame.Kind == FrameKind.Binary)
          throw new InvalidOperationException($"A str or bin of unknown length holds only its chunks, not objects.");
        if (withSchema && !body)
          throw new InvalidOperationException($"With the indexed schema an object is two values (its schema or a reference to it, then the object): {nameof(ReadObject)} reads it as a root value of the stream ({nameof(Depth)} 0). Objects in an array or map are read with the schema before it ({nameof(ReadHead)}), or with a serializer without the indexed schema.");
        if (AtEnd)
          throw new InvalidOperationException($"The {(frame.Kind == FrameKind.Map ? "map" : "array")} has no more values.");
      }
      else
        SkipStreamValues();
      if (!TryEnsure(1))
        throw EndOfData();

      int depth = _depth;
      _mark = _pos;
      _escaped = false;
      _counts.Clear();
      try
      {
        bool schema = withSchema && head is null && _buf[_pos] != 0xC0; // null is written without a schema
        PassValue(depth);
        PassPayload();
        int schemaEnd = _pos;
        bool escapedSchema = _escaped;
        if (schema)
        {
          if (escapedSchema)
            throw new MsgPackException("A schema cannot have values of unknown length.", _base + _mark);
          PassValue(depth);
        }
        PassPayload();

        if (schema && SchemaBytes.IsMap(_buf[_mark]))
          Register(_mark, schemaEnd);
        Plain(_mark, _pos, out byte[] data, out int offset, out int end);
        _token = MsgPackTokenType.None;
        _length = 0;
        if (head != null)
          return _serializer.DeserializeBody(head, root, type, data, offset, end, out int used);
        return _serializer.Deserialize(type, data, offset, end, out int consumed);
      }
      finally
      {
        _mark = -1;
      }
    }

    /// <summary>
    /// Reads the schema that <see cref="MsgPackStreamWriter.WriteHead{TRoot}"/> wrote (inline or a reference) for the root value that follows it (its body):
    /// the objects in the body are then read without their schema (<see cref="ReadObject{T}"/>, at any depth), as <see cref="LtMsgPackSerializer.Deserialize{T}(byte[])"/> reads them in a value of <typeparamref name="TRoot"/>.
    /// E.g. <c>ReadHead&lt;List&lt;Row&gt;&gt;()</c>, <see cref="Read"/> (the ArrayStart), a <see cref="ReadObject{T}"/> per row until <see cref="AtEnd"/>, <see cref="Read"/> (the ArrayEnd).
    /// <para>Any payload of <see cref="LtMsgPackSerializer.Serialize{T}(T)"/> can be read so too. Without the indexed schema nothing is read, the body is read as without a head.</para>
    /// </summary>
    /// <typeparam name="TRoot">The type of the root value that follows (pairs the classes of the schema with the reader's, as <see cref="LtMsgPackSerializer.Deserialize{T}(byte[])"/>)</typeparam>
    /// <exception cref="InvalidOperationException">Not at <see cref="Depth"/> 0, or the head read before still waits for its body</exception>
    /// <exception cref="MsgPackException">The next value is not a schema (or a reference to one), or the stream ends</exception>
    public void ReadHead<TRoot>()
    {
      ReadHead(typeof(TRoot));
    }

    /// <summary>
    /// <see cref="ReadHead{TRoot}"/> for a type known at runtime.
    /// </summary>
    public void ReadHead(Type root)
    {
      if (root is null)
        throw new ArgumentNullException(nameof(root));
      ThrowIfDisposed();
      PassPayload();
      if (_depth > 0)
        throw new InvalidOperationException($"A head is read before a root value of the stream ({nameof(Depth)} 0).");
      if (_headState == HeadState.AwaitingBody)
        throw new InvalidOperationException("The head read before still waits for its body (the next root value).");

      _head = null;
      _headRoot = root;
      if (_options._useInexedSchema)
      {
        SkipStreamValues();
        if (!TryEnsure(1))
          throw EndOfData();
        _mark = _pos;
        _escaped = false;
        _counts.Clear();
        try
        {
          PassValue(0);
          PassPayload();
          if (_escaped)
            throw new MsgPackException("A schema cannot have values of unknown length.", _base + _mark);
          if (SchemaBytes.IsMap(_buf[_mark]))
            Register(_mark, _pos);
          _head = _serializer.ReadHead(_buf, _mark, _pos);
          _token = MsgPackTokenType.None;
          _length = 0;
        }
        finally
        {
          _mark = -1;
        }
      }
      _headState = HeadState.AwaitingBody;
    }

    /// <summary>
    /// Reads past the next value (its data stays in the buffer from the mark).
    /// </summary>
    private void PassValue(int depth)
    {
      if (!Read())
        throw EndOfData();
      switch (_token)
      {
        case MsgPackTokenType.ArrayEnd:
        case MsgPackTokenType.MapEnd:
          throw new InvalidOperationException($"The {(_token == MsgPackTokenType.MapEnd ? "map" : "array")} has no more values.");
      }
      while (_depth > depth)
        Read();
    }

    /// <summary>
    /// The schema at the start of an object, registered in the store of the serializer, so the objects that refer to it later can be read (<see cref="MsgPackStreamSchemas.PerStream"/>).
    /// </summary>
    private void Register(int start, int end)
    {
      Register(_buf, start, end - start);
    }

    private void Register(byte[] data, int start, int count)
    {
      SchemaStore store = _options._schemaStore;
      if (store is null)
        return;
      for (int t = _registered.Count - 1; t >= 0; t--)
      {
        byte[] known = _registered[t];
        if (new ReadOnlySpan<byte>(data, start, count).SequenceEqual(new ReadOnlySpan<byte>(known)))
          return;
      }

      try
      {
        store.RegisterInline(data, start, count, _options);
      }
      catch (MsgPackException) // the store is full: the object can be read (its schema is inline), only later references to it cannot
      {
        return;
      }
      byte[] copy = new byte[count];
      Buffer.BlockCopy(data, start, copy, 0, count);
      _registered.Add(copy);
    }

    #endregion

    #region Index

    /// <summary>
    /// The indexes of the stream (<see cref="MsgPackStreamIndex"/>), the first one first: each lists the entries since the one before it (checkpoints, appended parts), so together they list all entries.
    /// The offsets in them are positions in the stream (moved along when the stream was moved since it was written). Empty when the stream has no index.
    /// The schemas whose inline copy the indexes locate are registered in the serializer's <see cref="SchemaStore"/>, so entries that refer to them can be read after <see cref="Seek(MsgPackStreamEntry)"/>.
    /// The reader stays where it was. Needs a stream that seeks.
    /// </summary>
    /// <param name="searchBackwards">When the stream does not end with a tail (a writer that stopped after a checkpoint, a crash): search backwards for the last one.
    /// This reads a stream without an index completely, from the end.</param>
    /// <exception cref="MsgPackException">An index matches its CRC-32 but is not valid (offsets outside of the stream, points out of order, a newer version...)</exception>
    public IReadOnlyList<MsgPackStreamIndex> ReadIndexes(bool searchBackwards = false)
    {
      ThrowIfDisposed();
      ThrowIfNotSeekable();
      long resume = _source.Position; // the buffer holds the data up to here
      try
      {
        List<MsgPackStreamIndex> indexes = IndexFormat.ReadAll(_source, searchBackwards, out long lastTail, out bool endsWithTail, out long delta);
        if (_options._schemaStore != null)
        {
          for (int i = 0; i < indexes.Count; i++)
          {
            List<MsgPackStreamSchemaLocation> schemas = indexes[i].Schemas;
            for (int t = 0; t < schemas.Count; t++)
            {
              if (!schemas[t].Offset.HasValue)
                continue; // External: the store has it, or not
              byte[] schema = new byte[schemas[t].Length];
              _source.Position = schemas[t].Offset.Value;
              if (!ReadExactly(schema))
                throw new MsgPackException($"The schema at {schemas[t].Offset.Value} that the index locates is cut off.", schemas[t].Offset.Value);
              Register(schema, 0, schema.Length);
            }
          }
        }
        return indexes;
      }
      finally
      {
        _source.Position = resume;
      }
    }

    /// <summary>
    /// Goes to an entry of the index (<see cref="ReadIndexes"/>): the next token is the start of its root value (or its head: then read it with <see cref="ReadHead{TRoot}"/>). Needs a stream that seeks.
    /// </summary>
    public void Seek(MsgPackStreamEntry entry)
    {
      if (entry is null)
        throw new ArgumentNullException(nameof(entry));
      ThrowIfDisposed();
      ThrowIfNotSeekable();
      MoveTo(entry.Offset);
      _depth = 0;
      _headState = HeadState.None;
      _head = null;
    }

    /// <summary>
    /// Goes to a point of an entry's sparse index (<see cref="MsgPackStreamEntry.Points"/>): the next value is the element it points to.
    /// The reader must be in the array of that entry (<see cref="Depth"/> 1: after its ArrayStart, and its head when it has one), so it knows the array and the schema of its objects.
    /// </summary>
    /// <exception cref="InvalidOperationException">The reader is not in a root array, or the entry is no array</exception>
    public void Seek(MsgPackStreamEntry entry, MsgPackStreamIndexPoint point)
    {
      if (entry is null)
        throw new ArgumentNullException(nameof(entry));
      ThrowIfDisposed();
      ThrowIfNotSeekable();
      if (_depth != 1 || _frames[0].Kind != FrameKind.Array)
        throw new InvalidOperationException($"A point is in the array of an entry: read the entry's ArrayStart first (after {nameof(Seek)}(entry), and its head when it has one).");
      if (!entry.ElementCount.HasValue)
        throw new InvalidOperationException("The entry is no array, it has no points.");
      if (point.Element < 0 || point.Element >= entry.ElementCount.Value || point.Offset < 0 || point.Offset >= entry.Length)
        throw new ArgumentOutOfRangeException(nameof(point), "The point is not in the entry.");

      MoveTo(entry.Offset + point.Offset);
      if (_frames[0].Unknown)
        _frames[0].Count = point.Element; // the elements read
      else
        _frames[0].Count = entry.ElementCount.Value - point.Element; // the elements still to come
      _frames[0].CountIndex = -1;
    }

    /// <summary>
    /// Goes to an element of the array of an entry: to the last point before it (<see cref="Seek(MsgPackStreamEntry, MsgPackStreamIndexPoint)"/>), or from where the reader is when that is closer, then passes the elements up to it.
    /// The next value is the element. The reader must be in the array of that entry, as for <see cref="Seek(MsgPackStreamEntry, MsgPackStreamIndexPoint)"/>.
    /// </summary>
    /// <param name="element">The number of the element (0 for the first)</param>
    public void SeekElement(MsgPackStreamEntry entry, long element)
    {
      if (entry is null)
        throw new ArgumentNullException(nameof(entry));
      ThrowIfDisposed();
      if (_depth != 1 || _frames[0].Kind != FrameKind.Array)
        throw new InvalidOperationException($"An element is in the array of an entry: read the entry's ArrayStart first (after {nameof(Seek)}(entry), and its head when it has one).");
      if (!entry.ElementCount.HasValue)
        throw new InvalidOperationException("The entry is no array.");
      if (element < 0 || element >= entry.ElementCount.Value)
        throw new ArgumentOutOfRangeException(nameof(element));
      PassPayload();

      long current = _frames[0].Unknown ? _frames[0].Count : entry.ElementCount.Value - _frames[0].Count; // the next element
      List<MsgPackStreamIndexPoint> points = entry.Points;
      int best = -1;
      if (points != null)
      {
        int low = 0, high = points.Count - 1;
        while (low <= high) // the last point at or before the element
        {
          int middle = (low + high) >> 1;
          if (points[middle].Element <= element) { best = middle; low = middle + 1; }
          else high = middle - 1;
        }
      }

      if (best >= 0 && (current > element || points[best].Element > current))
      {
        Seek(entry, points[best]);
        current = points[best].Element;
      }
      else if (current > element)
        throw new InvalidOperationException($"The element {element} is before the reader (at {current}) and before the first point of the entry: {nameof(Seek)} to the entry again to read it.");

      for (; current < element; current++)
      {
        if (!Read())
          throw EndOfData();
        Skip();
      }
      PassPayload(); // the data of the last element passed
      _token = MsgPackTokenType.None;
      _length = 0;
    }

    /// <summary>
    /// Continues at <paramref name="position"/> in the stream: drops the buffer and the current token, keeps the open arrays and maps.
    /// </summary>
    private void MoveTo(long position)
    {
      _source.Position = position;
      _base = position - _origin;
      _pos = 0;
      _end = 0;
      _tokenStart = 0;
      _eof = false;
      _payloadPending = false;
      _mark = -1;
      _token = MsgPackTokenType.None;
      _length = 0;
      _isKey = false;
    }

    private void ThrowIfNotSeekable()
    {
      if (!_source.CanSeek)
        throw new NotSupportedException("The index is read from a stream that seeks (a file, a memory stream; over HTTP: download the entries with range requests).");
    }

    private bool ReadExactly(byte[] buffer)
    {
      int done = 0;
      while (done < buffer.Length)
      {
        int read = _source.Read(buffer, done, buffer.Length - done);
        if (read <= 0)
          return false;
        done += read;
      }
      return true;
    }

    /// <summary>
    /// Passes the values of the index between root values: indexes (extension type 3) with their tails, and sidecars (type 4). Only at <see cref="Depth"/> 0 and without a mark.
    /// </summary>
    private void SkipStreamValues()
    {
      while (TryEnsure(1))
      {
        int header = IndexFormat.ExtensionHeaderLength(_buf[_pos]);
        if (header == 0 || !TryEnsure(header))
          return;
        sbyte type = unchecked((sbyte)_buf[_pos + header - 1]);
        if (type != MsgPackStreamIndex.IndexExtensionType && type != MsgPackStreamIndex.SidecarExtensionType)
          return;

        _tokenStart = _pos;
        _length = ExtensionLength(_pos, header);
        _pos += header;
        _payloadPending = _length > 0;
        PassPayload();
        _length = 0;
        if (type == MsgPackStreamIndex.IndexExtensionType)
        {
          if (!TryEnsure(MsgPackStreamIndex.TailLength) || !IndexFormat.TryParseTail(_buf, _pos, out IndexFormat.Tail unused))
            throw new MsgPackException("An index (extension type 3) of the stream is not followed by its tail.", _base + _pos);
          _pos += MsgPackStreamIndex.TailLength;
        }
        _tokenStart = _pos;
      }
    }

    /// <summary>
    /// <see cref="SkipStreamValues"/> without moving: the offset (from <see cref="_pos"/>) after the values of the index at <paramref name="at"/>, buffering them.
    /// </summary>
    private int PastStreamValues(int at)
    {
      while (TryEnsure(at + 1))
      {
        int header = IndexFormat.ExtensionHeaderLength(_buf[_pos + at]);
        if (header == 0 || !TryEnsure(at + header))
          break;
        sbyte type = unchecked((sbyte)_buf[_pos + at + header - 1]);
        if (type != MsgPackStreamIndex.IndexExtensionType && type != MsgPackStreamIndex.SidecarExtensionType)
          break;
        long size = header + ExtensionLength(_pos + at, header) + (type == MsgPackStreamIndex.IndexExtensionType ? MsgPackStreamIndex.TailLength : 0);
        if (size > MaxBufferLength - 2L - at)
          break;
        at += (int)size;
      }
      return at;
    }

    /// <summary>
    /// The length of the data of the extension value whose header (of <paramref name="header"/> bytes) is buffered at <paramref name="at"/>, in the stream's byte order.
    /// </summary>
    private long ExtensionLength(int at, int header)
    {
      switch (_buf[at])
      {
        case 0xD4: return 1;
        case 0xD5: return 2;
        case 0xD6: return 4;
        case 0xD7: return 8;
        case 0xD8: return 16;
        case 0xC7: return _buf[at + 1];
        case 0xC8: return U16(at + 1);
        default: return U32(at + 1);
      }
    }

    #endregion

    #region Values of unknown length

    /// <summary>
    /// The value read since the mark, with arrays, maps, str and bin of unknown length replaced by ones of known length (the counts were kept while reading), so the serializer can read it.
    /// </summary>
    private void Plain(int start, int end, out byte[] data, out int offset, out int stop)
    {
      if (!_escaped)
      {
        data = _buf;
        offset = start;
        stop = end;
        return;
      }

      if (_normalized is null)
        _normalized = new MsgPackWriter(Math.Max(256, end - start));
      MsgPackWriter w = _normalized;
      w.Reset(_options);
      int next = 0; // the next count, in the order of the starts
      bool chunks = false;
      int at = start;
      while (at < end)
      {
        byte b = _buf[at];
        if (b == Escape.Byte)
        {
          byte code = _buf[at + 1];
          at += 2;
          switch (code)
          {
            case Escape.ArrayStart: w.ArrayHeader(checked((int)_counts[next++])); break;
            case Escape.MapStart: w.MapHeader(checked((int)_counts[next++])); break;
            case Escape.StringStart: w.StrHeader(checked((int)_counts[next++])); chunks = true; break;
            case Escape.BinaryStart: w.BinHeader(checked((int)_counts[next++])); chunks = true; break;
            default: chunks = false; break; // the end: nothing for arrays and maps (their count is in the header), the chunks of a str or bin end
          }
          continue;
        }

        long length = Measure(at, out int header);
        int size = checked(header + (int)length);
        if (chunks)
          w.Raw(_buf, at + header, (int)length); // the data of the chunk, behind the one header
        else
          w.Raw(_buf, at, size);
        at += size;
      }

      data = w.Buf;
      offset = 0;
      stop = w.Pos;
    }

    /// <summary>
    /// The size of the header of the value at <paramref name="at"/> (a buffered value without escape byte) and the length of the data after it (str, bin, extension, the bytes of a number; 0 for arrays and maps: their elements follow).
    /// </summary>
    private long Measure(int at, out int header)
    {
      byte b = _buf[at];
      header = 1;
      if (b <= 0x7F || b >= 0xE0 || (b & 0xF0) == 0x90 || (b & 0xF0) == 0x80) return 0;
      if ((b & 0xE0) == 0xA0) return b & 0x1F;
      switch (b)
      {
        case 0xC0: case 0xC2: case 0xC3: return 0;
        case 0xCC: case 0xD0: return 1;
        case 0xCD: case 0xD1: return 2;
        case 0xCE: case 0xD2: case 0xCA: return 4;
        case 0xCF: case 0xD3: case 0xCB: return 8;
        case 0xD9: case 0xC4: header = 2; return _buf[at + 1];
        case 0xDA: case 0xC5: header = 3; return U16(at + 1);
        case 0xDB: case 0xC6: header = 5; return U32(at + 1);
        case 0xDC: case 0xDE: header = 3; return 0;
        case 0xDD: case 0xDF: header = 5; return 0;
        case 0xD4: header = 2; return 1;
        case 0xD5: header = 2; return 2;
        case 0xD6: header = 2; return 4;
        case 0xD7: header = 2; return 8;
        case 0xD8: header = 2; return 16;
        case 0xC7: header = 3; return _buf[at + 1];
        case 0xC8: header = 4; return U16(at + 1);
        case 0xC9: header = 6; return U32(at + 1);
      }
      throw Escape.NotSupported(_base + at);
    }

    #endregion

    #region Buffer

    /// <summary>
    /// Passes the data of the current String, Binary or Extension token: from the buffer, or (when it is not buffered and no value is read whole) read from the stream and dropped.
    /// </summary>
    private void PassPayload()
    {
      if (!_payloadPending)
        return;
      _payloadPending = false;
      long length = _length;
      if (_end - _pos >= length)
      {
        _pos += (int)length;
        return;
      }

      if (_mark >= 0) // read whole: buffered
      {
        _payloadPending = true;
        BufferPayload();
        _payloadPending = false;
        _pos += (int)length;
        return;
      }

      length -= _end - _pos;
      _base += _end;
      _pos = 0;
      _end = 0;
      _tokenStart = 0;
      while (length > 0)
      {
        int read = _source.Read(_buf, 0, (int)Math.Min(_buf.Length, length));
        if (read <= 0)
        {
          _eof = true;
          throw EndOfData();
        }
        _base += read;
        length -= read;
      }
    }

    /// <summary>
    /// The data of the current String, Binary or Extension token is in the buffer (from <see cref="_pos"/>).
    /// </summary>
    private void BufferPayload()
    {
      if (!_payloadPending)
        return;
      if (_length > MaxBufferLength - (_pos - _tokenStart))
        throw new MsgPackException($"The data claims a value of {_length} bytes, longer than a .NET array can hold.", TokenStart);
      if (!TryEnsure((int)_length))
        throw EndOfData();
    }

    private byte[] CopyPayload()
    {
      BufferPayload();
      byte[] copy = new byte[_length];
      Buffer.BlockCopy(_buf, _pos, copy, 0, (int)_length);
      return copy;
    }

    private void Need(int count)
    {
      if (!TryEnsure(count))
        throw EndOfData();
    }

    /// <summary>
    /// Reads until the buffer holds <paramref name="count"/> bytes from <see cref="_pos"/>, false when the stream ends before.
    /// </summary>
    private bool TryEnsure(int count)
    {
      while (_end - _pos < count)
      {
        if (_eof)
          return false;
        if (_end == _buf.Length)
          MakeRoom();
        int read = _source.Read(_buf, _end, _buf.Length - _end);
        if (read <= 0)
        {
          _eof = true;
          return false;
        }
        _end += read;
      }
      return true;
    }

    /// <summary>
    /// Drops what is before the current token (or the mark), otherwise doubles the buffer: it only grows as far as data arrives.
    /// </summary>
    private void MakeRoom()
    {
      int keep = _mark >= 0 && _mark < _tokenStart ? _mark : _tokenStart;
      if (keep > 0)
      {
        Buffer.BlockCopy(_buf, keep, _buf, 0, _end - keep);
        _base += keep;
        _pos -= keep;
        _end -= keep;
        _tokenStart -= keep;
        if (_mark >= 0)
          _mark -= keep;
        return;
      }

      if (_buf.Length >= MaxBufferLength)
        throw new MsgPackException("A value is longer than a .NET array can hold.", TokenStart);
      Array.Resize(ref _buf, (int)Math.Min((long)_buf.Length * 2, MaxBufferLength));
    }

    private MsgPackException EndOfData()
    {
      return new MsgPackException("Unexpected end of data.", _base + _end, MsgPackTypeId.NeverUsed);
    }

    private ushort U16(int at)
    {
      return _littleEndian ? (ushort)(_buf[at] | _buf[at + 1] << 8) : (ushort)(_buf[at] << 8 | _buf[at + 1]);
    }

    private uint U32(int at)
    {
      return _littleEndian
        ? (uint)(_buf[at] | _buf[at + 1] << 8 | _buf[at + 2] << 16 | _buf[at + 3] << 24)
        : (uint)(_buf[at] << 24 | _buf[at + 1] << 16 | _buf[at + 2] << 8 | _buf[at + 3]);
    }

    private ulong U64(int at)
    {
      return _littleEndian ? U32(at) | (ulong)U32(at + 4) << 32 : (ulong)U32(at) << 32 | U32(at + 4);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct SingleBits
    {
      [System.Runtime.InteropServices.FieldOffset(0)] private readonly uint _bits;
      [System.Runtime.InteropServices.FieldOffset(0)] public readonly float Value;

      public SingleBits(uint bits)
      {
        Value = 0;
        _bits = bits;
      }
    }

    private void ThrowIfDisposed()
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(MsgPackStreamReader));
    }

    /// <summary>
    /// Closes the stream, unless the reader was created with leaveOpen.
    /// </summary>
    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
      _buf = null;
      if (!_leaveOpen)
        _source.Dispose();
    }

    #endregion
  }
}
