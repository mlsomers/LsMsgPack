using System;
using System.IO;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Growable byte buffer that the items of a tree are written to (see <see cref="MsgPackItem.WriteTo"/>).
  /// <para>Containers write their items into the same buffer instead of concatenating the bytes of each nested item at every level.</para>
  /// </summary>
  internal sealed class ByteWriter
  {
    private byte[] _buffer;
    private int _length;

    public ByteWriter(int capacity = 256)
    {
      _buffer = new byte[capacity];
    }

    public void Write(byte value)
    {
      if (_length == _buffer.Length)
        Grow(1);
      _buffer[_length++] = value;
    }

    public void Write(byte[] bytes)
    {
      Write(bytes, 0, bytes.Length);
    }

    public void Write(byte[] bytes, int offset, int count)
    {
      if (_length + count > _buffer.Length)
        Grow(count);
      Buffer.BlockCopy(bytes, offset, _buffer, _length, count);
      _length += count;
    }

    /// <summary>
    /// Writes the lowest <paramref name="size"/> bytes of the value in the same order as <see cref="BitConverter.GetBytes(ulong)"/> (of a type with that size) followed by <see cref="MsgPackItem.SwapEndianChoice"/> would.
    /// </summary>
    public void WriteEndian(ulong value, int size, MsgPackSettings settings)
    {
      if (_length + size > _buffer.Length)
        Grow(size);

      if (BitConverter.IsLittleEndian != MsgPackItem.SwapEndianChoice(settings, size))
      {
        for (int t = 0; t < size; t++)
          _buffer[_length++] = (byte)(value >> (t * 8));
      }
      else
      {
        for (int t = size - 1; t >= 0; t--)
          _buffer[_length++] = (byte)(value >> (t * 8));
      }
    }

    /// <param name="byteCount">The result of <see cref="System.Text.Encoding.GetByteCount(string)"/> (it is needed for the header anyway)</param>
    public void Write(string value, System.Text.Encoding encoding, int byteCount)
    {
      if (_length + byteCount > _buffer.Length)
        Grow(byteCount);
      _length += encoding.GetBytes(value, 0, value.Length, _buffer, _length);
    }

    private void Grow(int extra)
    {
      Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + extra));
    }

    public byte[] ToArray()
    {
      byte[] result = new byte[_length];
      Buffer.BlockCopy(_buffer, 0, result, 0, _length);
      return result;
    }

    public void CopyTo(Stream target)
    {
      target.Write(_buffer, 0, _length);
    }
  }
}
