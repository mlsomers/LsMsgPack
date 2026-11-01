using LsMsgPack;
using LsMsgPack.Streaming;
using LtMsgPack.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace LtMsgPack.Streaming
{
  /// <summary>
  /// Writes and reads the index of a stream and its tail (<see cref="MsgPackStreamIndex"/> describes the layout). The positions of the fields are known here (writing) and in Core's <see cref="IndexContent"/> (reading, also for LsMsgPack's item reader).
  /// The index is always in the byte order of the specification, whatever the options of the stream say: a reader finds it before it knows them.
  /// </summary>
  internal static class IndexFormat
  {
    /// <summary>
    /// The options of the index's content: explicit values, not the static defaults (which a program may change).
    /// </summary>
    internal static readonly LtMsgPackOptions Options = new LtMsgPackOptions
    {
      EndianAction = EndianAction.SwapIfCurrentSystemIsLittleEndian,
      DynamicallyCompact = true,
      ReadDateTimeKind = DateTimeKind.Utc,
      MaxDepth = 64
    };

    private const int MaxPlainDepth = 32; // metadata nested deeper is refused when written (the reader allows MaxDepth)

    internal static MsgPackWriter NewWriter(int capacity)
    {
      MsgPackWriter w = new MsgPackWriter(capacity);
      w.Reset(Options);
      return w;
    }

    #region Writing

    internal static void WriteSchemaLocation(MsgPackWriter w, SchemaId id, long? offset, int length)
    {
      w.ArrayHeader(3);
      w.BinHeader(SchemaId.Length);
      w.Ensure(SchemaId.Length);
      id.WriteTo(w.Buf, w.Pos);
      w.Pos += SchemaId.Length;
      if (offset.HasValue)
        w.Int64(offset.Value);
      else
        w.Nil();
      w.Int32(length);
    }

    /// <summary>
    /// The entry as an array, trailing nils left out. Throws for metadata that is not a plain value (before anything is written to <paramref name="w"/>, so the entries written before stay valid).
    /// </summary>
    internal static void WriteEntry(MsgPackWriter w, MsgPackStreamEntry e)
    {
      int count = 2;
      if (e.Identifier != null) count = 3;
      if (e.ContentType != null) count = 4;
      if (e.Modified.HasValue) count = 5;
      if (e.Hash != null) count = 6;
      if (e.ElementCount.HasValue) count = 7;
      if (e.Points != null && e.Points.Count > 0) count = 8;
      if (e.SidecarOffset.HasValue) count = 10;
      if (e.Metadata != null) count = 11;
      if (e.Incomplete) count = 12;

      int start = w.Pos;
      try
      {
        w.ArrayHeader(count);
        w.Int64(e.Offset);
        w.Int64(e.Length);
        if (count == 2)
          return;
        WriteIdentifier(w, e.Identifier);
        if (count == 3)
          return;
        NilOr(w, e.ContentType);
        if (count == 4)
          return;
        if (e.Modified.HasValue) w.DateTime(e.Modified.Value); else w.Nil();
        if (count == 5)
          return;
        if (e.Hash != null) w.Bin(e.Hash); else w.Nil();
        if (count == 6)
          return;
        if (e.ElementCount.HasValue) w.Int64(e.ElementCount.Value); else w.Nil();
        if (count == 7)
          return;
        WritePoints(w, e.Points);
        if (count == 8)
          return;
        if (e.SidecarOffset.HasValue)
        {
          w.Int64(e.SidecarOffset.Value);
          w.Int32(e.SidecarLength);
        }
        else
        {
          w.Nil();
          w.Nil();
        }
        if (count == 10)
          return;
        WriteMetadata(w, e.Metadata);
        if (count == 11)
          return;
        w.Bool(true);
      }
      catch
      {
        w.Pos = start;
        throw;
      }
    }

    private static void WritePoints(MsgPackWriter w, List<MsgPackStreamIndexPoint> points)
    {
      if (points is null || points.Count == 0)
      {
        w.Nil();
        return;
      }
      w.ArrayHeader(points.Count);
      for (int t = 0; t < points.Count; t++)
      {
        MsgPackStreamIndexPoint point = points[t];
        w.ArrayHeader(point.Key is null ? 2 : 3);
        w.Int64(point.Element);
        w.Int64(point.Offset);
        if (point.Key != null)
          WritePlain(w, point.Key, 1);
      }
    }

    internal static void WriteIdentifier(MsgPackWriter w, object identifier)
    {
      if (identifier is null || identifier is string || IsInteger(identifier))
        WritePlain(w, identifier, 0);
      else
        throw new ArgumentException($"An identifier of an entry is a string or an integer, not a {identifier.GetType()}.");
    }

    internal static void WriteMetadata(MsgPackWriter w, Dictionary<string, object> metadata)
    {
      if (metadata is null)
      {
        w.Nil();
        return;
      }
      w.MapHeader(metadata.Count);
      foreach (KeyValuePair<string, object> pair in metadata)
      {
        if (pair.Key is null)
          throw new ArgumentException("A metadata key cannot be null.");
        w.String(pair.Key);
        WritePlain(w, pair.Value, 1);
      }
    }

    /// <summary>
    /// A plain value: null, bool, integers, float, double, string, byte[], DateTime, and arrays (lists) and maps (dictionaries, pairs) of them.
    /// </summary>
    internal static void WritePlain(MsgPackWriter w, object value, int depth)
    {
      if (depth > MaxPlainDepth)
        throw new ArgumentException($"Metadata nested deeper than {MaxPlainDepth} levels (a cycle?).");
      switch (value)
      {
        case null: w.Nil(); return;
        case bool b: w.Bool(b); return;
        case string s: w.String(s); return;
        case byte[] bytes: w.Bin(bytes); return;
        case DateTime d: w.DateTime(d); return;
        case float f: w.Single(f); return;
        case double d: w.Double(d); return;
        case sbyte i: w.Int64(i); return;
        case short i: w.Int64(i); return;
        case int i: w.Int64(i); return;
        case long i: w.Int64(i); return;
        case byte i: w.UInt64(i); return;
        case ushort i: w.UInt64(i); return;
        case uint i: w.UInt64(i); return;
        case ulong i: w.UInt64(i); return;
        case IDictionary dictionary:
          w.MapHeader(dictionary.Count);
          foreach (DictionaryEntry entry in dictionary)
          {
            WritePlain(w, entry.Key, depth + 1);
            WritePlain(w, entry.Value, depth + 1);
          }
          return;
        case KeyValuePair<object, object>[] pairs: // a map as GetValue and the reader return it
          w.MapHeader(pairs.Length);
          for (int t = 0; t < pairs.Length; t++)
          {
            WritePlain(w, pairs[t].Key, depth + 1);
            WritePlain(w, pairs[t].Value, depth + 1);
          }
          return;
        case IList list:
          w.ArrayHeader(list.Count);
          for (int t = 0; t < list.Count; t++)
            WritePlain(w, list[t], depth + 1);
          return;
      }
      throw new ArgumentException($"Metadata holds plain values (null, bool, integers, floats, strings, byte[], DateTime, and lists and dictionaries of them), not a {value.GetType()}.");
    }

    private static bool IsInteger(object value)
    {
      return value is int || value is long || value is short || value is sbyte || value is uint || value is ulong || value is ushort || value is byte;
    }

    private static void NilOr(MsgPackWriter w, string value)
    {
      if (value is null) w.Nil(); else w.String(value);
    }

    /// <summary>
    /// The content of the index (the data of its extension value).
    /// </summary>
    internal static void WriteIndex(MsgPackWriter w, MsgPackStreamIndexStatus status, string writer, long? previousTail, string hashAlgorithm,
      int schemaCount, MsgPackWriter schemas, int entryCount, MsgPackWriter entries, byte[] metadata)
    {
      w.ArrayHeader(metadata is null ? 7 : 8);
      w.Int32(MsgPackStreamIndex.CurrentVersion);
      w.Int32((int)status);
      NilOr(w, writer);
      if (previousTail.HasValue) w.Int64(previousTail.Value); else w.Nil();
      NilOr(w, hashAlgorithm);
      w.ArrayHeader(schemaCount);
      w.Raw(schemas.Buf, 0, schemas.Pos);
      w.ArrayHeader(entryCount);
      w.Raw(entries.Buf, 0, entries.Pos);
      if (metadata != null)
        w.Raw(metadata);
    }

    /// <summary>
    /// The tail, <see cref="MsgPackStreamIndex.TailLength"/> bytes, big-endian whatever the writer's byte order.
    /// </summary>
    internal static void WriteTail(MsgPackWriter w, long indexOffset, int indexLength, uint crc)
    {
      w.Ensure(MsgPackStreamIndex.TailLength);
      byte[] b = w.Buf;
      int p = w.Pos;
      b[p++] = 0x94;
      b[p++] = 0xCF;
      ulong offset = (ulong)indexOffset;
      for (int shift = 56; shift >= 0; shift -= 8)
        b[p++] = (byte)(offset >> shift);
      b[p++] = 0xCE;
      p = BigEndian32(b, p, (uint)indexLength);
      b[p++] = 0xCE;
      p = BigEndian32(b, p, crc);
      b[p++] = 0xA4;
      for (int t = 0; t < IndexContent.MagicBytes.Length; t++)
        b[p++] = IndexContent.MagicBytes[t];
      w.Pos = p;
    }

    private static int BigEndian32(byte[] b, int p, uint value)
    {
      b[p++] = (byte)(value >> 24);
      b[p++] = (byte)(value >> 16);
      b[p++] = (byte)(value >> 8);
      b[p++] = (byte)value;
      return p;
    }

    #endregion

    #region Reading

    /// <summary>
    /// The indexes of a stream that seeks, the first one first: the last one from the end (or, with <paramref name="search"/>, searched backwards when the stream does not end with a tail: a writer that stopped after a checkpoint),
    /// the others through <see cref="MsgPackStreamIndex.PreviousTail"/>. Empty when the stream has no index. The offsets in them are positions in the stream.
    /// </summary>
    /// <param name="lastTail">Where the tail of the last index starts, -1 when there is none</param>
    /// <param name="endsWithTail">The stream ends with the tail of the last index (false: data follows it, or there is none)</param>
    /// <param name="delta">How far the stream was moved since the last index was written (its positions minus the writer's)</param>
    internal static List<MsgPackStreamIndex> ReadAll(Stream stream, bool search, out long lastTail, out bool endsWithTail, out long delta)
    {
      List<MsgPackStreamIndex> indexes = new List<MsgPackStreamIndex>();
      long length = stream.Length;
      lastTail = -1;
      endsWithTail = false;
      delta = 0;
      byte[] tailBytes = new byte[MsgPackStreamIndex.TailLength];

      long at = length - MsgPackStreamIndex.TailLength;
      MsgPackStreamIndex index = null;
      if (at >= 0)
        index = TryReadAt(stream, at, tailBytes, out delta);
      if (index != null)
        endsWithTail = true;
      else if (search)
        index = Search(stream, length, tailBytes, out at, out delta);
      if (index is null)
        return indexes;

      lastTail = at;
      indexes.Add(index);
      while (index.PreviousTail.HasValue)
      {
        long previous = index.PreviousTail.Value;
        if (previous < 0 || previous > index.Offset - MsgPackStreamIndex.TailLength)
          throw new MsgPackException($"The index at {index.Offset} points to a tail at {previous}, which is not before it.", index.Offset);
        MsgPackStreamIndex before = TryReadAt(stream, previous, tailBytes, out long unused);
        if (before is null)
          throw new MsgPackException($"The index at {index.Offset} points to a tail at {previous}, which is not a valid tail (or its index does not match its CRC-32).", previous);
        indexes.Add(before);
        index = before;
      }
      indexes.Reverse();
      return indexes;
    }

    /// <summary>
    /// The index whose tail starts at <paramref name="at"/>, null when there is no tail or its index does not match (structure, CRC-32): the caller may search further.
    /// An index that matches its CRC-32 but is not valid throws.
    /// </summary>
    private static MsgPackStreamIndex TryReadAt(Stream stream, long at, byte[] tailBytes, out long delta)
    {
      delta = 0;
      stream.Position = at;
      if (!ReadExactly(stream, tailBytes, MsgPackStreamIndex.TailLength) || !IndexContent.TryParseTail(tailBytes, 0, out IndexContent.Tail tail))
        return null;
      if (tail.IndexLength < 3 || tail.IndexLength > at || tail.IndexLength > int.MaxValue - 64)
        return null;

      long start = at - tail.IndexLength;
      delta = start - tail.IndexOffset; // the stream was moved by this much since it was written (0 when not)
      byte[] index = new byte[tail.IndexLength];
      stream.Position = start;
      if (!ReadExactly(stream, index, index.Length))
        return null;
      if (Crc32.Compute(index) != tail.Crc)
        return null;
      int header = IndexContent.ExtensionHeaderLength(index[0]);
      if (header == 0 || header > index.Length || unchecked((sbyte)index[header - 1]) != MsgPackStreamIndex.IndexExtensionType)
        return null;

      MsgPackStreamIndex result = Parse(index, header, delta, start);
      result.Offset = start;
      result.Length = index.Length;
      return result;
    }

    /// <summary>
    /// The last tail before <paramref name="length"/>, searched backwards in blocks (for its fixed types and magic, then its CRC-32).
    /// </summary>
    private static MsgPackStreamIndex Search(Stream stream, long length, byte[] tailBytes, out long found, out long delta)
    {
      const int Block = 64 * 1024;
      int tailLength = MsgPackStreamIndex.TailLength;
      byte[] buffer = new byte[Block + tailLength];
      long blockEnd = length; // the bytes before this are not searched yet
      found = -1;
      delta = 0;
      while (blockEnd > 0)
      {
        long blockStart = Math.Max(0, blockEnd - Block);
        int count = (int)(Math.Min(length, blockEnd + tailLength - 1) - blockStart); // overlap with the block after it, so a tail across the border is found
        if (count < tailLength)
          return null;
        stream.Position = blockStart;
        if (!ReadExactly(stream, buffer, count))
          return null;
        for (int p = count - tailLength; p >= 0; p--)
        {
          if (buffer[p] != 0x94 || buffer[p + tailLength - 1] != (byte)'X' || !IndexContent.TryParseTail(buffer, p, out IndexContent.Tail unused))
            continue;
          MsgPackStreamIndex index = TryReadAt(stream, blockStart + p, tailBytes, out delta);
          if (index != null)
          {
            found = blockStart + p;
            return index;
          }
        }
        blockEnd = blockStart;
      }
      return null;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer, int count)
    {
      int done = 0;
      while (done < count)
      {
        int read = stream.Read(buffer, done, count - done);
        if (read <= 0)
          return false;
        done += read;
      }
      return true;
    }

    /// <summary>
    /// The content of an index (after the extension header in <paramref name="data"/>), checked by <see cref="IndexContent.Parse"/>.
    /// </summary>
    private static MsgPackStreamIndex Parse(byte[] data, int start, long delta, long indexStart)
    {
      MsgPackReader r = new MsgPackReader(data, start, data.Length, Options);
      object[] fields = r.ReadPlain(0) as object[];
      if (fields is null || r.Pos != data.Length)
        throw IndexContent.Invalid("is not one array", indexStart);
      return IndexContent.Parse(fields, delta, indexStart);
    }

    #endregion
  }
}
