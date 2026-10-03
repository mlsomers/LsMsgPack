using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using System;

namespace LsMsgPack
{
  public static partial class MsgPackSerializer
  {
    /// <summary>
    /// Map key holding the type identifier (see <see cref="GetTypeIdentifier"/>).
    /// </summary>
    internal const string TypeIdKey = MsgPackOptions.TypeIdKey;

    /// <summary>
    /// Map key holding the packed value of a wrapped item (collections, dictionaries or values that needed a type identifier).
    /// </summary>
    internal const string ContentKey = MsgPackOptions.ContentKey;

    /// <summary>
    /// Converts an unpacked value (<see cref="MsgPackItem.UnpackedValue"/>) into the type it will be assigned to (see <see cref="ValueConverter"/>, shared with LtMsgPack).
    /// </summary>
    private static object ConvertDeserializeValue(object val, Type assignType, MsgPackSettings settings, FullPropertyInfo prop)
    {
      return ValueConverter.ConvertDeserializeValue(val, assignType, settings, prop);
    }
  }
}
