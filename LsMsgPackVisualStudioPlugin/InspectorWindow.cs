using LsMsgPack;
using MsgPackExplorer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LsMsgPackVisualStudioPlugin
{
  public partial class InspectorWindow : Form
  {
    public LsMsgPackExplorer Explorer;

    public InspectorWindow()
    {
      Explorer=new LsMsgPackExplorer();

      InitializeComponent();
      
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
    }

    private void ddEndianess_DropDownClosed(object sender, EventArgs e) {
      EndianChoice choice = ddEndianess.SelectedItem as EndianChoice;
      if (choice is null)
        return;
      Explorer.EndianHandling = choice.Value;
      Explorer.Data = Explorer.Data;
    }
  }
}
