using Microsoft.CodeAnalysis;

namespace LsMsgPack.Analyzers
{
  /// <summary>
  /// The diagnostics of the analyzers (docs/Analyzers.md). They are reported for the properties of serialized types only (see <see cref="ModelTypes"/>).
  /// </summary>
  internal static class Rules
  {
    private const string DefaultsCategory = "LsMsgPack.Defaults";
    private const string SecurityCategory = "LsMsgPack.Security";
    private const string DocsUrl = "https://github.com/mlsomers/LsMsgPack/blob/master/docs/Analyzers.md#";

    /// <summary>
    /// The text of the attribute argument the code fix writes (e.g. <c>3L</c>, <c>typeof(decimal), "1.5"</c>), in <see cref="Diagnostic.Properties"/>.
    /// </summary>
    public const string ArgumentKey = "Argument";

    /// <summary>
    /// The text of the initializer the code fix of <see cref="DefaultValueDiffers"/> can write instead (absent when the property cannot get one).
    /// </summary>
    public const string InitializerKey = "Initializer";

    public static readonly DiagnosticDescriptor MissingDefaultValue = new DiagnosticDescriptor("LSMP001", "Initialized property without [DefaultValue]",
      "'{0}' is initialized to {1} but has no [DefaultValue({1})]: the serializers leave out the value {2}, and a reader gets {1}",
      DefaultsCategory, DiagnosticSeverity.Warning, true,
      "The serializers leave out default values (FilterDefaultValues): the type's default, or the value of [DefaultValue]. A reader gives a left out property what the constructor sets. "
      + "When that is not the type's default, the value is lost. [DefaultValue] with the initial value makes the writer leave out that value instead (and version 2 of the schema tells it to readers without the classes).",
      DocsUrl + "lsmp001-initialized-property-without-defaultvalue");

    public static readonly DiagnosticDescriptor DefaultValueDiffers = new DiagnosticDescriptor("LSMP002", "[DefaultValue] differs from the initial value",
      "'{0}' has [DefaultValue({1})] but the constructor sets {2}: the serializers leave out the value {1}, and a reader gets {2}",
      DefaultsCategory, DiagnosticSeverity.Warning, true,
      "The serializers leave out the value of [DefaultValue], a reader gives a left out property what the constructor sets: they must be the same.",
      DocsUrl + "lsmp002-defaultvalue-differs-from-the-initial-value");

    public static readonly DiagnosticDescriptor DefaultValueType = new DiagnosticDescriptor("LSMP003", "[DefaultValue] of another type than the property",
      "The [DefaultValue] of '{0}' is {1}, not {2}: it never equals the value, so the value is always written",
      DefaultsCategory, DiagnosticSeverity.Warning, true,
      "The serializers compare the value with the one of [DefaultValue] (object.Equals): a value of another type (e.g. the int 1 for a long) is never equal.",
      DocsUrl + "lsmp003-defaultvalue-of-another-type-than-the-property");

    public static readonly DiagnosticDescriptor OpenProperty = new DiagnosticDescriptor("LSMP004", "Property holds any type the data picks",
      "'{0}' is declared as {1}: the data can make a reader create any type that fits {2}, including types that run code when they are created or filled. Declare the type it holds, or set MsgPackOptions.TypeGuard.",
      SecurityCategory, DiagnosticSeverity.Warning, true,
      "Where a value is declared as object (or as an interface or abstract class of the framework), the type id in the data picks the type a reader creates (docs/security.md).",
      DocsUrl + "lsmp004-property-holds-any-type-the-data-picks");

    public static readonly DiagnosticDescriptor OpenRead = new DiagnosticDescriptor("LSMP005", "Data is read into any type it picks",
      "{0} reads {1}: the data can make the reader create any type that fits {2}, including types that run code when they are created or filled. Read into the type you expect, or set MsgPackOptions.TypeGuard.",
      SecurityCategory, DiagnosticSeverity.Warning, true,
      "Reading into object (or into an interface or abstract class of the framework) lets the type id in the data pick the type the reader creates (docs/security.md).",
      DocsUrl + "lsmp005-data-is-read-into-any-type-it-picks");
  }
}
