using LsMsgPack;
using System;

namespace LtMsgPack.Extensions
{
  /// <summary>
  /// The Guids of Nerdbank.MessagePack: extension type 2 with the 16 bytes in big-endian (RFC 4122) order.
  /// <para>Guids are written as bin 16 whatever the extensions (as in LsMsgPack), which Nerdbank.MessagePack reads as well: this extension reads its Guids.</para>
  /// </summary>
  public sealed class NerdbankGuidExtension : LtExtension<Guid>
  {
    public override sbyte TypeCode { get { return 2; } }

    public override int GetMaxLength(Guid value)
    {
      return 16;
    }

    public override int Write(Guid value, Span<byte> destination)
    {
      byte[] bytes = value.ToByteArray();
      SwapToBigEndian(bytes);
      bytes.CopyTo(destination);
      return 16;
    }

    public override Guid Read(ReadOnlySpan<byte> data)
    {
      if (data.Length != 16)
        throw new MsgPackException($"A Guid (extension type {TypeCode}) has 16 bytes, not {data.Length}.");
      byte[] bytes = data.ToArray();
      SwapToBigEndian(bytes);
      return new Guid(bytes);
    }

    /// <summary>
    /// Guid.ToByteArray() writes the first three groups little-endian, RFC 4122 big-endian (the same swap both ways).
    /// </summary>
    private static void SwapToBigEndian(byte[] b)
    {
      Swap(b, 0, 3);
      Swap(b, 1, 2);
      Swap(b, 4, 5);
      Swap(b, 6, 7);
    }

    private static void Swap(byte[] b, int x, int y)
    {
      byte keep = b[x];
      b[x] = b[y];
      b[y] = keep;
    }
  }
}
