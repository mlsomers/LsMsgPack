namespace LsMsgPack.Streaming
{
  /// <summary>
  /// Whether the writer finished the stream after an index (<see cref="MsgPackStreamIndex.Status"/>).
  /// </summary>
  public enum MsgPackStreamIndexStatus
  {
    /// <summary>
    /// The writer was disposed between root values: the stream ends here, unless another part was appended (an index after this one points to it).
    /// </summary>
    Complete = 0,

    /// <summary>
    /// Written while the stream was written (every so many entries): more entries follow, with an index after them that points to this one.
    /// When this is the last index found, the writer stopped without finishing (a crash): the entries it lists are complete, what follows it is not indexed.
    /// </summary>
    Checkpoint = 1,

    /// <summary>
    /// The writer was disposed in the middle of a root value (an exception left arrays or maps open, or a head without its body): the last entry is <see cref="MsgPackStreamEntry.Incomplete"/>,
    /// and its arrays and maps are not closed (a reader that reads the stream value by value fails there).
    /// </summary>
    Interrupted = 2
  }
}
