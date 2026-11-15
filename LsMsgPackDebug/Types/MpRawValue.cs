using LsMsgPack;
using LsMsgPack.Meta;
using System;
using System.IO;

namespace LsMsgPackDebug
{
  /// <summary>
  /// One MsgPack value written as its bytes: a value kept as extension data (see Core's ExtensionDataMember), already in the byte order of the settings. Only written, never read.
  /// </summary>
  internal sealed class MpRawValue : MsgPackItem
  {
    private readonly byte[] _bytes;

    internal MpRawValue(byte[] bytes, MsgPackSettings settings) : base(settings)
    {
      _bytes = bytes;
    }

    public override MsgPackTypeId TypeId
    {
      get { return (MsgPackTypeId)_bytes[0]; }
    }

    public override object Value
    {
      get { return _bytes; }
      set { throw new NotSupportedException("A kept value is written as it is."); }
    }

    public override byte[] ToBytes()
    {
      return (byte[])_bytes.Clone();
    }

    internal override void WriteTo(ByteWriter target)
    {
      target.Write(_bytes);
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data)
    {
      throw new NotSupportedException("A kept value is only written.");
    }

    public override string ToString()
    {
      return $"[Extension data] ({_bytes.Length} bytes)";
    }
  }
}
