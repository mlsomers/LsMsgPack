using LsMsgPack;
using System;
using System.Windows.Forms;
using System.Linq;

namespace MsgPackExplorer {
  public partial class Explorer: Form {
    public Explorer() {
      InitializeComponent();
      ddLimitItems.SelectedIndex = 0;

      ddEndianess.Items.AddRange(new[]{
        new EndianChoice(EndianAction.SwapIfCurrentSystemIsLittleEndian, "Reorder if system is little endian (default)."),
        new EndianChoice(EndianAction.NeverSwap, "Never reorder"),
        new EndianChoice(EndianAction.AlwaysSwap, "Always reorder")
      });
      ddEndianess.SelectedIndex = 0;
    }

    private void btnOpen_Click(object sender, EventArgs e) {
      if(openFileDialog1.ShowDialog() == DialogResult.OK) {
        msgPackExplorer1.Data = System.IO.File.ReadAllBytes(openFileDialog1.FileName);
      }
    }

    private void btnGenerateTestFiles_Click(object sender, EventArgs e) {
      if(saveTestSuiteDialog.ShowDialog()== DialogResult.OK) {
        new TestFileSuiteCreator().CreateSuite(System.IO.Path.GetDirectoryName(saveTestSuiteDialog.FileName));
      }
    }

    private void aboutToolStripMenuItem_Click(object sender, EventArgs e) {
      new AboutBox().ShowDialog(this);
    }

    private void btnProcessAfterError_CheckedChanged(object sender, EventArgs e) {
      msgPackExplorer1.ContinueOnError = btnProcessAfterError.Checked;
    }

    private void ddLimitItems_TextChanged(object sender, EventArgs e) {
      long limit;
      if (long.TryParse(ddLimitItems.Text, out limit))
        msgPackExplorer1.DisplayLimit = limit;
      else
        msgPackExplorer1.DisplayLimit = long.MaxValue;
      msgPackExplorer1.RefreshTree();
    }

    private void ddEndianess_DropDownClosed(object sender, EventArgs e) {
      EndianChoice choice = ddEndianess.SelectedItem as EndianChoice;
      if (choice is null)
        return;
      msgPackExplorer1.EndianHandling = choice.Value;
      msgPackExplorer1.Data = msgPackExplorer1.Data;
    }
  }

  public class EndianChoice {
    public EndianChoice(EndianAction value, string description) {
      Value = value;
      Description = description;
    }

    private string Description { get; }
    public EndianAction Value { get; }

    public override string ToString() {
      return Description;
    }
  }
}
