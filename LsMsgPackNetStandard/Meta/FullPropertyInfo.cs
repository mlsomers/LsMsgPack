using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace LsMsgPack.Meta
{
  public class FullPropertyInfo
  {
    // Type is a IMsgPackPropertyIdResolver Type
    private static readonly ConcurrentDictionary<PropertyInfo, FullPropertyInfo> Cache = new ConcurrentDictionary<PropertyInfo, FullPropertyInfo>();
    private Dictionary<Type, ConstructorInfo> _constructorTakingType; // per instance (created when first used), constructors differ per AssignedToType

    public static FullPropertyInfo GetFullPropInfo(PropertyInfo propertyInfo, MsgPackSettings settings)
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
    private static void ResolvePropertyId(FullPropertyInfo full, MsgPackSettings settings)
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

    /// <summary>
    /// Note that this may not be the complete set, When multiple attributes of the same type are applied, only the first one will be listed here, so if your custom attribute supports multiple instances on a property you will need to get them from the propertyInfo.
    /// </summary>
    public Dictionary<string, object> CustomAttributes { get; set; }

    /// <summary>
    /// Cached, only use it for attributes or other static metadata.
    /// <para>Do not set this when checking for null or default values.</para>
    /// Once this is set to true, no future calls to IMsgPackPropertyInclude....IncludeProperty() will be made for this property.
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

    internal static FullPropertyInfo[] GetSerializedProps(Type type, MsgPackSettings settings)
    {
      Dictionary<Type, FullPropertyInfo[]> sessionCache = settings._serializedPropsCache;
      if (sessionCache is null)
        return GetSerializedPropsWithoutSession(type, settings);

      if (sessionCache.TryGetValue(type, out FullPropertyInfo[] cached))
        return cached;

      // Within a session each property is resolved once: first the static filters, then the ids of the properties that are kept.
      // The indexed schema asks for the kept properties of the type (see GetStaticallyIncludedProps) when resolving the first id.
      FullPropertyInfo[] props = GetStaticallyIncludedProps(type, settings);
      for (int t = 0; t < props.Length; t++)
        ResolvePropertyId(props[t], settings);
      ThrowIfIdsNotUnique(type, props);

      sessionCache[type] = props;
      return props;
    }

    // Without property id resolvers the FullPropertyInfo instances (and their StaticallyIgnored) are shared by all settings (see GetFullPropInfo), so the result only depends on the type
    private static readonly ConcurrentDictionary<Type, FullPropertyInfo[]> SerializedPropsCache = new ConcurrentDictionary<Type, FullPropertyInfo[]>();

    private static FullPropertyInfo[] GetSerializedPropsWithoutSession(Type type, MsgPackSettings settings)
    {
      bool shared = settings._propertyNameResolvers is null || settings._propertyNameResolvers.Length == 0;
      if (shared && SerializedPropsCache.TryGetValue(type, out FullPropertyInfo[] cached))
        return cached;

      PropertyInfo[] props = GetProperties(type);
      List<FullPropertyInfo> keptProps = new List<FullPropertyInfo>(props.Length);
      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo full = FullPropertyInfo.GetFullPropInfo(props[t], settings);
        if (IsStaticallyIncluded(full, settings))
          keptProps.Add(full);
      }

      FullPropertyInfo[] result = keptProps.ToArray();
      ThrowIfIdsNotUnique(type, result); // custom resolvers are consulted for every call (they may have their own cache), so are their ids

      if (shared)
        return SerializedPropsCache.GetOrAdd(type, result);
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
        if (MsgPackSerializer.TypeIdKey.Equals(id) || MsgPackSerializer.ContentKey.Equals(id))
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
    internal static FullPropertyInfo[] GetStaticallyIncludedProps(Type type, MsgPackSettings settings)
    {
      Dictionary<Type, FullPropertyInfo[]> sessionCache = settings._staticPropsCache;
      if (sessionCache != null && sessionCache.TryGetValue(type, out FullPropertyInfo[] cached))
        return cached;

      PropertyInfo[] props = GetProperties(type);
      List<FullPropertyInfo> keptProps = new List<FullPropertyInfo>(props.Length);
      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo full = new FullPropertyInfo(props[t]) { PropertyId = props[t].Name };
        if (IsStaticallyIncluded(full, settings))
          keptProps.Add(full);
      }

      FullPropertyInfo[] result = keptProps.ToArray();
      if (sessionCache != null)
        sessionCache[type] = result;
      return result;
    }

    private static bool IsStaticallyIncluded(FullPropertyInfo full, MsgPackSettings settings)
    {
      if (full.StaticallyIgnored.HasValue) // statically cached
        return !full.StaticallyIgnored.Value;

      bool keep = true;
      for (int i = settings._staticFilters.Length - 1; i >= 0; i--)
        if (!settings._staticFilters[i].IncludeProperty(full)) { keep = false; break; }

      full.StaticallyIgnored = !keep;
      return keep;
    }

  }
}
