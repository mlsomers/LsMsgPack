using LsMsgPack.Meta;
using System;
using System.Collections.Generic;

namespace LsMsgPack.References
{
  /// <summary>
  /// The unpacked value of a reference (extension type <see cref="MsgPackOptions.ReferenceExtensionType"/>, see <see cref="ReferencePath"/>): what <see cref="ValueConverter"/> resolves
  /// to the value at its path. An extension too, so a binary converter of type code 5 (allowed without <see cref="ReferenceHandling.Preserve"/>) reads it as its own.
  /// </summary>
  internal sealed class MsgPackReference : IMsgPackExtension
  {
    /// <param name="resolver">The reader's own resolver (LsMsgPack), null: the one of the call (<see cref="ReferenceResolver.Current"/>)</param>
    internal MsgPackReference(byte[] data, ReferenceResolver resolver)
    {
      Data = data;
      Path = ReferencePath.Decode(data);
      Resolver = resolver;
    }

    /// <summary>
    /// The steps from the root value, null when the content is not a path.
    /// </summary>
    internal readonly int[] Path;

    internal readonly ReferenceResolver Resolver;

    /// <summary>
    /// Where LsMsgPack read it (its offset in the body), -1 otherwise.
    /// </summary>
    internal int Offset = -1;

    public sbyte TypeSpecifier { get { return MsgPackOptions.ReferenceExtensionType; } }

    public int Count { get { return Data.Length; } }

    public byte[] Data { get; }

    public MsgPackTypeId TypeId
    {
      get
      {
        switch (Data.Length)
        {
          case 1: return MsgPackTypeId.MpFExt1;
          case 2: return MsgPackTypeId.MpFExt2;
          case 4: return MsgPackTypeId.MpFExt4;
          case 8: return MsgPackTypeId.MpFExt8;
          case 16: return MsgPackTypeId.MpFExt16;
          default: return Data.Length <= 255 ? MsgPackTypeId.MpExt8 : MsgPackTypeId.MpExt16;
        }
      }
    }

    public override string ToString()
    {
      return "Reference to " + ReferencePath.Format(Path);
    }
  }

  /// <summary>
  /// Reading references (<see cref="ReferenceHandling.Preserve"/>): registers the objects and collections as they are created, and resolves a reference to the value at its path:
  /// the object made of it, or (not made yet, or a reader without the option) that value read again, a copy.
  /// <para>One per call (<see cref="Current"/> while Core converts; LsMsgPack's references carry their own), so a nested call has its own.</para>
  /// </summary>
  internal abstract class ReferenceResolver
  {
    /// <summary>
    /// The resolver of the call converting on this thread (LsMsgPackDebug: every call; LsMsgPack: its conversions in Core with <see cref="ReferenceHandling.Preserve"/>). Null otherwise.
    /// </summary>
    [ThreadStatic]
    internal static ReferenceResolver Current;

    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/>: the object or collection made of a plain value (an object[] or the entries of a map), as soon as it exists (before its members, so a cycle back to it resolves).
    /// </summary>
    /// <param name="replace">A converted value replaces its surrogate (registered first, by its own conversion)</param>
    internal abstract void Register(object plain, object created, bool replace);

    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/>: what was made of this plain value already. Core converts some values last to first, so a reference may have read it before its place is reached.
    /// </summary>
    internal abstract bool TryGetMade(object plain, Type type, out object made);

    internal abstract object Resolve(MsgPackReference reference, Type type, FullPropertyInfo prop, MsgPackOptions settings);

    /// <summary>
    /// The plain value at the reference's path (a copy where the reader reads it again): for extension data, which keeps values as they are.
    /// </summary>
    internal abstract object PlainAt(MsgPackReference reference, MsgPackOptions settings);

    /// <summary>
    /// Extension data (maintainer's decision 2026-11-16, option a): a kept value is self-contained, each reference in it is replaced by a copy of the value it points to. A path is absolute,
    /// so it would point elsewhere once an older class writes the value back among its own members. A cycle inside the kept value cannot be copied: an error (follows ReadErrors).
    /// </summary>
    internal static object Expand(object plain, MsgPackOptions settings)
    {
      return Expand(plain, settings, 0);
    }

