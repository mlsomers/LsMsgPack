namespace LtMsgPack
{
  /// <summary>
  /// How <see cref="System.Guid"/> values are written (<see cref="LtMsgPackOptions.GuidFormat"/>).
  /// </summary>
  public enum GuidFormat
  {
    /// <summary>
    /// bin 16 in the byte order of Guid.ToByteArray(), as LsMsgPack writes it (the default).
    /// </summary>
    Binary,

    /// <summary>
    /// A string of 36 characters ("D" format), as MessagePack-CSharp writes it by default and most other languages expect. Reading accepts both the string and bin 16.
    /// </summary>
    String
  }

  /// <summary>
  /// How decimal values are written (<see cref="LtMsgPackOptions.DecimalFormat"/>).
  /// </summary>
  public enum DecimalFormat
  {
    /// <summary>
    /// The decimal extension of <see cref="LtMsgPackOptions.Extensions"/> (extension type 1 by default, as LsMsgPack writes it).
    /// </summary>
    Extension,

    /// <summary>
    /// A string in the invariant culture (e.g. "1234.50"), as MessagePack-CSharp writes it by default, even when a decimal extension is registered (it is still used for reading).
    /// </summary>
    String
  }

  /// <summary>
  /// How BigInteger, Int128 and UInt128 values are written (<see cref="LtMsgPackOptions.BigIntegerFormat"/>).
  /// </summary>
  public enum BigIntegerFormat
  {
    /// <summary>
    /// An integer when the value fits in 64 bits, otherwise extension type -2 with the value in big-endian two's complement, as short as possible (the proposals for MsgPack's bigint, msgpack/msgpack#206),
    /// as LsMsgPack writes it (the default).
    /// </summary>
    Extension,

    /// <summary>
    /// bin with the value in little-endian two's complement, as MessagePack-CSharp writes it (BigInteger.ToByteArray(); Int128 and UInt128 always 16 bytes). Reading accepts every format.
    /// </summary>
    Binary
  }
}
