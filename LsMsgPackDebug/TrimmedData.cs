using LsMsgPack;
using System;
using System.Collections.Generic;
using System.IO;

namespace LsMsgPackDebug
{
  /// <summary>
  /// Bytes of the data that were not kept (<see cref="MsgPackSettings.SkippedRanges"/>).
  /// </summary>
  public struct SkippedRange
  {
    public SkippedRange(long offset, long length)
    {
      Offset = offset;
      Length = length;
    }

    /// <summary>
    /// Where the bytes start in the data.
    /// </summary>
    public long Offset { get; }
    public long Length { get; }
  }

  /// <summary>
  /// The data as the explorers keep it in memory: without the middle of the values that are too large for practical debugging, which may be more than a .NET array can hold.
  /// Items have offsets in the original data, the hex views show <see cref="Bytes"/>: this converts between them.
  /// </summary>
  public sealed class TrimmedData
  {
    private readonly long[] _indexes; // per range of Skipped: the index in Bytes of the byte after it

    private TrimmedData(long length, byte[] bytes, SkippedRange[] skipped)
    {
      Length = length;
      Bytes = bytes;
      Skipped = skipped;
      _indexes = new long[skipped.Length];
      long passed = 0;
      for (int t = 0; t < skipped.Length; t++)
      {
        _indexes[t] = skipped[t].Offset - passed;
        passed += skipped[t].Length;
      }
    }

    /// <summary>
    /// The length of the original data.
    /// </summary>
    public long Length { get; }

    /// <summary>
    /// The bytes that were kept.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// What is not in <see cref="Bytes"/> in the order of the data.
    /// </summary>
    public SkippedRange[] Skipped { get; }

    /// <summary>
    /// The index in <see cref="Skipped"/> of the first range that ends after the offset, the length of it when there is none.
    /// </summary>
    private int RangeAfter(long offset)
    {
      int low = 0;
      int high = Skipped.Length;
      while (low < high)
      {
        int middle = (low + high) >> 1;
        if (Skipped[middle].Offset + Skipped[middle].Length <= offset)
          low = middle + 1;
        else
          high = middle;
      }
      return low;
    }

    /// <summary>
    /// The index in <see cref="Bytes"/> of a byte of the data, a skipped byte gets the index of the next byte that was kept (the length of <see cref="Bytes"/> when there is none).
    /// </summary>
    public int ToIndex(long offset)
    {
      int range = RangeAfter(offset);
      long index;
      if (range < Skipped.Length && offset >= Skipped[range].Offset)
        index = _indexes[range];
      else
        index = offset - (range == 0 ? 0 : Skipped[range - 1].Offset + Skipped[range - 1].Length - _indexes[range - 1]);
      return (int)Math.Min(index, Bytes.Length);
    }

    /// <summary>
    /// The byte at an offset in the data, -1 when it was not kept or is outside the data.
    /// </summary>
    public int At(long offset)
    {
      if (offset < 0 || offset >= Length)
        return -1;
      int range = RangeAfter(offset);
      if (range < Skipped.Length && offset >= Skipped[range].Offset)
        return -1;
      return Bytes[ToIndex(offset)];
    }

    /// <summary>
    /// A copy of a part of the data, null when not all of it was kept.
    /// </summary>
    public byte[] Copy(long offset, long length)
    {
      if (offset < 0 || length < 0 || offset + length > Length || length > Bytes.Length)
        return null;
      int range = RangeAfter(offset);
      if (range < Skipped.Length && offset + length > Skipped[range].Offset)
        return null;
      byte[] copy = new byte[length];
      Array.Copy(Bytes, ToIndex(offset), copy, 0, copy.Length);
      return copy;
    }

    /// <summary>
    /// The data that is in memory anyway.
    /// </summary>
    /// <param name="skipped">Null or <see cref="MsgPackSettings.SkippedRanges"/></param>
    /// <param name="maxBytes">Bytes after this many are left out as well (<see cref="Skipped"/> ends with them)</param>
    public static TrimmedData FromBytes(byte[] data, IList<SkippedRange> skipped, long maxBytes = long.MaxValue)
    {
      if ((skipped is null || skipped.Count == 0) && data.Length <= maxBytes)
        return new TrimmedData(data.Length, data, new SkippedRange[0]);
      using (MemoryStream stream = new MemoryStream(data, false))
        return Read(stream, skipped, maxBytes);
    }

    /// <summary>
    /// Reads what was kept from a stream that seeks (a file), its position changes.
    /// </summary>
    /// <param name="skipped">Null or <see cref="MsgPackSettings.SkippedRanges"/></param>
    /// <param name="maxBytes">Bytes after this many are left out as well (<see cref="Skipped"/> ends with them): what stays in memory is limited whatever the data holds</param>
    public static TrimmedData Read(Stream data, IList<SkippedRange> skipped, long maxBytes = long.MaxValue)
    {
      const long maxArray = 0x7FFFFFC7;
      long max = Math.Min(maxBytes, maxArray);
      long length = data.Length;
      List<SkippedRange> ranges = Merge(skipped, length);

      // The kept parts are between the ranges: what does not fit becomes the last range
      long kept = 0;
      long position = 0;
      for (int t = 0; t <= ranges.Count; t++)
      {
        long next = t < ranges.Count ? ranges[t].Offset : length;
        if (kept + next - position > max)
        {
          long from = position + max - kept;
          ranges.RemoveRange(t, ranges.Count - t);
          ranges.Add(new SkippedRange(from, length - from));
          kept = max;
          break;
        }
        kept += next - position;
        if (t < ranges.Count)
          position = ranges[t].Offset + ranges[t].Length;
      }

      byte[] bytes = new byte[kept];
      int at = 0;
      position = 0;
      for (int t = 0; t <= ranges.Count && at < bytes.Length; t++)
      {
        long next = t < ranges.Count ? ranges[t].Offset : length;
        data.Position = position;
        int count = (int)(next - position);
        while (count > 0)
        {
          int read = data.Read(bytes, at, count);
          if (read <= 0)
            throw new EndOfStreamException("The data got shorter while it was read.");
          at += read;
          count -= read;
        }
        if (t < ranges.Count)
          position = ranges[t].Offset + ranges[t].Length;
      }
      return new TrimmedData(length, bytes, ranges.ToArray());
    }

    /// <summary>
    /// In the order of the data without overlaps (the index of a stream is read twice).
    /// </summary>
    private static List<SkippedRange> Merge(IList<SkippedRange> skipped, long length)
    {
      List<SkippedRange> sorted = skipped is null ? new List<SkippedRange>() : new List<SkippedRange>(skipped);
      sorted.Sort((a, b) => a.Offset.CompareTo(b.Offset));
      List<SkippedRange> merged = new List<SkippedRange>(sorted.Count);
      for (int t = 0; t < sorted.Count; t++)
      {
        long start = sorted[t].Offset;
        long end = Math.Min(start + sorted[t].Length, length);
        if (end <= start)
          continue;
        if (merged.Count > 0 && start <= merged[merged.Count - 1].Offset + merged[merged.Count - 1].Length)
        {
          SkippedRange last = merged[merged.Count - 1];
          merged[merged.Count - 1] = new SkippedRange(last.Offset, Math.Max(last.Offset + last.Length, end) - last.Offset);
        }
        else
          merged.Add(new SkippedRange(start, end - start));
      }
      return merged;
    }
  }
}
