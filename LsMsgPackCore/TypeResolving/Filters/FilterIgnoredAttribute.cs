using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// Leaves out the properties that have an "ignore" attribute of another serializer (see <see cref="IgnoreAttributes"/>), by default all of them, including any other attribute with "Ignore" in its name:
    /// <list type="bullet">
    /// <item>System.Xml.Serialization.XmlIgnore</item>
    /// <item>System.Text.Json.Serialization.JsonIgnore</item>
    /// <item>Newtonsoft.Json.JsonIgnore</item>
    /// <item>System.Runtime.Serialization.IgnoreDataMember</item>
    /// <item>MessagePack.IgnoreMember</item>
    /// <item>PolyType.PropertyShape(Ignore = true)</item>
    /// </list>
    /// <para>By default the attributes of the properties a property overrides and of the interface properties it implements count too (<see cref="IgnoreAttributeLookup.All"/>), the presets look where their library does.</para>
    /// <para>The attributes are recognized by their names, so there are no dependencies on the libraries that define them.
    /// To behave like one serializer, replace the default filter by one of the presets (e.g. <see cref="LikeNewtonsoft"/>) in a new <see cref="MsgPackOptions.StaticFilters"/> array.</para>
    /// <para>Immutable: the serializers cache which properties pass the static filters per filter array.</para>
    /// </summary>
    public class FilterIgnoredAttribute : IMsgPackPropertyIncludeStatically
    {
        /// <summary>
        /// What each attribute type is (its <see cref="IgnoreAttributes"/> flag), decided once per type.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, AttributeKind> Kinds = new ConcurrentDictionary<Type, AttributeKind>();

        // CustomAttributes keeps one attribute per class name, and both System.Text.Json and Json.NET call theirs JsonIgnoreAttribute
        private const string JsonIgnoreName = "JsonIgnoreAttribute";

        private readonly IgnoreAttributes _ignore;
        private readonly IgnoreAttributeLookup _lookup;
        private readonly HashSet<string> _alsoIgnore;

        /// <summary>
        /// Leaves out the properties with any of the <see cref="IgnoreAttributes.All"/> attributes, also on the properties they override or implement (<see cref="IgnoreAttributeLookup.All"/>).
        /// </summary>
        public FilterIgnoredAttribute() : this(IgnoreAttributes.All) { }

        /// <param name="ignore">The attributes that leave a property out</param>
        /// <param name="alsoIgnore">Names of other attributes that leave a property out: the full name of the class, its name, or its name without the "Attribute" suffix</param>
        public FilterIgnoredAttribute(IgnoreAttributes ignore, params string[] alsoIgnore) : this(ignore, IgnoreAttributeLookup.All, alsoIgnore) { }

        /// <param name="ignore">The attributes that leave a property out</param>
        /// <param name="lookup">Where the attributes are looked for besides the property itself</param>
        /// <param name="alsoIgnore">Names of other attributes that leave a property out: the full name of the class, its name, or its name without the "Attribute" suffix</param>
        public FilterIgnoredAttribute(IgnoreAttributes ignore, IgnoreAttributeLookup lookup, params string[] alsoIgnore)
        {
            _ignore = ignore;
            _lookup = lookup;
            if (alsoIgnore != null && alsoIgnore.Length > 0)
                _alsoIgnore = new HashSet<string>(alsoIgnore, StringComparer.Ordinal);
        }

        /// <summary>
        /// The attributes that leave a property out.
        /// </summary>
        public IgnoreAttributes Ignore { get { return _ignore; } }

        /// <summary>
        /// Where the attributes are looked for besides the property itself.
        /// </summary>
        public IgnoreAttributeLookup Lookup { get { return _lookup; } }

        /// <summary>
        /// The names of other attributes that leave a property out.
        /// </summary>
        public IEnumerable<string> AlsoIgnore { get { return _alsoIgnore ?? (IEnumerable<string>)new string[0]; } }

        /// <summary>
        /// As Json.NET: <c>[Newtonsoft.Json.JsonIgnore]</c> and <c>[IgnoreDataMember]</c>, also on the properties it overrides (inherited attributes: not <c>[IgnoreDataMember]</c>) and on the interface properties of the same name.
        /// </summary>
        public static FilterIgnoredAttribute LikeNewtonsoft { get; } = new FilterIgnoredAttribute(IgnoreAttributes.Newtonsoft | IgnoreAttributes.IgnoreDataMember, IgnoreAttributeLookup.Overridden | IgnoreAttributeLookup.Interfaces);

        /// <summary>
        /// As System.Text.Json: <c>[System.Text.Json.Serialization.JsonIgnore]</c> (with <c>Condition = Always</c>, the default), only on the property itself.
        /// </summary>
        public static FilterIgnoredAttribute LikeSystemTextJson { get; } = new FilterIgnoredAttribute(IgnoreAttributes.SystemTextJson, IgnoreAttributeLookup.Declared);

        /// <summary>
        /// As XmlSerializer: <c>[XmlIgnore]</c>, only on the property itself.
        /// </summary>
        public static FilterIgnoredAttribute LikeXmlSerializer { get; } = new FilterIgnoredAttribute(IgnoreAttributes.XmlIgnore, IgnoreAttributeLookup.Declared);

        /// <summary>
        /// As DataContractSerializer: <c>[IgnoreDataMember]</c>, also on the properties it overrides.
        /// </summary>
        public static FilterIgnoredAttribute LikeDataContract { get; } = new FilterIgnoredAttribute(IgnoreAttributes.IgnoreDataMember, IgnoreAttributeLookup.OverriddenAll);

        /// <summary>
        /// As MessagePack-CSharp: <c>[MessagePack.IgnoreMember]</c> and <c>[IgnoreDataMember]</c>, only on the property itself.
        /// </summary>
        public static FilterIgnoredAttribute LikeMessagePackCSharp { get; } = new FilterIgnoredAttribute(IgnoreAttributes.MessagePackCSharp | IgnoreAttributes.IgnoreDataMember, IgnoreAttributeLookup.Declared);

        /// <summary>
        /// As Nerdbank.MessagePack: <c>[PolyType.PropertyShape(Ignore = true)]</c> and <c>[IgnoreDataMember]</c>, also on the properties it overrides (inherited attributes: not <c>[IgnoreDataMember]</c>).
        /// </summary>
        public static FilterIgnoredAttribute LikeNerdbank { get; } = new FilterIgnoredAttribute(IgnoreAttributes.PolyType | IgnoreAttributes.IgnoreDataMember, IgnoreAttributeLookup.Overridden);

        /// <inheritdoc cref="IMsgPackPropertyIncludeStatically.IncludeProperty(FullPropertyInfo)"/>
        public bool IncludeProperty(FullPropertyInfo info)
        {
            if (_lookup != IgnoreAttributeLookup.Declared && info.Attributes != null)
            {
                // Read once per property (FullPropertyInfo.Attributes), most of these arrays are empty
                PropertyAttributeSet attributes = info.Attributes;
                return !IgnoresAny((_lookup & IgnoreAttributeLookup.Overridden) != 0 ? attributes.Inheritable : attributes.Declared)
                    && ((_lookup & IgnoreAttributeLookup.OverriddenAll) == 0 || !IgnoresAny(attributes.Overridden))
                    && ((_lookup & IgnoreAttributeLookup.Interfaces) == 0 || !IgnoresAny(attributes.Interfaces));
            }

            if (info.PropertyInfo != null && info.CustomAttributes.ContainsKey(JsonIgnoreName))
            {
                // Both JsonIgnore attributes may be there, only one of them is in CustomAttributes
                foreach (object att in info.PropertyInfo.GetCustomAttributes(true))
                    if (Ignores(att))
                        return false;
                return true;
            }

            foreach (object att in info.CustomAttributes.Values)
                if (Ignores(att))
                    return false;

            return true;
        }

        private bool IgnoresAny(object[] attributes)
        {
            for (int t = 0; t < attributes.Length; t++)
                if (Ignores(attributes[t]))
                    return true;
            return false;
        }

        private bool Ignores(object attribute)
        {
            Type type = attribute.GetType();
            if (_alsoIgnore != null && (_alsoIgnore.Contains(type.FullName) || _alsoIgnore.Contains(type.Name) || _alsoIgnore.Contains(WithoutSuffix(type.Name))))
                return true;

            AttributeKind kind = Kinds.GetOrAdd(type, t => new AttributeKind(t));
            return (kind.Flag & _ignore) != 0 && kind.Ignores(attribute);
        }

        private static string WithoutSuffix(string name)
        {
            return name.EndsWith("Attribute", StringComparison.Ordinal) ? name.Substring(0, name.Length - "Attribute".Length) : name;
        }

        /// <summary>
        /// Which <see cref="IgnoreAttributes"/> flag an attribute type is, and the property that switches the attribute off (when it has one).
        /// </summary>
        private sealed class AttributeKind
        {
            internal readonly IgnoreAttributes Flag;
            private readonly PropertyInfo _systemTextJsonCondition;
            private readonly PropertyInfo _polyTypeIgnore;

            internal AttributeKind(Type type)
            {
                switch (type.FullName)
                {
                    case "System.Xml.Serialization.XmlIgnoreAttribute": Flag = IgnoreAttributes.XmlIgnore; break;
                    case "System.Text.Json.Serialization.JsonIgnoreAttribute":
                        Flag = IgnoreAttributes.SystemTextJson;
                        _systemTextJsonCondition = type.GetProperty("Condition");
                        break;
                    case "Newtonsoft.Json.JsonIgnoreAttribute": Flag = IgnoreAttributes.Newtonsoft; break;
                    case "System.Runtime.Serialization.IgnoreDataMemberAttribute": Flag = IgnoreAttributes.IgnoreDataMember; break;
                    case "MessagePack.IgnoreMemberAttribute": Flag = IgnoreAttributes.MessagePackCSharp; break;
                    case "PolyType.PropertyShapeAttribute":
                        Flag = IgnoreAttributes.PolyType;
                        _polyTypeIgnore = type.GetProperty("Ignore");
                        break;
                    default:
                        Flag = type.Name.IndexOf("Ignore", StringComparison.OrdinalIgnoreCase) >= 0 ? IgnoreAttributes.OtherIgnore : IgnoreAttributes.None;
                        break;
                }
            }

            internal bool Ignores(object attribute)
            {
                if (Flag == IgnoreAttributes.SystemTextJson)
                {
                    // JsonIgnoreCondition.Always is the default, Never / WhenWritingNull / WhenWritingDefault keep the property (the latter two per value)
                    object condition = _systemTextJsonCondition?.GetValue(attribute);
                    return condition is null || string.Equals(condition.ToString(), "Always", StringComparison.Ordinal);
                }

                if (Flag == IgnoreAttributes.PolyType) // [PropertyShape] also sets names and order, only Ignore = true leaves the property out
                    return _polyTypeIgnore != null && true.Equals(_polyTypeIgnore.GetValue(attribute));

                return true;
            }
        }
    }
}
