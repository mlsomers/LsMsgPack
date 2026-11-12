using LsMsgPack;
using LsMsgPack.Streaming;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace LsMsgPack.Streaming
{
  // The async methods read ahead asynchronously until the buffer holds what a synchronous step needs (a token, a value read whole), then run that step without I/O (_noIo):
  // the parsing and the checks are the synchronous ones. Only what reads the stream is here twice: TryEnsure, PassPayload, SkipStreamValues, PastStreamValues, AtEnd.
  public sealed partial class MsgPackStreamReader
  {
    private bool _noIo; // a synchronous step of an async method runs: what it needs is buffered
    private long[] _scan; // BufferValuesAsync: per open array, map, str or bin the values still to come, -1 for an unknown length
    private int _scanDepth;
    private int _scanAt; // from _pos (which moves when the buffer makes room): the bytes passed by Scan

    /// <summary>
    /// <see cref="Read"/>, reading the stream asynchronously.
    /// </summary>
    /// <returns>false at the end of the stream between root values</returns>
    public Task<bool> ReadAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
      if (_disposed || _payloadPending || _depth == 0 || !TokenBuffered())
        return ReadSlowAsync(cancellationToken);

      // In an array or map with the token buffered (most tokens): the synchronous step, without the state machines of the async methods
      _noIo = true;
      try
      {
        return Read() ? True : False;
      }
      catch (Exception ex) // as an async method
      {
        return Task.FromException<bool>(ex);
      }
      finally
      {
        _noIo = false;
      }
    }

    /// <summary>
    /// The next token is in the buffer, or nothing needs to be read (the end of an array or map of known length): what <see cref="ReadSlowAsync"/> reads ahead in an array or map (<see cref="Depth"/> > 0, no payload pending).
    /// </summary>
    private bool TokenBuffered()
    {
      Frame frame = _frames[_depth - 1];
      if (!frame.Unknown && frame.Count == 0)
        return true;
      int buffered = _end - _pos;
      return buffered > 0 && buffered >= TokenLengths[_buf[_pos]];
    }

    private async Task<bool> ReadSlowAsync(CancellationToken cancellationToken)
    {
      ThrowIfDisposed();
      await PassPayloadAsync(cancellationToken).ConfigureAwait(false);
      if (_depth == 0 || _frames[_depth - 1].Unknown || _frames[_depth - 1].Count > 0) // otherwise the end of an array or map of known length: nothing to read
      {
        if (_depth == 0 && _mark < 0)
          await SkipStreamValuesAsync(cancellationToken).ConfigureAwait(false);
        if (await TryEnsureAsync(1, cancellationToken).ConfigureAwait(false))
          await TryEnsureAsync(TokenLengths[_buf[_pos]], cancellationToken).ConfigureAwait(false);
      }
      _noIo = true;
      try
      {
        return Read();
      }
      finally
      {
        _noIo = false;
      }
    }

    /// <summary>
    /// <see cref="AtEnd"/>, reading ahead asynchronously: there are no more values here (the innermost open array, map, str or bin ends with the next token, or at <see cref="Depth"/> 0 the stream ends).
    /// </summary>
    public Task<bool> AtEndAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
      ThrowIfDisposed();
      int atEnd = AtEndBuffered();
      return atEnd < 0 ? AtEndSlowAsync(cancellationToken) : atEnd == 0 ? False : True;
    }

    private static readonly Task<bool> True = Task.FromResult(true);
    private static readonly Task<bool> False = Task.FromResult(false);

    /// <summary>
    /// <see cref="AtEnd"/> in an array or map, when the buffer tells: 1 at the end, 0 not, -1 unknown (the data is not buffered, or at <see cref="Depth"/> 0).
    /// </summary>
    private int AtEndBuffered()
    {
      if (_depth == 0)
        return -1;
      if (!_frames[_depth - 1].Unknown)
        return _frames[_depth - 1].Count == 0 ? 1 : 0;
      long next = _payloadPending ? _length : 0;
      long buffered = _end - _pos;
      if (buffered <= next)
        return -1;
      if (_buf[_pos + next] != Escape.Byte)
        return 0;
      if (buffered <= next + 1)
        return -1;
      return _buf[_pos + next + 1] == Escape.End ? 1 : 0;
    }

    private async Task<bool> AtEndSlowAsync(CancellationToken cancellationToken)
    {
      if (_depth > 0 && !_frames[_depth - 1].Unknown)
        return _frames[_depth - 1].Count == 0;
      if (_payloadPending && _length >= MaxBufferLength - 2)
        return false;
      int next = _payloadPending ? (int)_length : 0; // after the data of the current token
      if (_depth == 0)
      {
        if (!_payloadPending && _mark < 0)
        {
          await SkipStreamValuesAsync(cancellationToken).ConfigureAwait(false);
          return !await TryEnsureAsync(1, cancellationToken).ConfigureAwait(false);
        }
        int past = await PastStreamValuesAsync(next, cancellationToken).ConfigureAwait(false);
        return !await TryEnsureAsync(past + 1, cancellationToken).ConfigureAwait(false);
      }
      return await TryEnsureAsync(next + 1, cancellationToken).ConfigureAwait(false) && _buf[_pos + next] == Escape.Byte
        && await TryEnsureAsync(next + 2, cancellationToken).ConfigureAwait(false) && _buf[_pos + next + 1] == Escape.End;
    }

    /// <summary>
    /// <see cref="Skip"/>, reading the stream asynchronously: an array, map, str or bin to its end (without buffering it).
    /// </summary>
    public async Task SkipAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
      ThrowIfDisposed();
      if (!IsStart(_token))
        return;
      int depth = _depth;
      while (_depth >= depth)
        await ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <see cref="GetValue"/>, reading the stream asynchronously: the current value as the serializer reads it into object; at the start of an array, map, str or bin all of it (the reader is then at its end).
    /// </summary>
    public async Task<object> GetValueAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
      ThrowIfDisposed();
      await BufferCurrentAsync(cancellationToken).ConfigureAwait(false);
      _noIo = true;
      try
      {
        return GetValue();
      }
      finally
      {
        _noIo = false;
      }
    }

    /// <summary>
    /// <see cref="GetString"/>, reading the stream asynchronously: a str, a str of unknown length (all its chunks) or nil (null).
    /// </summary>
    public async Task<string> GetStringAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
      ThrowIfDisposed();
      await BufferCurrentAsync(cancellationToken).ConfigureAwait(false);
      _noIo = true;
      try
      {
        return GetString();
      }
      finally
      {
        _noIo = false;
      }
    }

    /// <summary>
    /// <see cref="GetBytes"/>, reading the stream asynchronously: a bin, a bin of unknown length (all its chunks) or nil (null).
    /// </summary>
    public async Task<byte[]> GetBytesAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
      ThrowIfDisposed();
      await BufferCurrentAsync(cancellationToken).ConfigureAwait(false);
      _noIo = true;
      try
      {
        return GetBytes();
      }
      finally
      {
        _noIo = false;
      }
    }

    /// <summary>
    /// The current value is buffered: all of an array, map, str or bin that starts at the current token, or the data of a str, bin or extension (also for LsMsgPack.Data, before <see cref="GetWhole"/>).
    /// </summary>
    internal Task BufferCurrentAsync(CancellationToken cancellationToken)
    {
      if (IsStart(_token))
      {
        Frame frame = _frames[_depth - 1];
        return BufferValuesAsync(frame.Unknown ? 0 : frame.Count, frame.Unknown, cancellationToken);
      }
      if (_payloadPending && _length <= MaxBufferLength - (_pos - _tokenStart)) // a longer one is refused by the synchronous step
        return TryEnsureAsync((int)_length, cancellationToken);
      return Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="ReadObject{T}"/>, reading the stream asynchronously: the next value as <see cref="LsMsgPackSerializer.Deserialize{T}(byte[])"/> reads it, with its schema
    /// (or in the body of a head, with the head's schema). The value is buffered whole before it is read.
    /// </summary>
    /// <typeparam name="T">The type to read the value as. In the body of a head, the type of the place in the root type (e.g. the element type of the list).</typeparam>
    public Task<T> ReadObjectAsync<T>(CancellationToken cancellationToken = default(CancellationToken))
    {
      try
      {
        if (TryReadObjectBuffered(typeof(T), out object read))
          return Task.FromResult(read is null ? default(T) : (T)read);
      }
      catch (Exception ex) // as an async method
      {
        return Task.FromException<T>(ex);
      }
      return ReadObjectSlowAsync<T>(cancellationToken);
    }

    private async Task<T> ReadObjectSlowAsync<T>(CancellationToken cancellationToken)
    {
      object result = await ReadObjectSlowAsync(typeof(T), cancellationToken).ConfigureAwait(false);
      return result is null ? default(T) : (T)result;
    }

    /// <summary>
    /// <see cref="ReadObjectAsync{T}"/> for a type known at runtime.
    /// </summary>
    public Task<object> ReadObjectAsync(Type type, CancellationToken cancellationToken = default(CancellationToken))
    {
      try
      {
        if (type != null && TryReadObjectBuffered(type, out object read))
          return Task.FromResult(read);
      }
      catch (Exception ex) // as an async method
      {
        return Task.FromException<object>(ex);
      }
      return ReadObjectSlowAsync(type, cancellationToken);
    }

    /// <summary>
    /// <see cref="ReadObject(Type)"/> of an element of an array or map that is all in the buffer without escape sequences (one value, no schema before it), without the async steps:
    /// its end is found once (<see cref="BufferedEnd"/>) and passed on to ReadObject (<see cref="_passAt"/>), not found again after a <see cref="Scan"/> pass.
    /// </summary>
    /// <returns>false: nothing was read, the async steps buffer it</returns>
    private bool TryReadObjectBuffered(Type type, out object result)
    {
      result = null;
      if (_disposed || _payloadPending || _depth == 0 || AtEndBuffered() != 0 || SchemaFollows())
        return false;
      int end = BufferedEnd(_pos, _maxDepth - _depth, 1);
      if (end < 0)
        return false;
      _passAt = _pos;
      _passEnd = end;
      _noIo = true;
      try
      {
        result = ReadObject(type);
        return true;
      }
      finally
      {
        _noIo = false;
        _passAt = -1;
      }
    }

    private async Task<object> ReadObjectSlowAsync(Type type, CancellationToken cancellationToken)
    {
      if (type is null)
        throw new ArgumentNullException(nameof(type));
      ThrowIfDisposed();
      await PrepareObjectAsync(cancellationToken).ConfigureAwait(false);
      _noIo = true;
      try
      {
        return ReadObject(type);
      }
      finally
      {
        _noIo = false;
      }
    }

    /// <summary>
    /// Buffers what <see cref="ReadObject(Type)"/> reads: the next value, with its schema when it has one.
    /// </summary>
    private Task PrepareObjectAsync(CancellationToken cancellationToken)
    {
      if (!_payloadPending && _depth > 0) // the elements of an array: usually buffered
      {
        int atEnd = AtEndBuffered();
        if (atEnd == 1)
          return Task.CompletedTask; // refused by ReadObject
        if (atEnd == 0)
          return BufferValuesAsync(SchemaFollows() ? 2 : 1, false, cancellationToken);
      }
      return PrepareObjectSlowAsync(cancellationToken);
    }

    /// <summary>
    /// The value at <see cref="_pos"/> (buffered) is an object with its schema (or a reference to it) before it, as <see cref="ReadObject(Type)"/> decides.
    /// </summary>
    private bool SchemaFollows()
    {
      bool body = _depth == 0 ? _headState == HeadState.AwaitingBody : _headState == HeadState.InBody;
      return _options._useInexedSchema && (body ? _head : null) is null && _buf[_pos] != 0xC0;
    }

    private async Task PrepareObjectSlowAsync(CancellationToken cancellationToken)
    {
      await PassPayloadAsync(cancellationToken).ConfigureAwait(false);
      if (_depth > 0)
      {
        if (await AtEndAsync(cancellationToken).ConfigureAwait(false))
          return; // refused by ReadObject
      }
      else
        await SkipStreamValuesAsync(cancellationToken).ConfigureAwait(false);
      if (!await TryEnsureAsync(1, cancellationToken).ConfigureAwait(false))
        return; // ReadObject reports the end of the data

      await BufferValuesAsync(SchemaFollows() ? 2 : 1, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <see cref="ReadHead{TRoot}"/>, reading the stream asynchronously: the schema that <see cref="MsgPackStreamWriter.WriteHead{TRoot}"/> wrote for the root value that follows it.
    /// </summary>
    /// <typeparam name="TRoot">The type of the root value that follows</typeparam>
    public Task ReadHeadAsync<TRoot>(CancellationToken cancellationToken = default(CancellationToken))
    {
      return ReadHeadAsync(typeof(TRoot), cancellationToken);
    }

    /// <summary>
    /// <see cref="ReadHeadAsync{TRoot}"/> for a type known at runtime.
    /// </summary>
    public async Task ReadHeadAsync(Type root, CancellationToken cancellationToken = default(CancellationToken))
    {
      if (root is null)
        throw new ArgumentNullException(nameof(root));
      ThrowIfDisposed();
      await PassPayloadAsync(cancellationToken).ConfigureAwait(false);
      if (_depth == 0 && _headState != HeadState.AwaitingBody && _options._useInexedSchema) // otherwise ReadHead refuses it or reads nothing
      {
        await SkipStreamValuesAsync(cancellationToken).ConfigureAwait(false);
        if (await TryEnsureAsync(1, cancellationToken).ConfigureAwait(false) && _buf[_pos] != 0xC0)
          await BufferValuesAsync(1, false, cancellationToken).ConfigureAwait(false);
      }
      _noIo = true;
      try
      {
        ReadHead(root);
      }
      finally
      {
        _noIo = false;
      }
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// The elements of an array (each read as <see cref="ReadObjectAsync{T}"/> reads it), as they arrive: at its ArrayStart, or before it (then it is read first). The reader is at its ArrayEnd afterwards.
    /// E.g. after <see cref="ReadHeadAsync{TRoot}"/> of a <c>List&lt;T&gt;</c>, the rows of a table of unknown length.
    /// </summary>
    /// <typeparam name="T">The type of the elements (in the body of a head, the element type of the root type)</typeparam>
    /// <exception cref="InvalidOperationException">The value is not an array</exception>
    public async IAsyncEnumerable<T> ReadElementsAsync<T>([EnumeratorCancellation] CancellationToken cancellationToken = default(CancellationToken))
    {
      ThrowIfDisposed();
      if (_token != MsgPackTokenType.ArrayStart)
      {
        if (!await ReadAsync(cancellationToken).ConfigureAwait(false))
          throw EndOfData();
        if (_token != MsgPackTokenType.ArrayStart)
          throw new InvalidOperationException($"The value is not an array ({_token}).");
      }
      while (!await AtEndAsync(cancellationToken).ConfigureAwait(false))
      {
        if (!TryReadObjectBuffered(typeof(T), out object element))
        {
          await PrepareObjectAsync(cancellationToken).ConfigureAwait(false);
          _noIo = true;
          try
          {
            element = ReadObject(typeof(T));
          }
          finally
          {
            _noIo = false;
          }
        }
        yield return element is null ? default(T) : (T)element;
      }
      await ReadAsync(cancellationToken).ConfigureAwait(false); // the ArrayEnd
    }
#endif

    /// <summary>
    /// Reads ahead until the buffer holds, from <see cref="_pos"/>, <paramref name="values"/> whole values, or with <paramref name="unknown"/> the rest of the open value of unknown length (up to and with its end).
    /// Stops early at data that is not valid or at the end of the stream: the synchronous step refuses the data there.
    /// </summary>
    private Task BufferValuesAsync(long values, bool unknown, CancellationToken cancellationToken)
    {
      if (_scan is null)
        _scan = new long[8];
      _scan[0] = unknown ? -1 : values;
      _scanDepth = 1;
      _scanAt = 0;
      int need = Scan();
      return need == 0 ? Task.CompletedTask : BufferMoreAsync(need, cancellationToken); // the value is usually buffered already
    }

    private async Task BufferMoreAsync(int need, CancellationToken cancellationToken)
    {
      do
      {
        if (!await TryEnsureAsync(need, cancellationToken).ConfigureAwait(false))
          return;
      }
      while ((need = Scan()) > 0);
    }

    /// <summary>
    /// Passes the buffered values of <see cref="BufferValuesAsync"/> from <see cref="_scanAt"/>.
    /// </summary>
    /// <returns>0 when the values are buffered (or at data that is not valid), otherwise the bytes from <see cref="_pos"/> that must be buffered to go on</returns>
    private int Scan()
    {
      long[] open = _scan;
      int depth = _scanDepth;
      int at = _scanAt;
      int buffered = _end - _pos;
      byte[] buf = _buf;
      int pos = _pos;
      byte[] kinds = ScanKinds;
      byte[] headers = TokenLengths;
      while (true)
      {
        int top = depth - 1;
        if (open[top] == 0) // all values of an array or map of known length: it is a value of the one around it
        {
          if (top == 0)
            return 0;
          depth--;
          if (open[top - 1] > 0)
            open[top - 1]--;
          continue;
        }

        if (buffered <= at)
          return More(depth, at, at + 1);
        byte b = buf[pos + at];
        int token = headers[b];
        if (buffered < at + token)
          return More(depth, at, at + token);

        long size; // with the data of a str, bin or extension
        long count; // of an array or map
        switch (kinds[b])
        {
          case KindFixStr: size = 1 + (b & 0x1F); break;
          case KindStr8: size = 2 + buf[pos + at + 1]; break;
          case KindStr16: size = 3 + U16(pos + at + 1); break;
          case KindStr32: size = 5L + U32(pos + at + 1); break;
          case KindExt8: size = 3 + buf[pos + at + 1]; break;
          case KindExt16: size = 4 + U16(pos + at + 1); break;
          case KindExt32: size = 6L + U32(pos + at + 1); break;
          case KindFixArray: count = b & 0x0F; goto Container;
          case KindFixMap: count = 2 * (b & 0x0F); goto Container;
          case KindArray16: count = U16(pos + at + 1); goto Container;
          case KindArray32: count = U32(pos + at + 1); goto Container;
          case KindMap16: count = 2L * U16(pos + at + 1); goto Container;
          case KindMap32: count = 2L * U32(pos + at + 1); goto Container;
          case KindEscape:
            byte code = buf[pos + at + 1];
            at += 2;
            if (code == Escape.End)
            {
              if (open[top] >= 0 || top == 0)
                return 0; // not in a value of unknown length, or the end of the open one
              depth--;
              if (open[top - 1] > 0)
                open[top - 1]--;
              continue;
            }
            if ((code != Escape.ArrayStart && code != Escape.MapStart && code != Escape.StringStart && code != Escape.BinaryStart) || _depth + depth > _maxDepth)
              return 0; // reserved, or nested too deep
            open = Push(depth++, -1);
            continue;
          default: size = token; break; // the whole token
        }

        if (size > MaxBufferLength)
          return 0; // the synchronous step refuses a value longer than an array can hold
        if (at + size > MaxBufferLength)
          throw new MsgPackException("A value is longer than a .NET array can hold.", _base + _pos);
        if (buffered < at + size)
          return More(depth, at, (int)(at + size));
        at += (int)size;
        if (open[top] > 0)
          open[top]--;
        continue;

      Container:
        if (_depth + depth > _maxDepth)
          return 0;
        at += token;
        open = Push(depth++, count);
      }
    }

    private int More(int depth, int at, int need)
    {
      _scanDepth = depth;
      _scanAt = at;
      return need;
    }

    private long[] Push(int depth, long count)
    {
      if (depth == _scan.Length)
        Array.Resize(ref _scan, depth * 2);
      _scan[depth] = count;
      return _scan;
    }

    private const byte KindFixStr = 0x20, KindStr8 = 0x21, KindStr16 = 0x22, KindStr32 = 0x23, KindExt8 = 0x24, KindExt16 = 0x25, KindExt32 = 0x26,
      KindFixArray = 0x27, KindFixMap = 0x28, KindArray16 = 0x29, KindArray32 = 0x2A, KindMap16 = 0x2B, KindMap32 = 0x2C, KindEscape = 0x2D;

    /// <summary>
    /// Per first byte of a token: one of the kinds above, otherwise a value of a fixed size (<see cref="TokenLengths"/>).
    /// </summary>
    private static readonly byte[] ScanKinds = MakeScanKinds();

    /// <summary>
    /// Per first byte: the bytes of the token without the data of a str, bin or extension of variable length (what <see cref="ReadToken"/> needs of it, the data of a fixext too; for an escape the code).
    /// </summary>
    private static readonly byte[] TokenLengths = MakeTokenLengths();

    private static byte[] MakeScanKinds()
    {
      byte[] kinds = new byte[256];
      for (int b = 0x80; b <= 0x8F; b++)
        kinds[b] = KindFixMap;
      for (int b = 0x90; b <= 0x9F; b++)
        kinds[b] = KindFixArray;
      for (int b = 0xA0; b <= 0xBF; b++)
        kinds[b] = KindFixStr;
      kinds[0xD9] = kinds[0xC4] = KindStr8;
      kinds[0xDA] = kinds[0xC5] = KindStr16;
      kinds[0xDB] = kinds[0xC6] = KindStr32;
      kinds[0xC7] = KindExt8;
      kinds[0xC8] = KindExt16;
      kinds[0xC9] = KindExt32;
      kinds[0xDC] = KindArray16;
      kinds[0xDD] = KindArray32;
      kinds[0xDE] = KindMap16;
      kinds[0xDF] = KindMap32;
      kinds[Escape.Byte] = KindEscape;
      return kinds;
    }

    private static byte[] MakeTokenLengths()
    {
      byte[] lengths = new byte[256];
      for (int b = 0; b < 256; b++)
        lengths[b] = 1; // fixint, fixmap, fixarray, fixstr, nil, false, true
      lengths[Escape.Byte] = 2;
      lengths[0xCC] = lengths[0xD0] = lengths[0xD9] = lengths[0xC4] = 2;
      lengths[0xCD] = lengths[0xD1] = lengths[0xDA] = lengths[0xC5] = lengths[0xDC] = lengths[0xDE] = lengths[0xC7] = 3;
      lengths[0xC8] = 4;
      lengths[0xCE] = lengths[0xD2] = lengths[0xCA] = lengths[0xDB] = lengths[0xC6] = lengths[0xDD] = lengths[0xDF] = 5;
      lengths[0xC9] = 6;
      lengths[0xCF] = lengths[0xD3] = lengths[0xCB] = 9;
      lengths[0xD4] = 3; // fixext: the type and the data too
      lengths[0xD5] = 4;
      lengths[0xD6] = 6;
      lengths[0xD7] = 10;
      lengths[0xD8] = 18;
      return lengths;
    }

    #region Buffer

    /// <summary>
    /// <see cref="TryEnsure"/>, reading asynchronously.
    /// </summary>
    private async Task<bool> TryEnsureAsync(int count, CancellationToken cancellationToken)
    {
      while (_end - _pos < count)
      {
        if (_eof)
          return false;
        if (_end == _buf.Length)
          MakeRoom();
        int read = await _source.ReadAsync(_buf, _end, _buf.Length - _end, cancellationToken).ConfigureAwait(false);
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
    /// <see cref="PassPayload"/>, reading asynchronously (no value is read whole between the async steps: the data that is not buffered is dropped).
    /// </summary>
    private async Task PassPayloadAsync(CancellationToken cancellationToken)
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

      length -= _end - _pos;
      _base += _end;
      _pos = 0;
      _end = 0;
      _tokenStart = 0;
      while (length > 0)
      {
        int read = await _source.ReadAsync(_buf, 0, (int)Math.Min(_buf.Length, length), cancellationToken).ConfigureAwait(false);
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
    /// <see cref="SkipStreamValues"/>, reading asynchronously.
    /// </summary>
    private async Task SkipStreamValuesAsync(CancellationToken cancellationToken)
    {
      while (await TryEnsureAsync(1, cancellationToken).ConfigureAwait(false))
      {
        int header = IndexContent.ExtensionHeaderLength(_buf[_pos]);
        if (header == 0 || !await TryEnsureAsync(header, cancellationToken).ConfigureAwait(false))
          return;
        sbyte type = unchecked((sbyte)_buf[_pos + header - 1]);
        if (type != MsgPackStreamIndex.IndexExtensionType && type != MsgPackStreamIndex.SidecarExtensionType)
          return;

        _tokenStart = _pos;
        _length = ExtensionLength(_pos, header);
        _pos += header;
        _payloadPending = _length > 0;
        await PassPayloadAsync(cancellationToken).ConfigureAwait(false);
        _length = 0;
        if (type == MsgPackStreamIndex.IndexExtensionType)
        {
          if (!await TryEnsureAsync(MsgPackStreamIndex.TailLength, cancellationToken).ConfigureAwait(false) || !IndexContent.TryParseTail(_buf, _pos, out IndexContent.Tail unused))
            throw new MsgPackException("An index (extension type 3) of the stream is not followed by its tail.", _base + _pos);
          _pos += MsgPackStreamIndex.TailLength;
        }
        _tokenStart = _pos;
      }
    }

    /// <summary>
    /// <see cref="PastStreamValues"/>, reading asynchronously.
    /// </summary>
    private async Task<int> PastStreamValuesAsync(int at, CancellationToken cancellationToken)
    {
      while (await TryEnsureAsync(at + 1, cancellationToken).ConfigureAwait(false))
      {
        int header = IndexContent.ExtensionHeaderLength(_buf[_pos + at]);
        if (header == 0 || !await TryEnsureAsync(at + header, cancellationToken).ConfigureAwait(false))
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

    private static InvalidOperationException NotBuffered()
    {
      return new InvalidOperationException($"{nameof(MsgPackStreamReader)} would read the stream synchronously in an async method: the data it reads ahead is incomplete (a bug).");
    }

    #endregion

    /// <summary>
    /// Called by <see cref="ReadHead(Type)"/> with the bytes of the head's schema (inline or a reference), after it was read: the HTTP client learns it (<see cref="Http.LsMsgPackHttpSerializer.CreateStreamReader"/>).
    /// </summary>
    internal Action<byte[], int, int> HeadSchemaRead;
  }
}
