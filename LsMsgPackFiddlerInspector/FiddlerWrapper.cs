using System;
using System.Windows.Forms;
using LsMsgPack;
using MsgPackExplorer;

namespace LsMsgPackFiddlerInspector {
  public partial class FiddlerWrapper: UserControl {
    private readonly ExplorerSearch _search;

    public FiddlerWrapper() {
      InitializeComponent();
      _search = new ExplorerSearch(lsMsgPackExplorer1, searchTextBox, searchMatchCase, searchPrev, searchNext, searchPosCount);
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
      _search.Reset(); // other items are shown
    }

    private void ddEndianess_DropDownClosed(object sender, EventArgs e) {
      EndianChoice choice = ddEndianess.SelectedItem as EndianChoice;
      if (choice is null)
        return;
      lsMsgPackExplorer1.EndianHandling = choice.Value;
      lsMsgPackExplorer1.Data = lsMsgPackExplorer1.Data;
    }

    private void btnObjects_CheckedChanged(object sender, EventArgs e) {
      lsMsgPackExplorer1.ObjectsVisible = btnObjects.Checked;
    }

    private void lsMsgPackExplorer1_ItemChanged(object sender, EventArgs e) {
      // Data with a schema was written from objects, show them (the button can still hide them)
      btnObjects.Checked = lsMsgPackExplorer1.HasSchema;
    }
  }

}
