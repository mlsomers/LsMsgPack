using System.Collections.Generic;

namespace LsMsgPack.Streaming
{
  /// <summary>
  /// The index at the end of a stream: where its entries (ranges of root values) and the schemas they refer to are, so a reader can go to one without reading what comes before it.
  /// <para>Layout at the end of the stream (every part a MsgPack value, so any reader can read or skip the stream value by value):</para>
  /// <list type="number">
  /// <item>the index: an extension value of type <see cref="IndexExtensionType"/> whose data is one MsgPack array (positions below; trailing nils are left out, so fields can be added at the end).
  /// The extension marks it as no data: a reader that cannot seek meets it while reading the root values (it would read the array into the next object by position), and skips it by its length without parsing the entries;</item>
  /// <item>the tail: the array <c>[offset of the index, its length in bytes, <see cref="Magic"/>]</c> written with fixed types, always the last <see cref="TailLength"/> bytes:
  /// <c>93 cf &lt;uint64&gt; ce &lt;uint32&gt; a4 "LSIX"</c> (the offset is where the index's extension value starts, the length is the whole extension value).
  /// Fixed length and without DynamicallyCompact, because MsgPack cannot be read backwards: a reader reads the last <see cref="TailLength"/> bytes (an HTTP range request "bytes=-20" works too) and knows where the index is.
  /// It needs no extension of its own: the value after the index is the tail.</item>
  /// </list>
  /// <para>Index array: 0 <see cref="Version"/>, 1 <see cref="Writer"/>, 2 <see cref="PreviousIndex"/>, 3 <see cref="HashAlgorithm"/>,
  /// 4 <see cref="Schemas"/> (each <c>[id (bin 16), offset or nil, length]</c>), 5 <see cref="Entries"/> (see <see cref="MsgPackStreamEntry"/>), 6 <see cref="Metadata"/>.</para>
  /// <para>Offsets count from where the writer started (its Position). When the stream is moved (embedded in a file at another offset, a prefix added),
  /// the reader takes the difference between where the tail is and where the index says it is (its offset + its length) and adds it to every offset: no field is needed for that.</para>
  /// </summary>
  public sealed class MsgPackStreamIndex
  {
    /// <summary>
    /// The version of the index layout this library writes.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The extension type of the index. Application types (0 to 127) because the specification reserves the negative ones; 1 is the decimal and 2 the schema reference of this library.
    /// </summary>
    public const sbyte IndexExtensionType = 3;

    /// <summary>
    /// The extension type that wraps the bytes an <see cref="IMsgPackStreamObserver"/> returns for an entry (written right after the entry), so readers skip them (they need not be MsgPack: a signature, parity).
    /// </summary>
    public const sbyte SidecarExtensionType = 4;

    /// <summary>
    /// The length of the tail: the fixarray header (1), the index offset as uint64 (9), the index length as uint32 (5) and <see cref="Magic"/> as fixstr (5).
    /// </summary>
    public const int TailLength = 20;

    /// <summary>
    /// The last element of the tail, the last 4 bytes of a stream with an index: tells a tail apart from other data that ends in an array of the same shape.
    /// </summary>
    public const string Magic = "LSIX";

    /// <summary>
    /// The version of the layout the index was written in (<see cref="CurrentVersion"/>). A reader refuses versions it does not know, fields added at the end do not raise it.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// What wrote the stream, e.g. "LtMsgPack 2026.10.31.1" (as Parquet's created_by), to tell the output of a faulty version apart. Null: not written.
    /// </summary>
    public string Writer { get; set; }

    /// <summary>
    /// The offset of the index written before this one in the same stream, null for the first. A stream can be appended to: the new values follow the old tail,
    /// and the new index lists only the new entries and points to the old index (as PDF's incremental updates), so nothing is rewritten.
    /// </summary>
    public long? PreviousIndex { get; set; }

    /// <summary>
    /// The algorithm of the <see cref="MsgPackStreamEntry.Hash"/>es (<see cref="IMsgPackStreamHasher.Algorithm"/>), null when the entries have none.
    /// One per index: the writer has one hasher; an appended part may use another.
    /// </summary>
    public string HashAlgorithm { get; set; }

    /// <summary>
    /// The schemas the entries refer to (with <c>MsgPackStreamSchemas.PerStream</c> or <c>External</c>): where their inline copy is in the stream, if it is.
    /// A reader that goes to an entry directly registers them in its store first (or when an entry refers to one it does not have).
    /// </summary>
    public List<MsgPackStreamSchemaLocation> Schemas { get; } = new List<MsgPackStreamSchemaLocation>();

    /// <summary>
    /// The entries, in the order they were written.
    /// </summary>
    public List<MsgPackStreamEntry> Entries { get; } = new List<MsgPackStreamEntry>();

    /// <summary>
    /// Data about the whole stream (source, creation time, license...), plain values (null, bool, integers, floats, strings, byte[], DateTime, and arrays and maps of them). Null: none.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; }
  }
}
