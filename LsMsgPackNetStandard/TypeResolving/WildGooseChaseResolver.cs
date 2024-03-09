using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace LsMsgPack.TypeResolving
{
  /// <summary>
  /// Uses AppDomain.CurrentDomain.GetAssemblies(); to find a type.
  /// </summary>
  public class WildGooseChaseResolver : IMsgPackTypeResolver
  {
    public Type Resolve(string typeName, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<string, object> properties)
    {
      if (string.IsNullOrWhiteSpace(typeName))
        return null;

      // First try normal resolver:
      Type type = TypeResolver.ResolveInternal(typeName, assignedTo);
      if (type != null)
        return type;

      // Now go wild...
      Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
      for (int t = assemblies.Length - 1; t >= 0; t--)
      {
        if (TypeResolver.CachedAssembies.Contains(assemblies[t]))
        {
          Type tp = TypeResolver.CacheAssembly(assemblies[t], typeName);
          if (tp != null)
            return tp;
        }
      }
      return null;
    }
  }
}
