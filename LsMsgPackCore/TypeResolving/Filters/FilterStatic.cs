using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System.Reflection;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// Leaves out static properties (included in <see cref="MsgPackOptions.Default_StaticFilters"/>).
    /// <para>Type.GetProperties() also returns public static properties: without this filter a settable one is written with every instance and set again when reading,
    /// so the data could change state that is shared by the whole application (see docs/security.md).</para>
    /// </summary>
    public class FilterStatic : IMsgPackPropertyIncludeStatically
    {
        public bool IncludeProperty(FullPropertyInfo propertyInfo)
        {
            PropertyInfo property = propertyInfo.PropertyInfo;
            MethodInfo accessor = property.GetMethod ?? property.SetMethod; // non-public ones too, both accessors are static or neither
            return accessor is null || !accessor.IsStatic;
        }
    }
}
