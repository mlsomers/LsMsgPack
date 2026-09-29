using LsMsgPack;
using System;

namespace LtMsgPack.Extensions
{
  /// <summary>
  /// An extension that was read without a custom extension for its type code (see <see cref="LtMsgPackOptions.Extensions"/>).
  /// <para>It is only assigned as itself (e.g. to a property of type object) or as its bytes to a byte[], like LsMsgPack does with its MpExt.</para>
  /// </summary>
  public sealed class MsgPackExtension : IMsgPackExtension
  {
    private readonly MsgPackTypeId _typeId;

    public MsgPackExtension(sbyte typeCode, byte[] data)
      : this(typeCode, data, FormatOf(data?.Length ?? 0)) { }

    internal MsgPackExtension(sbyte typeCode, byte[] data, MsgPackTypeId typeId)
    {
      TypeCode = typeCode;
      Data = data ?? throw new ArgumentNullException(nameof(data));
      _typeId = typeId;
    }

    public sbyte TypeCode { get; }

    public byte[] Data { get; }

    sbyte IMsgPackExtension.TypeSpecifier { get { return TypeCode; } }

    int IMsgPackExtension.Count { get { return Data.Length; } }

    MsgPackTypeId IMsgPackExtension.TypeId { get { return _typeId; } }

    /// <summary>
    /// The smallest extension format for data of this length (as LsMsgPack's MpExt).
    /// </summary>
    internal static MsgPackTypeId FormatOf(long length)
    {
      if (length <= 0) return MsgPackTypeId.MpExt8;
      if (length == 1) return MsgPackTypeId.MpFExt1;
      if (length == 2) return MsgPackTypeId.MpFExt2;
      if (length == 3) return MsgPackTypeId.MpExt8;
      if (length == 4) return MsgPackTypeId.MpFExt4;
      if (length <= 7) return MsgPackTypeId.MpExt8;
      if (length == 8) return MsgPackTypeId.MpFExt8;
      if (length <= 15) return MsgPackTypeId.MpExt8;
      if (length == 16) return MsgPackTypeId.MpFExt16;
      if (length <= 255) return MsgPackTypeId.MpExt8;
      if (length <= ushort.MaxValue) return MsgPackTypeId.MpExt16;
      return MsgPackTypeId.MpExt32;
    }

    public override string ToString()
    {
      return $"Extension type {TypeCode} with {Data.Length} bytes";
    }
  }
}
