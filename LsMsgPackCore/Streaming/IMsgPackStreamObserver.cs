using System;

namespace LsMsgPack.Streaming
{
  /// <summary>
  /// Sees the bytes of each entry of a stream and may add to its index entry (<see cref="MsgPackStreamEntry.Metadata"/>, <see cref="MsgPackStreamEntry.ContentType"/>...)
  /// and return data to write after it (a sidecar: a signature, parity, a thumbnail...), e.g. computed incrementally from the bytes like a hash.
  /// <para>Called synchronously on the writer's thread, fed from the writer's buffer like an <see cref="IMsgPackStreamHasher"/>: keep it fast, the writer waits for it (it does not wait for the stream).</para>
  /// </summary>
  public interface IMsgPackStreamObserver
  {
    /// <summary>
    /// The next bytes of the current entry. Called when the writer flushes its buffer and at the end of the entry: the span is only valid during the call.
    /// </summary>
    void Append(ReadOnlySpan<byte> data);

    /// <summary>
    /// The entry ended: <paramref name="entry"/> has its <see cref="MsgPackStreamEntry.Offset"/>, <see cref="MsgPackStreamEntry.Length"/> and <see cref="MsgPackStreamEntry.Hash"/>, and may be completed.
    /// Its bytes may still be in the writer's buffer (they have not necessarily reached the stream).
    /// </summary>
    /// <returns>Data to write after the entry, wrapped in an extension value of type <see cref="MsgPackStreamIndex.SidecarExtensionType"/>
    /// (the index gets its <see cref="MsgPackStreamEntry.SidecarOffset"/> and <see cref="MsgPackStreamEntry.SidecarLength"/>); empty: nothing. The writer copies it before the next call.</returns>
    ReadOnlyMemory<byte> EntryEnded(MsgPackStreamEntry entry);
  }
}
