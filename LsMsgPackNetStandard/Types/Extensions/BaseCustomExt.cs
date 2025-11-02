using System.IO;

namespace LsMsgPack.Types.Extensions
{
  public abstract class BaseCustomExt<TSelf,Ttype> : BaseCustomExtNonCached<TSelf,Ttype> where TSelf : BaseCustomExt<TSelf, Ttype>, new()
  {
    private Ttype value;

    public BaseCustomExt() : base(){ }

    public BaseCustomExt(MsgPackSettings settings) : base(settings) { }

    public BaseCustomExt(MpExt ext) : base()
    {
      CopyBaseDataFrom(ext);
      value = ext.BaseValue  is null ? (ext.Value is Ttype ? (Ttype)ext.Value : default) : FromBytes(ext.BaseValue);
    }

    public BaseCustomExt(Ttype value) : base()
    {
      this.value = value;
      BaseValue = GetBytes(value);
    }

    public override object Value
    {
      get
      {
        return value; // cache advantage, this may be called multiple times during deserialization
      }
      set
      {
        this.value = (Ttype)value;
        BaseValue = GetBytes(this.value);
      }
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data)
    {
      base.Read(typeId, data);
      value = FromBytes(BaseValue);
      return this;
    }

    public override byte[] ToBytes()
    {
      if (BaseValue is null)
        BaseValue = GetBytes(this.value);

      return base.ToBytes();
    }

    protected override void CopyBaseDataFrom(MpExt generic)
    {
      base.CopyBaseDataFrom(generic);
      this.value = FromBytes(generic.BaseValue);
    }
  }
}
