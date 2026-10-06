using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace LsMsgPack.Meta
{
  public class FullPropertyInfo
  {
    // Type is a IMsgPackPropertyIdResolver Type
    private static readonly ConcurrentDictionary<PropertyInfo, FullPropertyInfo> Cache = new ConcurrentDictionary<PropertyInfo, FullPropertyInfo>();
    private Dictionary<Type, ConstructorInfo> _constructorTakingType; // per instance (created when first used), constructors differ per AssignedToType

    public static FullPropertyInfo GetFullPropInfo(PropertyInfo propertyInfo, MsgPackOptions settings)
    {
      if (propertyInfo == null)
        return null;

      FullPropertyInfo full;
      if (settings._propertyNameResolvers is null || settings._propertyNameResolvers.Length == 0){ // Only cache for default resolver, Implemented resolvers must have their own cache (or not)
        if (Cache.TryGetValue(propertyInfo, out full))
          return full;
      }

      full = new FullPropertyInfo(propertyInfo);
      ResolvePropertyId(full, settings);

      if (settings._propertyNameResolvers is null || settings._propertyNameResolvers.Length == 0)
        return Cache.GetOrAdd(propertyInfo, full); // another thread may have been first
      return full;
    }

    /// <summary>
    /// The first id returned by the <see cref="MsgPackSettings.PropertyNameResolvers"/> (consulted from last to first), or the name of the property.
    /// </summary>
    private static void ResolvePropertyId(FullPropertyInfo full, MsgPackOptions settings)
    {
      full.PropertyId = null;
      for (int t = settings._propertyNameResolvers.Length - 1; t >= 0; t--)
      {
        full.PropertyId = settings._propertyNameResolvers[t].GetId(full, settings);
        if (full.PropertyId != null)
          break;
      }

      if (full.PropertyId == null)
        full.PropertyId = full.PropertyInfo.Name;
    }

    // Attributes are static metadata, so they are read once per property (even when the FullPropertyInfo itself cannot be cached because of custom property id resolvers)
    private static readonly ConcurrentDictionary<PropertyInfo, Dictionary<string, object>> AttributesCache = new ConcurrentDictionary<PropertyInfo, Dictionary<string, object>>();

    private FullPropertyInfo(PropertyInfo prop)
    {
      PropertyInfo = prop;
      CustomAttributes = AttributesCache.GetOrAdd(prop, p => ReadCustomAttributes(p));
      AssignedToType = prop.PropertyType;
    }

    private static Dictionary<string, object> ReadCustomAttributes(PropertyInfo prop)
    {
      object[] atts = prop.GetCustomAttributes(true);
      Dictionary<string, object> attributes = new Dictionary<string, object>(atts.Length);
      for (int t = atts.Length - 1; t >= 0; t--)
      {
        string attName = atts[t].GetType().Name;
        attributes.TryAdd(attName, atts[t]);
      }
      return attributes;
    }

    public FullPropertyInfo(Type assignToType)
    {
      AssignedToType = assignToType;
    }

    private Type _assignedToType;
    public Type AssignedToType
    {
      get
      {
        return _assignedToType;
      }
      set
      {
        Type nullableType = Nullable.GetUnderlyingType(value);
        if (!(nullableType is null))
          _assignedToType = nullableType;
        else
          _assignedToType = value;
      }
    }

    public PropertyInfo PropertyInfo { get; set; }

    private PropertyAccessor _accessor;

    /// <summary>
    /// The value of the property, read by a compiled delegate once the property is used often (see <see cref="PropertyAccessor"/>).
    /// </summary>
    internal object GetValue(object instance)
    {
      PropertyAccessor accessor = _accessor ?? (_accessor = PropertyAccessor.Get(PropertyInfo));
      return accessor.GetValue(instance);
    }

    /// <summary>
    /// Sets the value of the property, by a compiled delegate once the property is used often (see <see cref="PropertyAccessor"/>).
    /// </summary>
    internal void SetValue(object instance, object value)
    {
      PropertyAccessor accessor = _accessor ?? (_accessor = PropertyAccessor.Get(PropertyInfo));
      accessor.SetValue(instance, value);
    }

    /// <summary>
    /// Note that this may not be the complete set, When multiple attributes of the same type are applied, only the first one will be listed here, so if your custom attribute supports multiple instances on a property you will need to get them from the propertyInfo.
    /// </summary>
    public Dictionary<string, object> CustomAttributes { get; set; }

    /// <summary>
    /// Whether the static filters left this property out (for display), set on the properties of a session or of custom property id resolvers.
    /// <para>Not set on the instances that all settings share (no property id resolvers, no session): different static filters may decide differently, the kept properties are cached per filter array instead.</para>
    /// </summary>
    public bool? StaticallyIgnored { get; set; }

    /// <summary>
    /// By default the property name (string), but can be overridden by IMsgPackPropertyIdResolver
    /// </summary>
    public object PropertyId { get; set; }

    public ConstructorInfo GetConstructorTaking(Type type)
    {
      if (_constructorTakingType is null)
        _constructorTakingType = new Dictionary<Type, ConstructorInfo>();

      if (_constructorTakingType.TryGetValue(type, out ConstructorInfo constructor))
        return constructor;

      // Todo: concurrent locking system

      ConstructorInfo ci = AssignedToType.GetConstructor(new[] { type });
      _constructorTakingType.Add(type, ci);
      return ci;
    }

    public override string ToString()
    {
      string ignored = (StaticallyIgnored.HasValue && StaticallyIgnored.Value) ? " (ignored)" : string.Empty;
      string propInfo = (PropertyInfo is null) ? " not a property" : $" property: {PropertyInfo.Name}";
      return $"{AssignedToType}{ignored}{propInfo}";
    }


    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertiesCache = new ConcurrentDictionary<Type, PropertyInfo[]>();

    /// <summary>
    /// Public properties excluding indexers and properties hidden by a property with the same name in a derived class ("new" modifier).
    /// </summary>
    private static PropertyInfo[] GetProperties(Type type)
    {
      return PropertiesCache.GetOrAdd(type, t =>
      {
        PropertyInfo[] all = t.GetProperties();
        List<PropertyInfo> kept = new List<PropertyInfo>(all.Length);
        for (int i = 0; i < all.Length; i++)
        {
          PropertyInfo prop = all[i];
          if (prop.GetIndexParameters().Length > 0)
            continue;

          bool hidden = false;
          for (int j = 0; j < all.Length; j++)
          {
            if (j != i && all[j].Name == prop.Name && all[j].DeclaringType != prop.DeclaringType && prop.DeclaringType.IsAssignableFrom(all[j].DeclaringType))
            {
              hidden = true;
              break;
            }
          }

          if (!hidden)
            kept.Add(prop);
        }
        return kept.ToArray();
      });
    }

    // One cache per PropertyOrder (the enum values are indexes, checked by MsgPackOptions.PropertyOrder), Reflection uses PropertiesCache
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]>[] OrderedPropertiesCache = CachePerOrder<PropertyInfo[]>();

    private static ConcurrentDictionary<Type, T>[] CachePerOrder<T>()
    {
      ConcurrentDictionary<Type, T>[] caches = new ConcurrentDictionary<Type, T>[(int)MsgPackOptions.LastPropertyOrder + 1];
      for (int t = 0; t < caches.Length; t++)
        caches[t] = new ConcurrentDictionary<Type, T>();
      return caches;
    }

    /// <summary>
    /// The properties of <see cref="GetProperties(Type)"/> in the order of the settings (see <see cref="PropertyOrder"/>).
    /// </summary>
    private static PropertyInfo[] GetProperties(Type type, MsgPackOptions settings)
    {
      PropertyOrder order = settings._propertyOrder;
      if (order == PropertyOrder.Reflection)
        return GetProperties(type);

      return OrderedPropertiesCache[(int)order].GetOrAdd(type, t => Sort(GetProperties(t), order));
    }

    private static PropertyInfo[] Sort(PropertyInfo[] props, PropertyOrder order)
    {
      OrderKey[] keys = new OrderKey[props.Length];
      for (int t = 0; t < props.Length; t++)
        keys[t] = new OrderKey(props[t], t, order);

      Array.Sort(keys);

      PropertyInfo[] sorted = new PropertyInfo[props.Length];
      for (int t = 0; t < keys.Length; t++)
        sorted[t] = keys[t].Property;
      return sorted;
    }

    /// <summary>
    /// The position of a property for a <see cref="PropertyOrder"/>, compared field by field. The reflection index comes last so the sort is stable.
    /// </summary>
    private struct OrderKey : IComparable<OrderKey>
    {
      internal readonly PropertyInfo Property;
      private readonly bool _byName;
      private readonly int _explicitOrder; // int.MaxValue: none (after the ones that have one)
      private readonly string _typeName; // TypeThenDeclaration, null otherwise
      private readonly int _depth; // of the class declaring the (overridden) property, base classes first
      private readonly int _token;
      private readonly int _index;

      internal OrderKey(PropertyInfo property, int index, PropertyOrder order)
      {
        Property = property;
        _index = index;
        _byName = order == PropertyOrder.Alphabetical;
        _explicitOrder = int.MaxValue;
        _typeName = order == PropertyOrder.TypeThenDeclaration ? property.PropertyType.ToString() : null; // FullName would contain the assemblies of generic arguments, which differ per runtime
        _depth = 0;
        _token = 0;

        if (_byName)
          return;

        if (order == PropertyOrder.Explicit)
        {
          // Attribute.GetCustomAttribute also looks at the overridden properties (PropertyInfo.GetCustomAttributes ignores inherit)
          DataMemberAttribute member = (DataMemberAttribute)Attribute.GetCustomAttribute(property, typeof(DataMemberAttribute), true);
          if (member != null && member.Order >= 0)
            _explicitOrder = member.Order;
        }

        PropertyInfo declared = OriginalDeclaration(property);
        _depth = Depth(declared.DeclaringType);
        try
        {
          _token = declared.MetadataToken;
        }
        catch (InvalidOperationException) // no metadata (e.g. a type being built with Reflection.Emit), keep the reflection order within its class
        {
          _token = int.MaxValue;
        }
      }

      public int CompareTo(OrderKey other)
      {
        if (_explicitOrder != other._explicitOrder)
          return _explicitOrder.CompareTo(other._explicitOrder);
        if (_typeName != null)
        {
          int byType = string.CompareOrdinal(_typeName, other._typeName);
          if (byType != 0)
            return byType;
        }
        if (_depth != other._depth)
          return _depth.CompareTo(other._depth);
        if (_token != other._token)
          return _token.CompareTo(other._token);
        if (_byName)
        {
          int byName = string.CompareOrdinal(Property.Name, other.Property.Name);
          if (byName != 0)
            return byName;
        }
        return _index.CompareTo(other._index);
      }

      /// <summary>
      /// The property an override overrides (recursively), so overriding a property in a derived class does not move it.
      /// </summary>
      private static PropertyInfo OriginalDeclaration(PropertyInfo property)
      {
        MethodInfo accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
        if (accessor is null)
          return property;

        Type original = accessor.GetBaseDefinition().DeclaringType;
        if (original is null || original == property.DeclaringType)
          return property;

        try
        {
          return original.GetProperty(property.Name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ?? property;
        }
        catch (AmbiguousMatchException)
        {
          return property;
        }
      }

      private static int Depth(Type type)
      {
        int depth = 0;
        for (Type baseType = type?.BaseType; baseType != null; baseType = baseType.BaseType)
          depth++;
        return depth;
      }
    }

    internal static FullPropertyInfo[] GetSerializedProps(Type type, MsgPackOptions settings)
    {
      Dictionary<Type, FullPropertyInfo[]> sessionCache = settings._serializedPropsCache;
      if (sessionCache is null)
        return GetSerializedPropsWithoutSession(type, settings);

      if (sessionCache.TryGetValue(type, out FullPropertyInfo[] cached))
        return cached;

      if (settings._schemaFrozen) // a shared session, see SchemaSession
        throw SchemaGrowthException.Instance;

      // Within a session each property is resolved once: first the static filters, then the ids of the properties that are kept.
      // The indexed schema asks for the kept properties of the type (see GetStaticallyIncludedProps) when resolving the first id.
      FullPropertyInfo[] props = GetStaticallyIncludedProps(type, settings);
      for (int t = 0; t < props.Length; t++)
        ResolvePropertyId(props[t], settings);
      ThrowIfIdsNotUnique(type, props);

      sessionCache[type] = props;
      return props;
    }

    /// <summary>
    /// The serialized properties per type, one dictionary per <see cref="PropertyOrder"/>, for one array of static filters.
    /// <para>Without property id resolvers the FullPropertyInfo instances are shared by all settings (see GetFullPropInfo), so the result only depends on the type, the order and the static filters.
    /// The filters are compared by reference (like SchemaStore.SessionKey): settings with another array get their own cache, an array that is changed in place keeps its old results.</para>
    /// </summary>
    internal sealed class SharedPropsCache
    {
      internal readonly IMsgPackPropertyIncludeStatically[] Filters;
      internal readonly ConcurrentDictionary<Type, FullPropertyInfo[]>[] PerOrder = CachePerOrder<FullPropertyInfo[]>();

      internal SharedPropsCache(IMsgPackPropertyIncludeStatically[] filters) { Filters = filters; }
    }

    // Weak keys: arrays of filters that are no longer used (e.g. settings created per call) take their caches with them
    private static readonly ConditionalWeakTable<IMsgPackPropertyIncludeStatically[], SharedPropsCache> SharedPropsCaches = new ConditionalWeakTable<IMsgPackPropertyIncludeStatically[], SharedPropsCache>();
    private static readonly IMsgPackPropertyIncludeStatically[] NoStaticFilters = new IMsgPackPropertyIncludeStatically[0];

    private static ConcurrentDictionary<Type, FullPropertyInfo[]> GetSharedPropsCache(MsgPackOptions settings)
    {
      // The settings remember the cache of their filters, so the table is only consulted when the filters change (or for new settings)
      IMsgPackPropertyIncludeStatically[] filters = settings._staticFilters ?? NoStaticFilters;
      SharedPropsCache cache = settings._sharedPropsCache;
      if (cache is null || !ReferenceEquals(cache.Filters, filters))
        settings._sharedPropsCache = cache = SharedPropsCaches.GetValue(filters, f => new SharedPropsCache(f));
      return cache.PerOrder[(int)settings._propertyOrder];
    }

    private static FullPropertyInfo[] GetSerializedPropsWithoutSession(Type type, MsgPackOptions settings)
    {
      bool shared = settings._propertyNameResolvers is null || settings._propertyNameResolvers.Length == 0;
      ConcurrentDictionary<Type, FullPropertyInfo[]> sharedCache = null;
      if (shared)
      {
        sharedCache = GetSharedPropsCache(settings);
        if (sharedCache.TryGetValue(type, out FullPropertyInfo[] cached))
          return cached;
      }

      PropertyInfo[] props = GetProperties(type, settings);
      List<FullPropertyInfo> keptProps = new List<FullPropertyInfo>(props.Length);
      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo full = FullPropertyInfo.GetFullPropInfo(props[t], settings);
        bool keep = IsStaticallyIncluded(full, settings);
        if (!shared)
          full.StaticallyIgnored = !keep;
        if (keep)
          keptProps.Add(full);
      }

      FullPropertyInfo[] result = keptProps.ToArray();
      ThrowIfIdsNotUnique(type, result); // custom resolvers are consulted for every call (they may have their own cache), so are their ids

      if (shared)
        return sharedCache.GetOrAdd(type, result);
      return result;
    }

    /// <summary>
    /// The property ids are the keys of the map an object is serialized to, the serializer does not check them for every object.
    /// </summary>
    /// <exception cref="MsgPackException">When two properties have the same id, or an id is a key the serializer uses for the type id or the content of a collection</exception>
    private static void ThrowIfIdsNotUnique(Type type, FullPropertyInfo[] props)
    {
      for (int t = 0; t < props.Length; t++)
      {
        object id = props[t].PropertyId;
        if (MsgPackOptions.TypeIdKey.Equals(id) || MsgPackOptions.ContentKey.Equals(id))
          throw new MsgPackException($"The id \"{id}\" of property {type.Name}.{props[t].PropertyInfo.Name} is reserved, the serializer uses it for the type id or the content of a collection.");

        for (int i = 0; i < t; i++)
        {
          if (Equals(props[i].PropertyId, id))
            throw new MsgPackException($"The properties {type.Name}.{props[i].PropertyInfo.Name} and {type.Name}.{props[t].PropertyInfo.Name} have the same id \"{id}\", the keys of a map must be unique.");
        }
      }
    }

    /// <summary>
    /// The properties that pass the static filters, their <see cref="PropertyId"/> is the name of the property (the property id resolvers are not consulted).
    /// <para>Cached for the rest of the session (see <see cref="MsgPackSettings._staticPropsCache"/>), <see cref="GetSerializedProps"/> resolves the ids of the same instances.</para>
    /// </summary>
    internal static FullPropertyInfo[] GetStaticallyIncludedProps(Type type, MsgPackOptions settings)
    {
      Dictionary<Type, FullPropertyInfo[]> sessionCache = settings._staticPropsCache;
      if (sessionCache != null && sessionCache.TryGetValue(type, out FullPropertyInfo[] cached))
        return cached;

      if (settings._schemaFrozen) // a shared session, see SchemaSession
        throw SchemaGrowthException.Instance;

      PropertyInfo[] props = GetProperties(type, settings);
      List<FullPropertyInfo> keptProps = new List<FullPropertyInfo>(props.Length);
      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo full = new FullPropertyInfo(props[t]) { PropertyId = props[t].Name };
        bool keep = IsStaticallyIncluded(full, settings);
        full.StaticallyIgnored = !keep;
        if (keep)
          keptProps.Add(full);
      }

      FullPropertyInfo[] result = keptProps.ToArray();
      if (sessionCache != null)
        sessionCache[type] = result;
      return result;
    }

    // Not cached on the property: the shared instances are used with different filters, the callers cache the kept properties
    private static bool IsStaticallyIncluded(FullPropertyInfo full, MsgPackOptions settings)
    {
      IMsgPackPropertyIncludeStatically[] filters = settings._staticFilters;
      if (filters is null)
        return true;

      for (int i = filters.Length - 1; i >= 0; i--)
        if (!filters[i].IncludeProperty(full))
          return false;
      return true;
    }

  }
}
