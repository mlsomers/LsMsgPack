using System;

namespace LsMsgPack.Types.Extensions
{

  public abstract class BaseCustomExtNonCached<TSelf, Ttype> : AbstractCustomExt<TSelf> where TSelf : BaseCustomExtNonCached<TSelf, Ttype>, new()
  {
    public BaseCustomExtNonCached() : base() { }

    public BaseCustomExtNonCached(MsgPackSettings settings) : base(settings) { }

    public BaseCustomExtNonCached(MpExt ext) : base(ext) { }

    public BaseCustomExtNonCached(Ttype value, MsgPackSettings settings) : base(settings)
    {
      BaseValue = GetBytes(value);
    }

    /// <summary>
    /// It may be desirable to override this if the type must be exact (not IsAssignableFrom())
    /// </summary>
    /// <param name="type">Type wanting to be serialized</param>
    /// <returns>True if this class supports serializing the specified type</returns>
    public override bool SupportsType(Type type)
    {
      return typeof(Ttype).IsAssignableFrom(type);
    }

    public abstract byte[] GetBytes(Ttype item);

    public abstract Ttype FromBytes(byte[] bytes);

    public override object Value
    {
      get
      {
        return FromBytes(BaseValue);
      }
      set
      {
        BaseValue = GetBytes((Ttype)value);
      }
    }

    public override string ToString()
    {
      object value = FromBytes(BaseValue);
      string valuestring = Value is null ? "null" : value.ToString();
      return $"{typeof(Ttype).Name} ({GetOfficialTypeName(TypeId)}) extension type {TypeSpecifier} with value {valuestring}";
    }
  }

}
