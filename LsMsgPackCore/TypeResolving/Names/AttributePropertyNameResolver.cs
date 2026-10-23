using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Serialization;

namespace LsMsgPack.TypeResolving.Names
{
    public class AttributePropertyNameResolver : IMsgPackPropertyIdResolver
    {
        private Type _customAttribute;

        private string _customAttributePropertyName;

        private PropertyInfo _customAttributePropertyInfo;

        public AttributePropertyNameResolver() { }
        public AttributePropertyNameResolver([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type customAttribute, string customAttributePropertyName)
        {
            _customAttribute = customAttribute;
            _customAttributePropertyName = customAttributePropertyName;
            _customAttributePropertyInfo = customAttribute.GetProperty(customAttributePropertyName);
        }

        public object GetId(FullPropertyInfo assignedTo, MsgPackOptions settings)
        {
            if (_customAttribute != null)
            {
                Attribute attrib = assignedTo.PropertyInfo.GetCustomAttribute(_customAttribute, true);
                if (attrib != null)
                    return _customAttributePropertyInfo.GetValue(attrib);
            }

            return NamesOf(assignedTo).Name;
        }

        /// <summary>
        /// The id the released versions until October 2026 gave a property named by an attribute they did not read: they did not find the attributes of the JSON serializers (looked up by the wrong key)
        /// and did not know [XmlArray] and [DataMember], so data they wrote has another id for it (read by <see cref="ValueConverter"/>, see <see cref="FullPropertyInfo.FormerId"/>).
        /// </summary>
        /// <param name="formerId">The name of [XmlAttribute] or [XmlElement], or null: the id of the next resolver, or the .NET name</param>
        /// <returns>False when the id does not come from one of those attributes</returns>
        internal bool TryGetFormerId(FullPropertyInfo assignedTo, out string formerId)
        {
            AttributeNames names = NamesOf(assignedTo);
            formerId = names.Released;
            if (!names.Renamed)
                return false;
            if (_customAttribute != null)
            {
                Attribute attrib = assignedTo.PropertyInfo.GetCustomAttribute(_customAttribute, true);
                if (attrib != null && _customAttributePropertyInfo.GetValue(attrib) != null)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The names the attributes of other serializers give a property (null when none or empty: the .NET name), read once per property (static metadata, kept in its PropertyAttributeSet).
        /// </summary>
        internal sealed class AttributeNames
        {
            internal static readonly AttributeNames None = new AttributeNames(null, null, false);

            /// <summary>
            /// The id: the name of [JsonPropertyName], [JsonProperty], [XmlAttribute], [XmlElement], [XmlArray] or [DataMember] (in a [DataContract] type), the first that gives one.
            /// </summary>
            internal readonly string Name;

            /// <summary>
            /// The name of [XmlAttribute] or [XmlElement], the attributes the released versions read.
            /// </summary>
            internal readonly string Released;

            /// <summary>
            /// Whether <see cref="Name"/> comes from an attribute the released versions did not read (see TryGetFormerId).
            /// </summary>
            internal readonly bool Renamed;

            internal AttributeNames(string name, string released, bool renamed)
            {
                Name = name;
                Released = released;
                Renamed = renamed;
            }
        }

        private static AttributeNames NamesOf(FullPropertyInfo assignedTo)
        {
            PropertyAttributeSet attributes = assignedTo.Attributes;
            if (attributes is null || !ReferenceEquals(attributes.ByName, assignedTo.CustomAttributes)) // CustomAttributes may have been replaced
                return ReadNames(assignedTo.CustomAttributes, assignedTo.PropertyInfo);
            return attributes.Names ?? (attributes.Names = ReadNames(attributes.ByName, assignedTo.PropertyInfo));
        }

        private static AttributeNames ReadNames(Dictionary<string, object> byName, PropertyInfo property)
        {
            if (byName is null || byName.Count == 0)
                return AttributeNames.None;
            string json = JsonName(byName);
            string xml = XmlName(byName);
            if (json != null)
                return new AttributeNames(json, xml, true);
            if (xml != null)
                return new AttributeNames(xml, xml, false);
            string other = XmlArrayName(byName) ?? DataMemberName(byName, property);
            return other is null ? AttributeNames.None : new AttributeNames(other, null, true);
        }

        private static string JsonName(Dictionary<string, object> byName)
        {
            // Keyed by the class name of the attribute, checked by full name: the JSON attributes are read by reflection because we do not want any dependency
            string name;
            if (byName.TryGetValue("JsonPropertyNameAttribute", out object val)
                && (name = NameOf(val, "System.Text.Json.Serialization.JsonPropertyNameAttribute", "Name")) != null) // System.Text.Json
                return name;
            if (byName.TryGetValue("JsonPropertyAttribute", out object val2)
                && (name = NameOf(val2, "Newtonsoft.Json.JsonPropertyAttribute", "PropertyName")) != null) // Newtonsoft.json, [JsonProperty] without a name keeps the .NET name
                return name;
            return null;
        }

        private static string XmlName(Dictionary<string, object> byName)
        {
            // The XML attributes return an empty name when none was given ([XmlElement(Order = 1)]), which keeps the .NET name too ("" is a reserved id)
            if (byName.TryGetValue(nameof(XmlAttributeAttribute), out object val3) && val3 is XmlAttributeAttribute xmlAttribute
                && !string.IsNullOrEmpty(xmlAttribute.AttributeName)) // Xml Attribute
                return xmlAttribute.AttributeName;
            if (byName.TryGetValue(nameof(XmlElementAttribute), out object val4) && val4 is XmlElementAttribute xmlElement
                && !string.IsNullOrEmpty(xmlElement.ElementName)) // Xml Element
                return xmlElement.ElementName;
            return null; // revert to default
        }

        private static string XmlArrayName(Dictionary<string, object> byName)
        {
            if (byName.TryGetValue(nameof(XmlArrayAttribute), out object val) && val is XmlArrayAttribute xmlArray && !string.IsNullOrEmpty(xmlArray.ElementName))
                return xmlArray.ElementName;
            return null;
        }

        private static string DataMemberName(Dictionary<string, object> byName, PropertyInfo property)
        {
            // DataContractSerializer only reads [DataMember] in a type with [DataContract] (not inherited: the type declaring the property)
            if (byName.TryGetValue(nameof(DataMemberAttribute), out object val) && val is DataMemberAttribute dataMember && !string.IsNullOrEmpty(dataMember.Name)
                && property?.DeclaringType != null && property.DeclaringType.IsDefined(typeof(DataContractAttribute), false))
                return dataMember.Name;
            return null;
        }

        /// <summary>
        /// The name an attribute of a JSON serializer gives, null when it is another class of the same name or gives none.
        /// </summary>
        [DynamicDependency("Name", "System.Text.Json.Serialization.JsonPropertyNameAttribute", "System.Text.Json")]
        [DynamicDependency("PropertyName", "Newtonsoft.Json.JsonPropertyAttribute", "Newtonsoft.Json")]
        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.KeptMembers)]
        private static string NameOf(object attribute, string fullName, string property)
        {
            Type type = attribute.GetType();
            if (!string.Equals(type.FullName, fullName, StringComparison.Ordinal))
                return null;
            string name = type.GetProperty(property)?.GetValue(attribute) as string;
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }
}
