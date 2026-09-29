using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text;

namespace LsMsgPack
{
  /// <summary>
  /// The settings that decide the format (shared by the serializers): <see cref="UseInexedSchema"/>, <see cref="DynamicallyCompact"/>, <see cref="EndianAction"/>, type ids, resolvers, filters and the <see cref="SchemaStore"/>.
  /// <para>Each serializer derives its own settings from it (LsMsgPack: MsgPackSettings). The resolvers and filters get these options, so they work with any of them.</para>
  /// </summary>
  public abstract class MsgPackOptions
  {

    #region Default settings

    /// <summary>
    /// Use the <see cref="TypeResolving.Types.IndexedSchemaTypeResolver"/> to compact repetitive type and property names
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_UseInexedSchema { get; set; } = true;

    /// <summary>
    /// When true (default) will dynamically use the smallest possible datatype that the value fits in. When false, will always use the predefined type of integer.
    /// </summary>
    /// <remarks>
    /// Only affects writing
    /// </remarks>
    [IgnoreDataMember]
    public static bool Default_DynamicallyCompact { get; set; } = true;

    /// <summary>
    /// The MsgPack specification explicitly states that it is a big-endian format, so by default we will reorder bytes of many types on little endian systems. Some implementations of MsgPack may ignore the endianness, so for this reason you can override the swapping action in order to correct the faulty endianness.
    /// </summary>
    [IgnoreDataMember]
    public static EndianAction Default_EndianAction { get; set; } = EndianAction.SwapIfCurrentSystemIsLittleEndian;

    /// <summary>
    /// Support type-hierarchy's where a property or collection can contain items of a base-type or interface with multiple implementations (eg. a list of IPet where a pet can be a dog, cat or fish etc...)
    /// <para>Using the full name will allow faster deserialization (less searching through assemblies) but obviously results in a much larger payload.</para>
    /// <para>Lookup speed my be increased when property types and their value types reside in the same assembly (i.e. when "interface IPet" and "class Dog" are defined in the same project).</para>
    /// <para>Alternatively or in addition, IMsgPackTypeResolver can be implemented.</para>
    /// <para>Defining a property with the type "Object" will probably take significantly longer to deserialize and may pose a false match when FullName is not true.</para>
    /// </summary>
    [IgnoreDataMember]
    public static AddTypeIdOption Default_AddTypeIdOptions { get; set; } = AddTypeIdOption.IfAmbiguious;

    /// <summary>
    /// Custom type resolvers can be added, only needed if using object-models with polymorphic properties (base types or interfaces that have multiple implementations).
    /// <para>
    /// There is a <see cref="TypeResolving.Types.WildGooseChaseResolver">WildGooseChaseResolver</see> that can be used while developing, but it is not recomended for production!
    /// <code>
    /// MsgPackSerializer.TypeResolvers.Add(new TypeResolving.WildGooseChaseResolver());
    /// </code>
    /// </para>
    /// <para>
    /// In order to keep a minimal payload and best performance, implement a custom IMsgPackTypeIdentifier
    /// </para>
    /// </summary>
    public static IMsgPackTypeResolver[] Default_TypeResolvers = new IMsgPackTypeResolver[0];

    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>FilterIgnoredAttribute</item>
    /// </list>
    /// </summary>
    public static IMsgPackPropertyIncludeStatically[] Default_StaticFilters = new IMsgPackPropertyIncludeStatically[]{
            new FilterNonSettable(),
            new FilterIgnoredAttribute()
        };

    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>FilterDefaultValues</item>
    /// <item>FilterNullValues</item>
    /// </list>
    /// </summary>
    public static IMsgPackPropertyIncludeDynamically[] Default_DynamicFilters = new[] { new FilterDefaultValues() };

    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>AttributePropertyNameResolver</item>
    /// </list>
    /// </summary>
    public static IMsgPackPropertyIdResolver[] Default_PropertyNameResolvers = new IMsgPackPropertyIdResolver[0];

    /// <summary>
    /// The <see cref="SchemaStore"/> of new settings (null by default).
    /// </summary>
    [IgnoreDataMember]
    public static SchemaStore Default_SchemaStore { get; set; } = null;

