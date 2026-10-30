using LsMsgPack;
using LsMsgPack.Meta;
using LtMsgPack.IO;
using LtMsgPack.Writing;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace LtMsgPack.Streaming
{
  /// <summary>
  /// Writes MsgPack values one after another to a stream: values, arrays and maps of a known length, arrays, maps, str and bin of unknown length (written before their length is known, see docs/Streaming.md),
  /// and whole objects with their schema (<see cref="WriteObject{T}(T)"/>). The values between which <see cref="Depth"/> is 0 are the root values of the stream, e.g. one per result set or table.
  /// <para>Values are written into a buffer that goes to the stream once it holds <see cref="FlushThreshold"/> bytes, at <see cref="Flush"/> and at <see cref="Dispose"/>.
  /// <see cref="Position"/> is where the next value starts, e.g. to keep an index of the root values.</para>
  /// <para>Arrays, maps, str and bin of unknown length use the escape byte 0xC1 as proposed in msgpack/msgpack#347 (not in the MsgPack specification yet):
  /// only <see cref="MsgPackStreamReader"/> reads them, other readers (also the serializers of this library) refuse the data. Without them the output is plain MsgPack.</para>
  /// <para>Not thread-safe.</para>
  /// </summary>
  public sealed class MsgPackStreamWriter : IDisposable, IPayloadSink
  {
    private const int InitialCapacity = 16 * 1024;

    private readonly Stream _target;
    private readonly bool _leaveOpen;
    private readonly Serializer _serializer;
    private readonly LtMsgPackOptions _options;
    private readonly MsgPackStreamSchemas _schemas;
    private readonly MsgPackWriter _w;
    private readonly WriteContext _c; // for the handlers of Guid, decimal and DateTime: the formats of the options
    private readonly ValueHandler<Guid> _guid;
    private readonly ValueHandler<decimal> _decimal;
    private readonly ValueHandler<DateTime> _dateTime;
    private readonly FullPropertyInfo _guidInfo = new FullPropertyInfo(typeof(Guid)); // as the serializer passes the declared type of a root value
    private readonly FullPropertyInfo _decimalInfo = new FullPropertyInfo(typeof(decimal));
    private readonly FullPropertyInfo _dateTimeInfo = new FullPropertyInfo(typeof(DateTime));
    private readonly List<SchemaId> _referenced = new List<SchemaId>();
    private readonly HashSet<SchemaId> _known = new HashSet<SchemaId>();
    private int _flushThreshold = 64 * 1024;
    private long _flushed;
    private Frame[] _frames = new Frame[8];
    private int _depth;
    private char _pendingHigh; // a high surrogate at the end of a chunk, written with the next one (a chunk holds whole UTF-8 sequences)
    private char[] _scratch;
    private bool _disposed;

    /// <summary>
    /// A writer with the default options of <see cref="LtMsgPackSerializer.Default"/>, objects with their schema (<see cref="MsgPackStreamSchemas.PerObject"/>). The stream is closed by <see cref="Dispose"/>.
    /// </summary>
    [RequiresUnreferencedCode(LtMsgPackSerializer.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LtMsgPackSerializer.RequiresDynamicCodeMessage)]
    public MsgPackStreamWriter(Stream target) : this(target, LtMsgPackSerializer.Default) { }

    /// <param name="serializer">Writes the objects, and its options decide the formats of the values (integers, byte order, Guids, decimals, DateTimes)</param>
    /// <param name="schemas">Where the schemas of the objects go</param>
    /// <param name="leaveOpen">Do not close the stream in <see cref="Dispose"/></param>
    /// <exception cref="ArgumentException"><see cref="MsgPackStreamSchemas.PerStream"/> or <see cref="MsgPackStreamSchemas.External"/> without a serializer that writes schema references</exception>
    public MsgPackStreamWriter(Stream target, LtMsgPackSerializer serializer, MsgPackStreamSchemas schemas = MsgPackStreamSchemas.PerObject, bool leaveOpen = false)
    {
      if (target is null)
        throw new ArgumentNullException(nameof(target));
      if (serializer is null)
        throw new ArgumentNullException(nameof(serializer));
      if (!target.CanWrite)
        throw new ArgumentException("The stream cannot be written.", nameof(target));

      _serializer = serializer.Implementation;
      _options = _serializer.Options;
      switch (schemas)
      {
        case MsgPackStreamSchemas.PerObject:
          break;
        case MsgPackStreamSchemas.PerStream:
        case MsgPackStreamSchemas.External:
          if (!_options._useInexedSchema || !_options._writeSchemaReference || _options._schemaStore is null)
            throw new ArgumentException($"{nameof(MsgPackStreamSchemas)}.{schemas} needs a serializer that refers to the schemas: set {nameof(MsgPackOptions.UseInexedSchema)}, {nameof(MsgPackOptions.WriteSchemaReference)} and a {nameof(MsgPackOptions.SchemaStore)} in its options.", nameof(schemas));
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(schemas));
      }

      _target = target;
      _leaveOpen = leaveOpen;
      _schemas = schemas;
      _w = new MsgPackWriter(InitialCapacity);
      _w.Reset(_options);
      _c = new WriteContext(_serializer) { W = _w, Mode = IdMode.Names, IdSettings = _options };
      _guid = _serializer.Handler<Guid>(null);
      _decimal = _serializer.Handler<decimal>(null);
      _dateTime = _serializer.Handler<DateTime>(null);
    }

    #region State

    /// <summary>
    /// The number of bytes written (to the stream and in the buffer) since the writer was created: where the next value starts, relative to where the writer started.
    /// <para>Read it after a root value (<see cref="Depth"/> 0) for an index of the values of the stream.</para>
    /// </summary>
    public long Position { get { return _flushed + _w.Pos; } }

    /// <summary>
    /// The number of open arrays, maps, str and bin (of known or unknown length): 0 between the root values of the stream.
    /// </summary>
    public int Depth { get { return _depth; } }

    public MsgPackStreamSchemas Schemas { get { return _schemas; } }

    /// <summary>
    /// The ids of the schemas the objects of the stream refer to (with a serializer that writes schema references), in the order of their first use, e.g. to keep the schemas of <see cref="MsgPackStreamSchemas.External"/>
    /// (<see cref="SchemaStore.GetSchema"/>).
    /// </summary>
    public IReadOnlyList<SchemaId> ReferencedSchemas { get { return _referenced; } }

    /// <summary>
    /// The buffer goes to the stream when a value makes it this long (64 KiB by default). 0: after every value. Values stay whole in the buffer while they are written (a long string, an object).
    /// </summary>
    public int FlushThreshold
    {
      get { return _flushThreshold; }
      set
      {
        if (value < 0)
          throw new ArgumentOutOfRangeException(nameof(value));
        _flushThreshold = value;
      }
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
      /// Unknown length: the elements written (keys and values for a map), the bytes for a str or bin.
      /// </summary>
      internal long Count;
    }

    private const long MaxUnknownCount = uint.MaxValue; // the limits of the specification also apply to values of unknown length

    #endregion

    #region Values

    public void WriteNil()
    {
      Value();
      _w.Nil();
      Wrote();
    }

    public void Write(bool value)
    {
      Value();
      _w.Bool(value);
      Wrote();
    }

    public void Write(sbyte value)
    {
      Value();
      _w.SByte(value);
      Wrote();
    }

    public void Write(byte value)
    {
      Value();
      _w.UInt8(value);
      Wrote();
    }

    public void Write(short value)
    {
      Value();
      _w.Int16(value);
      Wrote();
    }

    public void Write(ushort value)
    {
      Value();
      _w.UInt16(value);
      Wrote();
    }

    public void Write(int value)
    {
      Value();
      _w.Int32(value);
      Wrote();
    }

    public void Write(uint value)
    {
      Value();
      _w.UInt32(value);
      Wrote();
    }

    public void Write(long value)
    {
      Value();
      _w.Int64(value);
      Wrote();
    }

    public void Write(ulong value)
    {
      Value();
      _w.UInt64(value);
      Wrote();
    }

    public void Write(float value)
    {
      Value();
      _w.Single(value);
      Wrote();
    }

    public void Write(double value)
    {
      Value();
      _w.Double(value);
      Wrote();
    }

    /// <summary>
    /// A string (null: nil). In a str of unknown length (<see cref="WriteStringStart"/>) it is the next chunk, see <see cref="WriteStringChunk"/>.
    /// </summary>
    public void Write(string value)
    {
      if (InFrame(FrameKind.String))
      {
        if (value is null)
          throw new ArgumentNullException(nameof(value), "A chunk of a str of unknown length cannot be nil.");
        WriteStringChunk(value.AsSpan());
        return;
      }

      Value();
      if (value is null)
        _w.Nil();
      else
        _w.String(value);
      Wrote();
    }

    /// <summary>
    /// A timestamp, as the serializer writes a DateTime (see <see cref="LtMsgPackOptions.UnspecifiedDateTimeKind"/>).
    /// </summary>
    public void Write(DateTime value)
    {
      Value();
      _dateTime.Write(_c, value, _dateTimeInfo);
      Wrote();
    }

    /// <summary>
    /// A Guid, as the serializer writes it (see <see cref="LtMsgPackOptions.GuidFormat"/>).
    /// </summary>
    public void Write(Guid value)
    {
      Value();
      _guid.Write(_c, value, _guidInfo);
      Wrote();
    }

    /// <summary>
    /// A decimal, as the serializer writes it (see <see cref="LtMsgPackOptions.DecimalFormat"/>).
    /// </summary>
    public void Write(decimal value)
    {
      Value();
      _decimal.Write(_c, value, _decimalInfo);
      Wrote();
    }

    /// <summary>
    /// A bin (null: nil). In a bin of unknown length (<see cref="WriteBinaryStart"/>) it is the next chunk.
    /// </summary>
    public void WriteBinary(byte[] value)
    {
      if (value is null)
      {
        if (InFrame(FrameKind.Binary))
          throw new ArgumentNullException(nameof(value), "A chunk of a bin of unknown length cannot be nil.");
        WriteNil();
        return;
      }
      WriteBinary(new ReadOnlySpan<byte>(value));
    }

    /// <summary>
    /// A bin. In a bin of unknown length (<see cref="WriteBinaryStart"/>) it is the next chunk.
    /// </summary>
    public void WriteBinary(ReadOnlySpan<byte> value)
    {
      if (InFrame(FrameKind.Binary))
      {
        AddBytes(value.Length);
        _w.BinHeader(value.Length);
        _w.Raw(value);
        Flushed();
        return;
      }

      Value();
      _w.BinHeader(value.Length);
      _w.Raw(value);
      Wrote();
    }

    /// <summary>
    /// An extension value: its type code and data.
    /// </summary>
    public void WriteExtension(sbyte typeCode, ReadOnlySpan<byte> data)
    {
      Value();
      _w.ExtHeader(typeCode, data.Length);
      _w.Raw(data);
      Wrote();
    }

    /// <summary>
    /// One complete MsgPack value that is already encoded, counted as one value. The bytes are not checked.
    /// </summary>
    public void WriteRaw(ReadOnlySpan<byte> value)
    {
      Value();
      _w.Raw(value);
      Wrote();
    }

    #endregion

    #region Arrays and maps

    /// <summary>
    /// An array of <paramref name="count"/> elements: the next <paramref name="count"/> values (it ends by itself after them, there is no <see cref="WriteEnd"/>).
    /// </summary>
    public void WriteArrayHeader(int count)
    {
      if (count < 0)
        throw new ArgumentOutOfRangeException(nameof(count));
      Value();
      CheckDepth();
      _w.ArrayHeader(count);
      Count();
      Push(FrameKind.Array, false, count);
      Done();
    }

    /// <summary>
    /// A map of <paramref name="count"/> entries: the next 2 × <paramref name="count"/> values are its keys and values (it ends by itself after them, there is no <see cref="WriteEnd"/>).
    /// </summary>
    public void WriteMapHeader(int count)
    {
      if (count < 0)
        throw new ArgumentOutOfRangeException(nameof(count));
      Value();
      CheckDepth();
      _w.MapHeader(count);
      Count();
      Push(FrameKind.Map, false, 2L * count);
      Done();
    }

    /// <summary>
    /// Starts an array of unknown length: the values that follow are its elements, until <see cref="WriteEnd"/>.
    /// </summary>
    public void WriteArrayStart()
    {
      Start(FrameKind.Array, Escape.ArrayStart);
    }

    /// <summary>
    /// Starts a map of unknown length: the values that follow are its keys and values, until <see cref="WriteEnd"/> (where the next key would come).
    /// </summary>
    public void WriteMapStart()
    {
      Start(FrameKind.Map, Escape.MapStart);
    }

    /// <summary>
    /// Starts a str of unknown length: the strings that follow (<see cref="Write(string)"/>, <see cref="WriteStringChunk"/>) are its chunks, until <see cref="WriteEnd"/>.
    /// </summary>
    public void WriteStringStart()
    {
      Start(FrameKind.String, Escape.StringStart);
    }

    /// <summary>
    /// Starts a bin of unknown length: the bins that follow (<see cref="WriteBinary(ReadOnlySpan{byte})"/>) are its chunks, until <see cref="WriteEnd"/>.
    /// </summary>
    public void WriteBinaryStart()
    {
      Start(FrameKind.Binary, Escape.BinaryStart);
    }

    /// <summary>
    /// The next chunk of a str of unknown length (<see cref="WriteStringStart"/>), e.g. the chars of IDataRecord.GetChars.
    /// A high surrogate at the end is kept for the next chunk, so a chunk never splits a character (as the format requires).
    /// </summary>
    public void WriteStringChunk(ReadOnlySpan<char> chars)
    {
      if (!InFrame(FrameKind.String))
        throw new InvalidOperationException($"{nameof(WriteStringChunk)} writes the chunks of a str of unknown length, start it with {nameof(WriteStringStart)}.");
      if (chars.Length == 0)
        return;

      if (_pendingHigh != '\0') // the chunk before ended in the middle of a surrogate pair
      {
        char[] joined = new char[chars.Length + 1];
        joined[0] = _pendingHigh;
        chars.CopyTo(new Span<char>(joined, 1, chars.Length));
        _pendingHigh = '\0';
        chars = joined;
      }

      char last = chars[chars.Length - 1];
      if (char.IsHighSurrogate(last))
      {
        _pendingHigh = last;
        chars = chars.Slice(0, chars.Length - 1);
        if (chars.Length == 0)
          return;
      }

      AddBytes(_w.String(chars, ref _scratch));
      Flushed();
    }

    /// <summary>
    /// Ends the innermost array, map, str or bin of unknown length.
    /// </summary>
    public void WriteEnd()
    {
      ThrowIfDisposed();
      if (_depth == 0)
        throw new InvalidOperationException($"There is nothing to end: no array, map, str or bin of unknown length is open.");

      Frame frame = _frames[_depth - 1];
      if (!frame.Unknown)
        throw new InvalidOperationException($"The open {Name(frame.Kind)} has a known length, it ends by itself after {frame.Count} more {(frame.Kind == FrameKind.Map ? "keys and values" : "elements")}. {nameof(WriteEnd)} ends the ones of unknown length ({nameof(WriteArrayStart)}, {nameof(WriteMapStart)}, {nameof(WriteStringStart)}, {nameof(WriteBinaryStart)}).");
      if (frame.Kind == FrameKind.Map && (frame.Count & 1) != 0)
        throw new InvalidOperationException("The map of unknown length ends after a key: write its value first.");

      if (frame.Kind == FrameKind.String && _pendingHigh != '\0') // a high surrogate without its low one: encoded as the encoding does (UTF-8: the replacement character)
      {
        char[] single = { _pendingHigh };
        _pendingHigh = '\0';
        AddBytes(_w.String(single, ref _scratch));
      }

      _w.Ensure(2);
      _w.Buf[_w.Pos++] = Escape.Byte;
      _w.Buf[_w.Pos++] = Escape.End;
      _depth--;
      Done();
    }

    private void Start(FrameKind kind, byte code)
    {
      Value();
      CheckDepth();
      _w.Ensure(2);
      _w.Buf[_w.Pos++] = Escape.Byte;
      _w.Buf[_w.Pos++] = code;
      Count();
      Push(kind, true, 0);
      Done();
    }

    #endregion

    #region Objects

    /// <summary>
    /// Writes the value as <see cref="LtMsgPackSerializer.Serialize{T}(T)"/> does, with its schema as <see cref="Schemas"/> says.
    /// <para>With the indexed schema the object is two values (its schema or a reference to it, then the object), so it is only written as a root value of the stream (<see cref="Depth"/> 0).
    /// Without it, anywhere (one value).</para>
    /// </summary>
    /// <returns>The <see cref="Position"/> after the object</returns>
    public long WriteObject<T>(T value)
    {
      ObjectValue();
      _serializer.Serialize(typeof(T), value, LtMsgPackSerializer.TypedWriter<T>.Write, null, out byte[] unused, this);
      Wrote();
      return Position;
    }

    /// <summary>
    /// <see cref="WriteObject{T}(T)"/> for a type known at runtime.
    /// </summary>
    /// <param name="declaredType">The type the value is declared as (a type id is added when the value's type differs), null: the type of the value</param>
    public long WriteObject(object value, Type declaredType)
    {
      ObjectValue();
      _serializer.Serialize(declaredType ?? value?.GetType() ?? typeof(object), value, LtMsgPackSerializer.BoxedWriter, null, out byte[] unused, this);
      Wrote();
      return Position;
    }

    private void ObjectValue()
    {
      Value();
      if (_depth > 0 && _options._useInexedSchema)
        throw new InvalidOperationException($"With the indexed schema an object is two values (its schema or a reference to it, then the object): {nameof(WriteObject)} writes it as a root value of the stream ({nameof(Depth)} 0). In an array or map, write the values themselves, or use a serializer without the indexed schema.");
    }

    void IPayloadSink.Write(byte[] head, int headLength, bool reference, MsgPackWriter body)
    {
      if (reference)
      {
        SchemaId id = new SchemaId(head, 2);
        bool first = _known.Add(id);
        if (first)
          _referenced.Add(id);

        byte[] schema = null;
        if (_schemas != MsgPackStreamSchemas.External)
        {
          schema = _options._schemaStore.GetInlineSchema(id, _options); // the store has every schema it wrote
          if (_schemas == MsgPackStreamSchemas.PerStream && !first && schema.Length > SchemaStore.ReferenceLength) // a small schema is written anyway: it is shorter than the reference
            schema = null;
        }

        if (schema is null)
          _w.Raw(head, 0, headLength);
        else
          _w.Raw(schema, 0, schema.Length);
      }
      else if (headLength > 0)
        _w.Raw(head, 0, headLength);

      _w.Raw(body.Buf, 0, body.Pos);
    }

    #endregion

    #region Bookkeeping

    private bool InFrame(FrameKind kind)
    {
      return _depth > 0 && _frames[_depth - 1].Kind == kind;
    }

    /// <summary>
    /// A value may come here: not in a str or bin of unknown length (only their chunks), not more than an array of unknown length can hold.
    /// </summary>
    private void Value()
    {
      ThrowIfDisposed();
      if (_depth == 0)
        return;

      Frame frame = _frames[_depth - 1];
      if (frame.Kind == FrameKind.String)
        throw new InvalidOperationException($"A str of unknown length holds only its chunks: strings ({nameof(Write)}(string), {nameof(WriteStringChunk)}), then {nameof(WriteEnd)}.");
      if (frame.Kind == FrameKind.Binary)
        throw new InvalidOperationException($"A bin of unknown length holds only its chunks: bins ({nameof(WriteBinary)}), then {nameof(WriteEnd)}.");
      if (frame.Unknown && frame.Count >= (frame.Kind == FrameKind.Map ? 2 * MaxUnknownCount : MaxUnknownCount))
        throw new InvalidOperationException($"{(frame.Kind == FrameKind.Map ? "A map" : "An array")} of unknown length holds at most {MaxUnknownCount} {(frame.Kind == FrameKind.Map ? "entries" : "elements")}.");
    }

    /// <summary>
    /// The value was written: it is an element of the open array or map.
    /// </summary>
    private void Count()
    {
      if (_depth == 0)
        return;
      if (_frames[_depth - 1].Unknown)
        _frames[_depth - 1].Count++;
      else
        _frames[_depth - 1].Count--;
    }

    private void Wrote()
    {
      Count();
      Done();
    }

    /// <summary>
    /// Closes the arrays and maps of a known length that have all their elements, and passes the buffer to the stream when it is long enough.
    /// </summary>
    private void Done()
    {
      while (_depth > 0 && !_frames[_depth - 1].Unknown && _frames[_depth - 1].Count == 0)
        _depth--;
      Flushed();
    }

    private void Flushed()
    {
      if (_w.Pos >= _flushThreshold)
        FlushBuffer();
    }

    private void AddBytes(int count)
    {
      ThrowIfDisposed();
      if (_frames[_depth - 1].Count + count > MaxUnknownCount)
        throw new InvalidOperationException($"A {Name(_frames[_depth - 1].Kind)} of unknown length holds at most {MaxUnknownCount} bytes.");
      _frames[_depth - 1].Count += count;
    }

    private void CheckDepth()
    {
      if (_depth >= _options._maxDepth)
        throw new InvalidOperationException($"The values are nested deeper than {nameof(MsgPackOptions)}.{nameof(MsgPackOptions.MaxDepth)} ({_options._maxDepth}) of the serializer, readers refuse them.");
    }

    private void Push(FrameKind kind, bool unknown, long count)
    {
      if (_depth == _frames.Length)
        Array.Resize(ref _frames, _frames.Length * 2);
      _frames[_depth++] = new Frame { Kind = kind, Unknown = unknown, Count = count };
    }

    private static string Name(FrameKind kind)
    {
      switch (kind)
      {
        case FrameKind.Array: return "array";
        case FrameKind.Map: return "map";
        case FrameKind.String: return "str";
        default: return "bin";
      }
    }

    private void ThrowIfDisposed()
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(MsgPackStreamWriter));
    }

    #endregion

    #region Output

    private void FlushBuffer()
    {
      if (_w.Pos == 0)
        return;
      _target.Write(_w.Buf, 0, _w.Pos);
      _flushed += _w.Pos;
      _w.Pos = 0;
      if (_w.Buf.Length > 1024 * 1024) // do not keep the buffer of a huge value alive
        _w.Buf = new byte[InitialCapacity];
    }

    /// <summary>
    /// Writes the buffer to the stream and flushes the stream.
    /// </summary>
    public void Flush()
    {
      ThrowIfDisposed();
      FlushBuffer();
      _target.Flush();
    }

    /// <summary>
    /// Writes the buffer to the stream and closes it (unless the writer was created with leaveOpen). Arrays and maps that are still open are not ended: a reader finds the data cut off.
    /// </summary>
    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
      try
      {
        FlushBuffer();
        _target.Flush();
      }
      finally
      {
        if (!_leaveOpen)
          _target.Dispose();
      }
    }

    #endregion
  }
}
