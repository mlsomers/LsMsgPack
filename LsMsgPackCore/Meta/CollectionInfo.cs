using LsMsgPack.TypeResolving.Attributes;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Cached reflection metadata on how to fill a collection (or dictionary) type.
  /// </summary>
  internal sealed class CollectionInfo
  {
    private static readonly ConcurrentDictionary<Type, CollectionInfo> Cache = new ConcurrentDictionary<Type, CollectionInfo>();

    public static CollectionInfo Get(Type type)
    {
      return Cache.GetOrAdd(type, t => new CollectionInfo(t));
    }

    /// <summary>
    /// T of IEnumerable&lt;T&gt; (KeyValuePair&lt;TKey, TValue&gt; for generic dictionaries), object if unknown.
    /// </summary>
    public readonly Type ElementType;

    public readonly bool IsDictionary;

    /// <summary>
    /// [SerializeEnumerable] on the collection type (or a base class), may be overruled by one on the property.
    /// </summary>
    public readonly SerializeEnumerableAttribute Attribute;
    public readonly Type KeyType;
    public readonly Type ValueType;

    /// <summary>
    /// The type to create, differs from the requested type for interfaces like IList&lt;T&gt; or IDictionary&lt;TKey, TValue&gt;
    /// </summary>
    public readonly Type ConcreteType;

    /// <summary>
    /// Constructor taking ElementType[] (usually a constructor taking IEnumerable&lt;T&gt;)
    /// </summary>
    private readonly ConstructorInfo _itemsConstructor;

    /// <summary>
    /// ICollection&lt;T&gt;.Add(T), a public Add(T) (as XmlSerializer uses it) or IDictionary&lt;TKey, TValue&gt;.Add(TKey, TValue), used when the type does not implement IList or IDictionary
    /// </summary>
    public readonly MethodInfo AddMethod;

    /// <summary>
    /// KeyValuePair&lt;TKey, TValue&gt;.Key and .Value when the elements are pairs (written as a map, like a dictionary).
    /// </summary>
    public readonly PropertyInfo PairKey;
    public readonly PropertyInfo PairValue;

    /// <summary>
    /// A collection of pairs that is not a dictionary (no IDictionary or IDictionary&lt;TKey, TValue&gt;): filled with KeyValuePair elements, like other collections.
    /// </summary>
    public readonly bool FillsPairs;

    /// <summary>
    /// Stacks are enumerated from top to bottom, and filled from bottom to top by their constructor.
    /// </summary>
    private readonly bool _reverseItems;

    private CollectionInfo(Type type)
    {
      ElementType = GetElementType(type);
      Attribute = type.GetCustomAttribute<SerializeEnumerableAttribute>(true);

      if (ElementType.IsGenericType && ElementType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
      {
        IsDictionary = true;
        KeyType = ElementType.GenericTypeArguments[0];
        ValueType = ElementType.GenericTypeArguments[1];
        PairKey = ElementType.GetProperty(nameof(KeyValuePair<object, object>.Key));
        PairValue = ElementType.GetProperty(nameof(KeyValuePair<object, object>.Value));
      }
      else if (typeof(IDictionary).IsAssignableFrom(type)) // Hashtable, ...
      {
        IsDictionary = true;
        KeyType = typeof(object);
        ValueType = typeof(object);
      }

      ConcreteType = GetConcreteType(type);
      if (ConcreteType.IsArray)
        return;

      if (IsDictionary)
      {
        if (typeof(IDictionary).IsAssignableFrom(ConcreteType))
          return;
        Type dictInterface = typeof(IDictionary<,>).MakeGenericType(KeyType, ValueType);
        if (dictInterface.IsAssignableFrom(ConcreteType))
        {
          AddMethod = dictInterface.GetMethod(nameof(IDictionary.Add));
          return;
        }
        FillsPairs = true; // e.g. List<KeyValuePair<TKey, TValue>>: a constructor or an Add of the pairs, as below
      }

      _itemsConstructor = ConcreteType.GetConstructor(new[] { ElementType.MakeArrayType() });
      if (ConcreteType.IsGenericType)
      {
        Type definition = ConcreteType.GetGenericTypeDefinition();
        _reverseItems = definition == typeof(Stack<>) || definition == typeof(ConcurrentStack<>);
      }

      if (_itemsConstructor is null && !typeof(IList).IsAssignableFrom(ConcreteType))
      {
        Type collInterface = typeof(ICollection<>).MakeGenericType(ElementType);
        if (collInterface.IsAssignableFrom(ConcreteType))
          AddMethod = collInterface.GetMethod(nameof(ICollection<object>.Add));
        else // IEnumerable<T> with an Add(T), which is what XmlSerializer needs
          AddMethod = ConcreteType.GetMethod(nameof(ICollection<object>.Add), BindingFlags.Public | BindingFlags.Instance, null, new[] { ElementType }, null);
      }
    }

    /// <summary>
    /// The entries of a collection of KeyValuePair elements (<see cref="PairKey"/> is not null), for writing it as a map.
    /// </summary>
    public KeyValuePair<object, object>[] ToPairs(IEnumerable pairs)
    {
      List<KeyValuePair<object, object>> entries = pairs is ICollection collection ? new List<KeyValuePair<object, object>>(collection.Count) : new List<KeyValuePair<object, object>>();
      foreach (object pair in pairs)
        entries.Add(new KeyValuePair<object, object>(PairKey.GetValue(pair), PairValue.GetValue(pair)));
      return entries.ToArray();
    }

    /// <summary>
    /// Create the collection from the (already converted) elements
    /// </summary>
    /// <param name="settings">Decides whether a collection without a parameterless constructor may be created uninitialized (see <see cref="MsgPackOptions.ObjectCreation"/>)</param>
    public object Create(Array elements, MsgPackOptions settings = null)
    {
      if (ConcreteType.IsArray)
        return elements;

      if (_itemsConstructor != null)
      {
        if (_reverseItems)
          Array.Reverse(elements);
        return _itemsConstructor.Invoke(new object[] { elements });
      }

      object result = Instances.CreateCollection(ConcreteType, settings);
      if (result is IList list)
      {
        for (int t = 0; t < elements.Length; t++)
          list.Add(elements.GetValue(t));
        return result;
      }

      if (AddMethod is null)
        throw new MsgPackException($"Unable to fill a collection of type {ConcreteType.FullName}, it has no constructor taking {ElementType.Name}[] (or IEnumerable<{ElementType.Name}>) and no Add({ElementType.Name}) method.");

      object[] args = new object[1];
      for (int t = 0; t < elements.Length; t++)
      {
        args[0] = elements.GetValue(t);
        AddMethod.Invoke(result, args);
      }
      return result;
    }

    private static Type GetElementType(Type type)
    {
      if (type.IsArray)
        return type.GetElementType();

      if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        return type.GenericTypeArguments[0];

      Type[] interfaces = type.GetInterfaces();
      for (int t = 0; t < interfaces.Length; t++)
      {
        if (interfaces[t].IsGenericType && interfaces[t].GetGenericTypeDefinition() == typeof(IEnumerable<>))
          return interfaces[t].GenericTypeArguments[0];
      }

      return typeof(object);
    }

    private Type GetConcreteType(Type type)
    {
      if (!(type.IsInterface || type.IsAbstract) || type.IsArray)
        return type;

      Type[] candidates = IsDictionary
        ? new[] { typeof(Dictionary<,>).MakeGenericType(KeyType, ValueType) }
        : new[] {
          ElementType == typeof(object) ? typeof(object[]) : null, // IEnumerable, ICollection, IList
          typeof(List<>).MakeGenericType(ElementType), // IEnumerable<T>, IList<T>, IReadOnlyList<T>, ...
          typeof(HashSet<>).MakeGenericType(ElementType), // ISet<T>
          ElementType.MakeArrayType()
        };

      for (int t = 0; t < candidates.Length; t++)
      {
        if (candidates[t] != null && type.IsAssignableFrom(candidates[t]))
          return candidates[t];
      }

      return type; // Unknown abstraction, will fail on creating an instance
    }
  }
}