    private static object Expand(object plain, MsgPackOptions settings, int depth)
    {
      if (depth > settings._maxDepth)
        throw ReadDifferences.NotConverted(settings, new MsgPackException($"A value kept as extension data refers to a value that contains it: a cycle cannot be kept (its references are replaced by copies of what they point to)."));
      if (plain is MsgPackReference reference)
      {
        ReferenceResolver resolver = reference.Resolver ?? Current;
        if (resolver is null)
          throw ReadDifferences.NotConverted(settings, new MsgPackException($"A reference ({reference}) can only be read as a part of the payload it is in."));
        return Expand(resolver.PlainAt(reference, settings), settings, depth + 1);
      }
      if (plain is object[] items)
      {
        object[] copy = null;
        for (int t = 0; t < items.Length; t++)
        {
          object expanded = Expand(items[t], settings, depth + 1);
          if (!ReferenceEquals(expanded, items[t]))
            (copy ?? (copy = (object[])items.Clone()))[t] = expanded;
        }
        return copy ?? items;
      }
      if (plain is KeyValuePair<object, object>[] entries)
      {
        if (ContentOf(entries) is MsgPackReference) // { "": typeId, "@": reference }: the value it points to has the type id itself when it is wrapped
        {
          object content = Expand(ContentOf(entries), settings, depth + 1);
          if (content is KeyValuePair<object, object>[] wrapped && ContentOf(wrapped) != null)
            return content;
        }
        KeyValuePair<object, object>[] copy = null;
        for (int t = 0; t < entries.Length; t++)
        {
          object expanded = Expand(entries[t].Value, settings, depth + 1);
          if (!ReferenceEquals(expanded, entries[t].Value))
            (copy ?? (copy = (KeyValuePair<object, object>[])entries.Clone()))[t] = new KeyValuePair<object, object>(entries[t].Key, expanded);
        }
        return copy ?? entries;
      }
      return plain;
    }

    internal static object ResolveValue(MsgPackReference reference, Type type, FullPropertyInfo prop, MsgPackOptions settings)
    {
      ReferenceResolver resolver = reference.Resolver ?? Current;
      if (resolver is null)
        throw ReadDifferences.NotConverted(settings, new MsgPackException($"A reference ({reference}, extension type {MsgPackOptions.ReferenceExtensionType}) can only be read as a part of the payload it is in (it points to a value before it)."));
      return resolver.Resolve(reference, type, prop, settings);
    }

    internal static void Registered(MsgPackOptions settings, object plain, object created, bool replace = false)
    {
      if (settings.PreservesReferences && !(created is null))
        Current?.Register(plain, created, replace);
    }

    /// <summary>
    /// Whether a value is a container a reference may point to (made of an array or map).
    /// </summary>
    internal static bool IsContainer(object plain)
    {
      return plain is object[] || plain is KeyValuePair<object, object>[];
    }

    /// <summary>
    /// The content of a wrapper { "": typeId, "@": content }, null when the map is not one: an object is registered by its content, a path points to the value as written (with its wrapper).
    /// </summary>
    internal static object ContentOf(KeyValuePair<object, object>[] map)
    {
      if (map.Length < 2 || !(map[0].Key is string first) || first.Length != 0)
        return null;
      for (int t = 1; t < map.Length; t++)
        if (map[t].Key is string key && key == MsgPackOptions.ContentKey)
          return map[t].Value;
      return null;
    }

    internal static MsgPackException NotEarlier(MsgPackReference reference)
    {
      return new MsgPackException($"A reference ({reference}) must point to a value before it, the path does not.");
    }

    internal static MsgPackException NoValue(MsgPackReference reference)
    {
      return new MsgPackException(reference.Path is null
        ? $"A reference (extension type {MsgPackOptions.ReferenceExtensionType}) holds no path: its content is not a run of positive integers."
        : $"A reference ({reference}) points to no value: the payload has nothing at that path.");
    }

    internal static MsgPackException Cycle(MsgPackReference reference, Type type)
    {
      return new MsgPackException($"A reference ({reference}) points back to a {type.Name} that contains it and that is not made yet: a cycle through an object made by a constructor with values, "
        + "a converted value or a collection made after its elements cannot be read (as with System.Text.Json's Preserve). Break the cycle, or give the class a parameterless constructor.");
    }
  }

  /// <summary>
  /// The references of a payload unpacked into plain values (LsMsgPackDebug): a path is followed through the object[]s and map entries from the root, objects are registered by the plain value they are made of.
  /// </summary>
  internal sealed class PlainReferences : ReferenceResolver
  {
    private readonly object _root;
    private Dictionary<object, object> _made;
    private HashSet<object> _reading;
    private Dictionary<object, int[]> _ownPaths;

    internal PlainReferences(object root)
    {
      _root = root;
    }

    internal override void Register(object plain, object created, bool replace)
    {
      if (_made is null)
        _made = new Dictionary<object, object>(IdentityComparer.Instance);
      if (replace)
        _made[plain] = created;
      else if (!_made.ContainsKey(plain)) // the first wins: a value read again into another type (a copy) leaves the shared one
        _made.Add(plain, created);
    }

