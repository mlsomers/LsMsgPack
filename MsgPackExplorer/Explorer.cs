using LsMsgPack;
using LsMsgPack.Types.Extensions;
using System;
using System.IO;
using System.Windows.Forms;

namespace MsgPackExplorer
{
  public partial class Explorer : Form
  {
    public Explorer()
    {
      MsgPackSettings.Default_CustomExtentionTypes = new ICustomExt[0]; // exclude custom decimal type for general purpose debugger

      InitializeComponent();
      ddLimitItems.SelectedIndex = 0;

      ddEndianess.Items.AddRange(new[]{
        new EndianChoice(EndianAction.SwapIfCurrentSystemIsLittleEndian, "Reorder if system is little endian (default)."),
        new EndianChoice(EndianAction.NeverSwap, "Never reorder"),
        new EndianChoice(EndianAction.AlwaysSwap, "Always reorder")
      });
      ddEndianess.SelectedIndex = 0;
    }

    private void btnOpen_Click(object sender, EventArgs e)
    {
      if (openFileDialog1.ShowDialog() == DialogResult.OK)
      {
        msgPackExplorer1.Data = System.IO.File.ReadAllBytes(openFileDialog1.FileName);
      }
    }

    private void btnGenerateTestFiles_Click(object sender, EventArgs e)
    {
      if (saveTestSuiteDialog.ShowDialog() == DialogResult.OK)
      {
        new TestFileSuiteCreator().CreateSuite(System.IO.Path.GetDirectoryName(saveTestSuiteDialog.FileName));
      }
    }

    private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
    {
      new AboutBox().ShowDialog(this);
    }

    private void btnProcessAfterError_CheckedChanged(object sender, EventArgs e)
    {
      msgPackExplorer1.ContinueOnError = btnProcessAfterError.Checked;
    }

    private void ddLimitItems_TextChanged(object sender, EventArgs e)
    {
      long limit;
      if (long.TryParse(ddLimitItems.Text, out limit))
        msgPackExplorer1.DisplayLimit = limit;
      else
        msgPackExplorer1.DisplayLimit = long.MaxValue;
      msgPackExplorer1.RefreshTree();
      ResetSearch(); // other items are shown
    }

    private void ddEndianess_DropDownClosed(object sender, EventArgs e)
    {
      EndianChoice choice = ddEndianess.SelectedItem as EndianChoice;
      if (choice is null)
        return;
      msgPackExplorer1.EndianHandling = choice.Value;
      msgPackExplorer1.Data = msgPackExplorer1.Data;
    }

