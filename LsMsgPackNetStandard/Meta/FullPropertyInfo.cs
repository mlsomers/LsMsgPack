using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace LsMsgPack.Meta
{
  public class FullPropertyInfo
  {
    private static readonly Dictionary<PropertyInfo, FullPropertyInfo> Cache = new Dictionary<PropertyInfo, FullPropertyInfo>();
    private static readonly Dictionary<Type, ConstructorInfo> _constructorTakingType = new Dictionary<Type, ConstructorInfo>();

    public static FullPropertyInfo GetFullPropInfo(PropertyInfo propertyInfo, MsgPackSettings settings)
    {
      if (propertyInfo == null)
        return null;

      FullPropertyInfo full;
      if (Cache.TryGetValue(propertyInfo, out full))
        return full;

      full = new FullPropertyInfo(propertyInfo);

      for (int t = settings.PropertyNameResolvers.Length - 1; t >= 0; t--)
      {
        full.PropertyId = settings.PropertyNameResolvers[t].GetId(full);
        if (full.PropertyId != null)
          break;
      }

      if (full.PropertyId == null)
        full.PropertyId = full.PropertyInfo.Name;

      Cache.Add(propertyInfo, full);
      return full;
    }

    private FullPropertyInfo(PropertyInfo prop)
    {
      PropertyInfo = prop;
      object[] atts = prop.GetCustomAttributes(true);
      CustomAttributes = new Dictionary<string, object>(atts.Length);
      for (int t = atts.Length - 1; t >= 0; t--)
      {
        string attName = atts[t].GetType().Name;
        CustomAttributes.TryAdd(attName, atts[t]);
      }
      AssignedToType = prop.PropertyType;
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
      if (_constructorTakingType.TryGetValue(type, out ConstructorInfo constructor))
        return constructor;

      // Todo: concurrent locking system

      ConstructorInfo ci = AssignedToType.GetConstructor(new[] { type });
      _constructorTakingType.Add(type, ci);
      return ci;
    }

    public override string ToString()
    {
      string ignored = (StaticallyIgnored.HasValue && StaticallyIgnored.Value) ? " (ignored)":string.Empty;
      string propInfo = (PropertyInfo is null) ? " not a property" : $" property: {PropertyInfo.Name}";
      return $"{AssignedToType}{ignored}{propInfo}";
    }

  }
}
