using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// Leaves out the properties without a (public) setter: a reader could not set them. Except the properties of anonymous types (<c>new { Id = 1 }</c>), which are written only (readers set them through their fields).
    /// </summary>
    public class FilterNonSettable : IMsgPackPropertyIncludeStatically
    {
        private bool _onlyPublic;
        public FilterNonSettable(bool onlyPublic = true) { _onlyPublic = onlyPublic; }

        public bool IncludeProperty(FullPropertyInfo propertyInfo)
        {
            if (!propertyInfo.PropertyInfo.CanWrite)
                return SerializationRules.IsAnonymous(propertyInfo.PropertyInfo.DeclaringType);

            System.Reflection.MethodInfo mth = propertyInfo.PropertyInfo.SetMethod;
            if (mth is null)
                return false;

            if (_onlyPublic && !mth.IsPublic)
                return false;

            return true;
        }
    }
}
