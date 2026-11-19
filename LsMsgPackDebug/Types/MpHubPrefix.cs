namespace LsMsgPackDebug
{
  /// <summary>
  /// The length prefix of a SignalR hub message (<see cref="HubCapture"/>): a VarInt (7 bits per byte, the lowest first), not MsgPack; its <see cref="MpInt.Value"/> is the length of the message after it.
  /// Read with <see cref="MsgPackSettings.ReadHubMessages"/>.
  /// </summary>
  public class MpHubPrefix : MpInt
  {
    internal MpHubPrefix(MsgPackSettings settings, int length) : base(settings)
    {
      Value = length;
    }

    /// <summary>
    /// The number of bytes of the message after the prefix.
    /// </summary>
    public int MessageLength
    {
      get { return System.Convert.ToInt32(Value); }
    }

    public override string ToString()
    {
      return $"SignalR length prefix: a message of {MessageLength} bytes follows.";
    }
  }
}
