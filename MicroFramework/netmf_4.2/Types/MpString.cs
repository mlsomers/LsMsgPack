using System;
using System.Collections;
using System.IO;
using System.Text;

namespace LsMsgPackMicro {
  [Serializable]
  public class MpString: MsgPackVarLen {

    private string value = string.Empty;

    public override MsgPackTypeId TypeId {
      get {
        return GetTypeId(StrAsBytes.Length);
      }
    }

    public override int Count {
      get { return value.Length; }
    }

    protected override MsgPackTypeId GetTypeId(long len) {
      if(len < 32) return MsgPackTypeId.MpStr5;
      if(len < 256) return MsgPackTypeId.MpStr8;
      if(len <= ushort.MaxValue) return MsgPackTypeId.MpStr16;
      return MsgPackTypeId.MpStr32;
    }

    public override object Value {
      get { return value; }
      set { this.value = ReferenceEquals(value, null) ? string.Empty : value.ToString(); }
    }

    private byte[] StrAsBytes {
      get{
        return Encoding.UTF8.GetBytes(value);
      }
      set {
        this.Value = new string(Encoding.UTF8.GetChars(value));
      }
    }

    public override byte[] ToBytes() {
      byte[] strBytes = StrAsBytes;
      ArrayList bytes = new ArrayList(); // current max length limit is 4 bytes + string identifier
      MsgPackTypeId typeId = GetTypeId(strBytes.Length);
      if(typeId == MsgPackTypeId.MpStr5) bytes.Add(GetLengthBytes(typeId, strBytes.Length));
      else {
        bytes.Add((byte)typeId);
        bytes.AddRange(GetLengthBytes(strBytes.Length, SupportedLengths.All));
      }
      bytes.AddRange(strBytes);
      return (byte[])bytes.ToArray(typeof(byte));
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data) {
      long len;
      if(!IsMasked(MsgPackTypeId.MpStr5, typeId, 0x1F, out len)) {
        switch(typeId) {
          case MsgPackTypeId.MpStr8: len = ReadLen(data, 1); break;
          case MsgPackTypeId.MpStr16: len = ReadLen(data, 2); break;
          case MsgPackTypeId.MpStr32: len = ReadLen(data, 4); break;
          default: throw new MsgPackException(string.Concat("MpString does not support a type ID of ", GetOfficialTypeName(typeId), "."), data.Position - 1, typeId);
        }
      }
      StrAsBytes = ReadBytes(data, len); 
      return this;
    }

    public override string ToString() {
      return string.Concat("String (", GetOfficialTypeName(TypeId), ") with the value \"", value, "\"");
    }
  }
}
