using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace LsMsgPack
{
  public static partial class MsgPackSerializer
  {

    public static MsgPackItem SerializeObject(object item, MsgPackSettings settings, FullPropertyInfo assignedTo = null)
    {
      if (ReferenceEquals(item, null))
        return new MpNull();

      Type tType = item.GetType();
      Type nullableType = Nullable.GetUnderlyingType(tType);
      if (!(nullableType is null))
        tType = nullableType;

      MsgPackItem packed = MsgPackItem.Pack(item, settings, tType);
      if (packed != null && !(!(item is string) && item is IEnumerable))
      {
        if (assignedTo?.AssignedToType is null
          || settings._addTypeIdOptions == AddTypeIdOption.Never
          || tType.IsPrimitive
          || tType == typeof(string)
          || (settings._addTypeIdOptions.HasFlag(AddTypeIdOption.IfAmbiguious) && assignedTo?.AssignedToType == tType))
          return packed;
      }

      FullPropertyInfo[] props;
      Dictionary<object, object> propVals;
      SerializeEnumerableAttribute handleItems = null;

      if (item is IEnumerable) // typeof(IEnumerable).IsAssignableFrom(tType))
      {
        if (assignedTo?.CustomAttributes != null && assignedTo.CustomAttributes.TryGetValue(nameof(SerializeEnumerableAttribute), out object att)) // GetCustomAttribute<SerializeEnumerableAttribute>(true);
          handleItems = (SerializeEnumerableAttribute)att;

        if (handleItems is null)
          handleItems = tType.GetCustomAttribute<SerializeEnumerableAttribute>(true);

        if (handleItems is null)
        {
          handleItems = new SerializeEnumerableAttribute();
          if (tType.IsArray)
            handleItems.ElementType = tType.GetElementType();
          else if (tType is IDictionary)
          {
            Type[] types = tType.GenericTypeArguments;
            handleItems.ElementType = typeof(KeyValuePair<,>).MakeGenericType(types);
          }
          else
          {
            Type[] types = tType.GenericTypeArguments;
            if (types.Length == 1)
              handleItems.ElementType = types[0];
            else if (item is IDictionary)
            {
              // do nothing, dictionary should be in -> packed
            }
            else
              throw new NotImplementedException(string.Concat("Todo: check if we can derive element type from IEnumerable<T>.",
                "For now decorate/annotate your fancy collection (", tType.Name, assignedTo is null ? "" : ") or property (" + assignedTo.PropertyInfo.Name, ") with a [SerializeEnumerable] Attribute specifying the type of the elements and weather to include or exclude other properties..."));
          }
        }
      }

      // Any complex object with properties
      props = FullPropertyInfo.GetSerializedProps(tType, settings);
      propVals = new Dictionary<object, object>(props.Length);
      bool addTypeId = false;

      if (settings._addTypeIdOptions != AddTypeIdOption.Never)
      {
        if ((settings._addTypeIdOptions.HasFlag(AddTypeIdOption.IfAmbiguious) && assignedTo?.AssignedToType != tType)
          || settings._addTypeIdOptions.HasFlag(AddTypeIdOption.Always))
        {
          object typeIdd = GetTypeIdentifier(tType, settings, assignedTo);
          propVals.Add(string.Empty, typeIdd);
          addTypeId = true;
        }
      }

      if (packed != null)
      {
        if (settings._addTypeIdOptions == AddTypeIdOption.Never)
          return packed;
        
        propVals.Add("@", packed);
        return new MpMap(settings) { Value = propVals };
      }

      if (handleItems != null)
      {
        if (handleItems.SerializeElements)
        {
          List<MsgPackItem> objects = new List<MsgPackItem>();

          FullPropertyInfo assignedToInfo = new FullPropertyInfo(handleItems.ElementType);
          foreach (object o in (IEnumerable)item)
            objects.Add(SerializeObject(o, settings, assignedToInfo));

          MpArray arr = new MpArray(settings) { Value = objects.ToArray() };

          if (!addTypeId && !handleItems.SerializeProperties) // no need to wrap the array in a map
            return arr;

          propVals.Add("@", arr);
        }

        if (!handleItems.SerializeProperties)
        {
          return new MpMap(settings) { Value = propVals };
        }
      }

      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo prop = props[t];
        PropertyInfo prp = prop.PropertyInfo;
        object value = prp.GetValue(item, null);

        bool exclude = false;
        for (int i = settings._dynamicFilters.Length - 1; i >= 0; i--)
          if (!settings._dynamicFilters[i].IncludeProperty(prop, value)) { exclude = true; break; }

        if (exclude)
          continue;

        if (value is null)
        {
          propVals.Add(prop.PropertyId, value);
          continue;
        }
        propVals.Add(prop.PropertyId, SerializeObject(value, settings, prop)); //GetTypedOrUntyped(settings, prp.PropertyType, value, prop));
      }

      return new MpMap(settings) { Value = propVals };
    }

    /// <summary>
    /// This can be overridden by implementing <see cref="IMsgPackTypeResolver">IMsgPackTypeResolver</see>.
    /// </summary>
    private static object GetTypeIdentifier(Type type, MsgPackSettings settings, FullPropertyInfo propertyInfo)
    {
      object typeId = null;

      for (int t = settings._typeResolvers.Length - 1; t >= 0; t--)
      {
        typeId = settings._typeResolvers[t].IdForType(type, propertyInfo, settings);
        if (typeId != null)
          break;
      }
      if (typeId is null && !((settings._addTypeIdOptions & AddTypeIdOption.NoDefaultFallBack) > 0))
      {
        bool fullname = (settings._addTypeIdOptions & AddTypeIdOption.FullName) > 0;
        typeId = TypeResolver.GetTypeName(type, fullname);
      }

      return typeId;
    }
  }
}
