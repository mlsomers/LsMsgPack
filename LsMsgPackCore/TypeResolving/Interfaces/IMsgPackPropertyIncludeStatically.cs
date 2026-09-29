using LsMsgPack.Meta;

namespace LsMsgPack.TypeResolving.Interfaces
{
  /// <summary>
  /// Implement to filter properties (include or exclude them) based on static (hardcoded) property information (such as attributes)
  /// <para>This filter type affects writing as well as reading.</para>
  /// </summary>
  public interface IMsgPackPropertyIncludeStatically
  {
    /// <summary>
    /// Check if this property needs to be included or can be excluded from serialization
    /// </summary>
    /// <param name="propertyInfo">Item with some cached information, including propertyinfo</param>
    /// <returns>True if the property should be included</returns>
    bool IncludeProperty(FullPropertyInfo propertyInfo);
  }
}
