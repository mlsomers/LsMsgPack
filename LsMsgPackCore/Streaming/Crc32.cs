using System;

namespace LsMsgPack.Streaming
{
  /// <summary>
  /// CRC-32 (IEEE 802.3, as zip and PNG: reflected polynomial 0xEDB88320, initial and final value 0xFFFFFFFF) of the index of a stream (<see cref="MsgPackStreamIndex"/>).
  /// A table of 256 values: indexes are small and written rarely, System.IO.Hashing would be a dependency for this alone.
  /// </summary>
  internal static class Crc32
  {
    private static readonly uint[] Table = CreateTable();

    private static uint[] CreateTable()
    {
      uint[] table = new uint[256];
      for (uint n = 0; n < 256; n++)
      {
        uint c = n;
        for (int k = 0; k < 8; k++)
          c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        table[n] = c;
      }
      return table;
    }

    internal static uint Compute(ReadOnlySpan<byte> data)
    {
      uint[] table = Table;
      uint crc = 0xFFFFFFFF;
      for (int i = 0; i < data.Length; i++)
        crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
      return crc ^ 0xFFFFFFFF;
    }
  }
}
