using System;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// The attributes that make <see cref="FilterIgnoredAttribute"/> leave a property out. They are recognized by their full type name, so LsMsgPack does not reference the libraries that define them.
    /// </summary>
    [Flags]
    public enum IgnoreAttributes
    {
        /// <summary>
        /// No attribute leaves a property out (only the names given to the filter).
        /// </summary>
        None = 0,

        /// <summary>
        /// <c>[System.Xml.Serialization.XmlIgnore]</c> (XmlSerializer).
        /// </summary>
        XmlIgnore = 1,

        /// <summary>
        /// <c>[System.Text.Json.Serialization.JsonIgnore]</c>, only with <c>Condition = JsonIgnoreCondition.Always</c> (the default).
        /// The other conditions keep the property: <c>Never</c> always writes it, <c>WhenWritingNull</c> and <c>WhenWritingDefault</c> depend on the value (see the dynamic filters).
        /// </summary>
        SystemTextJson = 2,

        /// <summary>
        /// <c>[Newtonsoft.Json.JsonIgnore]</c> (Json.NET).
        /// </summary>
        Newtonsoft = 4,

        /// <summary>
        /// <c>[System.Runtime.Serialization.IgnoreDataMember]</c> (DataContractSerializer, also used by Json.NET, MessagePack-CSharp and Nerdbank.MessagePack).
        /// </summary>
        IgnoreDataMember = 8,

        /// <summary>
        /// <c>[MessagePack.IgnoreMember]</c> (MessagePack-CSharp).
        /// </summary>
        MessagePackCSharp = 16,

        /// <summary>
        /// <c>[PolyType.PropertyShape(Ignore = true)]</c> (Nerdbank.MessagePack).
        /// </summary>
        PolyType = 32,

        /// <summary>
        /// Any other attribute with "Ignore" in its name (the attributes above are not "other", their own flags decide).
        /// </summary>
        OtherIgnore = 1 << 30,

        /// <summary>
        /// All of the above (the default of <see cref="FilterIgnoredAttribute"/>).
        /// </summary>
        All = XmlIgnore | SystemTextJson | Newtonsoft | IgnoreDataMember | MessagePackCSharp | PolyType | OtherIgnore,
    }
}
