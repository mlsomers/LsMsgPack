using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Types;
using System;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The decisions of writing that the serializers share, so they write the same bytes: type ids, and how collections are written.
  /// </summary>
  internal static class SerializationRules
  {
    /// <summary>
    /// An attribute on the property takes precedence over one on the collection type
    /// </summary>
    internal static SerializeEnumerableAttribute GetEnumerableAttribute(Type tType, FullPropertyInfo assignedTo)
    {
      if (assignedTo?.CustomAttributes != null && assignedTo.CustomAttributes.TryGetValue(nameof(SerializeEnumerableAttribute), out object att))
        return (SerializeEnumerableAttribute)att;

      return CollectionInfo.Get(tType).Attribute;
    }

    internal static bool NeedsTypeId(Type tType, FullPropertyInfo assignedTo, MsgPackOptions settings)
    {
      if ((settings._addTypeIdOptions & AddTypeIdOption.Always) != 0)
        return true;

      if ((settings._addTypeIdOptions & AddTypeIdOption.IfAmbiguious) != 0)
        return assignedTo?.AssignedToType != tType;

      return false;
    }

    /// <summary>
    /// With the indexed schema, property keys are indexes into the schema of the runtime type, so without a type id the reader cannot tell which type (and thus which property names) they belong to.
    /// Resolving by signature (see <see cref="IMsgPackTypeResolver.Resolve"/>) is therefore impossible and the data would be read as the wrong type.
    /// </summary>
    internal static void ThrowIfUnresolvableWithSchema(Type tType, FullPropertyInfo assignedTo, MsgPackOptions settings)
    {
      if (assignedTo?.AssignedToType is null || assignedTo.AssignedToType == tType || !UsesIndexedSchema(settings))
        return;

      throw new MsgPackException($"Unable to serialize {tType.FullName} assigned to {assignedTo.AssignedToType.FullName} without a type id while using the indexed schema: the property keys are schema indexes of {tType.Name}, so the type cannot be resolved by its properties when deserializing. Use {nameof(AddTypeIdOption)}.{nameof(AddTypeIdOption.IfAmbiguious)} (with the schema a type id costs about 1 byte) or set MsgPackSettings.UseInexedSchema = false.");
    }

    internal static bool UsesIndexedSchema(MsgPackOptions settings)
    {
      for (int t = 0; t < settings._propertyNameResolvers.Length; t++)
        if (settings._propertyNameResolvers[t] is IndexedSchemaTypeResolver)
          return true;
      return false;
    }

    /// <summary>
    /// This can be overridden by implementing <see cref="IMsgPackTypeResolver">IMsgPackTypeResolver</see>.
    /// </summary>
    internal static object GetTypeIdentifier(Type type, MsgPackOptions settings, FullPropertyInfo propertyInfo)
    {
      object typeId = null;

      for (int t = settings._typeResolvers.Length - 1; t >= 0; t--)
      {
        typeId = settings._typeResolvers[t].IdForType(type, propertyInfo, settings);
        if (typeId != null)
          break;
      }
      if (typeId is null && !((settings._addTypeIdOptions & AddTypeIdOption.NoDefaultFallBack) > 0))
      {
        bool fullname = (settings._addTypeIdOptions & AddTypeIdOption.FullName) > 0;
        typeId = TypeResolver.GetTypeName(type, fullname);
      }

      return typeId;
    }
  }
}
