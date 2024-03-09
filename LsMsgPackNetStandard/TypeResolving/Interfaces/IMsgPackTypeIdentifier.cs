using System;
using System.Reflection;

namespace LsMsgPack.TypeResolving.Interfaces
{
  /// <summary>
  /// Used when writing to MsgPack, in order to support polymorphic class hierarchies and keep a small payload footprint
  /// </summary>
  public interface IMsgPackTypeIdentifier
  {
    /// <summary>
    /// Generate a string by wich the type can be identified later
    /// </summary>
    /// <param name="type">Type of the object</param>
    /// <param name="assignedTo">The property information (if any, may be null) of tyhe type it will be assigned to</param>
    /// <returns>A string by wich the type can be identified when deserializing</returns>
    string IdForType(Type type, FullPropertyInfo assignedTo);
  }
}
