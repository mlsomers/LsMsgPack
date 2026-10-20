using LsMsgPack.Meta;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack
{
  /// <summary>
  /// The classes a value of a root type can hold, in a fixed order: the entries of a complete schema (<see cref="MsgPackOptions.CompleteSchemas"/>).
  /// <para>Depth first from the root, as a writer adds them (the class of the root first): a class gets an entry when it has properties (the statically included ones, in <see cref="PropertyOrder"/>)
  /// or when a value of it gets a type id (a class deriving from the declared one). The types of its properties, the elements of collections, the keys and values of dictionaries, pairs, tuples and nullables are followed,
  /// and after a class or interface that is not sealed, the classes deriving from it in the assemblies the root reaches (by full name: the order does not depend on the metadata).</para>
  /// <para>The same types and settings give the same list in any process, so the same schema id. Also used by the TypeScript generator, which writes the schemas the server will write.</para>
  /// </summary>
  internal sealed class SchemaTypes
  {
    private readonly MsgPackOptions _settings;
    private readonly List<Type> _entries = new List<Type>();
    private readonly HashSet<Type> _withEntry = new HashSet<Type>();
    private readonly HashSet<Type> _visited = new HashSet<Type>();
    private readonly List<Assembly> _assemblies = new List<Assembly>();
    private readonly Dictionary<Type, List<Type>> _derived = new Dictionary<Type, List<Type>>();
    private List<Type> _candidates;

    private SchemaTypes(MsgPackOptions settings)
    {
      _settings = settings;
    }

    /// <summary>
    /// The classes of the entries of the complete schema of <paramref name="root"/>, in the order of their type ids.
    /// </summary>
    /// <param name="settings">The settings of the session (its static filters and property order decide which properties are followed)</param>
    internal static List<Type> Of(Type root, MsgPackOptions settings)
    {
      SchemaTypes walk = new SchemaTypes(settings);
      walk.FindAssemblies(root);
      walk.Visit(root, false, 0);
      return walk._entries;
    }

    private void Visit(Type type, bool hasTypeId, int depth)
    {
      if (type is null || type.IsGenericParameter || type.ContainsGenericParameters || type.IsPointer || type.IsByRef || depth > _settings._maxDepth)
        return;
      type = Nullable.GetUnderlyingType(type) ?? type;
      if (type.IsArray)
      {
        Visit(type.GetElementType(), false, depth + 1);
        return;
      }
      if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(object) || type == typeof(decimal))
        return;
      if (FrameworkTypeInfo.IsKeyValuePair(type) || FrameworkTypeInfo.IsTuple(type))
      {
        if (_visited.Add(type))
          VisitAll(type.GetGenericArguments(), depth);
        return;
      }
      if (_settings.HasCustomExtension(type) || FrameworkTypeInfo.GetPlainForm(type) != PlainForm.None)
        return;
      if (typeof(IEnumerable).IsAssignableFrom(type))
      {
        if (!_visited.Add(type))
          return;
        CollectionInfo info = CollectionInfo.Get(type);
        if (info.IsDictionary || !(info.PairKey is null))
        {
          Visit(info.PairKey?.PropertyType ?? info.KeyType, false, depth + 1);
          Visit(info.PairValue?.PropertyType ?? info.ValueType, false, depth + 1);
        }
        else
          Visit(info.ElementType, false, depth + 1); // the properties of a collection are only written when asked for (SerializeEnumerableAttribute), they are added when they are
        return;
      }
      if (!DescribedSchema.IsObject(type, _settings))
        return;

      bool concrete = !type.IsAbstract && !type.IsInterface;
      FullPropertyInfo[] props = concrete ? FullPropertyInfo.GetStaticallyIncludedProps(type, _settings) : null;
      if (concrete && (hasTypeId || props.Length > 0) && _withEntry.Add(type))
        _entries.Add(type);
      if (!_visited.Add(type))
        return;

      if (concrete)
        for (int t = 0; t < props.Length; t++)
          Visit(props[t].PropertyInfo.PropertyType, false, depth + 1);

      if (!type.IsSealed && !type.IsValueType && !TypeResolver.IsFrameworkAssembly(type.Assembly))
      {
        List<Type> derived = Derived(type);
        for (int t = 0; t < derived.Count; t++)
          Visit(derived[t], true, depth + 1);
      }
    }

    private void VisitAll(Type[] types, int depth)
    {
      for (int t = 0; t < types.Length; t++)
        Visit(types[t], false, depth + 1);
    }

    /// <summary>
    /// The classes of the reached assemblies that derive from (or implement) the type, by full name.
    /// </summary>
    private List<Type> Derived(Type type)
    {
      if (_derived.TryGetValue(type, out List<Type> derived))
        return derived;
      if (_candidates is null)
        _candidates = Candidates();
      derived = new List<Type>();
      for (int t = 0; t < _candidates.Count; t++)
        if (_candidates[t] != type && type.IsAssignableFrom(_candidates[t]))
          derived.Add(_candidates[t]);
      _derived.Add(type, derived);
      return derived;
    }

    /// <summary>
    /// The classes and structs of the reached assemblies that a type id can name, by full name.
    /// </summary>
    private List<Type> Candidates()
    {
      List<Type> candidates = new List<Type>();
      for (int a = 0; a < _assemblies.Count; a++)
      {
        Type[] types = TypesOf(_assemblies[a]);
        for (int t = 0; t < types.Length; t++)
        {
          Type type = types[t];
          if (type is null || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || type.FullName is null || type.Name.IndexOf('<') >= 0)
            continue; // also compiler generated classes
          candidates.Add(type);
        }
      }
      candidates.Sort((x, y) => string.CompareOrdinal(x.FullName, y.FullName));
      return candidates;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimReasons.TypeNames)]
    private static Type[] TypesOf(Assembly assembly)
    {
      try
      {
        return assembly.GetTypes();
      }
      catch (ReflectionTypeLoadException ex) // e.g. classes that need an assembly this process does not have: the others still count
      {
        return ex.Types;
      }
    }

    /// <summary>
    /// The assemblies (not the framework's) of the types the root reaches through generic arguments, element types, base types and the types of public properties, as TypeResolver.CacheReachableAssemblies.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    private void FindAssemblies(Type root)
    {
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

        if (TypeResolver.IsFrameworkAssembly(type.Assembly))
          continue;

        if (!_assemblies.Contains(type.Assembly))
          _assemblies.Add(type.Assembly);
        todo.Push(type.BaseType);
        PropertyInfo[] props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        for (int t = props.Length - 1; t >= 0; t--)
          todo.Push(props[t].PropertyType);
      }
    }
  }
}
