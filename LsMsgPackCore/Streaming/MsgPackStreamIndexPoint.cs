namespace LsMsgPack.Streaming
{
  /// <summary>
  /// A point of the sparse index of an entry that is one array (<see cref="MsgPackStreamEntry.Points"/>): where one of its elements starts.
  /// </summary>
  public readonly struct MsgPackStreamIndexPoint
  {
    public MsgPackStreamIndexPoint(long element, long offset, object key = null)
    {
      Element = element;
      Offset = offset;
      Key = key;
    }

    /// <summary>
    /// The number of the element in the array (0 for the first).
    /// </summary>
    public long Element { get; }

    /// <summary>
    /// Where the element starts, relative to <see cref="MsgPackStreamEntry.Offset"/> (smaller than absolute offsets, and the same when the entry is copied elsewhere).
    /// </summary>
    public long Offset { get; }

    /// <summary>
    /// A value the elements are ordered by (a timestamp, an id), to search by it; null: none.
    /// </summary>
    public object Key { get; }
  }
}
