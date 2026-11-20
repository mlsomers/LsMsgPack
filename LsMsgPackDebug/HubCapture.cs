using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LsMsgPackDebug
{
  /// <summary>
  /// SignalR's MessagePack hub protocols (Microsoft's "messagepack" and LsMsgPack.SignalR's "lsmsgpack": the same envelope): data captured from a connection is the JSON handshake
  /// (text ending with 0x1E) and messages, each a length prefix (VarInt: 7 bits per byte, the lowest first) and one array <c>[type, ...]</c>.
  /// <para>Read as such with <see cref="MsgPackSettings.ReadHubMessages"/> when the whole data has that shape (<see cref="Recognize(Stream)"/>): otherwise a prefix of 128 or more reads as a
  /// MsgPack header and the rest goes astray.</para>
  /// </summary>
  public static class HubCapture
  {
    /// <summary>
    /// The record separator after each JSON message of the handshake.
    /// </summary>
    public const byte RecordSeparator = 0x1E;

    private const int MaxHandshakeLength = 4096;
    private const int MaxPrefixLength = 5;

    /// <summary>
    /// A message found by <see cref="Recognize(Stream)"/>.
    /// </summary>
    public sealed class Frame
    {
      /// <summary>Where the length prefix starts (or the handshake text).</summary>
      public long Offset;
      /// <summary>The bytes of the prefix (0 for a handshake).</summary>
      public int PrefixLength;
      /// <summary>The bytes of the message after the prefix (of the handshake: its text with the separator).</summary>
      public int Length;
      /// <summary>The message type (1-9), 0 for a handshake.</summary>
      public int Type;
      public string InvocationId;
      public string Target;
      /// <summary>A Completion's result kind (1 error, 2 void, 3 a result), 0 for the other types.</summary>
      public int ResultKind;
      /// <summary>The handshake's JSON (without the separator).</summary>
      public string Text;
    }

    /// <summary>
    /// The name of a message type (HubProtocolConstants), null for an unknown one.
    /// </summary>
    public static string MessageTypeName(int type)
    {
      switch (type)
      {
        case 1: return "Invocation";
        case 2: return "StreamItem";
        case 3: return "Completion";
        case 4: return "StreamInvocation";
        case 5: return "CancelInvocation";
        case 6: return "Ping";
        case 7: return "Close";
        case 8: return "Ack";
        case 9: return "Sequence";
        default: return null;
      }
    }

    /// <summary>
    /// The name of the field at <paramref name="index"/> of a message of the type (Microsoft's MessagePack protocol), null beyond them.
    /// </summary>
    /// <param name="resultKind">Completion: the result kind (1 error, 2 void, 3 a result), which names the fifth field</param>
    public static string FieldName(int type, int index, int resultKind = 0)
    {
      if (index == 0)
        return "type";
      switch (type)
      {
        case 1:
        case 4:
          switch (index) { case 1: return "headers"; case 2: return "invocationId"; case 3: return "target"; case 4: return "arguments"; case 5: return "streamIds"; }
          break;
        case 2:
          switch (index) { case 1: return "headers"; case 2: return "invocationId"; case 3: return "item"; }
          break;
        case 3:
          switch (index) { case 1: return "headers"; case 2: return "invocationId"; case 3: return "resultKind"; case 4: return resultKind == 1 ? "error" : "result"; }
          break;
        case 5:
          switch (index) { case 1: return "headers"; case 2: return "invocationId"; }
          break;
        case 7:
          switch (index) { case 1: return "error"; case 2: return "allowReconnect"; }
          break;
        case 8:
        case 9:
          if (index == 1) return "sequenceId";
          break;
      }
      return null;
    }

    /// <summary>
    /// The text of a result kind of a Completion message.
    /// </summary>
    public static string ResultKindName(int kind)
    {
      switch (kind)
      {
        case 1: return "error";
        case 2: return "void";
        case 3: return "result";
        default: return null;
      }
    }

    /// <summary>
    /// Whether the data from the stream's position to its end is a capture of a hub connection: optionally the handshake, then messages that end exactly at the end of the data,
    /// each one array of the shape of its message type (the number of fields, the headers a map). Plain MsgPack almost never has that shape. The stream's position is kept.
    /// </summary>
    /// <returns>The messages, null when the data is not such a capture (or the stream cannot seek)</returns>
    public static List<Frame> Recognize(Stream stream)
    {
      if (!stream.CanSeek)
        return null;
      long start = stream.Position;
      try
      {
        return Read(stream);
      }
      catch (IOException)
      {
        return null;
      }
      finally
      {
        stream.Position = start;
      }
    }

    /// <inheritdoc cref="Recognize(Stream)"/>
    public static List<Frame> Recognize(byte[] data)
    {
      using (MemoryStream stream = new MemoryStream(data, false))
      {
        return Recognize(stream);
      }
    }

    private static List<Frame> Read(Stream stream)
    {
      List<Frame> frames = new List<Frame>();
      long end = stream.Length;
      byte[] buffer = new byte[256];
      bool handshakes = true;
      while (stream.Position < end)
      {
        long offset = stream.Position;
        int first = stream.ReadByte();
        if (handshakes && first == '{')
        {
          Frame handshake = ReadHandshake(stream, offset, end);
          if (handshake is null)
            return null;
          frames.Add(handshake);
          continue;
        }
        handshakes = false;

        long length = 0;
        int prefix = 0;
        int b = first;
        while (true)
        {
          if (b < 0)
            return null;
          length |= (long)(b & 0x7F) << (prefix * 7);
          prefix++;
          if ((b & 0x80) == 0)
            break;
          if (prefix == MaxPrefixLength)
            return null;
          b = stream.ReadByte();
        }
        if (length == 0 || length > int.MaxValue || length > end - stream.Position)
          return null;

        if (buffer.Length < length)
          buffer = new byte[Math.Max(length, buffer.Length * 2L)];
        if (!ReadExactly(stream, buffer, (int)length))
          return null;
        Frame frame = new Frame() { Offset = offset, PrefixLength = prefix, Length = (int)length };
        if (!IsMessage(buffer, (int)length, frame))
          return null;
        frames.Add(frame);
      }
      return frames.Count == 0 ? null : frames;
    }

    /// <summary>
    /// One JSON message of the handshake (the client's <c>{"protocol":"messagepack","version":1}</c>, the server's <c>{}</c> or <c>{"error":...}</c>), the first byte read.
    /// </summary>
    private static Frame ReadHandshake(Stream stream, long offset, long end)
    {
      List<byte> text = new List<byte>() { (byte)'{' };
      while (text.Count < MaxHandshakeLength)
      {
        int b = stream.ReadByte();
        if (b < 0)
          return null;
        if (b == RecordSeparator)
        {
          string json = Encoding.UTF8.GetString(text.ToArray()).TrimEnd();
          if (!json.EndsWith("}", StringComparison.Ordinal))
            return null;
          return new Frame() { Offset = offset, Length = text.Count + 1, Text = json };
        }
        if (b < 0x20 && b != '\t' && b != '\r' && b != '\n')
          return null; // not JSON text
        text.Add((byte)b);
      }
      return null;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer, int count)
    {
      int read = 0;
      while (read < count)
      {
        int n = stream.Read(buffer, read, count - read);
        if (n <= 0)
          return false;
        read += n;
      }
      return true;
    }

    /// <summary>
    /// The message is one array of the shape of its type, and nothing after it.
    /// </summary>
    private static bool IsMessage(byte[] buf, int end, Frame frame)
    {
      int pos = 0;
      long count = ReadArrayHeader(buf, ref pos, end);
      if (count < 1 || pos >= end)
        return false;
      int type = buf[pos];
      if (type < 1 || type > 9)
        return false; // Microsoft's writers use a positive fixint
      pos++;
      int min, max;
      switch (type)
      {
        case 1: case 4: min = 5; max = 6; break;
        case 2: min = 4; max = 4; break;
        case 3: min = 4; max = 5; break;
        case 5: min = 3; max = 3; break;
        case 6: min = 1; max = 1; break;
        case 7: min = 2; max = 3; break;
        default: min = 2; max = 2; break;
      }
      if (count < min || count > max)
        return false;
      frame.Type = type;

      for (int field = 1; field < count; field++)
      {
        if (pos >= end)
          return false;
        string name = FieldName(type, field);
        byte c = buf[pos];
        if (name == "headers" && !((c & 0xF0) == 0x80 || c == 0xDE || c == 0xDF))
          return false;
        if ((name == "invocationId" || name == "target") && !(IsString(c) || c == 0xC0))
          return false;
        if (name == "arguments" || name == "streamIds")
        {
          if (!((c & 0xF0) == 0x90 || c == 0xDC || c == 0xDD))
            return false;
        }
        if (name == "resultKind" && c >= 1 && c <= 3)
          frame.ResultKind = c;
        if (name == "invocationId" || name == "target")
        {
          string text = ReadString(buf, pos, end);
          if (name == "target")
            frame.Target = text;
          else
            frame.InvocationId = text;
        }
        if (!Skip(buf, ref pos, end))
          return false;
      }
      return pos == end;
    }

    private static bool IsString(byte c)
    {
      return (c & 0xE0) == 0xA0 || c == 0xD9 || c == 0xDA || c == 0xDB;
    }

    private static string ReadString(byte[] buf, int pos, int end)
    {
      byte c = buf[pos];
      long length;
      int header;
      if ((c & 0xE0) == 0xA0) { length = c & 0x1F; header = 1; }
      else if (c == 0xD9 && end - pos >= 2) { length = buf[pos + 1]; header = 2; }
      else if (c == 0xDA && end - pos >= 3) { length = Big(buf, pos + 1, 2); header = 3; }
      else if (c == 0xDB && end - pos >= 5) { length = Big(buf, pos + 1, 4); header = 5; }
      else return null;
      if (length > end - pos - header)
        return null;
      return Encoding.UTF8.GetString(buf, pos + header, (int)length);
    }

    private static long ReadArrayHeader(byte[] buf, ref int pos, int end)
    {
      if (pos >= end)
        return -1;
      byte c = buf[pos];
      if ((c & 0xF0) == 0x90) { pos++; return c & 0x0F; }
      if (c == 0xDC && end - pos >= 3) { pos += 3; return Big(buf, pos - 2, 2); }
      if (c == 0xDD && end - pos >= 5) { pos += 5; return Big(buf, pos - 4, 4); }
      return -1;
    }

    private static long Big(byte[] buf, int at, int count)
    {
      long value = 0;
      for (int t = 0; t < count; t++)
        value = (value << 8) | buf[at + t];
      return value;
    }

    /// <summary>
    /// Skips one value (its nested values counted, no recursion), false when the data ends inside it or has a code that is never used.
    /// </summary>
    private static bool Skip(byte[] buf, ref int pos, int end)
    {
      long pending = 1;
      while (pending > 0)
      {
        if (pos >= end)
          return false;
        byte c = buf[pos++];
        pending--;
        long skip = 0;
        if (c <= 0x7F || c >= 0xE0 || c == 0xC0 || c == 0xC2 || c == 0xC3) { }
        else if ((c & 0xF0) == 0x80) pending += 2L * (c & 0x0F);
        else if ((c & 0xF0) == 0x90) pending += c & 0x0F;
        else if ((c & 0xE0) == 0xA0) skip = c & 0x1F;
        else
        {
          int size;
          switch (c)
          {
            case 0xC4: case 0xD9: size = 1; break;
            case 0xC5: case 0xDA: size = 2; break;
            case 0xC6: case 0xDB: size = 4; break;
            case 0xC7: size = 1; break;
            case 0xC8: size = 2; break;
            case 0xC9: size = 4; break;
            case 0xDC: case 0xDE: size = 2; break;
            case 0xDD: case 0xDF: size = 4; break;
            default: size = 0; break;
          }
          if (size > 0)
          {
            if (end - pos < size)
              return false;
            long n = Big(buf, pos, size);
            pos += size;
            if (c == 0xDC || c == 0xDD) pending += n;
            else if (c == 0xDE || c == 0xDF) pending += 2 * n;
            else if (c >= 0xC7 && c <= 0xC9) skip = n + 1; // the type code
            else skip = n;
          }
          else
          {
            switch (c)
            {
              case 0xCC: case 0xD0: skip = 1; break;
              case 0xCD: case 0xD1: skip = 2; break;
              case 0xCA: case 0xCE: case 0xD2: skip = 4; break;
              case 0xCB: case 0xCF: case 0xD3: skip = 8; break;
              case 0xD4: skip = 2; break;
              case 0xD5: skip = 3; break;
              case 0xD6: skip = 5; break;
              case 0xD7: skip = 9; break;
              case 0xD8: skip = 17; break;
              default: return false; // 0xC1
            }
          }
        }
        if (skip > end - pos || pending > end - pos)
          return false;
        pos += (int)skip;
      }
      return true;
    }
  }
}
