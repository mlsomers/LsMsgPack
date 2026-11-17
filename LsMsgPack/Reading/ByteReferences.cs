using LsMsgPack.IO;
using LsMsgPack.Meta;
using LsMsgPack.References;
using System;
using System.Collections.Generic;

namespace LsMsgPack.Reading
{
  /// <summary>
  /// The references of one call (LsMsgPackDebug: PlainReferences): a path is followed through the bytes from the root value (the starts of the elements found are kept per array or map,
  /// so each is skipped once), the objects and collections are registered by where their value starts (<see cref="ReferenceHandling.Preserve"/>). A value that was not made (a member the class
  /// does not have, or a reader without the option) is read again from its bytes.
  /// </summary>
  internal sealed class ByteReferences : ReferenceResolver
  {
    private readonly ReadContext _c;
    private readonly int _bodyStart;
    private readonly bool _preserves;
    private readonly MsgPackReader _walker;
    private readonly Dictionary<int, object> _made = new Dictionary<int, object>();
    private readonly Dictionary<int, Container> _containers = new Dictionary<int, Container>();
    private Dictionary<object, int> _plainOffsets;
    private HashSet<int> _reading;
    private readonly int[] _prefix;

    internal ByteReferences(ReadContext c, int bodyStart, bool preserves)
    {
      _c = c;
      _bodyStart = bodyStart;
      _preserves = preserves;
      _walker = new MsgPackReader(c.R.Buf, bodyStart, c.R.End, c.Serializer.Options);
      _prefix = c.ReferencePrefix?.Invoke();
    }

    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/>: registers what Core makes of the plain values read by MsgPackReader.ReadPlain (<see cref="Note"/>).
    /// </summary>
    internal bool Preserves { get { return _preserves; } }

    /// <summary>
    /// Where a plain array or map read by MsgPackReader.ReadPlain starts (<see cref="ReferenceHandling.Preserve"/>): Core registers by the plain value.
    /// </summary>
    internal void Note(object plain, int start)
    {
      (_plainOffsets ?? (_plainOffsets = new Dictionary<object, int>(IdentityComparer.Instance)))[plain] = start;
    }

    /// <summary>
    /// The object or collection read from the value at <paramref name="start"/>: the last one wins (a value that was read again by LsMsgPack.Core, Slow).
    /// </summary>
    internal void Register(int start, object made)
    {
      if (made != null && _preserves) // a reader without the option reads every reference again (copies): the collection readers register whenever there is a table
        _made[start] = made;
    }

    /// <summary>
    /// The object at <paramref name="start"/> is read again from its start (ObjectReader.Restart): the one read so far is not it.
    /// </summary>
    internal void Unregister(int start)
    {
      _made.Remove(start);
    }

    internal override void Register(object plain, object created, bool replace)
    {
      if (_plainOffsets != null && _plainOffsets.TryGetValue(plain, out int start))
        _made[start] = created;
    }

    internal override bool TryGetMade(object plain, Type type, out object made)
    {
      made = null;
      return _plainOffsets != null && _plainOffsets.TryGetValue(plain, out int start) && TryGetMade(start, type, out made);
    }

    /// <summary>
    /// Whether the value at <paramref name="start"/> was made already: a reference was resolved before it (LsMsgPack.Core converts values last to first) and read it from its bytes.
    /// Reading it again would make a second instance, which replaces the first one here.
    /// </summary>
    internal bool TryGetMade(int start, Type type, out object made)
    {
      return _made.TryGetValue(start, out made) && (Nullable.GetUnderlyingType(type) ?? type).IsInstanceOfType(made);
    }

    internal override object Resolve(MsgPackReference reference, Type type, FullPropertyInfo prop, MsgPackOptions settings)
    {
      return Resolve(reference.Offset, reference.Path, reference, type, prop, settings);
    }

    internal override object PlainAt(MsgPackReference reference, MsgPackOptions settings)
    {
      int target = reference.Path is null ? -1 : Follow(reference.Path);
      if (target < 0)
        throw ReadDifferences.NotConverted(settings, NoValue(reference));
      if (reference.Offset >= 0 && target >= reference.Offset)
        throw ReadDifferences.NotConverted(settings, NotEarlier(reference));
      MsgPackReader r = _c.R;
      int pos = r.Pos;
      try
      {
        r.Pos = target;
        return r.ReadPlain(_c.Depth); // a copy, its own references read as plain values too
      }
      finally
      {
        r.Pos = pos;
      }
    }

    /// <summary>
    /// The value a reference read at <paramref name="at"/> points to.
    /// </summary>
    internal object Resolve(int at, int[] path, MsgPackReference reference, Type type, FullPropertyInfo prop, MsgPackOptions settings)
    {
      if (reference is null)
        reference = new MsgPackReference(path is null ? new byte[0] : ReferencePath.Encode(path), this) { Offset = at };
      int target = path is null ? -1 : Follow(path);
      if (target < 0)
        throw ReadDifferences.NotConverted(settings, NoValue(reference));
      if (target >= at)
        throw ReadDifferences.NotConverted(settings, NotEarlier(reference));

