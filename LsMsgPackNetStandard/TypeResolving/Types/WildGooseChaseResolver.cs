using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace LsMsgPack.TypeResolving.Types
{
  /// <summary>
  /// Uses AppDomain.CurrentDomain.GetAssemblies(); to find a type.
  /// </summary>
  public class WildGooseChaseResolver : IMsgPackTypeResolver
  {
    public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackSettings settings)
    {
      return null; // use default
    }

    public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
    {
      string typeName = typeId as string;

      if (string.IsNullOrWhiteSpace(typeName))
        return null;

      // First try normal resolver:
      Type type = TypeResolver.ResolveInternal(typeName, assignedTo, Array.Empty<IMsgPackTypeResolver>());
      if (type != null)
        return type;

      // Now go wild...
      Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
      for (int t = assemblies.Length - 1; t >= 0; t--)
      {
        Type tp = TypeResolver.CacheAssembly(assemblies[t], typeName);
        if (tp != null)
          return tp;
      }
      return null;
    }
  }
}
