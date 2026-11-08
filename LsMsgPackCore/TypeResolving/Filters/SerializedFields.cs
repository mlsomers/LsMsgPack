namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// The fields that are serialized (see <see cref="FilterNonSettable"/>, docs/schema.md "Fields"), after the properties: a field is a property in the sense of the format (a property id, a position in the array layout).
    /// <para>Never: static and const fields, fields the compiler generates (the backing fields of auto-properties and events), fixed-size buffers, fields of pointer or ref struct types, and fields declared by the framework's types
    /// (except the public fields of framework structs that have no settable properties, e.g. <c>System.Numerics.Vector3</c>: always serialized).</para>
    /// <para>A readonly field is serialized like a property without a setter (<see cref="GetOnlyProperties"/>): written, set when reading through a constructor that takes it, filled when it is a collection, otherwise skipped.</para>
    /// </summary>
    public enum SerializedFields
    {
        /// <summary>
        /// None.
        /// </summary>
        None = 0,

        /// <summary>
        /// The fields (public or not, also of base classes) with an attribute that opts them in, the same attributes as for <see cref="NonPublicProperties.Attributed"/>
        /// (<c>[JsonInclude]</c>, <c>[DataMember]</c>, <c>[Key]</c>, Json.NET's <c>[JsonProperty]</c>...). The default.
        /// </summary>
        Attributed = 1,

        /// <summary>
        /// All public instance fields, and the non-public ones of <see cref="Attributed"/> (as Json.NET, XmlSerializer and System.Text.Json with <c>IncludeFields</c> write them).
        /// </summary>
        Public = 2,

        /// <summary>
        /// All instance fields, also the non-public ones (of base classes too). The data can then set any field of the classes it is read into: use it for classes made for serialization (see docs/security.md).
        /// </summary>
        All = 3
    }
}
