using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// When a property has a default value, omit the whole property from the dictionary.
    /// Also takes [System.ComponentModel.DefaultValueAttribute] into account.
    /// <para>An empty string is not a default value (the default of a string is null), as in the JSON serializers: it is written, unless <see cref="OmitEmptyStrings"/> is set.</para>
    /// </summary>
    public class FilterDefaultValues : IMsgPackPropertyIncludeDynamically
    {
        /// <summary>
        /// Leaves out default values, empty strings are written.
        /// </summary>
        public FilterDefaultValues() { } // kept parameterless (compiled callers, Activator)

        /// <param name="omitEmptyStrings">Also leave out empty strings (read back as the value the constructor sets, often null)</param>
        public FilterDefaultValues(bool omitEmptyStrings)
        {
            OmitEmptyStrings = omitEmptyStrings;
        }

        /// <summary>
        /// Empty strings are left out like default values (smaller, but they are read back as the value the constructor sets, often null). Off by default.
        /// </summary>
        public bool OmitEmptyStrings { get; }

        /// <summary>
        /// Boxed default values of other value types (enums, decimal, DateTime, structs...), so they are not created for every property value
        /// </summary>
        private static readonly ConcurrentDictionary<Type, object> DefaultInstances = new ConcurrentDictionary<Type, object>();

        /// <inheritdoc cref="IMsgPackPropertyIncludeDynamically.IncludeProperty(FullPropertyInfo, object)"/>
        public bool IncludeProperty(FullPropertyInfo propertyInfo, object value)
        {
            // return value != default; // not going to work on boxed values...

            System.Collections.Generic.Dictionary<string, object> attributes = propertyInfo.CustomAttributes;
            if (attributes.Count != 0 && attributes.TryGetValue(nameof(DefaultValueAttribute), out object attribute)) // most properties have no attributes, the lookup hashes the name
            {
                DefaultValueAttribute def = attribute as DefaultValueAttribute;
                if (def?.Value != null)
                    return !def.Value.Equals(value); // In this case, null would be serialized!
            }

            if (value is null)
                return false;

            Type type = value.GetType();
            if (!type.IsValueType) // before the Nullable<> check: the value of a nullable property is never a reference type
            {
                if (OmitEmptyStrings && type == typeof(string)) return ((string)value).Length != 0;
                return true;
            }

            Type propertyType = propertyInfo.PropertyInfo.PropertyType;
            if (propertyType.IsGenericType && !propertyType.IsGenericTypeDefinition && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>)) // Nullable.GetUnderlyingType(propertyType) != null, without allocating the generic arguments
                return true; // type is nullable, value is not null but default...

            if (type == typeof(int)) return (int)value != 0;
            if (type == typeof(bool)) return (bool)value;
            if (type == typeof(long)) return (long)value != 0;
            if (type == typeof(float)) return (float)value != 0;
            if (type == typeof(double)) return (double)value != 0;
            if (type == typeof(Guid)) return !value.Equals(Guid.Empty);
            if (type == typeof(byte)) return (byte)value != 0;
            if (type == typeof(short)) return (short)value != 0;
            if (type == typeof(ushort)) return (ushort)value != 0;
            if (type == typeof(uint)) return (uint)value != 0;
            if (type == typeof(ulong)) return (ulong)value != 0;
            if (type == typeof(sbyte)) return (sbyte)value != 0;
            if (type == typeof(decimal)) return (decimal)value != 0m; // as decimal.Equals: 0.00m is a default value too
            if (type == typeof(DateTime)) return ((DateTime)value).Ticks != 0; // as DateTime.Equals: the Kind is not compared

            return !DefaultInstances.GetOrAdd(type, t => Activator.CreateInstance(t)).Equals(value);
        }
    }
}

