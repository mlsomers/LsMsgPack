using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Serialization;

namespace LsMsgPack.TypeResolving
{
  public class XmlRootAttributeTypeResolver : IMsgPackTypeResolver, IMsgPackTypeIdentifier
  {

    public string IdForType(Type type, FullPropertyInfo assignedTo)
    {
      
      XmlRootAttribute root = type.GetCustomAttribute<XmlRootAttribute>(false);
      return root?.ElementName;
    }

    public Type Resolve(string typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<string, object> properties)
    {
      // TODO: ...
      return null;
    }
  }
}
