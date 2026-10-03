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
    // The caches are shared by all threads, guard them with this lock (it is re-entrant, custom resolvers may call back into this class)
    private static readonly object SyncRoot = new object();

    // 1st tier cache
    private static readonly Dictionary<string, Type> FullNameCache = new Dictionary<string, Type>(); // full name's should never collide
    private static readonly Dictionary<string, Type> UsedNameCache = new Dictionary<string, Type>(); // Previously resolved names

    // 2nd tier cache
    private static readonly Dictionary<Type, Assembly> AssemblyCache = new Dictionary<Type, Assembly>(); // assembly previously found for this "assign-to" type
    internal static readonly HashSet<Assembly> CachedAssembies = new HashSet<Assembly>(); // keep track of what has been cached
    private static readonly ConcurrentDictionary<Assembly, bool> CachedAssembliesLockFree = new ConcurrentDictionary<Assembly, bool>(); // the same, read without the lock (both serializers cache the assembly of the root type on every call)

    // 3rd tier cache
    private static readonly Dictionary<string, HashSet<Type>> NameCache = new Dictionary<string, HashSet<Type>>(); // Can contain duplicate names

    internal static string GetTypeName(Type type, bool fullname)
    {
      if (type.IsArray) // "KeyValuePair<Int32, String>[]" instead of "KeyValuePair`2[]"
        return $"{GetTypeName(type.GetElementType(), fullname)}[]";

      Type[] args = type.GenericTypeArguments;

      if (args.Length == 0)
        return fullname ? type.FullName : type.Name;

      // Get the generic type name...

      string[] names = new string[args.Length];
      for (int t = args.Length - 1; t >= 0; t--)
        names[t] = GetTypeName(args[t], fullname);

      string typeName = fullname ? type.FullName : type.Name;
      typeName = typeName.Substring(0, typeName.IndexOf('`'));

      return $"{typeName}<{string.Join(", ", names)}>";
    }

    /// <summary>
    /// Returns <paramref name="assignedTo"/> when no (more specific) type could be resolved, it is up to the caller to decide if that type can be instantiated.
    /// </summary>
    /// <exception cref="MsgPackException">When the resolved type cannot be assigned to <paramref name="assignedTo"/> or the <see cref="MsgPackOptions.TypeGuard"/> refuses it (see docs/security.md)</exception>
    internal static Type Resolve(object typeId, Type assignedTo, FullPropertyInfo rootProp, MsgPackOptions settings, Dictionary<object, object> propVals)
    {
      Type result = ResolveUnchecked(typeId, assignedTo, rootProp, settings, propVals);
      if (result != assignedTo && result != null) // the data picked the type: before anything creates an instance of it
      {
        ThrowIfNotAssignable(result, assignedTo, typeId);
        ThrowIfNotAllowed(result, assignedTo, rootProp, settings, typeId);
      }
      return result;
    }

    private static Type ResolveUnchecked(object typeId, Type assignedTo, FullPropertyInfo rootProp, MsgPackOptions settings, Dictionary<object, object> propVals)
    {
      Type result;
      // First give custom resolvers (if any) a chance...
      for (int t = (settings?._typeResolvers.Length ?? 0) - 1; t >= 0; t--)
      {
        IMsgPackTypeResolver resolver = settings._typeResolvers[t];
        result = resolver.Resolve(typeId, assignedTo, rootProp, propVals, settings);
        if (result != null && !result.ContainsGenericParameters)
          return result;
      }

      string typeName = typeId as string;

      if (!string.IsNullOrWhiteSpace(typeName))
        return ResolveInternal(typeName, assignedTo, settings?.TypeResolvers);

      return assignedTo;
    }

    /// <summary>
    /// A type picked by the data must fit where it goes, otherwise any type the resolvers can find would be created and its setters called (only then would assigning it fail).
    /// </summary>
    internal static void ThrowIfNotAssignable(Type type, Type assignedTo, object typeId)
    {
      Type target = Nullable.GetUnderlyingType(assignedTo) ?? assignedTo;
      if (!target.IsAssignableFrom(type))
        throw new MsgPackException($"The type id {DescribeTypeId(typeId)} resolves to {type.FullName}, which cannot be assigned to {assignedTo.FullName}. Deserializing stops before creating it (see docs/security.md).");
    }

    /// <summary>
    /// Asks the <see cref="MsgPackOptions.TypeGuard"/> (if any) about a type picked by the data, assignable to <paramref name="assignedTo"/>.
    /// </summary>
    internal static void ThrowIfNotAllowed(Type type, Type assignedTo, FullPropertyInfo prop, MsgPackOptions settings, object typeId)
    {
      IMsgPackTypeGuard guard = settings?._typeGuard;
      if (guard != null && !guard.IsAllowed(type, assignedTo, prop, settings))
        throw new MsgPackException($"The type id {DescribeTypeId(typeId)} resolves to {type.FullName}, which the {nameof(MsgPackOptions.TypeGuard)} ({guard.GetType().Name}) does not allow{(prop is null ? "" : $" for {prop.PropertyInfo.DeclaringType?.Name}.{prop.PropertyInfo.Name}")} (declared as {assignedTo.FullName}, see docs/security.md).");
    }

    private static string DescribeTypeId(object typeId)
    {
      if (typeId is null)
        return "(none, a type resolver picked the type)";
      if (typeId is Type type) // LtMsgPack: the type was found by a name or schema index
        return $"\"{type.Name}\"";
      return typeId is string ? $"\"{typeId}\"" : typeId.ToString();
    }

    internal static Type ResolveInternal(string typeName, Type assignedTo, IMsgPackTypeResolver[] resolvers)
    {
      lock (SyncRoot)
      {
        return ResolveInternalLocked(typeName, assignedTo, resolvers);
      }
    }

    private static Type ResolveInternalLocked(string typeName, Type assignedTo, IMsgPackTypeResolver[] resolvers)
    {
      Type result;

      if (typeName.EndsWith("[]", StringComparison.Ordinal))
      {
        string nm = typeName.Substring(0, typeName.Length - 2);
        Type arr = ResolveInternalLocked(nm, assignedTo, resolvers);
        if (arr != null)
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
          Type genericType = ResolveIndirect(string.Concat($"{gen.Key}`{genArgs.Count}"), resolvers); // https://learn.microsoft.com/en-us/dotnet/api/system.type.gettype
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
      if (typeName.EndsWith("[]", StringComparison.Ordinal))
        return ResolveIndirect(typeName.Substring(0, typeName.Length - 2), resolvers).MakeArrayType();

      Type result;
      // 1st tier
      if (FullNameCache.TryGetValue(typeName, out result))
        return result;
      if (UsedNameCache.TryGetValue(typeName, out result))
        return result;

      for (int t = resolvers.Length - 1; t >= 0; t--)
      {
        result = resolvers[t].Resolve(typeName, null, null, null, null);
        if (result != null)
        {
          UsedNameCache.Add(typeName, result);
          return result;
        }
      }

      result = ResolveName(typeName, null);
      if (result is null)
        throw new Exception(
          $"Unable to resolve the type \"{typeName}\".\r\nEither create a resolver by implementing and using IMsgPackTypeResolver or pre-cache your type like this:\r\n  MsgPackSerializer.CacheAssemblyTypes(typeof({typeName}));"); // Or add an assembly to NativeAssemblies

      return result;
    }

    private static Assembly[] NativeAssemblies = new Assembly[]
    {
      typeof(List<>).Assembly, // System.Collections.Generic
      typeof(SortedDictionary<,>).Assembly, // System.Collections (SortedDictionary, SortedSet, LinkedList, Stack, Queue...)
      typeof(ConcurrentBag<>).Assembly, // System.Collections.Concurrent
      typeof(ObservableCollection<>).Assembly // System.Collections.ObjectModel
    };

    /// <summary>
    /// Characters of assembly-qualified names, bracketed generic arguments ("List`1[[System.Diagnostics.Process, System]]"), pointers and references: the serializers never write them in a type name
    /// (generic arguments are written as "List&lt;Process&gt;" and split before they get here), and <see cref="Type.GetType(string)"/> or <see cref="Assembly.GetType(string)"/> would load the assemblies they name.
    /// </summary>
    private static readonly char[] UnsafeNameChars = new[] { ',', '[', ']', '&', '*' };

    private static Type ResolveName(string typeName, Type assignedTo)
    {
      Type result;
      // 1st tier
      if (FullNameCache.TryGetValue(typeName, out result))
        return result;
      if (UsedNameCache.TryGetValue(typeName, out result))
        return result;

      if (typeName.IndexOfAny(UnsafeNameChars) >= 0) // the data must not choose assemblies to load (see docs/security.md)
        throw new MsgPackException($"The type name \"{typeName}\" is not resolved: assembly-qualified names and bracketed generic arguments could load assemblies. Use a custom IMsgPackTypeResolver to resolve such names.");

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
                throw new Exception(
                  $"Type assignment dilamma for \"{typeName}\" with the following choices:\r\n  {string.Join("\r\n  ", choices.Select(t => t.FullName))}\r\nFix this by either serializing with full name or implementing a IMsgPackTypeResolver and add it to MsgPackSettings._typeResolvers.");
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
          throw new Exception(
            $"Type assignment dilamma for \"{typeName}\" with the following choices:\r\n  {string.Join("\r\n  ", choices.Select(t => t.FullName))}\r\nFix this by either serializing with full name or implementing a IMsgPackTypeResolver and add it to MsgPackSettings._typeResolvers.");
        }
      }

      return assignedTo; // will probably fail
    }

    internal static Type CacheAssembly(Assembly assembly, string typeName)
    {
      if (CachedAssembliesLockFree.ContainsKey(assembly)) // assemblies are only added: the answer of the lock below
        return null;

      lock (SyncRoot)
      {
        return CacheAssemblyLocked(assembly, typeName);
      }
    }

    private static Type CacheAssemblyLocked(Assembly assembly, string typeName)
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
            found = type; // already added to FullNameCache above
          else if (name == typeName)
          {
            UsedNameCache.TryAdd(name, type);
            found = type;
          }
        }
      }
      CachedAssembies.Add(assembly);
      CachedAssembliesLockFree.TryAdd(assembly, true);
      return found;
    }
  }
}
