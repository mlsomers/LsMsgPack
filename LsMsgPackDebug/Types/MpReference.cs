using LsMsgPack;
using LsMsgPack.References;
using System.Collections.Generic;

namespace LsMsgPackDebug
{
  /// <summary>
  /// A reference to an earlier value of the payload (<see cref="ReferenceHandling.Preserve"/>, extension type <see cref="MsgPackOptions.ReferenceExtensionType"/>): the path from the root value to it,
  /// a step per level (an index in an array, or the number of an entry in a map, standing for its value). The explorers show it as a link to that value.
  /// </summary>
  public class MpReference : MpExt
  {
    private int[] _path;
    private MsgPackReference _plain;

    internal MpReference(MpExt ext) : base(ext.Settings)
    {
      CopyBaseDataFrom(ext);
      _path = ReferencePath.Decode((byte[])ext.Value);
    }

    /// <summary>
    /// Writing: a reference to the value written for <paramref name="target"/>, whose path is set when the whole value is written (<see cref="MsgPackSerializer"/>).
    /// </summary>
    internal MpReference(MsgPackSettings settings, ReferenceTarget target) : base(settings)
    {
      TypeSpecifier = MsgPackOptions.ReferenceExtensionType;
      Target = target; // no value until SetPath: MpExt picks its header at the first one
    }

    /// <summary>
    /// Writing: the value referred to (its item is known when the value is written).
    /// </summary>
    internal readonly ReferenceTarget Target;

    /// <summary>
    /// The steps from the root value (after the schema), null when the content is not a path (then it is invalid).
    /// </summary>
    public IReadOnlyList<int> Path { get { return _path; } }

    /// <summary>
    /// The path as text (e.g. [0, 1]), null when the content is not a path.
    /// </summary>
    public string PathText { get { return _path is null ? null : ReferencePath.Format(_path); } }

    internal void SetPath(int[] path)
    {
      _path = path;
      Value = ReferencePath.Encode(path);
    }

    /// <summary>
    /// The value the serializer converts (<see cref="MsgPackItem.UnpackedValue"/>): the same instance each time (references are found by identity).
    /// </summary>
    internal object Plain
    {
      get { return _plain ?? (_plain = new MsgPackReference((byte[])Value, null)); }
    }

#if KEEPTRACK
    /// <summary>
    /// The item of the value this reference points to (for the explorers: a link to it), followed from <paramref name="body"/>, the root value of the payload (after the schema:
    /// the last item of an <see cref="MpRoot"/>; KEEPTRACK, which keeps the items of arrays and maps). Null when the path leads to no value of the read items, or to one that does not come before the reference (not valid).
    /// </summary>
    public MsgPackItem FindTarget(MsgPackItem body)
    {
      if (_path is null || body is null)
        return null;
      MsgPackItem at = body;
      for (int t = 0; t < _path.Length; t++)
      {
        int step = _path[t];
        if (at is MpArray array && array.PackedValues is MsgPackItem[] items)
          at = step < items.Length ? items[step] : null;
        else if (at is MpMap map && map.PackedValues is KeyValuePair<MsgPackItem, MsgPackItem>[] entries)
          at = step < entries.Length ? entries[step].Value : null;
        else
          return null;
        if (at is null || ReferenceEquals(at, this))
          return null;
      }
      return ReferenceEquals(First(body, at, this), at) ? at : null; // an ancestor (a cycle) comes first too
    }

    /// <returns>Which of the two items comes first in the order they are written (depth first, a container before its values), null when neither is found</returns>
    private static MsgPackItem First(MsgPackItem at, MsgPackItem a, MsgPackItem b)
    {
      if (ReferenceEquals(at, a) || ReferenceEquals(at, b))
        return at;
      if (at is MpArray array && array.PackedValues is MsgPackItem[] items)
      {
        for (int t = 0; t < items.Length; t++)
        {
          MsgPackItem found = First(items[t], a, b);
          if (found != null)
            return found;
        }
      }
      else if (at is MpMap map && map.PackedValues is KeyValuePair<MsgPackItem, MsgPackItem>[] entries)
      {
        for (int t = 0; t < entries.Length; t++)
        {
          MsgPackItem found = First(entries[t].Value, a, b);
          if (found != null)
            return found;
        }
      }
      return null;
    }
#endif

    public override string ToString()
    {
      return $"Reference (extension type {TypeSpecifier}) to the value at {ReferencePath.Format(_path)}.";
    }
  }

  /// <summary>
  /// Writing with <see cref="ReferenceHandling.Preserve"/>: the first occurrence of an object or collection, whose item is set once it is made (a reference to it may be made before: a cycle).
  /// </summary>
  internal sealed class ReferenceTarget
  {
    internal MsgPackItem Item;
    internal int[] Path;
  }
}
