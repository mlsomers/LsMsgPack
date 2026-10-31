namespace LsMsgPack.Streaming
{
  /// <summary>
  /// A schema the entries of a stream refer to (<see cref="MsgPackStreamIndex.Schemas"/>) and where its inline copy is.
  /// <para>Listed once per stream instead of per entry: the entries that refer to a schema written inline in an earlier entry find it here.</para>
  /// </summary>
  public readonly struct MsgPackStreamSchemaLocation
  {
    public MsgPackStreamSchemaLocation(SchemaId id, long? offset, int length)
    {
      Id = id;
      Offset = offset;
      Length = length;
    }

    public SchemaId Id { get; }

    /// <summary>
    /// Where the inline schema starts (it may lie inside an entry: the first object with the schema), null when the stream does not contain it (<c>MsgPackStreamSchemas.External</c>):
    /// the reader's <see cref="SchemaStore"/> must have it. Not 0 for "none", 0 is the start of the stream.
    /// </summary>
    public long? Offset { get; }

    /// <summary>
    /// The length of the inline schema in bytes, 0 when it is not in the stream.
    /// </summary>
    public int Length { get; }
  }
}
