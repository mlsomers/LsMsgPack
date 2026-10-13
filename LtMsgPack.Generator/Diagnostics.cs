using Microsoft.CodeAnalysis;

namespace System.Runtime.CompilerServices
{
  /// <summary>
  /// For the init accessors of records (netstandard2.0 does not have it).
  /// </summary>
  internal static class IsExternalInit
  {
  }
}

namespace LtMsgPack.Generator
{
  internal static class Diagnostics
  {
    private const string Category = "LtMsgPack.Generator";

    public static readonly DiagnosticDescriptor NotPartial = new DiagnosticDescriptor("LTMP001", "Context class is not partial",
      "The context class '{0}' must be partial (and the classes it is nested in) so the generator can add its code", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NotAContext = new DiagnosticDescriptor("LTMP002", "Context class does not derive from LtMsgPackContext",
      "The class '{0}' has [LtMsgPackSerializable] but does not derive from LtMsgPack.LtMsgPackContext", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NotAccessible = new DiagnosticDescriptor("LTMP003", "Type is not accessible to the generated code",
      "The type '{0}' is not accessible to the code generated for '{1}' (private or protected), no code is generated for it: it is serialized as without the generator", Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor PropertyByReflection = new DiagnosticDescriptor("LTMP004", "Property is set by reflection",
      "The property '{0}.{1}' is set as without the generator (by reflection): its setter is {2}", Category, DiagnosticSeverity.Info, true);

    public static readonly DiagnosticDescriptor UnsupportedContext = new DiagnosticDescriptor("LTMP005", "Context class is not supported",
      "The context class '{0}' is {1}: declare a non-generic, non-abstract partial class (not nested in a generic type)", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NameConflict = new DiagnosticDescriptor("LTMP006", "Context name is taken",
      "The context of the assembly cannot be named '{0}': {1}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor HasConstructor = new DiagnosticDescriptor("LTMP007", "Context class declares a parameterless constructor",
      "The context class '{0}' must not declare a parameterless constructor, the generator writes it", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor OpenGeneric = new DiagnosticDescriptor("LTMP008", "Open generic type",
      "'{0}' is an open generic type, list its constructed types instead", Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor NotRegistered = new DiagnosticDescriptor("LTMP009", "Context is not registered automatically",
      "The context class '{0}' is not registered for the process: {1}. Pass it with LtMsgPackOptions.Contexts.", Category, DiagnosticSeverity.Info, true);

    public static readonly DiagnosticDescriptor Uncovered = new DiagnosticDescriptor("LTMP010", "Values of types only known at runtime",
      "'{0}.{1}' holds values of '{2}' and no class of the context fits it: list the classes it holds with [LtMsgPackSerializable] (when trimmed their properties may be removed, under NativeAOT their handlers cannot be created)",
      Category, DiagnosticSeverity.Warning, true);

    public static DiagnosticDescriptor Get(string id)
    {
      switch (id)
      {
        case "LTMP001": return NotPartial;
        case "LTMP002": return NotAContext;
        case "LTMP003": return NotAccessible;
        case "LTMP004": return PropertyByReflection;
        case "LTMP005": return UnsupportedContext;
        case "LTMP006": return NameConflict;
        case "LTMP007": return HasConstructor;
        case "LTMP008": return OpenGeneric;
        case "LTMP009": return NotRegistered;
        case "LTMP010": return Uncovered;
        default: throw new System.ArgumentException("Unknown diagnostic " + id);
      }
    }
  }
}