      if (TryGetMade(target, type, out object made))
        return made;
      int content = ContentOf(target); // an object is registered where its content starts too (the plan of a type id)
      if (content >= 0 && TryGetMade(content, type, out made))
        return made;

      // Not made (a member the class does not have), or a reader without the option: read again
      if (_reading is null)
        _reading = new HashSet<int>();
      if (!_reading.Add(target))
        throw ReadDifferences.NotConverted(settings, Cycle(reference, type));
      MsgPackReader r = _c.R;
      int pos = r.Pos;
      try
      {
        r.Pos = target;
        return _c.Serializer.ReaderFor(type, prop).ReadBoxed(_c, prop);
      }
      finally
      {
        r.Pos = pos;
        _reading.Remove(target);
      }
    }

    /// <returns>Where the value at the path starts, -1 when there is none</returns>
    private int Follow(int[] path)
    {
      int at = _bodyStart;
      int first = 0;
      if (_prefix != null) // a value of a head's body read on its own: the paths start at the body's root, through this value (references never point into another value)
      {
        if (path.Length < _prefix.Length)
          return -1;
        for (int t = 0; t < _prefix.Length; t++)
          if (path[t] != _prefix[t])
            return -1;
        first = _prefix.Length;
      }
      for (int t = first; t < path.Length; t++)
      {
        Container container = ContainerAt(at);
        if (container is null)
          return -1;
        at = container.ValueStart(_walker, path[t]);
        if (at < 0)
          return -1;
      }
      return at;
    }

    private Container ContainerAt(int at)
    {
      if (_containers.TryGetValue(at, out Container container))
        return container;
      _walker.Pos = at;
      int count = _walker.TryReadArrayHeader();
      bool map = false;
      if (count < 0)
      {
        count = _walker.TryReadMapHeader();
        map = true;
      }
      if (count < 0)
        return null;
      container = new Container(map, count, _walker.Pos);
      _containers.Add(at, container);
      return container;
    }

    /// <returns>Where the content of a wrapper { "": typeId, "@": content } at <paramref name="at"/> starts, -1 when it is not one</returns>
    private int ContentOf(int at)
    {
      Container container = ContainerAt(at);
      if (container is null || !container.IsMap || container.Count < 2)
        return -1;
      byte[] buf = _walker.Buf;
      if (buf[container.First] != 0xA0) // the type id key "" first
        return -1;
      int content = container.ValueStart(_walker, 1);
      int key = container.KeyStart(1);
      return content >= 0 && buf[key] == 0xA1 && buf[key + 1] == (byte)'@' ? content : -1;
    }

    /// <summary>
    /// Whether the value at the reader's position is a reference (an extension of type 5).
    /// </summary>
    internal static bool IsReference(MsgPackReader r)
    {
      int at = r.Pos;
      if (at >= r.End)
        return false;
      byte[] buf = r.Buf;
      int type;
      switch (buf[at])
      {
        case 0xD4: case 0xD5: case 0xD6: case 0xD7: case 0xD8: type = at + 1; break;
        case 0xC7: type = at + 2; break;
        case 0xC8: type = at + 3; break;
        case 0xC9: type = at + 5; break;
        default: return false;
      }
      return type < r.End && buf[type] == MsgPackOptions.ReferenceExtensionType;
    }

    /// <summary>
    /// Reads the reference at the reader's position (<see cref="IsReference"/>) and resolves it.
    /// </summary>
    internal object ReadReference(Type type, FullPropertyInfo prop)
    {
      MsgPackReader r = _c.R;
      int at = r.Pos;
      r.TryReadExtHeader(out _, out int length, out _);
      byte[] content = new byte[length];
      Buffer.BlockCopy(r.Buf, r.Pos, content, 0, length);
      r.Pos += length;
      return Resolve(at, ReferencePath.Decode(content), null, type, prop, _c.ConversionSettings());
    }

    /// <summary>
    /// An array or map of the data: where its elements (or the values of its entries) start, found as far as a path needed them.
    /// </summary>
    private sealed class Container
    {
      internal readonly bool IsMap;
      internal readonly int Count;
      internal readonly int First; // the first element, or the first key
      private readonly List<int> _keys = new List<int>(); // maps: where each key starts
      private readonly List<int> _values = new List<int>();

      internal Container(bool map, int count, int first)
      {
        IsMap = map;
        Count = count;
        First = first;
      }

      internal int KeyStart(int index)
      {
        return _keys[index];
      }

      internal int ValueStart(MsgPackReader walker, int index)
      {
        if (index >= Count)
          return -1;
        while (_values.Count <= index)
        {
          int at = _values.Count == 0 ? First : _values[_values.Count - 1];
          walker.Pos = at;
          if (_values.Count > 0)
            walker.Skip(0); // the previous value
          if (IsMap)
          {
            _keys.Add(walker.Pos);
            walker.Skip(0);
          }
          _values.Add(walker.Pos);
        }
        return _values[index];
      }
    }
  }
}
