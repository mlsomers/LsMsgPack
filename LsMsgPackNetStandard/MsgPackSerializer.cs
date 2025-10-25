using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LsMsgPack
{
  /// <summary>
  /// The main entry point, serialize and deserialize objects from here
  /// </summary>
  public static class MsgPackSerializer
  {
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
      if (ReferenceEquals(item, null))
        return new MpNull().ToBytes();

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
      MsgPackItem packed = SerializeObject(item, settings, new FullPropertyInfo(typeof(T)));
      byte[] buffer = packed.ToBytes();
      target.Write(buffer, 0, buffer.Length);
      return;
    }

    public static MsgPackItem SerializeObject(object item, bool dynamicallyCompact = true)
    {
      return SerializeObject(item, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static MsgPackItem SerializeObject(object item, MsgPackSettings settings, FullPropertyInfo assignedTo = null)
    {
      if (ReferenceEquals(item, null))
        return new MpNull();

      Type tType = item.GetType();
      Type nullableType = Nullable.GetUnderlyingType(tType);
      if (!(nullableType is null))
        tType = nullableType;

      MsgPackItem packed = MsgPackItem.Pack(item, settings, tType);
      if (packed != null)
      {
        if (assignedTo?.AssignedToType is null
          || settings.AddTypeIdOptions == AddTypeIdOption.Never
          || tType.IsPrimitive
          || tType == typeof(string)
          || (settings.AddTypeIdOptions.HasFlag(AddTypeIdOption.IfAmbiguious) && assignedTo?.AssignedToType == tType))
          return packed;
      }

      FullPropertyInfo[] props;
      Dictionary<object, object> propVals;
      SerializeEnumerableAttribute handleItems = null;

      if (packed is null && item is IEnumerable) // typeof(IEnumerable).IsAssignableFrom(tType))
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
            //else if (types.Length == 2)
            //{
            //  handleItems.ElementType = typeof(KeyValuePair<,>).MakeGenericType(types);
            //}
            else
              throw new NotImplementedException(string.Concat("Todo: check if we can derive element type from IEnumerable<T>.",
                "For now decorate/annotate your fancy collection (", tType.Name, assignedTo is null ? "" : ") or property (" + assignedTo.PropertyInfo.Name, ") with a [SerializeEnumerable] Attribute specifying the type of the elements and weather to include or exclude other properties..."));
          }
        }
      }

      // Any complex object with properties
      props = GetSerializedProps(tType, settings);
      propVals = new Dictionary<object, object>(props.Length);
      bool addTypeId = false;

      if (settings.AddTypeIdOptions != AddTypeIdOption.Never)
      {
        if ((settings.AddTypeIdOptions.HasFlag(AddTypeIdOption.IfAmbiguious) && assignedTo?.AssignedToType != tType)
          || settings.AddTypeIdOptions.HasFlag(AddTypeIdOption.Always))
        {
          object typeIdd = GetTypeIdentifier(tType, settings, assignedTo);
          propVals.Add(string.Empty, typeIdd);
          addTypeId = true;
        }
      }

      if (packed != null)
      {
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

      for (int t = settings.TypeResolvers.Length - 1; t >= 0; t--)
      {
        typeId = settings.TypeResolvers[t].IdForType(type, propertyInfo, settings);
        if (typeId != null)
          break;
      }
      if (typeId is null && !((settings._addTypeName & AddTypeIdOption.NoDefaultFallBack) > 0))
      {
        bool fullname = (settings._addTypeName & AddTypeIdOption.FullName) > 0;
        typeId = GetTypeName(type, fullname);
      }

      return typeId;
    }

    internal static string GetTypeName(Type type, bool fullname)
    {
      Type[] args = type.GenericTypeArguments;

      if (args.Length == 0)
        return fullname ? type.FullName : type.Name;

      // Get the generic type name...

      string[] names = new string[args.Length];
      for (int t = args.Length - 1; t >= 0; t--)
        names[t] = GetTypeName(args[t], fullname);

      string typeName = fullname ? type.FullName : type.Name;
      typeName = typeName.Substring(0, typeName.IndexOf('`'));

      return string.Concat(typeName, '<', string.Join(", ", names), '>');
    }


    public static byte[] SerializeWithSchema<T>(T item, bool dynamicallyCompact = true)
    {
      return SerializeWithSchema<T>(item, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static byte[] SerializeWithSchema<T>(T item, MsgPackSettings settings)
    {
      if (ReferenceEquals(item, null))
        return new MpNull().ToBytes();

      MemoryStream ms = new MemoryStream();
      SerializeWithSchema(item, ms, settings);
      return ms.ToArray();
    }

    public static void SerializeWithSchema<T>(T item, Stream target, bool dynamicallyCompact = true)
    {
      SerializeWithSchema<T>(item, target, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static void SerializeWithSchema<T>(T item, Stream target, MsgPackSettings settings)
    {

      if (settings == null)
        settings = new MsgPackSettings();

      IndexedSchemaTypeResolver resolver = new IndexedSchemaTypeResolver();

      InjectSchema(settings, resolver);

      MemoryStream ms = new MemoryStream();
      Serialize(item, ms, settings);

      MsgPackSettings schemaSerializationSettings = new MsgPackSettings() { AddTypeIdOptions = AddTypeIdOption.Never };
      schemaSerializationSettings.PropertyNameResolvers = new[]{ new AttributePropertyNameResolver() };
      Serialize(resolver, target, schemaSerializationSettings);
      ms.Seek(0, 0);
      ms.CopyTo(target);

      RemoveSchemaResolver(settings);

      return;
    }

    private static void InjectSchema(MsgPackSettings settings, IndexedSchemaTypeResolver resolver)
    {
      List<IMsgPackTypeResolver> resolvers = new List<IMsgPackTypeResolver>(settings.TypeResolvers);
      resolvers.Insert(0, resolver);
      settings.TypeResolvers = resolvers.ToArray();

      List<IMsgPackPropertyIdResolver> propNameResolvers = new List<IMsgPackPropertyIdResolver>(settings.PropertyNameResolvers);
      propNameResolvers.Insert(0, resolver);
      settings.PropertyNameResolvers = propNameResolvers.ToArray();
    }

    private static void RemoveSchemaResolver(MsgPackSettings settings)
    {
      List<IMsgPackTypeResolver> resolvers = new List<IMsgPackTypeResolver>(settings.TypeResolvers);
      for (int t = resolvers.Count - 1; t >= 0; t--)
        if (resolvers[t] is IndexedSchemaTypeResolver)
          resolvers.RemoveAt(t);
      settings.TypeResolvers = resolvers.ToArray();

      List<IMsgPackPropertyIdResolver> propNameResolvers = new List<IMsgPackPropertyIdResolver>(settings.PropertyNameResolvers);
      for (int t = propNameResolvers.Count - 1; t >= 0; t--)
        if (propNameResolvers[t] is IndexedSchemaTypeResolver)
          propNameResolvers.RemoveAt(t);
      settings.PropertyNameResolvers = propNameResolvers.ToArray();
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
      MsgPackItem unpacked = MsgPackItem.Unpack(stream, settings);
      if (unpacked.Value is T)
        return (T)unpacked.Value;

      if (unpacked is MpMap)
      {
        MpMap map = (MpMap)unpacked;

        T result = (T)Materialize(typeof(T), map);
        return result;
      }

      T resultt = (T)ConvertDeserializeValue(unpacked.Value, typeof(T), new MpMap(settings), new FullPropertyInfo(typeof(T)));
      return resultt;
    }

    public static T DeserializeWithSchema<T>(byte[] source)
    {
      return DeserializeWithSchema<T>(source, new MsgPackSettings());
    }

    public static T DeserializeWithSchema<T>(byte[] source, MsgPackSettings settings)
    {
      using (MemoryStream ms = new MemoryStream(source))
      {
        return DeserializeWithSchema<T>(ms, settings);
      }
    }

    public static T DeserializeWithSchema<T>(Stream stream)
    {
      return DeserializeWithSchema<T>(stream, new MsgPackSettings());
    }

    public static T DeserializeWithSchema<T>(Stream stream, MsgPackSettings settings)
    {
      RemoveSchemaResolver(settings);

      IndexedSchemaTypeResolver resolver = null;

      MsgPackSettings schemaSerializationSettings = new MsgPackSettings() { AddTypeIdOptions = AddTypeIdOption.Never };
      schemaSerializationSettings.PropertyNameResolvers = new[] { new AttributePropertyNameResolver() };

      MsgPackItem unpacked = MsgPackItem.Unpack(stream, schemaSerializationSettings);
      if (unpacked.Value is IndexedSchemaTypeResolver)
        resolver = (IndexedSchemaTypeResolver)unpacked.Value;
      else if (unpacked is MpMap)
      {
        MpMap map = (MpMap)unpacked;

        resolver = (IndexedSchemaTypeResolver)Materialize(typeof(IndexedSchemaTypeResolver), map);
      }

      resolver.ResolveDeserializedTypes(settings);
      InjectSchema(settings, resolver);
      
      unpacked = MsgPackItem.Unpack(stream, settings);
      if (unpacked.Value is T)
        return (T)unpacked.Value;

      if (unpacked is MpMap)
      {
        MpMap map = (MpMap)unpacked;

        T result = (T)Materialize(typeof(T), map);
        return result;
      }

      T resultt = (T)ConvertDeserializeValue(unpacked.Value, typeof(T), new MpMap(settings), new FullPropertyInfo(typeof(T)));
      return resultt;
    }

    /// <summary>
    /// Provided for generic flexibility, use the strongly typed Deserialize&lt;T&gt; to benifit from compile-time type safety.
    /// </summary>
    /// <param name="tType">Type of the object to be deserialized</param>
    /// <param name="source">Bytes containing MsgPack formatted data</param>
    /// <returns>The deserialized object</returns>
    public static object Deserialize(Type tType, byte[] source)
    {
      return Deserialize(tType, source, new MsgPackSettings());
    }

    /// <summary>
    /// Provided for generic flexibility, use the strongly typed Deserialize&lt;T&gt; to benifit from compile-time type safety.
    /// </summary>
    /// <param name="tType">Type of the object to be deserialized</param>
    /// <param name="source">Bytes containing MsgPack formatted data</param>
    /// <param name="settings"><see cref="MsgPackSettings"/></param>
    /// <returns>The deserialized object</returns>
    public static object Deserialize(Type tType, byte[] source, MsgPackSettings settings)
    {
      using (MemoryStream ms = new MemoryStream(source))
      {
        return Deserialize(tType, ms, settings);
      }
    }

    /// <summary>
    /// Provided for generic flexibility, use the strongly typed Deserialize&lt;T&gt; to benifit from compile-time type safety.
    /// </summary>
    /// <param name="tType">Type of the object to be deserialized</param>
    /// <param name="stream">Stream of bytes containing MsgPack formatted data</param>
    /// <returns>The deserialized object</returns>
    public static object Deserialize(Type tType, Stream stream)
    {
      return Deserialize(tType, stream, new MsgPackSettings());
    }

    /// <summary>
    /// Provided for generic flexibility, use the strongly typed Deserialize&lt;T&gt; to benifit from compile-time type safety.
    /// </summary>
    /// <param name="tType">Type of the object to be deserialized</param>
    /// <param name="stream">Stream of bytes containing MsgPack formatted data</param>
    /// <param name="settings"><see cref="MsgPackSettings"/></param>
    /// <returns>The deserialized object</returns>
    public static object Deserialize(Type tType, Stream stream, MsgPackSettings settings)
    {
      MsgPackItem unpacked = MsgPackItem.Unpack(stream, settings);
      if (unpacked.Value.GetType() == tType)
        return unpacked.Value;

      if (unpacked is MpMap)
      {
        MpMap map = (MpMap)unpacked;
        return Materialize(tType, map);
      }
      return ConvertDeserializeValue(unpacked.Value, tType, new MpMap(settings), new FullPropertyInfo(tType));
    }

    private static object Materialize(Type tType, MpMap map, FullPropertyInfo rootProp = null)
    {
      Dictionary<object, object> propVals = new Dictionary<object,object>(map.Count, new MapConversionEqualityComparer());
      map.FillDictionary(propVals);

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
          Array itemArr = (Array)items;
          Array typedArr = Array.CreateInstance(tType.GenericTypeArguments[0], itemArr.Length);

          //object[] kvs = itemArr.Cast<object[]>().ToArray();
          for (int t = itemArr.Length - 1; t >= 0; t--)
          {
            //typedArr.SetValue(Materialize(tType.GenericTypeArguments[0], itemArr[t], rootProp), t);
            typedArr.SetValue(itemArr.GetValue(t), t);
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
              value = itemArr[t].Key;

            object entry = Activator.CreateInstance(itemType, key, value);
            typedArr.SetValue(entry, t);
          }
          result = Activator.CreateInstance(tType, typedArr);
        }
        else if (tType.IsArray)
        {
          result = items;
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

        object propval=null;

        //if (propVals.TryGetValue(prp.Name, out propval))
        if (propVals.TryGetValue(prop.PropertyId, out propval))
        {
          Type propType = prp.PropertyType;

          object ConvertedVal;
          if (propval is MpMap)
            ConvertedVal = Materialize(propType, (MpMap)propval, prop);
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
      if (MsgPackMeta.NumericTypes.Contains(assignType)) { 
        return Convert.ChangeType(val, assignType);
      }
      return val;
    }


    internal static FullPropertyInfo[] GetSerializedProps(Type type, MsgPackSettings settings)
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
