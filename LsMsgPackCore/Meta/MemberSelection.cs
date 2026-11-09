using LsMsgPack.TypeResolving.Filters;

namespace LsMsgPack.Meta
{
  /// <summary>
  /// The members the serializers look at besides the public properties: the non-public properties and the fields that the <see cref="FilterNonSettable"/> of the static filters selects
  /// (<see cref="Default"/> without one). Decides the candidates (<see cref="FullPropertyInfo.GetMembers(System.Type, MemberSelection)"/>), so every cache of the candidates is kept per selection (<see cref="Index"/>).
  /// </summary>
  internal readonly struct MemberSelection
  {
    private const int FieldModes = (int)SerializedFields.All + 1;

    /// <summary>
    /// The number of selections, the range of <see cref="Index"/>.
    /// </summary>
    internal const int Count = ((int)NonPublicProperties.All + 1) * FieldModes;

    /// <summary>
    /// What the serializers select without a <see cref="FilterNonSettable"/>: no non-public properties, the attributed fields (the defaults of the filter).
    /// </summary>
    internal static readonly MemberSelection Default = new MemberSelection(NonPublicProperties.None, SerializedFields.Attributed);

    internal readonly NonPublicProperties NonPublic;
    internal readonly SerializedFields Fields;

    /// <param name="nonPublic">Checked by <see cref="FilterNonSettable"/></param>
    /// <param name="fields">Checked by <see cref="FilterNonSettable"/></param>
    internal MemberSelection(NonPublicProperties nonPublic, SerializedFields fields)
    {
      NonPublic = nonPublic;
      Fields = fields;
    }

    /// <summary>
    /// A number from 0 to <see cref="Count"/> - 1 per selection, for arrays of caches.
    /// </summary>
    internal int Index
    {
      get { return (int)NonPublic * FieldModes + (int)Fields; }
    }
  }
}
