namespace LsMsgPackDebug
{
  /// <summary>
  /// A JSON message of the handshake of a SignalR hub connection (<see cref="HubCapture"/>), before the hub messages: the client's <c>{"protocol":"messagepack","version":1}</c>,
  /// the server's <c>{}</c> (or <c>{"error":"..."}</c>), each followed by the record separator 0x1E. Not MsgPack; its <see cref="MpString.Value"/> is the JSON text.
  /// Read with <see cref="MsgPackSettings.ReadHubMessages"/>.
  /// </summary>
  public class MpHubHandshake : MpString
  {
    internal MpHubHandshake(MsgPackSettings settings, string json) : base(settings)
    {
      Value = json;
    }

    public override string ToString()
    {
      return "SignalR handshake: " + Value;
    }
  }
}
