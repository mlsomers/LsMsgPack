using LsMsgPack;
using LtMsgPack.Extensions;
using System.Runtime.Serialization;

namespace LtMsgPack
{
  /// <summary>
  /// The settings of <see cref="LtMsgPackSerializer"/>: the format settings shared with LsMsgPack (<see cref="MsgPackOptions"/>) plus custom extensions and limits for untrusted data.
  /// <para>The serializer takes a copy when it is created, change the options before creating it.</para>
  /// </summary>
  public class LtMsgPackOptions : MsgPackOptions
  {
    /// <summary>
    /// The custom extensions of new options: decimal as extension type 1 (the same bytes as LsMsgPack's MpDecimal).
    /// </summary>
    [IgnoreDataMember]
    public static LtExtension[] Default_Extensions = new LtExtension[] { new DecimalExtension() };

    /// <summary>
    /// The deepest nesting of arrays and maps read (and written) before a <see cref="MsgPackException"/> is thrown, 256 by default.
    /// <para>A limit, because hostile data can nest deep enough to exhaust the stack.</para>
    /// </summary>
    [IgnoreDataMember]
    public static int Default_MaxDepth { get; set; } = 256;

    internal LtExtension[] _extensions = Default_Extensions;
    internal int _maxDepth = Default_MaxDepth;

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

    /// <summary>
    /// The deepest nesting of arrays and maps (see <see cref="Default_MaxDepth"/>).
    /// </summary>
    [IgnoreDataMember]
    public int MaxDepth
    {
      get { return _maxDepth; }
      set { _maxDepth = value; }
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
