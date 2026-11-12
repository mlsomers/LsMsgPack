using System;
using System.Collections.Generic;

namespace LsMsgPack.Streaming
{
  /// <summary>
  /// Reads the index of a stream from its plain values, and its tail (<see cref="MsgPackStreamIndex"/> describes the layout): shared by LsMsgPack (IndexFormat, which also writes them) and LsMsgPack's item reader (MpStreamIndex, with MsgPackSettings.ReadStreams).
  /// </summary>
  internal static class IndexContent
  {
    internal static readonly byte[] MagicBytes = { (byte)'L', (byte)'S', (byte)'I', (byte)'X' };

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

    /// <summary>
    /// The content of an index (its array as plain values: object[], KeyValuePair&lt;object, object&gt;[], integers, strings, byte[], DateTime), checked: types, versions, offsets inside the stream before the index (<paramref name="indexStart"/>), points inside their entry and in order.
    /// </summary>
    /// <param name="delta">Added to every offset: how far the stream was moved since it was written</param>
    internal static MsgPackStreamIndex Parse(object[] fields, long delta, long indexStart)
    {
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

    internal static MsgPackException Invalid(string what, long indexStart)
    {
      return new MsgPackException($"The index at {indexStart} {what}.", indexStart);
    }
  }
}
