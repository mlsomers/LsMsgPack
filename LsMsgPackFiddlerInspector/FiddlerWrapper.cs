using System;
using System.Windows.Forms;
using LsMsgPack;
using MsgPackExplorer;

namespace LsMsgPackFiddlerInspector {
  public partial class FiddlerWrapper: UserControl {
    public FiddlerWrapper() {
      InitializeComponent();
      ddLimitItems.SelectedIndex = 0;

      ddEndianess.Items.AddRange(new[]{
        new EndianChoice(EndianAction.SwapIfCurrentSystemIsLittleEndian, "Reorder if system is little endian (default)."),
        new EndianChoice(EndianAction.NeverSwap, "Never reorder"),
        new EndianChoice(EndianAction.AlwaysSwap, "Always reorder")
      });
      ddEndianess.SelectedIndex = 0;
    }

    private void toolStripButton1_CheckedChanged(object sender, EventArgs e) {
      lsMsgPackExplorer1.ContinueOnError = toolStripButton1.Checked;
    }

    private void aboutToolStripMenuItem_Click(object sender, EventArgs e) {
      new AboutBox().ShowDialog(this);
    }

    private void ddLimitItems_TextChanged(object sender, EventArgs e) {
      long limit;
      if (long.TryParse(ddLimitItems.Text, out limit))
        lsMsgPackExplorer1.DisplayLimit = limit;
      else
        lsMsgPackExplorer1.DisplayLimit = long.MaxValue;
      lsMsgPackExplorer1.RefreshTree();
    }

    private void ddEndianess_DropDownClosed(object sender, EventArgs e) {
      EndianChoice choice = ddEndianess.SelectedItem as EndianChoice;
      if (choice is null)
        return;
      lsMsgPackExplorer1.EndianHandling = choice.Value;
      lsMsgPackExplorer1.Data = lsMsgPackExplorer1.Data;
    }
  }

}
