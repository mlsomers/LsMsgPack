using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// Leaves out the properties without a (public) setter: a reader could not set them. Except the properties of anonymous types (<c>new { Id = 1 }</c>), which are written only (readers set them through their fields),
    /// and the properties a constructor of the type takes (immutable classes: <c>Money(decimal amount, string currency)</c> with get-only <c>Amount</c> and <c>Currency</c>, see docs/schema.md "Immutable classes"):
    /// readers call that constructor.
    /// </summary>
    public class FilterNonSettable : IMsgPackPropertyIncludeStatically
    {
        private bool _onlyPublic;
        public FilterNonSettable(bool onlyPublic = true) { _onlyPublic = onlyPublic; }

        public bool IncludeProperty(FullPropertyInfo propertyInfo)
        {
            if (!propertyInfo.PropertyInfo.CanWrite)
                return SerializationRules.IsAnonymous(propertyInfo.PropertyInfo.DeclaringType) || ObjectConstructor.Sets(propertyInfo.PropertyInfo);

            System.Reflection.MethodInfo mth = propertyInfo.PropertyInfo.SetMethod;
            if (mth is null)
                return false;

            if (_onlyPublic && !mth.IsPublic)
                return ObjectConstructor.Sets(propertyInfo.PropertyInfo);

            return true;
        }
    }
}
