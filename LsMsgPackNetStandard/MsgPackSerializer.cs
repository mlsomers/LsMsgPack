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
        if (settings._writeSchemaReference)
          SerializeWithSchemaReference(item, assignedTo, target, settings);
        else
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
    /// The schema is kept in the <see cref="MsgPackSettings.SchemaStore"/> (per root type) and only referred to by its id (see <see cref="SchemaStore"/>).
    /// <para>The session of the root type is shared by all calls. When a call needs something the session does not have yet, it is repeated with a grown copy that replaces the session (a new schema id).</para>
    /// </summary>
    private static void SerializeWithSchemaReference(object item, Type assignedTo, Stream target, MsgPackSettings settings)
    {
      SchemaStore store = settings._schemaStore;
      if (store is null)
        throw new MsgPackException($"{nameof(MsgPackSettings)}.{nameof(MsgPackSettings.WriteSchemaReference)} needs a {nameof(MsgPackSettings)}.{nameof(MsgPackSettings.SchemaStore)} to keep the schema in.");

      FullPropertyInfo root = new FullPropertyInfo(assignedTo);
      SchemaSession session;
      MsgPackItem packed = store.RunWriter(assignedTo, settings, (s, schemaSettings) => SerializeObject(item, schemaSettings, root), out session);

      ByteWriter buffer = new ByteWriter();
      buffer.Write(session.Reference);
      packed.WriteTo(buffer);
      buffer.CopyTo(target);
    }


    /// <summary>
    /// Returns a copy of the settings using the given schema, the original settings are not modified so they can safely be shared between threads.
    /// </summary>
    private static MsgPackSettings WithSchema(MsgPackSettings settings, IndexedSchemaTypeResolver resolver)
    {
      MsgPackSettings schemaSettings = settings.Clone();
      SchemaSession.GetSchemaResolvers(settings, resolver, out schemaSettings._typeResolvers, out schemaSettings._propertyNameResolvers);

      // These settings (and resolver) are only used for this session, so the property ids can be cached for all instances of the same type
      schemaSettings._serializedPropsCache = new Dictionary<Type, FullPropertyInfo[]>();
      schemaSettings._staticPropsCache = new Dictionary<Type, FullPropertyInfo[]>();

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
      return ConvertDeserializeValue(unpacked.UnpackedValue, tType, settings, null);
    }

    private static object DeserializeWithSchema(Type tType, Stream stream, MsgPackSettings settings)
    {
      CacheAssemblyTypes(tType);

      int first = stream.ReadByte();
      if (first < 0)
        throw new MsgPackException("Unexpected end of data.", 0, MsgPackTypeId.NeverUsed);
      if (first == (int)MsgPackTypeId.MpNull) // null is serialized without a schema
        return null;

      SchemaStore store = settings._schemaStore;
      if (first == (int)MsgPackTypeId.MpFExt16)
      {
        SchemaId id = ReadSchemaReference(stream, first);
        if (store is null)
          throw new MsgPackException($"The data refers to the cached schema {id}, reading it needs a {nameof(MsgPackSettings)}.{nameof(MsgPackSettings.SchemaStore)} that holds the schema.");
        return DeserializeWithStoredSchema(tType, stream, settings, store.GetById(id), null);
      }

      if (!SchemaBytes.IsMap(first))
        throw SchemaBytes.NotASchema(first);

      byte[] raw = SchemaBytes.ReadRaw(stream, first, settings);
      SchemaStore.Entry cached = store?.GetInline(raw);
      if (cached != null)
        return DeserializeWithStoredSchema(tType, stream, settings, cached, settings);

      IndexedSchemaTypeResolver resolver = IndexedSchemaTypeResolver.FromBytes(raw, settings, settings);

      MsgPackSettings schemaSettings = WithSchema(settings, resolver);
      try
      {
        MsgPackItem unpacked = MsgPackItem.Unpack(stream, schemaSettings);
        return ConvertDeserializeValue(unpacked.UnpackedValue, tType, schemaSettings, null);
      }
      finally
      {
        settings.FileContainsErrors |= schemaSettings.FileContainsErrors;
      }
    }

    /// <exception cref="MsgPackException">When the extension is not a schema reference</exception>
    private static SchemaId ReadSchemaReference(Stream stream, int first)
    {
      byte[] reference = new byte[SchemaStore.ReferenceLength];
      reference[0] = (byte)first;
      int offset = 1;
      while (offset < reference.Length) // Stream.Read may return fewer bytes than asked (network streams)
      {
        int read = stream.Read(reference, offset, reference.Length - offset);
        if (read <= 0)
          throw new MsgPackException("Unexpected end of data while reading the schema reference.", 0, MsgPackTypeId.MpFExt16);
        offset += read;
      }

      if (unchecked((sbyte)reference[1]) != SchemaStore.ReferenceExtensionType)
        throw new MsgPackException($"Expected the data to start with an indexed schema (a map) or a reference to one (extension type {SchemaStore.ReferenceExtensionType}) but found extension type {unchecked((sbyte)reference[1])}. Was it serialized with {nameof(MsgPackSettings)}.{nameof(MsgPackSettings.UseInexedSchema)} = false?", 0, MsgPackTypeId.MpFExt16);

      return new SchemaId(reference, 2);
    }

    /// <summary>
    /// Reads the body with the session of a schema kept in the <see cref="SchemaStore"/>, shared by all calls that read the same schema (with settings of the same <see cref="SchemaStore.SessionKey"/>).
    /// <para>When the conversion needs something the session does not have yet (e.g. a type that was not read before), it is repeated with a grown copy that replaces the session.</para>
    /// </summary>
    /// <param name="lengthSettings">The byte order the schema bytes were read with, null for the one of the specification (see <see cref="IndexedSchemaTypeResolver.FromBytes"/>)</param>
    private static object DeserializeWithStoredSchema(Type tType, Stream stream, MsgPackSettings settings, SchemaStore.Entry schema, MsgPackSettings lengthSettings)
    {
      MsgPackItem unpacked = MsgPackItem.Unpack(stream, settings);
      object value = unpacked.UnpackedValue;

      return schema.RunReader(settings, lengthSettings, (s, schemaSettings) => ConvertDeserializeValue(value, tType, schemaSettings, null));
    }
  }
}
