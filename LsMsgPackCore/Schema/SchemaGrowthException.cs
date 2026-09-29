using System;

namespace LsMsgPack
{
  /// <summary>
  /// Thrown when a call with a frozen (shared) schema session needs a type, property or cache entry the session does not have yet.
  /// <para>Internal flow control, never seen by users: the caller catches it and repeats the call with an extended copy of the session (see <see cref="SchemaSession"/>).
  /// It only happens while a session grows, so it is rare once the types of an application have been seen.</para>
  /// </summary>
  internal sealed class SchemaGrowthException : Exception
  {
    private SchemaGrowthException() : base("The cached schema session needs to grow.") { }

    /// <summary>
    /// One instance (thrown by several threads at the same time), the stack trace is never used.
    /// </summary>
    internal static readonly SchemaGrowthException Instance = new SchemaGrowthException();
  }
}
