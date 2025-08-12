﻿using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LsMsgPack
{
    public class MsgPackSettings
  {
    internal bool FileContainsErrors = false;
    internal bool _dynamicallyCompact = true;
    internal EndianAction _endianAction = EndianAction.SwapIfCurrentSystemIsLittleEndian;
    internal AddTypeIdOption _addTypeName = AddTypeIdOption.IfAmbiguious;


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
      get { return _addTypeName; }
      set { _addTypeName = value; }
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
    public IMsgPackTypeResolver[] TypeResolvers = new IMsgPackTypeResolver[0];

    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>FilterIgnoredAttribute</item>
    /// </list>
    /// </summary>
    public IMsgPackPropertyIncludeStatically[] StaticFilters = new IMsgPackPropertyIncludeStatically[]{
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
    public IMsgPackPropertyIncludeDynamically[] DynamicFilters = new[] { new FilterDefaultValues() };

    /// <summary>
    /// Included:
    /// <list type="bullet">
    /// <item>AttributePropertyNameResolver</item>
    /// </list>
    /// </summary>
    public IMsgPackPropertyIdResolver[] PropertyNameResolvers = new IMsgPackPropertyIdResolver[0];
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
