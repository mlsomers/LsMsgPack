using System.Collections.Generic;
using System.ComponentModel;

namespace LsMsgPack
{
  public class MsgPackSettings
  {
    internal bool FileContainsErrors = false;
    internal bool _dynamicallyCompact = true;
    internal EndianAction _endianAction = EndianAction.SwapIfCurrentSystemIsLittleEndian;
    internal bool _omitNull = true;
    internal bool _omitDefault = true;
    internal AddTypeNameOption _addTypeName = AddTypeNameOption.Never;
#if KEEPTRACK
    internal bool _preservePackages = false;
#endif

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

    [Category("Control")]
    [DisplayName("Preserve Packages")]
    [Description("Preserve the packaged (MsgPackItem) items in arrays and maps (in order to debug or inspect them in an editor)")]
    [DefaultValue(true)]
    public bool PreservePackages
    {
      get { return _preservePackages; }
      set { _preservePackages = value; }
    }

    internal bool _continueProcessingOnBreakingError = false;
    [Category("Control")]
    [DisplayName("Continue Processing On Breaking Error")]
    [Description("If there is a breaking error (such as a non-existing MsgPack type) the reader will do a best effort to continue reading the rest of the file (it will search for the next valid MsgPack type in the stream and continue from there) This should never be done in production code, but for debugging it might help (in navigating or spotting multiple issues in one cycle).")]
    [DefaultValue(true)]
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
    /// When a property has a null value, omit the whole property from the dictionary.
    /// </summary>
    /// <remarks>
    /// Only affects writing
    /// </remarks>
    [Category("Control")]
    [DisplayName("Omit Null")]
    [Description("When a property has a null value, omit the whole property from the dictionary.")]
    [DefaultValue(true)]
    public bool OmitNull
    {
      get { return _omitNull; }
      set { _omitNull = value; }
    }

    /// <summary>
    /// When a property has a default value (for example an integer value of 0), omit the whole property from the dictionary.
    /// </summary>
    /// <remarks>
    /// Only affects writing
    /// </remarks>
    [Category("Control")]
    [DisplayName("Omit Default")]
    [Description("When a property has a default value (for example an integer value of 0), omit the whole property from the dictionary.")]
    [DefaultValue(true)]
    public bool OmitDefault
    {
      get { return _omitDefault; }
      set { _omitDefault = value; }
    }

    /// <summary>
    /// Support type-hierarchy's where a property or collection can contain items of a base-type or interface with multiple implementations (eg. a list of IPet where a pet can be a dog, cat or fish etc...)
    /// <para>Using the full name will allow faster deserialization (less searching through assemblies) but obviously results in a much larger payload.</para>
    /// <para>Lookup speed my be increased when property types and their value types reside in the same assembly (i.e. when "interface IPet" and "class Dog" are defined in the same project).</para>
    /// <para>Alternatively or in addition, IMsgPackTypeResolver can be implemented and added by calling MsgPackSerializer.TypeResolvers.Add().</para>
    /// <para>Defining a property with the type "Object" will probably take significantly longer to deserialize and may pose a false match when FullName is not true.</para>
    /// </summary>
    /// <remarks>
    /// This setting only affects writing, but the effect of it's usage wil mostly be noticed when reading 
    /// </remarks>
    [Category("OOP")]
    [DisplayName("Add type name")]
    [Description("Support type-hierarchy's where a property or collection can contain items of a base-type or interface with multiple implementations (eg. a list of IPet where a pet can be a dog, cat or fish etc...)")]
    [DefaultValue(true)]
    public AddTypeNameOption AddTypeName
    {
      get { return _addTypeName; }
      set { _addTypeName = value; }
    }
    
    /// <summary>
    /// Custom type resolvers can be added, only needed if using object-models with polymorphic properties (base types or interfaces that have multiple implementations).
    /// <para>
    /// There is a <see cref="WildGooseChaseResolver">WildGooseChaseResolver</see> that can be used while developing, but it is not recomended for production!
    /// <code>
    /// MsgPackSerializer.TypeResolvers.Add(new WildGooseChaseResolver());
    /// </code>
    /// </para>
    /// <para>
    /// In order to keep a minimal payload and best performance, implement a custom IMsgPackTypeIdentifier
    /// </para>
    /// </summary>
    public HashSet<IMsgPackTypeResolver> TypeResolvers = new HashSet<IMsgPackTypeResolver>();

    public HashSet<IMsgPackTypeIdentifier> TypeIdentifiers = new HashSet<IMsgPackTypeIdentifier>();
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

  public enum AddTypeNameOption
  {
    /// <summary>
    /// Never add the Type name to the dictionary (no Interface or Base-classes (abstract or not) hierarchy in your code-base)
    /// </summary>
    [Description("Never add the Type name to the dictionary (no Interface or Base-classes (abstract or not) hierarchy in your code-base)")]
    Never = 0,

    /// <summary>
    /// Only add type name if the property is of a different type than the value it contains
    /// </summary>
    [Description("Only add type name if the property is of a different type than the value it contains")]
    IfAmbiguious = 1,

    /// <summary>
    /// Only add type name if the property is of a different type than the value it contains, use the full type name (significantly larger payload, only needed if multiple objects with the same name exist in multiple namespaces)
    /// </summary>
    [Description("Only add type name if the property is of a different type than the value it contains")]
    IfAmbiguiousFullName = 2,

    /// <summary>
    /// Always add the correct type name
    /// </summary>
    [Description("Always add the correct type name")]
    Always = 3,

    /// <summary>
    /// Always add the correct full type name (wasteful but may be useful for debugging or reverse engineering)
    /// </summary>
    [Description("Always add the correct full type name (wasteful but may be useful for debugging or reverse engineering)")]
    AlwaysFullName = 4,

    /// <summary>
    /// Use a custom ID if the property is of a different type than the value it contains (this requires <see cref="MsgPackSettings.TypeIdentifiers">MsgPackSettings.TypeIdentifiers</see> to be populated!)
    /// </summary>
    [Description("Use a custom ID if the property is of a different type than the value it contains")]
    UseCustomIdWhenAmbiguious,

    /// <summary>
    /// Always use a custom ID (this requires <see cref="MsgPackSettings.TypeIdentifiers">MsgPackSettings.TypeIdentifiers</see> to be populated!)
    /// </summary>
    [Description("Always use a custom ID")]
    UseCustomIdAlways,
  }

}
