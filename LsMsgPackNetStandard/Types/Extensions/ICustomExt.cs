using System;

namespace LsMsgPack.Types.Extensions
{
  public interface ICustomExt
  {
    bool SupportsType(Type type);
    MsgPackItem Create(MsgPackSettings settings, MpExt value, object val);

    sbyte TypeSpecifier { get;}
  }
}