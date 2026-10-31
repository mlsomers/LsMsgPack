using System;

namespace LsMsgPack.Streaming
{
  /// <summary>
  /// Hashes the bytes of each entry of a stream (<see cref="MsgPackStreamEntry.Hash"/>), fed by the writer from its buffer: no extra flushes, no wrapping stream, the writer does not wait.
  /// Readers use the same algorithm to check an entry they read through the index.
  /// <para>An adapter for System.IO.Hashing (Crc32, Crc64, XxHash64, XxHash3, XxHash128) or IncrementalHash (SHA-256) is a few lines:
  /// <c>Append</c> calls their Append / AppendData, <c>GetHashAndReset</c> their GetHashAndReset.</para>
  /// </summary>
  public interface IMsgPackStreamHasher
  {
    /// <summary>
    /// The name written in the index (<see cref="MsgPackStreamIndex.HashAlgorithm"/>), e.g. "crc32", "xxh3-64", "sha256", by which a reader picks its hasher.
    /// </summary>
    string Algorithm { get; }

    /// <summary>
    /// The length of the hash in bytes.
    /// </summary>
    int HashLength { get; }

    /// <summary>
    /// The next bytes of the current entry. Called on the writer's thread, when the writer flushes its buffer and at the end of the entry: the span is only valid during the call.
    /// </summary>
    void Append(ReadOnlySpan<byte> data);

    /// <summary>
    /// The hash of the bytes appended since the last call, written into <paramref name="destination"/> (<see cref="HashLength"/> bytes), and starts over for the next entry.
    /// </summary>
    /// <returns>The number of bytes written</returns>
    int GetHashAndReset(Span<byte> destination);
  }
}
