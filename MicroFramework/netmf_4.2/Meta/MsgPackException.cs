using System;

namespace LsMsgPackMicro {

  [Serializable]
  public class MsgPackException: Exception {
    public long Offset { get; set; }
    public MsgPackTypeId TypeId { get; set; }
    public MsgPackException() { }
    public MsgPackException(string message, long offset = 0, MsgPackTypeId typeId = MsgPackTypeId.NeverUsed) : base(message) {
      Offset = offset;
      TypeId = typeId;
    }
    public MsgPackException(string message, Exception inner, long offset = 0, MsgPackTypeId typeId = MsgPackTypeId.NeverUsed) : base(message, inner) {
      Offset = offset;
      TypeId = typeId;
    }
  }
}
