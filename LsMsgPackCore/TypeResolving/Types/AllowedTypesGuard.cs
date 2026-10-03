using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace LsMsgPack.TypeResolving.Types
{
  /// <summary>
  /// An allow-list for the types the data may pick (see <see cref="MsgPackOptions.TypeGuard"/> and docs/security.md): the given types, the types of the given assemblies,
  /// and the values and collections of the framework that the serializers write themselves (<see cref="IsSafeFrameworkType"/>).
  /// <para>Generic types and arrays are allowed when their definition and all their arguments (the element type) are allowed, so <c>List&lt;Process&gt;</c> is refused unless Process is allowed:
  /// its elements would be created as the declared element type, without a type id.</para>
  /// <code>
  /// settings.TypeGuard = new AllowedTypesGuard().AllowAssemblyOf(typeof(IPet)).Allow(typeof(PluginPet));
  /// </code>
  /// <para>Configure it before using it, the decisions are cached per type (the cache is cleared when more types are allowed). Safe to share between settings and threads.</para>
  /// </summary>
  public class AllowedTypesGuard : IMsgPackTypeGuard
  {
    private readonly object _sync = new object();

    // Replaced (copy on write) when more types are allowed, so they can be read without a lock
    private HashSet<Type> _types = new HashSet<Type>();
    private HashSet<Assembly> _assemblies = new HashSet<Assembly>();
    private ConcurrentDictionary<Type, bool> _decided = new ConcurrentDictionary<Type, bool>();

    /// <param name="types">Allowed types, see <see cref="Allow"/></param>
    public AllowedTypesGuard(params Type[] types)
    {
      Allow(types);
    }

    /// <summary>
    /// Allows these types. A generic type definition (e.g. <c>typeof(Envelope&lt;&gt;)</c>) allows its constructed types whose arguments are allowed, a constructed type is allowed as it is.
    /// </summary>
    public AllowedTypesGuard Allow(params Type[] types)
    {
      if (types is null || types.Length == 0)
        return this;

      lock (_sync)
      {
        HashSet<Type> copy = new HashSet<Type>(_types);
        foreach (Type type in types)
        {
          if (type != null)
            copy.Add(type);
        }
        _types = copy;
        _decided = new ConcurrentDictionary<Type, bool>();
      }
      return this;
    }

    /// <summary>
    /// Allows all types of these assemblies (e.g. the assembly of your model), generic types only with allowed arguments.
    /// </summary>
    public AllowedTypesGuard AllowAssembly(params Assembly[] assemblies)
    {
      if (assemblies is null || assemblies.Length == 0)
        return this;

      lock (_sync)
      {
        HashSet<Assembly> copy = new HashSet<Assembly>(_assemblies);
        foreach (Assembly assembly in assemblies)
        {
          if (assembly != null)
            copy.Add(assembly);
        }
        _assemblies = copy;
        _decided = new ConcurrentDictionary<Type, bool>();
      }
      return this;
    }

    /// <summary>
    /// Allows all types of the assemblies that define these types, see <see cref="AllowAssembly"/>.
    /// </summary>
    public AllowedTypesGuard AllowAssemblyOf(params Type[] types)
    {
      if (types is null)
        return this;

      Assembly[] assemblies = new Assembly[types.Length];
      for (int t = 0; t < types.Length; t++)
        assemblies[t] = types[t]?.Assembly;
      return AllowAssembly(assemblies);
    }

    public bool IsAllowed(Type type, Type assignedTo, FullPropertyInfo assignedToProp, MsgPackOptions settings)
    {
      return IsAllowed(type);
    }

    /// <summary>
    /// Whether the type is allowed (cached per type).
    /// </summary>
    public bool IsAllowed(Type type)
    {
      if (type is null)
        return false;

      ConcurrentDictionary<Type, bool> decided = _decided;
      if (decided.TryGetValue(type, out bool allowed))
        return allowed;

      allowed = Decide(type);
      decided.TryAdd(type, allowed);
      return allowed;
    }

    /// <summary>
    /// Decides for a type that was not decided before. Override it to add rules, call the base for the arguments of generic types and the element types of arrays.
    /// </summary>
    protected virtual bool Decide(Type type)
    {
      if (type.IsArray)
        return type.GetArrayRank() == 1 && IsAllowed(type.GetElementType());

      if (type.ContainsGenericParameters || type.IsPointer || type.IsByRef)
        return false;

      if (_types.Contains(type)) // exactly this (constructed) type
        return true;

      if (type.IsGenericType)
      {
        Type definition = type.GetGenericTypeDefinition();
        if (!_types.Contains(definition) && !_assemblies.Contains(definition.Assembly) && !SafeGenericDefinitions.Contains(definition))
          return false;

        foreach (Type argument in type.GenericTypeArguments)
        {
          if (!IsAllowed(argument))
            return false;
        }
        return true;
      }

      return _assemblies.Contains(type.Assembly) || IsSafeFrameworkType(type);
    }

    /// <summary>
    /// Generic collections of the framework, harmless to create: they only hold their elements (which are checked).
    /// </summary>
    private static readonly HashSet<Type> SafeGenericDefinitions = new HashSet<Type>()
    {
      typeof(Nullable<>),
      typeof(KeyValuePair<,>),
      typeof(List<>),
      typeof(Dictionary<,>),
      typeof(HashSet<>),
      typeof(SortedDictionary<,>),
      typeof(SortedList<,>),
      typeof(SortedSet<>),
      typeof(LinkedList<>),
      typeof(Queue<>),
      typeof(Stack<>),
      typeof(ConcurrentDictionary<,>),
      typeof(ConcurrentBag<>),
      typeof(ConcurrentQueue<>),
      typeof(ConcurrentStack<>),
      typeof(Collection<>),
      typeof(ObservableCollection<>)
    };

    private static readonly HashSet<Type> SafeTypes = new HashSet<Type>()
    {
      typeof(object),
      typeof(string),
      typeof(decimal),
      typeof(DateTime),
      typeof(DateTimeOffset),
      typeof(TimeSpan),
      typeof(Guid),
      typeof(Uri),
      typeof(ArrayList),
      typeof(Hashtable)
    };

    /// <summary>
    /// The values the serializers write themselves (primitives, enums, string, decimal, date and time types, Guid, Uri) and the framework's non-generic collections <see cref="ArrayList"/> and <see cref="Hashtable"/>.
    /// The generic collections of the framework (<c>List&lt;T&gt;</c>, <c>Dictionary&lt;TKey, TValue&gt;</c>, ...) are allowed by <see cref="Decide"/> when their arguments are.
    /// </summary>
    public static bool IsSafeFrameworkType(Type type)
    {
      if (type.IsPrimitive || type.IsEnum || SafeTypes.Contains(type))
        return true;

      // DateOnly and TimeOnly: .NET Standard does not have them (found by name, only the framework's own)
      return type.Assembly == typeof(object).Assembly && (type.FullName == "System.DateOnly" || type.FullName == "System.TimeOnly");
    }
  }
}
