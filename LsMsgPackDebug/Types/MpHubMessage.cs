#if KEEPTRACK
using System.ComponentModel;
using System.Xml.Serialization;
#endif

namespace LsMsgPackDebug
{
  /// <summary>
  /// A message of a SignalR hub connection (<see cref="HubCapture"/>, read with <see cref="MsgPackSettings.ReadHubMessages"/>): the array after its length prefix (<see cref="MpHubPrefix"/>),
  /// <c>[type, headers, invocationId, target, arguments, streamIds]</c> for an invocation, see <see cref="HubCapture.FieldName"/> for the others.
  /// </summary>
  public class MpHubMessage : MpArray
  {
    internal MpHubMessage(MpArray array, HubCapture.Frame frame) : base(array.Settings)
    {
      CopyFrom(array);
      MessageType = frame.Type;
      InvocationId = frame.InvocationId;
      Target = frame.Target;
      ResultKind = frame.ResultKind;
    }

    /// <summary>
    /// A Completion's result kind (1 error, 2 void, 3 a result), 0 for the other message types: it names the fifth field (<see cref="HubCapture.FieldName"/>).
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Category("SignalR")]
    [DisplayName("Result kind")]
    [Description("A Completion's result kind: 1 an error, 2 void, 3 a result.")]
#endif
    public int ResultKind { get; private set; }

    /// <summary>
    /// The name of the field at the index of this message's array (<see cref="HubCapture.FieldName"/>).
    /// </summary>
    public string FieldName(int index)
    {
      return HubCapture.FieldName(MessageType, index, ResultKind);
    }

    /// <summary>
    /// The message type (1 Invocation ... 9 Sequence).
    /// </summary>
#if KEEPTRACK
    [XmlIgnore]
    [Category("SignalR")]
    [DisplayName("Message type")]
    [Description("The type of the hub message (1 Invocation, 2 StreamItem, 3 Completion, 4 StreamInvocation, 5 CancelInvocation, 6 Ping, 7 Close, 8 Ack, 9 Sequence).")]
#endif
    public int MessageType { get; private set; }

#if KEEPTRACK
    [XmlIgnore]
    [Category("SignalR")]
    [DisplayName("Message")]
    [Description("The name of the message type.")]
#endif
    public string MessageTypeName
    {
      get { return HubCapture.MessageTypeName(MessageType); }
    }

#if KEEPTRACK
    [XmlIgnore]
    [Category("SignalR")]
    [DisplayName("Invocation id")]
    [Description("The id of the invocation (null for an invocation that expects no answer).")]
#endif
    public string InvocationId { get; private set; }

#if KEEPTRACK
    [XmlIgnore]
    [Category("SignalR")]
    [DisplayName("Target")]
    [Description("The hub method or client method an invocation calls.")]
#endif
    public string Target { get; private set; }

    /// <summary>
    /// <c>Invocation "Send" (id 7)</c>.
    /// </summary>
    public string Summary
    {
      get
      {
        string text = MessageTypeName ?? ("message type " + MessageType);
        if (Target != null)
          text += " \"" + Target + "\"";
        if (InvocationId != null)
          text += " (id " + InvocationId + ")";
        return text;
      }
    }

    public override string ToString()
    {
      return "SignalR " + Summary + ".";
    }
  }
}
