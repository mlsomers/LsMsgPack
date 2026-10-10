using LsMsgPack;
using LsMsgPack.Types.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ObjectDebugger
{
  /// <summary>
  /// Finds the items of the data that hold a text, or the value the text converts to (number, bool, null, Guid, date and time).
  /// <para>Shared by the explorer (MsgPackExplorer, the Fiddler inspector and the Visual Studio plugin) and the VS Code extension's inspector.</para>
  /// </summary>
  public sealed class ItemSearch
  {
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

    /// <param name="matchCase">Only applies to strings containing the text</param>
    public ItemSearch(string text, bool matchCase)
    {
      _text = text ?? string.Empty;
      _comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
      string trimmed = _text.Trim();

      bool b;
      if (bool.TryParse(trimmed, out b))
        _bool = b;
      _null = string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "nil", StringComparison.OrdinalIgnoreCase);

      // Both the invariant notation (1.5) and the one of the user (e.g. 1,5)
      CultureInfo[] numberCultures = { CultureInfo.InvariantCulture, CultureInfo.CurrentCulture };
      for (int t = 0; t < numberCultures.Length; t++)
      {
        CultureInfo culture = numberCultures[t];
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
      if (trimmed.IndexOfAny(new[] { '-', '/', '.', ':', ' ' }) > 0 || trimmed.Length == 8 && _numbers.Count > 0)
      {
        DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;
        DateTime date;
        CultureInfo[] dateCultures = { CultureInfo.CurrentCulture, CultureInfo.InvariantCulture };
        for (int t = 0; t < dateCultures.Length; t++)
        {
          CultureInfo culture = dateCultures[t];
          if (DateTime.TryParseExact(trimmed, DateFormats, culture, styles, out date))
            AddDate(date);
          if (_numbers.Count == 0 && DateTime.TryParse(trimmed, culture, styles, out date))
            AddDate(date);
        }
        int lastColon = trimmed.LastIndexOf(':');
        if (lastColon >= 0)
        {
          bool twoColons = trimmed.IndexOf(':') != lastColon;
          bool fraction = trimmed.IndexOfAny(new[] { '.', ',' }, lastColon) > 0;
          _datePrecision = !twoColons ? TimeSpan.TicksPerMinute : fraction ? TimeSpan.TicksPerMillisecond : TimeSpan.TicksPerSecond;
        }
      }
    }

    /// <summary>
    /// Collects the matching items in the order of the explorer's tree: the items of a root or array, the key and then the value of each map entry.
    /// </summary>
    /// <param name="root">The item tree, read with KEEPTRACK and PreservePackages (without them only the root's own items are searched)</param>
    /// <param name="stop">Asked every 1024 items, true stops the search (e.g. Escape held down). Optional.</param>
    /// <returns>False when stopped, the matches found so far are kept</returns>
    public bool FindAll(MsgPackItem root, List<MsgPackItem> matches, Func<bool> stop = null)
    {
      long visited = 0;
      return SearchItem(root, matches, stop, ref visited);
    }

    private bool SearchItem(MsgPackItem current, List<MsgPackItem> matches, Func<bool> stop, ref long visited)
    {
      if (ReferenceEquals(current, null))
        return true;

      visited++;
      if ((visited & 0x3FF) == 0 && stop != null && stop())
        return false;

      if (IsMatch(current))
        matches.Add(current);

      MpRoot root = current as MpRoot;
      if (root != null)
      {
        for (int t = 0; t < root.Count; t++)
          if (!SearchItem(root[t], matches, stop, ref visited))
            return false;
        return true;
      }

#if KEEPTRACK
      // Only KEEPTRACK keeps the items of arrays and maps (PreservePackages), otherwise they hold plain values
      MpArray array = current as MpArray;
      if (array != null)
      {
        MsgPackItem[] elements = array.PackedValues;
        for (int t = 0; t < elements.Length; t++)
          if (!SearchItem(elements[t], matches, stop, ref visited))
            return false;
        return true;
      }

      MpMap map = current as MpMap;
      if (map != null)
      {
        KeyValuePair<MsgPackItem, MsgPackItem>[] entries = map.PackedValues;
        for (int t = 0; t < entries.Length; t++)
        {
          if (!SearchItem(entries[t].Key, matches, stop, ref visited))
            return false;
          if (!SearchItem(entries[t].Value, matches, stop, ref visited))
            return false;
        }
        return true;
      }

      MpError error = current as MpError;
      if (error != null)
        return SearchItem(error.PartialItem, matches, stop, ref visited);
#endif

      return true;
    }

    private void AddDate(DateTime date)
    {
      // Timestamps are read as local time
      if (date.Kind == DateTimeKind.Utc)
        date = date.ToLocalTime();
      if (!_dates.Contains(date))
        _dates.Add(date);
    }

    /// <summary>
    /// The item holds the text (strings) or the value the text converts to.
    /// </summary>
    public bool IsMatch(MsgPackItem item)
    {
      try
      {
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
        if (ext != null)
        {
          byte[] bytes = (byte[])ext.Value;
          // The explorers do not register custom extensions, a decimal of LsMsgPack is an extension of type 1
          if (ext.TypeSpecifier == MpDecimal.Default_TypeSpecifier && bytes.Length == 16 && _numbers.Count > 0)
            return _numbers.Contains((decimal)new MpDecimal(ext).Value);
          return _guid != null && SameBytes(bytes, _guid);
        }
      }
      catch (Exception)
      {
        // A value that does not convert is not a match
      }
      return false;
    }

    private bool IsFloatMatch(object value)
    {
      if (value is float)
      {
        float f = (float)value;
        for (int t = _doubles.Count - 1; t >= 0; t--)
        {
          double d = _doubles[t];
          if ((float)d == f || float.IsNaN(f) && double.IsNaN(d))
            return true;
        }
        return false;
      }

      double v = Convert.ToDouble(value, CultureInfo.InvariantCulture);
      for (int t = _doubles.Count - 1; t >= 0; t--)
      {
        double d = _doubles[t];
        if (d == v || double.IsNaN(v) && double.IsNaN(d))
          return true;
      }
      return false;
    }

    private bool IsDateMatch(DateTime value)
    {
      for (int t = _dates.Count - 1; t >= 0; t--)
        if (value.Ticks / _datePrecision == _dates[t].Ticks / _datePrecision)
          return true;
      return false;
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
      if (a is null || a.Length != b.Length)
        return false;
      for (int t = 0; t < a.Length; t++)
        if (a[t] != b[t])
          return false;
      return true;
    }
  }
}
