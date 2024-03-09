using System;
using System.Collections.Generic;

namespace LsMsgPack.TypeResolving.Interfaces
{
  /// <summary>
  /// Implement this class to resolve the type when having duplicate class names without using the fullName or when using ID's for each type in order to keep the payload smaller
  /// </summary>
  public interface IMsgPackTypeResolver
  {
    /// <summary>
    /// Resolve a type given it's name, the type it will be assigned to and properties it should have
    /// </summary>
    /// <param name="typeId">The name (or ID) of the class or struct to be resolved</param>
    /// <param name="assignedTo">The type of the property that will be set with an instance of the resolved type</param>
    /// <param name="assignedToProp">The property information (if any, may be null) of tyhe type it will be assigned to</param>
    /// <param name="properties">Names and values of properties that are going to be populated on an instance of the resolved type</param>
    /// <returns>The resolved type or null if no suiteble type is found.</returns>
    Type Resolve(string typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<string, object> properties);
  }
}
