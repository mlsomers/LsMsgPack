using System.Runtime.CompilerServices;

// The serializers use the settings fields, the session caches and the schema internals directly
[assembly: InternalsVisibleTo("LsMsgPack")]
[assembly: InternalsVisibleTo("LtMsgPack")]
[assembly: InternalsVisibleTo("LsMsgPackUnitTests")]
[assembly: InternalsVisibleTo("LtMsgPack.Data")] // the data reader add-on: the schemas of columns (DescribedSchema), conversions, the properties of the mapper
[assembly: InternalsVisibleTo("LsMsgPackTs")] // the TypeScript generator writes the schemas the server writes (SchemaTypes, DescribedSchema)
