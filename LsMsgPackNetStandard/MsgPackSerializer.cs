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
      return Serialize<T>(item, new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact });
    }

    public static byte[] Serialize<T>(T item, MsgPackSettings settings)
    {
      MemoryStream ms = new MemoryStream();
      Serialize(item, ms, settings);
      return ms.ToArray();
    }

    public static void Serialize<T>(T item, Stream target, bool dynamicallyCompact = true)
    {
      Serialize<T>(item, target, new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact });
    }

    public static void Serialize<T>(T item, Stream target, MsgPackSettings settings)
    {
      Serialize(item, typeof(T), target, settings);
    }

    /// <summary>
    /// Provided for generic flexibility, use the strongly typed Serialize&lt;T&gt; to benifit from compile-time type safety.
    /// </summary>
    /// <param name="item">The object to serialize</param>
    /// <param name="assignedTo">The declared type the item is assigned to, a type id is only added when the item's actual type differs (depending on <see cref="MsgPackSettings.AddTypeIdOptions"/>)</param>
    /// <param name="settings"><see cref="MsgPackSettings"/></param>
    /// <returns>Bytes containing MsgPack formatted data</returns>
    public static byte[] Serialize(object item, Type assignedTo, MsgPackSettings settings)
    {
      MemoryStream ms = new MemoryStream();
      Serialize(item, assignedTo, ms, settings);
      return ms.ToArray();
    }

    /// <summary>
    /// Provided for generic flexibility, use the strongly typed Serialize&lt;T&gt; to benifit from compile-time type safety.
    /// </summary>
    /// <param name="item">The object to serialize</param>
    /// <param name="assignedTo">The declared type the item is assigned to, a type id is only added when the item's actual type differs (depending on <see cref="MsgPackSettings.AddTypeIdOptions"/>)</param>
    /// <param name="target">Stream to write the MsgPack formatted data to</param>
    /// <param name="settings"><see cref="MsgPackSettings"/></param>
    public static void Serialize(object item, Type assignedTo, Stream target, MsgPackSettings settings)
    {
      if (settings is null)
        settings = new MsgPackSettings();

      if (assignedTo is null)
        assignedTo = item?.GetType() ?? typeof(object);

      if (settings.UseInexedSchema && !ReferenceEquals(item, null)) // null is serialized without a schema
      {
        SerializeWithSchema(item, assignedTo, target, settings);
        return;
      }

      MsgPackItem packed = SerializeObject(item, settings, new FullPropertyInfo(assignedTo));
      ByteWriter buffer = new ByteWriter();
      packed.WriteTo(buffer);
      buffer.CopyTo(target);
    }

    public static MsgPackItem SerializeObject(object item, bool dynamicallyCompact = true)
    {
      return SerializeObject(item, new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact });
    }

    private static void SerializeWithSchema(object item, Type assignedTo, Stream target, MsgPackSettings settings)
    {
      IndexedSchemaTypeResolver resolver = new IndexedSchemaTypeResolver();
      MsgPackSettings schemaSettings = WithSchema(settings, resolver);

      MsgPackItem packed = SerializeObject(item, schemaSettings, new FullPropertyInfo(assignedTo));
      ByteWriter buffer = new ByteWriter();
      packed.WriteTo(buffer); // Fills the schema, so this needs to be done before packing the schema

      byte[] schema = resolver.Pack(settings);
      target.Write(schema, 0, schema.Length);
      buffer.CopyTo(target);
    }

    /// <summary>
    /// Returns a copy of the settings using the given schema, the original settings are not modified so they can safely be shared between threads.
    /// </summary>
    private static MsgPackSettings WithSchema(MsgPackSettings settings, IndexedSchemaTypeResolver resolver)
    {
      MsgPackSettings schemaSettings = settings.Clone();

      // Resolvers are consulted from last to first, the schema should be consulted first so its type id's cannot be mistaken for those of another resolver.
      List<IMsgPackTypeResolver> resolvers = new List<IMsgPackTypeResolver>(settings._typeResolvers.Length + 1);
      for (int t = 0; t < settings._typeResolvers.Length; t++)
        if (!(settings._typeResolvers[t] is IndexedSchemaTypeResolver))
          resolvers.Add(settings._typeResolvers[t]);
      resolvers.Add(resolver);
      schemaSettings._typeResolvers = resolvers.ToArray();

      List<IMsgPackPropertyIdResolver> propNameResolvers = new List<IMsgPackPropertyIdResolver>(settings._propertyNameResolvers.Length + 1) { resolver };
      for (int t = 0; t < settings._propertyNameResolvers.Length; t++)
        if (!(settings._propertyNameResolvers[t] is IndexedSchemaTypeResolver))
          propNameResolvers.Add(settings._propertyNameResolvers[t]);
      schemaSettings._propertyNameResolvers = propNameResolvers.ToArray();

      // These settings (and resolver) are only used for this session, so the property ids can be cached for all instances of the same type
      schemaSettings._serializedPropsCache = new Dictionary<Type, FullPropertyInfo[]>();

      return schemaSettings;
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
      object result = Deserialize(typeof(T), stream, settings);
      if (result is null)
        return default;
      return (T)result;
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
      if (settings is null)
        settings = new MsgPackSettings();

      if (settings.UseInexedSchema)
        return DeserializeWithSchema(tType, stream, settings);

      MsgPackItem unpacked = MsgPackItem.Unpack(stream, settings);
      return ConvertDeserializeValue(unpacked.Value, tType, settings, null);
    }

    private static object DeserializeWithSchema(Type tType, Stream stream, MsgPackSettings settings)
    {
      CacheAssemblyTypes(tType);

      IndexedSchemaTypeResolver resolver = IndexedSchemaTypeResolver.Unpack(stream, settings);
      if (resolver is null) // null is serialized without a schema
        return null;

      MsgPackSettings schemaSettings = WithSchema(settings, resolver);
      try
      {
        MsgPackItem unpacked = MsgPackItem.Unpack(stream, schemaSettings);
        return ConvertDeserializeValue(unpacked.Value, tType, schemaSettings, null);
      }
      finally
      {
        settings.FileContainsErrors |= schemaSettings.FileContainsErrors;
      }
    }

  }
}
