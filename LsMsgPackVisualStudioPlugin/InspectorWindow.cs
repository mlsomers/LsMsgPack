using MsgPackExplorer;
using System.Windows.Forms;

namespace LsMsgPackVisualStudioPlugin
{
  public partial class InspectorWindow : Form
  {
    public LsMsgPackExplorer Explorer;

    public InspectorWindow()
    {
      Explorer=new LsMsgPackExplorer();
      Explorer.InstallersVisible = false; // the installer only works from the explorer application

      InitializeComponent();

      Explorer.Parent = this;
      Explorer.Dock = DockStyle.Fill;
      Explorer.BringToFront();
    }
  }
}
