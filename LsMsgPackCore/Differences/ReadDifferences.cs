using LsMsgPack.Meta;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace LsMsgPack
{
  /// <summary>
  /// What did not match between the data and the classes it was read into, counted per class and property (returned by the Deserialize overloads with an <c>out ReadDifferences</c>).
  /// Reading goes on as it does without it: unknown properties and extra values are skipped, an object of a class without a schema entry is left null.
  /// <para>Only the objects the differences were found on are kept (<see cref="Difference.Samples"/>), their paths are found in <see cref="Root"/> when the report is generated
  /// (<see cref="GenerateReport"/>), so collecting costs nothing while the data matches. The paths are the ones at that time: a graph that was changed after reading may give other paths, or none.</para>
  /// <para>When the deserialization throws, the differences found until then are in the exception's <see cref="Exception.Data"/> (<see cref="ExceptionDataKey"/>), without a <see cref="Root"/>.</para>
  /// </summary>
  [Serializable]
  public sealed class ReadDifferences
  {
    /// <summary>
    /// The key of the differences in <see cref="Exception.Data"/> of an exception thrown while reading with an <c>out ReadDifferences</c>.
    /// </summary>
    public const string ExceptionDataKey = "LsMsgPack.ReadDifferences";

    /// <summary>
    /// The number of objects kept per difference (<see cref="Difference.Samples"/>), 10 by default.
    /// </summary>
    public static int MaxSamples { get; set; } = 10;

    /// <summary>
    /// The number of different differences kept (per class and name), 100 by default. The data decides the names, so a hostile payload could otherwise fill the memory. Further ones are only counted (<see cref="Omitted"/>).
    /// </summary>
    public static int MaxDifferences { get; set; } = 100;

    /// <summary>
    /// Names in the data longer than this are cut.
    /// </summary>
    internal const int MaxNameLength = 256;

    /// <summary>
    /// The number of objects the report looks at to find the paths of the samples.
    /// </summary>
    internal const int MaxWalk = 200000;

    [NonSerialized]
    private readonly MsgPackOptions _settings;
    private readonly List<Difference> _differences = new List<Difference>();
    [NonSerialized]
    private readonly Dictionary<Key, Difference> _byKey = new Dictionary<Key, Difference>();
    [NonSerialized]
    private readonly List<object> _parents = new List<object>();

    /// <param name="settings">The settings of the call (the static filters decide which properties the report walks)</param>
    internal ReadDifferences(MsgPackOptions settings)
    {
      _settings = settings;
    }

    /// <summary>
    /// The value that was read (null when the deserialization did not finish).
    /// </summary>
    public object Root { get; internal set; }

    /// <summary>
    /// The differences in the order they were found.
    /// </summary>
    public IReadOnlyList<Difference> Differences { get { return _differences; } }

    /// <summary>
    /// The occurrences of differences that were not kept (more than <see cref="MaxDifferences"/> different ones).
    /// </summary>
    public int Omitted { get; private set; }

    internal bool IsEmpty { get { return _differences.Count == 0 && Omitted == 0; } }

    #region Collecting

    /// <summary>
    /// The objects whose values are being read (the last one holds an object of a class without a schema entry).
    /// </summary>
    internal void Push(object instance)
    {
      _parents.Add(instance);
    }

    internal void Pop()
    {
      if (_parents.Count > 0)
        _parents.RemoveAt(_parents.Count - 1);
    }

    /// <summary>
    /// Forgets what was found: the work is repeated (a shared schema session grew, see SchemaGrowthException).
    /// </summary>
    internal void Reset()
    {
      _differences.Clear();
      _byKey.Clear();
      _parents.Clear();
      Omitted = 0;
    }

    internal void UnknownProperty(object parent, object key)
    {
      string name = key as string ?? Convert.ToString(key, CultureInfo.InvariantCulture);
      Add(parent.GetType(), DifferenceKind.UnknownProperty, Cut(name), -1, parent);
    }

    /// <param name="length">The length of the name in bytes (UTF-8)</param>
    internal void UnknownProperty(object parent, byte[] buffer, int offset, int length)
    {
      int max = MaxNameLength * 4; // the most bytes MaxNameLength characters take
      string name = MsgPackOptions.StringEncoding.GetString(buffer, offset, Math.Min(length, max));
      Add(parent.GetType(), DifferenceKind.UnknownProperty, Cut(name), -1, parent);
    }

    internal void ExtraValue(object parent, int position)
    {
      Add(parent.GetType(), DifferenceKind.ExtraValue, null, position, parent);
    }

    /// <param name="type">The class without a schema entry</param>
    /// <param name="assignedTo">The property the object is assigned to, null for the root and the elements of collections</param>
    internal void UnmatchedClass(Type type, FullPropertyInfo assignedTo)
    {
      object parent = _parents.Count > 0 ? _parents[_parents.Count - 1] : null;
      Add(type, DifferenceKind.UnmatchedClass, assignedTo?.PropertyInfo.Name, -1, parent);
    }

    /// <summary>
    /// The differences found until the exception, for finding its cause (e.g. a property that was not set because its name was misspelled). Used in an exception filter, so the exception is passed on as it is.
    /// </summary>
    /// <returns>False</returns>
    internal bool AttachTo(Exception ex)
    {
      try
      {
        if (!IsEmpty && !ex.Data.IsReadOnly && !ex.Data.Contains(ExceptionDataKey))
          ex.Data[ExceptionDataKey] = this;
      }
      catch (Exception) // e.g. a Data that does not take it: the exception matters more
      {
      }
      return false;
    }

    private void Add(Type type, DifferenceKind kind, string name, int position, object sample)
    {
      Key key = new Key(type, kind, name, position);
      if (!_byKey.TryGetValue(key, out Difference difference))
      {
        if (_differences.Count >= MaxDifferences)
        {
          Omitted++;
          return;
        }
        difference = new Difference(type, kind, name, position);
        _byKey.Add(key, difference);
        _differences.Add(difference);
      }

      difference.Count++;
      if (sample != null && difference._samples.Count < MaxSamples && (difference._samples.Count == 0 || !ReferenceEquals(difference._samples[difference._samples.Count - 1], sample)))
        difference._samples.Add(sample);
    }

    private static string Cut(string name)
    {
      if (name is null || name.Length <= MaxNameLength)
        return name;
      return string.Concat(name.Substring(0, MaxNameLength), "…");
    }

    private struct Key : IEquatable<Key>
    {
      private readonly Type _type;
      private readonly DifferenceKind _kind;
      private readonly string _name;
      private readonly int _position;

      internal Key(Type type, DifferenceKind kind, string name, int position)
      {
        _type = type;
        _kind = kind;
        _name = name;
        _position = position;
      }

      public bool Equals(Key other)
      {
        return _type == other._type && _kind == other._kind && _position == other._position && string.Equals(_name, other._name, StringComparison.Ordinal);
      }

      public override bool Equals(object obj)
      {
        return obj is Key other && Equals(other);
      }

      public override int GetHashCode()
      {
        unchecked
        {
          int hash = _type is null ? 0 : _type.GetHashCode();
          hash = hash * 31 + (int)_kind;
          hash = hash * 31 + _position;
          return hash * 31 + (_name is null ? 0 : StringComparer.Ordinal.GetHashCode(_name));
        }
      }
    }

    #endregion

    #region Report

    /// <summary>
    /// A description of the differences: per class and name the count, the paths of the first objects (found in <see cref="Root"/>), and for unknown properties the property it might be meant for
    /// (one with a similar name) and the properties of the first object that were left at their default.
    /// </summary>
    public string GenerateReport()
    {
      StringBuilder report = new StringBuilder();
      if (IsEmpty)
        return "The data matched the classes.";

      report.Append(_differences.Count == 1 ? "1 difference" : string.Concat(_differences.Count.ToString(CultureInfo.InvariantCulture), " differences"))
        .Append(" between the data and the classes:");

      Walker walker = new Walker(_settings);
      Dictionary<object, Found> found = walker.FindPaths(Root, Samples());
      HashSet<Type> defaultsListed = new HashSet<Type>();
      foreach (Difference difference in _differences)
      {
        List<object> samples = InGraphOrder(difference, found);
        report.Append("\r\n- ").Append(difference);
        AppendPaths(report, difference, samples, found);
        if (difference.Kind == DifferenceKind.UnknownProperty)
          AppendHints(report, difference, samples.Count > 0 ? samples[0] : null, walker, defaultsListed.Add(difference.Class));
      }

      if (Omitted > 0)
        report.Append("\r\n- ").Append(Omitted.ToString(CultureInfo.InvariantCulture)).Append(" more, not kept (more than ").Append(MaxDifferences.ToString(CultureInfo.InvariantCulture)).Append(" different ones).");
      if (Root is null)
        report.Append("\r\n(The deserialization did not finish, the paths are not known.)");
      return report.ToString();
    }

    /// <summary>
    /// The path of an object in <see cref="Root"/> ($ for the root, .Name for a property, [0] for an element, ["key"] for a dictionary value), null when it is not found.
    /// </summary>
    public string PathOf(object instance)
    {
      if (instance is null)
        return null;
      HashSet<object> targets = new HashSet<object>(ReferenceComparer.Instance) { instance };
      new Walker(_settings).FindPaths(Root, targets).TryGetValue(instance, out Found found);
      return found.Path;
    }

    public override string ToString()
    {
      return GenerateReport();
    }

    private HashSet<object> Samples()
    {
      HashSet<object> samples = new HashSet<object>(ReferenceComparer.Instance);
      foreach (Difference difference in _differences)
        foreach (object sample in difference._samples)
          samples.Add(sample);
      return samples;
    }

    /// <summary>
    /// The samples in the order of the graph (breadth first), which is the order of the data for objects at the same depth (LsMsgPack converts from the last value to the first).
    /// </summary>
    private static List<object> InGraphOrder(Difference difference, Dictionary<object, Found> found)
    {
      List<KeyValuePair<int, object>> ordered = new List<KeyValuePair<int, object>>();
      foreach (object sample in difference._samples)
        ordered.Add(new KeyValuePair<int, object>(found.TryGetValue(sample, out Found at) ? at.Order : int.MaxValue, sample));
      List<object> samples = new List<object>(ordered.Count);
      foreach (KeyValuePair<int, object> sample in ordered.OrderBy(o => o.Key)) // stable: the samples that were not found keep their order
        samples.Add(sample.Value);
      return samples;
    }

    private static void AppendPaths(StringBuilder report, Difference difference, List<object> samples, Dictionary<object, Found> found)
    {
      if (samples.Count == 0)
        return;

      report.Append(difference.Kind == DifferenceKind.UnmatchedClass ? "\r\n    in " : "\r\n    at ");
      for (int t = 0; t < samples.Count; t++)
      {
        if (t > 0)
          report.Append(", ");
        report.Append(found.TryGetValue(samples[t], out Found at) ? at.Path : string.Concat("(not found: ", samples[t].GetType().Name, ")"));
      }
      if (difference.Count > samples.Count && difference.Kind != DifferenceKind.UnmatchedClass)
        report.Append(", …");
    }

    /// <summary>
    /// The property an unknown name may be meant for (a misspelling, another case), and the properties the first object left at their default (often the ones the data was meant for).
    /// </summary>
    /// <param name="listDefaults">The properties left at their default are listed once per class</param>
    private static void AppendHints(StringBuilder report, Difference difference, object first, Walker walker, bool listDefaults)
    {
      if (difference.Class is null || difference.Name is null)
        return;

      FullPropertyInfo[] props = walker.Properties(difference.Class);
      List<string> defaults = new List<string>();
      if (first != null)
      {
        foreach (FullPropertyInfo prop in props)
          if (Walker.TryGetValue(prop, first, out object value) && IsDefault(value))
            defaults.Add(prop.PropertyInfo.Name);
      }

      string best = null;
      int bestDistance = int.MaxValue;
      int allowed = difference.Name.Length <= 4 ? 1 : 2;
      foreach (FullPropertyInfo prop in props)
      {
        string name = prop.PropertyInfo.Name;
        int distance = Distance(difference.Name.ToLowerInvariant(), name.ToLowerInvariant());
        if (distance > allowed)
          continue;
        if (distance < bestDistance || (distance == bestDistance && defaults.Contains(name) && !defaults.Contains(best)))
        {
          best = name;
          bestDistance = distance;
        }
      }

      if (best != null)
        report.Append("\r\n    did you mean ").Append(best).Append(defaults.Contains(best) ? "? (left at its default)" : "?");

      if (listDefaults && defaults.Count > 0)
      {
        const int maxNames = 10;
        report.Append("\r\n    left at their default (first object): ").Append(string.Join(", ", defaults.GetRange(0, Math.Min(defaults.Count, maxNames))));
        if (defaults.Count > maxNames)
          report.Append(", …");
      }
    }

    private static bool IsDefault(object value)
    {
      if (value is null)
        return true;
      Type type = value.GetType();
      return type.IsValueType && value.Equals(Activator.CreateInstance(type));
    }

    /// <summary>
    /// The number of characters to add, remove, replace or swap (adjacent) to turn one name into the other (optimal string alignment).
    /// </summary>
    internal static int Distance(string a, string b)
    {
      if (Math.Abs(a.Length - b.Length) > 2)
        return int.MaxValue; // more than any name is allowed to differ
      int[,] d = new int[a.Length + 1, b.Length + 1];
      for (int i = 0; i <= a.Length; i++)
        d[i, 0] = i;
      for (int j = 0; j <= b.Length; j++)
        d[0, j] = j;
      for (int i = 1; i <= a.Length; i++)
      {
        for (int j = 1; j <= b.Length; j++)
        {
          int cost = a[i - 1] == b[j - 1] ? 0 : 1;
          int best = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
          if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
            best = Math.Min(best, d[i - 2, j - 2] + 1);
          d[i, j] = best;
        }
      }
      return d[a.Length, b.Length];
    }

    /// <summary>
    /// Finds objects in the graph that was read, breadth first (the shortest path), over the properties the serializer reads (the static filters of the settings), the elements of collections and the values of dictionaries.
    /// </summary>
    private sealed class Walker
    {
      private readonly MsgPackOptions _settings;
      private readonly Dictionary<Type, FullPropertyInfo[]> _props = new Dictionary<Type, FullPropertyInfo[]>();

      internal Walker(MsgPackOptions settings)
      {
        _settings = settings?.CloneOptions(); // without the caches of a schema session: the report does not change them
      }

      internal Dictionary<object, Found> FindPaths(object root, HashSet<object> targets)
      {
        Dictionary<object, Found> found = new Dictionary<object, Found>(ReferenceComparer.Instance);
        if (root is null || targets.Count == 0)
          return found;

        List<Node> nodes = new List<Node>();
        HashSet<object> visited = new HashSet<object>(ReferenceComparer.Instance);
        nodes.Add(new Node() { Value = root, Parent = -1, Segment = "$" });
        for (int n = 0; n < nodes.Count && n < MaxWalk && found.Count < targets.Count; n++)
        {
          object value = nodes[n].Value;
          if (!visited.Add(value))
            continue;
          if (targets.Contains(value))
            found[value] = new Found() { Path = PathTo(nodes, n), Order = n };
          AddChildren(nodes, n, value);
        }
        return found;
      }

      private struct Node
      {
        internal object Value;
        internal int Parent;
        internal string Segment;
      }

      private static string PathTo(List<Node> nodes, int n)
      {
        List<string> segments = new List<string>();
        for (int t = n; t >= 0; t = nodes[t].Parent)
          segments.Add(nodes[t].Segment);
        segments.Reverse();
        return string.Concat(segments);
      }

      private void AddChildren(List<Node> nodes, int n, object value)
      {
        if (value is IDictionary dictionary)
        {
          foreach (DictionaryEntry entry in dictionary)
            Add(nodes, n, entry.Value, string.Concat("[", KeyText(entry.Key), "]"));
          return;
        }

        if (value is IEnumerable enumerable && !(value is string))
        {
          int index = 0;
          foreach (object element in enumerable)
          {
            if (element != null && FrameworkTypeInfo.IsKeyValuePair(element.GetType())) // a collection of pairs is a map
            {
              FrameworkTypeInfo.PairInfo pair = FrameworkTypeInfo.GetPair(element.GetType());
              Add(nodes, n, pair.Value.GetValue(element), string.Concat("[", KeyText(pair.Key.GetValue(element)), "]"));
            }
            else
              Add(nodes, n, element, string.Concat("[", index.ToString(CultureInfo.InvariantCulture), "]"));
            index++;
          }
          return;
        }

        foreach (FullPropertyInfo prop in Properties(value.GetType()))
          if (TryGetValue(prop, value, out object child))
            Add(nodes, n, child, string.Concat(".", prop.PropertyInfo.Name));
      }

      private static void Add(List<Node> nodes, int parent, object value, string segment)
      {
        if (value != null && IsWalked(value.GetType()))
          nodes.Add(new Node() { Value = value, Parent = parent, Segment = segment });
      }

      /// <summary>
      /// Objects that can hold the objects that were read: not the values the serializers write themselves (numbers, strings, dates...).
      /// </summary>
      private static bool IsWalked(Type type)
      {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type.IsPointer || typeof(Delegate).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type))
          return false;
        if (type.Assembly == typeof(object).Assembly) // framework types: only collections (DateTime, Guid, Uri... hold no objects that were read)
          return typeof(IEnumerable).IsAssignableFrom(type);
        return true;
      }

      internal FullPropertyInfo[] Properties(Type type)
      {
        if (_props.TryGetValue(type, out FullPropertyInfo[] props))
          return props;

        List<FullPropertyInfo> readable = new List<FullPropertyInfo>();
        if (_settings != null)
        {
          foreach (FullPropertyInfo prop in FullPropertyInfo.GetStaticallyIncludedProps(type, _settings))
            if (prop.PropertyInfo.CanRead && prop.PropertyInfo.GetIndexParameters().Length == 0 && !prop.PropertyInfo.GetGetMethod(true).IsStatic)
              readable.Add(prop);
        }
        props = readable.ToArray();
        _props[type] = props;
        return props;
      }

      internal static bool TryGetValue(FullPropertyInfo prop, object instance, out object value)
      {
        try
        {
          value = prop.GetValue(instance);
          return true;
        }
        catch (Exception) // a getter that throws (e.g. a value that was not set): not a path
        {
          value = null;
          return false;
        }
      }

      private static string KeyText(object key)
      {
        if (key is string text)
          return string.Concat("\"", text, "\"");
        return Convert.ToString(key, CultureInfo.InvariantCulture);
      }
    }

    /// <summary>
    /// Where a sample was found: its path, and its position in the walk (breadth first).
    /// </summary>
    private struct Found
    {
      internal string Path;
      internal int Order;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
      internal static readonly ReferenceComparer Instance = new ReferenceComparer();

      bool IEqualityComparer<object>.Equals(object x, object y)
      {
        return ReferenceEquals(x, y);
      }

      int IEqualityComparer<object>.GetHashCode(object obj)
      {
        return RuntimeHelpers.GetHashCode(obj);
      }
    }

    #endregion
  }
}
