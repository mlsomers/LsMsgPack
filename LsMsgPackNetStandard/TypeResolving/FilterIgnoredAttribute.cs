using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Linq;

namespace LsMsgPack.TypeResolving
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
      string[] atts = info.CustomAttributes.Keys.ToArray();
      bool include = true;
      for (int i = atts.Length - 1; i >= 0; i--)
        if (atts[i].IndexOf("Ignore", StringComparison.InvariantCultureIgnoreCase) >= 0) { include = false; break; }
            
      return include;
    }
  }
}
