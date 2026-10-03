using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using System;

namespace LsMsgPack.TypeResolving.Filters
{
    /// <summary>
    /// This is going to be a drop-in replacement for xml, json, contract, binaryformatter etc..
    /// <para>we do not want dependencies on all supported types so we'll check for "Ignore" in any attribute name, including:</para>
    /// <list type="bullet">
    /// <item>System.Xml.Serialization.XmlIgnore</item>
    /// <item>System.Text.Json.Serialization.JsonIgnore</item>
    /// <item>Newtonsoft.Json.JsonIgnore</item>
    /// <item>System.Runtime.Serialization.IgnoreDataMember</item>
    /// </list>
    /// </summary>
    public class FilterIgnoredAttribute : IMsgPackPropertyIncludeStatically
    {
        /// <inheritdoc cref="IMsgPackPropertyIncludeStatically.IncludeProperty(FullPropertyInfo)"/>
        public bool IncludeProperty(FullPropertyInfo info)
        {
            foreach (string att in info.CustomAttributes.Keys)
                if (att.IndexOf("Ignore", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

            return true;
        }
    }
}
