using LsMsgPack;
using System;

namespace LsMsgPackDebug.Types.Extensions
{
  public interface ICustomExt
  {
    bool SupportsType(Type type);
    MsgPackItem Create(MsgPackSettings settings, MpExt value, object val);

    sbyte TypeSpecifier { get;}
  }
}