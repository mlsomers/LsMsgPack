using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.References;
using LsMsgPack.TypeResolving.Attributes;
using System;

namespace LsMsgPackDebug
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
    /// Converts an unpacked root value (<see cref="MsgPackItem.UnpackedValue"/>) into the type it will be assigned to (see <see cref="ValueConverter"/>, shared with LsMsgPack).
    /// <para>Its references (<see cref="ReferenceHandling.Preserve"/>) are resolved by their paths from this value (<see cref="PlainReferences"/>, also without the option: read again, as copies).
    /// A call of its own: a nested one (e.g. a converter that reads) has its own.</para>
    /// </summary>
    private static object ConvertDeserializeValue(object val, Type assignType, MsgPackSettings settings, FullPropertyInfo prop)
    {
      ReferenceResolver outer = ReferenceResolver.Current;
      ReferenceResolver.Current = new PlainReferences(val);
      try
      {
        return ValueConverter.ConvertDeserializeValue(val, assignType, settings, prop);
      }
      finally
      {
        ReferenceResolver.Current = outer;
      }
    }
  }
}
