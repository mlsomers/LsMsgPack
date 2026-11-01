namespace LtMsgPack.Streaming
{
  /// <summary>
  /// What <see cref="MsgPackStreamReader.Read"/> found.
  /// </summary>
  public enum MsgPackTokenType
  {
    /// <summary>
    /// Nothing read yet, the end of the stream, or after <see cref="MsgPackStreamReader.ReadObject{T}"/>.
    /// </summary>
    None,
    Nil,
    Boolean,
    Integer,

    /// <summary>
    /// float 32 or float 64.
    /// </summary>
    Float,

    /// <summary>
    /// A str, also a chunk of a str of unknown length.
    /// </summary>
    String,

    /// <summary>
    /// A bin, also a chunk of a bin of unknown length.
    /// </summary>
    Binary,

    /// <summary>
    /// An extension value (also a timestamp, see <see cref="MsgPackStreamReader.GetDateTime"/>).
    /// </summary>
    Extension,

    /// <summary>
    /// An array of known (<see cref="MsgPackStreamReader.Length"/>) or unknown length: the next tokens are its elements, until <see cref="ArrayEnd"/>.
    /// </summary>
    ArrayStart,

    /// <summary>
    /// A map of known (<see cref="MsgPackStreamReader.Length"/> entries) or unknown length: the next tokens are its keys and values, until <see cref="MapEnd"/>.
    /// </summary>
    MapStart,

    /// <summary>
    /// The end of an array: the end sequence of an array of unknown length, or after the last element of an array of known length (no bytes).
    /// </summary>
    ArrayEnd,

    /// <summary>
    /// The end of a map: the end sequence of a map of unknown length, or after the last value of a map of known length (no bytes).
    /// </summary>
    MapEnd,

    /// <summary>
    /// A str of unknown length: the next tokens are its chunks (<see cref="String"/>), until <see cref="StringEnd"/>. <see cref="MsgPackStreamReader.GetString"/> reads them all.
    /// </summary>
    StringStart,

    /// <summary>
    /// A bin of unknown length: the next tokens are its chunks (<see cref="Binary"/>), until <see cref="BinaryEnd"/>. <see cref="MsgPackStreamReader.GetBytes"/> reads them all.
    /// </summary>
    BinaryStart,

    StringEnd,
    BinaryEnd
  }
}
