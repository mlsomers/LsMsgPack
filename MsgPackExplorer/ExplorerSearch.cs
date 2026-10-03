using System;
using System.Windows.Forms;

namespace MsgPackExplorer {
  /// <summary>
  /// Connects the search items of a tool strip to an explorer, so the explorer application, the Fiddler inspector and the Visual Studio plugin share the behavior.
  /// </summary>
  public sealed class ExplorerSearch {
    private readonly LsMsgPackExplorer _explorer;
    private readonly ToolStripTextBox _textBox;
    private readonly ToolStripButton _matchCase;
    private readonly ToolStripButton _prev;
    private readonly ToolStripButton _next;
    private readonly ToolStripLabel _posCount;

    /// <summary>
    /// Null until previous or next is clicked after the text (or the data) changed.
    /// </summary>
    private LsMsgPackExplorer.SearchResult _searchResult;
    private int _searchPosition;

    public ExplorerSearch(LsMsgPackExplorer explorer, ToolStripTextBox textBox, ToolStripButton matchCase, ToolStripButton prev, ToolStripButton next, ToolStripLabel posCount) {
      _explorer = explorer;
      _textBox = textBox;
      _matchCase = matchCase;
      _prev = prev;
      _next = next;
      _posCount = posCount;

      _textBox.TextChanged += (sender, e) => Reset();
      _textBox.KeyDown += TextBox_KeyDown;
      _matchCase.CheckedChanged += (sender, e) => Reset();
      _prev.Click += (sender, e) => SearchStep(-1);
      _next.Click += (sender, e) => SearchStep(1);
      _explorer.ItemChanged += (sender, e) => Reset();
      Reset();
    }

    /// <summary>
    /// The next click on previous or next searches again. Call it when other items are shown (e.g. the display limit changed).
    /// </summary>
    public void Reset() {
      _searchResult = null;
      bool hasText = _textBox.Text.Length > 0;
      _prev.Enabled = hasText;
      _next.Enabled = hasText;
      _posCount.Text = "0/0";
      _posCount.ToolTipText = null;
    }

    private void TextBox_KeyDown(object sender, KeyEventArgs e) {
      if (e.KeyCode != Keys.Enter)
        return;
      e.Handled = true;
      e.SuppressKeyPress = true; // no beep
      if (e.Shift) {
        if (_prev.Enabled)
          SearchStep(-1);
      }
      else if (_next.Enabled)
        SearchStep(1);
    }

    /// <summary>
    /// The first click after a change searches the whole data and goes to the first item found, the next ones move through the items found.
    /// </summary>
    private void SearchStep(int step) {
      if (_searchResult is null) {
        Cursor.Current = Cursors.WaitCursor;
        try {
          _searchResult = _explorer.Search(_textBox.Text, _matchCase.Checked);
        }
        finally {
          Cursor.Current = Cursors.Default;
        }
        _searchPosition = 0;
      }
      else
        _searchPosition += step;

      ShowSearchPosition();
    }

    private void ShowSearchPosition() {
      int count = _searchResult.Displayed.Count;
      int total = _searchResult.TotalCount;
      _searchPosition = Math.Max(0, Math.Min(_searchPosition, count - 1));

      // Items beyond the display limit are not in the tree, so they are counted but cannot be selected
      string text = string.Concat(count == 0 ? 0 : _searchPosition + 1, "/", count);
      if (total > count)
        text = string.Concat(text, "/", total);
      _posCount.Text = text;

      if (_searchResult.Interrupted)
        _posCount.ToolTipText = "Searching was stopped (Escape), these are the items found so far.";
      else if (total > count)
        _posCount.ToolTipText = "Position / found within the display limit / found in all the data.";
      else
        _posCount.ToolTipText = "Position / found.";

      _prev.Enabled = _searchPosition > 0;
      _next.Enabled = _searchPosition < count - 1;
      if (count > 0)
        _explorer.SelectItem(_searchResult.Displayed[_searchPosition]);
    }
  }
}
