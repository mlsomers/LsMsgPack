namespace LsMsgPack.Data
{
  /// <summary>
  /// The names of tables in a stream (<see cref="StreamWriterDataExtensions"/>, <see cref="MsgPackDataReader"/>).
  /// </summary>
  public static class MsgPackDataStreams
  {
    /// <summary>
    /// The content type of a table's index entry (<see cref="LsMsgPack.Streaming.MsgPackStreamEntry.ContentType"/>).
    /// </summary>
    public const string ContentType = "application/x-lsmsgpack-rows";

    /// <summary>
    /// The key of the columns in a table's index entry metadata: <c>[[name, type, dataTypeName], ...]</c>, the type a .NET type name (<c>System.Int32</c>).
    /// </summary>
    public const string ColumnsKey = "columns";

    /// <summary>
    /// The name of the rows' entry in the indexed schema when none is given.
    /// </summary>
    public const string DefaultTypeName = "Row";
  }
}