    /// <summary>
    /// The <see cref="WriteSchemaReference"/> of new settings (false by default).
    /// </summary>
    public static bool Default_WriteSchemaReference { get; set; } = false;

    #endregion

    /// <summary>
    /// Property values are read and written by typed delegates bound to the get and set methods (once a property has been used a number of times) instead of reflection.
    /// <para>This applies to all settings, set it before (de)serializing. It is ignored when the runtime does not compile code (checked in the .NET Standard 2.1 build),
    /// switch it off on an AOT platform that uses the .NET Standard 2.0 build (the delegates need generic types that are created at runtime).</para>
    /// </summary>
    [IgnoreDataMember]
    public static bool CompilePropertyAccessors { get; set; } = true;

    internal bool FileContainsErrors = false;
    internal bool _useInexedSchema = Default_UseInexedSchema;
    internal bool _dynamicallyCompact = Default_DynamicallyCompact;
    internal EndianAction _endianAction = Default_EndianAction;
    internal AddTypeIdOption _addTypeIdOptions = Default_AddTypeIdOptions;

    internal IMsgPackTypeResolver[] _typeResolvers = Default_TypeResolvers;
    internal IMsgPackPropertyIncludeStatically[] _staticFilters = Default_StaticFilters;
    internal IMsgPackPropertyIncludeDynamically[] _dynamicFilters = Default_DynamicFilters;
    internal IMsgPackPropertyIdResolver[] _propertyNameResolvers = Default_PropertyNameResolvers;
    internal SchemaStore _schemaStore = Default_SchemaStore;
    internal bool _writeSchemaReference = Default_WriteSchemaReference;

    /// <summary>
    /// The session caches (<see cref="_serializedPropsCache"/>, <see cref="_staticPropsCache"/> and the schema) are shared by several calls and must not change (see <see cref="SchemaSession"/>). Not copied by <see cref="Clone"/>.
    /// </summary>
    internal bool _schemaFrozen;

    /// <summary>
    /// Serialized properties per type (see <see cref="Meta.FullPropertyInfo.GetSerializedProps(Type, MsgPackOptions)"/>), only for the settings of a single (de)serialization session with the indexed schema.
    /// <para>The property ids are indexes into that session's schema, so they cannot be shared with other sessions. Not copied by <see cref="Clone"/>.</para>
    /// </summary>
    internal Dictionary<Type, Meta.FullPropertyInfo[]> _serializedPropsCache;

    /// <summary>
    /// The properties per type that pass the static filters, before their ids are resolved (see <see cref="Meta.FullPropertyInfo.GetStaticallyIncludedProps"/>).
    /// <para>Only for the settings of a single session with the indexed schema, like <see cref="_serializedPropsCache"/>. Not copied by <see cref="Clone"/>.</para>
    /// </summary>
    internal Dictionary<Type, Meta.FullPropertyInfo[]> _staticPropsCache;

    /// <summary>
    /// Uses a micro schema (dictionary with type-name as key and an array of the types property names as value. The index of the name will be referenced from the serialized body (instead of the full name)
    /// </summary>
    [Category("Control")]
    [DisplayName("Use Indexed Schema")]
    [Description("Uses a micro schema (dictionary with type-name as key and an array of the types property names as value. The index of the name will be referenced from the serialized body (instead of the full name)")]
    [DefaultValue(true)]
    public bool UseInexedSchema
    {
      get { return _useInexedSchema; }
      set { _useInexedSchema = value; }
    }

    /// <summary>
    /// When true (default) will dynamically use the smallest possible datatype that the value fits in. When false, will always use the predefined type of integer.
    /// </summary>
    /// <remarks>
    /// Only affects writing
    /// </remarks>
    [Category("Control")]
    [DisplayName("Dynamically Compact")]
    [Description("When true (default) will dynamically use the smallest possible datatype that the value fits in. When false, will use the predefined type of integer.")]
    [DefaultValue(true)]
    public bool DynamicallyCompact
    {
      get { return _dynamicallyCompact; }
      set { _dynamicallyCompact = value; }
    }

