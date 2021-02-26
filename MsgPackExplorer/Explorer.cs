using System;
using System.Windows.Forms;

namespace MsgPackExplorer {
  public partial class Explorer: Form {
    public Explorer() {
      InitializeComponent();
      ddLimitItems.SelectedIndex = 0;
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
  }
}
