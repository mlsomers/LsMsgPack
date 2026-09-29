namespace LsMsgPack
{
  /// <summary>
  /// An extension value without a registered custom extension, as it is unpacked (LsMsgPack: MpExt).
  /// <para>It can only be assigned as itself (e.g. to object) or as its bytes to a byte[] (see ValueConverter).</para>
  /// </summary>
  internal interface IMsgPackExtension
  {
    sbyte TypeSpecifier { get; }

    /// <summary>
    /// The number of bytes of <see cref="Data"/>.
    /// </summary>
    int Count { get; }

    MsgPackTypeId TypeId { get; }

    byte[] Data { get; }
  }
}
