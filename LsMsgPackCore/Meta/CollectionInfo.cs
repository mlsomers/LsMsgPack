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
    /// ICollection&lt;T&gt;.Add(T) or IDictionary&lt;TKey, TValue&gt;.Add(TKey, TValue), used when the type does not implement IList or IDictionary
    /// </summary>
    public readonly MethodInfo AddMethod;

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
        if (!typeof(IDictionary).IsAssignableFrom(ConcreteType))
        {
          Type dictInterface = typeof(IDictionary<,>).MakeGenericType(KeyType, ValueType);
          if (dictInterface.IsAssignableFrom(ConcreteType))
            AddMethod = dictInterface.GetMethod(nameof(IDictionary.Add));
        }
        return;
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
      }
    }

    /// <summary>
    /// Create the collection from the (already converted) elements
    /// </summary>
    public object Create(Array elements)
    {
      if (ConcreteType.IsArray)
        return elements;

      if (_itemsConstructor != null)
      {
        if (_reverseItems)
          Array.Reverse(elements);
        return _itemsConstructor.Invoke(new object[] { elements });
      }

      object result = Instances.Create(ConcreteType);
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