    private void installAsFiddlerInspectorToolStripMenuItem_Click(object sender, EventArgs e)
    {
      try
      {
        string success=Installer.TryInstall(false).Trim();
        if (string.IsNullOrEmpty(success))
        {
          MessageBox.Show("Unable to find the Fiddler application files", "Not installed", MessageBoxButtons.OK, MessageBoxIcon.Error);
          return;
        }

        if (Installer.FiddlerIsRunning)
          MessageBox.Show($"Installed successfully.\r\nFiddler is currently running.\r\nYou will need to restart Fiddler in order to use the MsgPack inspector.\r\n\r\nFiles installed:\r\n{success}", "Installed", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        else
          MessageBox.Show($"Installed successfully.\r\n\r\nFiles installed:\r\n{success}", "Installed", MessageBoxButtons.OK, MessageBoxIcon.Information);

      }
      catch (Exception ex)
      {
        MessageBox.Show(string.Concat("Inastallation failed with the following message:\r\n", ex.Message, "\r\n\r\nYou may have more luck (depending on the error) running with administration privileges."), "Not installed", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    private void installVisualStudioPluginToolStripMenuItem_Click(object sender, EventArgs e)
    {
      try
      {
        string success=Installer.TryInstall(true).Trim();
        if (string.IsNullOrEmpty(success))
        {
          MessageBox.Show("Unable to find the Visual studio installation directory", "Not installed", MessageBoxButtons.OK, MessageBoxIcon.Error);
          return;
        }

        if (Installer.VsIsRunning)
          MessageBox.Show($"Installed successfully.\r\nVisual Studio is currently running.\r\nYou will need to restart Visual Studio in order to use the MsgPack inspector.\r\n\r\nFiles installed:\r\n{success}", "Installed", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        else
          MessageBox.Show($"Installed successfully.\r\n\r\nFiles installed:\r\n{success}", "Installed", MessageBoxButtons.OK, MessageBoxIcon.Information);

      }
      catch (Exception ex)
      {
        MessageBox.Show(string.Concat("Inastallation failed with the following message:\r\n", ex.Message, "\r\n\r\nYou may have more luck (depending on the error) running with administration privileges."), "Not installed", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    private void fromClipboardToolStripMenuItem_Click(object sender, EventArgs e)
    {
      string str = Clipboard.GetText();
      if (!string.IsNullOrWhiteSpace(str))
      {
        try
        {
          msgPackExplorer1.Data = ClipboardSupport.GetBytes(str);
          return;
        }
        catch (Exception ex)
        {
          MessageBox.Show("Clipboard content could not be converted to a byte array:\r\n" + ex.Message, "Parsing failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
          return;
        }
      }
      DataObject retrievedData = Clipboard.GetDataObject() as DataObject;
      if (retrievedData == null)
      {
        MessageBox.Show("Clipboard does not seem to contain anything.", "Unrecognised format", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
      }
      if (retrievedData.GetDataPresent(typeof(Byte[])))
        msgPackExplorer1.Data = retrievedData.GetData(typeof(Byte[])) as Byte[];
      else if (retrievedData.GetDataPresent(typeof(MemoryStream)))
        msgPackExplorer1.Data = (retrievedData.GetData(typeof(MemoryStream)) as MemoryStream).ToArray();
      else
      {
        MessageBox.Show("Clipboard did not contain anything recognised as a byte array, memory stream or an encoded string.", "Unrecognised format", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
      }
      return;
    }

    private void menUnistallFiddler_Click(object sender, EventArgs e)
    {
      try
      {
        string success = Installer.UnInstall(false).Trim();
        if (string.IsNullOrEmpty(success))
        {
          MessageBox.Show("Unable to find the Fiddler application files", "Not removed", MessageBoxButtons.OK, MessageBoxIcon.Error);
          return;
        }

        MessageBox.Show($"Uninstalled successfully.\r\n\r\nFiles removed:\r\n{success}", "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
      }
      catch (Exception ex)
      {
        MessageBox.Show(string.Concat("Removal failed with the following message:\r\n", ex.Message, "\r\n\r\nYou may have more luck (depending on the error) running with administration privileges."), "Not removed", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    private void menUninstallVisualStudio_Click(object sender, EventArgs e)
    {
      try
      {
        string success = Installer.UnInstall(true).Trim();
        if (string.IsNullOrEmpty(success))
        {
          MessageBox.Show("Unable to find the Visual studio installation directory", "Not removed", MessageBoxButtons.OK, MessageBoxIcon.Error);
          return;
        }

        MessageBox.Show($"Uninstalled successfully.\r\n\r\nFiles removed:\r\n{success}", "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);

      }
      catch (Exception ex)
      {
        MessageBox.Show(string.Concat("Removal failed with the following message:\r\n", ex.Message, "\r\n\r\nYou may have more luck (depending on the error) running with administration privileges."), "Not removed", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }
	
	  private void objectsMenuItem_CheckedChanged(object sender, EventArgs e){
		  msgPackExplorer1.ObjectsVisible = objectsMenuItem.Checked;
    }

    private void msgPackExplorer1_ItemChanged(object sender, EventArgs e)
    {
      // Data with a schema was written from objects, show them (the menu item can still hide them)
      objectsMenuItem.Checked = msgPackExplorer1.HasSchema;
      ResetSearch();
    }

    #region Search

    /// <summary>
    /// Null until previous or next is clicked after the text (or the data) changed.
    /// </summary>
    private LsMsgPackExplorer.SearchResult _searchResult;
    private int _searchPosition;

    private void searchTextBox_TextChanged(object sender, EventArgs e)
    {
      ResetSearch();
    }

    private void searchMatchCase_CheckedChanged(object sender, EventArgs e)
    {
      ResetSearch();
    }

    /// <summary>
    /// The next click on previous or next searches again.
    /// </summary>
    private void ResetSearch()
    {
      _searchResult = null;
      bool hasText = searchTextBox.Text.Length > 0;
      searchPrev.Enabled = hasText;
      searchNext.Enabled = hasText;
      searchPosCount.Text = "0/0";
      searchPosCount.ToolTipText = null;
    }

    private void searchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.KeyCode != Keys.Enter)
        return;
      e.Handled = true;
      e.SuppressKeyPress = true; // no beep
      if (e.Shift)
      {
        if (searchPrev.Enabled)
          SearchStep(-1);
      }
      else if (searchNext.Enabled)
        SearchStep(1);
    }

    private void searchPrev_Click(object sender, EventArgs e)
    {
      SearchStep(-1);
    }

    private void searchNext_Click(object sender, EventArgs e)
    {
      SearchStep(1);
    }

    /// <summary>
    /// The first click after a change searches the whole data and goes to the first item found, the next ones move through the items found.
    /// </summary>
    private void SearchStep(int step)
    {
      if (_searchResult is null)
      {
        Cursor.Current = Cursors.WaitCursor;
        try
        {
          _searchResult = msgPackExplorer1.Search(searchTextBox.Text, searchMatchCase.Checked);
        }
        finally
        {
          Cursor.Current = Cursors.Default;
        }
        _searchPosition = 0;
      }
      else
        _searchPosition += step;

      ShowSearchPosition();
    }

    private void ShowSearchPosition()
    {
      int count = _searchResult.Displayed.Count;
      int total = _searchResult.TotalCount;
      _searchPosition = Math.Max(0, Math.Min(_searchPosition, count - 1));

      // Items beyond the display limit are not in the tree, so they are counted but cannot be selected
      string text = string.Concat(count == 0 ? 0 : _searchPosition + 1, "/", count);
      if (total > count)
        text = string.Concat(text, "/", total);
      searchPosCount.Text = text;

      if (_searchResult.Interrupted)
        searchPosCount.ToolTipText = "Searching was stopped (Escape), these are the items found so far.";
      else if (total > count)
        searchPosCount.ToolTipText = "Position / found within the display limit / found in all the data.";
      else
        searchPosCount.ToolTipText = "Position / found.";

      searchPrev.Enabled = _searchPosition > 0;
      searchNext.Enabled = _searchPosition < count - 1;
      if (count > 0)
        msgPackExplorer1.SelectItem(_searchResult.Displayed[_searchPosition]);
    }

    #endregion
  }

  public class EndianChoice
  {
    public EndianChoice(EndianAction value, string description)
    {
      Value = value;
      Description = description;
    }

    private string Description { get; }
    public EndianAction Value { get; }

    public override string ToString()
    {
      return Description;
    }
  }
}
