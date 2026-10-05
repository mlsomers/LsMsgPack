using LsMsgPack;
using ObjectDebugger;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MsgPackExplorer {
  // In this partial class the search through all items of the data
  partial class LsMsgPackExplorer {

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_ESCAPE = 0x1B;

    /// <summary>
    /// What <see cref="Search"/> found.
    /// </summary>
    public sealed class SearchResult {
      /// <summary>
      /// The matching items that are shown in the tree (within the display limit), in the order of the data.
      /// </summary>
      public List<MsgPackItem> Displayed { get; } = new List<MsgPackItem>();

      /// <summary>
      /// All matching items in the data, also those beyond the display limit.
      /// </summary>
      public int TotalCount { get; internal set; }

      /// <summary>
      /// Escape was held down, only the items before that point were searched.
      /// </summary>
      public bool Interrupted { get; internal set; }
    }

    /// <summary>
    /// Finds all items of the data that hold the text, or the value the text converts to (number, bool, null, Guid, date and time). Holding Escape stops the search.
    /// </summary>
    /// <param name="matchCase">Only applies to strings containing the text</param>
    public SearchResult Search(string text, bool matchCase) {
      SearchResult result = new SearchResult();
      if (ReferenceEquals(item, null) || string.IsNullOrEmpty(text))
        return result;

      Dictionary<MsgPackItem, TreeNode> displayed = new Dictionary<MsgPackItem, TreeNode>();
      foreach (TreeNode node in treeView1.Nodes)
        CollectDisplayed(node, displayed);

      // In the order of the tree (see Traverse). Checking the keyboard costs a call to Windows, a few thousand items take no noticeable time
      List<MsgPackItem> matches = new List<MsgPackItem>();
      result.Interrupted = !new ItemSearch(text, matchCase).FindAll(item, matches, () => (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0);

      result.TotalCount = matches.Count;
      foreach (MsgPackItem match in matches)
        if (displayed.ContainsKey(match))
          result.Displayed.Add(match);
      return result;
    }

    /// <summary>
    /// Selects the item in the MsgPack tree (which selects its bytes and its object).
    /// </summary>
    public void SelectItem(MsgPackItem target) {
      TreeNode node = FindNode(treeView1.Nodes, target);
      if (node is null)
        return;
      treeView1.SelectedNode = node;
      node.EnsureVisible();
    }

    private static TreeNode FindNode(TreeNodeCollection nodes, MsgPackItem target) {
      foreach (TreeNode node in nodes) {
        if (ReferenceEquals(node.Tag, target))
          return node;
        TreeNode found = FindNode(node.Nodes, target);
        if (found != null)
          return found;
      }
      return null;
    }

    private static void CollectDisplayed(TreeNode node, Dictionary<MsgPackItem, TreeNode> displayed) {
      MsgPackItem nodeItem = node.Tag as MsgPackItem;
      if (nodeItem != null)
        displayed[nodeItem] = node;
      foreach (TreeNode child in node.Nodes)
        CollectDisplayed(child, displayed);
    }
  }
}
