using LsMsgPack.TypeResolving.Interfaces;
using System.Xml;

namespace LsMsgPack.TypeResolving
{
  internal class AttributePropertyNameResolver : IMsgPackPropertyIdResolver
  {
    public object GetId(FullPropertyInfo assignedTo)
    {
      if (assignedTo.CustomAttributes.TryGetValue("JsonPropertyName", out object val)) // System.Text.Json
      {
        return val.GetType().GetProperty("Name").GetValue(val).ToString(); // Using reflection because we do not want any dependency!
      }
      if (assignedTo.CustomAttributes.TryGetValue("JsonProperty", out object val2)) // Newtonsoft.json
      {
        return val.GetType().GetProperty("PropertyName").GetValue(val2).ToString(); // Using reflection because we do not want any dependency!
      }
      if (assignedTo.CustomAttributes.TryGetValue(nameof(XmlAttribute), out object val3)) // Xml attribute
      {
        return ((XmlAttribute)val3).Name;
      }
      if (assignedTo.CustomAttributes.TryGetValue(nameof(XmlElement), out object val4)) // Xml attribute
      {
        return ((XmlElement)val4).Name;
      }
      return null; // revert to default
    }
  }
}
