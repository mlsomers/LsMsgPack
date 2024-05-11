using LsMsgPack.Meta;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace LsMsgPack
{
  /// <summary>
  /// The main entry point, serialize and deserialize objects from here
  /// </summary>
  public static class MsgPackSerializer
  {

    public static readonly Type[] NativelySupportedTypes = new Type[]{
      typeof(bool),
      typeof(sbyte),
      typeof(short),
      typeof(int),
      typeof(long),
      typeof(byte ),
      typeof(ushort),
      typeof(uint),
      typeof(ulong),
      typeof(float),
      typeof(double),
      typeof(string),
      typeof(byte[]),
      typeof(object[]),
      typeof(Guid)
    };

    public static readonly Type[] NativelySupportedGenericTypes = new Type[]{
      typeof(List<>),
      typeof(Dictionary<,>),
    };

    public static bool IsNativelySupported(Type type)
    {
      return NativelySupportedTypes.Contains(type);
    }

    public static void CacheAssemblyTypes(Assembly assembly)
    {
      TypeResolver.CacheAssembly(assembly, null);
    }

    public static void CacheAssemblyTypes(Type type)
    {
      TypeResolver.CacheAssembly(type.Assembly, type.Name);
    }

    public static byte[] Serialize<T>(T item, bool dynamicallyCompact = true)
    {
      return Serialize<T>(item, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static byte[] Serialize<T>(T item, MsgPackSettings settings)
    {
      if (IsNativelySupported(typeof(T))) return MsgPackItem.Pack(item, settings).ToBytes();
      MemoryStream ms = new MemoryStream();
      Serialize(item, ms, settings);
      return ms.ToArray();
    }

    public static void Serialize<T>(T item, Stream target, bool dynamicallyCompact = true)
    {
      Serialize<T>(item, target, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static void Serialize<T>(T item, Stream target, MsgPackSettings settings)
    {
      MsgPackItem packed = SerializeObject(item, settings);
      byte[] buffer = packed.ToBytes();
      target.Write(buffer, 0, buffer.Length);
      return;
    }

    public static MsgPackItem SerializeObject(object item, bool dynamicallyCompact = true)
    {
      return SerializeObject(item, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static MsgPackItem SerializeObject(object item, MsgPackSettings settings)
    {
      if (ReferenceEquals(item, null)) return new MpNull();
      Type tType = item.GetType();
      if (tType.IsArray)
        tType = tType.GetElementType();

      if (IsNativelySupported(tType))
        return MsgPackItem.Pack(item, settings);

      FullPropertyInfo[] props;
      Dictionary<object, object> propVals;
      if (tType == typeof(MsgPackTypedItem)) // Add Type name as first entry with empty key
      {
        // TODO: Untangle type and nested (Array, Ienumerable, and IDictionary) types

        MsgPackTypedItem typedItem = (MsgPackTypedItem)item;
        if (typedItem.AssignmentType.IsArray)
        {
          Type arrType = typedItem.AssignmentType.GetElementType();
          Array orgObjects = (Array)typedItem.Instance;
          object[] objects = new object[orgObjects.Length];
          for (int t = objects.Length - 1; t >= 0; t--)
            objects[t] = GetTypedOrUntyped(settings, arrType, orgObjects.GetValue(t), typedItem.PropertyInfo);
          return new MpArray(settings) { Value = objects };
        }

        props = GetSerializedProps(typedItem.Type, settings);
        propVals = new Dictionary<object, object>(props.Length + 1);

        object typeId = typedItem.GetTypeIdentifier(settings);
        propVals.Add(string.Empty, typeId);
        item = typedItem.Instance;

        if (typedItem.Instance is IDictionary)
        {
          Type[] types = typedItem.Type.GenericTypeArguments;
          Type arrType = typeof(KeyValuePair<,>).MakeGenericType(types);
          List<KeyValuePair<object, object>> objects = new List<KeyValuePair<object, object>>();
          foreach (DictionaryEntry o in (IDictionary)typedItem.Instance)
          {
            KeyValuePair<object, object> kv = new KeyValuePair<object, object>(
              GetTypedOrUntyped(settings, types[0], o.Key, typedItem.PropertyInfo),
              GetTypedOrUntyped(settings, types[1], o.Value, typedItem.PropertyInfo)
              );
            objects.Add(kv);
          }

          if (arrType != typedItem.AssignmentType)
            return
              new MpMap(new[]
              {
              new KeyValuePair<object,object>(string.Empty, typeId),
              new KeyValuePair<object,object>("@", new MpMap(objects.ToArray(), settings)),
              }, settings);
          else
            return new MpMap(objects.ToArray(), settings);
        }
        else if (typedItem.Instance is IEnumerable)
        {
          Type arrType = typeof(object);
          Type[] types = typedItem.AssignmentType.GenericTypeArguments;
          if (types.Length == 1)
            arrType = types[0];
          List<object> objects = new List<object>();
          foreach (object o in (IEnumerable)typedItem.Instance)
            objects.Add(GetTypedOrUntyped(settings, arrType, o, typedItem.PropertyInfo));

          propVals.Add("@", new MpArray(settings) { Value = objects.ToArray() });
        }
      }
      else
      {
        props = GetSerializedProps(tType, settings);
        propVals = new Dictionary<object, object>(props.Length);
      }

      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo prop = props[t];
        PropertyInfo prp = prop.PropertyInfo;
        object value = prp.GetValue(item, null);

        bool exclude = false;
        for (int i = settings.DynamicFilters.Length - 1; i >= 0; i--)
          if (!settings.DynamicFilters[i].IncludeProperty(prop, value)) { exclude = true; break; }

        if (exclude)
          continue;

        if (value is null)
        {
          propVals.Add(prop.PropertyId, value);
          continue;
        }
        propVals.Add(prop.PropertyId, GetTypedOrUntyped(settings, prp.PropertyType, value, prop));
      }

      return new MpMap(settings) { Value = propVals };
    }

    private static object GetTypedOrUntyped(MsgPackSettings settings, Type assignementType, object value, FullPropertyInfo prp)
    {
      if (value is null)
        return null;
      Type valType = value.GetType();

      if (IsNativelySupported(valType))
        return value;

      if (settings._addTypeName == AddTypeIdOption.Never)
        return value;

      else if ((settings._addTypeName & AddTypeIdOption.Always) > 0)
        return new MsgPackTypedItem(value, valType, assignementType, prp);
      else // If Ambiguious
      {
        Type prpType;
        if (assignementType.IsArray) // TODO: Untangle type and nested (Array, Ienumerable, and IDictionary) types
          prpType = assignementType.GetElementType();
        else if (assignementType is IDictionary && assignementType.IsGenericType)
          prpType = typeof(KeyValuePair<,>).MakeGenericType(assignementType.GenericTypeArguments);
        else if (assignementType is IEnumerable && assignementType.IsGenericType)
        {
          Type[] types = assignementType.GenericTypeArguments;
          if (types.Length == 1)
            prpType = types[0];
          else
            prpType = assignementType;
        }
        else
          prpType = assignementType;

        if (prpType == valType)
          return value;
        else
          return new MsgPackTypedItem(value, valType, assignementType, prp);
      }
    }

    public static T Deserialize<T>(byte[] source)
    {
      return Deserialize<T>(source, new MsgPackSettings());
    }

    public static T Deserialize<T>(byte[] source, MsgPackSettings settings)
    {
      using (MemoryStream ms = new MemoryStream(source))
      {
        return Deserialize<T>(ms, settings);
      }
    }

    public static T Deserialize<T>(Stream stream)
    {
      return Deserialize<T>(stream, new MsgPackSettings());
    }

    public static T Deserialize<T>(Stream stream, MsgPackSettings settings)
    {
      Type tType = typeof(T);
      if (IsNativelySupported(tType))
      {
        return MsgPackItem.Unpack(stream).GetTypedValue<T>();
      }
      MpMap map = (MpMap)MsgPackItem.Unpack(stream, settings);
      T result = (T)Materialize(tType, map);
      return result;
    }

    private static object Materialize(Type tType, MpMap map, FullPropertyInfo rootProp = null)
    {
      Dictionary<string, object> propVals = map.GetTypedValue<Dictionary<string, object>>();

      object val;
      bool hasName = propVals.TryGetValue(string.Empty, out val);
      string name = val as string;
      bool namesMatch = hasName && !string.IsNullOrWhiteSpace(name) && tType.FullName.EndsWith(name, StringComparison.InvariantCultureIgnoreCase);
      if (!namesMatch)
      {
        if (hasName)
        {
          tType = TypeResolver.Resolve(val, tType, rootProp, map, propVals);
        }
        else if (tType.IsAbstract || tType.IsInterface)
        {
          tType = TypeResolver.Resolve(null, tType, rootProp, map, propVals);
        }
      }
      FullPropertyInfo[] props = GetSerializedProps(tType, map.Settings);

      object result;
      if (typeof(IEnumerable).IsAssignableFrom(tType) && propVals.TryGetValue("@", out object items)) // IEnumerable and IDictionary types
      {
        if (tType.GenericTypeArguments.Length == 1) // IEnumerable
        {
          object[] itemArr = (object[])items;
          Array typedArr = Array.CreateInstance(tType.GenericTypeArguments[0], itemArr.Length);
          KeyValuePair<object, object>[][] kvs = itemArr.Cast<KeyValuePair<object, object>[]>().ToArray();
          for (int t = kvs.Length - 1; t >= 0; t--)
            typedArr.SetValue(Materialize(tType.GenericTypeArguments[0], new MpMap(kvs[t], map.Settings), null), t);

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
              value = itemArr[t].Key;

            object entry = Activator.CreateInstance(itemType, key, value);
            typedArr.SetValue(entry, t);
          }
          result = Activator.CreateInstance(tType, typedArr);
        }
        else
        {
          result = Activator.CreateInstance(tType, true);
        }
      }
      else
      {
        result = Activator.CreateInstance(tType, true);
      }

      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo prop = props[t];
        PropertyInfo prp = prop.PropertyInfo;
        if (propVals.TryGetValue(prp.Name, out val))
        {
          Type propType = prp.PropertyType;
          if (!ReferenceEquals(val, null))
          {
            if (!IsNativelySupported(propType)
              && val is KeyValuePair<object, object>[])
            {
              val = Materialize(propType, new MpMap((KeyValuePair<object, object>[])val, map.Settings), prop);
            }
            if (propType.IsArray && !(propType == typeof(object)))
            {
              // Need to cast object[] to whatever[]
              object[] valAsArr = (object[])val;
              propType = propType.GetElementType();
              Array newInstance = Array.CreateInstance(propType, valAsArr.Length);

              // Part of the check for complex types can be done outside the loop
              bool complexTypes = !IsNativelySupported(propType);
              for (int i = valAsArr.Length - 1; i >= 0; i--)
              {
                if (complexTypes && !ReferenceEquals(valAsArr[i], null)
                  && valAsArr[i] is KeyValuePair<object, object>[])
                {
                  valAsArr[i] = Materialize(propType, new MpMap((KeyValuePair<object, object>[])valAsArr[i], map.Settings), prop);
                }
                newInstance.SetValue(valAsArr[i], i);
              }
              prp.SetValue(result, newInstance, null);
              continue;
            }
            else if (typeof(IList).IsAssignableFrom(propType))
            {
              IList newInstance = (IList)Activator.CreateInstance(propType, true);

              object[] valAsArr = (object[])val;

              // Part of the check for complex types can be done outside the loop
              bool complexTypes = !IsNativelySupported(propType);
              for (int i = 0; i < valAsArr.Length; i++)
              {
                if (complexTypes && !ReferenceEquals(valAsArr[i], null)
                  && valAsArr[i] is KeyValuePair<object, object>[])
                {
                  valAsArr[i] = Materialize(propType, new MpMap((KeyValuePair<object, object>[])valAsArr[i], map.Settings), prop);
                }
                newInstance.Add(valAsArr[i]);
              }
              prp.SetValue(result, newInstance, null);
              continue;
            }
          }
          // Fix ArgumentException like "System.Byte cannot be converted to System.Nullable`1[System.Int32]"
          Type nullableType = Nullable.GetUnderlyingType(prp.PropertyType);
          if (!(nullableType is null) && !(val is null))
          {
            if (val.GetType() != nullableType)
            {
              val = Convert.ChangeType(val, nullableType);
            }
          }
          if (propType == typeof(Guid))
          {
            prp.SetValue(result, new Guid((byte[])val), null);
            continue;
          }
          prp.SetValue(result, val, null);
        }
      }

      return result;
    }

    private static FullPropertyInfo[] GetSerializedProps(Type type, MsgPackSettings settings)
    {
      PropertyInfo[] props = type.GetProperties();
      List<FullPropertyInfo> keptProps = new List<FullPropertyInfo>(props.Length);
      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo full = FullPropertyInfo.GetFullPropInfo(props[t], settings);

        if (full.StaticallyIgnored.HasValue)
        {
          if (full.StaticallyIgnored.Value) // statically cached to ignore always
            continue;
        }
        else
        {
          bool keep = true;
          for (int i = settings.StaticFilters.Length - 1; i >= 0; i--)
            if (!settings.StaticFilters[i].IncludeProperty(full)) { keep = false; break; }

          full.StaticallyIgnored = !keep;

          if (!keep)
            continue;
        }

        keptProps.Add(full);
      }
      return keptProps.ToArray();
    }

  }
}
