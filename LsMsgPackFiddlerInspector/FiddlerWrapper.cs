using System;
using System.Windows.Forms;
using MsgPackExplorer;

namespace LsMsgPackFiddlerInspector {
  public partial class FiddlerWrapper: UserControl {
    public FiddlerWrapper() {
      InitializeComponent();
    }

    private void toolStripButton1_CheckedChanged(object sender, EventArgs e) {
      lsMsgPackExplorer1.ContinueOnError = toolStripButton1.Checked;
    }

    private void aboutToolStripMenuItem_Click(object sender, EventArgs e) {
      new AboutBox().ShowDialog(this);
    }
  }
}
