using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<string, Type> _resolve = new ConcurrentDictionary<string, Type>(); // settings (and their resolvers) may be shared between threads
        private readonly ConcurrentDictionary<Type, string> _resolveWriting = new ConcurrentDictionary<Type, string>();

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

            _resolve[name] = type;
            _resolveWriting[type] = name;

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
            string name = typeId as string; // null or an id from another resolver
            if (name != null && _resolve.TryGetValue(name, out Type type))
                return type;
            return null;
        }
    }
}
