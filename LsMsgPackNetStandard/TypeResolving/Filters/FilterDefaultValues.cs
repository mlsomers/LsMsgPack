using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.ComponentModel;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// When a property has a default value, omit the whole property from the dictionary.
    /// Also takes [System.ComponentModel.DefaultValueAttribute] into account.
    /// </summary>
    public class FilterDefaultValues : IMsgPackPropertyIncludeDynamically
    {
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
            if (Nullable.GetUnderlyingType(propertyInfo.PropertyInfo.PropertyType) != null)
                return true; // type is nullable, value is not null but default...

            if (!type.IsValueType)
            {
                if (type == typeof(string)) return !value.Equals(string.Empty);
                return true;
            }

            if (type == typeof(int)) return !value.Equals(0);
            if (type == typeof(bool)) return !value.Equals(false);
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

            return !Activator.CreateInstance(type).Equals(value);
        }
    }
}

