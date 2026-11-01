namespace LtMsgPack.Data
{
  /// <summary>
  /// How <see cref="StreamWriterDataExtensions.WriteTable"/> and <see cref="StreamWriterDataExtensions.WriteResultSets"/> write a result set.
  /// </summary>
  public sealed class MsgPackTableOptions
  {
    /// <summary>
    /// The name of the rows' entry in the indexed schema: the class readers pair with the rows when they read them as a <c>List&lt;T&gt;</c> (a name that resolves to no class is paired with the root's elements).
    /// null: the table's name, otherwise <see cref="MsgPackDataStreams.DefaultTypeName"/>.
    /// </summary>
    public string RowTypeName { get; set; }

    /// <summary>
    /// With an index (<see cref="Streaming.MsgPackStreamIndexOptions"/>): a column the rows are ordered by, a point of the sparse index is added where its value changes
    /// (its value as the point's key: a reader seeks to it), besides the points of <see cref="Streaming.MsgPackStreamIndexOptions.PointInterval"/>. null: none.
    /// </summary>
    public string PointColumn { get; set; }

    /// <summary>
    /// With an index: the columns (name, .NET type, the provider's type name) in the metadata of the table's entry (<see cref="MsgPackDataStreams.ColumnsKey"/>),
    /// so <see cref="MsgPackDataReader"/> knows their exact types. On by default.
    /// </summary>
    public bool ColumnMetadata { get; set; } = true;
  }
}
