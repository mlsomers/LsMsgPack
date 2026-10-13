using System.ComponentModel;

namespace LsMsgPack.Generated
{
  /// <summary>
  /// The versions of the contract between the code of the source generator (compiled into the user's assembly) and the runtime: generated code says which version it was generated for,
  /// the runtime uses it when the version is between <see cref="MinSupported"/> and <see cref="Current"/>, otherwise it ignores it (the runtime plans are used: slower, never broken).
  /// <para>An additive change (a new feature of the generated code) raises <see cref="Current"/> only: new capabilities are optional virtual members whose default means "not provided",
  /// and the members older generated code calls stay. A breaking change raises <see cref="MinSupported"/> too. Never add abstract members to the types generated code derives from:
  /// generated code of an older version would not load.</para>
  /// </summary>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public static class GeneratedContract
  {
    /// <summary>
    /// The version the generator of this release writes.
    /// </summary>
    public const int Current = 1;

    /// <summary>
    /// The oldest version of generated code this runtime still uses.
    /// </summary>
    public const int MinSupported = 1;

    public static bool IsSupported(int version)
    {
      return version >= MinSupported && version <= Current;
    }
  }
}
