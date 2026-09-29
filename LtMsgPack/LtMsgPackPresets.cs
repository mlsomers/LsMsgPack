using LsMsgPack;
using LsMsgPack.TypeResolving.Interfaces;
using LtMsgPack.Extensions;
using System;

namespace LtMsgPack
{
  /// <summary>
  /// Ready-made options for exchanging data with other MsgPack libraries (their default settings) or with LsMsgPack. Every call returns new options, change them as needed.
  /// <para>The other library must read and write objects as maps keyed by the property names (MessagePack-CSharp: the contractless resolver or <c>[MessagePackObject(true)]</c>), see docs/Compatibility.md.</para>
  /// <para>With the web formatters: <c>AddLsMsgPackSerializerFormatters(o =&gt; o.Plain = LtMsgPackPresets.MessagePackCSharp())</c>.</para>
  /// </summary>
  public static class LtMsgPackPresets
  {
    /// <summary>
    /// The same data as LsMsgPack with its default settings: the indexed schema, type ids where the type differs, default values left out, Guids as bin 16, decimals as extension type 1.
    /// </summary>
    public static LtMsgPackOptions LsMsgPack()
    {
      return new LtMsgPackOptions();
    }

    /// <summary>
    /// MessagePack-CSharp with its default formatters: Guids and decimals as strings, DateTimeOffset as [clock time, offset in minutes], DateTimeKind.Unspecified taken as UTC, every value written, no type ids.
    /// </summary>
    public static LtMsgPackOptions MessagePackCSharp()
    {
      LtMsgPackOptions options = Maps();
      options.GuidFormat = GuidFormat.String;
      options.DecimalFormat = DecimalFormat.String;
      options.DateTimeOffsetFormat = DateTimeOffsetFormat.ClockTimeAndOffset;
      options.UnspecifiedDateTimeKind = DateTimeKind.Utc;
      return options;
    }

    /// <summary>
    /// Nerdbank.MessagePack with its default settings: decimals as its extension type 4, its Guids (extension type 2) are read (Guids are written as bin 16, which it reads), every value written, no type ids.
    /// </summary>
    public static LtMsgPackOptions Nerdbank()
    {
      LtMsgPackOptions options = Maps();
      options.Extensions = new LtExtension[] { new DecimalExtension(4), new NerdbankGuidExtension() };
      return options;
    }

    /// <summary>
    /// Libraries without .NET types (Python, JavaScript, Go, Rust and others): Guids and decimals as strings, dates as timestamps, every value written, no type ids.
    /// </summary>
    public static LtMsgPackOptions Generic()
    {
      LtMsgPackOptions options = Maps();
      options.GuidFormat = GuidFormat.String;
      options.DecimalFormat = DecimalFormat.String;
      return options;
    }

    /// <summary>
    /// Objects as maps keyed by their property names, every value (also defaults) and no type ids: what other libraries read and write.
    /// </summary>
    private static LtMsgPackOptions Maps()
    {
      return new LtMsgPackOptions()
      {
        UseInexedSchema = false,
        AddTypeIdOptions = AddTypeIdOption.Never,
        DynamicFilters = new IMsgPackPropertyIncludeDynamically[0]
      };
    }
  }
}
