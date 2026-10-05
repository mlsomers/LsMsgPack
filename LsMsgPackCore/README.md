# LsMsgPack.Core

The parts shared by the [LtMsgPack](https://www.nuget.org/packages/LtMsgPack) and [LsMsgPack](https://www.nuget.org/packages/LsMsgPack) MsgPack serializers. You don't need to add it yourself: both serializers depend on it.

Its types are in the `LsMsgPack.*` namespaces, so they work the same with either serializer:

- `MsgPackOptions`: the format settings (`UseInexedSchema`, `ObjectLayout`, `AddTypeIdOptions`, `PropertyOrder`, `TypeGuard`...), the base class of `LtMsgPackOptions` and `MsgPackSettings`.
- Type resolvers (`IMsgPackTypeResolver`, `XmlRootAttributeTypeResolver`) and property id resolvers (`IMsgPackPropertyIdResolver`, `AttributePropertyNameResolver`): choose the ids written for types and properties.
- Filters (`FilterDefaultValues`, `FilterNullValues`, `FilterIgnoredAttribute`...): decide which properties are written.
- Type guards (`IMsgPackTypeGuard`, `AllowedTypesGuard`): limit the types the data may create.
- `SchemaStore`: keeps indexed schemas between calls, so messages can refer to a schema instead of carrying it.
- `MsgPackMediaTypes`: the MsgPack media types (`application/msgpack`, `application/x-msgpack`, `application/x-lsmsgpack`).

## Documentation

- [Wire formats, the indexed schema and type resolvers](https://github.com/mlsomers/LsMsgPack/blob/master/docs/schema.md)
- [Security](https://github.com/mlsomers/LsMsgPack/blob/master/docs/security.md): type guards
- Source and issues: [github.com/mlsomers/LsMsgPack](https://github.com/mlsomers/LsMsgPack)
