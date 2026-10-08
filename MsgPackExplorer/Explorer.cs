using LsMsgPack;
using LsMsgPack.Types.Extensions;
using System.Windows.Forms;

namespace MsgPackExplorer
{
  public partial class Explorer : Form
  {
    public Explorer()
    {
      MsgPackSettings.Default_CustomExtentionTypes = new ICustomExt[0]; // exclude custom decimal type for general purpose debugger

      InitializeComponent();
    }
  }
}
