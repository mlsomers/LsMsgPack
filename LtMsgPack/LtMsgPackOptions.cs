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

    /// <summary>
    /// A copy of the options (the session caches of the indexed schema are not copied).
    /// </summary>
    public LtMsgPackOptions Clone()
    {
      return (LtMsgPackOptions)CloneOptions();
    }
  }
}
