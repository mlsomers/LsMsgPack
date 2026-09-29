using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections.Generic;

namespace LsMsgPack
{
  /// <summary>
  /// The state of an indexed schema session (the schema and the properties resolved for it) that is kept by a <see cref="SchemaStore"/> for many calls.
  /// <para>Once published a session is frozen: it is only read, by any number of threads. A call that needs more (a new type, or a type that was not used before)
  /// gets a <see cref="SchemaGrowthException"/>, then repeats the work with a copy (<see cref="Thaw"/>) that is published in its place.
  /// Types and properties are only appended, so the ids used by earlier data stay valid.</para>
  /// </summary>
  internal sealed class SchemaSession
  {
    internal readonly IndexedSchemaTypeResolver Resolver;
    private readonly IMsgPackTypeResolver[] _typeResolvers;
    private readonly IMsgPackPropertyIdResolver[] _propertyIdResolvers;
    private readonly Dictionary<Type, FullPropertyInfo[]> _serializedProps;
    private readonly Dictionary<Type, FullPropertyInfo[]> _staticProps;

    /// <summary>
    /// Writing: the id of the schema (set when published).
    /// </summary>
    internal SchemaId Id;

    /// <summary>
    /// Writing: the bytes written in place of the schema (see <see cref="SchemaStore.WriteReference"/>).
    /// </summary>
    internal byte[] Reference;

    internal SchemaSession(IndexedSchemaTypeResolver resolver, MsgPackSettings settings)
      : this(resolver, settings, new Dictionary<Type, FullPropertyInfo[]>(), new Dictionary<Type, FullPropertyInfo[]>()) { }

    private SchemaSession(IndexedSchemaTypeResolver resolver, MsgPackSettings settings, Dictionary<Type, FullPropertyInfo[]> serializedProps, Dictionary<Type, FullPropertyInfo[]> staticProps)
    {
      Resolver = resolver;
      MsgPackSerializer.GetSchemaResolvers(settings, resolver, out _typeResolvers, out _propertyIdResolvers);
      _serializedProps = serializedProps;
      _staticProps = staticProps;
    }

    internal bool IsFrozen { get { return Resolver.IsFrozen; } }

    /// <summary>
    /// The settings for one call: a copy of the given settings using this session (like MsgPackSerializer.WithSchema, without resolving the types and properties again).
    /// </summary>
    /// <param name="settings">The settings of the call, their <see cref="SchemaStore.SessionKey"/> equals the one of the session</param>
    internal MsgPackSettings Apply(MsgPackSettings settings)
    {
      MsgPackSettings schemaSettings = settings.Clone();
      schemaSettings._typeResolvers = _typeResolvers;
      schemaSettings._propertyNameResolvers = _propertyIdResolvers;
      schemaSettings._serializedPropsCache = _serializedProps;
      schemaSettings._staticPropsCache = _staticProps;
      schemaSettings._schemaFrozen = IsFrozen;
      return schemaSettings;
    }

    /// <summary>
    /// A copy that can be extended. The resolved properties are shared: their ids are indexes into the schema, which only grows.
    /// </summary>
    internal SchemaSession Thaw(MsgPackSettings settings)
    {
      return new SchemaSession(Resolver.Copy(), settings, new Dictionary<Type, FullPropertyInfo[]>(_serializedProps), new Dictionary<Type, FullPropertyInfo[]>(_staticProps));
    }

    internal void Freeze()
    {
      Resolver.Freeze();
    }
  }
}
