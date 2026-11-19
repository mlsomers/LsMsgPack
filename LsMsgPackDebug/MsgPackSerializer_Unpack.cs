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
    /// <para>Its references (<see cref="ReferenceHandling.Preserve"/>) are resolved by their paths from this value (<see cref="PlainReferences"/>). Settings without the option convert
    /// the value again with it at the first reference (<see cref="ReadAgainPreserving"/>), so each value a reference points to is made once and shared, as LsMsgPack reads it.
    /// A call of its own: a nested one (e.g. a converter that reads) has its own.</para>
    /// </summary>
    /// <param name="payloadLength">The bytes of the value (from its start to the end of the data, as LsMsgPack counts it for the copies of kept values), -1 when not known</param>
    private static object ConvertDeserializeValue(object val, Type assignType, MsgPackOptions settings, FullPropertyInfo prop, long payloadLength)
    {
      ReferenceResolver outer = ReferenceResolver.Current;
      ReferenceResolver.Current = new PlainReferences(val, payloadLength);
      try
      {
        return ValueConverter.ConvertDeserializeValue(val, assignType, settings, prop);
      }
      catch (ReadAgainPreserving) when (!settings.PreservesReferences)
      {
        settings._differences?.Reset(); // the objects of the first attempt are dropped (they had their OnDeserializing callbacks, as a read repeated after a schema grew)
        return ConvertDeserializeValue(val, assignType, settings.Preserving(), prop, payloadLength);
      }
      finally
      {
        ReferenceResolver.Current = outer;
      }
    }
  }
}
