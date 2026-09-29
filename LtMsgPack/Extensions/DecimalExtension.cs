using LsMsgPack;
using System;
using System.Runtime.InteropServices;

namespace LtMsgPack.Extensions
{
  /// <summary>
  /// decimal as extension type 1 (fixext16) with the 16 bytes of the value in memory, the same bytes as LsMsgPack's MpDecimal.
  /// </summary>
  public sealed class DecimalExtension : LtExtension<decimal>
  {
    /// <summary>
    /// The type code of new instances, 1 like LsMsgPack's MpDecimal.Default_TypeSpecifier.
    /// </summary>
    public static sbyte Default_TypeCode = 1;

    private readonly sbyte _typeCode;

    public DecimalExtension() : this(Default_TypeCode) { }

    /// <param name="typeCode">Another type code (e.g. 4 for the decimals of Nerdbank.MessagePack)</param>
    public DecimalExtension(sbyte typeCode)
    {
      _typeCode = typeCode;
    }

    public override sbyte TypeCode { get { return _typeCode; } }

    public override int GetMaxLength(decimal value)
    {
      return 16;
    }

    public override int Write(decimal value, Span<byte> destination)
    {
      MemoryMarshal.Write(destination, ref value);
      return 16;
    }

    public override decimal Read(ReadOnlySpan<byte> data)
    {
      if (data.Length != 16)
        throw new MsgPackException($"A decimal (extension type {TypeCode}) has 16 bytes, not {data.Length}.");
      return MemoryMarshal.Read<decimal>(data);
    }
  }
}
