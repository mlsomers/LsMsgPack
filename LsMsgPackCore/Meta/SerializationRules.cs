using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

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
        return !IsHiddenFrameworkType(tType) && !HasAnonymousType(tType);

      if ((settings._addTypeIdOptions & AddTypeIdOption.IfAmbiguious) != 0)
        return assignedTo?.AssignedToType != tType && !IsHiddenFrameworkType(tType) && !HasAnonymousType(tType);

      return false;
    }

    private static readonly ConcurrentDictionary<Type, bool> Anonymous = new ConcurrentDictionary<Type, bool>();

    /// <summary>
    /// An anonymous type (<c>new { Id = 1, Name = "x" }</c>), or a type with one as a generic argument or element (<c>List&lt;anonymous&gt;</c>, <c>Page&lt;anonymous&gt;</c>).
    /// <para>No reader has such a type (the compiler makes it, internal to the writer's assembly), so a reader never resolves its name: it gets no type id (see <see cref="NeedsTypeId"/>), and where its type is not declared
    /// its objects are written by names (see <see cref="WritesByNames"/>). Anonymous types are written only, their properties have no setters (see FilterNonSettable).</para>
    /// <para>Cached per type, after a check of flags: anonymous types are generic (with one or more properties) or not public (the one without properties).</para>
    /// </summary>
    internal static bool HasAnonymousType(Type type)
    {
      if (!type.IsGenericType && !type.IsNotPublic && !type.HasElementType)
        return false;
      bool found;
      if (!Anonymous.TryGetValue(type, out found))
        found = Anonymous.GetOrAdd(type, HasAnonymousTypeUncached);
      return found;
    }

    private static bool HasAnonymousTypeUncached(Type type)
    {
      while (type.HasElementType)
        type = type.GetElementType();
      if (IsAnonymous(type))
        return true;
      Type[] arguments = type.GenericTypeArguments;
      for (int t = arguments.Length - 1; t >= 0; t--)
        if (HasAnonymousType(arguments[t]))
          return true;
      return false;
    }

    /// <summary>
    /// A type the compiler made for <c>new { ... }</c> (C# <c>&lt;&gt;f__AnonymousType0`2</c>, Visual Basic <c>VB$AnonymousType_0`2</c>): not public, compiler generated, with that name.
    /// </summary>
    internal static bool IsAnonymous(Type type)
    {
      if (!type.IsNotPublic || type.IsNested || !type.IsClass)
        return false;
      string name = type.Name;
      return (name.StartsWith("<>", StringComparison.Ordinal) || name.StartsWith("VB$", StringComparison.Ordinal))
        && name.IndexOf("AnonymousType", StringComparison.Ordinal) >= 0
        && type.IsDefined(typeof(CompilerGeneratedAttribute), false);
    }

    /// <summary>
    /// An object of a type with an anonymous type (see <see cref="HasAnonymousType"/>) is written as a map of its property names and values, not in the indexed schema and without a type id,
    /// where its type is not declared (a reader would need the type id) or below another such object (see <see cref="EntersByNames"/>).
    /// Otherwise (the root of <c>return Ok(new { Id = 1 })</c>, the elements of a root collection) it is written like any class, with an entry in the schema: the reader pairs its class with the first entry.
    /// <para>Readers match a map of names by name (with the schema too: there the keys of other objects are indexes, see ValueConverter.ConvertMap), whatever class they read it into, or keep the names when they read it as object.</para>
    /// </summary>
    /// <param name="byNames">The value is below an object with an anonymous type, or a value with one that was not written as its declared type (see <see cref="EntersByNames"/>)</param>
    internal static bool WritesByNames(Type tType, FullPropertyInfo assignedTo, bool byNames)
    {
      return (byNames || assignedTo?.AssignedToType != tType) && HasAnonymousType(tType);
    }

    /// <summary>
    /// Whether the values below a value are written in the names context of <see cref="WritesByNames"/>: below an object with an anonymous type (a reader of schema version 1 pairs the classes below the root through the writer's classes,
    /// which it never has for these), and below a value with one that is not written as its declared type (it gets no type id, so its content is read as object or as the class the reader declares).
    /// </summary>
    /// <param name="isObject">The value is an object with properties (not a collection, pair or tuple, whose elements a reader pairs at the root)</param>
    internal static bool EntersByNames(Type tType, FullPropertyInfo assignedTo, bool byNames, bool isObject)
    {
      return byNames || (isObject || assignedTo?.AssignedToType != tType) && HasAnonymousType(tType);
    }

    private static readonly ConcurrentDictionary<Type, bool> Hidden = new ConcurrentDictionary<Type, bool>();

    /// <summary>
    /// A type of the framework that is not public (or has such a type as a generic argument or element), e.g. the FrozenSet&lt;T&gt; that ToFrozenSet() returns, or a LINQ iterator:
    /// names in the data never resolve to it (see TypeResolver.Visible), so it gets no type id. The reader creates the declared type its own way, as it did for such an id.
    /// </summary>
    internal static bool IsHiddenFrameworkType(Type type)
    {
      bool hidden;
      if (!Hidden.TryGetValue(type, out hidden))
        hidden = Hidden.GetOrAdd(type, HasHiddenFrameworkType);
      return hidden;
    }

    private static bool HasHiddenFrameworkType(Type type)
    {
      while (type.HasElementType)
        type = type.GetElementType();
      Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
      if (!definition.IsVisible && TypeResolver.IsFrameworkAssembly(definition.Assembly))
        return true;
      Type[] arguments = type.GenericTypeArguments;
      for (int t = arguments.Length - 1; t >= 0; t--)
        if (HasHiddenFrameworkType(arguments[t]))
          return true;
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
      throw UnresolvableWithSchema(tType, assignedTo);
    }

    private static MsgPackException UnresolvableWithSchema(Type tType, FullPropertyInfo assignedTo) // apart, so the check above is inlined (called per object)
    {
      return new MsgPackException($"Unable to serialize {tType.FullName} assigned to {assignedTo.AssignedToType.FullName} without a type id while using the indexed schema: the property keys are schema indexes of {tType.Name}, so the type cannot be resolved by its properties when deserializing. Use {nameof(AddTypeIdOption)}.{nameof(AddTypeIdOption.IfAmbiguious)} (with the schema a type id costs about 1 byte) or set MsgPackSettings.UseInexedSchema = false.");
    }

    /// <summary>
    /// An object written as an array (<see cref="ObjectLayout.Array"/>) has no property ids at all, so without a type id the reader can only read it as the type it is assigned to.
    /// </summary>
    internal static void ThrowIfUnresolvableAsArray(Type tType, FullPropertyInfo assignedTo)
    {
      if (assignedTo?.AssignedToType is null || assignedTo.AssignedToType == tType)
        return;
      throw UnresolvableAsArray(tType, assignedTo);
    }

    private static MsgPackException UnresolvableAsArray(Type tType, FullPropertyInfo assignedTo) // apart, so the check above is inlined (called per object)
    {
      return new MsgPackException($"Unable to serialize {tType.FullName} assigned to {assignedTo.AssignedToType.FullName} without a type id as an array ({nameof(ObjectLayout)}.{nameof(ObjectLayout.Array)}): the values have no property ids, so the type cannot be resolved by its properties when deserializing. Use {nameof(AddTypeIdOption)}.{nameof(AddTypeIdOption.IfAmbiguious)} or {nameof(ObjectLayout)}.{nameof(ObjectLayout.Map)}.");
    }

    private static readonly ConcurrentDictionary<Type, string> Unsupported = new ConcurrentDictionary<Type, string>();

    /// <summary>
    /// A type of the framework that would be written as an object with properties but has no settable ones (e.g. Lazy&lt;T&gt;, Index): its values would be written as an empty map
    /// and read back as the default, without an error. The serializers write the framework types they know by themselves (see FrameworkTypeInfo), anything else of the framework that has no settable state is refused.
    /// <para>Checked once per type, when the serializers plan to write or read an object of it. A custom extension for the type (MsgPackSettings.CustomExtentionTypes, LtMsgPackOptions.Extensions) is asked first, so it can still be written that way.</para>
    /// </summary>
    internal static void ThrowIfUnsupportedFrameworkType(Type type)
    {
      string reason;
      if (!Unsupported.TryGetValue(type, out reason))
        reason = Unsupported.GetOrAdd(type, UnsupportedReason);
      if (reason != null)
        throw new MsgPackException(reason);
    }

    /// <returns>Why the type is refused, null when it is not</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private static string UnsupportedReason(Type type)
    {
      if (typeof(Type).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type))
        return $"{type.FullName} is not serialized: the data would pick the type (see docs/security.md). Write its name and resolve it in the application, against the types it expects.";

      if (type == typeof(object) || type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(IEnumerable).IsAssignableFrom(type) || !TypeResolver.IsFrameworkAssembly(type.Assembly))
        return null;

      bool hasState = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Length > 0;
      PropertyInfo[] props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
      for (int t = props.Length - 1; t >= 0; t--)
      {
        PropertyInfo prop = props[t];
        if (prop.GetIndexParameters().Length > 0)
          continue;
        if (prop.GetSetMethod() != null)
          return null; // written as an object with properties (as before)
        if (prop.GetGetMethod() != null)
          hasState = true;
      }
      if (!hasState)
        return null; // nothing to lose (e.g. an EventArgs)

      return $"{type.FullName} is not supported: it has no settable properties, so it would be written as an empty map and read back as its default value. "
        + "Serialize the values it holds (in a class of your own), or add a custom extension for it (MsgPackSettings.CustomExtentionTypes, LtMsgPackOptions.Extensions).";
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
      GetIndexedSchema(schemaSettings)?.BindReaderTypes(root, schemaSettings._propertyNameCaseInsensitive, FullPropertyInfo.NonPublicOf(schemaSettings));
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
