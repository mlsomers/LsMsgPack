using System;

namespace LsMsgPack.TypeResolving
{
  internal struct MsgPackTypedItem
  {
    internal MsgPackTypedItem(object item, Type type, Type assignmentType, FullPropertyInfo propertyInfo)
    {
      Type = type;
      Instance = item;
      PropertyInfo = propertyInfo;
      AssignmentType = assignmentType;
    }
    public readonly Type Type;
    public readonly object Instance;
    public readonly Type AssignmentType;

    /// <summary>
    /// Can be null (for example arrays).
    /// </summary>
    public readonly FullPropertyInfo PropertyInfo;
  }
}
