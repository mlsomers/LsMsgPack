using LsMsgPack;
using System;
using System.IO;

namespace LtMsgPack.IO
{
  /// <summary>
  /// Copies exactly one payload from a stream that cannot be read in place (e.g. a network stream): the stream is left after it, as LsMsgPack does.
  /// </summary>
  internal static class StreamPayload
  {
    internal static byte[] Read(Stream source, LtMsgPackOptions options)
    {
      bool littleEndian = BitConverter.IsLittleEndian != MsgPackOptions.SwapEndianChoice(options, 2);
      MemoryStream copy = new MemoryStream();
      int first = CopyValue(source, copy, littleEndian, 0, options._maxDepth);
      if (options._useInexedSchema && first != 0xC0) // the schema (or a reference to it), then the value
        CopyValue(source, copy, littleEndian, 0, options._maxDepth);
      return copy.ToArray();
    }

    /// <returns>The first byte of the value</returns>
    private static int CopyValue(Stream source, MemoryStream copy, bool littleEndian, int depth, int maxDepth)
    {
      int c = ReadByte(source, copy);
      if (c <= 0x7F || c >= 0xE0) return c;
      if ((c & 0xE0) == 0xA0) { CopyBytes(source, copy, c & 0x1F); return c; }
      if ((c & 0xF0) == 0x90) { CopyItems(source, copy, littleEndian, c & 0x0F, depth, maxDepth); return c; }
      if ((c & 0xF0) == 0x80) { CopyItems(source, copy, littleEndian, 2L * (c & 0x0F), depth, maxDepth); return c; }
      switch (c)
      {
        case 0xC0: case 0xC2: case 0xC3: return c;
        case 0xCC: case 0xD0: CopyBytes(source, copy, 1); return c;
        case 0xCD: case 0xD1: CopyBytes(source, copy, 2); return c;
        case 0xCE: case 0xD2: case 0xCA: CopyBytes(source, copy, 4); return c;
        case 0xCF: case 0xD3: case 0xCB: CopyBytes(source, copy, 8); return c;
        case 0xD9: case 0xC4: CopyBytes(source, copy, Length(source, copy, 1, littleEndian)); return c;
        case 0xDA: case 0xC5: CopyBytes(source, copy, Length(source, copy, 2, littleEndian)); return c;
        case 0xDB: case 0xC6: CopyBytes(source, copy, Length(source, copy, 4, littleEndian)); return c;
        case 0xDC: CopyItems(source, copy, littleEndian, Length(source, copy, 2, littleEndian), depth, maxDepth); return c;
        case 0xDD: CopyItems(source, copy, littleEndian, Length(source, copy, 4, littleEndian), depth, maxDepth); return c;
        case 0xDE: CopyItems(source, copy, littleEndian, 2 * Length(source, copy, 2, littleEndian), depth, maxDepth); return c;
        case 0xDF: CopyItems(source, copy, littleEndian, 2 * Length(source, copy, 4, littleEndian), depth, maxDepth); return c;
        case 0xD4: CopyBytes(source, copy, 2); return c;
        case 0xD5: CopyBytes(source, copy, 3); return c;
        case 0xD6: CopyBytes(source, copy, 5); return c;
        case 0xD7: CopyBytes(source, copy, 9); return c;
        case 0xD8: CopyBytes(source, copy, 17); return c;
        case 0xC7: CopyBytes(source, copy, 1 + Length(source, copy, 1, littleEndian)); return c;
        case 0xC8: CopyBytes(source, copy, 1 + Length(source, copy, 2, littleEndian)); return c;
        case 0xC9: CopyBytes(source, copy, 1 + Length(source, copy, 4, littleEndian)); return c;
      }
      throw new MsgPackException("The specification specifically states that the value 0xC1 should never be used.", copy.Length - 1, MsgPackTypeId.NeverUsed);
    }

    private static void CopyItems(Stream source, MemoryStream copy, bool littleEndian, long count, int depth, int maxDepth)
    {
      if (depth >= maxDepth)
        throw new MsgPackException($"The data is nested deeper than {nameof(LtMsgPackOptions)}.{nameof(LtMsgPackOptions.MaxDepth)} ({maxDepth}).");
      for (long t = 0; t < count; t++)
        CopyValue(source, copy, littleEndian, depth + 1, maxDepth);
    }

    private static int ReadByte(Stream source, MemoryStream copy)
    {
      int b = source.ReadByte();
      if (b < 0)
        throw MsgPackReader.EndOfData();
      copy.WriteByte((byte)b);
      return b;
    }

    private static long Length(Stream source, MemoryStream copy, int size, bool littleEndian)
    {
      long value = 0;
      byte[] bytes = new byte[size];
      Fill(source, bytes, size);
      copy.Write(bytes, 0, size);
      for (int t = 0; t < size; t++)
        value = (value << 8) | bytes[littleEndian ? size - 1 - t : t];
      return value;
    }

    private static void CopyBytes(Stream source, MemoryStream copy, long count)
    {
      byte[] buffer = new byte[(int)Math.Min(count, 81920)];
      while (count > 0)
      {
        int chunk = (int)Math.Min(count, buffer.Length);
        Fill(source, buffer, chunk);
        copy.Write(buffer, 0, chunk);
        count -= chunk;
      }
    }

    private static void Fill(Stream source, byte[] buffer, int count)
    {
      int offset = 0;
      while (offset < count) // Stream.Read may return fewer bytes than asked (network streams)
      {
        int read = source.Read(buffer, offset, count - offset);
        if (read <= 0)
          throw MsgPackReader.EndOfData();
        offset += read;
      }
    }
  }
}
