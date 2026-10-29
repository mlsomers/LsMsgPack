using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// The non-public properties that are serialized (see <see cref="FilterNonSettable"/>, docs/schema.md "Non-public properties"): properties without a public getter or setter
    /// (private, protected, internal), and the non-public setters of public properties.
    /// <para>Never: static properties, indexers, explicit interface implementations, properties the compiler generates (a record's <c>EqualityContract</c>) and non-public properties of the framework's types.</para>
    /// </summary>
    public enum NonPublicProperties
    {
        /// <summary>
        /// None (the default): only public properties, with a public setter (or none, see <see cref="GetOnlyProperties"/>).
        /// </summary>
        None = 0,

        /// <summary>
        /// The properties with an attribute that opts them in for another serializer, also in base classes, and their non-public setters (as those serializers use them):
        /// <list type="bullet">
        /// <item><c>[System.Text.Json.Serialization.JsonInclude]</c></item>
        /// <item><c>[Newtonsoft.Json.JsonProperty]</c> (Json.NET)</item>
        /// <item><c>[System.Runtime.Serialization.DataMember]</c> (DataContractSerializer)</item>
        /// <item><c>[MessagePack.Key]</c> (MessagePack-CSharp) and <c>[Nerdbank.MessagePack.Key]</c></item>
        /// <item><c>[PolyType.PropertyShape]</c> (Nerdbank.MessagePack, not with <c>Ignore = true</c>)</item>
        /// <item>any other attribute with "Include" in its name (e.g. one of your own: <c>[MsgPackInclude]</c>)</item>
        /// </list>
        /// The attributes are recognized by their names, so there are no dependencies on the libraries that define them.
        /// </summary>
        Attributed = 1,

        /// <summary>
        /// All non-public instance properties (also of base classes) and all non-public setters. The data can then set any property of the classes it is read into:
        /// use it for classes made for serialization (see docs/security.md).
        /// </summary>
        All = 2
    }

    /// <summary>
    /// The attributes that opt a property in for <see cref="NonPublicProperties.Attributed"/>, decided once per attribute type.
    /// </summary>
    internal static class IncludeAttributes
    {
        private static readonly ConcurrentDictionary<Type, Kind> Kinds = new ConcurrentDictionary<Type, Kind>();

        private sealed class Kind
        {
            internal readonly bool Includes;
            internal readonly PropertyInfo PolyTypeIgnore; // [PropertyShape(Ignore = true)] leaves the property out

            [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The type of an attribute on a kept property. Ignore (PolyType) is read by its own library, so it is kept when that library uses it.")]
            internal Kind(Type type)
            {
                switch (type.FullName)
                {
                    case "System.Text.Json.Serialization.JsonIncludeAttribute":
                    case "Newtonsoft.Json.JsonPropertyAttribute":
                    case "System.Runtime.Serialization.DataMemberAttribute":
                    case "MessagePack.KeyAttribute":
                    case "Nerdbank.MessagePack.KeyAttribute":
                        Includes = true;
                        break;
                    case "PolyType.PropertyShapeAttribute":
                        Includes = true;
                        PolyTypeIgnore = type.GetProperty("Ignore");
                        break;
                    default:
                        Includes = type.Name.IndexOf("Include", StringComparison.Ordinal) >= 0;
                        break;
                }
            }
        }

        /// <summary>
        /// Whether one of the attributes (of a property) opts it in.
        /// </summary>
        internal static bool Any(IEnumerable<object> attributes)
        {
            foreach (object attribute in attributes)
            {
                Type type = attribute.GetType();
                Kind kind = Kinds.GetOrAdd(type, t => new Kind(t));
                if (kind.Includes && (kind.PolyTypeIgnore is null || !true.Equals(kind.PolyTypeIgnore.GetValue(attribute))))
                    return true;
            }
            return false;
        }
    }
}
