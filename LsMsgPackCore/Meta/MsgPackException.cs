using System;

namespace LsMsgPack {

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

#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
#if NET8_0_OR_GREATER
    [Obsolete("Formatter-based serialization is obsolete (as the constructor of Exception it calls).", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
#endif
    protected MsgPackException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
#endif
  }
}
