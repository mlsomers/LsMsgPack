using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// Framework types without settable properties that are written like MessagePack-CSharp does: char as an integer, TimeSpan and TimeOnly as ticks, DateOnly as its day number, Uri as its original string.
  /// <para>DateOnly and TimeOnly are found by name, .NET Standard does not have them.</para>
  /// </summary>
  internal static class FrameworkTypeInfo
  {
    internal static readonly Type DateOnlyType = typeof(DateTime).Assembly.GetType("System.DateOnly");
    internal static readonly Type TimeOnlyType = typeof(DateTime).Assembly.GetType("System.TimeOnly");

    internal static readonly PropertyInfo DayNumber = DateOnlyType?.GetProperty("DayNumber");
    private static readonly MethodInfo FromDayNumber = DateOnlyType?.GetMethod("FromDayNumber", new[] { typeof(int) });
    internal static readonly PropertyInfo TimeOnlyTicks = TimeOnlyType?.GetProperty("Ticks");
    private static readonly ConstructorInfo TimeOnlyFromTicks = TimeOnlyType?.GetConstructor(new[] { typeof(long) });

    /// <summary>
    /// Converts the unpacked value of one of these types (char is converted like the other primitives).
    /// </summary>
    /// <returns>False when the target type is none of these types, or the value is not what they are written as</returns>
    internal static bool TryConvert(object val, Type targetType, out object result)
    {
      result = null;
      if (targetType == typeof(Uri) && val is string uri)
        result = new Uri(uri, UriKind.RelativeOrAbsolute);
      else if (!IsInteger(val))
        return false;
      else if (targetType == typeof(TimeSpan))
        result = new TimeSpan(Convert.ToInt64(val, CultureInfo.InvariantCulture));
      else if (targetType == DateOnlyType)
        result = Invoke(() => FromDayNumber.Invoke(null, new object[] { Convert.ToInt32(val, CultureInfo.InvariantCulture) }));
      else if (targetType == TimeOnlyType)
        result = Invoke(() => TimeOnlyFromTicks.Invoke(new object[] { Convert.ToInt64(val, CultureInfo.InvariantCulture) }));
      else
        return false;

      return true;
    }

    private static bool IsInteger(object val)
    {
      return val is int || val is long || val is byte || val is sbyte || val is short || val is ushort || val is uint || val is ulong;
    }

    /// <summary>
    /// Rethrows the exception of the invoked method (e.g. an out of range day number) as it is, not as a TargetInvocationException.
    /// </summary>
    private static object Invoke(Func<object> invoke)
    {
      try
      {
        return invoke();
      }
      catch (TargetInvocationException ex) when (ex.InnerException != null)
      {
        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw; // not reached
      }
    }
  }
}
