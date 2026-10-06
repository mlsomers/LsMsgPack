using System;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// Where <see cref="FilterIgnoredAttribute"/> looks for the "ignore" attributes of a property, besides the property itself.
    /// The libraries differ: the presets of the filter look where their library does (checked against those libraries in the interop tests).
    /// </summary>
    [Flags]
    public enum IgnoreAttributeLookup
    {
        /// <summary>
        /// Only the property itself (System.Text.Json, XmlSerializer, MessagePack-CSharp): an override of an ignored property is written.
        /// </summary>
        Declared = 0,

        /// <summary>
        /// Also the properties it overrides, for the attributes that are inherited (<c>AttributeUsage(Inherited = true)</c>, as <c>Attribute.GetCustomAttributes(property, true)</c> finds them): Json.NET and Nerdbank.MessagePack.
        /// <c>[IgnoreDataMember]</c> is not inherited.
        /// </summary>
        Overridden = 1,

        /// <summary>
        /// Also the properties it overrides, all of their attributes (DataContractSerializer, where the base class decides: also <c>[IgnoreDataMember]</c>).
        /// </summary>
        OverriddenAll = 2,

        /// <summary>
        /// Also the properties with the same name and type of the interfaces of the type that declares it (Json.NET).
        /// </summary>
        Interfaces = 4,

        /// <summary>
        /// Everywhere (the default of <see cref="FilterIgnoredAttribute"/>): a property that any of the libraries leaves out.
        /// </summary>
        All = Overridden | OverriddenAll | Interfaces,
    }
}
