using LsMsgPack;
using LtMsgPack.Extensions;
using System;
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

    internal GuidFormat _guidFormat = GuidFormat.Binary;
    internal DecimalFormat _decimalFormat = DecimalFormat.Extension;
    internal DateTimeOffsetFormat _dateTimeOffsetFormat = DateTimeOffsetFormat.Timestamp;
    internal bool _unspecifiedIsUtc;

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
    /// How DateTimeOffsets are written, a timestamp of the moment by default (as LsMsgPack, the offset is lost). <see cref="LtMsgPack.DateTimeOffsetFormat.ClockTimeAndOffset"/> for MessagePack-CSharp.
    /// </summary>
    [IgnoreDataMember]
    public DateTimeOffsetFormat DateTimeOffsetFormat
    {
      get { return _dateTimeOffsetFormat; }
      set { _dateTimeOffsetFormat = value; }
    }

    /// <summary>
    /// What a DateTime of <see cref="DateTimeKind.Unspecified"/> is taken to be when it is written as a timestamp (a moment in UTC): <see cref="DateTimeKind.Local"/> (the default, as LsMsgPack) or <see cref="DateTimeKind.Utc"/> (as MessagePack-CSharp).
    /// </summary>
    [IgnoreDataMember]
    public DateTimeKind UnspecifiedDateTimeKind
    {
      get { return _unspecifiedIsUtc ? DateTimeKind.Utc : DateTimeKind.Local; }
      set { _unspecifiedIsUtc = value == DateTimeKind.Utc; }
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
