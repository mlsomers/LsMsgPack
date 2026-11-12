namespace LsMsgPack.Streaming
{
  /// <summary>
  /// The escape sequences of arrays, maps, str and bin of unknown length: the byte 0xC1 followed by one code byte, as proposed in msgpack/msgpack#347 (not in the specification yet, so the codes may still change: the only .NET place with them, shared by LsMsgPack's stream reader and writer and LsMsgPack's item reader with MsgPackSettings.ReadStreams).
  /// <para>Each start code is the first byte of an empty value of the same family, the end code is nil. Every other code is reserved and an error, never "more data needed".</para>
  /// </summary>
  internal static class Escape
  {
    internal const byte Byte = 0xC1;
    internal const byte ArrayStart = 0x90;
    internal const byte MapStart = 0x80;
    internal const byte StringStart = 0xA0;
    internal const byte BinaryStart = 0xC4;
    internal const byte End = 0xC0;

    internal const string Proposal = "msgpack/msgpack#347";

    /// <summary>
    /// The serializers' readers do not read escape sequences, the stream reader does (and LsMsgPack's item reader with MsgPackSettings.ReadStreams).
    /// </summary>
    internal static MsgPackException NotSupported(long offset)
    {
      return new MsgPackException($"0xC1 is not a MsgPack type. It starts an escape sequence (an array, map, str or bin of unknown length, as proposed in {Proposal}), which only MsgPackStreamReader reads (and LsMsgPack with MsgPackSettings.ReadStreams).", offset, MsgPackTypeId.NeverUsed);
    }

    internal static MsgPackException Reserved(byte code, long offset)
    {
      return new MsgPackException($"The escape sequence 0xC1 0x{code:X2} is reserved ({Proposal}), the data is not valid.", offset, MsgPackTypeId.NeverUsed);
    }

    internal static MsgPackException AtEndOfData(long offset)
    {
      return new MsgPackException("The data ends with the escape byte 0xC1, an escape sequence has two bytes.", offset, MsgPackTypeId.NeverUsed);
    }

    internal static MsgPackException MisplacedEnd(string where, long offset)
    {
      return new MsgPackException($"The end of a value of unknown length (0xC1 0xC0) {where}.", offset, MsgPackTypeId.NeverUsed);
    }
  }
}
