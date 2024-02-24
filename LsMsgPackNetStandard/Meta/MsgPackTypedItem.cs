using System;

namespace LsMsgPackNetStandard.Meta
{
  internal struct MsgPackTypedItem
  {
    internal MsgPackTypedItem(object item, Type type)
    {
      Type = type;
      Instance = item;
    }
    public readonly Type Type;
    public readonly object Instance;
  }
}
