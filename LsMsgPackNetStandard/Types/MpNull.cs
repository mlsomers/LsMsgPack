using System;
using LsMsgPack.Meta;

namespace LsMsgPack {
  [Serializable]
  public class MpNull:MsgPackItem {
    public MpNull() : base() { }
    public MpNull(MsgPackSettings settings) : base(settings) { }

    public override MsgPackTypeId TypeId {
      get { return MsgPackTypeId.MpNull; }
    }

    public override byte[] ToBytes() {
      return new byte[1] { (byte)TypeId };
    }

    internal override void WriteTo(ByteWriter target) {
      if (GetType() != typeof(MpNull)) { // a derived type may override ToBytes
        base.WriteTo(target);
        return;
      }
      target.Write((byte)MsgPackTypeId.MpNull);
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, System.IO.Stream data) {
      return this;
    }

    public override object Value {
      get { return null; }
      set { if(!ReferenceEquals(value, null)) throw new MsgPackException("Cannot assign anything to Null!"); }
    }

    public virtual int Write(System.IO.Stream data) {
      byte[] item = ToBytes();
      data.Write(item, 0, item.Length);
      return item.Length;
    }

    public override string ToString() {
      return $"[NULL] ({GetOfficialTypeName(TypeId)})";
    }
  }
}
