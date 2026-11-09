using LsMsgPack.Meta;

namespace LsMsgPack.TypeResolving.Interfaces
{
  /// <summary>
  /// A dynamic filter (in <see cref="MsgPackOptions.DynamicFilters"/>) that decides by the object owning the property too, e.g. a property left out depending on another one
  /// (<see cref="Filters.FilterShouldSerialize"/>). The serializers call <see cref="IncludeProperty(FullPropertyInfo, object, object)"/> instead of the one without the owner.
  /// <para>This filter type only affects writing.</para>
  /// </summary>
  public interface IMsgPackPropertyIncludeWithOwner : IMsgPackPropertyIncludeDynamically
  {
    /// <param name="propertyInfo">The property (or field)</param>
    /// <param name="owner">The object being written (a struct: a boxed copy)</param>
    /// <param name="value">The value of the property</param>
    /// <returns>false to leave the value out (no key in a map, nil in the array layout)</returns>
    bool IncludeProperty(FullPropertyInfo propertyInfo, object owner, object value);
  }
}
