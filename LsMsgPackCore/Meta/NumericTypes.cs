using System;
using System.Collections.Generic;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The .NET number types (equal across types when used as map keys, see <see cref="MapConversionEqualityComparer"/>).
  /// </summary>
  internal static class NumericTypes
  {
    internal static readonly HashSet<Type> All = new HashSet<Type>(new[]
    {
     typeof(sbyte),
     typeof(short),
     typeof(int),
     typeof(long),
     typeof(byte),
     typeof(ushort),
     typeof(uint),
     typeof(ulong),
     typeof(float),
     typeof(double),
     typeof(decimal),
    });
  }
}
