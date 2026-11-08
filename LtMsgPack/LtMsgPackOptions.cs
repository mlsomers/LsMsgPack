using LsMsgPack;
using LtMsgPack.Extensions;
using System;
using System.Runtime.Serialization;

namespace LtMsgPack
{
  /// <summary>
  /// The settings of <see cref="LtMsgPackSerializer"/>: the format settings shared with LsMsgPack (<see cref="MsgPackOptions"/>) plus custom extensions and the formats of other libraries (the limits for untrusted data, such as <see cref="MsgPackOptions.MaxDepth"/>, are shared).
  /// <para>The serializer takes a copy when it is created, change the options before creating it.</para>
  /// </summary>
  public class LtMsgPackOptions : MsgPackOptions
  {
    /// <summary>
    /// The custom extensions of new options: decimal as extension type 1 (the same bytes as LsMsgPack's MpDecimal).
    /// </summary>
    [IgnoreDataMember]
    public static LtExtension[] Default_Extensions = new LtExtension[] { new DecimalExtension() };

    internal LtExtension[] _extensions = Default_Extensions;

    /// <summary>
    /// Custom extensions: values of their types are written as MsgPack extensions (the first extension that supports the type), and extensions of their type code are read by them.
    /// <para>Write the same bytes as the custom extensions of LsMsgPack (<c>MsgPackSettings.CustomExtentionTypes</c>) to stay compatible.</para>
    /// </summary>
    [IgnoreDataMember]
    public LtExtension[] Extensions
    {
      get { return _extensions; }
      set { _extensions = value ?? new LtExtension[0]; }
    }

    internal GuidFormat _guidFormat = GuidFormat.Binary;
    internal DecimalFormat _decimalFormat = DecimalFormat.Extension;
    internal BigIntegerFormat _bigIntegerFormat = BigIntegerFormat.Extension;

    /// <summary>
    /// How Guids are written, bin 16 by default (as LsMsgPack). <see cref="LtMsgPack.GuidFormat.String"/> for other libraries (see <see cref="LtMsgPackPresets"/>).
    /// </summary>
    [IgnoreDataMember]
    public GuidFormat GuidFormat
    {
      get { return _guidFormat; }
      set { _guidFormat = value; }
    }

    /// <summary>
    /// How decimals are written, the decimal extension by default (as LsMsgPack). <see cref="LtMsgPack.DecimalFormat.String"/> for other libraries (see <see cref="LtMsgPackPresets"/>).
    /// </summary>
    [IgnoreDataMember]
    public DecimalFormat DecimalFormat
    {
      get { return _decimalFormat; }
      set { _decimalFormat = value; }
    }

    /// <summary>
    /// How BigInteger, Int128 and UInt128 are written: an integer, or extension type -2 beyond 64 bits (as LsMsgPack). <see cref="LtMsgPack.BigIntegerFormat.Binary"/> for MessagePack-CSharp.
    /// <para>DateTimeOffset values: <see cref="MsgPackOptions.DateTimeOffsetFormat"/>, shared with LsMsgPack.</para>
    /// </summary>
    [IgnoreDataMember]
    public BigIntegerFormat BigIntegerFormat
    {
      get { return _bigIntegerFormat; }
      set { _bigIntegerFormat = value; }
    }

    internal LtMsgPackContext[] _contexts = new LtMsgPackContext[0];

    /// <summary>
    /// The code of LtMsgPack's source generator to use (see <see cref="LtMsgPackContext"/>), asked before the registered contexts (see <see cref="UseRegisteredContexts"/>).
    /// The first context that describes a type is used for it.
    /// <para>With <see cref="UseRegisteredContexts"/> (the default) a serializer registers these contexts for the process when it is created, as the generated module initializer does:
    /// LsMsgPack.Core creates collections and objects with the registered code, which NativeAOT needs.</para>
    /// </summary>
    [IgnoreDataMember]
    public LtMsgPackContext[] Contexts
    {
      get { return _contexts; }
      set { _contexts = value ?? new LtMsgPackContext[0]; }
    }

    internal bool _useRegisteredContexts = true;

    /// <summary>
    /// Whether the generated code registered for the process (by the module initializers of the generator, see <see cref="LtMsgPackGenerateAttribute.AutoRegister"/>) is used, true by default.
    /// <para>With neither, the serializer builds its plans at runtime (as without the generator). The data is the same either way.</para>
    /// <para>Also decides whether the serializer registers its own contexts (<see cref="Contexts"/>, the one of its constructor): off, a serializer neither uses nor adds to the registered code.
    /// Under NativeAOT keep it on (or register the context, e.g. with the module initializer): LsMsgPack.Core creates collections with the registered code.</para>
    /// </summary>
    [IgnoreDataMember]
    public bool UseRegisteredContexts
    {
      get { return _useRegisteredContexts; }
      set { _useRegisteredContexts = value; }
    }

    internal int _compileWritersAfter = Default_CompileWritersAfter;

    /// <summary>
    /// The default of <see cref="CompileWritersAfter"/>.
    /// </summary>
    public static int Default_CompileWritersAfter = 1000;

    /// <summary>
    /// After how many objects of a type a writer compiled for that type takes over (default 1000): the code the source generator writes (see <see cref="LtMsgPackContext"/>),
    /// built at runtime for the type's properties and these options. Compiling costs 1-7 ms per type (most of it the JIT), done on the thread pool while the plans of the runtime keep writing,
    /// so types written only a few times are not compiled at all. 0 compiles when the type is first written (on the calling thread, so its first object is written by the compiled writer), a negative number never.
    /// <para>Only where the runtime compiles dynamic code (not under NativeAOT or on WebAssembly), not for types the source generator covers (its code is used; but where it writes another property order than it predicted, by a loop over the properties, after this many objects the compiled writer takes over), and not with custom dynamic filters
    /// or <see cref="LsMsgPack.AddTypeIdOption.Always"/>. The data is the same either way.</para>
    /// </summary>
    [IgnoreDataMember]
    public int CompileWritersAfter
    {
      get { return _compileWritersAfter; }
      set { _compileWritersAfter = value; }
    }

    internal override bool HasCustomExtension(Type type)
    {
      LtExtension[] extensions = _extensions;
      for (int t = 0; t < extensions.Length; t++)
        if (extensions[t] != null && extensions[t].SupportsType(type))
          return true;
      return false;
    }

    /// <summary>
    /// Only called when a described schema (<see cref="SchemaVersion.Described"/>) is made, for the values of [DefaultValue].
    /// </summary>
    internal override byte[] SerializePlainValue(object value, Type type, EndianAction endianAction)
    {
      LtMsgPackOptions plain = Clone();
      plain._useInexedSchema = false;
      plain._writeSchemaReference = false;
      plain._endianAction = endianAction;
      plain._differences = null;
      return new LtMsgPackSerializer(plain, null).Serialize(value, type); // the contexts of these options are copied (the one of the serializer too)
    }

    /// <summary>
    /// A copy of the options (the session caches of the indexed schema are not copied).
    /// </summary>
    public LtMsgPackOptions Clone()
    {
      return (LtMsgPackOptions)CloneOptions();
    }
  }
}
