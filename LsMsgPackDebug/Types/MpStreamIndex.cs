using LsMsgPack;
using System;
using System.Collections.Generic;
using System.IO;
using LsMsgPack.Streaming;

#if KEEPTRACK
using System.ComponentModel;
using System.Xml.Serialization;
#endif

namespace LsMsgPackDebug
{
  /// <summary>
  /// The index of a stream (<see cref="MsgPackStreamIndex"/>): an extension value of type 3 at the root, read with <see cref="MsgPackSettings.ReadStreams"/>.
  /// Its data is one MsgPack array (<see cref="Content"/>, always in the byte order of the specification), its tail (<see cref="MpStreamTail"/>) follows it.
  /// </summary>
  public class MpStreamIndex : MpExt
  {
    internal MpStreamIndex(MpExt ext, Stream data, long start, long contentStart) : base(ext.Settings)
    {
      CopyBaseDataFrom(ext);
      _start = start;
      _length = data.Position - start;
      ReadContent(data, contentStart);
      Parse(0);
    }

    private readonly long _start;
    private readonly long _length;
    private string _contentError;

    /// <summary>
    /// Where the extension value starts in the data.
    /// </summary>
    internal long Start { get { return _start; } }

    /// <summary>
    /// The data of the extension read as MsgPack: the array of the index's fields (see <see cref="MsgPackStreamIndex"/>), null when it could not be read.
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Browsable(false)]
#endif
    public MsgPackItem Content { get; private set; }

    /// <summary>
    /// The index, with the offsets moved as its tail says (when the stream was moved since it was written), null when it is not valid (<see cref="IndexError"/>).
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Browsable(false)]
#endif
    public MsgPackStreamIndex Index { get; private set; }

    /// <summary>
    /// Why the content is not a valid index, null when it is.
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Error")]
    [Description("Why the content is not a valid index (empty when it is).")]
#endif
    public string IndexError { get; private set; }

    /// <summary>
    /// The tail that follows the index, null when there is none (the stream was cut off, or the index was written by something else).
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Browsable(false)]
#endif
    public MpStreamTail Tail { get; private set; }

    /// <summary>
    /// How far the stream was moved since the index was written (where it is minus where its tail says it is), added to every offset of <see cref="Index"/>.
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Moved by")]
    [Description("How far the stream was moved since the index was written (where it is minus where its tail says it is), added to every offset of the index.")]
#endif
    public long Delta { get; private set; }

#if KEEPTRACK
    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Status")]
    [Description("Complete: the writer finished the stream after this index. Checkpoint: written while the stream was written, more entries may follow. Interrupted: the writer stopped in the middle of an entry.")]
    public string Status { get { return Index?.Status.ToString(); } }

    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Writer")]
    [Description("The library that wrote the index.")]
    public string Writer { get { return Index?.Writer; } }

    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Entries")]
    [Description("The number of entries (root values) this index lists: the ones since the index before it.")]
    public int EntryCount { get { return Index is null ? 0 : Index.Entries.Count; } }

    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Schemas")]
    [Description("The number of schemas this index lists (where their inline copy is).")]
    public int SchemaCount { get { return Index is null ? 0 : Index.Schemas.Count; } }

    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Previous tail")]
    [Description("Where the tail of the index before this one is (a checkpoint or an earlier part of the stream), empty for the first one.")]
    public long? PreviousTail { get { return Index?.PreviousTail; } }

    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("Hash algorithm")]
    [Description("The algorithm of the hashes of the entries (a name of the multicodec table), empty without hashes.")]
    public string HashAlgorithm { get { return Index?.HashAlgorithm; } }

    [XmlIgnore]
    [Category("Stream index")]
    [DisplayName("CRC-32 matches")]
    [Description("The CRC-32 in the tail matches the bytes of the index (false: the index is damaged, or no tail follows it).")]
    public bool CrcMatches { get { return Tail != null && Tail.CrcMatches; } }
