using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text;
using LsMsgPack.Converters;

namespace LsMsgPack
{
  /// <summary>
  /// The settings that decide the format (shared by the serializers): <see cref="UseIndexedSchema"/>, <see cref="DynamicallyCompact"/>, <see cref="EndianAction"/>, type ids, resolvers, filters and the <see cref="SchemaStore"/>.
  /// <para>Each serializer derives its own settings from it (LsMsgPack: MsgPackSettings). The resolvers and filters get these options, so they work with any of them.</para>
  /// </summary>
  public abstract class MsgPackOptions
  {

    #region Default settings

    /// <summary>
    /// Use the <see cref="TypeResolving.Types.IndexedSchemaTypeResolver"/> to compact repetitive type and property names
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_UseIndexedSchema { get; set; } = true;

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
    public static AddTypeIdOption Default_AddTypeIdOptions { get; set; } = AddTypeIdOption.IfAmbiguous;

    /// <summary>
    /// Custom type resolvers can be added, only needed if using object-models with polymorphic properties (base types or interfaces that have multiple implementations).
    /// <para>
    /// There is a <see cref="TypeResolving.Types.WildGooseChaseResolver">WildGooseChaseResolver</see> that can be used while developing, but it is not recommended for production!
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
    /// The <see cref="TypeGuard"/> of new settings (null by default: the data may pick any type that is assignable to the declared type).
    /// </summary>
    [IgnoreDataMember]
    public static IMsgPackTypeGuard Default_TypeGuard { get; set; } = null;

    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>FilterNonSettable</item>
    /// <item>FilterIgnoredAttribute</item>
    /// <item>FilterStatic</item>
    /// </list>
    /// </summary>
    public static IMsgPackPropertyIncludeStatically[] Default_StaticFilters = new IMsgPackPropertyIncludeStatically[]{
            new FilterNonSettable(),
            new FilterIgnoredAttribute(),
            new FilterStatic()
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

    /// <summary>
    /// The <see cref="CompleteSchemas"/> of new settings (false by default).
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_CompleteSchemas { get; set; } = false;

    /// <summary>
    /// The <see cref="PropertyOrder"/> of new settings (<see cref="LsMsgPack.PropertyOrder.Reflection"/> by default, the order so far).
    /// </summary>
    [IgnoreDataMember]
    public static PropertyOrder Default_PropertyOrder { get; set; } = PropertyOrder.Reflection;

    /// <summary>
    /// The <see cref="ObjectLayout"/> of new settings (<see cref="LsMsgPack.ObjectLayout.Array"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static ObjectLayout Default_ObjectLayout { get; set; } = ObjectLayout.Array;

    /// <summary>
    /// The <see cref="TrimTrailingNulls"/> of new settings (false by default).
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_TrimTrailingNulls { get; set; } = false;

    /// <summary>
    /// The <see cref="PropertyNameCaseInsensitive"/> of new settings (false by default).
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_PropertyNameCaseInsensitive { get; set; } = false;

    /// <summary>
    /// The <see cref="SchemaVersion"/> of new settings (<see cref="LsMsgPack.SchemaVersion.Names"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static SchemaVersion Default_SchemaVersion { get; set; } = SchemaVersion.Names;

    /// <summary>
    /// The <see cref="MaxDepth"/> of new settings, 256 by default.
    /// </summary>
    [IgnoreDataMember]
    public static int Default_MaxDepth { get; set; } = 256;

    /// <summary>
    /// The <see cref="ReferenceHandling"/> of new settings (<see cref="LsMsgPack.ReferenceHandling.None"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static ReferenceHandling Default_ReferenceHandling { get; set; } = ReferenceHandling.None;

    /// <summary>
    /// The <see cref="ObjectCreation"/> of new settings (<see cref="LsMsgPack.ObjectCreation.ConstructorOrUninitialized"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static ObjectCreation Default_ObjectCreation { get; set; } = ObjectCreation.ConstructorOrUninitialized;

    /// <summary>
    /// The <see cref="ReadErrors"/> of new settings (<see cref="ReadErrorHandling.FailFast"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static ReadErrorHandling Default_ReadErrors { get; set; } = ReadErrorHandling.FailFast;

    /// <summary>
    /// The <see cref="UnspecifiedDateTimeKind"/> of new settings (<see cref="System.DateTimeKind.Utc"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static DateTimeKind Default_UnspecifiedDateTimeKind { get; set; } = DateTimeKind.Utc;

    /// <summary>
    /// The <see cref="ReadDateTimeKind"/> of new settings (<see cref="System.DateTimeKind.Utc"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static DateTimeKind Default_ReadDateTimeKind { get; set; } = DateTimeKind.Utc;

    /// <summary>
    /// The <see cref="DateTimeOffsetFormat"/> of new settings (<see cref="LsMsgPack.DateTimeOffsetFormat.TimestampAndOffset"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static DateTimeOffsetFormat Default_DateTimeOffsetFormat { get; set; } = DateTimeOffsetFormat.TimestampAndOffset;

    /// <summary>
    /// The <see cref="DateTimeFormat"/> of new settings (<see cref="LsMsgPack.DateTimeFormat.Native"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static DateTimeFormat Default_DateTimeFormat { get; set; } = DateTimeFormat.Native;

    /// <summary>
    /// The <see cref="EnumFormat"/> of new settings (<see cref="LsMsgPack.EnumFormat.Number"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static EnumFormat Default_EnumFormat { get; set; } = EnumFormat.Number;

    /// <summary>
    /// The <see cref="Populate"/> of new settings (<see cref="PopulateMembers.Collections"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static PopulateMembers Default_Populate { get; set; } = PopulateMembers.Collections;

    #endregion

    /// <summary>
    /// Property values are read and written by typed delegates bound to the get and set methods (once a property has been used a number of times) instead of reflection.
    /// <para>This applies to all settings, set it before (de)serializing. It is ignored when the runtime does not support dynamic code (NativeAOT; checked in the .NET Standard 2.1 build),
    /// switch it off on an AOT platform that uses the .NET Standard 2.0 build (the delegates need generic types that are created at runtime).</para>
    /// </summary>
    [IgnoreDataMember]
    public static bool CompilePropertyAccessors { get; set; } = true;

    internal bool FileContainsErrors = false;
    internal bool _useIndexedSchema = Default_UseIndexedSchema;
    internal bool _dynamicallyCompact = Default_DynamicallyCompact;
    internal EndianAction _endianAction = Default_EndianAction;
    internal AddTypeIdOption _addTypeIdOptions = Default_AddTypeIdOptions;

    internal IMsgPackTypeResolver[] _typeResolvers = Default_TypeResolvers;
    internal IMsgPackTypeGuard _typeGuard = Default_TypeGuard;
    internal IMsgPackPropertyIncludeStatically[] _staticFilters = Default_StaticFilters;
    internal IMsgPackPropertyIncludeDynamically[] _dynamicFilters = Default_DynamicFilters;
    internal IMsgPackPropertyIdResolver[] _propertyNameResolvers = Default_PropertyNameResolvers;
    internal SchemaStore _schemaStore = Default_SchemaStore;
    internal bool _writeSchemaReference = Default_WriteSchemaReference;
    internal bool _completeSchemas = Default_CompleteSchemas;
    internal PropertyOrder _propertyOrder = Default_PropertyOrder;
    internal ObjectLayout _objectLayout = Default_ObjectLayout;
    internal bool _trimTrailingNulls = Default_TrimTrailingNulls;
    internal bool _propertyNameCaseInsensitive = Default_PropertyNameCaseInsensitive;
    internal SchemaVersion _schemaVersion = Default_SchemaVersion;
    internal int _maxDepth = Default_MaxDepth;
    internal int _maxObjects = int.MaxValue;
    internal long _maxItems = int.MaxValue;
    internal int _maxStringLength = int.MaxValue;
    internal int _maxBinLength = int.MaxValue;
    internal Compression.MsgPackCompression _compression;
    internal int _maxDecompressedLength = int.MaxValue;
    internal ReferenceHandling _referenceHandling = Default_ReferenceHandling;
    internal ObjectCreation _objectCreation = Default_ObjectCreation;
    internal ReadErrorHandling _readErrors = Default_ReadErrors;
    internal bool _unspecifiedIsUtc = Default_UnspecifiedDateTimeKind == DateTimeKind.Utc;
    internal DateTimeKind _readDateTimeKind = Default_ReadDateTimeKind;
    internal DateTimeOffsetFormat _dateTimeOffsetFormat = Default_DateTimeOffsetFormat;
    internal bool _nativeDateTime = Default_DateTimeFormat == DateTimeFormat.Native;
    internal EnumFormat _enumFormat = Default_EnumFormat;
    internal PopulateMembers _populate = Default_Populate;
    internal MsgPackNamingPolicy _propertyNamingPolicy;
    internal MsgPackNamingPolicy _dictionaryKeyPolicy;
    internal MsgPackNamingPolicy _enumNamingPolicy;

    /// <summary>
    /// The cache of the serialized properties for <see cref="_staticFilters"/> (see FullPropertyInfo.GetSerializedProps without a session), looked up again when the filters are replaced.
    /// <para>Copied by <see cref="Clone"/>: it belongs to the filter array, not to these settings.</para>
    /// </summary>
    internal Meta.FullPropertyInfo.SharedPropsCache _sharedPropsCache;

    /// <summary>
    /// The highest <see cref="LsMsgPack.PropertyOrder"/>, the orders are indexes of the caches in FullPropertyInfo.
    /// </summary>
    internal const PropertyOrder LastPropertyOrder = PropertyOrder.TypeThenDeclaration;

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
    /// Collects the differences between the data and the classes (the Deserialize overloads with an out <see cref="ReadDifferences"/>), null otherwise.
    /// <para>Only set on a copy of the settings for one call (<see cref="WithDifferences"/>), settings are shared by threads. Copied by <see cref="Clone"/>, so the copies of that call (the schema session) collect too.</para>
    /// </summary>
    internal ReadDifferences _differences;

    /// <summary>
    /// Uses a micro schema (dictionary with type-name as key and an array of the types property names as value. The index of the name will be referenced from the serialized body (instead of the full name)
    /// </summary>
    [Category("Control")]
    [DisplayName("Use Indexed Schema")]
    [Description("Uses a micro schema (dictionary with type-name as key and an array of the types property names as value. The index of the name will be referenced from the serialized body (instead of the full name)")]
    [DefaultValue(true)]
    public bool UseIndexedSchema
    {
      get { return _useIndexedSchema; }
      set { _useIndexedSchema = value; }
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
    /// There is a <see cref="TypeResolving.Types.WildGooseChaseResolver">WildGooseChaseResolver</see> that can be used while developing, but it is not recommended for production!
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
    /// Decides which types the data may pick when deserializing (null by default), see docs/security.md and <see cref="TypeResolving.Types.AllowedTypesGuard"/>.
    /// <para>A type id (or a type resolver) can only pick a type that is assignable to the declared type, also without a guard. A property, element or root declared as <c>object</c>,
    /// an interface or a base class therefore accepts every type that the type resolvers can find: limit them with a guard when the data comes from somewhere you do not trust.</para>
    /// <para>Only asked when the data picks another type than the declared one, before the instance is created.</para>
    /// </summary>
    [IgnoreDataMember]
    public IMsgPackTypeGuard TypeGuard { get { return _typeGuard; } set { _typeGuard = value; } }


    /// <summary>
    /// Included by default (<see cref="Default_StaticFilters"/>):
    /// <list type="bullet">
    /// <item>FilterNonSettable</item>
    /// <item>FilterIgnoredAttribute</item>
    /// <item>FilterStatic</item>
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

    internal MsgPackConverter[] _converters = ConverterLookup.None;

    /// <summary>
    /// Converters that write the values of a type as another type and read them back (see <see cref="MsgPackConverter{T, TSurrogate}"/>), at most one per type; empty by default.
    /// They are used before <see cref="MsgPackConverterAttribute"/> and before the built-in formats, so a converter can also change how e.g. <see cref="DateTime"/> is written.
    /// <para>The setter keeps a checked copy: change the converters by setting a new array. Reader and writer need the same converters.</para>
    /// </summary>
    /// <exception cref="ArgumentException">Two converters for one type, a converter for object, an interface, an abstract class or a Nullable&lt;T&gt;, or converters that convert a type back into itself</exception>
    [IgnoreDataMember]
    public MsgPackConverter[] Converters
    {
      get { return (MsgPackConverter[])_converters.Clone(); }
      set { _converters = ConverterLookup.Checked(value); }
    }

    /// <summary>
    /// The converter of values of exactly this type: of <see cref="Converters"/>, otherwise of its <see cref="MsgPackConverterAttribute"/>; null for none.
    /// </summary>
    /// <param name="type">A runtime or declared type: a <see cref="Nullable{T}"/> gets the converter of its underlying type</param>
    internal MsgPackConverter ConverterFor(Type type)
    {
      return ConverterLookup.Find(_converters, type);
    }

    /// <summary>
    /// The converter of a value of this type assigned to the member: the member's own [MsgPackConverter] when the value is of its type (not an element, not a derived class), otherwise <see cref="ConverterFor(Type)"/>.
    /// </summary>
    /// <param name="type">The runtime type of the value (writing) or the type it is read into</param>
    /// <param name="assignedTo">The property, field, element or root the value is assigned to (null for none)</param>
    internal MsgPackConverter ConverterFor(Type type, Meta.FullPropertyInfo assignedTo)
    {
      MsgPackConverter member = assignedTo?.MemberConverter;
      if (member != null && (type == member.ConvertedType || type == assignedTo.DeclaredType))
        return member;
      return ConverterLookup.Find(_converters, type);
    }

    /// <summary>
    /// The dynamic filters leave out default values (<see cref="FilterDefaultValues"/>): a nil in the array layout is then the value of [DefaultValue] where a property has one (see FullPropertyInfo.NilValue).
    /// </summary>
    internal bool LeavesOutDefaultValues
    {
      get
      {
        IMsgPackPropertyIncludeDynamically[] filters = _dynamicFilters;
        if (filters is null)
          return false;
        for (int t = filters.Length - 1; t >= 0; t--)
          if (filters[t] is FilterDefaultValues)
            return true;
        return false;
      }
    }

    /// <summary>
    /// Asks the dynamic filters whether the value is written (both serializers): the filters that implement <see cref="IMsgPackPropertyIncludeWithOwner"/> get the object too.
    /// </summary>
    /// <param name="owner">The object owning the property (a struct: boxed)</param>
    internal static bool IncludeDynamically(IMsgPackPropertyIncludeDynamically[] filters, Meta.FullPropertyInfo prop, object owner, object value)
    {
      for (int t = filters.Length - 1; t >= 0; t--)
      {
        IMsgPackPropertyIncludeDynamically filter = filters[t];
        if (filter is IMsgPackPropertyIncludeWithOwner withOwner ? !withOwner.IncludeProperty(prop, owner, value) : !filter.IncludeProperty(prop, value))
          return false;
      }
      return true;
    }

    /// <summary>
    /// The dynamic filters have a <see cref="FilterShouldSerialize"/>.
    /// </summary>
    internal bool CallsShouldSerialize
    {
      get
      {
        IMsgPackPropertyIncludeDynamically[] filters = _dynamicFilters;
        if (filters is null)
          return false;
        for (int t = filters.Length - 1; t >= 0; t--)
          if (filters[t] is FilterShouldSerialize)
            return true;
        return false;
      }
    }


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
    /// Write a reference to the schema (18 bytes, see <see cref="SchemaStore"/>) instead of the schema itself, false by default. Needs <see cref="UseIndexedSchema"/> and a <see cref="SchemaStore"/>.
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
    /// With <see cref="WriteSchemaReference"/>: the schema of a root type is made at once with every class a value of it can hold (the classes of its properties, elements and dictionary values,
    /// and the classes deriving from them in the assemblies the root type reaches), in a fixed order. False by default: a schema gets the classes the values written so far had.
    /// <para>Its id then only depends on the root type, the classes and the settings, not on the data: every process (e.g. the instances of a web farm) writes the same schemas,
    /// and a reader can hold them before the first value arrives (the TypeScript generator writes them for its clients). A value with a class the schema does not have (one of another assembly,
    /// or in a property declared as object) makes it grow as usual.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Complete Schemas")]
    [Description("With schema references: the schema of a root type gets every class its values can hold at once (in a fixed order), so its id does not depend on the data.")]
    [DefaultValue(false)]
    public bool CompleteSchemas
    {
      get { return _completeSchemas; }
      set { _completeSchemas = value; }
    }

    /// <summary>
    /// The order in which the properties of an object are written (and listed in the indexed schema), <see cref="LsMsgPack.PropertyOrder.Reflection"/> by default.
    /// <para>Readers look properties up by name (or by their name in the schema), so data written in any order is read by settings with any order.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Property Order")]
    [Description("The order in which the properties of an object are written (and listed in the indexed schema). Readers look properties up by name, so it does not need to match the writer's.")]
    [DefaultValue(PropertyOrder.Reflection)]
    public PropertyOrder PropertyOrder
    {
      get { return _propertyOrder; }
      set
      {
        if (value < PropertyOrder.Reflection || value > LastPropertyOrder) // used as an index (see FullPropertyInfo)
          throw new ArgumentOutOfRangeException(nameof(value), value, "Not a defined PropertyOrder.");
        _propertyOrder = value;
      }
    }

    /// <summary>
    /// How objects with properties are written: an array of the values in <see cref="PropertyOrder"/> (default), or a map of property ids and values.
    /// <para>Only affects writing: readers take an array for an object as its values by position. Without the indexed schema the reader needs the same <see cref="PropertyOrder"/> (and properties) as the writer, with the schema the positions are matched by name.</para>
    /// <para>Other libraries mostly read objects as maps keyed by property names (see <see cref="MsgPackMediaTypes.ToPlain"/>).</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Object Layout")]
    [Description("How objects are written: an array of the values in the property order (default), or a map of property ids and values. Readers read either.")]
    [DefaultValue(ObjectLayout.Array)]
    public ObjectLayout ObjectLayout
    {
      get { return _objectLayout; }
      set
      {
        if (value != ObjectLayout.Map && value != ObjectLayout.Array)
          throw new ArgumentOutOfRangeException(nameof(value), value, "Not a defined ObjectLayout.");
        _objectLayout = value;
      }
    }

    /// <summary>
    /// <see cref="ObjectLayout.Array"/>: leave out the nil values at the end of an object's array (null values and values the dynamic filters leave out), false by default.
    /// <para>Smaller, and readers treat a missing value like a nil (the property keeps what the constructor made). Off by default because MessagePack-CSharp writes every value (integer keys), so the bytes are the same.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Trim Trailing Nulls")]
    [Description("Arrays of objects (ObjectLayout.Array): leave out the nil values at the end. Readers treat a missing value like a nil.")]
    [DefaultValue(false)]
    public bool TrimTrailingNulls
    {
      get { return _trimTrailingNulls; }
      set { _trimTrailingNulls = value; }
    }

    /// <summary>
    /// Reading property names: a key that is not the name of a property sets the one property whose name equals it ignoring case
    /// (ordinal; none when several properties do), false by default. For writers that change the names, e.g. camelCase (the TypeScript client, JSON habits).
    /// <para>With the indexed schema the names of the schema are matched the same way, once per class and schema, and the reader's classes are paired with the writer's by these names too.</para>
    /// <para>The exact name is looked up first, so it only costs something for keys that are not properties. The web formatters turn it on (<c>LsMsgPackHttpOptions.PropertyNameCaseInsensitive</c>).</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Property Name Case Insensitive")]
    [Description("Reading property names (maps and the indexed schema): a name that is not a property name sets the one property whose name equals it ignoring case.")]
    [DefaultValue(false)]
    public bool PropertyNameCaseInsensitive
    {
      get { return _propertyNameCaseInsensitive; }
      set { _propertyNameCaseInsensitive = value; }
    }

    /// <summary>
    /// The version of the indexed schema that is written: <see cref="LsMsgPack.SchemaVersion.Names"/> (the default, the property names) or <see cref="LsMsgPack.SchemaVersion.Described"/>
    /// (also the kind and default of each property and the type of the root, for readers without the classes, e.g. JavaScript; see docs/schema.md).
    /// <para>Only affects writing: readers take both versions (the names are all they need).</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Schema Version")]
    [Description("The version of the indexed schema that is written: the property names (default), or also the kind and default of each property, for readers without the classes. Readers read both.")]
    [DefaultValue(SchemaVersion.Names)]
    public SchemaVersion SchemaVersion
    {
      get { return _schemaVersion; }
      set
      {
        if (value != SchemaVersion.Names && value != SchemaVersion.Described)
          throw new ArgumentOutOfRangeException(nameof(value), value, "Not a defined SchemaVersion.");
        _schemaVersion = value;
      }
    }

    /// <summary>
    /// The deepest nesting read and written before a <see cref="MsgPackException"/> is thrown, 256 by default (<see cref="Default_MaxDepth"/>).
    /// <para>Reading: arrays and maps in the data. Writing: objects and collections in the object graph (a cycle ends here instead of in a stack overflow).</para>
    /// <para>A limit, because hostile data can nest deep enough to exhaust the stack, which ends the process (see docs/security.md).</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Max Depth")]
    [Description("The deepest nesting of arrays and maps read (and of objects and collections written) before an exception is thrown. Protects against data nested deep enough to exhaust the stack.")]
    [DefaultValue(256)]
    public int MaxDepth
    {
      get { return _maxDepth; }
      set { _maxDepth = value; }
    }

    /// <summary>
    /// The most arrays and maps one call reads (each object, collection, type id wrapper, and the arrays of tuples, pairs and the like), those read again for a copy included;
    /// more throw a <see cref="MsgPackException"/>. Unlimited (<see cref="int.MaxValue"/>) by default, 1,000,000 in the HTTP serializer.
    /// <para>A limit for untrusted data: an empty map is one byte and becomes a whole object (constructor, fields, the collections it creates), so a body of 30 MB can claim
    /// 30 million objects (see docs/security.md). Values that are skipped (members the class does not have) are not counted.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Max Objects")]
    [Description("The most arrays and maps (objects, collections) one call reads before an exception is thrown. Protects against small data that becomes many objects.")]
    [DefaultValue(int.MaxValue)]
    public int MaxObjects
    {
      get { return _maxObjects; }
      set { _maxObjects = value < 0 ? 0 : value; }
    }

    /// <summary>
    /// The most array elements and map entries one call reads, all arrays and maps together (those read again for a copy included); more throw a <see cref="MsgPackException"/>.
    /// Unlimited (<see cref="int.MaxValue"/>) by default, 10,000,000 in the HTTP serializer.
    /// <para>A limit for untrusted data: an entry of a dictionary takes two bytes in the data and dozens in memory (see docs/security.md). Values that are skipped are not counted.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Max Items")]
    [Description("The most array elements and map entries one call reads, in all arrays and maps together, before an exception is thrown.")]
    [DefaultValue(int.MaxValue)]
    public int MaxItems
    {
      get { return (int)_maxItems; }
      set { _maxItems = value < 0 ? 0 : value; }
    }

    /// <summary>
    /// The longest string read, in bytes as written (UTF-8); a longer one throws a <see cref="MsgPackException"/>. Unlimited (<see cref="int.MaxValue"/>) by default.
    /// <para>Map keys count where they are read as strings (the compiled and generated readers compare the keys of a class's members in place: no string is made).
    /// A string is at most about twice its bytes in memory: the size of the data limits it as well (in ASP.NET Core: <c>MaxRequestBodySize</c>).</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Max String Length")]
    [Description("The longest string read, in bytes as written, before an exception is thrown.")]
    [DefaultValue(int.MaxValue)]
    public int MaxStringLength
    {
      get { return _maxStringLength; }
      set { _maxStringLength = value < 0 ? 0 : value; }
    }

    /// <summary>
    /// The longest binary value (bin) or extension read, in bytes; a longer one throws a <see cref="MsgPackException"/>. Unlimited (<see cref="int.MaxValue"/>) by default.
    /// <para>Extensions of a fixed size (1 to 16 bytes: dates, decimals, Guids, references) are not checked.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Max Bin Length")]
    [Description("The longest binary value or extension read, in bytes, before an exception is thrown.")]
    [DefaultValue(int.MaxValue)]
    public int MaxBinLength
    {
      get { return _maxBinLength; }
      set { _maxBinLength = value < 0 ? 0 : value; }
    }

    /// <summary>
    /// Compresses the body of each payload with LZ4 (null, off, by default): <c>Lz4Compression.Block</c> or <c>Lz4Compression.BlockArray</c> of the package LsMsgPack.Lz4, in the format of
    /// MessagePack-CSharp's <c>Lz4Block</c> and <c>Lz4BlockArray</c> (see <see cref="Compression.MsgPackCompression"/> and docs/Compression.md).
    /// <para>The schema (or the reference to it) stays in front of the body as it is; a body shorter than <see cref="Compression.MsgPackCompression.MinimumLength"/> (64 bytes), or one compression would
    /// not make shorter, is written as it is. Readers need the option too: with it they take compressed and uncompressed bodies (either layout), without it a compressed body is an extension value.</para>
    /// </summary>
    [IgnoreDataMember]
    public Compression.MsgPackCompression Compression { get { return _compression; } set { _compression = value; } }

    /// <summary>
    /// The longest body a compressed body may decompress to, in bytes; a longer one throws a <see cref="MsgPackException"/> before anything is allocated. Unlimited (<see cref="int.MaxValue"/>)
    /// by default, 64 MiB in the HTTP serializer.
    /// <para>A limit for untrusted data: LZ4 makes up to 255 bytes of each byte (a body of 30 MB can claim gigabytes). Whatever this says, a body cannot claim more than its blocks can produce.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Max Decompressed Length")]
    [Description("The longest body a compressed body may decompress to, in bytes, before an exception is thrown.")]
    [DefaultValue(int.MaxValue)]
    public int MaxDecompressedLength
    {
      get { return _maxDecompressedLength; }
      set { _maxDecompressedLength = value < 0 ? 0 : value; }
    }

    /// <summary>
    /// The message of a limit that is exceeded: what, the limit and the option.
    /// </summary>
    internal static MsgPackException OverLimit(string what, long limit, string option)
    {
      return new MsgPackException($"The data holds more {what} than {option} ({limit}) allows. Raise it for data you trust (see docs/security.md).");
    }

    internal static MsgPackException TooLong(string what, long length, long limit, string option)
    {
      return new MsgPackException($"The data holds {what} of {length} bytes, longer than {option} ({limit}) allows. Raise it for data you trust (see docs/security.md).");
    }

    /// <summary>
    /// What happens to an object or collection that is met again while writing (<see cref="LsMsgPack.ReferenceHandling.None"/> by default: it is written again, a cycle ends at
    /// <see cref="MaxDepth"/> with a <see cref="MsgPackException"/>). See docs/References.md.
    /// <para><see cref="LsMsgPack.ReferenceHandling.IgnoreCycles"/>: a reference to an object that is being written is nil, as System.Text.Json's <c>ReferenceHandler.IgnoreCycles</c>.
    /// <see cref="LsMsgPack.ReferenceHandling.Preserve"/>: the second time an object or collection appears it is written as a reference to the first (an extension of type
    /// <see cref="ReferenceExtensionType"/> holding the path to it), and readers with this setting give the same instance there.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Reference Handling")]
    [Description("What happens to an object or collection met again while writing: written again (None), nil in a cycle (IgnoreCycles), or a reference to the first one (Preserve).")]
    [DefaultValue(ReferenceHandling.None)]
    public ReferenceHandling ReferenceHandling
    {
      get { return _referenceHandling; }
      set { _referenceHandling = value; }
    }

    /// <summary>
    /// The extension type of a reference to an earlier value (<see cref="LsMsgPack.ReferenceHandling.Preserve"/>): its content is the path to that value, a run of positive integers.
    /// </summary>
    public const sbyte ReferenceExtensionType = 5;

    internal bool IgnoresCycles { get { return _referenceHandling == ReferenceHandling.IgnoreCycles; } }

    internal bool PreservesReferences { get { return _referenceHandling == ReferenceHandling.Preserve; } }

    /// <summary>
    /// How the objects with properties are created when reading, <see cref="LsMsgPack.ObjectCreation.ConstructorOrUninitialized"/> by default (see docs/security.md).
    /// <para>Collections and dictionaries always use their constructor (with <see cref="LsMsgPack.ObjectCreation.Constructor"/> only their constructor).</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Object Creation")]
    [Description("How objects are created when reading: with their parameterless constructor, without running a constructor, or the constructor when there is one (default).")]
    [DefaultValue(ObjectCreation.ConstructorOrUninitialized)]
    public ObjectCreation ObjectCreation
    {
      get { return _objectCreation; }
      set { _objectCreation = value; }
    }

    /// <summary>
    /// What happens to a value that cannot be read into its property: a value that does not convert (a string where an int is declared, a name that is not a value of the enum...),
    /// an object of a class without a schema entry, or a type id that is not found (see docs/ReadDifferences.md). <see cref="ReadErrorHandling.FailFast"/> (the default) throws where it is found.
    /// <para>The other modes skip the value (the property keeps what the constructor gave it) and collect the error with the differences: <see cref="ReadErrorHandling.FailDeferred"/> throws a
    /// <see cref="ReadErrorsException"/> with all of them once the data is read, <see cref="ReadErrorHandling.ReportAndContinue"/> returns what could be read (the errors are in the <c>out ReadDifferences</c>).
    /// They collect the differences also without an <c>out ReadDifferences</c>, which makes reading a bit slower.</para>
    /// <para>Data that cannot be parsed (truncated, nested too deep...), types refused by the security checks (docs/security.md) and exceptions of the classes' constructors and setters always throw.</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Read Errors")]
    [Description("What happens to a value that cannot be read into its property: throw at once (default), skip it and throw once everything is read (with all errors), or skip it and report it.")]
    [DefaultValue(ReadErrorHandling.FailFast)]
    public ReadErrorHandling ReadErrors
    {
      get { return _readErrors; }
      set { _readErrors = value; }
    }

    /// <summary>
    /// How DateTime values are written: <see cref="LsMsgPack.DateTimeFormat.Native"/> (the default: extension type <see cref="DateTimeExtensionType"/> holding <see cref="DateTime.ToBinary"/>,
    /// the ticks and the Kind, so a value reads back as it was written, a Local one as the same moment in the reader's time zone, as Json.NET reads it)
    /// or <see cref="LsMsgPack.DateTimeFormat.Timestamp"/> (the timestamp of the MsgPack specification, which other libraries read: a moment in UTC without the Kind,
    /// see <see cref="UnspecifiedDateTimeKind"/> and <see cref="ReadDateTimeKind"/>). Readers take both, whatever this option says.
    /// </summary>
    [Category("Dates")]
    [DisplayName("DateTime Format")]
    [Description("How DateTime values are written: Native (default, extension type 6 with the ticks and the Kind: read back as written, a Local value in the reader's time zone) or Timestamp (the MsgPack timestamp other libraries read: a moment in UTC, the Kind is lost).")]
    [DefaultValue(DateTimeFormat.Native)]
    public DateTimeFormat DateTimeFormat
    {
      get { return _nativeDateTime ? DateTimeFormat.Native : DateTimeFormat.Timestamp; }
      set { _nativeDateTime = value == DateTimeFormat.Native; }
    }

    /// <summary>
    /// How enum values are written: <see cref="LsMsgPack.EnumFormat.Number"/> (the default: the underlying integer) or <see cref="LsMsgPack.EnumFormat.Name"/> (the name as a string, as the JSON serializers write it with a string enum converter).
    /// <para>An enum type or a property or field can choose for itself with <see cref="MsgPackEnumFormatAttribute"/> (or the attributes of other serializers that mean the same, see docs/Enums.md), which wins over this option.
    /// Readers take both forms, whatever this option says: a number, or a name (also the custom names of <c>[EnumMember]</c>, <c>[JsonStringEnumMemberName]</c>, <c>[EnumMemberShape]</c>, ignoring case as a fallback).</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Enum Format")]
    [Description("How enum values are written: Number (default, the underlying integer) or Name (the name as a string). Readers take both. [MsgPackEnumFormat] on an enum or member wins.")]
    [DefaultValue(EnumFormat.Number)]
    public EnumFormat EnumFormat
    {
      get { return _enumFormat; }
      set { _enumFormat = value; }
    }

    /// <summary>
    /// Which members reading fills instead of replacing them (docs/Populate.md): <see cref="PopulateMembers.Collections"/> (the default) fills the collections of members without a setter
    /// and skips their other values, as System.Text.Json does with objects; <see cref="PopulateMembers.GetOnly"/> also reads into the object a property without a setter
    /// (an auto-property, <c>public Address ShipTo { get; } = new Address();</c>) or a readonly field holds; <see cref="PopulateMembers.All"/> also reads into the instances that settable members hold.
    /// <para><see cref="MsgPackPopulateAttribute"/> on a member or class wins (and System.Text.Json's <c>[JsonObjectCreationHandling]</c>, Json.NET's <c>[JsonProperty(ObjectCreationHandling = ...)]</c>).</para>
    /// </summary>
    [Category("Control")]
    [DisplayName("Populate")]
    [Description("Which members reading fills instead of replacing them: Collections (default: the collections of members without a setter; their other values are skipped), GetOnly (also the objects of members without a setter) or All (also settable members holding an instance).")]
    [DefaultValue(PopulateMembers.Collections)]
    public PopulateMembers Populate
    {
      get { return _populate; }
      set { _populate = value; }
    }

    /// <summary>
    /// The names properties and fields are written by (map keys, and the names in the indexed schema), converted from their .NET names: e.g. <see cref="MsgPackNamingPolicy.CamelCase"/>
    /// for readers in JavaScript or Python that expect <c>firstName</c>; null (the default) for the .NET names. See docs/NamingPolicies.md.
    /// <para>Names given by a property id resolver (e.g. <c>[JsonPropertyName]</c> with <c>AttributePropertyNameResolver</c>) are written as they are. Readers look a key up by the
    /// name the policy gives, then by the .NET name (so data of writers without the policy is read too), then ignoring case with <see cref="PropertyNameCaseInsensitive"/>.
    /// Two properties of a class with the same converted name throw a <see cref="MsgPackException"/>.</para>
    /// </summary>
    [IgnoreDataMember]
    public MsgPackNamingPolicy PropertyNamingPolicy
    {
      get { return _propertyNamingPolicy; }
      set { _propertyNamingPolicy = value; }
    }

    /// <summary>
    /// The string keys of dictionaries as they are written, converted by this policy (System.Text.Json's <c>DictionaryKeyPolicy</c>); null (the default) writes them as they are.
    /// <para>Writing only, as System.Text.Json: keys are read as they were written. Not the keys of extension data (written as they were read) nor keys of other types.</para>
    /// </summary>
    [IgnoreDataMember]
    public MsgPackNamingPolicy DictionaryKeyPolicy
    {
      get { return _dictionaryKeyPolicy; }
      set { _dictionaryKeyPolicy = value; }
    }

    /// <summary>
    /// The names of enum values written by name (<see cref="LsMsgPack.EnumFormat.Name"/>), converted from their .NET names by this policy; null (the default) for the .NET names.
    /// Custom names (<c>[EnumMember(Value = ...)]</c>, <c>[JsonStringEnumMemberName]</c>...) are written as they are. Readers take the converted names, the .NET names and numbers.
    /// </summary>
    [IgnoreDataMember]
    public MsgPackNamingPolicy EnumNamingPolicy
    {
      get { return _enumNamingPolicy; }
      set { _enumNamingPolicy = value; }
    }

    /// <summary>
    /// How a value of this enum type is written: its own attribute (<see cref="MsgPackEnumFormatAttribute"/> or another serializer's), otherwise <see cref="EnumFormat"/>.
    /// </summary>
    internal EnumFormat EnumFormatOf(Type enumType)
    {
      EnumFormat? own = Meta.EnumFormats.OfType(enumType);
      return own ?? _enumFormat;
    }

    /// <summary>
    /// How a value of this enum type assigned to the member is written: the member's own attribute when the value is of the member's type (or the underlying type of a nullable member: not elements), otherwise <see cref="EnumFormatOf(Type)"/>.
    /// </summary>
    internal EnumFormat EnumFormatOf(Type enumType, Meta.FullPropertyInfo assignedTo)
    {
      Meta.PropertyAttributeSet attributes = assignedTo?.Attributes;
      if (attributes != null)
      {
        EnumFormat? member = attributes.EnumFormat;
        if (member != null && attributes.EnumType == enumType)
          return member.Value;
      }
      return EnumFormatOf(enumType);
    }

    /// <summary>
    /// The extension type of a DateTime in <see cref="LsMsgPack.DateTimeFormat.Native"/>: fixext 8 holding <see cref="DateTime.ToBinary"/> (62 bits of ticks and the Kind in the top 2 bits;
    /// a Local value holds the ticks in UTC) as an integer in the byte order of the settings.
    /// </summary>
    public const sbyte DateTimeExtensionType = 6;

    /// <summary>
    /// The DateTime of the 64 bits of <see cref="DateTimeExtensionType"/> (<see cref="DateTime.FromBinary"/>), checked: data that is no DateTime throws a <see cref="MsgPackException"/>.
    /// </summary>
    internal static DateTime FromNative(long bits)
    {
      long ticks = bits & 0x3FFFFFFFFFFFFFFF;
      if (bits >= 0 && ticks <= MaxTicks) // Unspecified (0) or Utc (1): the ticks as they are, without the call
        return new DateTime(ticks, (DateTimeKind)(bits >> 62));
      return FromNativeLocal(bits);
    }

    private const long MaxTicks = 3155378975999999999; // DateTime.MaxValue.Ticks

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static DateTime FromNativeLocal(long bits)
    {
      if (bits < 0) // Local: the ticks in UTC, converted to the reader's time zone (clamped at the ends of the range, as DateTime.FromBinary does)
      {
        try
        {
          return DateTime.FromBinary(bits);
        }
        catch (ArgumentException)
        {
        }
      }
      throw new MsgPackException($"The extension type {DateTimeExtensionType} holds no DateTime: 0x{bits:X16} has ticks beyond DateTime.MaxValue.");
    }

    /// <summary>
    /// What a DateTime of <see cref="System.DateTimeKind.Unspecified"/> is taken to be when it is written as a timestamp (a moment in UTC, <see cref="LsMsgPack.DateTimeFormat.Timestamp"/>): <see cref="System.DateTimeKind.Utc"/> (the default,
    /// the clock time is written as it is, as JSON writes it without an offset and MessagePack-CSharp takes it; it reads back as the same clock time in any time zone)
    /// or <see cref="System.DateTimeKind.Local"/> (like <see cref="DateTime.ToUniversalTime"/>: the clock time then depends on the time zones of the writer and the reader).
    /// </summary>
    [Category("Dates")]
    [DisplayName("Unspecified DateTime Kind")]
    [Description("What a DateTime of Kind Unspecified is taken to be when it is written as a timestamp (a moment in UTC): Utc (default, the clock time is written as it is) or Local (converted from local time).")]
    [DefaultValue(DateTimeKind.Utc)]
    public DateTimeKind UnspecifiedDateTimeKind
    {
      get { return _unspecifiedIsUtc ? DateTimeKind.Utc : DateTimeKind.Local; }
      set { _unspecifiedIsUtc = value == DateTimeKind.Utc; }
    }

    /// <summary>
    /// The Kind of the DateTime values read from timestamps (which hold a moment in UTC, not the Kind; <see cref="LsMsgPack.DateTimeFormat.Native"/> values keep their own): <see cref="System.DateTimeKind.Utc"/> (the default: UTC and Unspecified values
    /// keep their clock time, as JSON reads them, Local values come back as the same moment in UTC), <see cref="System.DateTimeKind.Local"/> (converted to local time),
    /// or <see cref="System.DateTimeKind.Unspecified"/> (the UTC clock time, for values written as Unspecified with <see cref="UnspecifiedDateTimeKind"/> = Utc).
    /// <para>A DateTimeOffset read from a timestamp has the local offset with Local, offset zero otherwise (the same moment).</para>
    /// </summary>
    [Category("Dates")]
    [DisplayName("Read DateTime Kind")]
    [Description("The Kind of the DateTime values read from timestamps: Utc (default), Local (converted to local time), or Unspecified (the UTC clock time, for values written as Unspecified with Unspecified DateTime Kind = Utc).")]
    [DefaultValue(DateTimeKind.Utc)]
    public DateTimeKind ReadDateTimeKind
    {
      get { return _readDateTimeKind; }
      set { _readDateTimeKind = value; }
    }

    /// <summary>
    /// How DateTimeOffset values are written: [the moment as a timestamp, the offset in minutes] by default (the offset is kept, as JSON keeps it),
    /// a timestamp of the moment (<see cref="LsMsgPack.DateTimeOffsetFormat.Timestamp"/>, the offset is lost, read as <see cref="ReadDateTimeKind"/> says), or MessagePack-CSharp's array.
    /// <para>Reading takes a timestamp in any format, and the array as this format says: the clock time with <see cref="LsMsgPack.DateTimeOffsetFormat.ClockTimeAndOffset"/>, the moment otherwise (the arrays look the same).</para>
    /// </summary>
    [Category("Dates")]
    [DisplayName("DateTimeOffset Format")]
    [Description("How DateTimeOffset values are written: [timestamp of the moment, offset in minutes] (default, keeps the offset), a timestamp of the moment (the offset is lost), or MessagePack-CSharp's [clock time, offset in minutes].")]
    [DefaultValue(DateTimeOffsetFormat.TimestampAndOffset)]
    public DateTimeOffsetFormat DateTimeOffsetFormat
    {
      get { return _dateTimeOffsetFormat; }
      set { _dateTimeOffsetFormat = value; }
    }

    /// <summary>
    /// A DateTimeOffset read from an array [timestamp, offset in minutes] (see <see cref="DateTimeOffsetFormat"/>).
    /// </summary>
    /// <param name="utcTicks">The ticks of the timestamp (UTC)</param>
    internal DateTimeOffset OffsetOfArray(long utcTicks, long minutes)
    {
      TimeSpan offset = TimeSpan.FromMinutes(minutes);
      if (_dateTimeOffsetFormat == DateTimeOffsetFormat.ClockTimeAndOffset)
        return new DateTimeOffset(utcTicks, offset); // MessagePack-CSharp: the clock time
      return new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(offset); // the moment
    }

    /// <summary>
    /// The moment a DateTime written as a timestamp stands for, in UTC (see <see cref="UnspecifiedDateTimeKind"/>).
    /// </summary>
    internal DateTime ToTimestamp(DateTime value)
    {
      if (_unspecifiedIsUtc && value.Kind == DateTimeKind.Unspecified)
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
      return value.ToUniversalTime();
    }

    /// <summary>
    /// A DateTime read from a timestamp (<paramref name="utc"/>) as <see cref="ReadDateTimeKind"/> asks.
    /// </summary>
    internal DateTime FromTimestamp(DateTime utc)
    {
      return FromTimestamp(utc, _readDateTimeKind);
    }

    internal static DateTime FromTimestamp(DateTime utc, DateTimeKind kind)
    {
      switch (kind)
      {
        case DateTimeKind.Utc: return utc;
        case DateTimeKind.Unspecified: return DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        default: return utc.ToLocalTime();
      }
    }

    /// <summary>
    /// A DateTimeOffset of a DateTime read from a timestamp (see <see cref="FromTimestamp(DateTime)"/>): Unspecified is the UTC clock time, not local time as the DateTimeOffset constructor takes it.
    /// </summary>
    internal static DateTimeOffset OffsetOfTimestamp(DateTime read)
    {
      return read.Kind == DateTimeKind.Unspecified ? new DateTimeOffset(read.Ticks, TimeSpan.Zero) : new DateTimeOffset(read);
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
    /// A copy of these options for a conversion repeated with <see cref="ReferenceHandling.Preserve"/> (data with references read by settings without the option, see
    /// References.ReadAgainPreserving): everything else is shared, the caches of a schema session and the differences collected too.
    /// </summary>
    internal MsgPackOptions Preserving()
    {
      MsgPackOptions copy = (MsgPackOptions)MemberwiseClone();
      copy._referenceHandling = ReferenceHandling.Preserve;
      return copy;
    }

    /// <summary>
    /// A copy of these options for one call that collects the differences (everything else is shared, the caches of a schema session too).
    /// </summary>
    internal MsgPackOptions WithDifferences(ReadDifferences differences)
    {
      MsgPackOptions copy = (MsgPackOptions)MemberwiseClone();
      copy._differences = differences;
      return copy;
    }

    /// <summary>
    /// Whether a custom extension of these settings writes values of the type (asked by <see cref="DescribedSchema"/>, the serializers keep their own extensions).
    /// </summary>
    internal virtual bool HasCustomExtension(Type type)
    {
      return false;
    }

    /// <summary>
    /// Whether a custom extension of these settings has the extension type code (it then reads that code, not the built-in meaning: <see cref="DateTimeExtensionType"/>, <see cref="ReferenceExtensionType"/>).
    /// </summary>
    internal virtual bool HasCustomExtensionCode(sbyte code)
    {
      return false;
    }

    /// <summary>
    /// The value as the serializer of these settings writes it without the schema (a default value in <see cref="DescribedSchema"/>), null when it cannot.
    /// </summary>
    /// <param name="type">The declared type of the value</param>
    /// <param name="endianAction">The byte order of the schema</param>
    internal byte[] SerializePlainValue(object value, Type type, EndianAction endianAction)
    {
      return SerializePlainValue(value, type, endianAction, false);
    }

    /// <param name="byNames">Objects as maps keyed by their names (<see cref="ObjectLayout.Map"/>), whatever the layout of these settings (a value kept as extension data, see KeptValues)</param>
    internal virtual byte[] SerializePlainValue(object value, Type type, EndianAction endianAction, bool byNames)
    {
      return null;
    }

    /// <summary>
    /// For <see cref="SerializePlainValue(object, Type, EndianAction, bool)"/> on a copy: without the indexed schema, also when these are the settings of a schema session
    /// (whose resolvers include the session's <see cref="IndexedSchemaTypeResolver"/>), without the differences of a read.
    /// </summary>
    internal void WithoutSchema(EndianAction endianAction, bool byNames)
    {
      _useIndexedSchema = false;
      _writeSchemaReference = false;
      _endianAction = endianAction;
      _differences = null;
      if (byNames)
        _objectLayout = ObjectLayout.Map;
      _typeResolvers = Array.FindAll(_typeResolvers, r => !(r is IndexedSchemaTypeResolver));
      _propertyNameResolvers = Array.FindAll(_propertyNameResolvers, r => !(r is IndexedSchemaTypeResolver));
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

  /// <summary>
  /// The version of the indexed schema (see <see cref="MsgPackOptions.SchemaVersion"/>). It is part of the <see cref="SchemaId"/>, readers tell the versions apart by their content.
  /// </summary>
  public enum SchemaVersion
  {
    /// <summary>
    /// Version 1 (the default): a map of type names with an array of property names each.
    /// </summary>
    [Description("Version 1 (default): the type names with the names of their properties.")]
    Names = 1,

    /// <summary>
    /// Version 2: the same map, each property an array <c>[name, type]</c> or <c>[name, type, default]</c>, and a last entry <c>""</c> with the type of the root (see docs/schema.md).
    /// </summary>
    [Description("Version 2: also the kind and default of each property and the type of the root, for readers without the classes (e.g. JavaScript).")]
    Described = 2
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

  /// <summary>
  /// The order of the properties of an object (see <see cref="MsgPackOptions.PropertyOrder"/>). Decided once per type, it costs nothing per object.
  /// <para>Serialized fields (<see cref="TypeResolving.Filters.SerializedFields"/>) come after the properties (per class in the orders that follow the declaration, all together by name in <see cref="Alphabetical"/>),
  /// so the positions of data written before a class had serialized fields stay valid.</para>
  /// </summary>
  public enum PropertyOrder
  {
    /// <summary>
    /// As <see cref="Type.GetProperties()"/> returns them (default), then the non-public properties, then the fields. The runtime does not guarantee this order.
    /// </summary>
    [Description("As Type.GetProperties() returns them (default), then the fields. The runtime does not guarantee this order.")]
    Reflection = 0,

    /// <summary>
    /// By name (ordinal, not culture-sensitive), properties and fields together.
    /// </summary>
    [Description("By name (ordinal, not culture-sensitive).")]
    Alphabetical = 1,

    /// <summary>
    /// In the order of the source code: the properties of base classes first, then per class in the order of their metadata tokens (the order the compiler emitted them).
    /// The fields of a class follow its properties (properties and fields have separate metadata tables, the order between them in the source is not kept).
    /// An overridden property keeps the position of the property it overrides.
    /// <para>The compiler emits the members of a partial class in the order of its files, so moving code or adding a file can change the order.</para>
    /// </summary>
    [Description("In the order of the source code: base classes first, then per class in the order the compiler emitted them, fields after properties. Partial classes follow the order of their files.")]
    Declaration = 2,

    /// <summary>
    /// By <see cref="System.Runtime.Serialization.DataMemberAttribute.Order"/> (lowest first, whatever class declares them), then the properties without an order in <see cref="Declaration"/> order.
    /// Properties with the same order keep their <see cref="Declaration"/> order.
    /// </summary>
    [Description("By DataMember(Order = n), lowest first, then the properties without an order in declaration order.")]
    Explicit = 3,

    /// <summary>
    /// Grouped by the type of the property (ordinal, by its name without the assembly: <see cref="Type.ToString()"/>, the same on .NET Framework and .NET), then in <see cref="Declaration"/> order.
    /// <para>Values of the same type follow each other, which may help a reader later on. Changing the type of a property moves it.</para>
    /// </summary>
    [Description("Grouped by the type of the property (by its name, without the assembly), then in declaration order.")]
    TypeThenDeclaration = 4
  }

  /// <summary>
  /// Which members reading fills instead of replacing them (see <see cref="MsgPackOptions.Populate"/> and <see cref="MsgPackPopulateAttribute"/>). An object is read into the instance
  /// (the members the data lacks keep their values), a collection is cleared and filled. A member whose getter returns null, or whose value is nil, is left as it is.
  /// </summary>
  public enum PopulateMembers
  {
    /// <summary>
    /// Only the collections of members without a setter are filled (<c>public List&lt;Line&gt; Lines { get; } = new List&lt;Line&gt;();</c>); other members without a setter are skipped when read (default).
    /// </summary>
    [Description("Only the collections of members without a setter are filled; other members without a setter are skipped (default).")]
    Collections = 0,

    /// <summary>
    /// Also the objects of auto-properties without a setter and of readonly fields (<c>public Address ShipTo { get; } = new Address();</c>) are read into.
    /// A getter written by hand (<c>public Settings Settings =&gt; Settings.Global;</c>) is left alone: it may return shared state (opt in with <see cref="MsgPackPopulateAttribute"/>).
    /// </summary>
    [Description("Also the objects of auto-properties without a setter and of readonly fields are read into.")]
    GetOnly = 1,

    /// <summary>
    /// Also settable members: the object a member holds is read into, its collection is filled (an array, or a read-only collection, is replaced); a member holding null gets a new instance.
    /// As System.Text.Json's <c>JsonObjectCreationHandling.Populate</c>, except that collections are cleared before they are filled (System.Text.Json appends).
    /// </summary>
    [Description("Also settable members holding an instance: objects are read into, collections cleared and filled.")]
    All = 2
  }

  /// <summary>
  /// How enum values are written (see <see cref="MsgPackOptions.EnumFormat"/> and <see cref="MsgPackEnumFormatAttribute"/>). Readers take both.
  /// </summary>
  public enum EnumFormat
  {
    /// <summary>
    /// The underlying integer (default): compact and fast, but a reader in another language sees a number, and renumbering the enum changes the meaning of stored data.
    /// </summary>
    [Description("The underlying integer (default).")]
    Number = 0,

    /// <summary>
    /// The name as a string, as System.Text.Json and Json.NET write it with their string enum converters: robust against reordering, readable for other languages.
    /// A custom name (<c>[EnumMember(Value = ...)]</c>, <c>[JsonStringEnumMemberName]</c>, <c>[EnumMemberShape(Name = ...)]</c>) is written instead of the .NET name;
    /// a combination of <c>[Flags]</c> values as <c>"A, B"</c>; a value that has no name (and is no combination of named flags) as its number.
    /// </summary>
    [Description("The name as a string (custom names of [EnumMember], [JsonStringEnumMemberName]; flags as \"A, B\"; a value without a name as its number).")]
    Name = 1
  }

  /// <summary>
  /// How <see cref="System.DateTime"/> values are written (see <see cref="MsgPackOptions.DateTimeFormat"/>).
  /// </summary>
  public enum DateTimeFormat
  {
    /// <summary>
    /// Extension type 6 (<see cref="MsgPackOptions.DateTimeExtensionType"/>, fixext 8) holding <see cref="System.DateTime.ToBinary"/>: the ticks and the Kind (default).
    /// Utc and Unspecified values read back exactly as written, a Local value as the same moment in the reader's time zone (as Json.NET and System.Text.Json read a time with an offset).
    /// Other MsgPack libraries read it as an unknown extension.
    /// </summary>
    [Description("Extension type 6 with the ticks and the Kind (default): read back as written, a Local value as the same moment in the reader's time zone. Other libraries see an unknown extension.")]
    Native = 0,

    /// <summary>
    /// The timestamp of the MsgPack specification (extension type -1): a moment in UTC, which every MsgPack library reads as a date. The Kind is lost
    /// (see <see cref="MsgPackOptions.UnspecifiedDateTimeKind"/> and <see cref="MsgPackOptions.ReadDateTimeKind"/>).
    /// </summary>
    [Description("The MsgPack timestamp (extension type -1) that every library reads: a moment in UTC, the Kind is lost.")]
    Timestamp = 1
  }

  /// <summary>
  /// How <see cref="System.DateTimeOffset"/> values are written (see <see cref="MsgPackOptions.DateTimeOffsetFormat"/>).
  /// </summary>
  public enum DateTimeOffsetFormat
  {
    /// <summary>
    /// An array of the moment (a timestamp) and the offset in minutes (default): the offset is kept. Nerdbank.MessagePack writes the same.
    /// A reader that only takes the first value still has the right moment.
    /// </summary>
    [Description("An array of the moment (a timestamp) and the offset in minutes (default): the offset is kept. Nerdbank.MessagePack writes the same.")]
    TimestampAndOffset = 0,

    /// <summary>
    /// A timestamp of the moment (UTC): the offset is lost. Any MsgPack library reads it as a date.
    /// </summary>
    [Description("A timestamp of the moment (UTC): the offset is lost. Any MsgPack library reads it as a date.")]
    Timestamp = 1,

    /// <summary>
    /// An array of the clock time (as a timestamp, as if it were UTC) and the offset in minutes, as MessagePack-CSharp writes it. The offset is kept.
    /// </summary>
    [Description("An array of the clock time (as a timestamp, as if it were UTC) and the offset in minutes, as MessagePack-CSharp writes it.")]
    ClockTimeAndOffset = 2
  }

  /// <summary>
  /// What happens to an object or collection that is met again while writing (see <see cref="MsgPackOptions.ReferenceHandling"/>).
  /// </summary>
  public enum ReferenceHandling
  {
    /// <summary>
    /// Written again each time (the default): an object referenced twice is read back as two copies, and a cycle fails at <see cref="MsgPackOptions.MaxDepth"/>.
    /// </summary>
    [Description("Written again each time: read back as copies; a cycle fails at Max Depth.")]
    None = 0,

    /// <summary>
    /// A reference to an object that is being written (the object itself or one that contains it, a cycle) is written as nil, as System.Text.Json's <c>ReferenceHandler.IgnoreCycles</c>.
    /// <para>Objects with properties are tracked (not structs, collections, pairs and tuples): every object being written is compared with the ones containing it. That costs LsMsgPack
    /// a part of writing per object (11-16% more time in the map layout, 15-21% in the array layout); it does not track a class whose properties are all numbers, strings, enums, dates, Guids or byte[].
    /// Only the current path counts: an object referenced twice side by side is written twice. Read back, the reference is null.</para>
    /// </summary>
    [Description("A reference to an object being written (a cycle) is nil, as System.Text.Json's IgnoreCycles.")]
    IgnoreCycles = 1,

    /// <summary>
    /// The second time an object or collection appears it is written as a reference to the first (extension type <see cref="MsgPackOptions.ReferenceExtensionType"/>, the path to it),
    /// so shared objects and cycles keep their identity. Readers with this setting give the same instance there; other readers (and other libraries) see an extension they read as a copy, or not at all.
    /// <para>Objects with properties and collections are tracked, not strings, byte[], structs and framework types written as one value. Off by default for that reason.</para>
    /// </summary>
    [Description("The second time an object or collection appears it is a reference to the first: shared objects and cycles keep their identity.")]
    Preserve = 2
  }

  /// <summary>
  /// How an object with properties is written (see <see cref="MsgPackOptions.ObjectLayout"/>). Collections and dictionaries are always arrays and maps.
  /// </summary>
  public enum ObjectLayout
  {
    /// <summary>
    /// A map of property ids (names, or indexes of the indexed schema) and values. Values left out by the dynamic filters (e.g. default values) are not written.
    /// <para>What other libraries read without configuration (keyed by property names, see <see cref="MsgPackMediaTypes.ToPlain"/>).</para>
    /// </summary>
    [Description("A map of property ids (names, or indexes of the indexed schema) and values. What other libraries read without configuration.")]
    Map = 0,

    /// <summary>
    /// An array of the values in <see cref="MsgPackOptions.PropertyOrder"/>, without keys (default). A value left out by the dynamic filters is written as nil, nils at the end are left out with <see cref="MsgPackOptions.TrimTrailingNulls"/>.
    /// <para>Readers leave a property as the constructor made it when its value is nil or missing (the array is shorter), as they do for a property that is not in a map. Values after the known properties are skipped.</para>
    /// <para>A type id wraps the array: { "": typeId, "@": [values] }. Without the indexed schema the reader needs the same order and properties as the writer.</para>
    /// </summary>
    [Description("An array of the values in the property order, without keys (nil for values left out by the filters). Without the indexed schema the reader needs the same order and properties.")]
    Array = 1
  }

  /// <summary>
  /// What happens to a value that cannot be read into its property (see <see cref="MsgPackOptions.ReadErrors"/>).
  /// </summary>
  public enum ReadErrorHandling
  {
    /// <summary>
    /// Throw where the error is found (the default). With an <c>out ReadDifferences</c> the exception has the differences found until then (<see cref="ReadDifferences.ExceptionDataKey"/>).
    /// </summary>
    [Description("Throw where the error is found.")]
    FailFast = 0,

    /// <summary>
    /// Skip the value, read the rest, then throw a <see cref="ReadErrorsException"/> with all errors and differences (with their paths).
    /// </summary>
    [Description("Skip the value and read the rest, then throw with all errors.")]
    FailDeferred = 1,

    /// <summary>
    /// Skip the value (the property keeps what the constructor gave it) and report it as a difference (<see cref="Difference.IsError"/>).
    /// </summary>
    [Description("Skip the value and report it with the differences.")]
    ReportAndContinue = 2
  }

  /// <summary>
  /// How the objects with properties are created when reading (see <see cref="MsgPackOptions.ObjectCreation"/>).
  /// <para>An object created without a constructor (uninitialized) has all its fields zero: the constructor and the initializers of fields and properties do not run,
  /// so a property that is not in the data stays null (or 0) instead of getting the value the class gives it.</para>
  /// </summary>
  public enum ObjectCreation
  {
    /// <summary>
    /// Only with the parameterless constructor (public or not). Reading a type without one throws a <see cref="MsgPackException"/>, an exception of the constructor is passed on as it is.
    /// </summary>
    [Description("Only with the parameterless constructor: reading a type without one throws, exceptions of the constructor are passed on.")]
    Constructor = 0,

    /// <summary>
    /// With the parameterless constructor (an exception of it is passed on as it is), uninitialized when the type has none (default).
    /// <para>The finalizer of an uninitialized object is suppressed (<see cref="GC.SuppressFinalize"/>): some finalizers fail on an object that was never constructed, which ends the process (see docs/security.md).</para>
    /// </summary>
    [Description("With the parameterless constructor, uninitialized (without its finalizer) when the type has none.")]
    ConstructorOrUninitialized = 1,

    /// <summary>
    /// Always uninitialized, no constructor runs (like DataContractSerializer), and the finalizer is suppressed. For data from a trusted source.
    /// <para>Could be faster on .NET Framework, but not on modern .NET Core (needs benchmarking to be sure if it also applies to code in .Net standard)</para>
    /// </summary>
    [Description("Always uninitialized: no constructor or initializer runs, the finalizer is suppressed. For trusted data.")]
    Uninitialized = 2
  }

  [Flags]
  [DefaultValue(AddTypeIdOption.IfAmbiguous)]
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
    IfAmbiguous = 1,

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
    /// By default the custom <see cref="IMsgPackTypeResolver">type resolvers</see> will be tried and if they all return null the built-in name/fullname resolver will be used. Setting this flag will prevent the default implementation to bloat the output (and the resolver should be able to handle null as input).
    /// </summary>
    [Description("By default the custom type resolvers will be tried and if they all return null the built-in name/fullname resolver will be used. Setting this flag will prevent the default implementation to bloat the output (and the resolver should be able to handle null as input).")]
    NoDefaultFallBack = 64
  }

  /// <summary>
  /// Options with the defaults, for code in this assembly that needs options when none were given (the serializers derive their own settings).
  /// </summary>
  internal sealed class DefaultOptions : MsgPackOptions
  {
  }
}
