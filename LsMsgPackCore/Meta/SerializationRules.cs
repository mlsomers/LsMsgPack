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
      ThrowIfUnresolvableWithSchema(tType, assignedTo, UsesIndexedSchema(settings));
    }

    /// <param name="usesIndexedSchema">Whether the property ids are indexes of an indexed schema (see <see cref="UsesIndexedSchema"/>)</param>
    internal static void ThrowIfUnresolvableWithSchema(Type tType, FullPropertyInfo assignedTo, bool usesIndexedSchema)
    {
      if (assignedTo?.AssignedToType is null || assignedTo.AssignedToType == tType || !usesIndexedSchema)
        return;

      throw new MsgPackException($"Unable to serialize {tType.FullName} assigned to {assignedTo.AssignedToType.FullName} without a type id while using the indexed schema: the property keys are schema indexes of {tType.Name}, so the type cannot be resolved by its properties when deserializing. Use {nameof(AddTypeIdOption)}.{nameof(AddTypeIdOption.IfAmbiguious)} (with the schema a type id costs about 1 byte) or set MsgPackSettings.UseInexedSchema = false.");
    }

    /// <summary>
    /// An object written as an array (<see cref="ObjectLayout.Array"/>) has no property ids at all, so without a type id the reader can only read it as the type it is assigned to.
    /// </summary>
    internal static void ThrowIfUnresolvableAsArray(Type tType, FullPropertyInfo assignedTo)
    {
      if (assignedTo?.AssignedToType is null || assignedTo.AssignedToType == tType)
        return;

      throw new MsgPackException($"Unable to serialize {tType.FullName} assigned to {assignedTo.AssignedToType.FullName} without a type id as an array ({nameof(ObjectLayout)}.{nameof(ObjectLayout.Array)}): the values have no property ids, so the type cannot be resolved by its properties when deserializing. Use {nameof(AddTypeIdOption)}.{nameof(AddTypeIdOption.IfAmbiguious)} or {nameof(ObjectLayout)}.{nameof(ObjectLayout.Map)}.");
    }

    internal static IndexedSchemaTypeResolver GetIndexedSchema(MsgPackOptions settings)
    {
      for (int t = 0; t < settings._propertyNameResolvers.Length; t++)
        if (settings._propertyNameResolvers[t] is IndexedSchemaTypeResolver schema)
          return schema;
      return null;
    }

    /// <summary>
    /// Before reading a payload with the indexed schema: the classes that are read into other classes than the writer's (see <see cref="IndexedSchemaTypeResolver.BindReaderTypes"/>).
    /// </summary>
    internal static void BindReaderTypes(Type root, MsgPackOptions schemaSettings)
    {
      GetIndexedSchema(schemaSettings)?.BindReaderTypes(root);
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
