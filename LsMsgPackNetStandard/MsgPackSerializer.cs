using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace LsMsgPack
{
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
      typeof(string),
      typeof(byte[]),
      typeof(List<>),
      typeof(object[]),
      typeof(Dictionary<,>)
    };

    public static byte[] Serialize<T>(T item, bool dynamicallyCompact = true)
    {
      return Serialize<T>(item, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static byte[] Serialize<T>(T item, MsgPackSettings settings)
    {
      if (MsgPackSerializer.NativelySupportedTypes.Contains(typeof(T))) return MsgPackItem.Pack(item, settings).ToBytes();
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
      if (MsgPackSerializer.NativelySupportedTypes.Contains(tType))
      {
        return MsgPackItem.Pack(item, settings);
        // Maybe we should rather throw an exception here
      }
      PropertyInfo[] props = GetSerializedProps(tType);
      Dictionary<string, object> propVals = new Dictionary<string, object>(props.Length);
      for (int t = props.Length - 1; t >= 0; t--)
      {
        propVals.Add(props[t].Name, props[t].GetValue(item, null));
      }
      return new MpMap(settings) { Value = propVals };
    }

    public static T Deserialize<T>(byte[] source)
    {
      using (MemoryStream ms = new MemoryStream(source))
      {
        return Deserialize<T>(ms);
      }
    }

    public static T Deserialize<T>(Stream stream)
    {
      Type tType = typeof(T);
      if (MsgPackSerializer.NativelySupportedTypes.Contains(tType))
      {
        return MsgPackItem.Unpack(stream).GetTypedValue<T>();
      }
      MpMap map = (MpMap)MsgPackItem.Unpack(stream);
      T result = (T)Materialize(tType, map);
      return result;
    }

    private static object Materialize(Type tType, MpMap map)
    {
      PropertyInfo[] props = GetSerializedProps(tType);
      Dictionary<string, object> propVals = map.GetTypedValue<Dictionary<string, object>>();

      object result = Activator.CreateInstance(tType);

      for (int t = props.Length - 1; t >= 0; t--)
      {
        object val;
        if (propVals.TryGetValue(props[t].Name, out val))
        {
          Type propType = props[t].PropertyType;
          if (!ReferenceEquals(val, null))
          {
            if (!MsgPackSerializer.NativelySupportedTypes.Contains(propType)
              && val is KeyValuePair<object, object>[])
            {
              val = Materialize(propType, new MpMap((KeyValuePair<object, object>[])val, map.Settings));
            }
            if (propType.IsArray && !(propType == typeof(object)))
            {
              // Need to cast object[] to whatever[]
              object[] valAsArr = (object[])val;
              propType = propType.GetElementType();
              Array newInstance = Array.CreateInstance(propType, valAsArr.Length);

              // Part of the check for complex types can be done outside the loop
              bool complexTypes = !MsgPackSerializer.NativelySupportedTypes.Contains(propType);
              for (int i = valAsArr.Length - 1; i >= 0; i--)
              {
                if (complexTypes && !ReferenceEquals(valAsArr[i], null)
                  && valAsArr[i] is KeyValuePair<object, object>[])
                {
                  valAsArr[i] = Materialize(propType, new MpMap((KeyValuePair<object, object>[])valAsArr[i], map.Settings));
                }
                newInstance.SetValue(valAsArr[i], i);
              }
              props[t].SetValue(result, newInstance, null);
              continue;
            }
            if (typeof(IList).IsAssignableFrom(propType))
            {
              IList newInstance = (IList)Activator.CreateInstance(propType);

              object[] valAsArr = (object[])val;

              // Part of the check for complex types can be done outside the loop
              bool complexTypes = !MsgPackSerializer.NativelySupportedTypes.Contains(propType);
              for (int i = 0; i < valAsArr.Length; i++)
              {
                if (complexTypes && !ReferenceEquals(valAsArr[i], null)
                  && valAsArr[i] is KeyValuePair<object, object>[])
                {
                  valAsArr[i] = Materialize(propType, new MpMap((KeyValuePair<object, object>[])valAsArr[i], map.Settings));
                }
                newInstance.Add(valAsArr[i]);
              }
              props[t].SetValue(result, newInstance, null);
              continue;
            }
          }
          // Fix ArgumentException like "System.Byte cannot be converted to System.Nullable`1[System.Int32]"
          Type nullableType = Nullable.GetUnderlyingType(props[t].PropertyType);
          if (!(nullableType is null) && !(val is null))
          {
            if (val.GetType() != nullableType)
            {
              val = Convert.ChangeType(val, nullableType);
            }
          }
          props[t].SetValue(result, val, null);
        }
      }

      return result;
    }

    private static PropertyInfo[] GetSerializedProps(Type type)
    {
      PropertyInfo[] props = type.GetProperties();
      List<PropertyInfo> keptProps = new List<PropertyInfo>(props.Length);
      for (int t = props.Length - 1; t >= 0; t--)
      {
        if (CheckIgnored(props[t])) continue;
        keptProps.Add(props[t]);
      }
      return keptProps.ToArray();
    }

    private static bool CheckIgnored(PropertyInfo propInf)
    {
      object[] atts = propInf.GetCustomAttributes(true);
      bool ignore = false;
      for (int i = atts.Length - 1; i >= 0; i--)
      {
        if (atts[i] is XmlIgnoreAttribute) { ignore = true; break; }
      }
      return ignore;
    }

  }
}
