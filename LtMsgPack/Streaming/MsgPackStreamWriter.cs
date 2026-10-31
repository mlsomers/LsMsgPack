using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.Streaming;
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
  /// and whole objects with their schema (<see cref="WriteObject{T}(T)"/>), or one schema for a whole root value (<see cref="WriteHead{TRoot}"/>) whose objects are then written without it.
  /// The values between which <see cref="Depth"/> is 0 are the root values of the stream, e.g. one per result set or table.
  /// <para>Values are written into a buffer that goes to the stream once it holds <see cref="FlushThreshold"/> bytes, at <see cref="Flush"/> and at <see cref="Dispose"/>.
  /// <see cref="Position"/> is where the next value starts.</para>
  /// <para>With <see cref="MsgPackStreamIndexOptions"/> the writer keeps an index of the root values (each one an entry: a value, an array or map, an object, a head with its body), with hashes, points into large arrays and checkpoints,
  /// and writes it at the end of the stream (<see cref="MsgPackStreamIndex"/>), so a reader can go to an entry directly (<see cref="MsgPackStreamReader.ReadIndexes"/>).</para>
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
    private HeadState _headState;
    private Serializer.WriterHead _head; // null without the indexed schema
    private readonly IndexState _ix; // null without an index
    private bool _faulted; // writing to the stream failed: Dispose writes no index after it

    /// <summary>
    /// A writer with the default options of <see cref="LtMsgPackSerializer.Default"/>, objects with their schema (<see cref="MsgPackStreamSchemas.PerObject"/>). The stream is closed by <see cref="Dispose"/>.
    /// </summary>
    [RequiresUnreferencedCode(LtMsgPackSerializer.RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(LtMsgPackSerializer.RequiresDynamicCodeMessage)]
    public MsgPackStreamWriter(Stream target) : this(target, LtMsgPackSerializer.Default) { }

    /// <param name="serializer">Writes the objects, and its options decide the formats of the values (integers, byte order, Guids, decimals, DateTimes)</param>
    /// <param name="schemas">Where the schemas of the objects go</param>
    /// <param name="leaveOpen">Do not close the stream in <see cref="Dispose"/></param>
    /// <param name="index">Keep an index of the root values and write it at the end of the stream (and at checkpoints), null: none</param>
    /// <exception cref="ArgumentException"><see cref="MsgPackStreamSchemas.PerStream"/> or <see cref="MsgPackStreamSchemas.External"/> without a serializer that writes schema references, or metadata in <paramref name="index"/> that is not plain values</exception>
    public MsgPackStreamWriter(Stream target, LtMsgPackSerializer serializer, MsgPackStreamSchemas schemas = MsgPackStreamSchemas.PerObject, bool leaveOpen = false, MsgPackStreamIndexOptions index = null)
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
      if (index != null)
      {
        _ix = new IndexState(index);
        _flushed = index.StartPosition;
      }
    }

    /// <summary>
    /// A writer that appends to a stream (that seeks, reads and writes), e.g. a file opened with FileMode.OpenOrCreate: after its last index, which the new index points to (<see cref="MsgPackStreamIndex.PreviousTail"/>),
    /// so a reader finds the entries of both parts. An empty stream gets an index of its own. The stream is not changed until the writer writes.
    /// <para>A stream without an index is searched backwards completely for one first (to tell it from a stream that was not finished): it gets an index of the appended entries only.</para>
    /// </summary>
    /// <param name="index">The options of the index (copied: <see cref="MsgPackStreamIndexOptions.StartPosition"/> and <see cref="MsgPackStreamIndexOptions.PreviousTail"/> are set from the stream), null: the defaults</param>
    /// <exception cref="InvalidOperationException">The stream continues after its last index (the writer stopped without finishing it: cut it off after that tail first),
    /// or its last index says it was interrupted in the middle of a root value</exception>
    public static MsgPackStreamWriter Append(Stream stream, LtMsgPackSerializer serializer, MsgPackStreamSchemas schemas = MsgPackStreamSchemas.PerObject, MsgPackStreamIndexOptions index = null, bool leaveOpen = false)
    {
      if (stream is null)
        throw new ArgumentNullException(nameof(stream));
      if (serializer is null)
        throw new ArgumentNullException(nameof(serializer));
      if (!stream.CanSeek || !stream.CanRead || !stream.CanWrite)
        throw new ArgumentException("Appending needs a stream that seeks, reads and writes.", nameof(stream));

      MsgPackStreamIndexOptions options = new MsgPackStreamIndexOptions();
      if (index != null)
      {
        options.Hasher = index.Hasher;
        options.Observer = index.Observer;
        options.CheckpointInterval = index.CheckpointInterval;
        options.PointInterval = index.PointInterval;
        options.Writer = index.Writer;
        options.Metadata = index.Metadata;
      }

      long length = stream.Length;
      List<MsgPackStreamIndex> indexes = IndexFormat.ReadAll(stream, true, out long lastTail, out bool endsWithTail, out long delta);
      if (indexes.Count == 0)
        options.StartPosition = length;
      else
      {
        if (!endsWithTail)
          throw new InvalidOperationException($"The stream continues after its last index (its tail at {lastTail}): the writer stopped without finishing it. Cut it off after that tail (at {lastTail + MsgPackStreamIndex.TailLength}) to append to it; the data after it is not indexed.");
        if (indexes[indexes.Count - 1].Status == MsgPackStreamIndexStatus.Interrupted)
          throw new InvalidOperationException("The stream was interrupted in the middle of a root value (its last index says so): values appended to it would be read as part of that value.");
        options.StartPosition = length - delta; // as the writer before counted
        options.PreviousTail = lastTail - delta;
      }
      stream.Position = length;
      return new MsgPackStreamWriter(stream, serializer, schemas, leaveOpen, options);
    }

    #region State

    /// <summary>
    /// The number of bytes written (to the stream and in the buffer) since the writer was created: where the next value starts, relative to where the writer started
    /// (plus <see cref="MsgPackStreamIndexOptions.StartPosition"/> when it appends).
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

    private enum HeadState : byte
    {
      None,

      /// <summary>
      /// A head was written, the next root value is its body.
      /// </summary>
      AwaitingBody,

      /// <summary>
      /// The body of the head is being written (an array or map that is still open).
      /// </summary>
      InBody
    }

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
    /// <para>With the indexed schema the object is two values (its schema or a reference to it, then the object), so it is only written as a root value of the stream (<see cref="Depth"/> 0),
    /// unless it is part of the body of a head (<see cref="WriteHead{TRoot}"/>): then only the value is written, with the schema of the head, at any depth. Without the indexed schema, anywhere (one value).</para>
    /// </summary>
    /// <typeparam name="T">The declared type of the value: a type id is added when the value's type differs. In the body of a head, the type of the place in the root type (e.g. the element type of the list).</typeparam>
    /// <returns>The <see cref="Position"/> after the object</returns>
    /// <exception cref="InvalidOperationException">In the body of a head: the value needs a class that the schema of the head does not have (nothing is written)</exception>
    public long WriteObject<T>(T value)
    {
      bool body = ObjectValue(out Serializer.WriterHead head);
      try
      {
        if (head != null)
          _serializer.SerializeBody(head, typeof(T), value, LtMsgPackSerializer.TypedWriter<T>.Write, _depth, this);
        else
          _serializer.Serialize(typeof(T), value, LtMsgPackSerializer.TypedWriter<T>.Write, null, out byte[] unused, this);
      }
      catch
      {
        Unwritten(body);
        throw;
      }
      Wrote();
      return Position;
    }

    /// <summary>
    /// <see cref="WriteObject{T}(T)"/> for a type known at runtime.
    /// </summary>
    /// <param name="declaredType">The type the value is declared as (a type id is added when the value's type differs), null: the type of the value</param>
    public long WriteObject(object value, Type declaredType)
    {
      bool body = ObjectValue(out Serializer.WriterHead head);
      Type assignedTo = declaredType ?? value?.GetType() ?? typeof(object);
      try
      {
        if (head != null)
          _serializer.SerializeBody(head, assignedTo, value, LtMsgPackSerializer.BoxedWriter, _depth, this);
        else
          _serializer.Serialize(assignedTo, value, LtMsgPackSerializer.BoxedWriter, null, out byte[] unused, this);
      }
      catch
      {
        Unwritten(body);
        throw;
      }
      Wrote();
      return Position;
    }

    /// <returns>The value is (part of) the body of a head</returns>
    /// <param name="head">The head whose schema the value is written with, null for a whole payload (or without the indexed schema)</param>
    private bool ObjectValue(out Serializer.WriterHead head)
    {
      ThrowIfDisposed();
      bool body = _depth == 0 ? _headState == HeadState.AwaitingBody : _headState == HeadState.InBody;
      head = body ? _head : null;
      if (!body && _depth > 0 && _options._useInexedSchema)
        throw new InvalidOperationException($"With the indexed schema an object is two values (its schema or a reference to it, then the object): {nameof(WriteObject)} writes it as a root value of the stream ({nameof(Depth)} 0). To write objects in an array or map, write the schema once before it with {nameof(WriteHead)}, or use a serializer without the indexed schema.");
      Value();
      return body;
    }

    /// <summary>
    /// The root value of the body was not written after all: the head still waits for it.
    /// </summary>
    private void Unwritten(bool body)
    {
      if (body && _depth == 0)
        _headState = HeadState.AwaitingBody;
    }

    /// <summary>
    /// Writes the schema of the root type, for the root value that follows it (its body): the objects in the body are then written without their schema (<see cref="WriteObject{T}(T)"/>, at any depth), as <see cref="LtMsgPackSerializer.Serialize{T}(T)"/> writes them in a value of <typeparamref name="TRoot"/>.
    /// E.g. <c>WriteHead&lt;List&lt;Row&gt;&gt;()</c>, <see cref="WriteArrayStart"/>, a <see cref="WriteObject{T}(T)"/> per row, <see cref="WriteEnd"/>: the schema once for all rows.
    /// With arrays and maps of a known length the stream holds what <see cref="LtMsgPackSerializer.Serialize{T}(T)"/> writes for the whole value (with <see cref="MsgPackOptions.CompleteSchemas"/>).
    /// <para>The schema is complete (<see cref="MsgPackOptions.CompleteSchemas"/>, whatever the options say): it has the classes of the root type, of its properties, elements and their derived classes in the assemblies it reaches.
    /// A value in the body that needs another class (declared as object, or of an assembly the root type does not reach) is refused.</para>
    /// <para>The schema goes where <see cref="Schemas"/> says: inline (<see cref="MsgPackStreamSchemas.PerObject"/>, and the first time with <see cref="MsgPackStreamSchemas.PerStream"/>) or a reference.
    /// Without the indexed schema nothing is written, the body is written as without a head.</para>
    /// </summary>
    /// <typeparam name="TRoot">The declared type of the root value that follows, as the type argument of <see cref="LtMsgPackSerializer.Serialize{T}(T)"/></typeparam>
    /// <exception cref="InvalidOperationException">Not at <see cref="Depth"/> 0, or the head written before still waits for its body</exception>
    public void WriteHead<TRoot>()
    {
      WriteHead(typeof(TRoot));
    }

    /// <summary>
    /// <see cref="WriteHead{TRoot}"/> for a type known at runtime.
    /// </summary>
    public void WriteHead(Type root)
    {
      if (root is null)
        throw new ArgumentNullException(nameof(root));
      ThrowIfDisposed();
      if (_depth > 0)
        throw new InvalidOperationException($"A head is written before a root value of the stream ({nameof(Depth)} 0).");
      if (_headState == HeadState.AwaitingBody)
        throw new InvalidOperationException("The head written before still waits for its body (the next root value).");

      _head = null;
      if (_options._useInexedSchema)
      {
        Serializer.WriterHead head = _serializer.PrepareHead(root);
        if (_ix != null && _ix.Entry == null)
          StartEntry(); // the head and its body are one entry
        WriteSchema(head.Id, head.Session.Reference, head.Store);
        _head = head;
      }
      else if (_ix != null && _ix.Entry == null)
        StartEntry();
      _headState = HeadState.AwaitingBody;
      Flushed();
    }

    void IPayloadSink.Write(byte[] head, int headLength, bool reference, MsgPackWriter body)
    {
      if (reference)
        WriteSchema(new SchemaId(head, 2), head, _options._schemaStore);
      else if (headLength > 0)
        _w.Raw(head, 0, headLength);

      _w.Raw(body.Buf, 0, body.Pos);
    }

    /// <summary>
    /// The schema of an object or a head, as <see cref="Schemas"/> says: inline or the reference.
    /// </summary>
    /// <param name="store">Has the schema</param>
    private void WriteSchema(SchemaId id, byte[] reference, SchemaStore store)
    {
      bool first = _known.Add(id);
      if (first && _options._writeSchemaReference && ReferenceEquals(store, _options._schemaStore))
        _referenced.Add(id);

      byte[] schema = null;
      if (_schemas != MsgPackStreamSchemas.External)
      {
        schema = store.GetInlineSchema(id, _options); // the store has every schema it wrote
        if (_schemas == MsgPackStreamSchemas.PerStream && !first && schema.Length > SchemaStore.ReferenceLength) // a small schema is written anyway: it is shorter than the reference
          schema = null;
      }

      if (first && _ix != null && _schemas != MsgPackStreamSchemas.PerObject) // with PerObject every entry has its schemas inline
      {
        IndexFormat.WriteSchemaLocation(_ix.Schemas, id, schema is null ? (long?)null : Position, schema is null ? 0 : schema.Length);
        _ix.SchemaCount++;
      }

      if (schema is null)
        _w.Raw(reference, 0, SchemaStore.ReferenceLength);
      else
        _w.Raw(schema, 0, schema.Length);
    }

    #endregion

    #region Index

    /// <summary>
    /// The index entry of the next root value (with <see cref="MsgPackStreamIndexOptions"/>), to name it and add to it (<see cref="MsgPackStreamEntry.Modified"/>, <see cref="MsgPackStreamEntry.Metadata"/>...) before or while it is written;
    /// the writer fills in where it is. A root value without it gets an entry without a name.
    /// </summary>
    /// <param name="identifier">A string or an integer, null for none</param>
    /// <param name="contentType">What the entry holds (a MIME type, the name of the root type), null for none</param>
    /// <exception cref="InvalidOperationException">The writer keeps no index, or not before a root value (<see cref="Depth"/> 0, not between a head and its body)</exception>
    /// <exception cref="ArgumentException">The identifier is neither a string nor an integer</exception>
    public MsgPackStreamEntry BeginEntry(object identifier = null, string contentType = null)
    {
      ThrowIfDisposed();
      if (_ix is null)
        throw new InvalidOperationException($"The writer keeps no index: create it with {nameof(MsgPackStreamIndexOptions)}.");
      if (_ix.Entry != null)
        throw new InvalidOperationException($"An entry begins before a root value ({nameof(Depth)} 0), not in one (nor between a head and its body).");
      IndexFormat.WriteIdentifier(IndexFormat.NewWriter(32), identifier); // checks it
      _ix.Next = new MsgPackStreamEntry { Identifier = identifier, ContentType = contentType };
      return _ix.Next;
    }

    /// <summary>
    /// Adds a point to the sparse index of the current entry (<see cref="MsgPackStreamEntry.Points"/>): where the next element of its array starts, e.g. where the <paramref name="key"/> the elements are ordered by changes
    /// (besides the points of <see cref="MsgPackStreamIndexOptions.PointInterval"/>).
    /// </summary>
    /// <param name="key">A plain value the elements are ordered by (a timestamp, an id), null for none</param>
    /// <exception cref="InvalidOperationException">The writer keeps no index, or not between the elements of a root array (<see cref="Depth"/> 1, also the body of a head)</exception>
    public void AddPoint(object key = null)
    {
      ThrowIfDisposed();
      if (_ix is null)
        throw new InvalidOperationException($"The writer keeps no index: create it with {nameof(MsgPackStreamIndexOptions)}.");
      if (_depth != 1 || !_ix.RootArray || _ix.Entry is null)
        throw new InvalidOperationException($"A point is added between the elements of an array that is a root value ({nameof(Depth)} 1), before the element it points to.");
      if (key != null)
        IndexFormat.WritePlain(IndexFormat.NewWriter(32), key, 1); // checks it
      _ix.AddPoint(_ix.Elements, Position, key);
    }

    private void StartEntry()
    {
      IndexState ix = _ix;
      MsgPackStreamEntry entry = ix.Next ?? new MsgPackStreamEntry();
      ix.Next = null;
      entry.Offset = Position;
      ix.Entry = entry;
      ix.FeedFrom = _w.Pos;
      ix.Elements = 0;
      ix.RootArray = false;
    }

    /// <summary>
    /// An element of the entry's array was written (its start is <see cref="IndexState.ElementStart"/>).
    /// </summary>
    private void CountElement()
    {
      IndexState ix = _ix;
      long element = ix.Elements++;
      if (ix.PointInterval > 0 && element % ix.PointInterval == 0)
        ix.AddPoint(element, ix.ElementStart, null);
    }

    /// <summary>
    /// The root value of the entry is complete (or the writer is disposed in the middle of it): its length, hash and points, the observer's sidecar after it, encoded into the entries of the next index.
    /// </summary>
    private void EndEntry(bool incomplete)
    {
      IndexState ix = _ix;
      MsgPackStreamEntry entry = ix.Entry;
      ix.Entry = null; // the sidecar and an index written below are no part of the entry
      ix.Feed(_w.Buf, ix.FeedFrom, _w.Pos);
      entry.Length = Position - entry.Offset;
      if (ix.Hasher != null)
      {
        byte[] hash = new byte[ix.Hasher.HashLength];
        int length = ix.Hasher.GetHashAndReset(hash);
        if (length != hash.Length)
          Array.Resize(ref hash, length);
        entry.Hash = hash;
      }
      if (ix.RootArray)
      {
        entry.ElementCount = ix.Elements;
        List<MsgPackStreamIndexPoint> points = entry.Points;
        while (points != null && points.Count > 0 && points[points.Count - 1].Element >= ix.Elements) // added before an element that did not come
          points.RemoveAt(points.Count - 1);
      }
      entry.Incomplete = incomplete;

      if (!incomplete && ix.Observer != null)
      {
        ReadOnlyMemory<byte> sidecar = ix.Observer.EntryEnded(entry);
        if (!sidecar.IsEmpty)
        {
          _w.ExtHeader(MsgPackStreamIndex.SidecarExtensionType, sidecar.Length);
          entry.SidecarOffset = Position;
          entry.SidecarLength = sidecar.Length;
          _w.Raw(sidecar.Span);
        }
      }

      try
      {
        IndexFormat.WriteEntry(ix.Entries, entry);
      }
      catch (ArgumentException) when (incomplete) // Dispose still writes the index: the entry without its metadata
      {
        entry.Metadata = null;
        IndexFormat.WriteEntry(ix.Entries, entry);
      }
      ix.EntryCount++;

      if (!incomplete && ix.CheckpointInterval > 0 && ix.EntryCount >= ix.CheckpointInterval)
      {
        WriteIndex(MsgPackStreamIndexStatus.Checkpoint);
        FlushBuffer(); // a checkpoint is only worth something on the disk
        _faulted = true;
        _target.Flush();
        _faulted = false;
      }
    }

    /// <summary>
    /// The index of the entries since the last one, then its tail (<see cref="MsgPackStreamIndex"/>), between root values.
    /// </summary>
    private void WriteIndex(MsgPackStreamIndexStatus status)
    {
      IndexState ix = _ix;
      MsgPackWriter content = ix.Content;
      content.Reset(IndexFormat.Options);
      IndexFormat.WriteIndex(content, status, ix.Writer, ix.PreviousTail, ix.Hasher?.Algorithm, ix.SchemaCount, ix.Schemas, ix.EntryCount, ix.Entries, ix.Metadata);

      long offset = Position;
      int start = _w.Pos;
      _w.ExtHeader(MsgPackStreamIndex.IndexExtensionType, content.Pos); // in the stream's byte order, for the readers that skip it (the reader of the index measures it by the tail)
      _w.Raw(content.Buf, 0, content.Pos);
      int length = _w.Pos - start;
      uint crc = Crc32.Compute(new ReadOnlySpan<byte>(_w.Buf, start, length));
      ix.PreviousTail = Position;
      IndexFormat.WriteTail(_w, offset, length, crc);

      ix.Entries.Reset(IndexFormat.Options);
      ix.EntryCount = 0;
      ix.Schemas.Reset(IndexFormat.Options);
      ix.SchemaCount = 0;
      if (content.Buf.Length > 1024 * 1024)
        ix.Content = IndexFormat.NewWriter(4096);
    }

    /// <summary>
    /// What the writer keeps for the index: the options, the entries and schemas since the last index (encoded), the open entry.
    /// </summary>
    private sealed class IndexState
    {
      internal readonly IMsgPackStreamHasher Hasher;
      internal readonly IMsgPackStreamObserver Observer;
      internal readonly int CheckpointInterval;
      internal readonly long PointInterval;
      internal readonly string Writer;
      internal readonly byte[] Metadata; // encoded, in every index
      internal long? PreviousTail;

      internal readonly MsgPackWriter Entries = IndexFormat.NewWriter(4096);
      internal int EntryCount;
      internal readonly MsgPackWriter Schemas = IndexFormat.NewWriter(256);
      internal int SchemaCount;
      internal MsgPackWriter Content = IndexFormat.NewWriter(4096);

      internal MsgPackStreamEntry Next; // of BeginEntry, for the next root value
      internal MsgPackStreamEntry Entry; // open: its root value is being written
      internal int FeedFrom; // the entry's bytes in the writer's buffer from here are not passed to the hasher and observer yet
      internal bool RootArray; // the entry's root value is an array
      internal long Elements; // the elements of that array written so far
      internal long ElementStart; // where the element being written starts

      internal IndexState(MsgPackStreamIndexOptions options)
      {
        if (options.CheckpointInterval < 0)
          throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(MsgPackStreamIndexOptions.CheckpointInterval)} cannot be negative.");
        if (options.PointInterval < 0)
          throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(MsgPackStreamIndexOptions.PointInterval)} cannot be negative.");
        if (options.StartPosition < 0)
          throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(MsgPackStreamIndexOptions.StartPosition)} cannot be negative.");
        if (options.PreviousTail.HasValue && (options.PreviousTail.Value < 0 || options.PreviousTail.Value > options.StartPosition - MsgPackStreamIndex.TailLength))
          throw new ArgumentOutOfRangeException(nameof(options), $"{nameof(MsgPackStreamIndexOptions.PreviousTail)} is before {nameof(MsgPackStreamIndexOptions.StartPosition)}.");

        Hasher = options.Hasher;
        Observer = options.Observer;
        CheckpointInterval = options.CheckpointInterval;
        PointInterval = options.PointInterval;
        Writer = options.Writer;
        PreviousTail = options.PreviousTail;
        if (options.Metadata != null)
        {
          MsgPackWriter metadata = IndexFormat.NewWriter(256);
          IndexFormat.WriteMetadata(metadata, options.Metadata); // throws now for values that are not plain, not when the index is written
          Metadata = metadata.ToArray();
        }
      }

      /// <summary>
      /// The bytes of the open entry from <paramref name="from"/> to <paramref name="to"/> in the writer's buffer, to the hasher and the observer.
      /// </summary>
      internal void Feed(byte[] buffer, int from, int to)
      {
        if (to <= from)
          return;
        ReadOnlySpan<byte> bytes = new ReadOnlySpan<byte>(buffer, from, to - from);
        if (Hasher != null)
          Hasher.Append(bytes);
        if (Observer != null)
          Observer.Append(bytes);
      }

      /// <summary>
      /// A point at <paramref name="element"/>, which starts at <paramref name="position"/>; a second one at the same element replaces it (keeping a key).
      /// </summary>
      internal void AddPoint(long element, long position, object key)
      {
        List<MsgPackStreamIndexPoint> points = Entry.Points;
        if (points is null)
          Entry.Points = points = new List<MsgPackStreamIndexPoint>();
        long offset = position - Entry.Offset;
        int last = points.Count - 1;
        if (last >= 0 && points[last].Element == element)
          points[last] = new MsgPackStreamIndexPoint(element, offset, key ?? points[last].Key);
        else
          points.Add(new MsgPackStreamIndexPoint(element, offset, key));
      }
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
      {
        if (_headState == HeadState.AwaitingBody)
          _headState = HeadState.InBody;
        if (_ix != null && _ix.Entry == null)
          StartEntry();
        return;
      }

      Frame frame = _frames[_depth - 1];
      if (frame.Kind == FrameKind.String)
        throw new InvalidOperationException($"A str of unknown length holds only its chunks: strings ({nameof(Write)}(string), {nameof(WriteStringChunk)}), then {nameof(WriteEnd)}.");
      if (frame.Kind == FrameKind.Binary)
        throw new InvalidOperationException($"A bin of unknown length holds only its chunks: bins ({nameof(WriteBinary)}), then {nameof(WriteEnd)}.");
      if (frame.Unknown && frame.Count >= (frame.Kind == FrameKind.Map ? 2 * MaxUnknownCount : MaxUnknownCount))
        throw new InvalidOperationException($"{(frame.Kind == FrameKind.Map ? "A map" : "An array")} of unknown length holds at most {MaxUnknownCount} {(frame.Kind == FrameKind.Map ? "entries" : "elements")}.");
      if (_depth == 1 && _ix != null)
        _ix.ElementStart = Position;
    }

    /// <summary>
    /// The value was written: it is an element of the open array or map.
    /// </summary>
    private void Count()
    {
      if (_depth == 0)
        return;
      if (_depth == 1 && _ix != null && _ix.RootArray)
        CountElement();
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
      if (_depth == 0 && _headState == HeadState.InBody) // the body of the head is complete
      {
        _headState = HeadState.None;
        _head = null;
      }
      if (_depth == 0 && _ix != null && _ix.Entry != null && _headState == HeadState.None)
        EndEntry(false);
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
      if (_depth == 0 && _ix != null)
        _ix.RootArray = kind == FrameKind.Array;
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
      if (_ix != null && _ix.Entry != null)
      {
        _ix.Feed(_w.Buf, _ix.FeedFrom, _w.Pos);
        _ix.FeedFrom = 0;
      }
      _faulted = true;
      _target.Write(_w.Buf, 0, _w.Pos);
      _faulted = false;
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
      _faulted = true;
      _target.Flush();
      _faulted = false;
    }

    /// <summary>
    /// Writes the buffer to the stream and closes it (unless the writer was created with leaveOpen). Arrays and maps that are still open are not ended: a reader finds the data cut off.
    /// <para>With an index, the index is written first: <see cref="MsgPackStreamIndexStatus.Complete"/> between root values; in the middle of one (an exception left arrays or maps open, or a head without its body)
    /// <see cref="MsgPackStreamIndexStatus.Interrupted"/>, with that entry <see cref="MsgPackStreamEntry.Incomplete"/>. Not after writing to the stream failed.</para>
    /// </summary>
    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
      try
      {
        if (_ix != null && !_faulted)
        {
          MsgPackStreamIndexStatus status = MsgPackStreamIndexStatus.Complete;
          if (_ix.Entry != null && _depth == 0 && _headState == HeadState.None && Position == _ix.Entry.Offset)
            _ix.Entry = null; // a root value that was refused (nothing written)
          if (_ix.Entry != null)
          {
            EndEntry(true);
            status = MsgPackStreamIndexStatus.Interrupted;
          }
          WriteIndex(status);
        }
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
