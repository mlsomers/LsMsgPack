using LsMsgPack;
using LsMsgPack.Streaming;

namespace LsMsgPackDebug
{
  /// <summary>
  /// A sidecar of an entry of a stream (<see cref="MsgPackStreamEntry.SidecarOffset"/>): the bytes an observer of the writer added after the entry (a signature, a thumbnail; not necessarily MsgPack),
  /// wrapped in an extension value of type 4 at the root so readers skip them. Read with <see cref="MsgPackSettings.ReadStreams"/>.
  /// </summary>
  public class MpStreamSidecar : MpExt
  {
    internal MpStreamSidecar(MpExt ext) : base(ext.Settings)
    {
      CopyBaseDataFrom(ext);
    }

    public override string ToString()
    {
      return $"Stream sidecar (extension type {TypeSpecifier}) of {Count} bytes.";
    }
  }
}
