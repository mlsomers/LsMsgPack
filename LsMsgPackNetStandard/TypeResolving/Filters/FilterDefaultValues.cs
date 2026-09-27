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
    /// </summary>
    public class FilterDefaultValues : IMsgPackPropertyIncludeDynamically
    {
        /// <summary>
        /// Boxed default values of other value types (enums, decimal, DateTime, structs...), so they are not created for every property value
        /// </summary>
        private static readonly ConcurrentDictionary<Type, object> DefaultInstances = new ConcurrentDictionary<Type, object>();

        /// <inheritdoc cref="IMsgPackPropertyIncludeDynamically.IncludeProperty(FullPropertyInfo, object)"/>
        public bool IncludeProperty(FullPropertyInfo propertyInfo, object value)
        {
            // return value != default; // not going to work on boxed values...

            if (propertyInfo.CustomAttributes.TryGetValue(nameof(DefaultValueAttribute), out object attribute))
            {
                DefaultValueAttribute def = attribute as DefaultValueAttribute;
                if (def?.Value != null)
                    return !def.Value.Equals(value); // In this case, null would be serialized!
            }

            if (value is null)
                return false;

            Type type = value.GetType();
            Type propertyType = propertyInfo.PropertyInfo.PropertyType;
            if (propertyType.IsGenericType && !propertyType.IsGenericTypeDefinition && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>)) // Nullable.GetUnderlyingType(propertyType) != null, without allocating the generic arguments
                return true; // type is nullable, value is not null but default...

            if (!type.IsValueType)
            {
                if (type == typeof(string)) return !value.Equals(string.Empty);
                return true;
            }

            if (type == typeof(int)) return (int)value != 0;
            if (type == typeof(bool)) return (bool)value;
            if (type == typeof(long)) return !value.Equals(0);
            if (type == typeof(float)) return !value.Equals(0);
            if (type == typeof(double)) return !value.Equals(0);
            if (type == typeof(Guid)) return !value.Equals(Guid.Empty);
            if (type == typeof(byte)) return !value.Equals(0);
            if (type == typeof(short)) return !value.Equals(0);
            if (type == typeof(ushort)) return !value.Equals(0);
            if (type == typeof(uint)) return !value.Equals(0);
            if (type == typeof(ulong)) return !value.Equals(0);
            if (type == typeof(sbyte)) return !value.Equals(0);

            return !DefaultInstances.GetOrAdd(type, t => Activator.CreateInstance(t)).Equals(value);
        }
    }
}

