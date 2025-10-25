using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Serialization;

namespace LsMsgPack.TypeResolving.Types
{
    /// <summary>
    /// This resolver requires pre-registering your root type or assembly in order to successfully deserialize.
    /// It will use "Name" from the [XmlRoot("Name")] attribute to identify the type. Short names yield faster/smaller packages.
    /// </summary>
    public class XmlRootAttributeTypeResolver : IMsgPackTypeResolver
    {
        private Dictionary<string, Type> _resolve = new Dictionary<string, Type>();
        private Dictionary<Type, string> _resolveWriting = new Dictionary<Type, string>();

        public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackSettings settings)
        {
            if (_resolveWriting.TryGetValue(type, out string value))
                return value;

            return RegisterType(type);
        }

        /// <summary>
        /// Register a type that has an XmlRoot attribute.
        /// </summary>
        public string RegisterType(Type type)
        {
            XmlRootAttribute root = type.GetCustomAttribute<XmlRootAttribute>(false);
            string name = root?.ElementName;
            if (name == null)
                return null;

            _resolve.Add(name, type);
            _resolveWriting.Add(type, name);

            return name;
        }

        /// <summary>
        /// Bulk preregister types.
        /// An easy way to get the assembly is <code>typeof(YourType).Assembly</code>.
        /// </summary>
        public void RegisterAssembly(Assembly assembly)
        {
            Type[] types = assembly.GetExportedTypes();
            for (int t = types.Length - 1; t >= 0; t--)
                RegisterType(types[t]);
        }

        public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
        {
            return _resolve[(string)typeId];
        }
    }
}
