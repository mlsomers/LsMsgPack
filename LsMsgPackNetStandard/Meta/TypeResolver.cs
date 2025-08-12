using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LsMsgPack.Meta
{
  internal static class TypeResolver
  {
    // 1st tier cache
    private static readonly Dictionary<string, Type> FullNameCache = new Dictionary<string, Type>(); // full name's should never collide
    private static readonly Dictionary<string, Type> UsedNameCache = new Dictionary<string, Type>(); // Previously resolved names

    // 2nd tier cache
    private static readonly Dictionary<Type, Assembly> AssemblyCache = new Dictionary<Type, Assembly>(); // assembly previously found for this "assign-to" type
    internal static readonly HashSet<Assembly> CachedAssembies = new HashSet<Assembly>(); // keep track of what has been cached

    // 3rd tier cache
    private static readonly Dictionary<string, HashSet<Type>> NameCache = new Dictionary<string, HashSet<Type>>(); // Can contain duplicate names

    internal static Type Resolve(object typeId, Type assignedTo, FullPropertyInfo rootProp, MpMap map, Dictionary<string, object> propVals)
    {
      Type result;
      // First give custom resolvers (if any) a chance...
      for (int t = (map.Settings?.TypeResolvers.Length ?? 0) - 1; t >= 0; t--)
      {
        IMsgPackTypeResolver resolver = map.Settings.TypeResolvers[t];
        result = resolver.Resolve(typeId, assignedTo, rootProp, propVals);
        if (result != null && !result.ContainsGenericParameters)
          return result;
      }

      string typeName = typeId as string;

      if (string.IsNullOrWhiteSpace(typeName))
      {
        if (assignedTo.IsAbstract || assignedTo.IsInterface)
          throw new Exception(string.Concat("Cannot create an instance of an interface or abstract type:\r\n  ", assignedTo.FullName,
            "\r\nEither use MsgPackSettings.AddTypeIdOptions when serializing (easiest but adds payload) or add a custom IMsgPackTypeResolver to MsgPackSettings.TypeResolvers."));
      }
      else
      {
        return ResolveInternal(typeName, assignedTo, map.Settings?.TypeResolvers);
      }

      return assignedTo;
    }

    internal static Type ResolveInternal(string typeName, Type assignedTo, IMsgPackTypeResolver[] resolvers)
    {
      Type result;

      if (typeName.EndsWith("[]"))
      {
        string nm=typeName.Substring(0, typeName.Length -2);
        Type arr= ResolveInternal(nm, assignedTo, resolvers);
        if(arr != null)
          return arr.MakeArrayType();
      }

      // 1st tier
      if (FullNameCache.TryGetValue(typeName, out result))
        return result;
      if (UsedNameCache.TryGetValue(typeName, out result))
        return result;

      if (!string.IsNullOrWhiteSpace(typeName) && typeName.IndexOf('<', 1) > 0)
      {
        result = SplitByParsing(typeName, resolvers);
        if (result != null)
        {
          UsedNameCache.Add(typeName, result);
          return result;
        }
      }

      return ResolveName(typeName, assignedTo);
    }

    private static Type SplitByParsing(string args, IMsgPackTypeResolver[] resolvers)
    {
      Stack<KeyValuePair<string, Type[]>> stack = new Stack<KeyValuePair<string, Type[]>>();

      int idx = 0;
      List<Type> genArgs = new List<Type>();
      StringBuilder sb = new StringBuilder();
      while (idx < args.Length)
      {
        char c = args[idx];
        idx++;
        if (c == '<')
        {
          stack.Push(new KeyValuePair<string, Type[]>(sb.ToString(), genArgs.ToArray()));
          sb.Clear();
          genArgs.Clear();
          continue;
        }
        else if (c == '>')
        {
          string typename = sb.ToString();
          sb.Clear();
          if (typename.Length > 0)
          {
            Type type = ResolveIndirect(typename, resolvers);
            genArgs.Add(type);
          }

          KeyValuePair<string, Type[]> gen = stack.Pop();
          Type genericType = ResolveIndirect(string.Concat(gen.Key + '`' + genArgs.Count), resolvers); // https://learn.microsoft.com/en-us/dotnet/api/system.type.gettype
          Type spcificGenericType = genericType.MakeGenericType(genArgs.ToArray());
          genArgs.Clear();
          genArgs.AddRange(gen.Value); // restore parent args
          genArgs.Add(spcificGenericType); // and add the nested generic type
        }
        else if (c == ',')
        {
          string typename = sb.ToString();
          sb.Clear();
          Type type = ResolveIndirect(typename, resolvers);
          genArgs.Add(type);
        }
        else if (char.IsWhiteSpace(c))
          continue;
        else
          sb.Append(c);
      }
      if (sb.Length > 0)
      {
        string typename = sb.ToString();
        Type type = ResolveIndirect(typename, resolvers);
        genArgs.Add(type);
      }

      return genArgs[0];
    }

    /// <summary>
    /// Type is part of a generic argument
    /// </summary>
    private static Type ResolveIndirect(string typeName, IMsgPackTypeResolver[] resolvers)
    {
      Type result;
      // 1st tier
      if (FullNameCache.TryGetValue(typeName, out result))
        return result;
      if (UsedNameCache.TryGetValue(typeName, out result))
        return result;

      for (int t = resolvers.Length - 1; t >= 0; t--)
      {
        result = resolvers[t].Resolve(typeName, null, null, null);
        if (result != null)
        {
          UsedNameCache.Add(typeName, result);
          return result;
        }
      }

      result = ResolveName(typeName, null);
      if (result is null)
        throw new Exception(string.Concat("Unable to resolve the type \"", typeName,
          "\".\r\nEither create a resolver by implementing and using IMsgPackTypeResolver or pre-cache your type like this:\r\n  MsgPackSerializer.CacheAssemblyTypes(typeof(",
          typeName, "));")); // Or add an assembly to NativeAssemblies

      return result;
    }

    private static Assembly[] NativeAssemblies =new Assembly[]
    {
      typeof(List<>).Assembly, // System.Collections.Generic
      typeof(ConcurrentBag<>).Assembly, // System.Collections.Concurrent
      typeof(ObservableCollection<>).Assembly // System.Collections.ObjectModel
    };

    private static Type ResolveName(string typeName, Type assignedTo)
    {
      Type result;
      // 1st tier
      if (FullNameCache.TryGetValue(typeName, out result))
        return result;
      if (UsedNameCache.TryGetValue(typeName, out result))
        return result;

      // First try offloading this work to the framework...
      result = Type.GetType(typeName, false, true);
      if (result == null)
      {
        // search all types from System.Collections.Generic
        for (int t = 0; t < NativeAssemblies.Length; t++)
        {
          Assembly assm = NativeAssemblies[t];
          if (!CachedAssembies.Contains(assm))
          {
            result = CacheAssembly(assm, typeName);
            if (result != null)
              return result; // has already been added to cache, code below would crash
          } 
        }
      }
      if (result != null)
      {
        if (typeName.Contains("."))
          FullNameCache.Add(typeName, result);
        else
          UsedNameCache.Add(typeName, result);
        return result;
      }

      // 2nd tier (use the assembly of the type it is assigned to)
      Assembly assembly = null;
      if (assignedTo != null && !AssemblyCache.TryGetValue(assignedTo, out assembly))
      {
        if (assignedTo != typeof(object))
          assembly = assignedTo.Assembly;

        //if (assembly != null) // Also cache null for a specific assign-to type so this block can be skipped for the next item
        AssemblyCache.Add(assignedTo, assembly);
      }

      Type[] argTypes = assignedTo?.GenericTypeArguments;
      for (int t = (argTypes?.Length ?? 0) - 1; t >= 0; t--)
        CacheAssembly(argTypes[t].Assembly, argTypes[t].Name);

      HashSet<Type> choices;
      if (assembly != null)
      {
        result = assembly.GetType(typeName, false, true);
        if (result is null)
        {
          if (!CachedAssembies.Contains(assembly))
            result = CacheAssembly(assembly, typeName); // At this point we search all types in the assembly
          else // retreive from NameCache
          {
            // The assembly has previously been cached, but the type was not found by full name...
            if (NameCache.TryGetValue(typeName, out choices))
            {
              if (choices.Count == 1)
              {
                result = choices.First();
                UsedNameCache.Add(typeName, result);
                return result;
              }
              if (choices.Count > 1)
              {
                throw new Exception(string.Concat("Type assignment dilamma for \"", typeName, "\" with the following choices:\r\n  ",
                  string.Join("\r\n  ", choices.Select(t => t.FullName)),
                  "\r\nFix this by either serializing with full name or implementing a IMsgPackTypeResolver and add it to MsgPackSettings.TypeResolvers."));
              }
            }
          }

          if (result != null)
            return result;
        }
        else // full type name used..
        {
          FullNameCache.Add(typeName, result);
          return result;
        }
      }

      // 3rd tier, cached names (generic types)
      if (NameCache.TryGetValue(typeName, out choices))
      {
        if (choices.Count == 1)
        {
          result = choices.First();
          UsedNameCache.Add(typeName, result);
          return result;
        }
        if (choices.Count > 1)
        {
          throw new Exception(string.Concat("Type assignment dilamma for \"", typeName, "\" with the following choices:\r\n  ",
            string.Join("\r\n  ", choices.Select(t => t.FullName)),
            "\r\nFix this by either serializing with full name or implementing a IMsgPackTypeResolver and add it to MsgPackSettings.TypeResolvers."));
        }
      }

      return assignedTo; // will probably fail
    }

    internal static Type CacheAssembly(Assembly assembly, string typeName)
    {
      if (CachedAssembies.Contains(assembly))
        return null;

      Type found = null;
      if (string.IsNullOrEmpty(typeName))
        found = typeof(object); // skip the find part, just cache assembly

      Type[] types = assembly.GetTypes();
      for (int t = types.Length - 1; t >= 0; t--)
      {
        string fullName = types[t].FullName;
        string name = types[t].Name;
        Type type = types[t];
        FullNameCache.TryAdd(fullName, type);
        if (!NameCache.TryAdd(name, new HashSet<Type> { type }))
          NameCache[name].Add(type);

        // check if found but don't bail out when found, once we start cahcing an assembly we'll finish the job!
        if (found == null)
        {
          if (fullName == typeName)
          {
            FullNameCache.Add(fullName, type);
            found = type;
          }
          else if (name == typeName)
          {
            UsedNameCache.Add(name, type);
            found = type;
          }
        }
      }
      CachedAssembies.Add(assembly);
      return found;
    }
  }
}
