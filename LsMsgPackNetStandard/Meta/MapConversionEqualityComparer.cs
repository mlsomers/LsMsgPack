using System;
using System.Collections.Generic;

namespace LsMsgPack.Meta
{
  public class MapConversionEqualityComparer : IEqualityComparer<object>
  {
    /// <summary>
    /// The comparer has no state, so one instance can be shared (also between threads).
    /// </summary>
    internal static readonly MapConversionEqualityComparer Instance = new MapConversionEqualityComparer();

    public new bool Equals(object x, object y)
    {
      if (x == y)
        return true;

      if (x == null || y == null)
        return false;

      // Most common cases: property names, or with the indexed schema a (byte) key read from the data compared to an (int) property id
      if (x is string xString && y is string yString)
        return string.Equals(xString, yString);

      if (TryGetInteger(x, out long xInt) && TryGetInteger(y, out long yInt))
        return xInt == yInt;

      if (x.Equals(y))
        return true;

      if (y.Equals(x))
        return true;

      Type xType = x.GetType();

      if (MsgPackMeta.NumericTypes.Contains(xType))
      {
        Type yType = y.GetType();
        if (MsgPackMeta.NumericTypes.Contains(yType))
        {
          Decimal xx = Convert.ToDecimal(x);
          Decimal yy = Convert.ToDecimal(y);
          return xx == yy;
        }
      }

      return false;
    }

    public int GetHashCode(object obj)
    {
      if (obj is string)
        return obj.GetHashCode();

      // Numbers considered equal by Equals (e.g. (sbyte)-1 and (int)-1) must return the same hash code
      if (TryGetInteger(obj, out long integer))
        return new decimal(integer).GetHashCode(); // the same as Convert.ToDecimal(obj).GetHashCode() below, without the lookup and conversion

      if (MsgPackMeta.NumericTypes.Contains(obj.GetType()))
      {
        try
        {
          return Convert.ToDecimal(obj).GetHashCode();
        }
        catch (OverflowException) { } // NaN, infinity or out of range, these can only equal themselves
      }

      return obj.GetHashCode();
    }

    /// <summary>
    /// Integer types (except ulongs that do not fit in a long) without converting them to decimal
    /// </summary>
    private static bool TryGetInteger(object value, out long result)
    {
      switch (value)
      {
        case byte b: result = b; return true;
        case int i: result = i; return true;
        case sbyte sb: result = sb; return true;
        case short s: result = s; return true;
        case ushort us: result = us; return true;
        case uint ui: result = ui; return true;
        case long l: result = l; return true;
        case ulong ul when ul <= long.MaxValue: result = (long)ul; return true;
      }
      result = 0;
      return false;
    }
  }
}
