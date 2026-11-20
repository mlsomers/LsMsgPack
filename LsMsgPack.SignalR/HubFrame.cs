using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace LsMsgPack.SignalR
{
  /// <summary>
  /// The buffer one message is written into: the envelope by the methods here, the arguments by the serializer (<see cref="IBufferWriter{T}"/>).
  /// The first <see cref="PrefixRoom"/> bytes are kept free for the length prefix, which is only known at the end (<see cref="Framed"/>), so the message is not copied to make room for it.
  /// <para>One per thread, taken while a message is written (<see cref="Rent"/>/<see cref="Return"/>): a nested write gets its own.</para>
  /// </summary>
  internal sealed class HubFrame : IBufferWriter<byte>
  {
    /// <summary>
    /// The longest length prefix: 7 bits per byte, a message is at most 2 GB (as Microsoft's BinaryMessageParser).
    /// </summary>
    internal const int PrefixRoom = 5;

    /// <summary>
    /// A buffer that grew beyond this is not kept for the thread's next message (one large message should not hold its memory for good).
    /// </summary>
    private const int KeepAtMost = 1024 * 1024;

    internal byte[] Buf;
    internal int Pos;

    [ThreadStatic]
    private static HubFrame _thread;

    private HubFrame(int capacity)
    {
      Buf = new byte[capacity];
    }

    internal static HubFrame Rent()
    {
      HubFrame frame = _thread;
      if (frame is null)
        frame = new HubFrame(512);
      else
        _thread = null;
      frame.Pos = PrefixRoom;
      return frame;
    }

    internal static void Return(HubFrame frame)
    {
      if (frame.Buf.Length > KeepAtMost)
        return;
      _thread = frame;
    }

    /// <summary>
    /// Writes the length prefix in front of the message: the framed message is <c>Buf[start..Pos]</c>.
    /// </summary>
    internal int Framed()
    {
      int length = Pos - PrefixRoom;
      int start = PrefixRoom - PrefixLength(length);
      WritePrefix(length, Buf, start);
      return start;
    }

    #region Length prefix (VarInt: 7 bits per byte, the lowest first, the high bit set when more follow)

    internal static int PrefixLength(long length)
    {
      int count = 0;
      do
      {
        length >>= 7;
        count++;
      }
      while (length > 0);
      return count;
    }

    internal static int WritePrefix(long length, byte[] target, int at)
    {
      int count = 0;
      do
      {
        byte current = (byte)(length & 0x7F);
        length >>= 7;
        if (length > 0)
          current |= 0x80;
        target[at + count] = current;
        count++;
      }
      while (length > 0);
      return count;
    }

    /// <summary>
    /// Takes the next whole message from <paramref name="input"/> (Microsoft's BinaryMessageParser: the same checks and errors).
    /// </summary>
    /// <returns>False when the message (or its prefix) has not arrived completely, <paramref name="input"/> is then unchanged</returns>
    internal static bool TryRead(ref ReadOnlySequence<byte> input, out ReadOnlySequence<byte> payload)
    {
      payload = default(ReadOnlySequence<byte>);
      if (input.IsEmpty)
        return false;

      int available = (int)Math.Min(PrefixRoom, input.Length);
      uint length;
      int count;
      byte read;
      ReadOnlySpan<byte> first = input.First.Span;
      if (first.Length >= available)
        read = ReadPrefix(first, available, out length, out count);
      else // the prefix spans segments
      {
        Span<byte> copied = stackalloc byte[PrefixRoom];
        input.Slice(0, available).CopyTo(copied);
        read = ReadPrefix(copied, available, out length, out count);
      }

      if ((read & 0x80) != 0 && count < PrefixRoom)
        return false; // more of the prefix is coming
      if ((read & 0x80) != 0 || (count == PrefixRoom && read > 7))
        throw new FormatException("Messages over 2GB in size are not supported.");
      if (input.Length < length + count)
        return false;

      payload = input.Slice(count, length);
      input = input.Slice(count + length);
      return true;
    }

    /// <returns>The last byte of the prefix read</returns>
    private static byte ReadPrefix(ReadOnlySpan<byte> span, int available, out uint length, out int count)
    {
      length = 0;
      count = 0;
      byte read;
      do
      {
        read = span[count];
        length |= ((uint)(read & 0x7F)) << (count * 7);
        count++;
      }
      while (count < available && (read & 0x80) != 0);
      return read;
    }

    #endregion

    #region IBufferWriter (the arguments, written by the serializer)

    public void Advance(int count)
    {
      if (count < 0 || Pos + count > Buf.Length)
        throw new ArgumentOutOfRangeException(nameof(count));
      Pos += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
      Ensure(Math.Max(sizeHint, 1));
      return new Memory<byte>(Buf, Pos, Buf.Length - Pos);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
      Ensure(Math.Max(sizeHint, 1));
      return new Span<byte>(Buf, Pos, Buf.Length - Pos);
    }

    #endregion

    #region The envelope (the smallest encodings, as MessagePack-CSharp's writer: the bytes of Microsoft's MessagePack protocol)

    internal void Ensure(int count)
    {
      if (Buf.Length - Pos >= count)
        return;
      long wanted = Math.Max((long)Buf.Length * 2, (long)Pos + count);
      if (wanted > int.MaxValue - 64)
      {
        if ((long)Pos + count > int.MaxValue - 64)
          throw new InvalidDataException("A message over 2GB in size cannot be written.");
        wanted = int.MaxValue - 64;
      }
      byte[] grown = new byte[wanted];
      Buffer.BlockCopy(Buf, 0, grown, 0, Pos);
      Buf = grown;
    }

    internal void Nil()
    {
      Ensure(1);
      Buf[Pos++] = 0xC0;
    }

    internal void Bool(bool value)
    {
      Ensure(1);
      Buf[Pos++] = value ? (byte)0xC3 : (byte)0xC2;
    }

    internal void ArrayHeader(int count)
    {
      Ensure(5);
      if (count < 16)
        Buf[Pos++] = (byte)(0x90 | count);
      else if (count <= ushort.MaxValue)
      {
        Buf[Pos++] = 0xDC;
        BigEndian16(count);
      }
      else
      {
        Buf[Pos++] = 0xDD;
        BigEndian32((uint)count);
      }
    }

    internal void MapHeader(int count)
    {
      Ensure(5);
      if (count < 16)
        Buf[Pos++] = (byte)(0x80 | count);
      else if (count <= ushort.MaxValue)
      {
        Buf[Pos++] = 0xDE;
        BigEndian16(count);
      }
      else
      {
        Buf[Pos++] = 0xDF;
        BigEndian32((uint)count);
      }
    }

    internal void Integer(long value)
    {
      Ensure(9);
      if (value >= 0)
      {
        if (value <= 0x7F)
          Buf[Pos++] = (byte)value;
        else if (value <= byte.MaxValue)
        {
          Buf[Pos++] = 0xCC;
          Buf[Pos++] = (byte)value;
        }
        else if (value <= ushort.MaxValue)
        {
          Buf[Pos++] = 0xCD;
          BigEndian16((int)value);
        }
        else if (value <= uint.MaxValue)
        {
          Buf[Pos++] = 0xCE;
          BigEndian32((uint)value);
        }
        else
        {
          Buf[Pos++] = 0xCF;
          BigEndian64((ulong)value);
        }
      }
      else if (value >= -32)
        Buf[Pos++] = (byte)value; // negative fixint
      else if (value >= sbyte.MinValue)
      {
        Buf[Pos++] = 0xD0;
        Buf[Pos++] = (byte)value;
      }
      else if (value >= short.MinValue)
      {
        Buf[Pos++] = 0xD1;
        BigEndian16((int)value);
      }
      else if (value >= int.MinValue)
      {
        Buf[Pos++] = 0xD2;
        BigEndian32((uint)value);
      }
      else
      {
        Buf[Pos++] = 0xD3;
        BigEndian64((ulong)value);
      }
    }

    /// <summary>
    /// A string in UTF-8 whatever the serializer's options say (the envelope is the protocol's), nil for null.
    /// </summary>
    internal void String(string value)
    {
      if (value is null)
      {
        Nil();
        return;
      }
      int byteCount = Encoding.UTF8.GetByteCount(value);
      Ensure(byteCount + 5);
      if (byteCount < 32)
        Buf[Pos++] = (byte)(0xA0 | byteCount);
      else if (byteCount <= byte.MaxValue)
      {
        Buf[Pos++] = 0xD9;
        Buf[Pos++] = (byte)byteCount;
      }
      else if (byteCount <= ushort.MaxValue)
      {
        Buf[Pos++] = 0xDA;
        BigEndian16(byteCount);
      }
      else
      {
        Buf[Pos++] = 0xDB;
        BigEndian32((uint)byteCount);
      }
      Pos += Encoding.UTF8.GetBytes(value, 0, value.Length, Buf, Pos);
    }

    internal void Raw(in ReadOnlySequence<byte> bytes)
    {
      if (bytes.Length > int.MaxValue)
        throw new InvalidDataException("A message over 2GB in size cannot be written.");
      int length = (int)bytes.Length;
      Ensure(length);
      bytes.CopyTo(new Span<byte>(Buf, Pos, length));
      Pos += length;
    }

    private void BigEndian16(int value)
    {
      Buf[Pos++] = (byte)(value >> 8);
      Buf[Pos++] = (byte)value;
    }

    private void BigEndian32(uint value)
    {
      Buf[Pos++] = (byte)(value >> 24);
      Buf[Pos++] = (byte)(value >> 16);
      Buf[Pos++] = (byte)(value >> 8);
      Buf[Pos++] = (byte)value;
    }

    private void BigEndian64(ulong value)
    {
      BigEndian32((uint)(value >> 32));
      BigEndian32((uint)value);
    }

    #endregion
  }
}
