using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace LsMsgPack
{
  public static partial class MsgPackSerializer
  {
    /// <summary>
    /// Map key holding the type identifier (see <see cref="GetTypeIdentifier"/>).
    /// </summary>
    internal const string TypeIdKey = "";

    /// <summary>
    /// Map key holding the packed value of a wrapped item (collections, dictionaries or values that needed a type identifier).
    /// </summary>
    internal const string ContentKey = "@";

    /// <summary>
    /// Single entry point for converting an unpacked value into the type it will be assigned to.
    /// <para>Unpacked values are either null, a primitive (or extension) value, an object[] (MsgPack array) or a KeyValuePair&lt;object, object&gt;[] (MsgPack map).</para>
    /// </summary>
    /// <param name="val">The unpacked value (<see cref="MsgPackItem.Value"/>)</param>
    /// <param name="assignType">The type the result will be assigned to</param>
    /// <param name="settings">Settings used for unpacking</param>
    /// <param name="prop">The property the result will be assigned to (null for the root object, collection elements and dictionary entries)</param>
    private static object ConvertDeserializeValue(object val, Type assignType, MsgPackSettings settings, FullPropertyInfo prop)
    {
      if (val is null)
        return null;

      if (val is KeyValuePair<object, object>[] map)
        return ConvertMap(map, assignType, settings, prop);

      if (val is object[] items)
        return ConvertArray(items, assignType, settings);

      return ConvertScalar(val, assignType);
    }

    /// <summary>
    /// A map can be:
    /// <list type="bullet">
    /// <item>An object with properties (optionally including a type identifier).</item>
    /// <item>A wrapper around a value that needed a type identifier or (optionally) other properties: { "": typeId, "@": content }</item>
    /// <item>The entries of a dictionary (when serialized without a wrapper).</item>
    /// </list>
    /// </summary>
    private static object ConvertMap(KeyValuePair<object, object>[] map, Type assignType, MsgPackSettings settings, FullPropertyInfo prop)
    {
      if (assignType == typeof(KeyValuePair<object, object>[]))
        return map;

      Dictionary<object, object> propVals = new Dictionary<object, object>(map.Length, MapConversionEqualityComparer.Instance);
      for (int t = map.Length - 1; t >= 0; t--)
      {
        if (!(map[t].Key is null))
          propVals[map[t].Key] = map[t].Value;
      }

      bool hasTypeId = propVals.TryGetValue(TypeIdKey, out object typeId);
      Type tType = TypeResolver.Resolve(typeId, assignType, prop, settings, propVals);

      object result;
      if (propVals.TryGetValue(ContentKey, out object content))
      {
        result = content is KeyValuePair<object, object>[] pairs && IsDictionary(tType)
          ? ConvertPairs(pairs, tType, settings) // the content of a dictionary is always a map of entries, never an object
          : ConvertDeserializeValue(content, tType, settings, null);

        int reservedKeys = hasTypeId ? 2 : 1;
        if (propVals.Count > reservedKeys && !(result is null)) // properties serialized along with the content (see SerializeEnumerableAttribute.SerializeProperties)
          SetProperties(result, result.GetType(), propVals, settings);

        return result;
      }

      if (IsDictionary(tType) && !ElementsOmitted(tType, prop))
        return ConvertPairs(map, tType, settings);

      if (tType.IsInstanceOfType(map)) // object, IEnumerable, ... there is nothing more specific known, keep the raw map
        return map;

      if (tType.IsAbstract || tType.IsInterface)
        throw new Exception(
          $"Cannot create an instance of an interface or abstract type:\r\n  {tType.FullName}\r\nEither use MsgPackSettings.AddTypeIdOptions when serializing (easiest but adds payload) or add a custom IMsgPackTypeResolver to MsgPackSettings._typeResolvers.");

      result = CreateInstance(tType);
      SetProperties(result, tType, propVals, settings);
      return result;
    }

    /// <summary>
    /// A collection serialized with <see cref="SerializeEnumerableAttribute.SerializeElements"/> = false is a map of properties (not of dictionary entries).
    /// </summary>
    private static bool ElementsOmitted(Type tType, FullPropertyInfo prop)
    {
      SerializeEnumerableAttribute att = null;
      if (prop?.CustomAttributes != null && prop.CustomAttributes.TryGetValue(nameof(SerializeEnumerableAttribute), out object propAtt))
        att = (SerializeEnumerableAttribute)propAtt;
      else
        att = CollectionInfo.Get(tType).Attribute;

      return att != null && !att.SerializeElements;
    }

    private static void SetProperties(object instance, Type tType, Dictionary<object, object> propVals, MsgPackSettings settings)
    {
      FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(tType, settings);
      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo prop = props[t];
        if (propVals.TryGetValue(prop.PropertyId, out object propval))
        {
          prop.SetValue(instance, ConvertDeserializeValue(propval, prop.PropertyInfo.PropertyType, settings, prop));
        }
      }
    }

    /// <summary>
    /// Converts the items of a MsgPack array into an array or collection of the given type.
    /// </summary>
    private static object ConvertArray(object[] items, Type assignType, MsgPackSettings settings)
    {
      if (!IsCollection(assignType))
      {
        if (!assignType.IsInstanceOfType(items))
          return items; // will probably fail when assigned, but not our call to make
        assignType = typeof(object[]); // object, still convert the elements (they may contain type identifiers)
      }

      CollectionInfo info = CollectionInfo.Get(assignType);
      Array elements = Array.CreateInstance(info.ElementType, items.Length);
      for (int t = items.Length - 1; t >= 0; t--)
        elements.SetValue(ConvertDeserializeValue(items[t], info.ElementType, settings, null), t);

      return info.Create(elements);
    }

    /// <summary>
    /// Converts the entries of a MsgPack map into a dictionary (or KeyValuePair&lt;,&gt;[]) of the given type.
    /// </summary>
    private static object ConvertPairs(KeyValuePair<object, object>[] pairs, Type assignType, MsgPackSettings settings)
    {
      CollectionInfo info = CollectionInfo.Get(assignType);

      if (info.ConcreteType.IsArray)
      {
        Array typedArr = Array.CreateInstance(info.ElementType, pairs.Length);
        for (int t = pairs.Length - 1; t >= 0; t--)
        {
          object key = ConvertDeserializeValue(pairs[t].Key, info.KeyType, settings, null);
          object value = ConvertDeserializeValue(pairs[t].Value, info.ValueType, settings, null);
          typedArr.SetValue(Activator.CreateInstance(info.ElementType, key, value), t);
        }
        return typedArr;
      }

      object result = CreateInstance(info.ConcreteType);
      IDictionary dictionary = result as IDictionary;
      object[] args = dictionary is null ? new object[2] : null;
      for (int t = 0; t < pairs.Length; t++) // keep the original order
      {
        object key = ConvertDeserializeValue(pairs[t].Key, info.KeyType, settings, null);
        object value = ConvertDeserializeValue(pairs[t].Value, info.ValueType, settings, null);
        if (dictionary != null)
          dictionary.Add(key, value);
        else
        {
          args[0] = key;
          args[1] = value;
          info.AddMethod.Invoke(result, args);
        }
      }
      return result;
    }

    private static object ConvertScalar(object val, Type assignType)
    {
      Type valType = val.GetType();
      // Fix ArgumentException like "System.Byte cannot be converted to System.Nullable`1[System.Int32]"
      Type targetType = Nullable.GetUnderlyingType(assignType) ?? assignType;
      if (targetType == valType)
        return val;

      if (val is byte[] bytes)
      {
        if (targetType == typeof(Guid))
          return new Guid(bytes);

        if (targetType == typeof(sbyte[])) // the CLR considers sbyte[] to be a byte[], so it is packed as MpBin
        {
          sbyte[] signed = new sbyte[bytes.Length];
          Buffer.BlockCopy(bytes, 0, signed, 0, bytes.Length);
          return signed;
        }
      }

      if (assignType.IsAssignableFrom(valType))
        return val;

      if (targetType == typeof(DateTimeOffset) && val is DateTime dateTime)
        return new DateTimeOffset(dateTime); // the timestamp does not contain the offset, the value is local time

      if (targetType.IsEnum)
        return Enum.ToObject(targetType, val);

      if ((targetType.IsPrimitive || targetType == typeof(decimal)) && val is IConvertible)
        return Convert.ChangeType(val, targetType, CultureInfo.InvariantCulture);

      return val;
    }

    private static bool IsCollection(Type type)
    {
      return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }

    private static bool IsDictionary(Type type)
    {
      return IsCollection(type) && CollectionInfo.Get(type).IsDictionary;
    }

    /// <summary>
    /// Whether the type has a (public or non-public) parameterless constructor, without one <see cref="Activator.CreateInstance(Type, bool)"/> would throw (slow) for every instance.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, bool> HasParameterlessConstructor = new ConcurrentDictionary<Type, bool>();

    private static object CreateInstance(Type type)
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

    /// <summary>
    /// Cached reflection metadata on how to fill a collection (or dictionary) type.
    /// </summary>
    private sealed class CollectionInfo
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

        object result = CreateInstance(ConcreteType);
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
}
