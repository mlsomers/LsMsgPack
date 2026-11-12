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

namespace LsMsgPack.Generator
{
  internal static class Diagnostics
  {
    private const string Category = "LsMsgPack.Generator";

    public static readonly DiagnosticDescriptor NotPartial = new DiagnosticDescriptor("LSMP101", "Context class is not partial",
      "The context class '{0}' must be partial (and the classes it is nested in) so the generator can add its code", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NotAContext = new DiagnosticDescriptor("LSMP102", "Context class does not derive from LsMsgPackContext",
      "The class '{0}' has [LsMsgPackSerializable] but does not derive from LsMsgPack.LsMsgPackContext", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NotAccessible = new DiagnosticDescriptor("LSMP103", "Type is not accessible to the generated code",
      "The type '{0}' is not accessible to the code generated for '{1}' (private or protected), no code is generated for it: it is serialized as without the generator", Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor PropertyByReflection = new DiagnosticDescriptor("LSMP104", "Property is set by reflection",
      "The property '{0}.{1}' is set as without the generator (by reflection): its setter is {2}", Category, DiagnosticSeverity.Info, true);

    public static readonly DiagnosticDescriptor UnsupportedContext = new DiagnosticDescriptor("LSMP105", "Context class is not supported",
      "The context class '{0}' is {1}: declare a non-generic, non-abstract partial class (not nested in a generic type)", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NameConflict = new DiagnosticDescriptor("LSMP106", "Context name is taken",
      "The context of the assembly cannot be named '{0}': {1}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor HasConstructor = new DiagnosticDescriptor("LSMP107", "Context class declares a parameterless constructor",
      "The context class '{0}' must not declare a parameterless constructor, the generator writes it", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor OpenGeneric = new DiagnosticDescriptor("LSMP108", "Open generic type",
      "'{0}' is an open generic type, list its constructed types instead", Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor NotRegistered = new DiagnosticDescriptor("LSMP109", "Context is not registered automatically",
      "The context class '{0}' is not registered for the process: {1}. Pass it with LsMsgPackOptions.Contexts.", Category, DiagnosticSeverity.Info, true);

    public static readonly DiagnosticDescriptor Uncovered = new DiagnosticDescriptor("LSMP110", "Values of types only known at runtime",
      "'{0}.{1}' holds values of '{2}' and no class of the context fits it: list the classes it holds with [LsMsgPackSerializable] (when trimmed their properties may be removed, under NativeAOT their handlers cannot be created)",
      Category, DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor Unoptimized = new DiagnosticDescriptor("LSMP111", "Generated code is not optimized",
      "The code of LsMsgPack's source generator is compiled without optimizations (a Debug build), so it can be slower than without the generator: measure performance in a Release build",
      Category, DiagnosticSeverity.Info, true);

    public static DiagnosticDescriptor Get(string id)
    {
      switch (id)
      {
        case "LSMP101": return NotPartial;
        case "LSMP102": return NotAContext;
        case "LSMP103": return NotAccessible;
        case "LSMP104": return PropertyByReflection;
        case "LSMP105": return UnsupportedContext;
        case "LSMP106": return NameConflict;
        case "LSMP107": return HasConstructor;
        case "LSMP108": return OpenGeneric;
        case "LSMP109": return NotRegistered;
        case "LSMP110": return Uncovered;
        case "LSMP111": return Unoptimized;
        default: throw new System.ArgumentException("Unknown diagnostic " + id);
      }
    }
  }
}
