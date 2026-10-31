using System;
using System.Collections.Generic;

namespace LsMsgPack.Streaming
{
  /// <summary>
  /// One entry of a <see cref="MsgPackStreamIndex"/>: a range of root values of the stream (one object with its schema or schema reference, an array of unknown length of rows...).
  /// <para>Written as an array (trailing nils left out): 0 <see cref="Offset"/>, 1 <see cref="Length"/>, 2 <see cref="Identifier"/>, 3 <see cref="ContentType"/>, 4 <see cref="Modified"/>,
  /// 5 <see cref="Hash"/>, 6 <see cref="ElementCount"/>, 7 <see cref="Points"/> (each <c>[element, offset, key]</c>), 8 <see cref="SidecarOffset"/>, 9 <see cref="SidecarLength"/>, 10 <see cref="Metadata"/>.</para>
  /// </summary>
  public sealed class MsgPackStreamEntry
  {
    /// <summary>
    /// Where the entry's first root value starts.
    /// </summary>
    public long Offset { get; set; }

    /// <summary>
    /// The length in bytes of the entry's root values (an inline schema of the entry included), so a reader can fetch exactly these bytes (a range request, a memory mapped view) and a hash covers them.
    /// </summary>
    public long Length { get; set; }

    /// <summary>
    /// What the entry is called: a string (a name, a path with '/' as in tar and zip, the library does not interpret it) or an integer, null for none.
    /// Identifiers need not be unique: when several entries have the same one, the last one is the current one (earlier ones are older versions, see the "deleted" convention in <see cref="Metadata"/>).
    /// </summary>
    public object Identifier { get; set; }

    /// <summary>
    /// What the entry holds, e.g. a MIME type or the name of the root type, so a tool can show or open it without guessing. Null: not written.
    /// </summary>
    public string ContentType { get; set; }

    /// <summary>
    /// When the entry's content was last changed (UTC), null: not written.
    /// </summary>
    public DateTime? Modified { get; set; }

    /// <summary>
    /// The hash of the entry's bytes (<see cref="Offset"/> to <see cref="Offset"/> + <see cref="Length"/>) by <see cref="MsgPackStreamIndex.HashAlgorithm"/>, null: none.
    /// </summary>
    public byte[] Hash { get; set; }

    /// <summary>
    /// When the entry is one array (of known or unknown length): its number of elements, null otherwise.
    /// </summary>
    public long? ElementCount { get; set; }

    /// <summary>
    /// A sparse index into that array: where some of its elements start (every Nth, or where a key such as a timestamp changes), in element order, so a reader can start close to the element it looks for.
    /// Null: none.
    /// </summary>
    public List<MsgPackStreamIndexPoint> Points { get; set; }

    /// <summary>
    /// Where the data an <see cref="IMsgPackStreamObserver"/> returned for this entry is: the data of the extension value (<see cref="MsgPackStreamIndex.SidecarExtensionType"/>) that follows the entry, null: none.
    /// </summary>
    public long? SidecarOffset { get; set; }

    /// <summary>
    /// The length of that data in bytes (without the extension header).
    /// </summary>
    public int SidecarLength { get; set; }

    /// <summary>
    /// Any other data about the entry (flags, a deleted marker for an older entry with the same <see cref="Identifier"/>, a link to another entry, an encoding, ID3 tags, EXIF...),
    /// plain values (null, bool, integers, floats, strings, byte[], DateTime, and arrays and maps of them). Null: none.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; }
  }
}
