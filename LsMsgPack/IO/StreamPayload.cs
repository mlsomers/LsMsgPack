using LsMsgPack;
using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPack.IO
{
  /// <summary>
  /// Copies exactly one payload from a stream that cannot be read in place (e.g. a network stream) into an array of the shared pool: the stream is left after it, as LsMsgPack does.
  /// <para>A stream cannot be peeked, so each read asks for no more than the value still needs at least (<see cref="Scan"/>: the rest of the current item, plus a byte per item still to come),
  /// never past its end, and the reads get larger as the headers tell more. A claimed length grows the buffer only by <see cref="Chunk"/> ahead of the bytes that arrived.</para>
  /// <para>The reader copies everything it keeps, so nothing read refers to the array; <see cref="Release"/> clears it before it goes back (see LsMsgPackSerializer.DeserializeCopy).</para>
  /// </summary>
  internal sealed class StreamPayload
  {
    private const int Chunk = 81920;
    private const int MaxArrayLength = 0x7FFFFFC7; // Array.MaxLength (not in .NET Standard)

    /// <summary>
    /// The bytes read: the payload, from 0 to <see cref="Length"/>, once <see cref="Scan"/> returns 0.
    /// </summary>
    internal byte[] Buffer;
    internal int Length;

    private readonly bool _littleEndian;
    private readonly bool _indexed;
    private readonly int _maxDepth;

    private int _pos;        // where the scan goes on
    private long _data;      // bytes of the current item still to pass (str, bin, ext, numbers)
    private long[] _counts;  // per level the items still to come: level 0 is the payload (one value, two with the indexed schema), each array or map adds one
    private int _depth;
    private long _pending;   // the sum of _counts
    private bool _first = true;

    internal StreamPayload(LsMsgPackOptions options)
    {
      _littleEndian = BitConverter.IsLittleEndian != MsgPackOptions.SwapEndianChoice(options, 2);
      _indexed = options._useIndexedSchema;
      _maxDepth = options._maxDepth;
      Buffer = ArrayPool<byte>.Shared.Rent(256);
      _counts = new long[8];
      _counts[0] = 1;
      _depth = 1;
      _pending = 1;
    }

    internal void ReadFrom(Stream source)
    {
      long need;
      while ((need = Scan()) > 0)
      {
        int count = Reserve(need);
        int read = source.Read(Buffer, Length, count); // may return fewer bytes than asked (network streams)
        if (read <= 0)
          throw MsgPackReader.EndOfData();
        Length += read;
      }
    }

    /// <summary>
    /// <see cref="ReadFrom"/> with <c>ReadAsync</c>: keep the two equal.
    /// </summary>
    internal async Task ReadFromAsync(Stream source, CancellationToken cancellationToken)
    {
      long need;
      while ((need = Scan()) > 0)
      {
        int count = Reserve(need);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        int read = await source.ReadAsync(new Memory<byte>(Buffer, Length, count), cancellationToken).ConfigureAwait(false);
#else
        int read = await source.ReadAsync(Buffer, Length, count, cancellationToken).ConfigureAwait(false);
#endif
        if (read <= 0)
          throw MsgPackReader.EndOfData();
        Length += read;
      }
    }

    /// <summary>
    /// Clears the bytes read and returns the array to the pool.
    /// </summary>
    internal void Release()
    {
      byte[] buffer = Buffer;
      if (buffer is null)
        return;
      Buffer = null;
      new Span<byte>(buffer, 0, Length).Clear();
      ArrayPool<byte>.Shared.Return(buffer);
    }

    /// <summary>
    /// Makes room for the next read and returns its size: what the value still needs at least, up to the free space or <see cref="Chunk"/>.
    /// </summary>
    private int Reserve(long need)
    {
      int free = Buffer.Length - Length;
      long count = Math.Min(need, Math.Max(free, Chunk));
      if (count > free)
      {
        if (count > MaxArrayLength - Length)
          throw new MsgPackException($"The value is larger than an array can hold ({MaxArrayLength} bytes).");
        long size = Math.Max(2L * Buffer.Length, Length + count);
        byte[] larger = ArrayPool<byte>.Shared.Rent((int)Math.Min(size, MaxArrayLength));
        new ReadOnlySpan<byte>(Buffer, 0, Length).CopyTo(larger);
        new Span<byte>(Buffer, 0, Length).Clear();
        ArrayPool<byte>.Shared.Return(Buffer);
        Buffer = larger;
      }
      return (int)count;
    }

    /// <summary>
    /// Passes what has arrived, as far as it goes.
    /// </summary>
    /// <returns>0 when the payload is complete, otherwise the number of bytes it needs at least</returns>
    private long Scan()
    {
      byte[] buf = Buffer;
      int end = Length;
      int pos = _pos;
      while (true)
      {
        if (_data > 0)
        {
          int available = end - pos;
          if (available < _data)
          {
            _data -= available;
            _pos = end;
            return _data + _pending;
          }
          pos += (int)_data;
          _data = 0;
        }

        while (_depth > 0 && _counts[_depth - 1] == 0)
          _depth--;
        if (_depth == 0)
        {
          _pos = pos;
          return 0;
        }

        if (pos >= end)
        {
          _pos = pos;
          return _pending; // this item and the ones after it: at least a byte each
        }
        int c = buf[pos];
        int header = HeaderLength(c);
        if (end - pos < header)
        {
          _pos = pos;
          return header - (end - pos) + _pending - 1;
        }

        _counts[_depth - 1]--;
        _pending--;
        if (_first)
        {
          _first = false;
          if (_indexed && c != 0xC0) // the schema (or a reference to it), then the value
          {
            _counts[0]++;
            _pending++;
          }
        }
        pos++;

        if (c <= 0x7F || c >= 0xE0)
          continue;
        if ((c & 0xE0) == 0xA0) { _data = c & 0x1F; continue; }
        if ((c & 0xF0) == 0x90) { Push(c & 0x0F); continue; }
        if ((c & 0xF0) == 0x80) { Push(2L * (c & 0x0F)); continue; }
        switch (c)
        {
          case 0xC0: case 0xC2: case 0xC3: break;
          case 0xCC: case 0xD0: _data = 1; break;
          case 0xCD: case 0xD1: _data = 2; break;
          case 0xCE: case 0xD2: case 0xCA: _data = 4; break;
          case 0xCF: case 0xD3: case 0xCB: _data = 8; break;
          case 0xD4: _data = 2; break;
          case 0xD5: _data = 3; break;
          case 0xD6: _data = 5; break;
          case 0xD7: _data = 9; break;
          case 0xD8: _data = 17; break;
          case 0xD9: case 0xC4: _data = Size(buf, pos, 1); break;
          case 0xDA: case 0xC5: _data = Size(buf, pos, 2); break;
          case 0xDB: case 0xC6: _data = Size(buf, pos, 4); break;
          case 0xC7: _data = 1 + Size(buf, pos, 1); break;
          case 0xC8: _data = 1 + Size(buf, pos, 2); break;
          case 0xC9: _data = 1 + Size(buf, pos, 4); break;
          case 0xDC: Push(Size(buf, pos, 2)); break;
          case 0xDD: Push(Size(buf, pos, 4)); break;
          case 0xDE: Push(2 * Size(buf, pos, 2)); break;
          case 0xDF: Push(2 * Size(buf, pos, 4)); break;
          default: throw LsMsgPack.Streaming.Escape.NotSupported(pos - 1);
        }
        pos += header - 1;
      }
    }

    /// <summary>
    /// The bytes of an item's header: its first byte and the size of its length.
    /// </summary>
    private static int HeaderLength(int c)
    {
      switch (c)
      {
        case 0xC4: case 0xC7: case 0xD9: return 2;
        case 0xC5: case 0xC8: case 0xDA: case 0xDC: case 0xDE: return 3;
        case 0xC6: case 0xC9: case 0xDB: case 0xDD: case 0xDF: return 5;
        default: return 1;
      }
    }

    private long Size(byte[] buf, int at, int size)
    {
      long value = 0;
      for (int t = 0; t < size; t++)
        value = (value << 8) | buf[at + (_littleEndian ? size - 1 - t : t)];
      return value;
    }

    /// <summary>
    /// The items of an array or map that was just passed (at level <c>_depth - 1</c>).
    /// </summary>
    private void Push(long count)
    {
      if (_depth - 1 >= _maxDepth)
        throw new MsgPackException($"The data is nested deeper than {nameof(LsMsgPackOptions)}.{nameof(LsMsgPackOptions.MaxDepth)} ({_maxDepth}).");
      if (_depth == _counts.Length)
        Array.Resize(ref _counts, 2 * _counts.Length);
      _counts[_depth++] = count;
      _pending += count;
    }
  }
}
