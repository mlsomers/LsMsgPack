using System;
using System.Collections.Generic;

namespace LsMsgPack.Meta
{
  public class MapConversionEqualityComparer : IEqualityComparer<object>
  {
    public new bool Equals(object x, object y)
    {
      if (x == y)
        return true;

      if (x == null || y == null)
        return false;

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
      return obj.GetHashCode();
    }
  }
}
