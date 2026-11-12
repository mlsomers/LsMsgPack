using LsMsgPack;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.Extensions;
using System;

namespace LsMsgPack
{
  /// <summary>
  /// Ready-made options for exchanging data with other MsgPack libraries (their default settings) or with LsMsgPack. Every call returns new options, change them as needed.
  /// <para>The other library must read and write objects as maps keyed by the property names (MessagePack-CSharp: the contractless resolver or <c>[MessagePackObject(true)]</c>), see docs/Compatibility.md.</para>
  /// <para>With the web formatters: <c>AddLsMsgPackSerializerFormatters(o =&gt; o.Plain = LsMsgPackPresets.MessagePackCSharp())</c>.</para>
  /// </summary>
  public static class LsMsgPackPresets
  {
    /// <summary>
    /// The default static filters, with the public fields serialized (MessagePack-CSharp and Nerdbank.MessagePack write them, after the properties: the same order).
    /// One array for all the options of these presets: the serializers cache the members per filter array.
    /// </summary>
    private static readonly IMsgPackPropertyIncludeStatically[] PublicFields = new IMsgPackPropertyIncludeStatically[]
    {
      new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, SerializedFields.Public),
      new FilterIgnoredAttribute(),
      new FilterStatic()
    };

    /// <summary>
    /// The same data as LsMsgPack with its default settings: the indexed schema, objects as arrays, type ids where the type differs, default values as nil, Guids as bin 16, decimals as extension type 1.
    /// </summary>
    public static LsMsgPackOptions LsMsgPack()
    {
      return new LsMsgPackOptions();
    }

    /// <summary>
    /// MessagePack-CSharp with its default formatters: Guids and decimals as strings, DateTimeOffset as [clock time, offset in minutes], DateTimeKind.Unspecified taken as UTC,
    /// BigInteger, Int128 and UInt128 as bin (little-endian), every value written, no type ids, public fields too (<see cref="SerializedFields.Public"/>, as its contractless resolver).
    /// </summary>
    public static LsMsgPackOptions MessagePackCSharp()
    {
      LsMsgPackOptions options = Maps();
      options.StaticFilters = PublicFields;
      options.GuidFormat = GuidFormat.String;
      options.DecimalFormat = DecimalFormat.String;
      options.DateTimeOffsetFormat = DateTimeOffsetFormat.ClockTimeAndOffset;
      options.UnspecifiedDateTimeKind = DateTimeKind.Utc;
      options.BigIntegerFormat = BigIntegerFormat.Binary;
      return options;
    }

    /// <summary>
    /// Nerdbank.MessagePack with its default settings: decimals as its extension type 4, its Guids (extension type 2) are read (Guids are written as bin 16, which it reads), every value written, no type ids.
    /// DateTimeOffset is [the moment, offset in minutes] as LsMsgPack's default. Big integers beyond 64 bits differ (Nerdbank: extension types 3, 5 and 6). Public fields too (<see cref="SerializedFields.Public"/>).
    /// </summary>
    public static LsMsgPackOptions Nerdbank()
    {
      LsMsgPackOptions options = Maps();
      options.StaticFilters = PublicFields;
      options.Extensions = new LsExtension[] { new DecimalExtension(4), new NerdbankGuidExtension() };
      return options;
    }

    /// <summary>
    /// Libraries without .NET types (Python, JavaScript, Go, Rust and others): Guids and decimals as strings, dates (also DateTimeOffset) as timestamps, every value written, no type ids.
    /// Fields as by default (the attributed ones, <see cref="SerializedFields.Attributed"/>, as System.Text.Json: what ASP.NET Core models expect).
    /// The default of the web formatters for application/msgpack and application/x-msgpack.
    /// </summary>
    public static LsMsgPackOptions Generic()
    {
      LsMsgPackOptions options = Maps();
      options.GuidFormat = GuidFormat.String;
      options.DecimalFormat = DecimalFormat.String;
      options.DateTimeOffsetFormat = DateTimeOffsetFormat.Timestamp;
      return options;
    }

    /// <summary>
    /// Objects as maps keyed by their property names, every value (also defaults) and no type ids: what other libraries read and write.
    /// </summary>
    private static LsMsgPackOptions Maps()
    {
      return new LsMsgPackOptions()
      {
        UseInexedSchema = false,
        AddTypeIdOptions = AddTypeIdOption.Never,
        ObjectLayout = ObjectLayout.Map,
        DynamicFilters = new IMsgPackPropertyIncludeDynamically[0]
      };
    }
  }
}
