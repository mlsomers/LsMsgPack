using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Reflection;
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
        public AttributePropertyNameResolver(Type customAttribute, string customAttributePropertyName)
        {
            _customAttribute = customAttribute;
            _customAttributePropertyName = customAttributePropertyName;
            _customAttributePropertyInfo = customAttribute.GetProperty(customAttributePropertyName);
        }

        public object GetId(FullPropertyInfo assignedTo, MsgPackSettings settings)
        {
            if (_customAttribute != null)
            {
                Attribute attrib = assignedTo.PropertyInfo.GetCustomAttribute(_customAttribute, true);
                if (attrib != null)
                    return _customAttributePropertyInfo.GetValue(attrib);
            }

            if (assignedTo.CustomAttributes.TryGetValue("JsonPropertyName", out object val)) // System.Text.Json
            {
                return val.GetType().GetProperty("Name").GetValue(val).ToString(); // Using reflection because we do not want any dependency!
            }
            if (assignedTo.CustomAttributes.TryGetValue("JsonProperty", out object val2)) // Newtonsoft.json
            {
                return val.GetType().GetProperty("PropertyName").GetValue(val2).ToString(); // Using reflection because we do not want any dependency!
            }
            if (assignedTo.CustomAttributes.TryGetValue(nameof(XmlAttributeAttribute), out object val3)) // Xml Attribute
            {
                return ((XmlAttributeAttribute)val3).AttributeName;
            }
            if (assignedTo.CustomAttributes.TryGetValue(nameof(XmlElementAttribute), out object val4)) // Xml Element
            {
                return ((XmlElementAttribute)val4).ElementName;
            }
            return null; // revert to default
        }
    }
}
