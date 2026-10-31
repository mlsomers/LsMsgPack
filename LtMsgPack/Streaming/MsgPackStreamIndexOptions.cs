using LsMsgPack.Streaming;
using System.Collections.Generic;

namespace LtMsgPack.Streaming
{
  /// <summary>
  /// Makes a <see cref="MsgPackStreamWriter"/> keep an index of its root values and write it at the end of the stream (and at checkpoints): see <see cref="MsgPackStreamIndex"/> for the layout.
  /// The writer copies the values when it is created.
  /// </summary>
  public sealed class MsgPackStreamIndexOptions
  {
    /// <summary>
    /// Hashes the bytes of every entry (<see cref="MsgPackStreamEntry.Hash"/>), null: no hashes.
    /// </summary>
    public IMsgPackStreamHasher Hasher { get; set; }

    /// <summary>
    /// Sees the bytes of every entry, completes its index entry and may write a sidecar after it, null: none.
    /// </summary>
    public IMsgPackStreamObserver Observer { get; set; }

    /// <summary>
    /// Writes an index (<see cref="MsgPackStreamIndexStatus.Checkpoint"/>) after every so many entries and flushes the stream, so a writer that stops without finishing (a crash) leaves the entries before it indexed,
    /// and the writer keeps only the entries since the last checkpoint in memory. 0 (the default): only at the end.
    /// </summary>
    public int CheckpointInterval { get; set; }

    /// <summary>
    /// Adds a point to the sparse index (<see cref="MsgPackStreamEntry.Points"/>) every so many elements of an entry that is an array (the first element too), so a reader can start near any element.
    /// 0 (the default): only the points added by <see cref="MsgPackStreamWriter.AddPoint"/>.
    /// </summary>
    public long PointInterval { get; set; }

    /// <summary>
    /// What wrote the stream (<see cref="MsgPackStreamIndex.Writer"/>), "LtMsgPack" and its version by default; null: not written.
    /// </summary>
    public string Writer { get; set; } = DefaultWriter;

    /// <summary>
    /// Data about the whole stream (<see cref="MsgPackStreamIndex.Metadata"/>), written in every index. Plain values, checked when the writer is created.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; }

    /// <summary>
    /// The position the writer starts counting at: the length of the stream it appends to (as that writer counted). Set by <see cref="MsgPackStreamWriter.Append"/>.
    /// </summary>
    public long StartPosition { get; set; }

    /// <summary>
    /// Where the tail of the last index of the stream the writer appends to starts (<see cref="MsgPackStreamIndex.PreviousTail"/>), null for a new stream. Set by <see cref="MsgPackStreamWriter.Append"/>.
    /// </summary>
    public long? PreviousTail { get; set; }

    internal static readonly string DefaultWriter = "LtMsgPack " + typeof(MsgPackStreamIndexOptions).Assembly.GetName().Version;
  }
}
