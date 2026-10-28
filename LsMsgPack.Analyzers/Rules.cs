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
    private const string ReadingCategory = "LsMsgPack.Reading";
    private const string CoverageCategory = "LsMsgPack.Coverage";
    private const string SupportCategory = "LsMsgPack.Support";
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
      "'{0}' is initialized to {1} but has no [DefaultValue({1})]: in a map (ObjectLayout.Map, application/msgpack) the serializers leave out the value {2}, and a reader gets {1}",
      DefaultsCategory, DiagnosticSeverity.Warning, true,
      "The serializers leave out default values (FilterDefaultValues): the type's default, or the value of [DefaultValue]. A reader gives a property whose key a map lacks (or that a trimmed array ends before) what the constructor sets. "
      + "When that is not the type's default, the value is lost. [DefaultValue] with the initial value makes the writer leave out that value instead (and version 2 of the schema tells it to readers without the classes). "
      + "In the array layout (the default) a left out value is nil, which a reader reads as the value that was left out.",
      DocsUrl + "lsmp001-initialized-property-without-defaultvalue");

    public static readonly DiagnosticDescriptor DefaultValueDiffers = new DiagnosticDescriptor("LSMP002", "[DefaultValue] differs from the initial value",
      "'{0}' has [DefaultValue({1})] but the constructor sets {2}: in a map (ObjectLayout.Map, application/msgpack) the serializers leave out the value {1}, and a reader gets {2}",
      DefaultsCategory, DiagnosticSeverity.Warning, true,
      "The serializers leave out the value of [DefaultValue], a reader gives a property whose key a map lacks what the constructor sets: they must be the same (the array layout writes nil, which a reader reads as the [DefaultValue]).",
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

    public static readonly DiagnosticDescriptor NullReadAsInitial = new DiagnosticDescriptor("LSMP006", "Null is read back as the initial value",
      "'{0}' is initialized to {1}: in an object written as an array of its values (ObjectLayout.Array, the default) null is nil, the same as the left out [DefaultValue], so a reader gets {1}. Remove the initializer and the [DefaultValue] if null must arrive, or write objects as maps (ObjectLayout.Map).",
      DefaultsCategory, DiagnosticSeverity.Warning, true,
      "In the array layout a value the writer leaves out is nil, and so is null: a reader cannot tell them apart and reads the [DefaultValue]. [DefaultValue] makes the writer keep null, but only maps can carry it.",
      DocsUrl + "lsmp006-null-is-read-back-as-the-initial-value");

    public static readonly DiagnosticDescriptor UntypedRead = new DiagnosticDescriptor("LSMP007", "Objects are read without their property names",
      "{0} reads {1}: an object written from a class arrives without its property names, {2} (by default, and for application/x-lsmsgpack, it is an array of its values with the indexed schema). Read into a class (with nullable properties for a partial update), or write maps without the schema (ObjectLayout.Map and UseInexedSchema = false, as for application/msgpack).",
      ReadingCategory, DiagnosticSeverity.Warning, true,
      "The names of an object's properties are in the schema, or nowhere in the array layout: only the class they are read into gives them back. Only maps without the indexed schema (ObjectLayout.Map, UseInexedSchema = false, as application/msgpack writes them) carry the names.",
      DocsUrl + "lsmp007-objects-are-read-without-their-property-names");

    public static readonly DiagnosticDescriptor UncheckedModel = new DiagnosticDescriptor("LSMP008", "Serialized class of another project is not checked",
      "'{0}' is declared in {1}, which the analyzers do not check: classes are checked in the project that declares them. Reference the LsMsgPack.Core package in that project (or a package that depends on it) and set <LsMsgPackAnalyze>AllTypes</LsMsgPackAnalyze> there.",
      CoverageCategory, DiagnosticSeverity.Warning, true,
      "The rules need the source of a class (its initializers and constructor). A class of a referenced project is only checked when that project asks for it; a project with LsMsgPackAnalyze=AllTypes marks its assembly ([assembly: AssemblyMetadata(\"LsMsgPackAnalyze\", \"AllTypes\")]), which stops this warning. Classes of NuGet packages are not reported.",
      DocsUrl + "lsmp008-serialized-class-of-another-project-is-not-checked");

    public static readonly DiagnosticDescriptor JsonDocument = new DiagnosticDescriptor("LSMP009", "JSON document type is not serialized",
      "{0}: the serializers do not write or read {1}, a JSON document type. Hold the JSON as a string, or its values in a class of your own.",
      SupportCategory, DiagnosticSeverity.Warning, true,
      "System.Text.Json's JsonElement, JsonDocument and JsonNode (JsonObject, JsonArray, JsonValue) have no settable state: the serializers refuse them with a MsgPackException "
      + "(in an ASP.NET Core response a 500, as a request body a 400). Json.NET's JToken types (JObject, JArray, JValue...) are written as their own properties and read back empty or with an exception. "
      + "A string with the JSON text, or a class with the values, works both ways.",
      DocsUrl + "lsmp009-json-document-type-is-not-serialized");
  }
}
