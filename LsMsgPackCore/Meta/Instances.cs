using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Creates the instances that are filled while deserializing.
  /// </summary>
  internal static class Instances
  {
    /// <summary>
    /// Whether the type has a (public or non-public) parameterless constructor, without one <see cref="Activator.CreateInstance(Type, bool)"/> would throw (slow) for every instance.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, bool> HasParameterlessConstructor = new ConcurrentDictionary<Type, bool>();

    internal static object Create(Type type)
    {
      if (HasParameterlessConstructor.GetOrAdd(type, t => t.IsValueType || t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null))
      {
        try
        {
          return Activator.CreateInstance(type, true);
        }
        catch { } // e.g. the constructor itself throws, fall back to an uninitialized instance
      }

      try
      {
        return System.Runtime.Serialization.FormatterServices.GetSafeUninitializedObject(type);
      }
      catch
      {
        return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
      }
    }
  }
}