#endif

    private void ReadContent(Stream data, long contentStart)
    {
      MsgPackSettings settings = _settings.Clone();
      settings._endianAction = EndianAction.SwapIfCurrentSystemIsLittleEndian; // the content is always in the byte order of the specification
      settings._readStreams = false;
      settings._readDateTimeKind = DateTimeKind.Utc;
      settings.FileContainsErrors = false;
      try
      {
        if (data.CanSeek)
        {
          // From the stream itself, so the offsets of the items are positions in the data
          long end = data.Position;
          data.Position = contentStart;
          try
          {
            Content = Unpack(data, settings, _depth + 1);
            if (data.Position != end)
              _contentError = string.Concat("The content of the index ends at ", data.Position.ToString(), ", not at the end of its extension value (", end.ToString(), ").");
          }
          finally
          {
            data.Position = end;
          }
        }
        else
          Content = Unpack(new MemoryStream(BaseValue), settings, _depth + 1);
      }
      catch (Exception ex)
      {
        _contentError = "The content of the index is not MsgPack: " + ex.Message;
      }
    }

    private void Parse(long delta)
    {
      Index = null;
      Delta = delta;
      IndexError = _contentError;
      if (IndexError != null)
        return;
      object[] fields = Content is MpArray ? Content.Value as object[] : null;
      if (fields is null)
      {
#if KEEPTRACK
        if (Content is MpError)
        {
          IndexError = "The content of the index is not valid MsgPack.";
          return;
        }
#endif
        IndexError = "The content of the index is not one array.";
        return;
      }
      try
      {
        MsgPackStreamIndex index = IndexContent.Parse(fields, delta, _start);
        index.Offset = _start;
        index.Length = _length;
        Index = index;
      }
      catch (MsgPackException ex)
      {
        IndexError = ex.Message;
      }
    }

    /// <summary>
    /// The tail that follows the index: its offsets are moved by the difference between where the index is and where the tail says it is.
    /// </summary>
    internal void SetTail(MpStreamTail tail)
    {
      Tail = tail;
      tail.Index = this;
      long delta = _start - tail.IndexOffset;
      if (delta != 0)
        Parse(delta);
    }

    public override string ToString()
    {
      if (Index is null)
        return $"Stream index (extension type {TypeSpecifier}) that is not valid: {IndexError}";
      string tail = Tail is null ? "no tail follows it" : Tail.CrcMatches ? "CRC-32 matches" : "CRC-32 does not match";
      return $"Stream index ({Index.Status}) of {Index.Entries.Count} entries, {tail}.";
    }

#if KEEPTRACK
    private Dictionary<MsgPackItem, string> _names;

    private static readonly string[] IndexFields = { "version", "status", "writer", "previousTail", "hashAlgorithm", "schemas", "entries", "metadata" };
    private static readonly string[] EntryFields = { "offset", "length", "identifier", "contentType", "modified", "hash", "elementCount", "points", "sidecarOffset", "sidecarLength", "metadata", "incomplete" };
    private static readonly string[] SchemaFields = { "id", "offset", "length" };
    private static readonly string[] PointFields = { "element", "offset", "key" };

    /// <summary>
    /// The name of a field of the index (an item of <see cref="Content"/>: "entries", "offset", ...), null for the others (elements of the lists, metadata).
    /// </summary>
    public string NameOf(MsgPackItem item)
    {
      if (_names is null)
        _names = Names();
      string name;
      return item != null && _names.TryGetValue(item, out name) ? name : null;
    }

    private Dictionary<MsgPackItem, string> Names()
    {
      Dictionary<MsgPackItem, string> names = new Dictionary<MsgPackItem, string>();
      MsgPackItem[] fields = Items(Content);
      Name(names, fields, IndexFields);
      if (fields.Length > 5)
      {
        MsgPackItem[] schemas = Items(fields[5]);
        for (int t = 0; t < schemas.Length; t++)
          Name(names, Items(schemas[t]), SchemaFields);
      }
      if (fields.Length > 6)
      {
        MsgPackItem[] entries = Items(fields[6]);
        for (int t = 0; t < entries.Length; t++)
        {
          MsgPackItem[] entry = Items(entries[t]);
          Name(names, entry, EntryFields);
          if (entry.Length > 7)
          {
            MsgPackItem[] points = Items(entry[7]);
            for (int p = 0; p < points.Length; p++)
              Name(names, Items(points[p]), PointFields);
          }
        }
      }
      return names;
    }

    private static void Name(Dictionary<MsgPackItem, string> names, MsgPackItem[] items, string[] fieldNames)
    {
      for (int t = 0; t < items.Length && t < fieldNames.Length; t++)
        if (items[t] != null)
          names[items[t]] = fieldNames[t];
    }

    private static MsgPackItem[] Items(MsgPackItem item)
    {
      MpArray array = item as MpArray;
      return array is null ? new MsgPackItem[0] : array.PackedValues;
    }
#endif
  }
}
