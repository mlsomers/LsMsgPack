﻿using LsMsgPack.Meta;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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
      typeof(List<>),
      typeof(object[]),
      typeof(Dictionary<,>),
      typeof(Guid)
    };

    public static byte[] Serialize<T>(T item, bool dynamicallyCompact = true)
    {
      return Serialize<T>(item, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static byte[] Serialize<T>(T item, MsgPackSettings settings)
    {
      if (NativelySupportedTypes.Contains(typeof(T))) return MsgPackItem.Pack(item, settings).ToBytes();
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
      if (NativelySupportedTypes.Contains(tType))
        return MsgPackItem.Pack(item, settings);
      PropertyInfo[] props;
      Dictionary<string, object> propVals;
      if (tType == typeof(MsgPackTypedItem)) // Add Type name as first entry with empty key
      {
        MsgPackTypedItem typedItem = (MsgPackTypedItem)item;
        props = GetSerializedProps(typedItem.Instance.GetType());
        propVals = new Dictionary<string, object>(props.Length + 1);

        object typeId = null;
        if (settings._addTypeName == AddTypeNameOption.AlwaysFullName || settings._addTypeName == AddTypeNameOption.IfAmbiguiousFullName)
          typeId = typedItem.Type.FullName;
        else if (settings._addTypeName == AddTypeNameOption.Always || settings._addTypeName == AddTypeNameOption.IfAmbiguious)
          typeId = typedItem.Type.Name;
        else
        {
          IMsgPackTypeIdentifier[] idGens = settings.TypeIdentifiers.ToArray();
          for (int t = idGens.Length - 1; t >= 0; t--)
          {
            typeId = idGens[t].IdForType(typedItem.Type);
            if (typeId != null)
              break;
          }
          if (typeId is null)
            typeId = typedItem.Type.Name;
        }
        propVals.Add(string.Empty, typeId);
        item = typedItem.Instance;
      }
      else
      {
        props = GetSerializedProps(tType);
        propVals = new Dictionary<string, object>(props.Length);
      }
      for (int t = props.Length - 1; t >= 0; t--)
      {
        object value = props[t].GetValue(item, null);
        if (settings._omitDefault && value == default)
          continue;
        if (value is null)
        {
          if (!settings._omitNull)
            propVals.Add(props[t].Name, value);
          continue;
        }
        Type valType = value.GetType();
        if (settings._addTypeName == AddTypeNameOption.Never || NativelySupportedTypes.Contains(valType))
          propVals.Add(props[t].Name, value);
        else if (settings._addTypeName == AddTypeNameOption.Always || settings._addTypeName == AddTypeNameOption.AlwaysFullName || settings._addTypeName == AddTypeNameOption.UseCustomIdAlways)
          propVals.Add(props[t].Name, new MsgPackTypedItem(value, valType));
        else // If Ambiguious
        {
          if (props[t].PropertyType == valType)
            propVals.Add(props[t].Name, value);
          else
            propVals.Add(props[t].Name, new MsgPackTypedItem(value, valType));
        }
      }
      return new MpMap(settings) { Value = propVals };
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
      if (NativelySupportedTypes.Contains(tType))
      {
        return MsgPackItem.Unpack(stream).GetTypedValue<T>();
      }
      MpMap map = (MpMap)MsgPackItem.Unpack(stream, settings);
      T result = (T)Materialize(tType, map);
      return result;
    }

    private static object Materialize(Type tType, MpMap map)
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
          tType = TypeResolver.Resolve(name, tType, map, propVals);
        }
        else if (tType.IsAbstract || tType.IsInterface)
        {
          tType = TypeResolver.Resolve(null, tType, map, propVals);
        }
      }
      PropertyInfo[] props = GetSerializedProps(tType);
      object result = Activator.CreateInstance(tType, true);

      for (int t = props.Length - 1; t >= 0; t--)
      {

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
              IList newInstance = (IList)Activator.CreateInstance(propType, true);

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
          if (propType == typeof(Guid))
          {
            props[t].SetValue(result, new Guid((byte[])val), null);
            continue;
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
        // This is going to be a drop-in replacement for xml, json, contract, binaryformatter etc..
        // we do not want dependencies on all supported types so we'll check for common names:
        //
        // System.Xml.Serialization.XmlIgnore,
        // System.Text.Json.Serialization.JsonIgnore,
        // Newtonsoft.Json.JsonIgnore
        // System.Runtime.Serialization.IgnoreDataMember
        //
        // Not sure, but you may squeeze out more performance by doing it the old way (knowing what attributes are in your source base)
        // if(atts[i] is XmlIgnoreAttribute) { ignore = true; break; }

        string attName = atts[i].GetType().Name;
        if (attName.IndexOf("Ignore", StringComparison.InvariantCultureIgnoreCase) >= 0) { ignore = true; break; }
      }
      return ignore;
    }

  }
}
