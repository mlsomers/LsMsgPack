using System;
using System.IO;
using System.Text;

namespace LsMsgPack.SignalR
{
  /// <summary>
  /// Reads the envelope of a hub message (Microsoft's MessagePack protocol: an array of the message type, headers, invocation id, ...) from one message in memory.
  /// The errors are those of Microsoft's protocol (<see cref="InvalidDataException"/> naming the field), SignalR closes the connection on them.
  /// </summary>
  internal static class HubEnvelope
  {
    /// <summary>
    /// The message types (<c>HubProtocolConstants</c>), the first element of each message.
    /// </summary>
    internal const int Invocation = 1;
    internal const int StreamItem = 2;
    internal const int Completion = 3;
    internal const int StreamInvocation = 4;
    internal const int CancelInvocation = 5;
    internal const int Ping = 6;
    internal const int Close = 7;
    internal const int Ack = 8;
    internal const int Sequence = 9;

    /// <summary>
    /// The result kinds of a completion message.
    /// </summary>
    internal const int ErrorResult = 1;
    internal const int VoidResult = 2;
    internal const int NonVoidResult = 3;

    internal static int ReadArrayHeader(byte[] buf, ref int pos, int end, string field)
    {
      if (pos < end)
      {
        byte c = buf[pos];
        if ((c & 0xF0) == 0x90)
        {
          pos++;
          return c & 0x0F;
        }
        if (c == 0xDC && end - pos >= 3)
        {
          pos += 3;
          return (buf[pos - 2] << 8) | buf[pos - 1];
        }
        if (c == 0xDD && end - pos >= 5)
        {
          uint count = BigEndian32(buf, pos + 1);
          pos += 5;
          if (count <= int.MaxValue)
            return (int)count;
        }
      }
      throw new InvalidDataException($"Reading array length for '{field}' failed.", Unexpected(buf, pos, end, "an array"));
    }

    internal static int ReadMapHeader(byte[] buf, ref int pos, int end, string field)
    {
      if (pos < end)
      {
        byte c = buf[pos];
        if ((c & 0xF0) == 0x80)
        {
          pos++;
          return c & 0x0F;
        }
        if (c == 0xDE && end - pos >= 3)
        {
          pos += 3;
          return (buf[pos - 2] << 8) | buf[pos - 1];
        }
        if (c == 0xDF && end - pos >= 5)
        {
          uint count = BigEndian32(buf, pos + 1);
          pos += 5;
          if (count <= int.MaxValue)
            return (int)count;
        }
      }
      throw new InvalidDataException($"Reading map length for '{field}' failed.", Unexpected(buf, pos, end, "a map"));
    }

    internal static int ReadInt32(byte[] buf, ref int pos, int end, string field)
    {
      int start = pos;
      if (TryReadInteger(buf, ref pos, end, out long value) && value >= int.MinValue && value <= int.MaxValue)
        return (int)value;
      pos = start;
      throw new InvalidDataException($"Reading '{field}' as Int32 failed.", Unexpected(buf, start, end, "an Int32"));
    }

    internal static long ReadInt64(byte[] buf, ref int pos, int end, string field)
    {
      int start = pos;
      if (TryReadInteger(buf, ref pos, end, out long value))
        return value;
      throw new InvalidDataException($"Reading '{field}' as Int64 failed.", Unexpected(buf, start, end, "an Int64"));
    }

    internal static bool ReadBoolean(byte[] buf, ref int pos, int end, string field)
    {
      if (pos < end)
      {
        byte c = buf[pos];
        if (c == 0xC2 || c == 0xC3)
        {
          pos++;
          return c == 0xC3;
        }
      }
      throw new InvalidDataException($"Reading '{field}' as Boolean failed.", Unexpected(buf, pos, end, "a Boolean"));
    }

    /// <summary>
    /// A string or nil (null).
    /// </summary>
    internal static string ReadString(byte[] buf, ref int pos, int end, string field)
    {
      if (!TryReadStringBytes(buf, ref pos, end, out int start, out int length))
        return null;
      return Encoding.UTF8.GetString(buf, start, length);
    }

    /// <summary>
    /// The bytes of a string (false for nil), so the target of an invocation can be looked up without making a string (<c>IInvocationBinder.GetTarget</c>).
    /// </summary>
    internal static bool TryReadStringBytes(byte[] buf, ref int pos, int end, out int start, out int length)
    {
      start = 0;
      length = 0;
      int at = pos;
      if (at < end)
      {
        byte c = buf[at];
        int count = -1;
        int header = 1;
        if ((c & 0xE0) == 0xA0)
          count = c & 0x1F;
        else if (c == 0xD9 && end - at >= 2)
        {
          count = buf[at + 1];
          header = 2;
        }
        else if (c == 0xDA && end - at >= 3)
        {
          count = (buf[at + 1] << 8) | buf[at + 2];
          header = 3;
        }
        else if (c == 0xDB && end - at >= 5)
        {
          uint big = BigEndian32(buf, at + 1);
          count = big <= int.MaxValue ? (int)big : -1;
          header = 5;
        }
        else if (c == 0xC0)
        {
          pos = at + 1;
          return false;
        }
        if (count >= 0 && end - at - header >= count)
        {
          start = at + header;
          length = count;
          pos = start + count;
          return true;
        }
      }
      throw new InvalidDataException($"Reading the string failed.", Unexpected(buf, at, end, "a string"));
    }

    internal static bool TryReadInteger(byte[] buf, ref int pos, int end, out long value)
    {
      value = 0;
      if (pos >= end)
        return false;
      byte c = buf[pos];
      if (c <= 0x7F)
      {
        value = c;
        pos++;
        return true;
      }
      if (c >= 0xE0)
      {
        value = (sbyte)c;
        pos++;
        return true;
      }
      int available = end - pos - 1;
      switch (c)
      {
        case 0xCC: if (available < 1) return false; value = buf[pos + 1]; pos += 2; return true;
        case 0xCD: if (available < 2) return false; value = (buf[pos + 1] << 8) | buf[pos + 2]; pos += 3; return true;
        case 0xCE: if (available < 4) return false; value = BigEndian32(buf, pos + 1); pos += 5; return true;
        case 0xCF:
          if (available < 8) return false;
          ulong big = ((ulong)BigEndian32(buf, pos + 1) << 32) | BigEndian32(buf, pos + 5);
          if (big > long.MaxValue) return false;
          value = (long)big; pos += 9; return true;
        case 0xD0: if (available < 1) return false; value = (sbyte)buf[pos + 1]; pos += 2; return true;
        case 0xD1: if (available < 2) return false; value = (short)((buf[pos + 1] << 8) | buf[pos + 2]); pos += 3; return true;
        case 0xD2: if (available < 4) return false; value = (int)BigEndian32(buf, pos + 1); pos += 5; return true;
        case 0xD3:
          if (available < 8) return false;
          value = (long)(((ulong)BigEndian32(buf, pos + 1) << 32) | BigEndian32(buf, pos + 5)); pos += 9; return true;
        default: return false;
      }
    }

    private static uint BigEndian32(byte[] buf, int at)
    {
      return ((uint)buf[at] << 24) | ((uint)buf[at + 1] << 16) | ((uint)buf[at + 2] << 8) | buf[at + 3];
    }

    private static Exception Unexpected(byte[] buf, int pos, int end, string expected)
    {
      if (pos >= end)
        return new EndOfStreamException($"The message ended where {expected} was expected.");
      return new InvalidDataException($"Expected {expected}, found the MsgPack code 0x{buf[pos]:X2}.");
    }
  }
}
