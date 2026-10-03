using LsMsgPack;
using LsMsgPack.Types.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
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

      SearchCriteria criteria = new SearchCriteria(text, matchCase);
      List<MsgPackItem> matches = new List<MsgPackItem>();
      long visited = 0;
      result.Interrupted = !SearchItem(item, criteria, matches, ref visited);

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

    /// <summary>
    /// In the order of the tree (see Traverse): the items of a root or array, the key and then the value of each map entry.
    /// </summary>
    /// <returns>False when interrupted (Escape held down)</returns>
    private static bool SearchItem(MsgPackItem current, SearchCriteria criteria, List<MsgPackItem> matches, ref long visited) {
      if (ReferenceEquals(current, null))
        return true;

      // Checking the keyboard costs a call to Windows, a few thousand items take no noticeable time
      visited++;
      if ((visited & 0x3FF) == 0 && (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
        return false;

      if (criteria.IsMatch(current))
        matches.Add(current);

      MpRoot root = current as MpRoot;
      if (root != null) {
        for (int t = 0; t < root.Count; t++)
          if (!SearchItem(root[t], criteria, matches, ref visited))
            return false;
        return true;
      }

      MpArray array = current as MpArray;
      if (array != null) {
        foreach (MsgPackItem element in array.PackedValues)
          if (!SearchItem(element, criteria, matches, ref visited))
            return false;
        return true;
      }

      MpMap map = current as MpMap;
      if (map != null) {
        foreach (KeyValuePair<MsgPackItem, MsgPackItem> entry in map.PackedValues) {
          if (!SearchItem(entry.Key, criteria, matches, ref visited))
            return false;
          if (!SearchItem(entry.Value, criteria, matches, ref visited))
            return false;
        }
        return true;
      }

      MpError error = current as MpError;
      if (error != null)
        return SearchItem(error.PartialItem, criteria, matches, ref visited);

      return true;
    }

    /// <summary>
    /// The text, and the values it converts to.
    /// </summary>
    private sealed class SearchCriteria {
      private static readonly string[] DateFormats = new string[] {
        "o", "s", "u",
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss",
        "yyyy/MM/dd", "yyyy/MM/dd HH:mm", "yyyy/MM/dd HH:mm:ss", "yyyyMMdd", "yyyyMMddHHmmss",
        "dd-MM-yyyy", "dd-MM-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss", "d-M-yyyy", "d-M-yyyy H:mm", "d-M-yyyy H:mm:ss",
        "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy",
        "MM/dd/yyyy", "MM/dd/yyyy HH:mm", "MM/dd/yyyy HH:mm:ss", "M/d/yyyy", "M/d/yyyy h:mm tt", "M/d/yyyy h:mm:ss tt",
        "dd.MM.yyyy", "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss", "d.M.yyyy",
        "d MMM yyyy", "d MMMM yyyy", "MMM d, yyyy", "MMMM d, yyyy"
      };

      private readonly string _text;
      private readonly StringComparison _comparison;
      private readonly bool? _bool;
      private readonly bool _null;
      private readonly List<decimal> _numbers = new List<decimal>();
      private readonly List<double> _doubles = new List<double>();
      private readonly byte[] _guid;
      private readonly List<DateTime> _dates = new List<DateTime>();

      /// <summary>
      /// What the text tells of the time: a date matches the whole day, "14:30" the whole minute.
      /// </summary>
      private readonly long _datePrecision = TimeSpan.TicksPerDay;

      public SearchCriteria(string text, bool matchCase) {
        _text = text;
        _comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string trimmed = text.Trim();

        bool b;
        if (bool.TryParse(trimmed, out b))
          _bool = b;
        _null = string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "nil", StringComparison.OrdinalIgnoreCase);

        // Both the invariant notation (1.5) and the one of the user (e.g. 1,5)
        foreach (CultureInfo culture in new[] { CultureInfo.InvariantCulture, CultureInfo.CurrentCulture }) {
          decimal number;
          if (decimal.TryParse(trimmed, NumberStyles.Float, culture, out number) && !_numbers.Contains(number))
            _numbers.Add(number);
          double d;
          if (double.TryParse(trimmed, NumberStyles.Float, culture, out d) && !_doubles.Contains(d))
            _doubles.Add(d);
        }

        Guid guid;
        if (Guid.TryParse(trimmed, out guid))
          _guid = guid.ToByteArray();

        // Only text with a separator, numbers like 2026 are not dates
        if (trimmed.IndexOfAny(new[] { '-', '/', '.', ':', ' ' }) > 0 || trimmed.Length == 8 && _numbers.Count > 0) {
          DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;
          DateTime date;
          foreach (CultureInfo culture in new[] { CultureInfo.CurrentCulture, CultureInfo.InvariantCulture }) {
            if (DateTime.TryParseExact(trimmed, DateFormats, culture, styles, out date))
              AddDate(date);
            if (_numbers.Count == 0 && DateTime.TryParse(trimmed, culture, styles, out date))
              AddDate(date);
          }
          int lastColon = trimmed.LastIndexOf(':');
          if (lastColon >= 0) {
            bool twoColons = trimmed.IndexOf(':') != lastColon;
            bool fraction = trimmed.IndexOfAny(new[] { '.', ',' }, lastColon) > 0;
            _datePrecision = !twoColons ? TimeSpan.TicksPerMinute : fraction ? TimeSpan.TicksPerMillisecond : TimeSpan.TicksPerSecond;
          }
        }
      }

      private void AddDate(DateTime date) {
        // Timestamps are read as local time
        if (date.Kind == DateTimeKind.Utc)
          date = date.ToLocalTime();
        if (!_dates.Contains(date))
          _dates.Add(date);
      }

      public bool IsMatch(MsgPackItem item) {
        try {
          if (item is MpString)
            return ((string)item.Value ?? string.Empty).IndexOf(_text, _comparison) >= 0;
          if (item is MpBool)
            return _bool.HasValue && (bool)item.Value == _bool.Value;
          if (item is MpNull)
            return _null;
          if (item is MpInt)
            return _numbers.Count > 0 && _numbers.Contains(Convert.ToDecimal(item.Value, CultureInfo.InvariantCulture));
          if (item is MpFloat)
            return IsFloatMatch(item.Value);
          if (item is MpBin)
            return _guid != null && SameBytes((byte[])item.Value, _guid);
          if (item is MpDateTime)
            return IsDateMatch((DateTime)item.Value);
          if (item is MpDecimal)
            return _numbers.Contains((decimal)item.Value);

          MpExt ext = item as MpExt;
          if (ext != null) {
            byte[] bytes = (byte[])ext.Value;
            // The explorer does not register custom extensions, a decimal of LsMsgPack is an extension of type 1
            if (ext.TypeSpecifier == MpDecimal.Default_TypeSpecifier && bytes.Length == 16 && _numbers.Count > 0)
              return _numbers.Contains((decimal)new MpDecimal(ext).Value);
            return _guid != null && SameBytes(bytes, _guid);
          }
        } catch (Exception) {
          // A value that does not convert is not a match
        }
        return false;
      }

      private bool IsFloatMatch(object value) {
        if (value is float) {
          float f = (float)value;
          foreach (double d in _doubles)
            if ((float)d == f || float.IsNaN(f) && double.IsNaN(d))
              return true;
          return false;
        }

        double v = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        foreach (double d in _doubles)
          if (d == v || double.IsNaN(v) && double.IsNaN(d))
            return true;
        return false;
      }

      private bool IsDateMatch(DateTime value) {
        foreach (DateTime date in _dates)
          if (value.Ticks / _datePrecision == date.Ticks / _datePrecision)
            return true;
        return false;
      }

      private static bool SameBytes(byte[] a, byte[] b) {
        if (a is null || a.Length != b.Length)
          return false;
        for (int t = 0; t < a.Length; t++)
          if (a[t] != b[t])
            return false;
        return true;
      }
    }
  }
}
