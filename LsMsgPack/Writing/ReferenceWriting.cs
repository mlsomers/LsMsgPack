using LsMsgPack.IO;
using LsMsgPack.References;
using System;
using System.Collections.Generic;

namespace LsMsgPack.Writing
{
  /// <summary>
  /// <see cref="ReferenceHandling.Preserve"/> while writing one value (LsMsgPackDebug: MsgPackSerializer.SerializeReferenced): the objects and collections written, each where it starts.
  /// A repeat is written as a placeholder (fixext 4 of type 5 holding the target's number), and when the value is complete one walk over its bytes finds the paths and writes the references
  /// (<see cref="Finish"/>): the bytes before a repeat may still move (a reserved header patched smaller, <see cref="Moved"/>; inline mode copies the elements of a wrapped collection from another
  /// writer, <see cref="Copied"/>), the structure does not.
  /// </summary>
  internal sealed class ReferenceWriting
  {
    private const int PlaceholderLength = 6; // d6 05 + 4 bytes

    private readonly Dictionary<object, int> _targets = new Dictionary<object, int>(IdentityComparer.Instance);
    private MsgPackWriter[] _writers = new MsgPackWriter[16];
    private int[] _offsets = new int[16];
    private bool[] _referenced = new bool[16];
    private int _count;
    private bool _anyReference;
    private int _placeholderCount; // written in this attempt: the walk stops after the last one

    /// <summary>
    /// Above 0 while writing the keys of a map: a key is always written in full (a path step stands for an entry's value).
    /// </summary>
    internal int InKeys;

    /// <summary>
    /// <see cref="WriteContext.ReferencePrefix"/> of a value inside a key of the body of a stream head: written in full, nothing tracked (as a key of a whole root value).
    /// </summary>
    internal static readonly int[] InKey = new int[0];

    /// <summary>
    /// The surrogate about to be written for a converted value: the converted value keeps the identity, its surrogate is not tracked (as LsMsgPackDebug's SerializeSurrogate).
    /// </summary>
    internal object Surrogate;

    /// <summary>
    /// At the start of every attempt (a write with schema references may be repeated).
    /// </summary>
    internal void Start()
    {
      if (_count > 0 || _targets.Count > 0)
      {
        _targets.Clear();
        Array.Clear(_writers, 0, _count);
        Array.Clear(_referenced, 0, _count);
        _count = 0;
      }
      _anyReference = false;
      _placeholderCount = 0;
      InKeys = 0;
      Surrogate = null;
    }

    /// <returns>The number of the earlier occurrence (write a reference to it with <see cref="WritePlaceholder"/>), or -1: the first, registered where the writer is now</returns>
    internal int Find(object value, MsgPackWriter w)
    {
      if (InKeys != 0)
        return -1;
      if (ReferenceEquals(value, Surrogate))
      {
        Surrogate = null;
        return -1;
      }
      if (_targets.TryGetValue(value, out int target))
        return target;
      if (_count == _offsets.Length)
      {
        Array.Resize(ref _writers, _count * 2);
        Array.Resize(ref _offsets, _count * 2);
        Array.Resize(ref _referenced, _count * 2);
      }
      _writers[_count] = w;
      _offsets[_count] = w.Pos;
      _targets.Add(value, _count++);
      return -1;
    }

    /// <summary>
    /// Whether the value is tracked here (not a key, not a surrogate) without registering it: for the callers that decide about the type id first.
    /// </summary>
    internal bool IsRepeat(object value, out int target)
    {
      target = -1;
      return InKeys == 0 && !ReferenceEquals(value, Surrogate) && _targets.TryGetValue(value, out target);
    }

    internal void WritePlaceholder(MsgPackWriter w, int target)
    {
      _referenced[target] = true;
      _anyReference = true;
      _placeholderCount++;
      w.Ensure(PlaceholderLength);
      byte[] buf = w.Buf;
      int at = w.Pos;
      buf[at] = 0xd6;
      buf[at + 1] = (byte)MsgPackOptions.ReferenceExtensionType;
      buf[at + 2] = (byte)(target >> 24);
      buf[at + 3] = (byte)(target >> 16);
      buf[at + 4] = (byte)(target >> 8);
      buf[at + 5] = (byte)target;
      w.Pos = at + PlaceholderLength;
    }

    /// <summary>
    /// <see cref="MsgPackWriter"/> moved the bytes of <paramref name="writer"/> from <paramref name="from"/> to its end by <paramref name="delta"/> (a reserved header patched): the objects written there since.
    /// </summary>
    internal void Moved(MsgPackWriter writer, int from, int delta)
    {
      for (int t = _count - 1; t >= 0 && ReferenceEquals(_writers[t], writer) && _offsets[t] >= from; t--)
        _offsets[t] += delta;
    }

