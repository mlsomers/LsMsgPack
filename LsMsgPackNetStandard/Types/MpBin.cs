using System;
using System.Collections.Generic;
using System.IO;
using LsMsgPack.Meta;

namespace LsMsgPack {
  [Serializable]
  public class MpBin: MsgPackVarLen {

    public MpBin() : base() { }
    public MpBin(MsgPackSettings settings) : base(settings) { }

    private byte[] value = new byte[0];

    public override MsgPackTypeId TypeId {
      get {
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
        return GetTypeId(value.LongLength);
#else
        return GetTypeId(value.Length);
#endif
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
      ByteWriter bytes = new ByteWriter(value.Length + 5); // current max length limit is 4 bytes + identifier
      WriteValue(bytes);
      return bytes.ToArray();
    }

    internal override void WriteTo(ByteWriter target) {
      if (GetType() != typeof(MpBin)) { // a derived type may override ToBytes
        base.WriteTo(target);
        return;
      }
      WriteValue(target);
    }

    private void WriteValue(ByteWriter bytes) {
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
      MsgPackTypeId typeId = GetTypeId(value.LongLength);
#else
      MsgPackTypeId typeId = GetTypeId(value.Length);
#endif
      bytes.Write((byte)typeId);
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
      WriteLength(bytes, value.LongLength, SupportedLengths.All);
#else
      WriteLength(bytes, value.Length, SupportedLengths.All);
#endif
      bytes.Write(value);
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data) {
      long len;

      switch(typeId) {
        case MsgPackTypeId.MpBin8: len = ReadLen(data, 1); break;
        case MsgPackTypeId.MpBin16: len = ReadLen(data, 2); break;
        case MsgPackTypeId.MpBin32: len = ReadLen(data, 4); break;
        default: throw new MsgPackException($"MpBin does not support a type ID of {GetOfficialTypeName(typeId)}.", data.Position - 1, typeId);
      }
      value = ReadBytes(data, len);
      return this;
    }

    public override string ToString() {
      return $"Blob ({GetOfficialTypeName(TypeId)}) of {value.Length} bytes.";
    }
  }
}
