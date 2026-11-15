using LsMsgPack.Meta;
using System;
using System.Collections.Generic;

namespace LsMsgPack
{
  /// <summary>
  /// The members of the data that a class does not have, kept when it is read and written again when it is written (docs/ExtensionData.md).
  /// A property or field of this type is found by its type (public, or any with <see cref="MsgPackExtensionDataAttribute"/>) and is never a property in the data itself.
  /// <para>Each value is one MsgPack value as raw bytes, in the byte order of the specification and without the indexed schema: objects are maps keyed by their names and type ids are type names,
  /// so any reader (also one without the schema) can read them. Numbers keep their width, strings, binary data, extensions and timestamps their format.</para>
  /// <para>Kept by name (a key of a map, or a name of the schema), or by position: the values after the last property of an object written as an array without the indexed schema
  /// (<see cref="ObjectLayout.Array"/>), written again only in that layout.</para>
  /// </summary>
  public sealed class MsgPackExtensionData
  {
    private readonly List<Entry> _entries = new List<Entry>();

    internal struct Entry
    {
      internal readonly string Name;
      internal readonly int Position;
      internal readonly byte[] Value;

      internal Entry(string name, int position, byte[] value)
      {
        Name = name;
        Position = position;
        Value = value;
      }
    }

    /// <summary>
    /// The entries in the order they were read or set.
    /// </summary>
    internal List<Entry> Entries { get { return _entries; } }

    /// <summary>
    /// The number of values kept (by name and by position).
    /// </summary>
    public int Count { get { return _entries.Count; } }

    /// <summary>
    /// The names of the values kept by name, in the order they were read or set.
    /// </summary>
    public IReadOnlyList<string> Names
    {
      get
      {
        List<string> names = new List<string>(_entries.Count);
        for (int t = 0; t < _entries.Count; t++)
          if (_entries[t].Name != null)
            names.Add(_entries[t].Name);
        return names;
      }
    }

    /// <summary>
    /// The positions of the values kept by position (after the last property of an object written as an array without the indexed schema), in the order they were read or set.
    /// </summary>
    public IReadOnlyList<int> Positions
    {
      get
      {
        List<int> positions = new List<int>();
        for (int t = 0; t < _entries.Count; t++)
          if (_entries[t].Name is null)
            positions.Add(_entries[t].Position);
        return positions;
      }
    }

    /// <summary>
    /// The raw MsgPack value kept for the name (not a copy: do not change it).
    /// </summary>
    public bool TryGetRaw(string name, out byte[] value)
    {
      int index = IndexOf(name);
      value = index >= 0 ? _entries[index].Value : null;
      return index >= 0;
    }

    /// <summary>
    /// The raw MsgPack value kept for the position in the array (not a copy: do not change it).
    /// </summary>
    public bool TryGetRaw(int position, out byte[] value)
    {
      int index = IndexOf(position);
      value = index >= 0 ? _entries[index].Value : null;
      return index >= 0;
    }

    /// <summary>
    /// Keeps a value for the name, replacing the one it had.
    /// </summary>
    /// <param name="value">Exactly one MsgPack value, in the byte order of the specification, without the indexed schema (objects as maps keyed by names). Kept as it is (not copied).</param>
    /// <exception cref="ArgumentException">The name is empty or "@" (keys of the format), or the value is not exactly one MsgPack value</exception>
    public void SetRaw(string name, byte[] value)
    {
      if (name is null)
        throw new ArgumentNullException(nameof(name));
      if (name.Length == 0 || name == MsgPackOptions.ContentKey)
        throw new ArgumentException($"\"{name}\" is a key of the format (\"\" holds type ids, \"@\" the content of a wrapped value), not a name of a member.", nameof(name));
      KeptValues.CheckOneValue(value, nameof(value));
      int index = IndexOf(name);
      if (index >= 0)
        _entries[index] = new Entry(name, -1, value);
      else
        _entries.Add(new Entry(name, -1, value));
    }

    /// <summary>
    /// Keeps a value for the position in the array (written only in the array layout without the indexed schema, after the properties), replacing the one it had.
    /// </summary>
    /// <param name="value">Exactly one MsgPack value, in the byte order of the specification. Kept as it is (not copied).</param>
    public void SetRaw(int position, byte[] value)
    {
      if (position < 0)
        throw new ArgumentOutOfRangeException(nameof(position), position, "A position in an array is not negative.");
      KeptValues.CheckOneValue(value, nameof(value));
      int index = IndexOf(position);
      if (index >= 0)
        _entries[index] = new Entry(null, position, value);
      else
        _entries.Add(new Entry(null, position, value));
    }

    public bool Remove(string name)
    {
      int index = IndexOf(name);
      if (index >= 0)
        _entries.RemoveAt(index);
      return index >= 0;
    }

    public bool Remove(int position)
    {
      int index = IndexOf(position);
      if (index >= 0)
        _entries.RemoveAt(index);
      return index >= 0;
    }

    public void Clear()
    {
      _entries.Clear();
    }

    /// <summary>
    /// Kept while reading: the value read last wins (a key twice in a map).
    /// </summary>
    internal void Keep(string name, int position, byte[] value)
    {
      int index = name is null ? IndexOf(position) : IndexOf(name);
      if (index >= 0)
        _entries[index] = new Entry(name, position, value);
      else
        _entries.Add(new Entry(name, position, value));
    }

    private int IndexOf(string name)
    {
      if (name is null)
        return -1;
      for (int t = 0; t < _entries.Count; t++)
        if (string.Equals(_entries[t].Name, name, StringComparison.Ordinal))
          return t;
      return -1;
    }

    private int IndexOf(int position)
    {
      for (int t = 0; t < _entries.Count; t++)
        if (_entries[t].Name is null && _entries[t].Position == position)
          return t;
      return -1;
    }
  }

  /// <summary>
  /// Marks the member that keeps the members of the data the class does not have (docs/ExtensionData.md): a <see cref="MsgPackExtensionData"/> (found by its type when it is public, so the attribute is only needed
  /// on a non-public one), or an <see cref="IDictionary{TKey, TValue}"/> of string to object, which gets the values as plain .NET values (as System.Text.Json's <c>[JsonExtensionData]</c>,
  /// which is recognized too, as Json.NET's).
  /// </summary>
  [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
  public sealed class MsgPackExtensionDataAttribute : Attribute
  {
  }
}
