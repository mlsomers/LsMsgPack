using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LsMsgPack
{
  /// <summary>
  /// The main entry point, serialize and deserialize objects from here
  /// </summary>
  public static partial class MsgPackSerializer
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
      MsgPackSettings settings = new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact };

      if (settings.UseInexedSchema)
        return SerializeWithSchema(item, settings);

      return Serialize<T>(item, settings);
    }

    public static byte[] Serialize<T>(T item, MsgPackSettings settings)
    {
      if (ReferenceEquals(item, null))
        return new MpNull().ToBytes();

      if (settings != null && settings.UseInexedSchema)
        return SerializeWithSchema(item, settings);

      MemoryStream ms = new MemoryStream();
      Serialize(item, ms, settings);
      return ms.ToArray();
    }

    public static void Serialize<T>(T item, Stream target, bool dynamicallyCompact = true)
    {
      MsgPackSettings settings = new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact };

      if (settings.UseInexedSchema)
      {
        SerializeWithSchema(item, target, settings);
        return;
      }

      Serialize<T>(item, target, settings);
    }

    public static void Serialize<T>(T item, Stream target, MsgPackSettings settings)
    {
      if (settings != null && settings.UseInexedSchema)
      {
        SerializeWithSchema(item, target, settings);
        return;
      }

      MsgPackItem packed = SerializeObject(item, settings, new FullPropertyInfo(typeof(T)));
      byte[] buffer = packed.ToBytes();
      target.Write(buffer, 0, buffer.Length);
      return;
    }

    public static MsgPackItem SerializeObject(object item, bool dynamicallyCompact = true)
    {
      return SerializeObject(item, new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact });
    }

    private static byte[] SerializeWithSchema<T>(T item, MsgPackSettings settings)
    {
      if (ReferenceEquals(item, null))
        return new MpNull().ToBytes();

      MemoryStream ms = new MemoryStream();
      SerializeWithSchema(item, ms, settings);
      return ms.ToArray();
    }

    private static void SerializeWithSchema<T>(T item, Stream target, MsgPackSettings settings)
    {

      if (settings == null)
        settings = new MsgPackSettings();

      RemoveSchemaResolver(settings);

      IndexedSchemaTypeResolver resolver = new IndexedSchemaTypeResolver();

      InjectSchema(settings, resolver);

      MsgPackItem packed = SerializeObject(item, settings, new FullPropertyInfo(typeof(T)));
      byte[] buffer = packed.ToBytes();
      

      byte[] schema = resolver.Pack();
      target.Write(schema, 0, schema.Length);
      target.Write(buffer, 0, buffer.Length);

      RemoveSchemaResolver(settings);

      return;
    }

    private static void InjectSchema(MsgPackSettings settings, IndexedSchemaTypeResolver resolver)
    {
      List<IMsgPackTypeResolver> resolvers = new List<IMsgPackTypeResolver>(settings._typeResolvers);
      resolvers.Insert(0, resolver);
      settings._typeResolvers = resolvers.ToArray();

      List<IMsgPackPropertyIdResolver> propNameResolvers = new List<IMsgPackPropertyIdResolver>(settings._propertyNameResolvers);
      propNameResolvers.Insert(0, resolver);
      settings._propertyNameResolvers = propNameResolvers.ToArray();
    }

    private static void RemoveSchemaResolver(MsgPackSettings settings)
    {
      List<IMsgPackTypeResolver> resolvers = new List<IMsgPackTypeResolver>(settings._typeResolvers);
      for (int t = resolvers.Count - 1; t >= 0; t--)
        if (resolvers[t] is IndexedSchemaTypeResolver)
          resolvers.RemoveAt(t);
      settings._typeResolvers = resolvers.ToArray();

      List<IMsgPackPropertyIdResolver> propNameResolvers = new List<IMsgPackPropertyIdResolver>(settings._propertyNameResolvers);
      for (int t = propNameResolvers.Count - 1; t >= 0; t--)
        if (propNameResolvers[t] is IndexedSchemaTypeResolver)
          propNameResolvers.RemoveAt(t);
      settings._propertyNameResolvers = propNameResolvers.ToArray();
    }

    public static T Deserialize<T>(byte[] source)
    {
      MsgPackSettings settings = new MsgPackSettings();
      if (settings.UseInexedSchema)
        return DeserializeWithSchema<T>(source, settings);

      return Deserialize<T>(source, new MsgPackSettings());
    }

    public static T Deserialize<T>(byte[] source, MsgPackSettings settings)
    {
      if(settings.UseInexedSchema)
        return DeserializeWithSchema<T>(source, settings);

      using (MemoryStream ms = new MemoryStream(source))
      {
        return Deserialize<T>(ms, settings);
      }
    }

    public static T Deserialize<T>(Stream stream)
    {
      MsgPackSettings settings = new MsgPackSettings();
      if (settings.UseInexedSchema)
        return DeserializeWithSchema<T>(stream, settings);

      return Deserialize<T>(stream, new MsgPackSettings());
    }

    public static T Deserialize<T>(Stream stream, MsgPackSettings settings)
    {
      if (settings.UseInexedSchema)
        return DeserializeWithSchema<T>(stream, settings);

      MsgPackItem unpacked = MsgPackItem.Unpack(stream, settings);
      return ConvertRoot<T>(unpacked, settings);
    }

    private static T DeserializeWithSchema<T>(byte[] source, MsgPackSettings settings)
    {
      using (MemoryStream ms = new MemoryStream(source))
      {
        return DeserializeWithSchema<T>(ms, settings);
      }
    }

    private static T DeserializeWithSchema<T>(Stream stream, MsgPackSettings settings)
    {
      RemoveSchemaResolver(settings);
      MsgPackSerializer.CacheAssemblyTypes(typeof(T));

      IndexedSchemaTypeResolver resolver = IndexedSchemaTypeResolver.Unpack(stream, settings);
      if (resolver is null) // null is serialized without a schema
        return default;

      InjectSchema(settings, resolver);

      try
      {
        MsgPackItem unpacked = MsgPackItem.Unpack(stream, settings);
        return ConvertRoot<T>(unpacked, settings);
      }
      finally
      {
        RemoveSchemaResolver(settings);
      }
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
      return ConvertDeserializeValue(unpacked.Value, tType, settings, null);
    }

    private static T ConvertRoot<T>(MsgPackItem unpacked, MsgPackSettings settings)
    {
      object result = ConvertDeserializeValue(unpacked.Value, typeof(T), settings, null);
      if (result is null)
        return default;
      return (T)result;
    }

  }
}
