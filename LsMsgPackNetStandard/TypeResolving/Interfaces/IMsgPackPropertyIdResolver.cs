using LsMsgPack.Meta;

namespace LsMsgPack.TypeResolving.Interfaces
{
  public interface IMsgPackPropertyIdResolver
  {
    object GetId(FullPropertyInfo assignedTo, MsgPackSettings settings);
  }
}
