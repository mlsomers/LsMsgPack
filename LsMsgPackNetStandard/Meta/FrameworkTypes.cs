using System;
using System.Reflection;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Framework types that have no settable properties, without this they would be serialized as an empty map (and read back as their default value).
  /// <para>They use the same encodings as MessagePack-CSharp: char as an integer, TimeSpan and TimeOnly as ticks, DateOnly as its day number and Uri as its original string,
  /// and the types with a <see cref="PlainForm"/> (Half, Version, Memory&lt;byte&gt;, Complex...) as their plain value (see <see cref="FrameworkTypeInfo"/>).</para>
  /// <para>DateOnly and TimeOnly do not exist in .NET Standard, they are looked up in the assembly of DateTime (.NET 6 and later).</para>
  /// </summary>
  internal static class FrameworkTypes
  {
    private static readonly Type DateOnlyType = FrameworkTypeInfo.DateOnlyType;
    private static readonly Type TimeOnlyType = FrameworkTypeInfo.TimeOnlyType;
    private static readonly PropertyInfo DayNumber = FrameworkTypeInfo.DayNumber;
    private static readonly PropertyInfo TimeOnlyTicks = FrameworkTypeInfo.TimeOnlyTicks;

    internal static MsgPackItem Pack(object value, Type valuesType, MsgPackSettings settings)
    {
      if (value is char) return new MpInt(settings) { Value = (ushort)(char)value };
      if (value is TimeSpan) return new MpInt(settings) { Value = ((TimeSpan)value).Ticks };
      if (value is Uri) return new MpString(settings) { Value = ((Uri)value).OriginalString };
      if (valuesType == DateOnlyType) return new MpInt(settings) { Value = (int)DayNumber.GetValue(value) };
      if (valuesType == TimeOnlyType) return new MpInt(settings) { Value = (long)TimeOnlyTicks.GetValue(value) };
      return null;
    }

    /// <summary>
    /// As <see cref="MsgPackOptions.DateTimeOffsetFormat"/> says: [the moment as a timestamp, the offset in minutes], a timestamp of the moment, or [the clock time as a timestamp, the offset in minutes].
    /// </summary>
    internal static MsgPackItem PackDateTimeOffset(DateTimeOffset value, MsgPackSettings settings)
    {
      switch (settings._dateTimeOffsetFormat)
      {
        case DateTimeOffsetFormat.Timestamp:
          return new MpDateTime(settings) { Value = value };
        case DateTimeOffsetFormat.ClockTimeAndOffset:
          return OffsetArray(new DateTime(value.Ticks, DateTimeKind.Utc), value, settings);
        default:
          return OffsetArray(value.UtcDateTime, value, settings);
      }
    }

    private static MpArray OffsetArray(DateTime timestamp, DateTimeOffset value, MsgPackSettings settings)
    {
      return new MpArray(settings)
      {
        Value = new MsgPackItem[] { new MpDateTime(settings) { Value = timestamp }, new MpInt(settings) { Value = (short)value.Offset.TotalMinutes } }
      };
    }

    /// <summary>
    /// The item of a plain value (<see cref="FrameworkTypeInfo.ToPlain"/>).
    /// </summary>
    internal static MsgPackItem PackPlain(object plain, MsgPackSettings settings)
    {
      switch (plain)
      {
        case float single: return new MpFloat(settings) { Value = single };
        case string text: return new MpString(settings) { Value = text };
        case byte[] bytes: return new MpBin(settings) { Value = bytes };
        case PlainExtension extension: return new MpExt(settings) { TypeSpecifier = extension.TypeCode, Value = extension.Data };
        case double[] doubles:
          MsgPackItem[] items = new MsgPackItem[doubles.Length];
          for (int t = items.Length - 1; t >= 0; t--)
            items[t] = new MpFloat(settings) { Value = doubles[t] };
          return new MpArray(settings) { Value = items };
      }
      return new MpInt(settings) { Value = plain }; // int, long or ulong
    }
  }
}
