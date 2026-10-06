using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    private static readonly ConcurrentDictionary<Type, PairInfo> Pairs = new ConcurrentDictionary<Type, PairInfo>();

    /// <summary>
    /// KeyValuePair&lt;TKey, TValue&gt; (Key and Value have no setters): an array [key, value], as MessagePack-CSharp writes it. Collections of pairs are maps (see CollectionInfo).
    /// </summary>
    internal static bool IsKeyValuePair(Type type)
    {
      return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
    }

    /// <summary>
    /// How to read and create a KeyValuePair&lt;TKey, TValue&gt; (see <see cref="IsKeyValuePair"/>).
    /// </summary>
    internal static PairInfo GetPair(Type type)
    {
      return Pairs.GetOrAdd(type, t => new PairInfo(t));
    }

    internal sealed class PairInfo
    {
      internal readonly PropertyInfo Key;
      internal readonly PropertyInfo Value;
      internal readonly FullPropertyInfo KeyInfo;
      internal readonly FullPropertyInfo ValueInfo;
      private readonly Type _type;

      internal PairInfo(Type type)
      {
        _type = type;
        Key = type.GetProperty(nameof(KeyValuePair<object, object>.Key));
        Value = type.GetProperty(nameof(KeyValuePair<object, object>.Value));
        KeyInfo = new FullPropertyInfo(type.GenericTypeArguments[0]);
        ValueInfo = new FullPropertyInfo(type.GenericTypeArguments[1]);
      }

      internal object Create(object key, object value)
      {
        return Activator.CreateInstance(_type, key, value);
      }
    }

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
