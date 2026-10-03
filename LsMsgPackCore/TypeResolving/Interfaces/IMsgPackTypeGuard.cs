using LsMsgPack.Meta;
using System;

namespace LsMsgPack.TypeResolving.Interfaces
{
  /// <summary>
  /// Decides which types the data may pick when deserializing (see <see cref="MsgPackOptions.TypeGuard"/> and docs/security.md).
  /// <para>Only asked when a type id (or a resolver) picks another type than the declared one, after all type resolvers and the indexed schema, before the instance is created.
  /// The picked type is always assignable to the declared type: that is checked without a guard.</para>
  /// </summary>
  public interface IMsgPackTypeGuard
  {
    /// <param name="type">The type the data picked, assignable to <paramref name="assignedTo"/></param>
    /// <param name="assignedTo">The declared type (of the property, the collection element or the root)</param>
    /// <param name="assignedToProp">The property the value will be assigned to (null for the root, collection elements and dictionary entries)</param>
    /// <param name="settings">The settings used for reading</param>
    /// <returns>false to refuse the type: deserializing throws a <see cref="MsgPackException"/> before an instance of it is created</returns>
    bool IsAllowed(Type type, Type assignedTo, FullPropertyInfo assignedToProp, MsgPackOptions settings);
  }
}
