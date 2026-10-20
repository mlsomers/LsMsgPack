using LtMsgPack.Http;
using System.Reflection;

namespace LsMsgPackTypeScript
{
  /// <summary>
  /// How the client reads the values (the TypeScript types depend on it, the client checks that it reads with the same), and what the generated code imports.
  /// </summary>
  internal sealed class GeneratorOptions
  {
    internal static readonly string Version = typeof(GeneratorOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
      ?? typeof(GeneratorOptions).Assembly.GetName().Version?.ToString() ?? "0";

    /// <summary>
    /// long and ulong: 'number' or 'bigint' (the client's int64 option).
    /// </summary>
    internal string Int64 = "number";

    /// <summary>
    /// DateTime and DateTimeOffset: 'date' or 'iso' (the client's dates option).
    /// </summary>
    internal string Dates = "date";

    /// <summary>
    /// decimal: 'number' or 'string' (the client's decimal option).
    /// </summary>
    internal string Decimal = "number";

    /// <summary>
    /// The module the generated code imports defineModels from.
    /// </summary>
    internal string Import = "@lsmsgpack/client";

    /// <summary>
    /// The options of the server's formatters (the static filters, property order and type names decide the models and the schemas).
    /// </summary>
    internal LtMsgPackHttpOptions Http = new LtMsgPackHttpOptions();
  }
}
