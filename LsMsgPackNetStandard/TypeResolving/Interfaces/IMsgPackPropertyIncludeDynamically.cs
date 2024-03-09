namespace LsMsgPack.TypeResolving.Interfaces
{
  /// <summary>
  /// Implement to filter properties (include or exclude them) based on the value of the property
  /// <para>This filter type only affects writing.</para>
  /// </summary>
  public interface IMsgPackPropertyIncludeDynamically
  {

    bool IncludeProperty(FullPropertyInfo propertyInfo, object value);
  }
}
