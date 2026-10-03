namespace LsMsgPack
{
  public enum MsgPackTypeId : byte
  {
    /// <summary>
    /// NULL
    /// </summary>
    MpNull = 0xc0,
    /// <summary>
    /// True
    /// </summary>
    MpBoolTrue = 0xc3,
    /// <summary>
    /// False
    /// </summary>
    MpBoolFalse = 0xc2,
    /// <summary>
    /// 5-bit negative (signed) number (-32 to -1)
    /// </summary>
    MpSBytePart = 0xE0,
    /// <summary>
    /// Unsigned up to 127
    /// </summary>
    MpBytePart = 0x00,
    /// <summary>
    /// Normal unsigned Byte
    /// </summary>
    MpUByte = 0xcc,
    /// <summary>
    /// Unsigned Short (UInt16)
    /// </summary>
    MpUShort = 0xcd,
    /// <summary>
    /// Unsigned UInt32
    /// </summary>
    MpUInt = 0xce,
    /// <summary>
    /// Unsigned UInt64
    /// </summary>
    MpULong = 0xcf,
    /// <summary>
    /// Signed Byte
    /// </summary>
    MpSByte = 0xd0,
    /// <summary>
    /// Signed Short (Int16)
    /// </summary>
    MpShort = 0xd1,
    /// <summary>
    /// Signd Int (int32)
    /// </summary>
    MpInt = 0xd2,
    /// <summary>
    /// Signed Long (Int64)
    /// </summary>
    MpLong = 0xd3,
    /// <summary>
    /// 32bit Float
    /// </summary>
    MpFloat = 0xca,
    /// <summary>
    /// 64bit Float
    /// </summary>
    MpDouble = 0xcb,
    /// <summary>
    /// String up to 31 bytes
    /// </summary>
    MpStr5 = 0xa0,
    /// <summary>
    /// String up to 255 bytes
    /// </summary>
    MpStr8 = 0xd9,
    /// <summary>
    /// String with a length (in bytes) that fits in 16 bits
    /// </summary>
    MpStr16 = 0xda,
    /// <summary>
    /// String with a length (in bytes) that fits in 32 bits
    /// </summary>
    MpStr32 = 0xdb,
    /// <summary>
    /// Byte array with less than 256 bytes
    /// </summary>
    MpBin8 = 0xc4,
    /// <summary>
    /// Byte array where the length fits in 16 bits
    /// </summary>
    MpBin16 = 0xc5,
    /// <summary>
    /// Byte array where the length fits in 32 bits
    /// </summary>
    MpBin32 = 0xc6,
    /// <summary>
    /// Array with less than 16 items
    /// </summary>
    MpArray4 = 0x90,
    /// <summary>
    /// Array where the number of items fits in 16 bits
    /// </summary>
    MpArray16 = 0xdc,
    /// <summary>
    /// Array where the number of items fits in 32 bits
    /// </summary>
    MpArray32 = 0xdd,
    /// <summary>
    /// Array of key-value pairs with less than 16 items
    /// </summary>
    MpMap4 = 0x80,
    /// <summary>
    /// Array of key-value pairs where the number of items fits in 16 bits
    /// </summary>
    MpMap16 = 0xde,
    /// <summary>
    /// Array of key-value pairs where the number of items fits in 32 bits
    /// </summary>
    MpMap32 = 0xdf,

    /// <summary>
    /// fixext 1 stores an Integer and a byte array whose length is 1 byte
    /// </summary>
    MpFExt1 = 0xd4,
    /// <summary>
    /// fixext 2 stores an integer and a byte array whose length is 2 bytes
    /// </summary>
    MpFExt2 = 0xd5,
    /// <summary>
    /// fixext 4 stores an integer and a byte array whose length is 4 bytes
    /// </summary>
    MpFExt4 = 0xd6,
    /// <summary>
    /// fixext 8 stores an integer and a byte array whose length is 8 bytes
    /// </summary>
    MpFExt8 = 0xd7,
    /// <summary>
    /// fixext 16 stores an integer and a byte array whose length is 16 bytes
    /// </summary>
    MpFExt16 = 0xd8,

    /// <summary>
    /// ext 8 stores an integer and a byte array whose length is upto (2^8)-1 bytes
    /// </summary>
    MpExt8 = 0xc7,
    /// <summary>
    /// ext 16 stores an integer and a byte array whose length is upto (2^16)-1 bytes
    /// </summary>
    MpExt16 = 0xc8,
    /// <summary>
    /// ext 32 stores an integer and a byte array whose length is upto (2^32)-1 bytes
    /// </summary>
    MpExt32 = 0xc9,

    /// <summary>
    /// An uninitialised ext might have this value, but it should actually never be used
    /// </summary>
    NeverUsed = 0xc1
  }
}
