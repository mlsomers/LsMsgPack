using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LsMsgPack.TypeResolving
{
    internal static class TypeResolver
    {
        // 1st tier cache
        private static readonly Dictionary<string, Type> FullNameCache = new Dictionary<string, Type>(); // full name's should never collide
        private static readonly Dictionary<string, Type> UsedNameCache = new Dictionary<string, Type>(); // Previously resolved names

        // 2nd tier cache
        private static readonly Dictionary<Type, Assembly> AssemblyCache = new Dictionary<Type, Assembly>(); // assembly previously found for this "assign-to" type
        internal static readonly HashSet<Assembly> CachedAssembies = new HashSet<Assembly>(); // keep track of what has been cached

        // 3rd tier cache
        private static readonly Dictionary<string, HashSet<Type>> NameCache = new Dictionary<string, HashSet<Type>>(); // Can contain duplicate names

        internal static Type Resolve(string typeId, Type assignedTo, FullPropertyInfo rootProp, MpMap map, Dictionary<string, object> propVals)
        {
            Type result;
            // First give custom resolvers (if any) a chance...
            foreach (IMsgPackTypeResolver resolver in map.Settings?.TypeResolvers)
            {
                result = resolver.Resolve(typeId, assignedTo, rootProp, propVals);
                if (result != null)
                    return result;
            }

            if (string.IsNullOrWhiteSpace(typeId))
            {
                if (assignedTo.IsAbstract || assignedTo.IsInterface)
                    throw new Exception(string.Concat("Cannot create an instance of an interface or abstract type:\r\n  ", assignedTo.FullName,
                      "\r\nEither use MsgPackSettings.AddTypeName when serializing (easiest but adds payload) or add a custom IMsgPackTypeResolver to MsgPackSettings.TypeResolvers."));
            }
            else
            {
                return ResolveInternal(typeId, assignedTo);
            }

            return assignedTo;
        }

        internal static Type ResolveInternal(string typeName, Type assignedTo)
        {
            Type result;
            // 1st tier
            if (FullNameCache.TryGetValue(typeName, out result))
                return result;
            if (UsedNameCache.TryGetValue(typeName, out result))
                return result;

            // First try offloading this work to the framework...
            result = Type.GetType(typeName, false, true);
            if (result != null)
            {
                if (typeName.Contains("."))
                    FullNameCache.Add(typeName, result);
                else
                    UsedNameCache.Add(typeName, result);
                return result;
            }

            // 2nd tier (use the assembly of the type it is assigned to)
            Assembly assembly = null;
            if (!AssemblyCache.TryGetValue(assignedTo, out assembly))
            {
                if (assignedTo != typeof(object))
                    assembly = assignedTo.Assembly;

                //if (assembly != null) // Also cache null for a specific assign-to type so this block can be skipped for the next item
                AssemblyCache.Add(assignedTo, assembly);
            }

            if (assembly != null)
            {
                result = assembly.GetType(typeName, false, true);
                if (result is null)
                {
                    if (!CachedAssembies.Contains(assembly))
                        result = CacheAssembly(assembly, typeName); // At this point we search all types in the assembly
                    else // retreive from NameCache
                    {
                        // The assembly has previously been cached, but the type was not found by full name...
                        if (NameCache.TryGetValue(typeName, out HashSet<Type> choices))
                        {
                            if (choices.Count == 1)
                            {
                                result = choices.First();
                                UsedNameCache.Add(typeName, result);
                                return result;
                            }
                            if (choices.Count > 1)
                            {
                                throw new Exception(string.Concat("Type assignment dilamma for \"", typeName, "\" with the following choices:\r\n  ",
                                  string.Join("\r\n  ", choices.Select(t => t.FullName)),
                                  "\r\nFix this by either serializing with full name or implementing a IMsgPackTypeResolver and add it to MsgPackSettings.TypeResolvers."));
                            }
                        }
                    }

                    if (result != null)
                        return result;
                }
                else // full type name used..
                {
                    FullNameCache.Add(typeName, result);
                    return result;
                }
            }

            return assignedTo; // will probably fail
        }

        internal static Type CacheAssembly(Assembly assembly, string typeName)
        {
            Type found = null;
            Type[] types = assembly.GetTypes();
            for (int t = types.Length - 1; t >= 0; t--)
            {
                string fullName = types[t].FullName;
                string name = types[t].Name;
                Type type = types[t];
                FullNameCache.TryAdd(fullName, type);
                if (!NameCache.TryAdd(name, new HashSet<Type> { type }))
                    NameCache[name].Add(type);

                // check if found but don't bail out when found, once we start cahcing an assembly we'll finish the job!
                if (found == null)
                {
                    if (fullName == typeName)
                    {
                        FullNameCache.Add(fullName, type);
                        found = type;
                    }
                    else if (name == typeName)
                    {
                        UsedNameCache.Add(name, type);
                        found = type;
                    }
                }
            }
            CachedAssembies.Add(assembly);
            return found;
        }
    }
}
