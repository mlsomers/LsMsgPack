using LsMsgPack.Meta;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace LsMsgPack
{
  public static partial class MsgPackSerializer
  {
    private static object Materialize(Type assignedTo, MpMap map, FullPropertyInfo rootProp = null)
    {
      Dictionary<object, object> propVals = new Dictionary<object, object>(map.Count, new MapConversionEqualityComparer());
      map.FillDictionary(propVals);

      object typeId=null;
      bool hasName = propVals.TryGetValue(string.Empty, out typeId);
      Type tType = TypeResolver.Resolve(typeId, assignedTo, rootProp, map, propVals);

      FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(tType, map.Settings);

      object result;
      if (typeof(IEnumerable).IsAssignableFrom(tType) && propVals.TryGetValue("@", out object items)) // IEnumerable and IDictionary types
      {
        if (tType.GenericTypeArguments.Length == 1) // IEnumerable
        {
          Array itemArr = (Array)items;
          Array typedArr = Array.CreateInstance(tType.GenericTypeArguments[0], itemArr.Length);

          //object[] kvs = itemArr.Cast<object[]>().ToArray();
          for (int t = itemArr.Length - 1; t >= 0; t--)
          {
            //typedArr.SetValue(Materialize(tType.GenericTypeArguments[0], itemArr[t], rootProp), t);

            object val = itemArr.GetValue(t);
            if (val != null)
            {
              Type valType= val.GetType();

              if (tType.GenericTypeArguments[0] != valType) { 
                if(valType == typeof(KeyValuePair<object, object>[]))
                  val = Materialize(tType.GenericTypeArguments[0], new MpMap((KeyValuePair<object, object>[])val, map.Settings));
              }
            }
            typedArr.SetValue(val, t);
          }

          result = Activator.CreateInstance(tType, typedArr);
        }
        else if (tType.GenericTypeArguments.Length == 2) // IDictionary
        {
          KeyValuePair<object, object>[] itemArr = (KeyValuePair<object, object>[])items;
          Type itemType = typeof(KeyValuePair<,>).MakeGenericType(tType.GenericTypeArguments[0], tType.GenericTypeArguments[1]);
          Array typedArr = Array.CreateInstance(itemType, itemArr.Length);
          for (int t = itemArr.Length - 1; t >= 0; t--)
          {
            object key;
            if (itemArr[t].Key is KeyValuePair<object, object>[])
              key = Materialize(tType.GenericTypeArguments[0], new MpMap((KeyValuePair<object, object>[])itemArr[t].Key, map.Settings), null);
            else
              key = itemArr[t].Key;

            object value;
            if (itemArr[t].Value is KeyValuePair<object, object>[])
              value = Materialize(tType.GenericTypeArguments[1], new MpMap((KeyValuePair<object, object>[])itemArr[t].Value, map.Settings), null);
            else
              value = itemArr[t].Value;

            object entry = Activator.CreateInstance(itemType, key, value);
            typedArr.SetValue(entry, t);
          }
          result = Activator.CreateInstance(tType, typedArr);
        }
        else if (tType.IsArray)
        {
          Type elementType = tType.GetElementType();
          Array itemArr = (Array)items;
          result = Array.CreateInstance(elementType, itemArr.Length);
          Array typedArr = (Array)result;
          for (int t = itemArr.Length - 1; t >= 0; t--)
          {
            //typedArr.SetValue(Materialize(tType.GenericTypeArguments[0], itemArr[t], rootProp), t);

            object val = itemArr.GetValue(t);
            if(val != null) { 
              Type valType = val.GetType();

              if (elementType != valType)
              {
                if (valType == typeof(KeyValuePair<object, object>[]))
                  val = Materialize(elementType, new MpMap((KeyValuePair<object, object>[])val, map.Settings));
              }
            }
            typedArr.SetValue(val, t);
          }

        }
        else
        {
          result = CreateInstance(tType);
        }
      }
      else
      {
        result = CreateInstance(tType);
      }

      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo prop = props[t];
        PropertyInfo prp = prop.PropertyInfo;

        object propval = null;

        //if (propVals.TryGetValue(prp.Name, out propval))
        if (propVals.TryGetValue(prop.PropertyId, out propval))
        {
          Type propType = prp.PropertyType;

          object ConvertedVal;
          if (propval is MpMap)
            ConvertedVal = Materialize(propType, (MpMap)propval, prop);
          else if (propval is KeyValuePair<object, object>[] && prp.PropertyType != typeof(KeyValuePair<object, object>[]))
            ConvertedVal = Materialize(propType, new MpMap((KeyValuePair<object, object>[])propval, map.Settings), prop);
          else
            ConvertedVal = ConvertDeserializeValue(propval, propType, map, prop);

          prp.SetValue(result, ConvertedVal, null);
        }
      }

      return result;
    }

    private static object CreateInstance(Type type)
    {
      try
      {
        return Activator.CreateInstance(type, true);
      }
      catch
      {
        try
        {
          return System.Runtime.Serialization.FormatterServices.GetSafeUninitializedObject(type);
        }
        catch
        {
          return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        }
      }
    }

    private static object ConvertDeserializeValue(object val, Type assignType, MpMap map, FullPropertyInfo prop)
    {
      if (ReferenceEquals(val, null))
      {
        return null;
      }

      Type nestedAssignType = null;

      if (val is KeyValuePair<object, object>[])
      {
        KeyValuePair<object, object>[] vVal = (KeyValuePair<object, object>[])val;
        if (vVal.Length <= 0)
        {
          val = CreateInstance(assignType);
        }
        else if ((vVal[0].Key as string) == "")
        {
          string nestedAssignTypestr = vVal[0].Value.ToString();
          nestedAssignType = TypeResolver.Resolve(nestedAssignTypestr, assignType, prop, map, null);
          val = Materialize(assignType, new MpMap(vVal, map.Settings), prop); // <- ???
        }
        else if (!(vVal[0].Key is null) && assignType.IsGenericType && typeof(Dictionary<,>) == assignType.GetGenericTypeDefinition())
        {
          val = CreateInstance(assignType);
          IDictionary dictionary = (IDictionary)val;
          for (int t = vVal.Length - 1; t >= 0; t--)
          {
            KeyValuePair<object, object> item = vVal[t];
            if (assignType.GenericTypeArguments[0] == vVal[t].Key.GetType() && assignType.GenericTypeArguments[1] == vVal[t].Value.GetType())
            {
              dictionary.Add(item.Key, item.Value);
            }
            else
            {
              object key = assignType.GenericTypeArguments[0] == vVal[t].Key.GetType() ? vVal[t].Key : ConvertDeserializeValue(vVal[t].Key, assignType.GenericTypeArguments[0], map, prop);
              object value = assignType.GenericTypeArguments[1] == vVal[t].Value.GetType() ? vVal[t].Value : ConvertDeserializeValue(vVal[t].Value, assignType.GenericTypeArguments[1], map, prop);

              dictionary.Add(key, value);
            }
          }
          return dictionary;
        }
        else
          val = Materialize(assignType, new MpMap(vVal, map.Settings), prop); // <- ???

      }
      Type valType = val.GetType();
      if (assignType == valType)
      {
        return val;
      }
      if (assignType.IsArray && !(assignType == typeof(object)))
      {
        // Need to cast object[] to whatever[]
        object[] valAsArr = (object[])val;
        assignType = nestedAssignType?.GetElementType() ?? assignType.GetElementType();
        Array newInstance = Array.CreateInstance(assignType, valAsArr.Length);

        for (int i = valAsArr.Length - 1; i >= 0; i--)
        {
          if (!ReferenceEquals(valAsArr[i], null) && valAsArr[i] is KeyValuePair<object, object>[])
          {
            valAsArr[i] = Materialize(assignType, new MpMap((KeyValuePair<object, object>[])valAsArr[i], map.Settings), prop);
          }
          newInstance.SetValue(valAsArr[i], i);
        }
        return newInstance;
      }
      else if (typeof(IList).IsAssignableFrom(assignType))
      {
        IList newInstance;

        ConstructorInfo specialConstructor = prop.GetConstructorTaking(valType);
        if (specialConstructor != null)
        {
          newInstance = (IList)specialConstructor.Invoke(new[] { val });
        }
        else
        {
          object[] valAsArr = (object[])val;
          specialConstructor = prop.GetConstructorTaking(typeof(int));
          if (specialConstructor != null)
          {
            newInstance = (IList)specialConstructor.Invoke(new object[] { valAsArr.Length });
          }
          else
            newInstance = (IList)CreateInstance(assignType);

          for (int i = 0; i < valAsArr.Length; i++)
          {
            if (!ReferenceEquals(valAsArr[i], null)
              && valAsArr[i] is KeyValuePair<object, object>[])
            {
              valAsArr[i] = Materialize(assignType, new MpMap((KeyValuePair<object, object>[])valAsArr[i], map.Settings), prop);
            }
            newInstance.Add(valAsArr[i]);
          }
        }
        return newInstance;
      }

      // Fix ArgumentException like "System.Byte cannot be converted to System.Nullable`1[System.Int32]"
      Type nullableType = Nullable.GetUnderlyingType(assignType);
      if (!(nullableType is null) && !(val is null))
      {
        if (nullableType == valType)
        {
          return val;
        }

        if (nullableType == typeof(Guid))
        {
          return new Guid((byte[])val);
        }
        if (val.GetType() != nullableType)
        {
          val = Convert.ChangeType(val, nullableType);
        }
      }
      if (assignType == typeof(Guid))
      {
        return new Guid((byte[])val);
      }
      if (MsgPackMeta.NumericTypes.Contains(assignType))
      {
        return Convert.ChangeType(val, assignType);
      }
      return val;
    }
  }
}
