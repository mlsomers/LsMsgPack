namespace LsMsgPack.Meta
{
  /// <summary>
  /// Why the reflection of the serializers is still correct in trimmed and NativeAOT apps: the justifications of their <c>UnconditionalSuppressMessage</c> attributes (see docs/SourceGenerator.md, "Trimming and NativeAOT").
  /// <para>The serializers that run without generated code are annotated where they are created (LtMsgPackSerializer's constructors without a context: RequiresUnreferencedCode and RequiresDynamicCode),
  /// so the warnings that remain inside are about what the generated code covers.</para>
  /// </summary>
  internal static class TrimReasons
  {
    /// <summary>
    /// Reflection over the types of the values: the application's classes and the framework types they use.
    /// </summary>
    internal const string ModelTypes = "Reflects over the types of the values: the generated code keeps the properties and constructors of the classes it describes (DynamicDependency) and LTMP010 names those it misses; without generated code the serializer's constructor requires unreferenced code.";

    /// <summary>
    /// Names in the data (type ids, schema entries).
    /// </summary>
    internal const string TypeNames = "Resolves type names of the data: after trimming only the types the application keeps can be found (the generated code keeps the classes of its context); a name that is not found is reported like any unresolved type id.";

    /// <summary>
    /// Members of framework types used by reflection, kept by a DynamicDependency on the same member.
    /// </summary>
    internal const string KeptMembers = "The members are kept by the DynamicDependency attributes of this member.";

    /// <summary>
    /// Generic types and arrays made from the types of the values (MakeGenericType, MakeArrayType, Array.CreateInstance).
    /// </summary>
    internal const string Instantiations = "Instantiates collections, arrays and framework generics over the types of the values: NativeAOT has them when the application uses them (the generated code references the collections of its model); without generated code the serializer's constructor requires dynamic code.";

    /// <summary>
    /// The static factories of System.Collections.Immutable and System.Collections.Frozen (CreateRange, ToFrozenSet...), found by name.
    /// </summary>
    internal const string ImmutableFactories = "Immutable and frozen collections are created by the static factory of their framework class (CreateRange, ToFrozenSet...), found by name: the generated code keeps the factory classes of the collections it describes.";

    /// <summary>
    /// A fallback for types without generated code.
    /// </summary>
    internal const string Fallback = "Only used for types without generated code: without a context the serializer's constructor requires unreferenced and dynamic code, with one LTMP010 names the types it does not cover.";
  }
}
