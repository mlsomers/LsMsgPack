using LsMsgPack.TypeResolving.Interfaces;

namespace LsMsgPack.TypeResolving
{
  public class FilterNonSettable : IMsgPackPropertyIncludeStatically
  {
    private bool _onlyPublic;
    public FilterNonSettable(bool onlyPublic = true) { _onlyPublic = onlyPublic; }

    public bool IncludeProperty(FullPropertyInfo propertyInfo)
    {
      if (!propertyInfo.PropertyInfo.CanWrite)
        return false;

      System.Reflection.MethodInfo mth = propertyInfo.PropertyInfo.SetMethod;
      if (mth is null)
        return false;

      if(_onlyPublic && !mth.IsPublic)
        return false;
      
      return true;
    }
  }
}
