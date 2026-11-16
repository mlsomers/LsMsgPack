using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPack.IO
{
  /// <summary>
  /// The payload of LsMsgPackSerializer.SerializeAsync in an array of the shared pool (the serializer's <c>BufferSink</c> asks one span of the whole length), written to the stream with one <c>WriteAsync</c>.
  /// Cleared before it goes back, so the data does not linger in the pool.
  /// </summary>
  internal sealed class PooledPayload : IBufferWriter<byte>
  {
    private const int MaxArrayLength = 0x7FFFFFC7; // Array.MaxLength (not in .NET Standard)

    private byte[] _buffer;
    private int _length;

    public void Advance(int count)
    {
      if (count < 0 || count > _buffer.Length - _length)
        throw new ArgumentOutOfRangeException(nameof(count));
      _length += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
      Ensure(sizeHint);
      return new Memory<byte>(_buffer, _length, _buffer.Length - _length);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
      Ensure(sizeHint);
      return new Span<byte>(_buffer, _length, _buffer.Length - _length);
    }

    private void Ensure(int sizeHint)
    {
      if (sizeHint < 1)
        sizeHint = 1;
      if (_buffer is null)
      {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(sizeHint, 256));
        return;
      }
      if (_buffer.Length - _length >= sizeHint)
        return;
      if (sizeHint > MaxArrayLength - _length)
        throw new OutOfMemoryException($"The payload is larger than an array can hold ({MaxArrayLength} bytes).");
      byte[] larger = ArrayPool<byte>.Shared.Rent((int)Math.Min(Math.Max(2L * _buffer.Length, (long)_length + sizeHint), MaxArrayLength));
      new ReadOnlySpan<byte>(_buffer, 0, _length).CopyTo(larger);
      Return(_buffer, _length);
      _buffer = larger;
    }

    internal async Task WriteToAsync(Stream target, CancellationToken cancellationToken)
    {
      try
      {
        if (_length > 0)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
          await target.WriteAsync(new ReadOnlyMemory<byte>(_buffer, 0, _length), cancellationToken).ConfigureAwait(false);
#else
          await target.WriteAsync(_buffer, 0, _length, cancellationToken).ConfigureAwait(false);
#endif
        }
      }
      finally
      {
        Release();
      }
    }

    internal void Release()
    {
      byte[] buffer = _buffer;
      if (buffer is null)
        return;
      _buffer = null;
      Return(buffer, _length);
    }

    private static void Return(byte[] buffer, int length)
    {
      new Span<byte>(buffer, 0, length).Clear();
      ArrayPool<byte>.Shared.Return(buffer);
    }
  }
}
