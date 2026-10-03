using System.Collections.Generic;

namespace LsMsgPack.Meta
{
  public static class MissingExtensions
  {
    public static bool TryAdd<Ta, Tb>(this Dictionary<Ta, Tb> dict, Ta key, Tb val)
    {
      if (dict.ContainsKey(key))
        return false;
      dict.Add(key, val);
      return true;
    }
  }
}
