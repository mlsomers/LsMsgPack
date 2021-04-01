using System;

namespace TestDateTime {
  class Program {
    static void Main(string[] args) {
      DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

      DateTime MaxFExt4 = epoch.AddSeconds(uint.MaxValue);
      Console.WriteLine(string.Concat("Timestamp 32 : ", epoch.ToString("yyyy-MM-dd HH:mm:ss.fffffff"), " - ", MaxFExt4.ToString("yyyy-MM-dd HH:mm:ss.fffffff")));

      long maxSecs = ((long)uint.MaxValue << 2) | 3; // 17179869183
      long maxNanoSec = 999999999; // not uint.MaxValue
      TimeSpan maxNanoSecSpan = new TimeSpan(maxNanoSec * 100); // 1 tick = 100 nanosec.
      DateTime MaxFExt8 = epoch.AddSeconds(maxSecs).Add(maxNanoSecSpan);
      Console.WriteLine(string.Concat("Timestamp 64 : ", epoch.ToString("yyyy-MM-dd HH:mm:ss.fffffff"), " - ", MaxFExt8.ToString("yyyy-MM-dd HH:mm:ss.fffffff")));

      // DateTime MaxExt8 = Zero.AddSeconds(long.MaxValue).Add(maxNanoSecSpan); // System.ArgumentOutOfRangeException
      DateTime MinExt8 = DateTime.MinValue;
      // DateTime MinExt8 = Zero.AddSeconds(long.MinValue).Add(-maxNanoSecSpan); // System.ArgumentOutOfRangeException
      DateTime MaxExt8 = DateTime.MaxValue;
      Console.WriteLine(string.Concat("Timestamp 96 : ", MinExt8.ToString("yyyy-MM-dd HH:mm:ss.fffffff"), " - ", MaxExt8.ToString("yyyy-MM-dd HH:mm:ss.fffffff")));

      // output:
      // Timestamp 32 : 1970-01-01 00:00:00.0000000 - 2106-02-07 06:28:15.0000000
      // Timestamp 64 : 1970-01-01 00:00:00.0000000 - 2514-05-30 04:39:42.9999900
      // Timestamp 96 : 0001-01-01 00:00:00.0000000 - 9999-12-31 23:59:59.9999999
    }
  }
}
