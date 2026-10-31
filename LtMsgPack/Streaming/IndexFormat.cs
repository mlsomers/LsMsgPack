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
  /// Writes and reads the index of a stream and its tail (<see cref="MsgPackStreamIndex"/> describes the layout): the only place that knows the positions of the fields.
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
    private static readonly byte[] MagicBytes = { (byte)'L', (byte)'S', (byte)'I', (byte)'X' };

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
      for (int t = 0; t < MagicBytes.Length; t++)
        b[p++] = MagicBytes[t];
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

    #region Tail

    internal struct Tail
    {
      internal long IndexOffset; // as the writer counted
      internal long IndexLength;
      internal uint Crc;
    }

    /// <summary>
    /// The tail at <paramref name="at"/> in <paramref name="b"/> (fixed types and <see cref="MsgPackStreamIndex.Magic"/>), false when the bytes are no tail.
    /// </summary>
    internal static bool TryParseTail(byte[] b, int at, out Tail tail)
    {
      tail = default(Tail);
      if (b[at] != 0x94 || b[at + 1] != 0xCF || b[at + 10] != 0xCE || b[at + 15] != 0xCE || b[at + 20] != 0xA4)
        return false;
      for (int t = 0; t < MagicBytes.Length; t++)
        if (b[at + 21 + t] != MagicBytes[t])
          return false;
      ulong offset = 0;
      for (int t = 0; t < 8; t++)
        offset = offset << 8 | b[at + 2 + t];
      if (offset > long.MaxValue)
        return false;
      tail.IndexOffset = (long)offset;
      tail.IndexLength = U32(b, at + 11);
      tail.Crc = U32(b, at + 16);
      return true;
    }

    private static uint U32(byte[] b, int at)
    {
      return (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
    }

    /// <summary>
    /// The length of the header of the extension value that starts with <paramref name="format"/>, 0 when it is no extension.
    /// From the format byte alone: the length in the header is in the writer's byte order, which the reader of the index does not know.
    /// </summary>
    internal static int ExtensionHeaderLength(byte format)
    {
      switch (format)
      {
        case 0xD4: case 0xD5: case 0xD6: case 0xD7: case 0xD8: return 2;
        case 0xC7: return 3;
        case 0xC8: return 4;
        case 0xC9: return 6;
        default: return 0;
      }
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
      if (!ReadExactly(stream, tailBytes, MsgPackStreamIndex.TailLength) || !TryParseTail(tailBytes, 0, out Tail tail))
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
      int header = ExtensionHeaderLength(index[0]);
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
          if (buffer[p] != 0x94 || buffer[p + tailLength - 1] != (byte)'X' || !TryParseTail(buffer, p, out Tail unused))
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
    /// The content of an index (after the extension header in <paramref name="data"/>), checked: types, versions, offsets inside the stream before the index (<paramref name="indexStart"/>), points inside their entry and in order.
    /// </summary>
    private static MsgPackStreamIndex Parse(byte[] data, int start, long delta, long indexStart)
    {
      MsgPackReader r = new MsgPackReader(data, start, data.Length, Options);
      object[] fields = r.ReadPlain(0) as object[];
      if (fields is null || r.Pos != data.Length)
        throw Invalid("is not one array", indexStart);

      MsgPackStreamIndex index = new MsgPackStreamIndex();
      index.Version = (int)Integer(Field(fields, 0), 1, int.MaxValue, "version", indexStart);
      if (index.Version > MsgPackStreamIndex.CurrentVersion)
        throw new MsgPackException($"The index at {indexStart} has version {index.Version}, this library reads up to version {MsgPackStreamIndex.CurrentVersion}.", indexStart);
      index.Status = (MsgPackStreamIndexStatus)Integer(Field(fields, 1) ?? 0, 0, 2, "status", indexStart);
      index.Writer = Text(Field(fields, 2), "writer", indexStart);
      object previous = Field(fields, 3);
      if (previous != null)
        index.PreviousTail = Offset(previous, delta, indexStart, "previous tail");
      index.HashAlgorithm = Text(Field(fields, 4), "hash algorithm", indexStart);

      object[] schemas = Field(fields, 5) as object[];
      if (Field(fields, 5) != null && schemas is null)
        throw Invalid("has no array of schemas", indexStart);
      if (schemas != null)
        for (int t = 0; t < schemas.Length; t++)
          index.Schemas.Add(ParseSchema(schemas[t], delta, indexStart));

      object[] entries = Field(fields, 6) as object[];
      if (Field(fields, 6) != null && entries is null)
        throw Invalid("has no array of entries", indexStart);
      if (entries != null)
        for (int t = 0; t < entries.Length; t++)
          index.Entries.Add(ParseEntry(entries[t], delta, indexStart));

      index.Metadata = Metadata(Field(fields, 7), indexStart);
      return index;
    }

    private static MsgPackStreamSchemaLocation ParseSchema(object value, long delta, long indexStart)
    {
      object[] fields = value as object[];
      byte[] id = fields != null && fields.Length >= 3 ? fields[0] as byte[] : null;
      if (id is null || id.Length != SchemaId.Length)
        throw Invalid("has a schema without an id of 16 bytes", indexStart);
      int length = (int)Integer(fields[2], 0, int.MaxValue, "schema length", indexStart);
      long? offset = null;
      if (fields[1] != null)
      {
        offset = Offset(fields[1], delta, indexStart, "schema");
        if (length == 0 || length > indexStart - offset.Value)
          throw Invalid($"has a schema at {offset.Value} of {length} bytes that does not end before the index", indexStart);
      }
      return new MsgPackStreamSchemaLocation(new SchemaId(id), offset, length);
    }

    private static MsgPackStreamEntry ParseEntry(object value, long delta, long indexStart)
    {
      object[] fields = value as object[];
      if (fields is null || fields.Length < 2)
        throw Invalid("has an entry that is not an array of at least its offset and length", indexStart);

      MsgPackStreamEntry entry = new MsgPackStreamEntry();
      entry.Offset = Offset(fields[0], delta, indexStart, "entry");
      entry.Length = Integer(fields[1], 0, long.MaxValue, "entry length", indexStart);
      if (entry.Length > indexStart - entry.Offset)
        throw Invalid($"has an entry at {entry.Offset} of {entry.Length} bytes that does not end before the index", indexStart);

      object identifier = Field(fields, 2);
      if (identifier != null && !(identifier is string))
        identifier = IdentifierValue(identifier, indexStart);
      entry.Identifier = identifier;
      entry.ContentType = Text(Field(fields, 3), "content type", indexStart);
      object modified = Field(fields, 4);
      if (modified != null)
      {
        if (!(modified is DateTime))
          throw Invalid("has an entry whose modification time is not a timestamp", indexStart);
        entry.Modified = (DateTime)modified;
      }
      object hash = Field(fields, 5);
      if (hash != null && !(hash is byte[]))
        throw Invalid("has an entry whose hash is not a bin", indexStart);
      entry.Hash = (byte[])hash;
      object elements = Field(fields, 6);
      if (elements != null)
        entry.ElementCount = Integer(elements, 0, uint.MaxValue, "element count", indexStart);
      entry.Points = Points(Field(fields, 7), entry, indexStart);

      object sidecar = Field(fields, 8);
      if (sidecar != null)
      {
        long at = Offset(sidecar, delta, indexStart, "sidecar");
        int length = (int)Integer(Field(fields, 9), 0, int.MaxValue, "sidecar length", indexStart);
        if (at < entry.Offset + entry.Length || length > indexStart - at)
          throw Invalid($"has a sidecar at {at} that is not between its entry and the index", indexStart);
        entry.SidecarOffset = at;
        entry.SidecarLength = length;
      }
      entry.Metadata = Metadata(Field(fields, 10), indexStart);
      object incomplete = Field(fields, 11);
      if (incomplete != null && !(incomplete is bool))
        throw Invalid("has an entry whose incomplete flag is not a bool", indexStart);
      entry.Incomplete = incomplete is bool b && b;
      return entry;
    }

    private static List<MsgPackStreamIndexPoint> Points(object value, MsgPackStreamEntry entry, long indexStart)
    {
      if (value is null)
        return null;
      object[] items = value as object[];
      if (items is null)
        throw Invalid("has an entry whose points are not an array", indexStart);
      List<MsgPackStreamIndexPoint> points = new List<MsgPackStreamIndexPoint>(items.Length);
      long lastElement = -1;
      long lastOffset = -1;
      for (int t = 0; t < items.Length; t++)
      {
        object[] fields = items[t] as object[];
        if (fields is null || fields.Length < 2)
          throw Invalid("has a point that is not an array of its element and offset", indexStart);
        long element = Integer(fields[0], 0, long.MaxValue, "point element", indexStart);
        long offset = Integer(fields[1], 0, long.MaxValue, "point offset", indexStart);
        if (element <= lastElement || offset <= lastOffset)
          throw Invalid($"has points of the entry at {entry.Offset} that are not in ascending order", indexStart);
        if (offset >= entry.Length || (entry.ElementCount.HasValue && element >= entry.ElementCount.Value))
          throw Invalid($"has a point (element {element}, offset {offset}) outside of the entry at {entry.Offset}", indexStart);
        points.Add(new MsgPackStreamIndexPoint(element, offset, Field(fields, 2)));
        lastElement = element;
        lastOffset = offset;
      }
      return points;
    }

    private static Dictionary<string, object> Metadata(object value, long indexStart)
    {
      if (value is null)
        return null;
      KeyValuePair<object, object>[] pairs = value as KeyValuePair<object, object>[];
      if (pairs is null)
        throw Invalid("has metadata that is not a map", indexStart);
      Dictionary<string, object> metadata = new Dictionary<string, object>(pairs.Length, StringComparer.Ordinal);
      for (int t = 0; t < pairs.Length; t++)
      {
        string key = pairs[t].Key as string;
        if (key is null || metadata.ContainsKey(key))
          throw Invalid("has metadata with a key that is not a string, or twice", indexStart);
        metadata.Add(key, pairs[t].Value);
      }
      return metadata;
    }

    private static object Field(object[] fields, int position)
    {
      return position < fields.Length ? fields[position] : null;
    }

    private static string Text(object value, string what, long indexStart)
    {
      if (value is null || value is string)
        return (string)value;
      throw Invalid($"has a {what} that is not a string", indexStart);
    }

    /// <summary>
    /// An offset as the writer counted it, moved by <paramref name="delta"/>: a position in the stream before the index.
    /// </summary>
    private static long Offset(object value, long delta, long indexStart, string what)
    {
      long offset = Integer(value, 0, long.MaxValue, what + " offset", indexStart);
      if (delta < 0 ? offset < -delta : offset > long.MaxValue - delta)
        throw Invalid($"has a {what} offset ({offset}) outside of the stream", indexStart);
      offset += delta;
      if (offset >= indexStart)
        throw Invalid($"has a {what} offset ({offset}) that is not before the index", indexStart);
      return offset;
    }

    private static long Integer(object value, long min, long max, string what, long indexStart)
    {
      long result;
      switch (value)
      {
        case byte v: result = v; break;
        case sbyte v: result = v; break;
        case ushort v: result = v; break;
        case short v: result = v; break;
        case uint v: result = v; break;
        case int v: result = v; break;
        case long v: result = v; break;
        case ulong v:
          if (v > long.MaxValue)
            throw Invalid($"has a {what} that is too large", indexStart);
          result = (long)v;
          break;
        default:
          throw Invalid($"has a {what} that is not an integer", indexStart);
      }
      if (result < min || result > max)
        throw Invalid($"has a {what} out of range ({result})", indexStart);
      return result;
    }

    /// <summary>
    /// An integer identifier as a long (an ulong above long.MaxValue), whatever format the writer picked for it.
    /// </summary>
    private static object IdentifierValue(object value, long indexStart)
    {
      if (value is ulong u && u > long.MaxValue)
        return u;
      return Integer(value, long.MinValue, long.MaxValue, "identifier (a string or an integer)", indexStart);
    }

    private static MsgPackException Invalid(string what, long indexStart)
    {
      return new MsgPackException($"The index at {indexStart} {what}.", indexStart);
    }

    #endregion
  }
}
