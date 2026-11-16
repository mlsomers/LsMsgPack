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
