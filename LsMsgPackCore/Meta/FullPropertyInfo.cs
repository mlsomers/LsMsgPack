using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace LsMsgPack.Meta
{
  public class FullPropertyInfo
  {
    // Type is a IMsgPackPropertyIdResolver Type
    private static readonly ConcurrentDictionary<MemberInfo, FullPropertyInfo> Cache = new ConcurrentDictionary<MemberInfo, FullPropertyInfo>();
    private Dictionary<Type, ConstructorInfo> _constructorTakingType; // per instance (created when first used), constructors differ per AssignedToType

    /// <param name="member">A property or a field</param>
    public static FullPropertyInfo GetFullPropInfo(MemberInfo member, MsgPackOptions settings)
    {
      if (member == null)
        return null;

      FullPropertyInfo full;
      if (settings._propertyNameResolvers is null || settings._propertyNameResolvers.Length == 0){ // Only cache for default resolver, Implemented resolvers must have their own cache (or not)
        if (Cache.TryGetValue(member, out full))
          return full;
      }

      full = new FullPropertyInfo(member);
      ResolvePropertyId(full, settings);

      if (settings._propertyNameResolvers is null || settings._propertyNameResolvers.Length == 0)
        return Cache.GetOrAdd(member, full); // another thread may have been first
      return full;
    }

    /// <summary>
    /// The first id returned by the <see cref="MsgPackSettings.PropertyNameResolvers"/> (consulted from last to first), or the name of the property.
    /// </summary>
    private static void ResolvePropertyId(FullPropertyInfo full, MsgPackOptions settings)
    {
      IMsgPackPropertyIdResolver[] resolvers = settings._propertyNameResolvers;
      full.PropertyId = null;
      int t = resolvers.Length - 1;
      for (; t >= 0; t--)
      {
        full.PropertyId = resolvers[t].GetId(full, settings);
        if (full.PropertyId != null)
          break;
      }

      if (full.PropertyId == null)
      {
        full.PropertyId = full.Name;
        return;
      }

      if (full.PropertyId is string name && IsSchemaName(name) && !(resolvers[t] is IndexedSchemaTypeResolver))
      {
        IndexedSchemaTypeResolver schema = SchemaAfter(resolvers, t);
        if (schema != null) // the schema has the name the resolver gives (see SchemaName), the id is its index
        {
          full.SchemaName = name;
          full.PropertyId = schema.IdOfSchemaName(full, name, settings) ?? name; // the name itself when the schema of the data lacks it: written before October 2026 (the name was the key)
        }
      }
    }

    /// <summary>
    /// The indexed schema among the resolvers asked after the one at <paramref name="index"/> (the session adds it first, so it is asked last), null when there is none.
    /// </summary>
    private static IndexedSchemaTypeResolver SchemaAfter(IMsgPackPropertyIdResolver[] resolvers, int index)
    {
      for (int t = index - 1; t >= 0; t--)
        if (resolvers[t] is IndexedSchemaTypeResolver schema)
          return schema;
      return null;
    }

    /// <summary>
    /// Ids the schema cannot hold as names: the empty string and "@" (reserved, see ThrowIfIdsNotUnique; "@" marks the content in version 2 of the schema).
    /// </summary>
    private static bool IsSchemaName(string id)
    {
      return id.Length > 0 && id != MsgPackOptions.ContentKey;
    }

    /// <summary>
    /// The name of a property in the indexed schema (see <see cref="SchemaName"/>): the string id the first property id resolver asked before the schema gives it, otherwise its .NET name.
    /// </summary>
    internal static string SchemaNameOf(FullPropertyInfo full, MsgPackOptions settings)
    {
      if (full.SchemaName != null)
        return full.SchemaName;
      IMsgPackPropertyIdResolver[] resolvers = settings._propertyNameResolvers;
      for (int t = resolvers.Length - 1; t >= 0 && !(resolvers[t] is IndexedSchemaTypeResolver); t--) // as ResolvePropertyId asks them
      {
        object id = resolvers[t].GetId(full, settings);
        if (id is null)
          continue;
        if (id is string name && IsSchemaName(name))
          return full.SchemaName = name;
        break;
      }
      return full.Name;
    }

    // Attributes are static metadata, so they are read once per member (even when the FullPropertyInfo itself cannot be cached because of custom property id resolvers)
    private static readonly ConcurrentDictionary<MemberInfo, PropertyAttributeSet> AttributesCache = new ConcurrentDictionary<MemberInfo, PropertyAttributeSet>();

    internal static PropertyAttributeSet AttributesOf(MemberInfo member)
    {
      return AttributesCache.GetOrAdd(member, m => new PropertyAttributeSet(m));
    }

    /// <param name="member">A property or a field</param>
    private FullPropertyInfo(MemberInfo member)
    {
      Member = member;
      PropertyInfo = member as PropertyInfo;
      FieldInfo = member as FieldInfo;
      if (PropertyInfo is null && FieldInfo is null)
        throw new ArgumentException($"{member.MemberType} {member.Name} is not a property or a field.", nameof(member));
      Name = member.Name;
      DeclaredType = PropertyInfo?.PropertyType ?? FieldInfo.FieldType;
      Attributes = AttributesOf(member);
      CustomAttributes = Attributes.ByName;
      AssignedToType = DeclaredType;
    }

    /// <summary>
    /// A new instance for a session (see <see cref="GetStaticallyIncludedProps"/>), with the name as its id: the session sets its own ids on it.
    /// </summary>
    private FullPropertyInfo(FullPropertyInfo kept)
    {
      Member = kept.Member;
      PropertyInfo = kept.PropertyInfo;
      FieldInfo = kept.FieldInfo;
      Name = kept.Name;
      DeclaredType = kept.DeclaredType;
      Attributes = kept.Attributes;
      CustomAttributes = kept.CustomAttributes;
      _assignedToType = kept._assignedToType;
      _accessor = kept._accessor;
      _uniqueName = kept._uniqueName;
      PropertyId = kept.Name;
      SchemaName = null;
      StaticallyIgnored = false;
    }

    /// <summary>
    /// The attributes of the property or field, also the ones a property may inherit (shared by all FullPropertyInfo of the member). Null without a member.
    /// </summary>
    internal readonly PropertyAttributeSet Attributes;

    public FullPropertyInfo(Type assignToType)
    {
      AssignedToType = assignToType;
    }

    private Type _assignedToType;
    public Type AssignedToType
    {
      get
      {
        return _assignedToType;
      }
      set
      {
        Type nullableType = Nullable.GetUnderlyingType(value);
        if (!(nullableType is null))
          _assignedToType = nullableType;
        else
          _assignedToType = value;
      }
    }

    /// <summary>
    /// The serialized member: a <see cref="System.Reflection.PropertyInfo"/> or a <see cref="System.Reflection.FieldInfo"/> (see <see cref="TypeResolving.Filters.SerializedFields"/>).
    /// Null for the info of a root value, an element, a key or a value of a collection (only the type it is assigned to).
    /// </summary>
    public MemberInfo Member { get; }

    /// <summary>
    /// The property, null for a field (see <see cref="FieldInfo"/>) and without a member (see <see cref="Member"/>).
    /// </summary>
    public PropertyInfo PropertyInfo { get; }

    /// <summary>
    /// The field, null for a property (see <see cref="PropertyInfo"/>) and without a member (see <see cref="Member"/>).
    /// </summary>
    public FieldInfo FieldInfo { get; }

    /// <summary>
    /// The .NET name of the property or field (also the default property id), null without a member.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The type of the property or field as declared (a <see cref="Nullable{T}"/> stays one, <see cref="AssignedToType"/> is its underlying type), null without a member.
    /// </summary>
    public Type DeclaredType { get; }

    private PropertyAccessor _accessor;

    /// <summary>
    /// Set by <see cref="GetStaticallyIncludedProps"/> when no other kept property of the type has the same name (a property hidden by one of another type may have).
    /// </summary>
    internal bool _uniqueName;

    /// <summary>
    /// The value of the property, read by a compiled delegate once the property is used often (see <see cref="PropertyAccessor"/>).
    /// </summary>
    internal object GetValue(object instance)
    {
      PropertyAccessor accessor = _accessor ?? (_accessor = PropertyAccessor.Get(Member));
      return accessor.GetValue(instance);
    }

    /// <summary>
    /// The property has no setter and reading skips its value (see <see cref="PropertyAccessor.Computed"/>).
    /// </summary>
    internal bool IsComputed
    {
      get { return (_accessor ?? (_accessor = PropertyAccessor.Get(Member))).Computed; }
    }

    /// <summary>
    /// Sets the value of the property, by a compiled delegate once the property is used often (see <see cref="PropertyAccessor"/>).
    /// A collection property without a setter is filled, a computed property is not set.
    /// </summary>
    internal void SetValue(object instance, object value)
    {
      PropertyAccessor accessor = _accessor ?? (_accessor = PropertyAccessor.Get(Member));
      accessor.SetValue(instance, value);
    }

    /// <summary>
    /// The value a nil sets in an object written as an array (<see cref="ObjectLayout.Array"/>): what the writer's <see cref="TypeResolving.Filters.FilterDefaultValues"/> leaves out,
    /// so the value of [DefaultValue] when the settings have that filter, otherwise the default of the type (null for references and nullables), as JSON reads a null.
    /// A value missing after the end of the array leaves the property as the constructor made it (like a key that is not in a map).
    /// <para>Until October 2026 a nil also left the property as the constructor made it, so a null could not be read into a property the constructor initializes, and a left out 0 or false was read as the initial value.</para>
    /// </summary>
    internal object NilValue(MsgPackOptions settings)
    {
      PropertyAttributeSet.NilValues nil = Attributes.Nil;
      if (nil.DefaultValue != null && settings.LeavesOutDefaultValues)
        return nil.DefaultValue;
      return nil.TypeDefault;
    }

    /// <summary>
    /// Note that this may not be the complete set, When multiple attributes of the same type are applied, only the first one will be listed here, so if your custom attribute supports multiple instances on a property you will need to get them from the propertyInfo.
    /// </summary>
    public Dictionary<string, object> CustomAttributes { get; set; }

    /// <summary>
    /// Whether the static filters left this property out (for display), set on the properties of a session or of custom property id resolvers.
    /// <para>Not set on the instances that all settings share (no property id resolvers, no session): different static filters may decide differently, the kept properties are cached per filter array instead.</para>
    /// </summary>
    public bool? StaticallyIgnored { get; set; }

    /// <summary>
    /// By default the property name (string), but can be overridden by IMsgPackPropertyIdResolver
    /// </summary>
    public object PropertyId { get; set; }

    /// <summary>
    /// With the indexed schema: the name the schema has for the property when a property id resolver gives it a string id (e.g. the name of [JsonPropertyName] with <see cref="AttributePropertyNameResolver"/>), null for its .NET name.
    /// The id is then the index of that name, as for any other property (until October 2026 the schema had the .NET name, and the resolver's id was written as the key; readers still read that).
    /// </summary>
    internal string SchemaName;

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    public ConstructorInfo GetConstructorTaking(Type type)
    {
      if (_constructorTakingType is null)
        _constructorTakingType = new Dictionary<Type, ConstructorInfo>();

      if (_constructorTakingType.TryGetValue(type, out ConstructorInfo constructor))
        return constructor;

      // Todo: concurrent locking system

      ConstructorInfo ci = AssignedToType.GetConstructor(new[] { type });
      _constructorTakingType.Add(type, ci);
      return ci;
    }

    public override string ToString()
    {
      string ignored = (StaticallyIgnored.HasValue && StaticallyIgnored.Value) ? " (ignored)" : string.Empty;
      string propInfo = Member is null ? " not a member" : FieldInfo is null ? $" property: {Name}" : $" field: {Name}";
      return $"{AssignedToType}{ignored}{propInfo}";
    }


    /// <summary>
    /// The type of a property or field as declared.
    /// </summary>
    internal static Type TypeOf(MemberInfo member)
    {
      return member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
    }

    /// <summary>
    /// "property" or "field", for messages.
    /// </summary>
    internal string Kind
    {
      get { return FieldInfo is null ? "property" : "field"; }
    }

    // One cache per MemberSelection (its Index)
    private static readonly ConcurrentDictionary<Type, MemberInfo[]>[] MembersCache = CachePer<MemberInfo[]>(MemberSelection.Count);

    /// <summary>
    /// What an <see cref="ObjectConstructor"/> binds its parameters to, for any settings: the public properties and the public fields.
    /// </summary>
    internal static readonly MemberSelection PublicMembers = new MemberSelection(NonPublicProperties.None, SerializedFields.Public);

    /// <summary>
    /// The candidates for serialization: the public properties, the non-public properties of <see cref="MemberSelection.NonPublic"/> (see <see cref="NonPublicCandidates"/>), then the fields of
    /// <see cref="MemberSelection.Fields"/> (see <see cref="AddFieldCandidates"/>; after the properties, so the positions of the properties do not depend on the fields),
    /// excluding indexers and members hidden by a member with the same name in a derived class ("new" modifier, or an override of a non-public property).
    /// </summary>
    internal static MemberInfo[] GetMembers(Type type, MemberSelection selection)
    {
      ConcurrentDictionary<Type, MemberInfo[]> cache = MembersCache[selection.Index];
      if (cache.TryGetValue(type, out MemberInfo[] members))
        return members;
      return cache.GetOrAdd(type, FindMembers(type, selection));
    }

    private static MemberInfo[] FindMembers(Type type, MemberSelection selection)
    {
      List<MemberInfo> all = new List<MemberInfo>();
      bool fieldStruct = SerializationRules.IsFieldStruct(type);
      if (!fieldStruct) // the fields are its state, its properties are computed from them (Matrix4x4.Translation)
      {
        all.AddRange(AllProperties(type));
        if (selection.NonPublic != NonPublicProperties.None)
          all.AddRange(NonPublicCandidates(type, selection.NonPublic));
      }
      AddFieldCandidates(type, fieldStruct ? SerializedFields.Public : selection.Fields, all);

      List<MemberInfo> kept = new List<MemberInfo>(all.Count);
      for (int i = 0; i < all.Count; i++)
      {
        MemberInfo member = all[i];
        if (member is PropertyInfo property && property.GetIndexParameters().Length > 0)
          continue;

        bool hidden = false;
        for (int j = 0; j < all.Count; j++)
        {
          if (j != i && all[j].Name == member.Name && all[j].DeclaringType != member.DeclaringType && member.DeclaringType.IsAssignableFrom(all[j].DeclaringType))
          {
            hidden = true;
            break;
          }
        }

        if (!hidden)
          kept.Add(member);
      }
      return kept.ToArray();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private static PropertyInfo[] AllProperties(Type type)
    {
      return type.GetProperties();
    }

    /// <summary>
    /// The non-public instance properties of the type and its base classes (the class's own first, each in the order of reflection) that <paramref name="nonPublic"/> includes
    /// (see <see cref="NonPublicProperties"/>): not explicit interface implementations (their names have dots), not generated by the compiler (a record's EqualityContract), not declared by the framework.
    /// Properties with a public getter or setter are not among them (Type.GetProperties() returns those).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    internal static List<PropertyInfo> NonPublicCandidates(Type type, NonPublicProperties nonPublic)
    {
      List<PropertyInfo> found = new List<PropertyInfo>();
      if (nonPublic == NonPublicProperties.None)
        return found;
      for (Type declaring = type; declaring != null && declaring != typeof(object); declaring = declaring.BaseType) // private properties of base classes are not returned for the derived class
      {
        if (TypeResolver.IsFrameworkAssembly(declaring.Assembly))
          continue;
        PropertyInfo[] declared = declaring.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int t = 0; t < declared.Length; t++)
        {
          PropertyInfo prop = declared[t];
          if (prop.Name.IndexOf('.') >= 0 || prop.IsDefined(typeof(CompilerGeneratedAttribute), false)
            || (nonPublic == NonPublicProperties.Attributed && !AttributesOf(prop).OptsIn))
            continue;
          found.Add(prop);
        }
      }
      return found;
    }

    /// <summary>
    /// Adds the instance fields of the type and its base classes that <paramref name="fields"/> includes (see <see cref="SerializedFields"/>): first the public ones, then the non-public ones
    /// (as the properties), each pass the class's own first, in the order of reflection (metadata order).
    /// Not the fields the compiler generates (backing fields of auto-properties and events, <c>&lt;X&gt;k__BackingField</c>, VB's static locals and anonymous types: <see cref="IsCompilerName"/>), fixed-size buffers, pointers, ref structs and fields declared by the framework
    /// (except those of a framework struct whose state they are, <see cref="SerializationRules.IsFieldStruct"/>: the caller passes <see cref="SerializedFields.Public"/> for it).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    internal static void AddFieldCandidates(Type type, SerializedFields fields, List<MemberInfo> found)
    {
      if (fields == SerializedFields.None)
        return;
      bool fieldStruct = SerializationRules.IsFieldStruct(type);
      for (int pass = 0; pass < 2; pass++)
      {
        bool publicPass = pass == 0;
        for (Type declaring = type; declaring != null && declaring != typeof(object) && declaring != typeof(ValueType); declaring = declaring.BaseType) // private fields of base classes are not returned for the derived class
        {
          if (!fieldStruct && TypeResolver.IsFrameworkAssembly(declaring.Assembly))
            continue;
          FieldInfo[] declared = declaring.GetFields(BindingFlags.Instance | (publicPass ? BindingFlags.Public : BindingFlags.NonPublic) | BindingFlags.DeclaredOnly);
          for (int t = 0; t < declared.Length; t++)
          {
            FieldInfo field = declared[t];
            if (IsFieldCandidate(field, fieldStruct ? SerializedFields.Public : fields))
              found.Add(field);
          }
          if (fieldStruct)
            return; // only its own public fields (a struct has no base class with fields)
        }
      }
    }

    private static bool IsFieldCandidate(FieldInfo field, SerializedFields fields)
    {
      if (field.IsStatic || field.IsLiteral || IsCompilerName(field.Name) || field.IsDefined(typeof(CompilerGeneratedAttribute), false) || field.IsDefined(typeof(FixedBufferAttribute), false))
        return false;
      Type type = field.FieldType;
      if (type.IsPointer || type.IsByRef || SerializationRules.IsByRefLike(type) || IsEventField(field))
        return false;

      switch (fields)
      {
        case SerializedFields.All:
          return true;
        case SerializedFields.Public:
          return field.IsPublic || AttributesOf(field).OptsIn;
        case SerializedFields.Attributed:
          return AttributesOf(field).OptsIn;
        default:
          return false;
      }
    }

    /// <summary>
    /// A name no source can declare, which only the compiler gives: C#'s have &lt; (<c>&lt;X&gt;k__BackingField</c>, <c>&lt;X&gt;i__Field</c>), VB's $ (a <c>Static</c> local of an instance method is the field
    /// <c>$STATIC$Method$2008$x</c>, with an initializer also <c>...$x$Init</c> of the type StaticLocalInitFlag; anonymous types' <c>$X</c>), neither marked [CompilerGenerated].
    /// </summary>
    internal static bool IsCompilerName(string name)
    {
      return name.IndexOf('<') >= 0 || name.IndexOf('$') >= 0;
    }

    /// <summary>
    /// The field of a field-like event (<c>public event EventHandler Changed;</c>): a field of the delegate type with the name of the event (the compiler does not always mark it as generated).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    private static bool IsEventField(FieldInfo field)
    {
      return typeof(Delegate).IsAssignableFrom(field.FieldType)
        && field.DeclaringType.GetEvent(field.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null;
    }

    /// <summary>
    /// The non-public properties and the fields the settings serialize: those of the <see cref="FilterNonSettable"/> of their static filters, <see cref="MemberSelection.Default"/> without one.
    /// </summary>
    internal static MemberSelection SelectionOf(MsgPackOptions settings)
    {
      return GetSharedPropsCaches(settings).Selection;
    }

    // One cache per PropertyOrder and MemberSelection (the enum values are indexes, checked by MsgPackOptions.PropertyOrder and FilterNonSettable), Reflection uses MembersCache
    private static readonly ConcurrentDictionary<Type, MemberInfo[]>[] OrderedMembersCache = CachePer<MemberInfo[]>(((int)MsgPackOptions.LastPropertyOrder + 1) * MemberSelection.Count);

    private static ConcurrentDictionary<Type, T>[] CachePerOrder<T>()
    {
      return CachePer<T>((int)MsgPackOptions.LastPropertyOrder + 1);
    }

    private static ConcurrentDictionary<Type, T>[] CachePer<T>(int count)
    {
      ConcurrentDictionary<Type, T>[] caches = new ConcurrentDictionary<Type, T>[count];
      for (int t = 0; t < caches.Length; t++)
        caches[t] = new ConcurrentDictionary<Type, T>();
      return caches;
    }

    /// <summary>
    /// The members of <see cref="GetMembers(Type, MemberSelection)"/> in the order of the settings (see <see cref="PropertyOrder"/>), with the non-public properties and the fields of their static filters.
    /// </summary>
    internal static MemberInfo[] GetMembers(Type type, MsgPackOptions settings)
    {
      PropertyOrder order = settings._propertyOrder;
      MemberSelection selection = SelectionOf(settings);
      if (order == PropertyOrder.Reflection)
        return GetMembers(type, selection);

      ConcurrentDictionary<Type, MemberInfo[]> cache = OrderedMembersCache[(int)order * MemberSelection.Count + selection.Index];
      if (cache.TryGetValue(type, out MemberInfo[] sorted))
        return sorted;
      return cache.GetOrAdd(type, Sort(GetMembers(type, selection), order));
    }

    private static MemberInfo[] Sort(MemberInfo[] members, PropertyOrder order)
    {
      OrderKey[] keys = new OrderKey[members.Length];
      for (int t = 0; t < members.Length; t++)
        keys[t] = new OrderKey(members[t], t, order);

      Array.Sort(keys);

      MemberInfo[] sorted = new MemberInfo[members.Length];
      for (int t = 0; t < keys.Length; t++)
        sorted[t] = keys[t].Member;
      return sorted;
    }

    /// <summary>
    /// The position of a member for a <see cref="PropertyOrder"/>, compared field by field. The reflection index comes last so the sort is stable.
    /// <para>Within a class the properties come before the fields: properties and fields are numbered in separate metadata tables, so the order of the source between them cannot be known.</para>
    /// </summary>
    private struct OrderKey : IComparable<OrderKey>
    {
      internal readonly MemberInfo Member;
      private readonly bool _byName;
      private readonly int _explicitOrder; // int.MaxValue: none (after the ones that have one)
      private readonly string _typeName; // TypeThenDeclaration, null otherwise
      private readonly int _depth; // of the class declaring the (overridden) property, base classes first
      private readonly int _kind; // 0: property, 1: field
      private readonly int _token;
      private readonly int _index;

      internal OrderKey(MemberInfo member, int index, PropertyOrder order)
      {
        Member = member;
        _index = index;
        _byName = order == PropertyOrder.Alphabetical;
        _explicitOrder = int.MaxValue;
        PropertyInfo property = member as PropertyInfo;
        _typeName = order == PropertyOrder.TypeThenDeclaration ? (property?.PropertyType ?? ((FieldInfo)member).FieldType).ToString() : null; // FullName would contain the assemblies of generic arguments, which differ per runtime
        _depth = 0;
        _kind = 0;
        _token = 0;

        if (_byName)
          return;

        if (order == PropertyOrder.Explicit)
        {
          // Attribute.GetCustomAttribute also looks at the overridden properties (PropertyInfo.GetCustomAttributes ignores inherit)
          DataMemberAttribute dataMember = (DataMemberAttribute)Attribute.GetCustomAttribute(member, typeof(DataMemberAttribute), true);
          if (dataMember != null && dataMember.Order >= 0)
            _explicitOrder = dataMember.Order;
        }

        MemberInfo declared = property is null ? member : OriginalDeclaration(property); // a field overrides nothing
        _depth = Depth(declared.DeclaringType);
        _kind = property is null ? 1 : 0;
        try
        {
          _token = declared.MetadataToken;
        }
        catch (InvalidOperationException) // no metadata (e.g. a type being built with Reflection.Emit), keep the reflection order within its class
        {
          _token = int.MaxValue;
        }
      }

      public int CompareTo(OrderKey other)
      {
        if (_explicitOrder != other._explicitOrder)
          return _explicitOrder.CompareTo(other._explicitOrder);
        if (_typeName != null)
        {
          int byType = string.CompareOrdinal(_typeName, other._typeName);
          if (byType != 0)
            return byType;
        }
        if (_depth != other._depth)
          return _depth.CompareTo(other._depth);
        if (_kind != other._kind)
          return _kind.CompareTo(other._kind);
        if (_token != other._token)
          return _token.CompareTo(other._token);
        if (_byName)
        {
          int byName = string.CompareOrdinal(Member.Name, other.Member.Name);
          if (byName != 0)
            return byName;
        }
        return _index.CompareTo(other._index);
      }

      /// <summary>
      /// The property an override overrides (recursively), so overriding a property in a derived class does not move it.
      /// </summary>
      [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
      private static PropertyInfo OriginalDeclaration(PropertyInfo property)
      {
        MethodInfo accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
        if (accessor is null)
          return property;

        Type original = accessor.GetBaseDefinition().DeclaringType;
        if (original is null || original == property.DeclaringType)
          return property;

        try
        {
          return original.GetProperty(property.Name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ?? property;
        }
        catch (AmbiguousMatchException)
        {
          return property;
        }
      }

      private static int Depth(Type type)
      {
        int depth = 0;
        for (Type baseType = type?.BaseType; baseType != null; baseType = baseType.BaseType)
          depth++;
        return depth;
      }
    }

    internal static FullPropertyInfo[] GetSerializedProps(Type type, MsgPackOptions settings)
    {
      Dictionary<Type, FullPropertyInfo[]> sessionCache = settings._serializedPropsCache;
      if (sessionCache is null)
        return GetSerializedPropsWithoutSession(type, settings);

      if (sessionCache.TryGetValue(type, out FullPropertyInfo[] cached))
        return cached;

      if (settings._schemaFrozen) // a shared session, see SchemaSession
        throw SchemaGrowthException.Instance;

      // Within a session each property is resolved once: first the static filters, then the ids of the properties that are kept.
      // The indexed schema asks for the kept properties of the type (see GetStaticallyIncludedProps) when resolving the first id.
      FullPropertyInfo[] props = GetStaticallyIncludedProps(type, settings);
      IMsgPackPropertyIdResolver[] resolvers = settings._propertyNameResolvers;
      if (resolvers.Length == 1 && resolvers[0] is IndexedSchemaTypeResolver schema && schema.TryAssignIds(type, props, settings))
      {
        // The ids are 0 to n-1 (unique, see TryAssignIds)
        sessionCache[type] = props;
        return props;
      }

      if (props.Length > 0 && ReferenceEquals(props, SharedIndexed(type, settings))) // the ids are not the positions (reading data with other names): this session's own instances
        settings._staticPropsCache[type] = props = Copies(GetStaticallyKept(type, settings));

      for (int t = 0; t < props.Length; t++)
        ResolvePropertyId(props[t], settings);
      ThrowIfIdsNotUnique(type, props);

      sessionCache[type] = props;
      return props;
    }

    /// <summary>
    /// The serialized properties per type, one dictionary per <see cref="PropertyOrder"/>, for one array of static filters.
    /// <para>Without property id resolvers the FullPropertyInfo instances are shared by all settings (see GetFullPropInfo), so the result only depends on the type, the order and the static filters.
    /// The filters are compared by reference (like SchemaStore.SessionKey): settings with another array get their own cache, an array that is changed in place keeps its old results.</para>
    /// </summary>
    internal sealed class SharedPropsCache
    {
      internal readonly IMsgPackPropertyIncludeStatically[] Filters;
      internal readonly ConcurrentDictionary<Type, FullPropertyInfo[]>[] PerOrder = CachePerOrder<FullPropertyInfo[]>();

      /// <summary>
      /// What the filters keep with the name as id (see <see cref="GetStaticallyIncludedProps"/>), the sessions get copies.
      /// </summary>
      internal readonly ConcurrentDictionary<Type, FullPropertyInfo[]>[] StaticPerOrder = CachePerOrder<FullPropertyInfo[]>();

      /// <summary>
      /// What <see cref="StaticPerOrder"/> keeps, as copies with their positions as ids, for the sessions whose only property id resolver is the indexed schema
      /// (see <see cref="GetStaticallyIncludedProps"/>), null for a type whose properties do not all have unique names.
      /// </summary>
      internal readonly ConcurrentDictionary<Type, FullPropertyInfo[]>[] IndexedPerOrder = CachePerOrder<FullPropertyInfo[]>();

      /// <summary>
      /// The non-public properties and the fields of the <see cref="FilterNonSettable"/> among the filters (the last one, as the filters are asked from last to first), <see cref="MemberSelection.Default"/> without one.
      /// </summary>
      internal readonly MemberSelection Selection = MemberSelection.Default;

      internal SharedPropsCache(IMsgPackPropertyIncludeStatically[] filters)
      {
        Filters = filters;
        for (int t = 0; t < filters.Length; t++)
          if (filters[t] is FilterNonSettable nonSettable)
            Selection = new MemberSelection(nonSettable.NonPublicProperties, nonSettable.Fields);
      }
    }

    // Weak keys: arrays of filters that are no longer used (e.g. settings created per call) take their caches with them
    private static readonly ConditionalWeakTable<IMsgPackPropertyIncludeStatically[], SharedPropsCache> SharedPropsCaches = new ConditionalWeakTable<IMsgPackPropertyIncludeStatically[], SharedPropsCache>();
    private static readonly IMsgPackPropertyIncludeStatically[] NoStaticFilters = new IMsgPackPropertyIncludeStatically[0];

    private static SharedPropsCache GetSharedPropsCaches(MsgPackOptions settings)
    {
      // The settings remember the cache of their filters, so the table is only consulted when the filters change (or for new settings)
      IMsgPackPropertyIncludeStatically[] filters = settings._staticFilters ?? NoStaticFilters;
      SharedPropsCache cache = settings._sharedPropsCache;
      if (cache is null || !ReferenceEquals(cache.Filters, filters))
        settings._sharedPropsCache = cache = SharedPropsCaches.GetValue(filters, f => new SharedPropsCache(f));
      return cache;
    }

    private static ConcurrentDictionary<Type, FullPropertyInfo[]> GetSharedPropsCache(MsgPackOptions settings)
    {
      return GetSharedPropsCaches(settings).PerOrder[(int)settings._propertyOrder];
    }

    private static FullPropertyInfo[] GetSerializedPropsWithoutSession(Type type, MsgPackOptions settings)
    {
      bool shared = settings._propertyNameResolvers is null || settings._propertyNameResolvers.Length == 0;
      ConcurrentDictionary<Type, FullPropertyInfo[]> sharedCache = null;
      if (shared)
      {
        sharedCache = GetSharedPropsCache(settings);
        if (sharedCache.TryGetValue(type, out FullPropertyInfo[] cached))
          return cached;
      }

      MemberInfo[] props = GetMembers(type, settings);
      List<FullPropertyInfo> keptProps = new List<FullPropertyInfo>(props.Length);
      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo full = FullPropertyInfo.GetFullPropInfo(props[t], settings);
        bool keep = IsStaticallyIncluded(full, settings);
        if (!shared)
          full.StaticallyIgnored = !keep;
        if (keep)
          keptProps.Add(full);
      }

      FullPropertyInfo[] result = keptProps.ToArray();
      ThrowIfIdsNotUnique(type, result); // custom resolvers are consulted for every call (they may have their own cache), so are their ids

      if (shared)
        return sharedCache.GetOrAdd(type, result);
      return result;
    }

    /// <summary>
    /// The property ids are the keys of the map an object is serialized to, the serializer does not check them for every object.
    /// </summary>
    /// <exception cref="MsgPackException">When two properties have the same id, or an id is a key the serializer uses for the type id or the content of a collection</exception>
    private static void ThrowIfIdsNotUnique(Type type, FullPropertyInfo[] props)
    {
      for (int t = 0; t < props.Length; t++)
      {
        object id = props[t].PropertyId;
        if (MsgPackOptions.TypeIdKey.Equals(id) || MsgPackOptions.ContentKey.Equals(id))
          throw new MsgPackException($"The id \"{id}\" of {props[t].Kind} {type.Name}.{props[t].Name} is reserved, the serializer uses it for the type id or the content of a collection.");

        for (int i = 0; i < t; i++)
        {
          if (Equals(props[i].PropertyId, id))
            throw new MsgPackException($"The {props[i].Kind} {type.Name}.{props[i].Name} and the {props[t].Kind} {type.Name}.{props[t].Name} have the same id \"{id}\", the keys of a map must be unique.");
        }
      }
    }

    /// <summary>
    /// The properties that pass the static filters, their <see cref="PropertyId"/> is the name of the property (the property id resolvers are not consulted).
    /// <para>Cached for the rest of the session (see <see cref="MsgPackSettings._staticPropsCache"/>), <see cref="GetSerializedProps"/> resolves the ids of the same instances.</para>
    /// </summary>
    internal static FullPropertyInfo[] GetStaticallyIncludedProps(Type type, MsgPackOptions settings)
    {
      Dictionary<Type, FullPropertyInfo[]> sessionCache = settings._staticPropsCache;
      if (sessionCache != null && sessionCache.TryGetValue(type, out FullPropertyInfo[] cached))
        return cached;

      if (settings._schemaFrozen) // a shared session, see SchemaSession
        throw SchemaGrowthException.Instance;

      // The filters decide per property (they are immutable, see SharedPropsCache), so they run once per type and filter array; each session gets its own instances (it sets its ids on them),
      // except that sessions with the indexed schema as their only property id resolver share instances with the positions as ids: the ids TryAssignIds gives in every such session
      // that has the same names in its entry (any session writing, and reading the writer's properties). GetSerializedProps replaces them by copies for the others.
      FullPropertyInfo[] result = sessionCache is null ? null : SharedIndexed(type, settings);
      if (result is null)
        result = Copies(GetStaticallyKept(type, settings));

      if (sessionCache != null)
        sessionCache[type] = result;
      return result;
    }

    private static FullPropertyInfo[] GetStaticallyKept(Type type, MsgPackOptions settings)
    {
      ConcurrentDictionary<Type, FullPropertyInfo[]> shared = GetSharedPropsCaches(settings).StaticPerOrder[(int)settings._propertyOrder];
      if (!shared.TryGetValue(type, out FullPropertyInfo[] kept))
        kept = shared.GetOrAdd(type, FilterStatically(type, settings));
      return kept;
    }

    private static FullPropertyInfo[] Copies(FullPropertyInfo[] kept)
    {
      FullPropertyInfo[] copies = new FullPropertyInfo[kept.Length];
      for (int t = 0; t < kept.Length; t++)
        copies[t] = new FullPropertyInfo(kept[t]);
      return copies;
    }

    /// <summary>
    /// The statically included properties with their positions as ids, shared by the sessions whose only property id resolver is the indexed schema (see GetStaticallyIncludedProps),
    /// null for other settings and when two properties have the same name (TryAssignIds refuses them).
    /// </summary>
    private static FullPropertyInfo[] SharedIndexed(Type type, MsgPackOptions settings)
    {
      IMsgPackPropertyIdResolver[] resolvers = settings._propertyNameResolvers;
      if (resolvers is null || resolvers.Length != 1 || !(resolvers[0] is IndexedSchemaTypeResolver))
        return null;

      ConcurrentDictionary<Type, FullPropertyInfo[]> indexed = GetSharedPropsCaches(settings).IndexedPerOrder[(int)settings._propertyOrder];
      if (indexed.TryGetValue(type, out FullPropertyInfo[] props))
        return props;

      FullPropertyInfo[] kept = GetStaticallyKept(type, settings);
      props = Copies(kept);
      for (int t = 0; t < props.Length && props != null; t++)
      {
        if (props[t]._uniqueName)
          props[t].PropertyId = IndexedSchemaTypeResolver.BoxedId(t);
        else
          props = null;
      }
      return indexed.GetOrAdd(type, props);
    }

    /// <summary>
    /// The properties of an object written as a map of names (an object with an anonymous type, see SerializationRules.WritesByNames), their <see cref="PropertyId"/> is the name:
    /// the string id a property id resolver other than the indexed schema gives (as <see cref="SchemaNameOf"/>), otherwise the .NET name. The indexed schema is not asked, the type gets no entry.
    /// <para>Without such resolvers the instances the static filters kept for all settings (not changed by sessions), otherwise new instances on every call (as custom resolvers are asked without the schema).</para>
    /// </summary>
    internal static FullPropertyInfo[] GetNamedProps(Type type, MsgPackOptions settings)
    {
      ConcurrentDictionary<Type, FullPropertyInfo[]> shared = GetSharedPropsCaches(settings).StaticPerOrder[(int)settings._propertyOrder];
      if (!shared.TryGetValue(type, out FullPropertyInfo[] kept))
        kept = shared.GetOrAdd(type, FilterStatically(type, settings));

      IMsgPackPropertyIdResolver[] resolvers = settings._propertyNameResolvers;
      bool named = false;
      for (int t = resolvers.Length - 1; t >= 0 && !named; t--)
        named = !(resolvers[t] is IndexedSchemaTypeResolver);
      if (!named)
        return kept;

      FullPropertyInfo[] props = new FullPropertyInfo[kept.Length];
      for (int t = 0; t < kept.Length; t++)
      {
        FullPropertyInfo prop = new FullPropertyInfo(kept[t]);
        prop.PropertyId = SchemaNameOf(prop, settings);
        props[t] = prop;
      }
      ThrowIfIdsNotUnique(type, props);
      return props;
    }

    private static FullPropertyInfo[] FilterStatically(Type type, MsgPackOptions settings)
    {
      MemberInfo[] props = GetMembers(type, settings);
      List<FullPropertyInfo> keptProps = new List<FullPropertyInfo>(props.Length);
      for (int t = 0; t < props.Length; t++)
      {
        FullPropertyInfo full = new FullPropertyInfo(props[t]) { PropertyId = props[t].Name };
        if (IsStaticallyIncluded(full, settings))
        {
          full._accessor = PropertyAccessor.Get(props[t]); // shared by the copies (the accessors are global anyway)
          keptProps.Add(full);
        }
      }

      for (int t = 0; t < keptProps.Count; t++)
      {
        bool unique = true;
        for (int i = 0; i < keptProps.Count && unique; i++)
          unique = i == t || !string.Equals(keptProps[i].Name, keptProps[t].Name, StringComparison.Ordinal);
        keptProps[t]._uniqueName = unique;
      }
      return keptProps.ToArray();
    }

    // Not cached on the property: the shared instances are used with different filters, the callers cache the kept properties
    private static bool IsStaticallyIncluded(FullPropertyInfo full, MsgPackOptions settings)
    {
      IMsgPackPropertyIncludeStatically[] filters = settings._staticFilters;
      if (filters is null)
        return true;

      for (int i = filters.Length - 1; i >= 0; i--)
        if (!filters[i].IncludeProperty(full))
          return false;
      return true;
    }

  }
}
