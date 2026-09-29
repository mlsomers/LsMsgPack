using System;
using System.Reflection;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Framework types that have no settable properties, without this they would be serialized as an empty map (and read back as their default value).
  /// <para>They use the same encodings as MessagePack-CSharp: char as an integer, TimeSpan and TimeOnly as ticks, DateOnly as its day number and Uri as its original string.</para>
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

  }
}
