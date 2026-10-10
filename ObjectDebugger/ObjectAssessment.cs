using LsMsgPack;
using System;
using System.Collections.Generic;

namespace ObjectDebugger
{
  /// <summary>
  /// How sure we are that the data was written from objects (so the objects pane shows more than the MsgPack tree).
  /// </summary>
  public enum ObjectConfidence
  {
    /// <summary>No map with text keys: nothing to name</summary>
    None,
    /// <summary>Maps with text keys, which may as well be dictionaries</summary>
    Low,
    /// <summary>A map with names as keys and values of different kinds (a dictionary's values are usually of one kind)</summary>
    Medium,
    /// <summary>Several maps with the same names as keys: records of one type</summary>
    High,
    /// <summary>Written by LsMsgPack or LtMsgPack from objects: an indexed schema (or a reference to one), or a type id</summary>
    Certain
  }

  /// <summary>
  /// Decides whether to show the objects of data that may have no schema (e.g. written in another language), from the shape of the data alone.
  /// </summary>
  public sealed class ObjectAssessment
  {
    private const int MaxNameLength = 64;
    private const int MaxKeysInReason = 4;

    public ObjectConfidence Confidence { get; private set; }

    /// <summary>
    /// Why, for a tooltip (empty for <see cref="ObjectConfidence.None"/>).
    /// </summary>
    public string Reason { get; private set; } = string.Empty;

    private readonly Dictionary<string, int> _keySets = new Dictionary<string, int>(StringComparer.Ordinal);

    private ObjectAssessment()
    {
    }

    /// <param name="item">An <see cref="MpRoot"/> or a single item, as for <see cref="RootObject.Reconstruct(MsgPackItem, SchemaStore)"/></param>
    public static ObjectAssessment Assess(MsgPackItem item)
    {
      ObjectAssessment assessment = new ObjectAssessment();
      if (item is null)
        return assessment;

      if (RootObject.FindSchemaItems(item).Count > 0)
      {
        assessment.Raise(ObjectConfidence.Certain, "The data has an indexed schema.");
        return assessment;
      }

      assessment.Visit(Node.FromItem(item));
      return assessment;
    }

    private void Raise(ObjectConfidence confidence, string reason)
    {
      if (confidence <= Confidence)
        return;
      Confidence = confidence;
      Reason = reason;
    }

    private void Visit(Node node)
    {
      if (Confidence == ObjectConfidence.Certain)
        return;

      if (node.Kind == ValueKind.Array)
      {
        for (int t = 0; t < node.Elements.Length; t++)
          Visit(node.Elements[t]);
        return;
      }

      if (node.Kind != ValueKind.Map)
        return;

      AssessMap(node);
      for (int t = 0; t < node.Entries.Length; t++)
      {
        Visit(node.Entries[t].Key);
        Visit(node.Entries[t].Value);
      }
    }

    private void AssessMap(Node map)
    {
      KeyValuePair<Node, Node>[] entries = map.Entries;
      if (entries.Length == 0)
        return;

      // The serializer writes the type id first, under the key ""
      Node first = entries[0].Key;
      ValueKind typeIdKind = entries[0].Value.Kind;
      if (first.IsString(string.Empty) && (typeIdKind == ValueKind.Int || typeIdKind == ValueKind.String))
      {
        Raise(ObjectConfidence.Certain, "The data has type ids (the map key \"\").");
        return;
      }

      string[] keys = new string[entries.Length];
      bool names = true;
      for (int t = 0; t < entries.Length; t++)
      {
        if (entries[t].Key.Kind != ValueKind.String)
          return;
        keys[t] = (string)entries[t].Key.Value;
        names &= IsName(keys[t]);
      }
      Raise(ObjectConfidence.Low, "The data has maps with text keys.");
      if (!names)
        return;

      // Objects of one type have the same names, a dictionary's keys are data
      Array.Sort(keys, StringComparer.Ordinal);
      string keySet = string.Join("\0", keys);
      int count;
      _keySets.TryGetValue(keySet, out count);
      _keySets[keySet] = ++count;
      if (count > 1)
      {
        Raise(ObjectConfidence.High, string.Concat("Maps with the same names as keys (", Describe(keys), ") look like objects of one type."));
        return;
      }

      if (HasMixedValues(entries))
        Raise(ObjectConfidence.Medium, string.Concat("A map with names as keys (", Describe(keys), ") and values of different kinds looks like an object."));
    }

    /// <summary>
    /// Looks like a property name (any case convention), not like data (numbers, ids, dates, paths, text).
    /// </summary>
    private static bool IsName(string key)
    {
      if (key.Length == 0 || key.Length > MaxNameLength)
        return false;
      char first = key[0];
      if (!char.IsLetter(first) && first != '_' && first != '$' && first != '@')
        return false;
      for (int t = 1; t < key.Length; t++)
      {
        char c = key[t];
        if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '$')
          return false;
      }
      Guid guid;
      return !Guid.TryParseExact(key, "D", out guid) && !Guid.TryParseExact(key, "N", out guid);
    }

    private static bool HasMixedValues(KeyValuePair<Node, Node>[] entries)
    {
      if (entries.Length < 2)
        return false;
      ValueKind? seen = null;
      for (int t = entries.Length - 1; t >= 0; t--)
      {
        ValueKind kind = entries[t].Value.Kind;
        if (kind == ValueKind.Nil || kind == ValueKind.Error)
          continue;
        // A dictionary of numbers may hold integers and floats
        if (kind == ValueKind.Float || kind == ValueKind.Decimal)
          kind = ValueKind.Int;
        if (seen is null)
          seen = kind;
        else if (seen.Value != kind)
          return true;
      }
      return false;
    }

    private static string Describe(string[] keys)
    {
      if (keys.Length <= MaxKeysInReason)
        return string.Join(", ", keys);
      return string.Concat(string.Join(", ", keys, 0, MaxKeysInReason), ", ...");
    }
  }
}