    /// <summary>
    /// The MsgPack specification explicitly states that it is a big-endian format, so by default we will reorder bytes of many types on little endian systems. Some implementations of MsgPack may ignore the endianness, so for this reason you can override the swapping action in order to correct the faulty endianness.
    /// </summary>
    [Category("Control")]
    [DisplayName("System Endian handling")]
    [Description("The MsgPack specification explicitly states that it is a big-endian format, so by default we will reorder bytes of many types on little endian systems. Some implementations of MsgPack may ignore the endianness, so for this reason you can override the swapping action in order to correct the faulty endianness.")]
    [DefaultValue(EndianAction.SwapIfCurrentSystemIsLittleEndian)]
    public EndianAction EndianAction
    {
      get { return _endianAction; }
      set { _endianAction = value; }
    }

    /// <summary>
    /// Support type-hierarchy's where a property or collection can contain items of a base-type or interface with multiple implementations (eg. a list of IPet where a pet can be a dog, cat or fish etc...)
    /// <para>Using the full name will allow faster deserialization (less searching through assemblies) but obviously results in a much larger payload.</para>
    /// <para>Lookup speed my be increased when property types and their value types reside in the same assembly (i.e. when "interface IPet" and "class Dog" are defined in the same project).</para>
    /// <para>Alternatively or in addition, IMsgPackTypeResolver can be implemented.</para>
    /// <para>Defining a property with the type "Object" will probably take significantly longer to deserialize and may pose a false match when FullName is not true.</para>
    /// </summary>
    [Category("OOP")]
    [DisplayName("Add type name")]
    [Description("Support type-hierarchy's where a property or collection can contain items of a base-type or interface with multiple implementations (eg. a list of IPet where a pet can be a dog, cat or fish etc...)")]
    [DefaultValue(true)]
    public AddTypeIdOption AddTypeIdOptions
    {
      get { return _addTypeIdOptions; }
      set { _addTypeIdOptions = value; }
    }


    /// <summary>
    /// Custom type resolvers can be added, only needed if using object-models with polymorphic properties (base types or interfaces that have multiple implementations).
    /// <para>
    /// There is a <see cref="TypeResolving.Types.WildGooseChaseResolver">WildGooseChaseResolver</see> that can be used while developing, but it is not recomended for production!
    /// <code>
    /// MsgPackSerializer.TypeResolvers.Add(new TypeResolving.WildGooseChaseResolver());
    /// </code>
    /// </para>
    /// <para>
    /// In order to keep a minimal payload and best performance, implement a custom IMsgPackTypeIdentifier
    /// </para>
    /// </summary>
    public IMsgPackTypeResolver[] TypeResolvers { get { return _typeResolvers; } set { _typeResolvers = value; } }


    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>FilterIgnoredAttribute</item>
    /// </list>
    /// </summary>
    public IMsgPackPropertyIncludeStatically[] StaticFilters { get { return _staticFilters; } set { _staticFilters = value; } }


    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>FilterDefaultValues</item>
    /// <item>FilterNullValues</item>
    /// </list>
    /// </summary>
    public IMsgPackPropertyIncludeDynamically[] DynamicFilters { get { return _dynamicFilters; } set { _dynamicFilters = value; } }


    /// <summary>
    /// Available:
    /// <list type="bullet">
    /// <item>AttributePropertyNameResolver</item>
    /// <item>IndexedSchemaTypeResolver</item>
    /// </list>
    /// </summary>
    public IMsgPackPropertyIdResolver[] PropertyNameResolvers { get { return _propertyNameResolvers; } set { _propertyNameResolvers = value; } }


    /// <summary>
    /// Keeps the indexed schemas between calls (null by default). Share one store between settings and threads, <see cref="Clone"/> keeps the same store.
    /// <para>Reading: data with a schema reference (see <see cref="WriteSchemaReference"/>) needs the store, data with the schema inline is read faster when its schema was seen before (<see cref="SchemaStore.CacheInlineSchemas"/>).</para>
    /// </summary>
    [IgnoreDataMember]
    public SchemaStore SchemaStore { get { return _schemaStore; } set { _schemaStore = value; } }

    /// <summary>
    /// Write a reference to the schema (18 bytes, see <see cref="SchemaStore"/>) instead of the schema itself, false by default. Needs <see cref="UseInexedSchema"/> and a <see cref="SchemaStore"/>.
    /// <para>The reader needs the schema in its <see cref="SchemaStore"/>, other MsgPack libraries cannot read such data.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Write Schema Reference")]
    [Description("Write a reference to the schema (kept in the SchemaStore) instead of the schema itself. The reader needs the schema in its own SchemaStore.")]
    [DefaultValue(false)]
    public bool WriteSchemaReference
    {
      get { return _writeSchemaReference; }
      set { _writeSchemaReference = value; }
    }

