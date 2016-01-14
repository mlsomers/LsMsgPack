using System;
using System.Collections;
using System.IO;

namespace LsMsgPackMicro {
  [Serializable]
  public class MpBin: MsgPackVarLen {

    private byte[] value = new byte[0];

    public override MsgPackTypeId TypeId {
      get {
        return GetTypeId(value.Length);
      }
    }

    public override int Count {
      get { return value.Length; }
    }

    protected override MsgPackTypeId GetTypeId(long len) {
      if(len < 256) return MsgPackTypeId.MpBin8;
      if(len <= ushort.MaxValue) return MsgPackTypeId.MpBin16;
      return MsgPackTypeId.MpBin32;
    }

    public override object Value {
      get { return value; }
      set { this.value = ReferenceEquals(value, null) ? new byte[0] : (byte[])value; }
    }

    public override byte[] ToBytes() {
      ArrayList bytes = new ArrayList();
      MsgPackTypeId typeId = GetTypeId(value.Length);
      bytes.Add((byte)typeId);
      bytes.AddRange(GetLengthBytes(value.Length, SupportedLengths.All));
      bytes.AddRange(value);
      return (byte[])bytes.ToArray(typeof(byte));
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data) {
      long len;

      switch(typeId) {
        case MsgPackTypeId.MpBin8: len = ReadLen(data, 1); break;
        case MsgPackTypeId.MpBin16: len = ReadLen(data, 2); break;
        case MsgPackTypeId.MpBin32: len = ReadLen(data, 4); break;
        default: throw new MsgPackException(string.Concat("MpBin does not support a type ID of ", GetOfficialTypeName(typeId), "."), data.Position - 1, typeId);
      }
      value = ReadBytes(data, len);
      return this;
    }

    public override string ToString() {
      return string.Concat("Blob (", GetOfficialTypeName(TypeId), ") of ", value.Length, " bytes.");
    }
  }
}
