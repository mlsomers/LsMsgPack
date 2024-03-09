using LsMsgPack.TypeResolving;
using LsMsgPack.TypeResolving.Interfaces;
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
      if (tType.IsArray)
        tType = tType.GetElementType();

      if (NativelySupportedTypes.Contains(tType))
        return MsgPackItem.Pack(item, settings);

      FullPropertyInfo[] props;
      Dictionary<object, object> propVals;
      if (tType == typeof(MsgPackTypedItem)) // Add Type name as first entry with empty key
      {
        MsgPackTypedItem typedItem = (MsgPackTypedItem)item;
        if (typedItem.AssignmentType.IsArray)
        {
          Type arrType = typedItem.AssignmentType.GetElementType();
          object[] orgObjects = (object[])typedItem.Instance;
          object[] objects = new object[orgObjects.Length];
          for (int t = objects.Length - 1; t >= 0; t--)
            objects[t]= GetTypedOrUntyped(settings, arrType, orgObjects[t], typedItem.PropertyInfo);
          return new MpArray(settings) { Value = objects };
        }
        props = GetSerializedProps(typedItem.Type, settings);
        propVals = new Dictionary<object, object>(props.Length + 1);

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
            typeId = idGens[t].IdForType(typedItem.Type, typedItem.PropertyInfo);
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
        props = GetSerializedProps(tType, settings);
        propVals = new Dictionary<object, object>(props.Length);
      }

      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo prop = props[t];
        PropertyInfo prp = prop.PropertyInfo;
        object value = prp.GetValue(item, null);

        bool exclude = false;
        foreach (IMsgPackPropertyIncludeDynamically filter in settings.DynamicFilters)
          if (!filter.IncludeProperty(prop, value)) { exclude = true; break; }
        if (exclude)
          continue;

        if (value is null)
        {
          propVals.Add(prop.PropertyId, value); // TODO: prop name instead... Add IMsgPackPropertyIdResolver stuff to FullPropertyInfo....
          continue;
        }
        propVals.Add(prop.PropertyId, GetTypedOrUntyped(settings, prp.PropertyType, value, prop));
      }
      return new MpMap(settings) { Value = propVals };
    }

    private static object GetTypedOrUntyped(MsgPackSettings settings, Type assignementType, object value, FullPropertyInfo prp)
    {
      Type valType = value.GetType();
      if (settings._addTypeName == AddTypeNameOption.Never || NativelySupportedTypes.Contains(valType))
        return value;
      else if (settings._addTypeName == AddTypeNameOption.Always || settings._addTypeName == AddTypeNameOption.AlwaysFullName || settings._addTypeName == AddTypeNameOption.UseCustomIdAlways)
        return new MsgPackTypedItem(value, valType, assignementType, prp);
      else // If Ambiguious
      {
        Type prpType;
        if (assignementType.IsArray)
          prpType = assignementType.GetElementType();
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
      if (NativelySupportedTypes.Contains(tType))
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
          tType = TypeResolver.Resolve(name, tType, rootProp, map, propVals);
        }
        else if (tType.IsAbstract || tType.IsInterface)
        {
          tType = TypeResolver.Resolve(null, tType, rootProp, map, propVals);
        }
      }
      FullPropertyInfo[] props = GetSerializedProps(tType, map.Settings);
      object result = Activator.CreateInstance(tType, true);

      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo prop = props[t];
        PropertyInfo prp = prop.PropertyInfo;
        if (propVals.TryGetValue(prp.Name, out val))
        {
          Type propType = prp.PropertyType;
          if (!ReferenceEquals(val, null))
          {
            if (!MsgPackSerializer.NativelySupportedTypes.Contains(propType)
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
              bool complexTypes = !MsgPackSerializer.NativelySupportedTypes.Contains(propType);
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
          foreach (IMsgPackPropertyIncludeStatically item in settings.StaticFilters)
            if (!item.IncludeProperty(full)) { keep = false; break; }

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