    /// <summary>
    /// The content of <paramref name="from"/> (rented for the elements of a wrapped collection) was copied to <paramref name="to"/> at <paramref name="at"/>.
    /// </summary>
    internal void Copied(MsgPackWriter from, MsgPackWriter to, int at)
    {
      for (int t = _count - 1; t >= 0 && ReferenceEquals(_writers[t], from); t--)
      {
        _writers[t] = to;
        _offsets[t] += at;
      }
    }

    /// <summary>
    /// The value is complete in <paramref name="body"/> from <paramref name="start"/>: the paths of the objects referred to, and the references in place of the placeholders.
    /// </summary>
    /// <param name="prefix">The value's place in a larger root value (the body of a stream head: its position there), the start of every path; null at the root</param>
    internal void Finish(MsgPackWriter body, int start, LsMsgPackOptions options, int[] prefix)
    {
      if (!_anyReference)
        return;
      for (int t = 0; t < _count; t++) // the first ones are where they will stay
      {
        if (!ReferenceEquals(_writers[t], body))
          throw new InvalidOperationException("A tracked value was not copied into the body."); // a bug
      }

      byte[] bytes = new byte[body.Pos - start];
      Buffer.BlockCopy(body.Buf, start, bytes, 0, bytes.Length);
      Walker walker = new Walker(this, bytes, start, options);
      if (prefix != null)
        walker.Prefix(prefix);
      walker.Walk(0);
      body.Pos = start;
      walker.WriteTo(body);
    }

    /// <summary>
    /// One pass over the bytes of the value: the path of every value start, kept for the objects referred to; the bytes copied, with the references in place of the placeholders.
    /// </summary>
    private sealed class Walker
    {
      private readonly ReferenceWriting _table;
      private readonly byte[] _bytes;
      private readonly int _start;
      private readonly MsgPackReader _r;
      private readonly List<int> _path = new List<int>();
      private readonly int[][] _paths;
      private readonly List<int> _placeholders = new List<int>();
      private int _next; // the next tracked value (they are registered in the order of the bytes)

      internal Walker(ReferenceWriting table, byte[] bytes, int start, LsMsgPackOptions options)
      {
        _table = table;
        _bytes = bytes;
        _start = start;
        _r = new MsgPackReader(bytes, 0, bytes.Length, options);
        _paths = new int[table._count][];
      }

      internal void Prefix(int[] prefix)
      {
        _path.AddRange(prefix);
      }

      /// <summary>
      /// Every placeholder was found: what comes after the last one is copied as it is (<see cref="WriteTo"/>), the walk stops.
      /// </summary>
      private bool Done
      {
        get { return _placeholders.Count == _table._placeholderCount; }
      }

      internal void Walk(int depth)
      {
        int at = _r.Pos;
        while (_next < _table._count && _table._offsets[_next] - _start <= at)
        {
          if (_table._offsets[_next] - _start == at && _table._referenced[_next])
            _paths[_next] = _path.ToArray();
          _next++;
        }

        byte b = _bytes[at];
        if ((b & 0xF0) == 0x90 || b == 0xDC || b == 0xDD)
        {
          int count = _r.TryReadArrayHeader();
          for (int t = 0; t < count && !Done; t++)
          {
            _path.Add(t);
            Walk(depth + 1);
            _path.RemoveAt(_path.Count - 1);
          }
          return;
        }
        if ((b & 0xF0) == 0x80 || b == 0xDE || b == 0xDF)
        {
          int count = _r.TryReadMapHeader();
          for (int t = 0; t < count && !Done; t++)
          {
            _r.Skip(depth + 1); // a key is never tracked
            _path.Add(t);
            Walk(depth + 1);
            _path.RemoveAt(_path.Count - 1);
          }
          return;
        }
        if (b == 0xD6 && _bytes[at + 1] == (byte)MsgPackOptions.ReferenceExtensionType) // a placeholder (an extension of type 5 is nothing else: refused with the option)
        {
          _placeholders.Add(at);
          _r.Pos = at + PlaceholderLength;
          return;
        }
        _r.Skip(depth);
      }

      internal void WriteTo(MsgPackWriter w)
      {
        int copied = 0;
        for (int t = 0; t < _placeholders.Count; t++)
        {
          int at = _placeholders[t];
          w.Raw(_bytes, copied, at - copied);
          int target = _bytes[at + 2] << 24 | _bytes[at + 3] << 16 | _bytes[at + 4] << 8 | _bytes[at + 5];
          int[] path = _paths[target];
          if (path is null)
            throw new InvalidOperationException("A reference to a value that was not found in the bytes written."); // a bug
          w.Extension(MsgPackOptions.ReferenceExtensionType, ReferencePath.Encode(path));
          copied = at + PlaceholderLength;
        }
        w.Raw(_bytes, copied, _bytes.Length - copied);
      }
    }
  }
}
