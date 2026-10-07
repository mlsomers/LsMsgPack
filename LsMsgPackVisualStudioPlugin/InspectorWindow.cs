using LsMsgPack;
using MsgPackExplorer;
using System;
using System.Windows.Forms;

namespace LsMsgPackVisualStudioPlugin
{
  public partial class InspectorWindow : Form
  {
    public LsMsgPackExplorer Explorer;
    private readonly ExplorerSearch _search;

    public InspectorWindow()
    {
      Explorer=new LsMsgPackExplorer();

      InitializeComponent();
      Explorer.ItemChanged += Explorer_ItemChanged;
      _search = new ExplorerSearch(Explorer, searchTextBox, searchMatchCase, searchPrev, searchNext, searchPosCount);

      ddLimitItems.SelectedIndex = 0;

      ddEndianess.Items.AddRange(new[]{
        new EndianChoice(EndianAction.SwapIfCurrentSystemIsLittleEndian, "Reorder if system is little endian (default)."),
        new EndianChoice(EndianAction.NeverSwap, "Never reorder"),
        new EndianChoice(EndianAction.AlwaysSwap, "Always reorder")
      });
      ddEndianess.SelectedIndex = 0;
            
      Explorer.Parent = this;
      Explorer.Dock = DockStyle.Fill;
      Explorer.BringToFront();
    }

    private void toolStripButton1_CheckedChanged(object sender, EventArgs e)
    {
      Explorer.ContinueOnError = toolStripButton1.Checked;
    }

    private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
    {
      new AboutBox().ShowDialog(this);
    }

    private void ddLimitItems_TextChanged(object sender, EventArgs e)
    {
      long limit;
      if (long.TryParse(ddLimitItems.Text, out limit))
        Explorer.DisplayLimit = limit;
      else
        Explorer.DisplayLimit = long.MaxValue;
      Explorer.RefreshTree();
      _search.Reset(); // other items are shown
    }

    private void ddEndianess_DropDownClosed(object sender, EventArgs e) {
      EndianChoice choice = ddEndianess.SelectedItem as EndianChoice;
      if (choice is null)
        return;
      Explorer.EndianHandling = choice.Value;
      Explorer.Data = Explorer.Data;
    }

    private void btnObjects_CheckedChanged(object sender, EventArgs e)
    {
      Explorer.ObjectsVisible = btnObjects.Checked;
    }

    private const string ObjectsToolTip = "Show the objects the data was written from (switched on when the data looks like objects)";

    private void Explorer_ItemChanged(object sender, EventArgs e)
    {
      // Data that was likely written from objects: show them (the button can still hide them)
      btnObjects.Checked = Explorer.ObjectsLikely;
      string reason = Explorer.ObjectsReason;
      btnObjects.ToolTipText = reason.Length == 0 ? ObjectsToolTip : string.Concat(ObjectsToolTip, "\r\n", reason);
    }
  }
}
