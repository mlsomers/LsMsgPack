using System;
using System.Collections.Generic;
using System.IO;

namespace LsMsgPack {
  [Serializable]
  public class MpBin: MsgPackVarLen {

    public MpBin() : base() { }
    public MpBin(MsgPackSettings settings) : base(settings) { }

    private byte[] value = new byte[0];

    public override MsgPackTypeId TypeId {
      get {
        return GetTypeId(value.LongLength);
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
      set
      {
        if (ReferenceEquals(value, null))
          this.value = new byte[0];
        else if (value is Guid)
          this.value = ((Guid)value).ToByteArray();
        else
          this.value = (byte[])value;
      }
    }

    public override T GetTypedValue<T>()
    {
      Type targetType = typeof(T);
      if (targetType == typeof(Guid))
        return (T)(object)(new Guid(value));
      return base.GetTypedValue<T>();
    }

    public override byte[] ToBytes() {
      List<byte> bytes = new List<byte>(value.Length + 5); // current max length limit is 4 bytes + identifier
      MsgPackTypeId typeId = GetTypeId(value.LongLength);
      bytes.Add((byte)typeId);
      bytes.AddRange(GetLengthBytes(value.LongLength, SupportedLengths.All));
      bytes.AddRange(value);
      return bytes.ToArray();
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
