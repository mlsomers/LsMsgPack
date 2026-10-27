using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
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
      if (type.IsArray) // "KeyValuePair<Int32, String>[]" instead of "KeyValuePair`2[]", "Int32[,]" for two dimensions
        return $"{GetTypeName(type.GetElementType(), fullname)}[{new string(',', type.GetArrayRank() - 1)}]";

      Type[] args = type.GenericTypeArguments;

      if (args.Length == 0)
        return AnonymousName(type, fullname ? type.FullName : type.Name);

      if (IsNestedInGeneric(type))
        return NestedInGenericName(type, args, fullname);

      // Get the generic type name...

      string[] names = new string[args.Length];
      for (int t = args.Length - 1; t >= 0; t--)
        names[t] = GetTypeName(args[t], fullname);

      string typeName = fullname ? type.FullName : type.Name;
      typeName = AnonymousName(type, typeName.Substring(0, typeName.IndexOf('`')));

      return $"{typeName}<{string.Join(", ", names)}>";
    }

    /// <summary>
    /// The name of an anonymous type without the prefix of the compiler ("&lt;&gt;f__AnonymousType0&lt;Int32, String&gt;" is "AnonymousType0&lt;Int32, String&gt;"):
    /// a name that starts with '&lt;' is not split into its generic arguments, so a released reader refused it (with the ',' of its arguments) and the schema it is in.
    /// No reader resolves it either way (the type is internal to the writer's assembly), see SerializationRules.HasAnonymousType.
    /// </summary>
    private static string AnonymousName(Type type, string name)
    {
      if (!SerializationRules.IsAnonymous(type))
        return name;
      int start = name.IndexOf("AnonymousType", StringComparison.Ordinal);
      return start > 0 ? name.Substring(start) : name;
    }

    private static bool IsNestedInGeneric(Type type)
    {
      for (Type outer = type.DeclaringType; outer != null; outer = outer.DeclaringType)
        if (outer.IsGenericType)
          return true;
      return false;
    }

    /// <summary>
    /// A class nested in a generic class has the generic arguments of the classes around it (Outer&lt;T&gt;.Inner is Outer`1+Inner[T]): each class gets its own ones, joined by '+' as in its full name,
    /// "Outer&lt;Int32&gt;+Inner", "Gen&lt;Int32&gt;+NestedGen&lt;String&gt;" (full names "Ns.Outer&lt;System.Int32&gt;+Inner"). The short name starts at the outermost class that has generic parameters.
    /// <para>Until October 2026 the full name was the one of the outer class ("Ns.Outer&lt;System.Int32&gt;", every argument after it) and the short name threw.</para>
    /// </summary>
    private static string NestedInGenericName(Type type, Type[] args, bool fullname)
    {
      List<Type> chain = new List<Type>(); // the definitions, innermost first
      for (Type level = type.GetGenericTypeDefinition(); level != null; level = level.DeclaringType)
        chain.Add(level);

      StringBuilder name = new StringBuilder();
      int used = 0;
      for (int t = chain.Count - 1; t >= 0; t--)
      {
        Type level = chain[t];
        int count = level.GetGenericArguments().Length; // a nested definition declares the parameters of the classes around it too
        if (name.Length > 0)
          name.Append('+');
        if (fullname || name.Length > 0 || count > used)
          name.Append(WithoutArity(fullname && t == chain.Count - 1 ? level.FullName : level.Name));
        if (count > used)
        {
          name.Append('<');
          for (int a = used; a < count; a++)
            name.Append(a == used ? "" : ", ").Append(GetTypeName(args[a], fullname));
          name.Append('>');
          used = count;
        }
      }
      return name.ToString();
    }

    private static string WithoutArity(string name)
    {
      int arity = name.IndexOf('`');
      return arity < 0 ? name : name.Substring(0, arity);
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
      {
        if (settings?._differences is null)
          return ResolveInternal(typeName, assignedTo, settings?.TypeResolvers);
        return ResolveOrReport(typeName, assignedTo, settings);
      }

      return assignedTo;
    }

    /// <summary>
    /// A name that is not found is read as the declared type (as without the differences), and reported. When the declared type cannot be created the value is skipped (see <see cref="UnresolvedTypeException"/>).
    /// </summary>
    private static Type ResolveOrReport(string typeName, Type assignedTo, MsgPackOptions settings)
    {
      Type found = ResolveInternal(typeName, assignedTo, settings.TypeResolvers, false);
      if (found != null || assignedTo is null)
        return found ?? assignedTo;
      if (assignedTo.IsAbstract || assignedTo.IsInterface)
        throw new UnresolvedTypeException(typeName, $"Unable to resolve the type \"{typeName}\" (declared as {assignedTo.FullName}).");
      settings._differences.UnresolvedType(typeName, assignedTo, null);
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

    /// <param name="orAssignedTo">Return <paramref name="assignedTo"/> when the name is not found (it is up to the caller to fail), otherwise null</param>
    internal static Type ResolveInternal(string typeName, Type assignedTo, IMsgPackTypeResolver[] resolvers, bool orAssignedTo = true)
    {
      lock (SyncRoot)
      {
        return ResolveInternalLocked(typeName, assignedTo, resolvers, orAssignedTo);
      }
    }

    private static Type ResolveInternalLocked(string typeName, Type assignedTo, IMsgPackTypeResolver[] resolvers, bool orAssignedTo)
    {
      Type result;

      if (IsArrayName(typeName, out string elementName, out int rank))
      {
        Type element = ResolveInternalLocked(elementName, assignedTo?.IsArray == true ? assignedTo.GetElementType() : null, resolvers, false);
        if (element != null)
          return MakeArrayType(element, rank);
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

      return ResolveName(typeName, assignedTo, orAssignedTo);
    }

    /// <summary>
    /// A name as <see cref="GetTypeName"/> writes generic types: "Dictionary&lt;String, List&lt;Int32&gt;&gt;", "List&lt;Gen&lt;Int32&gt;[]&gt;", "Outer&lt;Int32&gt;+Inner" (a class nested in a generic class).
    /// </summary>
    /// <returns>null when the name is not of that form</returns>
    /// <exception cref="Exception">When a name in it is not found</exception>
    private static Type SplitByParsing(string name, IMsgPackTypeResolver[] resolvers)
    {
      int idx = 0;
      Type type = ParseType(name, ref idx, resolvers);
      SkipWhiteSpace(name, ref idx);
      return idx == name.Length ? type : null;
    }

    /// <summary>
    /// A type: a name with or without generic arguments, the classes nested in it after '+' (the '+' of a full name without generic arguments is part of the name), then array ranks.
    /// </summary>
    private static Type ParseType(string name, ref int idx, IMsgPackTypeResolver[] resolvers)
    {
      Type type = ParseGeneric(name, ref idx, null, resolvers);
      while (type != null && idx < name.Length && name[idx] == '+')
      {
        idx++;
        type = ParseGeneric(name, ref idx, type, resolvers);
      }
      while (type != null && idx < name.Length && name[idx] == '[')
      {
        int close = idx + 1;
        while (close < name.Length && name[close] == ',')
          close++;
        if (close == name.Length || name[close] != ']')
          return null;
        type = MakeArrayType(type, close - idx);
        idx = close + 1;
      }
      SkipWhiteSpace(name, ref idx);
      return type;
    }

    /// <param name="outer">The (generic) class the name is nested in, null for a name that is resolved by itself</param>
    private static Type ParseGeneric(string name, ref int idx, Type outer, IMsgPackTypeResolver[] resolvers)
    {
      SkipWhiteSpace(name, ref idx);
      int start = idx;
      while (idx < name.Length && name[idx] != '<' && name[idx] != '>' && name[idx] != ',' && name[idx] != '[' && !char.IsWhiteSpace(name[idx]))
        idx++;
      if (idx == start)
        return null;
      string typeName = name.Substring(start, idx - start);
      SkipWhiteSpace(name, ref idx);

      List<Type> args = null;
      if (idx < name.Length && name[idx] == '<')
      {
        idx++;
        args = new List<Type>();
        while (true)
        {
          Type arg = ParseType(name, ref idx, resolvers);
          if (arg is null || idx == name.Length)
            return null;
          args.Add(arg);
          char c = name[idx++];
          if (c == '>')
            break;
          if (c != ',')
            return null;
        }
      }

      if (outer != null)
        return Nested(outer, typeName, args);
      if (args is null)
        return ResolveIndirect(typeName, resolvers);
      return MakeGenericType(ResolveIndirect($"{typeName}`{args.Count}", resolvers), args.ToArray()); // https://learn.microsoft.com/en-us/dotnet/api/system.type.gettype
    }

    /// <summary>
    /// The class <paramref name="names"/> ("Inner", "Mid+Inner") nested in the generic class <paramref name="outer"/>, with the generic arguments of <paramref name="outer"/> and its own ones.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.TypeNames)]
    private static Type Nested(Type outer, string names, List<Type> args)
    {
      Type nested = outer.GetGenericTypeDefinition();
      string[] levels = names.Split('+');
      for (int t = 0; t < levels.Length && nested != null; t++)
      {
        string level = t == levels.Length - 1 && args != null ? $"{levels[t]}`{args.Count}" : levels[t];
        nested = Visible(nested.GetNestedType(level, BindingFlags.Public | BindingFlags.NonPublic));
      }
      if (nested is null)
        throw new Exception($"Unable to resolve the type \"{names}\" nested in {GetTypeName(outer, true)}.");

      Type[] outerArgs = outer.GenericTypeArguments;
      if (args is null && !nested.IsGenericTypeDefinition)
        return nested;
      Type[] all = new Type[outerArgs.Length + (args?.Count ?? 0)];
      outerArgs.CopyTo(all, 0);
      args?.CopyTo(all, outerArgs.Length);
      return MakeGenericType(nested, all);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = TrimReasons.TypeNames)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.TypeNames)]
    private static Type MakeGenericType(Type definition, Type[] args)
    {
      return definition.MakeGenericType(args);
    }

    private static void SkipWhiteSpace(string name, ref int idx)
    {
      while (idx < name.Length && char.IsWhiteSpace(name[idx]))
        idx++;
    }

    /// <summary>
    /// "Name[]" or "Name[,]"...: the name of the element type and the number of dimensions.
    /// </summary>
    private static bool IsArrayName(string typeName, out string elementName, out int rank)
    {
      elementName = null;
      rank = 0;
      if (!typeName.EndsWith("]", StringComparison.Ordinal))
        return false;
      int open = typeName.LastIndexOf('[');
      if (open <= 0)
        return false;
      for (int t = typeName.Length - 2; t > open; t--)
        if (typeName[t] != ',')
          return false;
      elementName = typeName.Substring(0, open);
      rank = typeName.Length - open - 1;
      return true;
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.TypeNames)]
    private static Type MakeArrayType(Type element, int rank)
    {
      return rank == 1 ? element.MakeArrayType() : element.MakeArrayType(rank);
    }

    /// <summary>
    /// Type is part of a generic argument
    /// </summary>
    private static Type ResolveIndirect(string typeName, IMsgPackTypeResolver[] resolvers)
    {
      if (IsArrayName(typeName, out string elementName, out int rank))
        return MakeArrayType(ResolveIndirect(elementName, resolvers), rank);

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

      result = ResolveName(typeName, null, false);
      if (result is null)
        throw new Exception(
          $"Unable to resolve the type \"{typeName}\".\r\nEither create a resolver by implementing and using IMsgPackTypeResolver or pre-cache your type like this:\r\n  MsgPackTypes.CacheAssemblyTypes(typeof({typeName}));"); // Or add an assembly to NativeAssemblies

      return result;
    }

    private static readonly Assembly[] NativeAssemblies = GetNativeAssemblies();

    private static Assembly[] GetNativeAssemblies()
    {
      List<Assembly> assemblies = new List<Assembly>()
      {
        typeof(List<>).Assembly, // System.Collections.Generic (System.Private.CoreLib on .NET: also Version, Half, Int128, tuples...)
        typeof(SortedDictionary<,>).Assembly, // System.Collections (SortedDictionary, SortedSet, LinkedList, Stack, Queue, BitArray...)
        typeof(ConcurrentBag<>).Assembly, // System.Collections.Concurrent
        typeof(ObservableCollection<>).Assembly, // System.Collections.ObjectModel
        typeof(System.Numerics.BigInteger).Assembly // System.Runtime.Numerics (BigInteger, Complex)
      };
      Type immutable = FrameworkTypeInfo.ImmutableArrayType; // System.Collections.Immutable, when the application has it (not referenced by .NET Standard)
      if (immutable != null)
        assemblies.Add(immutable.Assembly);
      for (int t = assemblies.Count - 1; t > 0; t--)
        if (assemblies.IndexOf(assemblies[t]) != t) // the same assembly on .NET Framework (mscorlib, System)
          assemblies.RemoveAt(t);
      return assemblies.ToArray();
    }

    /// <summary>
    /// The framework's own types a name in the data can resolve to: only the public ones. Its internal types share short names with public ones (System.Private.CoreLib has a nested Complex),
    /// and the data must not pick types the application cannot even name (see docs/security.md).
    /// </summary>
    private static Type Visible(Type type)
    {
      return type is null || type.IsVisible || !IsFrameworkAssembly(type.Assembly) ? type : null;
    }

    /// <summary>
    /// Characters of assembly-qualified names, bracketed generic arguments ("List`1[[System.Diagnostics.Process, System]]"), pointers and references: the serializers never write them in a type name
    /// (generic arguments are written as "List&lt;Process&gt;" and split before they get here), and <see cref="Type.GetType(string)"/> or <see cref="Assembly.GetType(string)"/> would load the assemblies they name.
    /// </summary>
    private static readonly char[] UnsafeNameChars = new[] { ',', '[', ']', '&', '*' };

    [UnconditionalSuppressMessage("Trimming", "IL2096", Justification = TrimReasons.TypeNames)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimReasons.TypeNames)]
    private static Type ResolveName(string typeName, Type assignedTo, bool orAssignedTo)
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
      result = Visible(Type.GetType(typeName, false, true));
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
        result = Visible(assembly.GetType(typeName, false, true));
        if (result is null)
        {
          if (!CachedAssembies.Contains(assembly))
            result = CacheAssembly(assembly, typeName); // At this point we search all types in the assembly
          else // retreive from NameCache
          {
            // The assembly has previously been cached, but the type was not found by full name...
            if (NameCache.TryGetValue(typeName, out choices))
              result = Pick(typeName, choices, assignedTo);
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
        return Pick(typeName, choices, assignedTo);

      return orAssignedTo ? assignedTo : null; // will probably fail
    }

    /// <summary>
    /// One of the types with the same (short) name. A name in the data names a runtime type, so abstract types and interfaces do not count (e.g. System.Attribute),
    /// nor do types that cannot be assigned to <paramref name="assignedTo"/>. A name that is unique is remembered, a name that fits only one type here is not (it may fit another one elsewhere).
    /// </summary>
    private static Type Pick(string typeName, HashSet<Type> choices, Type assignedTo)
    {
      if (choices.Count == 1)
      {
        Type only = choices.First();
        UsedNameCache.Add(typeName, only);
        return only;
      }

      Type target = assignedTo is null ? null : Nullable.GetUnderlyingType(assignedTo) ?? assignedTo;
      Type fitting = null;
      int count = 0;
      foreach (Type choice in choices)
      {
        if (choice.IsAbstract || choice.IsInterface || (target != null && !target.IsAssignableFrom(choice)))
          continue;
        fitting = choice;
        count++;
      }
      if (count == 1)
        return fitting;

      throw new Exception(
        $"Type assignment dilamma for \"{typeName}\" with the following choices:\r\n  {string.Join("\r\n  ", choices.Select(t => t.FullName))}\r\nFix this by either serializing with full name or implementing a IMsgPackTypeResolver and add it to MsgPackSettings._typeResolvers.");
    }

    // The root types whose reachable assemblies have been cached
    private static readonly ConcurrentDictionary<Type, bool> ReachableCached = new ConcurrentDictionary<Type, bool>();

    /// <summary>
    /// Caches the assemblies of the types that can be reached from <paramref name="root"/>: generic arguments, element types, base types and the types of public properties (not of framework types).
    /// <para>The names in a schema are resolved before the values are read: a type in another assembly (e.g. the T of List&lt;T&gt;) was not known yet.</para>
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    internal static void CacheReachableAssemblies(Type root)
    {
      if (ReachableCached.ContainsKey(root))
        return;

      HashSet<Type> visited = new HashSet<Type>();
      Stack<Type> todo = new Stack<Type>();
      todo.Push(root);
      while (todo.Count > 0)
      {
        Type type = todo.Pop();
        if (type is null || type.IsGenericParameter || !visited.Add(type))
          continue;

        if (type.IsArray || type.IsPointer || type.IsByRef)
        {
          todo.Push(type.GetElementType());
          continue;
        }
        Type[] args = type.GenericTypeArguments;
        for (int t = args.Length - 1; t >= 0; t--)
          todo.Push(args[t]);

        if (IsFrameworkAssembly(type.Assembly))
          continue; // the framework's types are found otherwise, and do not reach the model

        CacheAssembly(type.Assembly, null);
        todo.Push(type.BaseType);
        PropertyInfo[] props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        for (int t = props.Length - 1; t >= 0; t--)
          todo.Push(props[t].PropertyType);
      }
      ReachableCached.TryAdd(root, true);
    }

    internal static bool IsFrameworkAssembly(Assembly assembly)
    {
      if (assembly == typeof(object).Assembly)
        return true;
      for (int t = 0; t < NativeAssemblies.Length; t++)
        if (NativeAssemblies[t] == assembly)
          return true;
      string name = assembly.GetName().Name;
      return name == "mscorlib" || name == "netstandard" || name == "System" || name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Makes one type known by its names (without the other types of its assembly, see <see cref="TypeResolving.Types.AllowedTypesGuard.Allow"/>).
    /// </summary>
    internal static void CacheType(Type type)
    {
      if (type.FullName is null || CachedAssembliesLockFree.ContainsKey(type.Assembly) || Visible(type) is null)
        return;

      lock (SyncRoot)
      {
        FullNameCache.TryAdd(type.FullName, type);
        if (!NameCache.TryGetValue(type.Name, out HashSet<Type> choices))
          NameCache.Add(type.Name, choices = new HashSet<Type>());
        choices.Add(type);
      }
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimReasons.TypeNames)]
    private static Type CacheAssemblyLocked(Assembly assembly, string typeName)
    {
      if (CachedAssembies.Contains(assembly))
        return null;

      Type found = null;
      if (string.IsNullOrEmpty(typeName))
        found = typeof(object); // skip the find part, just cache assembly

      Type[] types = assembly.GetTypes();
      bool framework = IsFrameworkAssembly(assembly);
      for (int t = types.Length - 1; t >= 0; t--)
      {
        if (framework && !types[t].IsVisible) // see Visible
          continue;
        string fullName = types[t].FullName;
        string name = types[t].Name;
        Type type = types[t];
        FullNameCache.TryAdd(fullName, type);
        if (!NameCache.TryAdd(name, new HashSet<Type> { type }))
          NameCache[name].Add(type);

        // check if found but don't bail out when found, once we start cahcing an assembly we'll finish the job!
        if (found == null && fullName == typeName)
          found = type; // already added to FullNameCache above
      }
      CachedAssembies.Add(assembly);
      CachedAssembliesLockFree.TryAdd(assembly, true);

      // A short name only when it is the name of one type (of all cached assemblies), otherwise the callers pick one that fits (Pick)
      if (found == null && !string.IsNullOrEmpty(typeName) && NameCache.TryGetValue(typeName, out HashSet<Type> choices) && choices.Count == 1)
      {
        found = choices.First();
        UsedNameCache.TryAdd(typeName, found);
      }
      return found;
    }
  }
}
