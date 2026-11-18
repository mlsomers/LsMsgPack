using System;
using System.Runtime.CompilerServices;

namespace LsMsgPack.References
{
  /// <summary>
  /// Objects by identity to an int, for the tables of <see cref="ReferenceHandling.Preserve"/> (every object and collection written or read goes in): open addressing over
  /// <see cref="RuntimeHelpers.GetHashCode(object)"/>, so a lookup is one probe sequence without the calls through an <see cref="System.Collections.Generic.IEqualityComparer{T}"/>
  /// that a Dictionary with <see cref="IdentityComparer"/> makes, and find-or-add is a single lookup. Nothing is removed; <see cref="Clear"/> empties only the slots used.
  /// </summary>
  internal sealed class IdentityMap
  {
    private object[] _keys;
    private int[] _values;
    private int[] _used; // the slots filled, in order
    private int _count;
    private int _shift; // 32 - log2(capacity): the top bits of the multiplied hash pick the slot

    internal IdentityMap()
    {
      Allocate(32);
    }

    internal int Count { get { return _count; } }

    private void Allocate(int capacity)
    {
      _keys = new object[capacity];
      _values = new int[capacity];
      _used = new int[capacity >> 1]; // at most half full
      int bits = 0;
      while ((1 << bits) < capacity)
        bits++;
      _shift = 32 - bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int SlotOf(object key)
    {
      return (int)((uint)RuntimeHelpers.GetHashCode(key) * 0x9E3779B9u >> _shift); // the runtime's hash codes are random per object; multiplied, the slot does not depend on the low bits only
    }

    internal bool TryGetValue(object key, out int value)
    {
      object[] keys = _keys;
      int mask = keys.Length - 1;
      for (int slot = SlotOf(key); ; slot = (slot + 1) & mask)
      {
        object found = keys[slot];
        if (found is null)
        {
          value = 0;
          return false;
        }
        if (ReferenceEquals(found, key))
        {
          value = _values[slot];
          return true;
        }
      }
    }

    /// <summary>
    /// Adds the key with <paramref name="value"/>, or (already there) gives its value.
    /// </summary>
    /// <returns>true when it was added</returns>
    internal bool TryAdd(object key, int value, out int existing)
    {
      object[] keys = _keys;
      int mask = keys.Length - 1;
      int slot = SlotOf(key);
      for (; ; slot = (slot + 1) & mask)
      {
        object found = keys[slot];
        if (found is null)
          break;
        if (ReferenceEquals(found, key))
        {
          existing = _values[slot];
          return false;
        }
      }
      existing = 0;
      if (_count == _used.Length)
      {
        Grow();
        Insert(key, value);
        return true;
      }
      keys[slot] = key;
      _values[slot] = value;
      _used[_count++] = slot;
      return true;
    }

    /// <summary>
    /// Adds the key, or replaces its value.
    /// </summary>
    internal void Set(object key, int value)
    {
      object[] keys = _keys;
      int mask = keys.Length - 1;
      for (int slot = SlotOf(key); ; slot = (slot + 1) & mask)
      {
        object found = keys[slot];
        if (found is null)
          break;
        if (ReferenceEquals(found, key))
        {
          _values[slot] = value;
          return;
        }
      }
      if (_count == _used.Length)
        Grow();
      Insert(key, value);
    }

    /// <summary>
    /// A key that is not there yet, with room for it.
    /// </summary>
    private void Insert(object key, int value)
    {
      object[] keys = _keys;
      int mask = keys.Length - 1;
      int slot = SlotOf(key);
      while (!(keys[slot] is null))
        slot = (slot + 1) & mask;
      keys[slot] = key;
      _values[slot] = value;
      _used[_count++] = slot;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow()
    {
      object[] keys = _keys;
      int[] values = _values;
      int[] used = _used;
      int count = _count;
      Allocate(keys.Length * 2);
      _count = 0;
      for (int t = 0; t < count; t++) // in the order they were added, so Clear keeps emptying the slots used
        Insert(keys[used[t]], values[used[t]]);
    }

    internal void Clear()
    {
      object[] keys = _keys;
      int[] used = _used;
      for (int t = _count - 1; t >= 0; t--)
        keys[used[t]] = null;
      _count = 0;
    }
  }
}
