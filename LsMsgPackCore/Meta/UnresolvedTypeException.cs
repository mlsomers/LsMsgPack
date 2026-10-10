namespace LsMsgPack.Meta
{
  /// <summary>
  /// The type id of a value (a name, or with the indexed schema the writer's class) did not resolve to a type that can be read.
  /// When the differences are collected (<see cref="MsgPackOptions._differences"/>) the value is skipped and reported instead (<see cref="ValueConverter"/>).
  /// </summary>
  internal sealed class UnresolvedTypeException : MsgPackException
  {
    internal UnresolvedTypeException(string typeName, string message)
      : base(message)
    {
      TypeName = typeName;
    }

    /// <summary>
    /// The name in the data.
    /// </summary>
    internal string TypeName { get; }
  }
}
