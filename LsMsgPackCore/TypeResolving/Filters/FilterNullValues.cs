using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System.ComponentModel;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// When a property has a null value, omit the whole property from the dictionary.
    /// </summary>
    public class FilterNullValues : IMsgPackPropertyIncludeDynamically
    {
        /// <inheritdoc cref="IMsgPackPropertyIncludeDynamically.IncludeProperty(FullPropertyInfo, object)"/>
        public bool IncludeProperty(FullPropertyInfo propertyInfo, object value)
        {
            if (propertyInfo.CustomAttributes.TryGetValue(nameof(DefaultValueAttribute), out object attribute))
            {
                DefaultValueAttribute def = attribute as DefaultValueAttribute;
                if (def?.Value != null && value == null)
                    return true; // In this case, null sould be serialized any way!
            }

            return value != null;
        }
    }
}
