using System;

namespace LsMsgPack.Meta
{
  internal class MsgPackTypedItem
  {
    internal MsgPackTypedItem(object item, Type type)
    {
      Type = type;
      Instance = item;
    }
    public readonly Type Type;
    public object Instance;
  }
}
