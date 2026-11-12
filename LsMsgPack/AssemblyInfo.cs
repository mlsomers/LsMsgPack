using System.Runtime.CompilerServices;

// The data reader add-on (its own package, released with the same version) writes rows with the value handlers into the stream writer's buffer and reads them with the read plans
[assembly: InternalsVisibleTo("LsMsgPack.Data")]