    internal override bool TryGetMade(object plain, Type type, out object made)
    {
      if (_made != null && _made.TryGetValue(plain, out made) && Fits(type, made))
        return true;
      made = null;
      return false;
    }

    private static bool Fits(Type type, object made)
    {
      return (Nullable.GetUnderlyingType(type) ?? type).IsInstanceOfType(made);
    }

    internal override object PlainAt(MsgPackReference reference, MsgPackOptions settings)
    {
      bool found = false;
      object target = reference.Path is null ? null : Follow(reference.Path, out found);
      if (!found)
        throw ReadDifferences.NotConverted(settings, NoValue(reference));
      int[] own = OwnPath(reference);
      if (own != null && !IsBefore(reference.Path, own))
        throw ReadDifferences.NotConverted(settings, NotEarlier(reference));
      return target;
    }

    internal override object Resolve(MsgPackReference reference, Type type, FullPropertyInfo prop, MsgPackOptions settings)
    {
      int[] path = reference.Path;
      bool found = false;
      object target = path is null ? null : Follow(path, out found);
      if (!found)
        throw ReadDifferences.NotConverted(settings, NoValue(reference));
      int[] own = OwnPath(reference);
      if (own != null && !IsBefore(path, own))
        throw ReadDifferences.NotConverted(settings, NotEarlier(reference));

      if (target is KeyValuePair<object, object>[] map) // an object is registered by its content, the path points to its wrapper
      {
        object content = ContentOf(map);
        if (content != null && TryGetMade(content, type, out object wrapped))
          return wrapped;
      }
      if (target != null && TryGetMade(target, type, out object made))
        return made;
      if (target is null)
        return null;

      // Not made yet (a value Core converts later, a member the class does not have), or a reader without the option: read again
      if (_reading is null)
        _reading = new HashSet<object>(IdentityComparer.Instance);
      if (!_reading.Add(target))
        throw ReadDifferences.NotConverted(settings, Cycle(reference, type));
      try
      {
        return ValueConverter.ConvertDeserializeValue(target, type, settings, prop);
      }
      finally
      {
        _reading.Remove(target);
      }
    }

    /// <summary>
    /// The value at the path, through arrays and the values of map entries.
    /// </summary>
    private object Follow(int[] path, out bool found)
    {
      object value = _root;
      for (int t = 0; t < path.Length; t++)
      {
        int step = path[t];
        if (value is object[] items && step < items.Length)
          value = items[step];
        else if (value is KeyValuePair<object, object>[] entries && step < entries.Length)
          value = entries[step].Value;
        else
        {
          found = false;
          return null;
        }
      }
      found = true;
      return value;
    }

    /// <summary>
    /// Whether the value at <paramref name="path"/> comes before the reference at <paramref name="own"/> in the bytes: an earlier sibling (of it or of one containing it), or one containing it.
    /// </summary>
    internal static bool IsBefore(IList<int> path, IList<int> own)
    {
      for (int t = 0; t < path.Count; t++)
      {
        if (t == own.Count)
          return false; // inside the reference itself
        if (path[t] != own[t])
          return path[t] < own[t];
      }
      return path.Count < own.Count; // a value containing the reference (a cycle)
    }

    /// <summary>
    /// The path of a reference itself: the paths of all references are found by one walk over the plain values, at the first one resolved.
    /// </summary>
    private int[] OwnPath(MsgPackReference reference)
    {
      if (_ownPaths is null)
      {
        _ownPaths = new Dictionary<object, int[]>(IdentityComparer.Instance);
        List<int> path = new List<int>();
        Walk(_root, path, 0);
      }
      return _ownPaths.TryGetValue(reference, out int[] own) ? own : null;
    }

    private void Walk(object value, List<int> path, int depth)
    {
      if (depth > 1024) // the readers refuse deeper data (MaxDepth) before this
        return;
      if (value is MsgPackReference reference)
      {
        _ownPaths[reference] = path.ToArray();
        return;
      }
      if (value is object[] items)
      {
        for (int t = 0; t < items.Length; t++)
        {
          path.Add(t);
          Walk(items[t], path, depth + 1);
          path.RemoveAt(path.Count - 1);
        }
      }
      else if (value is KeyValuePair<object, object>[] entries)
      {
        for (int t = 0; t < entries.Length; t++)
        {
          path.Add(t);
          Walk(entries[t].Value, path, depth + 1);
          path.RemoveAt(path.Count - 1);
        }
      }
    }
  }
}