    /// <summary>
    /// A copy of these options (of the same derived type), without the caches of a session (see <see cref="SchemaSession"/>).
    /// </summary>
    internal MsgPackOptions CloneOptions()
    {
      MsgPackOptions copy = (MsgPackOptions)MemberwiseClone();
      copy._serializedPropsCache = null;
      copy._staticPropsCache = null;
      copy._schemaFrozen = false;
      return copy;
    }

    /// <summary>
    /// Map key holding the type identifier.
    /// </summary>
    internal const string TypeIdKey = "";

    /// <summary>
    /// Map key holding the packed value of a wrapped item (collections, dictionaries or values that needed a type identifier).
    /// </summary>
    internal const string ContentKey = "@";

    /// <summary>
    /// The encoding of strings (LsMsgPack: MpString.DefaultEncoding), UTF-8 unless changed.
    /// </summary>
    internal static Encoding StringEncoding = Encoding.UTF8;

    /// <summary>
    /// Whether values of <paramref name="length"/> bytes are reversed (see <see cref="EndianAction"/>).
    /// </summary>
    internal static bool SwapEndianChoice(MsgPackOptions settings, int length)
    {
      if (settings._endianAction == EndianAction.NeverSwap || length <= 1)
        return false;
      if (settings._endianAction == EndianAction.SwapIfCurrentSystemIsLittleEndian && !BitConverter.IsLittleEndian)
        return false;
      return true;
    }
  }

  public enum EndianAction
  {
    /// <summary>
    /// Default value, since the specification explicitly states that MsgPack is big-endian.
    /// </summary>
    [Description("Default value: will only reorder when the current system is little-endian.")]
    SwapIfCurrentSystemIsLittleEndian = 0,

    /// <summary>
    /// Force reordering bytes (regardless of current system)
    /// </summary>
    [Description("Force reordering bytes (regardless of current system)")]
    AlwaysSwap = 1,

    /// <summary>
    /// Do not reorder bytes (regardless of current system)
    /// </summary>
    [Description("Do not reorder bytes (regardless of current system)")]
    NeverSwap = 2
  }

  [Flags]
  [DefaultValue(AddTypeIdOption.IfAmbiguious)]
  public enum AddTypeIdOption
  {
    /// <summary>
    /// Never add the Type name to the dictionary (no Interface or Base-classes hierarchy in your code-base)
    /// </summary>
    [Description("Never add the Type name to the dictionary (no Interface or Base-classes hierarchy in your code-base)")]
    Never = 0,

    /// <summary>
    /// Only add type name if the property is of a different type than the value it contains (interfaces or base types)
    /// </summary>
    [Description("Only add type name if the property is of a different type than the value it contains (interfaces or base types)")]
    IfAmbiguious = 1,

    /// <summary>
    /// Always add the type id
    /// </summary>
    [Description("Always add the type id")]
    Always = 2,

    /// <summary>
    /// Use the full type name (significantly larger payload, only needed if multiple objects with the same name exist in multiple namespaces)
    /// </summary>
    [Description("Use the full type name (significantly larger payload, only needed if multiple objects with the same name exist in multiple namespaces)")]
    FullName = 16,

    /// <summary>
    /// By default the custom <see cref="IMsgPackTypeResolver">type resolvers</see> will be tried and if they all retuen null the built-in name/fullname resolver will be used. Setting this flag will prevent the default implementation to bloat the output (and the resolver should be able to handle null as input).
    /// </summary>
    [Description("By default the custom type resolvers will be tried and if they all retuen null the built-in name/fullname resolver will be used. Setting this flag will prevent the default implementation to bloat the output (and the resolver should be able to handle null as input).")]
    NoDefaultFallBack = 64
  }

  /// <summary>
  /// Options with the defaults, for code in this assembly that needs options when none were given (the serializers derive their own settings).
  /// </summary>
  internal sealed class DefaultOptions : MsgPackOptions
  {
  }
}
