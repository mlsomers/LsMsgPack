using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;

namespace LsMsgPack.TypeResolving.Filters
{
  /// <summary>
  /// Calls the <c>bool ShouldSerialize{Name}()</c> method of the object for a property or field that has one, and leaves the value out when it returns false,
  /// as Json.NET does. The method is public (instance or static), takes no parameters and belongs to the class that declares the member (declared there or inherited, overrides are called);
  /// {Name} is the .NET name of the member. A method declared by a derived class for an inherited member is not called, as in Json.NET.
  /// <para>Add it to <see cref="MsgPackOptions.DynamicFilters"/> (with the other dynamic filters, e.g. <see cref="FilterDefaultValues"/>). Only the members that have such a method are checked:
  /// LtMsgPack writes the others with its usual (generated or compiled) code.</para>
  /// <para>This filter type only affects writing.</para>
  /// </summary>
  public class FilterShouldSerialize : IMsgPackPropertyIncludeWithOwner
  {
    /// <summary>
    /// Without the owner nothing can be decided: the serializers call <see cref="IncludeProperty(FullPropertyInfo, object, object)"/>.
    /// </summary>
    public bool IncludeProperty(FullPropertyInfo propertyInfo, object value)
    {
      return true;
    }

    /// <inheritdoc cref="IMsgPackPropertyIncludeWithOwner.IncludeProperty(FullPropertyInfo, object, object)"/>
    public bool IncludeProperty(FullPropertyInfo propertyInfo, object owner, object value)
    {
      ShouldSerializeMethod method = propertyInfo.Attributes?.ShouldSerialize;
      return method is null || method.Call(owner);
    }
  }
}
