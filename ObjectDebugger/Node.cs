using LsMsgPack;
using System;
using System.Collections.Generic;

namespace ObjectDebugger
{
  /// <summary>
  /// The kind of a value in the MsgPack data (what the data tells, not the .NET type it was written from).
  /// </summary>
  public enum ValueKind
  {
    Nil,
    Bool,
    Int,
    Float,
    String,
    Bin,
    Timestamp,
    Decimal,
    Extension,
    /// <summary>A value of another registered custom extension</summary>
    Other,
    Array,
    Map,
    /// <summary>The data could not be read here (KEEPTRACK with ContinueProcessingOnBreakingError)</summary>
    Error
  }

  /// <summary>
  /// One value of the data, read from the item tree where it was kept (KEEPTRACK with PreservePackages), otherwise from the unpacked values (no items, so no offsets).
  /// </summary>
  internal sealed class Node
  {
    /// <summary>
    /// The <see cref="Error"/> of a value holding a value with an error.
    /// </summary>
    public const string NestedError = "A nested item contains an error.";

    public ValueKind Kind;

    /// <summary>
    /// The value of a leaf (an <see cref="MpExt"/> for an extension without a registered type).
    /// </summary>
    public object Value;

    /// <summary>
    /// Null when only the unpacked values were kept.
    /// </summary>
    public MsgPackItem Item;

    public Node[] Elements;

    public KeyValuePair<Node, Node>[] Entries;

    /// <summary>
    /// Why the data could not be read here (only KEEPTRACK reads on after an error).
    /// </summary>
    public string Error { get; set; }

    public bool IsContainer
    {
      get { return Kind == ValueKind.Array || Kind == ValueKind.Map; }
    }

    public int Count
    {
      get
      {
        if (Kind == ValueKind.Array)
          return Elements.Length;
        if (Kind == ValueKind.Map)
          return Entries.Length;
        return 0;
      }
    }

    public static Node FromItem(MsgPackItem item)
    {
      if (item is null)
        return new Node() { Kind = ValueKind.Nil };

#if KEEPTRACK
      if (item is MpError error)
      {
        // The partial item is what could be read up to the error
        Node partial = error.PartialItem is null ? new Node() { Kind = ValueKind.Error, Item = item } : FromItem(error.PartialItem);
        // Around a partial item the error only says that it holds one (shown where it is): its own message (how to find the error with the
        // item classes) was repeated on every level above an error, 220 KB of text for data nested 300 levels deep
        string message = error.IsInNestedItem ? NestedError : error.ToString();
        partial.Error = partial.Error is null ? message : string.Concat(message, Environment.NewLine, partial.Error);
        return partial;
      }
#endif

      if (item is MpArray array)
      {
        Array values = (Array)array.Value;
#if KEEPTRACK
        MsgPackItem[] packed = array.PackedValues;
#else
        MsgPackItem[] packed = null;
#endif
        Node node = new Node() { Kind = ValueKind.Array, Item = item, Elements = new Node[values.Length] };
        for (int t = 0; t < values.Length; t++)
        {
          MsgPackItem packedItem = packed != null && t < packed.Length ? packed[t] : null;
          node.Elements[t] = packedItem is null ? FromValue(values.GetValue(t)) : FromItem(packedItem);
        }
        return node;
      }

      if (item is MpMap map)
      {
        KeyValuePair<object, object>[] values = (KeyValuePair<object, object>[])map.Value;
#if KEEPTRACK
        KeyValuePair<MsgPackItem, MsgPackItem>[] packed = map.PackedValues;
#else
        KeyValuePair<MsgPackItem, MsgPackItem>[] packed = null;
#endif
        Node node = new Node() { Kind = ValueKind.Map, Item = item, Entries = new KeyValuePair<Node, Node>[values.Length] };
        for (int t = 0; t < values.Length; t++)
        {
          MsgPackItem packedKey = packed != null && t < packed.Length ? packed[t].Key : null;
          MsgPackItem packedValue = packed != null && t < packed.Length ? packed[t].Value : null;
          node.Entries[t] = new KeyValuePair<Node, Node>(
            packedKey is null ? FromValue(values[t].Key) : FromItem(packedKey),
            packedValue is null ? FromValue(values[t].Value) : FromItem(packedValue));
        }
        return node;
      }

      if (item is MpRoot root)
      {
        Node node = new Node() { Kind = ValueKind.Array, Item = item, Elements = new Node[root.Count] };
        for (int t = 0; t < root.Count; t++)
          node.Elements[t] = FromItem(root[t]);
        return node;
      }

      // The same as MsgPackItem.UnpackedValue: an extension without a registered type keeps its type specifier
      object value = item.GetType() == typeof(MpExt) ? item : item.Value;
      return new Node() { Kind = KindOf(value), Value = value, Item = item };
    }

    /// <param name="value">An unpacked value: object[] for arrays, KeyValuePair&lt;object, object&gt;[] for maps</param>
    public static Node FromValue(object value)
    {
      if (value is MsgPackItem item)
        return FromItem(item);

      if (value is object[] values)
      {
        Node node = new Node() { Kind = ValueKind.Array, Elements = new Node[values.Length] };
        for (int t = 0; t < values.Length; t++)
          node.Elements[t] = FromValue(values[t]);
        return node;
      }

      if (value is KeyValuePair<object, object>[] pairs)
      {
        Node node = new Node() { Kind = ValueKind.Map, Entries = new KeyValuePair<Node, Node>[pairs.Length] };
        for (int t = 0; t < pairs.Length; t++)
          node.Entries[t] = new KeyValuePair<Node, Node>(FromValue(pairs[t].Key), FromValue(pairs[t].Value));
        return node;
      }

      return new Node() { Kind = KindOf(value), Value = value };
    }

    private static ValueKind KindOf(object value)
    {
      if (value is null)
        return ValueKind.Nil;
      if (value is MpExt)
        return ValueKind.Extension;
      if (value is bool)
        return ValueKind.Bool;
      if (value is string)
        return ValueKind.String;
      if (value is byte[])
        return ValueKind.Bin;
      if (value is DateTime)
        return ValueKind.Timestamp;
      if (value is decimal)
        return ValueKind.Decimal;
      if (value is float || value is double)
        return ValueKind.Float;
      if (IsInteger(value))
        return ValueKind.Int;
      return ValueKind.Other;
    }

    public static bool IsInteger(object value)
    {
      return value is sbyte || value is byte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong;
    }

    /// <summary>
    /// The value of an integer leaf (property ids and type ids in the indexed schema).
    /// </summary>
    public bool TryGetIndex(out long index)
    {
      index = 0;
      if (Kind != ValueKind.Int)
        return false;
      if (Value is ulong big)
      {
        if (big > long.MaxValue)
          return false;
        index = (long)big;
        return true;
      }
      index = Convert.ToInt64(Value);
      return true;
    }

    public bool IsString(string text)
    {
      return Kind == ValueKind.String && string.Equals((string)Value, text, StringComparison.Ordinal);
    }
  }
}
