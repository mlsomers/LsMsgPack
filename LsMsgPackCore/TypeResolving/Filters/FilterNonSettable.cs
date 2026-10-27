using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Reflection;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// Leaves out the properties without a (public) setter: a reader could not set them. Except the properties of anonymous types (<c>new { Id = 1 }</c>), which are written only (readers set them through their fields),
    /// the properties a constructor of the type takes (immutable classes: <c>Money(decimal amount, string currency)</c> with get-only <c>Amount</c> and <c>Currency</c>, see docs/schema.md "Immutable classes"):
    /// readers call that constructor, and the properties without a setter that <see cref="GetOnlyProperties"/> keeps (by default: computed properties and get-only collections, see docs/schema.md "Computed and get-only properties").
    /// </summary>
    public class FilterNonSettable : IMsgPackPropertyIncludeStatically
    {
        private bool _onlyPublic;
        private readonly GetOnlyProperties _getOnly;

        /// <summary>
        /// Keeps the properties without a setter of <see cref="GetOnlyProperties.All"/>.
        /// </summary>
        public FilterNonSettable(bool onlyPublic = true) : this(GetOnlyProperties.All, onlyPublic) { }

        /// <param name="getOnlyProperties">The properties without a setter that are kept (<see cref="GetOnlyProperties.None"/>: none, as versions until October 2026 did)</param>
        /// <param name="onlyPublic">Leaves out the properties with a setter that is not public (except those a constructor takes)</param>
        public FilterNonSettable(GetOnlyProperties getOnlyProperties, bool onlyPublic = true)
        {
            _onlyPublic = onlyPublic;
            _getOnly = getOnlyProperties;
        }

        /// <summary>
        /// The properties without a setter that are kept.
        /// </summary>
        public GetOnlyProperties GetOnlyProperties { get { return _getOnly; } }

        public bool IncludeProperty(FullPropertyInfo propertyInfo)
        {
            if (!propertyInfo.PropertyInfo.CanWrite)
                return SerializationRules.IsAnonymous(propertyInfo.PropertyInfo.DeclaringType) || ObjectConstructor.Sets(propertyInfo.PropertyInfo) || KeepsGetOnly(propertyInfo.PropertyInfo);

            System.Reflection.MethodInfo mth = propertyInfo.PropertyInfo.SetMethod;
            if (mth is null)
                return false;

            if (_onlyPublic && !mth.IsPublic)
                return ObjectConstructor.Sets(propertyInfo.PropertyInfo);

            return true;
        }

        /// <summary>
        /// A property without a setter (not a private one), not static, of the user's (properties declared by framework types, e.g. <c>Exception.Message</c> or <c>List&lt;T&gt;.Count</c>, stay out:
        /// framework types without settable properties are refused or written as their plain value, see SerializationRules.ThrowIfUnsupportedFrameworkType).
        /// </summary>
        private bool KeepsGetOnly(PropertyInfo property)
        {
            MethodInfo getter = property.GetGetMethod();
            if (_getOnly == GetOnlyProperties.None || getter is null || getter.IsStatic || TypeResolver.IsFrameworkAssembly(property.DeclaringType.Assembly))
                return false;
            GetOnlyProperties kind = GetOnlyCollection.IsFillable(property.PropertyType) ? GetOnlyProperties.Collections : GetOnlyProperties.Computed;
            return (_getOnly & kind) != 0;
        }
    }

    /// <summary>
    /// The properties without a setter (<c>{ get; }</c> or <c>=&gt; ...</c>) that <see cref="FilterNonSettable"/> keeps, besides those of anonymous types and those a constructor takes (always kept).
    /// Properties with a setter that is not public are not among them (see the onlyPublic parameter of the filter).
    /// </summary>
    [Flags]
    public enum GetOnlyProperties
    {
        /// <summary>
        /// Left out, as versions until October 2026 did.
        /// </summary>
        None = 0,

        /// <summary>
        /// A collection or dictionary with Add (<c>public List&lt;Line&gt; Lines { get; } = new List&lt;Line&gt;();</c>, not an array): written, and filled when read (cleared first, then the items read are added, as the getter returns it).
        /// A read-only collection, or a getter returning null, is left as it is.
        /// </summary>
        Collections = 1,

        /// <summary>
        /// Any other property without a setter (<c>public decimal Total =&gt; Lines.Sum(l =&gt; l.Amount);</c>): written for readers without the class (JavaScript, other languages, DTOs with setters), skipped when read.
        /// </summary>
        Computed = 2,

        /// <summary>
        /// Both (the default).
        /// </summary>
        All = Collections | Computed
    }
}
