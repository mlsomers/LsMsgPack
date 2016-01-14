using System;
using System.Collections;
using System.IO;

namespace LsMsgPackMicro {
  [Serializable]
  public class MpMap: MsgPackVarLen {

    KeyValuePair[] value = new KeyValuePair[0];

    public MpMap() : base() { }

    public MpMap(KeyValuePair[] val):this() {
      value = val;
    }

    public override int Count {
      get { return value.Length; }
    }

    public override MsgPackTypeId TypeId {
      get {
        return GetTypeId(value.Length);
      }
    }

    protected override MsgPackTypeId GetTypeId(long len) {
      if(len < 16) return MsgPackTypeId.MpMap4;
      if(len <= ushort.MaxValue) return MsgPackTypeId.MpMap16;
      return MsgPackTypeId.MpMap32;
    }

    public override object Value {
      get { return value; }
      set {
        if(ReferenceEquals(value, null)) {
          value = new KeyValuePair[0];
          return;
        }
        this.value = (KeyValuePair[])value;
      }
    }

    public override byte[] ToBytes() {
      ArrayList bytes = new ArrayList();// cannot estimate this one
      MsgPackTypeId typeId = GetTypeId(value.Length);
      if(typeId == MsgPackTypeId.MpMap4) bytes.Add(GetLengthBytes(typeId, value.Length));
      else {
        bytes.Add((byte)typeId);
        bytes.AddRange(GetLengthBytes(value.Length, SupportedLengths.FromShortUpward));
      }
      for(int t = 0; t < value.Length; t++) {
        MsgPackItem key = MsgPackItem.Pack(value[t].Key);
        MsgPackItem val = MsgPackItem.Pack(value[t].Value);
        bytes.AddRange(key.ToBytes());
        bytes.AddRange(val.ToBytes());
      }
      return (byte[])bytes.ToArray(typeof(byte));
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data) {
      long len;
      if(!IsMasked(MsgPackTypeId.MpMap4, typeId, 0x0F, out len)) {
        switch(typeId) {
          case MsgPackTypeId.MpMap16: len = ReadLen(data, 2); break;
          case MsgPackTypeId.MpMap32: len = ReadLen(data, 4); break;
          default: throw new MsgPackException(string.Concat("MpMap does not support a type ID of ", GetOfficialTypeName(typeId), "."), data.Position - 1, typeId);
        }
      }

      value = new KeyValuePair[len];
      
      for(int t = 0; t < len; t++) {
        MsgPackItem key = MsgPackItem.Unpack(data);
        MsgPackItem val = MsgPackItem.Unpack(data);
        value[t] = new KeyValuePair(key.Value, val.Value);
      }

      return this;
    }

    public override string ToString() {
      return string.Concat("Map (", GetOfficialTypeName(TypeId), ") of ", value.Length.ToString(), " key-value pairs.");
    }

  }
}
