using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.Types.Extensions;
using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace LsMsgPack
{
  public class MsgPackSettings
  {

    #region Default settings

    /// <summary>
    /// Use the <see cref="TypeResolving.Types.IndexedSchemaTypeResolver"/> to compact repetitive type and property names
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_UseInexedSchema {  get; set; } = true;

    /// <summary>
    /// When true (default) will dynamically use the smallest possible datatype that the value fits in. When false, will always use the predefined type of integer.
    /// </summary>
    /// <remarks>
    /// Only affects writing
    /// </remarks>
    [IgnoreDataMember]
    public static bool Default_DynamicallyCompact { get; set; } = true;

#if KEEPTRACK

    /// <summary>
    /// Preserve the packaged (MsgPackItem) items in arrays and maps (in order to debug or inspect them in an editor)
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_PreservePackages { get; set; } = false;

    /// <summary>
    /// If there is a breaking error (such as a non-existing MsgPack type) the reader will do a best effort to continue reading the rest of the file (it will search for the next valid MsgPack type in the stream and continue from there) This should never be done in production code, but for debugging it might help (in navigating or spotting multiple issues in one cycle).
    /// </summary>
    [IgnoreDataMember]
    public static bool Default_ContinueProcessingOnBreakingError { get; set; } = false;

#endif

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
    /// Should inherit from BaseCustomExt, BaseCustomExtNonCached or AbstractCustomExt
    /// </summary>
    [IgnoreDataMember]
    public static ICustomExt[] Default_CustomExtentionTypes = new ICustomExt[]
    {
      new MpDecimal((MsgPackSettings)null)
    };


    #endregion

    internal bool FileContainsErrors = false;
    internal bool _useInexedSchema = Default_UseInexedSchema;
    internal bool _dynamicallyCompact = Default_DynamicallyCompact;
    internal EndianAction _endianAction = Default_EndianAction;
    internal AddTypeIdOption _addTypeIdOptions = Default_AddTypeIdOptions;

#if KEEPTRACK
    internal bool _preservePackages = Default_PreservePackages;
    internal bool _continueProcessingOnBreakingError = Default_ContinueProcessingOnBreakingError;
#endif

    internal IMsgPackTypeResolver[] _typeResolvers = Default_TypeResolvers;
    internal IMsgPackPropertyIncludeStatically[] _staticFilters = Default_StaticFilters;
    internal IMsgPackPropertyIncludeDynamically[] _dynamicFilters = Default_DynamicFilters;
    internal IMsgPackPropertyIdResolver[] _propertyNameResolvers = Default_PropertyNameResolvers;
    internal ICustomExt[] _customExtentionTypes = Default_CustomExtentionTypes;

    /// <summary>
    /// Uses a micro schema (dictionary with type-name as key and an array of the types property names as value. The index of the name will be referenced from the serialized body (instead of the full name)
    /// </summary>
    [Category("Control")]
    [DisplayName("Use Indexed Schema")]
    [Description("Uses a micro schema (dictionary with type-name as key and an array of the types property names as value. The index of the name will be referenced from the serialized body (instead of the full name)")]
    [DefaultValue(true)]
    public bool UseInexedSchema { 
      get { return _useInexedSchema; } 
      set {  _useInexedSchema = value; } 
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

#if KEEPTRACK

    /// <summary>
    /// Preserve the packaged (MsgPackItem) items in arrays and maps (in order to debug or inspect them in an editor)
    /// </summary>
    [Category("Control")]
    [DisplayName("Preserve Packages")]
    [Description("Preserve the packaged (MsgPackItem) items in arrays and maps (in order to debug or inspect them in an editor)")]
    [DefaultValue(false)]
    public bool PreservePackages
    {
      get { return _preservePackages; }
      set { _preservePackages = value; }
    }

    /// <summary>
    /// If there is a breaking error (such as a non-existing MsgPack type) the reader will do a best effort to continue reading the rest of the file (it will search for the next valid MsgPack type in the stream and continue from there) This should never be done in production code, but for debugging it might help (in navigating or spotting multiple issues in one cycle).
    /// </summary>
    [Category("Control")]
    [DisplayName("Continue Processing On Breaking Error")]
    [Description("If there is a breaking error (such as a non-existing MsgPack type) the reader will do a best effort to continue reading the rest of the file (it will search for the next valid MsgPack type in the stream and continue from there) This should never be done in production code, but for debugging it might help (in navigating or spotting multiple issues in one cycle).")]
    [DefaultValue(false)]
    public bool ContinueProcessingOnBreakingError
    {
      get { return _continueProcessingOnBreakingError; }
      set { _continueProcessingOnBreakingError = value; }
    }

#endif

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
    public IMsgPackTypeResolver[] TypeResolvers { get {  return _typeResolvers; } set {_typeResolvers=value;} }

    
    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>FilterIgnoredAttribute</item>
    /// </list>
    /// </summary>
    public IMsgPackPropertyIncludeStatically[] StaticFilters { get { return _staticFilters; } set {_staticFilters=value;} }

    
    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>FilterDefaultValues</item>
    /// <item>FilterNullValues</item>
    /// </list>
    /// </summary>
    public IMsgPackPropertyIncludeDynamically[] DynamicFilters { get { return _dynamicFilters;} set { _dynamicFilters=value;} }

    
    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>AttributePropertyNameResolver</item>
    /// </list>
    /// </summary>
    public IMsgPackPropertyIdResolver[] PropertyNameResolvers { get { return _propertyNameResolvers; } set { _propertyNameResolvers=value;} }

    
    /// <summary>
    /// Should inherit from BaseCustomExt, BaseCustomExtNonCached or AbstractCustomExt
    /// </summary>
    public ICustomExt[] CustomExtentionTypes { get { return _customExtentionTypes;} set { _customExtentionTypes=value;} }

    public MsgPackSettings Clone()
    {
      return new MsgPackSettings
      {
        FileContainsErrors = FileContainsErrors,
        _dynamicallyCompact = _dynamicallyCompact,
        _endianAction = _endianAction,
        _addTypeIdOptions = _addTypeIdOptions,

#if KEEPTRACK
        _preservePackages = _preservePackages,
        _continueProcessingOnBreakingError = _continueProcessingOnBreakingError,
#endif
        _typeResolvers = _typeResolvers,
        _staticFilters = _staticFilters,
        _dynamicFilters = _dynamicFilters,
        _propertyNameResolvers = _propertyNameResolvers,
        _customExtentionTypes = _customExtentionTypes
      };
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

}
