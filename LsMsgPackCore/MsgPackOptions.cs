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
    /// The <see cref="MaxDepth"/> of new settings, 256 by default.
    /// </summary>
    [IgnoreDataMember]
    public static int Default_MaxDepth { get; set; } = 256;

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
    /// The <see cref="UnspecifiedDateTimeKind"/> of new settings (<see cref="System.DateTimeKind.Local"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static DateTimeKind Default_UnspecifiedDateTimeKind { get; set; } = DateTimeKind.Local;

    /// <summary>
    /// The <see cref="ReadDateTimeKind"/> of new settings (<see cref="System.DateTimeKind.Local"/> by default).
    /// </summary>
    [IgnoreDataMember]
    public static DateTimeKind Default_ReadDateTimeKind { get; set; } = DateTimeKind.Local;

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
    internal IMsgPackTypeGuard _typeGuard = Default_TypeGuard;
    internal IMsgPackPropertyIncludeStatically[] _staticFilters = Default_StaticFilters;
    internal IMsgPackPropertyIncludeDynamically[] _dynamicFilters = Default_DynamicFilters;
    internal IMsgPackPropertyIdResolver[] _propertyNameResolvers = Default_PropertyNameResolvers;
    internal SchemaStore _schemaStore = Default_SchemaStore;
    internal bool _writeSchemaReference = Default_WriteSchemaReference;
    internal PropertyOrder _propertyOrder = Default_PropertyOrder;
    internal ObjectLayout _objectLayout = Default_ObjectLayout;
    internal bool _trimTrailingNulls = Default_TrimTrailingNulls;
    internal int _maxDepth = Default_MaxDepth;
    internal ObjectCreation _objectCreation = Default_ObjectCreation;
    internal ReadErrorHandling _readErrors = Default_ReadErrors;
    internal bool _unspecifiedIsUtc = Default_UnspecifiedDateTimeKind == DateTimeKind.Utc;
    internal DateTimeKind _readDateTimeKind = Default_ReadDateTimeKind;

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
    /// What a DateTime of <see cref="System.DateTimeKind.Unspecified"/> is taken to be when it is written as a timestamp (a moment in UTC): <see cref="System.DateTimeKind.Local"/> (the default)
    /// or <see cref="System.DateTimeKind.Utc"/> (as MessagePack-CSharp; with <see cref="ReadDateTimeKind"/> = Unspecified the clock time comes back unchanged in any time zone).
    /// </summary>
    [Category("Dates")]
    [DisplayName("Unspecified DateTime Kind")]
    [Description("What a DateTime of Kind Unspecified is taken to be when it is written as a timestamp (a moment in UTC): Local (default) or Utc (the clock time is written as it is).")]
    [DefaultValue(DateTimeKind.Local)]
    public DateTimeKind UnspecifiedDateTimeKind
    {
      get { return _unspecifiedIsUtc ? DateTimeKind.Utc : DateTimeKind.Local; }
      set { _unspecifiedIsUtc = value == DateTimeKind.Utc; }
    }

    /// <summary>
    /// The Kind of the DateTime values read from timestamps (which hold a moment in UTC, not the Kind): <see cref="System.DateTimeKind.Local"/> (the default, converted to local time),
    /// <see cref="System.DateTimeKind.Utc"/>, or <see cref="System.DateTimeKind.Unspecified"/> (the UTC clock time, for values written as Unspecified with <see cref="UnspecifiedDateTimeKind"/> = Utc).
    /// <para>A DateTimeOffset read from a timestamp has the local offset with Local, offset zero otherwise (the same moment).</para>
    /// </summary>
    [Category("Dates")]
    [DisplayName("Read DateTime Kind")]
    [Description("The Kind of the DateTime values read from timestamps: Local (default, converted to local time), Utc, or Unspecified (the UTC clock time, for values written as Unspecified with Unspecified DateTime Kind = Utc).")]
    [DefaultValue(DateTimeKind.Local)]
    public DateTimeKind ReadDateTimeKind
    {
      get { return _readDateTimeKind; }
      set { _readDateTimeKind = value; }
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
    /// A copy of these options for one call that collects the differences (everything else is shared, the caches of a schema session too).
    /// </summary>
    internal MsgPackOptions WithDifferences(ReadDifferences differences)
    {
      MsgPackOptions copy = (MsgPackOptions)MemberwiseClone();
      copy._differences = differences;
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

  /// <summary>
  /// The order of the properties of an object (see <see cref="MsgPackOptions.PropertyOrder"/>). Decided once per type, it costs nothing per object.
  /// </summary>
  public enum PropertyOrder
  {
    /// <summary>
    /// As <see cref="Type.GetProperties()"/> returns them (default). The runtime does not guarantee this order.
    /// </summary>
    [Description("As Type.GetProperties() returns them (default). The runtime does not guarantee this order.")]
    Reflection = 0,

    /// <summary>
    /// By name (ordinal, not culture-sensitive).
    /// </summary>
    [Description("By name (ordinal, not culture-sensitive).")]
    Alphabetical = 1,

    /// <summary>
    /// In the order of the source code: the properties of base classes first, then per class in the order of their metadata tokens (the order the compiler emitted them).
    /// An overridden property keeps the position of the property it overrides.
    /// <para>The compiler emits the members of a partial class in the order of its files, so moving code or adding a file can change the order.</para>
    /// </summary>
    [Description("In the order of the source code: base classes first, then per class in the order the compiler emitted them. Partial classes follow the order of their files.")]
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
