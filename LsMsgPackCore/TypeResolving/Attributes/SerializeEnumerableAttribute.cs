using System;

namespace LsMsgPack.TypeResolving.Attributes
{
  /// <summary>
  /// Fine-grained control for serializing special collections
  /// </summary>
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
  public sealed class SerializeEnumerableAttribute : Attribute // Attribute is sealed for performance benefit in calls to GetCustomAttribute (see CA1813).
  {
    /// <summary>
    /// Specify the type of the items (elements)
    /// </summary>
    public Type ElementType { get; set; }

    /// <summary>
    /// When false, only the IEnumerable items (elements) will be serialized ignoring any other properties.
    /// </summary>
    public bool SerializeProperties { get; set; } = false;

    /// <summary>
    /// Specify not to serialize elements directly (for example when they are also accessible via a serialized property on the collection)
    /// </summary>
    public bool SerializeElements { get; set; } = true;

    public SerializeEnumerableAttribute() { }

    public SerializeEnumerableAttribute(Type elementType)
    {
      ElementType = elementType;
    }

    public SerializeEnumerableAttribute(bool serializeProperties)
    {
      SerializeProperties = serializeProperties;
    }

    public SerializeEnumerableAttribute(Type elementType, bool serializeProperties)
    {
      ElementType = elementType;
      SerializeProperties = serializeProperties;
    }
  }
}
