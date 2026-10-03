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
  /// How <see cref="System.DateTimeOffset"/> values are written (<see cref="LtMsgPackOptions.DateTimeOffsetFormat"/>).
  /// </summary>
  public enum DateTimeOffsetFormat
  {
    /// <summary>
    /// A timestamp of the moment, the offset is lost (as LsMsgPack writes it, the default).
    /// </summary>
    Timestamp,

    /// <summary>
    /// An array of the clock time (as a timestamp, as if it were UTC) and the offset in minutes, as MessagePack-CSharp writes it. The offset is kept.
    /// </summary>
    ClockTimeAndOffset
  }
}
