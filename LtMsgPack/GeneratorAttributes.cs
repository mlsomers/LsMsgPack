using System;

namespace LtMsgPack
{
  /// <summary>
  /// A root type of a context of the source generator (see <see cref="LtMsgPackContext"/>): the generator writes code for it and for the types it reaches
  /// (the types of its properties, elements of collections, base classes). Types that are only known at runtime (implementations assigned to an object or interface property) need their own attribute.
  /// </summary>
  [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
  public sealed class LtMsgPackSerializableAttribute : Attribute
  {
    public LtMsgPackSerializableAttribute(Type type)
    {
      Type = type;
    }

    public Type Type { get; }
  }

  /// <summary>
  /// The types the source generator writes code for without a context class of your own, from markers they already have.
  /// </summary>
  [Flags]
  public enum LtMsgPackInclude
  {
    None = 0,

    /// <summary>
    /// All classes of the assembly that the generated code can access (public and internal) and that have properties.
    /// </summary>
    AllTypes = 1,

    /// <summary>
    /// The classes marked with <see cref="SerializableAttribute"/> (as for the XmlSerializer's sgen and the binary formatters) and the types they reach.
    /// </summary>
    Serializable = 2,

    /// <summary>
    /// The classes marked with <c>System.Runtime.Serialization.DataContractAttribute</c> and the types they reach.
    /// </summary>
    DataContract = 4,

    /// <summary>
    /// The root types of System.Text.Json's source generation (<c>[JsonSerializable(typeof(...))]</c> on a <c>JsonSerializerContext</c>) and the types they reach.
    /// </summary>
    JsonSerializable = 8
  }

  /// <summary>
  /// The compile-time options of LtMsgPack's source generator for this assembly. The same can be set in the project file:
  /// <c>&lt;LtMsgPackGenerate&gt;Serializable;DataContract&lt;/LtMsgPackGenerate&gt;</c>, <c>&lt;LtMsgPackContextName&gt;</c>, <c>&lt;LtMsgPackAutoRegister&gt;</c>, <c>&lt;LtMsgPackNonPublicProperties&gt;</c> (this attribute wins).
  /// <para>Only which types get code is decided at compile time: the format (layout, schema, filters, formats of values) stays a runtime option of <see cref="LtMsgPackOptions"/>,
  /// the generated code checks it when it is first used.</para>
  /// </summary>
  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
  public sealed class LtMsgPackGenerateAttribute : Attribute
  {
    /// <summary>
    /// The types that are written into the context of the assembly (<see cref="ContextName"/>), besides the ones of the context classes with <see cref="LtMsgPackSerializableAttribute"/>.
    /// </summary>
    public LtMsgPackInclude Include { get; set; }

    /// <summary>
    /// The full name of the context class the generator writes for <see cref="Include"/>: <c>GeneratedMsgPackContext</c> in the root namespace of the project by default.
    /// </summary>
    public string ContextName { get; set; }

    /// <summary>
    /// Whether the generated module initializer registers the contexts of this assembly for the process (<c>LsMsgPack.Generated.GeneratedTypes.Register</c>), true by default.
    /// Needs C# 9 or later in this project (without, pass the contexts with <see cref="LtMsgPackOptions.Contexts"/>).
    /// </summary>
    public bool AutoRegister { get; set; } = true;

    /// <summary>
    /// The non-public properties the generated code covers (<see cref="LsMsgPack.TypeResolving.Filters.NonPublicProperties.Attributed"/> by default: those with an opt-in attribute such as <c>[JsonInclude]</c>).
    /// Which ones are serialized stays a runtime option (<see cref="LsMsgPack.TypeResolving.Filters.FilterNonSettable"/>), the others are read and set by reflection.
    /// The generated code calls non-public accessors through <c>UnsafeAccessor</c> (.NET 8 or later), otherwise the runtime uses reflection. Also the MSBuild property <c>&lt;LtMsgPackNonPublicProperties&gt;</c>.
    /// </summary>
    public LsMsgPack.TypeResolving.Filters.NonPublicProperties NonPublicProperties { get; set; } = LsMsgPack.TypeResolving.Filters.NonPublicProperties.Attributed;
  }
}
