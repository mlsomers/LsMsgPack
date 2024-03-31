using LsMsgPack.TypeResolving.Interfaces;
using System;

namespace LsMsgPack.Meta
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

    /// <summary>
    /// This can be overridden by implementing <see cref="IMsgPackTypeResolver">IMsgPackTypeResolver</see>.
    /// </summary>
    public object GetTypeIdentifier(MsgPackSettings settings)
    {
      object typeId = null;

      for (int t = settings.TypeResolvers.Length - 1; t >= 0; t--)
      {
        typeId = settings.TypeResolvers[t].IdForType(Type, PropertyInfo);
        if (typeId != null)
          break;
      }
      if (typeId is null && !((settings._addTypeName & AddTypeIdOption.NoDefaultFallBack) > 0))
      {
        bool fullname = (settings._addTypeName & AddTypeIdOption.FullName) > 0;
        typeId = GetTypeName(Type, fullname);
      }

      return typeId;
    }

    private string GetTypeName(Type type, bool fullname)
    {
      Type[] args = type.GenericTypeArguments;

      if (args.Length==0)
        return fullname ? type.FullName : type.Name;

      // Get the generic type name...

      string[] names = new string[args.Length];
      for (int t = args.Length - 1; t >= 0; t--)
        names[t] = GetTypeName(args[t], fullname);

      string typeName = fullname ? type.FullName : type.Name;
      typeName = typeName.Substring(0, typeName.IndexOf('`'));

      return string.Concat(typeName, '<', string.Join(", ", names), '>');
    }
  }
}
