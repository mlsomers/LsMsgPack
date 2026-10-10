using System;
using System.Collections.Generic;
using System.Globalization;

namespace ObjectDebugger
{
  /// <summary>
  /// Builds the object tree of one payload, the way LsMsgPack writes it:
  /// <list type="bullet">
  /// <item>an object is a map of property ids to values, or (<c>ObjectLayout.Array</c>) an array of its values in the order of its properties;</item>
  /// <item>the map key "" holds the type id, "@" the content of a wrapped value: <c>{ "": typeId, "@": [values or elements], ...properties }</c>;</item>
  /// <item>with the indexed schema, type ids are indexes of the types in the schema and property ids indexes of the names of the type's properties.</item>
  /// </list>
  /// <para>Without a type id the data does not tell the type. With the schema it is inferred: the writer adds a type to the schema when it starts writing the first value of that type (depth first), so a value of a type that was not seen yet must be of the first type of the schema that was not seen yet.
  /// Candidates must have the shape of the value (number of values, property ids in range), and the kinds of values seen before at the same positions. Values in the same place (the same property of the same type, or the elements of the same collection) are assumed to have the same type.</para>
  /// </summary>
  internal sealed class ObjectReconstructor
  {
    private const string TypeIdKey = "";
    private const string ContentKey = "@";

    private const int CollectionSlot = -1;

    /// <summary>
    /// Null without a schema (or when the referred schema is not available).
    /// </summary>
    private readonly List<SchemaType> _types;

    private readonly bool[] _seen;
    private int _nextUnseen;

    /// <summary>
    /// What a place in the object model (<see cref="ComplexObject.Slot"/>) was found to hold: a type index or <see cref="CollectionSlot"/>.
    /// </summary>
    private readonly Dictionary<string, int> _slots = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Per type, the kinds of the values seen at each property index (<see cref="ValueKind.Nil"/> while unknown).
    /// </summary>
    private readonly Dictionary<int, ValueKind[]> _signatures = new Dictionary<int, ValueKind[]>();

    public ObjectReconstructor(List<SchemaType> types)
    {
      _types = types;
      _seen = new bool[types?.Count ?? 0];
    }

    public void FillRoot(RootObject root, Node body)
    {
      root.Slot = string.Empty;
      if (body.Kind == ValueKind.Map && IsWrappedValue(body))
      {
        // A primitive value that needed a type id
        root.Kind = ObjectKind.Value;
        PrimitiveObject value = BuildMember(body, "Value", root, null, null);
        root.Type = value.Type;
        root.ValueKind = value.ValueKind;
        root.Value = value.Value;
        return;
      }

      if (!body.IsContainer)
      {
        root.Kind = ObjectKind.Value;
        root.ValueKind = body.Kind;
        root.Value = body.Value;
        BuildMember(body, "Value", root, null, null);
        return;
      }

      Fill(root, body);
    }

    /// <summary>
    /// Adds the value to the parent's members before filling it, so what was reconstructed stays when it fails half way.
    /// </summary>
    private PrimitiveObject BuildMember(Node node, string name, ComplexObject parent, string slot, Node key)
    {
      PrimitiveObject member;
      if (node.IsContainer && !IsWrappedValue(node))
        member = new ComplexObject() { Slot = slot, ValueKind = node.Kind };
      else
        member = new PrimitiveObject();

      member.Name = name;
      member.Parent = parent;
      member.FirstItemRef = key?.Item ?? node.Item;
      member.LastItemRef = node.Item;
      if (key?.Error != null)
        member.AddError(key.Error);
      if (node.Error != null)
        member.AddError(node.Error);
      parent.Members.Add(member);

      try
      {
        if (member is ComplexObject complex)
          Fill(complex, node);
        else if (node.Kind == ValueKind.Map)
          FillWrappedValue(member, node);
        else
        {
          member.ValueKind = node.Kind;
          member.Value = node.Value;
        }
      }
      catch (Exception ex)
      {
        member.AddError(ex.Message);
      }
      return member;
    }

    private void Fill(ComplexObject target, Node node)
    {
      if (node.Kind == ValueKind.Map)
        FillMap(target, node);
      else
        FillArray(target, node);
    }

    /// <summary>
    /// <c>{ "": typeId, "@": value }</c> where the value is not a container: a primitive value assigned to a property of another type (e.g. object).
    /// </summary>
    private static bool IsWrappedValue(Node node)
    {
      if (node.Kind != ValueKind.Map || node.Entries.Length != 2)
        return false;
      return node.Entries[0].Key.IsString(TypeIdKey) && node.Entries[1].Key.IsString(ContentKey) && !node.Entries[1].Value.IsContainer;
    }

    private void FillWrappedValue(PrimitiveObject target, Node node)
    {
      SchemaType type;
      target.Type = ResolveTypeId(node.Entries[0].Value, out type);
      if (type != null)
        MarkSeen(type);
      Node content = node.Entries[1].Value;
      target.ValueKind = content.Kind;
      target.Value = content.Value;
      target.LastItemRef = content.Item ?? target.LastItemRef;
      if (content.Error != null)
        target.AddError(content.Error);
    }

    private void FillMap(ComplexObject target, Node node)
    {
      Node typeId = null;
      Node content = null;
      List<KeyValuePair<Node, Node>> others = new List<KeyValuePair<Node, Node>>(node.Entries.Length);
      for (int t = 0; t < node.Entries.Length; t++)
      {
        KeyValuePair<Node, Node> entry = node.Entries[t];
        if (typeId is null && entry.Key.IsString(TypeIdKey))
          typeId = entry.Value;
        else if (content is null && entry.Key.IsString(ContentKey))
          content = entry.Value;
        else
          others.Add(entry);
      }

      SchemaType type = null;
      if (typeId != null)
        target.Type = ResolveTypeId(typeId, out type);

      if (content != null)
      {
        // { "": typeId, "@": [values] } is an object written as an array when its type has properties (a collection type only has the properties it was written with)
        if (content.Kind == ValueKind.Array && others.Count == 0 && type != null && type.Properties.Count > 0)
        {
          target.Kind = ObjectKind.Object;
          target.SchemaType = type;
          MarkSeen(type); // the writer adds the type before writing the values
          FillPositional(target, content, type);
          return;
        }

        // A collection (or dictionary) wrapped for its type id or its properties: the writer writes the elements before it adds the collection's type
        target.Kind = content.Kind == ValueKind.Map ? ObjectKind.Dictionary : ObjectKind.Collection;
        target.SchemaType = type;
        if (content.Kind == ValueKind.Array)
          FillElements(target, content);
        else if (content.Kind == ValueKind.Map)
          FillEntries(target, content);
        else
          BuildMember(content, ContentKey, target, ElementSlot(target), null);
        if (type != null)
          MarkSeen(type);
        FillProperties(target, others, type);
        return;
      }

      if (type != null)
      {
        target.Kind = ObjectKind.Object;
        target.SchemaType = type;
        MarkSeen(type);
        FillProperties(target, others, type);
        return;
      }

      if (typeId is null && others.Count > 0 && _types != null && AllKeysAreIndexes(others))
      {
        type = Infer(node, target.Slot);
        if (type != null)
        {
          SetInferred(target, type);
          FillProperties(target, others, type);
          return;
        }
        target.Kind = ObjectKind.Dictionary;
        FillEntries(target, node);
        return;
      }

      if (typeId is null && others.Count > 0 && !AllKeysAreStrings(others))
      {
        target.Kind = ObjectKind.Dictionary;
        FillEntries(target, node);
        return;
      }

      // Keys are property names (no schema), a schema writes property ids as indexes so string keys are those of a dictionary (or custom property ids)
      target.Kind = _types is null || others.Count == 0 || typeId != null ? ObjectKind.Object : ObjectKind.Dictionary;
      FillProperties(target, others, null);
    }

    private void FillArray(ComplexObject target, Node node)
    {
      SchemaType type = _types is null ? null : Infer(node, target.Slot);
      if (type is null)
      {
        target.Kind = ObjectKind.Collection;
        FillElements(target, node);
        return;
      }

      SetInferred(target, type);
      FillPositional(target, node, type);
    }

    private void SetInferred(ComplexObject target, SchemaType type)
    {
      target.Kind = ObjectKind.Object;
      target.Type = type.Name;
      target.TypeIsGuess = true;
      target.SchemaType = type;
    }

    private void FillPositional(ComplexObject target, Node values, SchemaType type)
    {
      RecordSignature(type, values);
      for (int t = 0; t < values.Elements.Length; t++)
      {
        string name = t < type.Properties.Count ? PropertyName(type, t) : Index(t);
        BuildMember(values.Elements[t], name, target, MemberSlot(target, name), null);
      }
    }

    private void FillElements(ComplexObject target, Node values)
    {
      string slot = ElementSlot(target);
      InferTogether(values.Elements, slot);
      for (int t = 0; t < values.Elements.Length; t++)
        BuildMember(values.Elements[t], Index(t), target, slot, null);
    }

    private void FillEntries(ComplexObject target, Node map)
    {
      string slot = TypeKey(target) + "{}";
      Node[] values = new Node[map.Entries.Length];
      for (int t = 0; t < values.Length; t++)
        values[t] = map.Entries[t].Value;
      InferTogether(values, slot);
      for (int t = 0; t < map.Entries.Length; t++)
      {
        Node key = map.Entries[t].Key;
        Node value = map.Entries[t].Value;
        if (!key.IsContainer)
        {
          BuildMember(value, KeyText(key), target, slot, key);
          continue;
        }

        // A key that is an object or collection, keep key and value together
        ComplexObject entry = new ComplexObject() { Kind = ObjectKind.Entry, Name = Index(t), Parent = target, FirstItemRef = key.Item, LastItemRef = value.Item };
        target.Members.Add(entry);
        BuildMember(key, "Key", entry, TypeKey(target) + "{key}", null);
        BuildMember(value, "Value", entry, slot, null);
      }
    }

    private void FillProperties(ComplexObject target, List<KeyValuePair<Node, Node>> entries, SchemaType type)
    {
      if (type != null)
        RecordSignature(type, entries);

      for (int t = 0; t < entries.Count; t++)
      {
        KeyValuePair<Node, Node> entry = entries[t];
        long index;
        string name;
        if (type != null && entry.Key.TryGetIndex(out index) && index >= 0 && index < type.Properties.Count)
          name = PropertyName(type, (int)index);
        else
          name = KeyText(entry.Key);
        BuildMember(entry.Value, name, target, MemberSlot(target, name), entry.Key);
      }
    }

    /// <returns>The type name (or a description of the id when the type is unknown)</returns>
    private string ResolveTypeId(Node typeId, out SchemaType type)
    {
      type = null;
      long index;
      if (typeId.TryGetIndex(out index))
      {
        if (_types != null && index >= 0 && index < _types.Count)
        {
          type = _types[(int)index];
          return type.Name;
        }
        return string.Concat("type #", index.ToString(CultureInfo.InvariantCulture));
      }

      if (typeId.Kind == ValueKind.String)
      {
        string name = (string)typeId.Value;
        if (_types != null)
          type = _types.Find(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        return name;
      }

      return string.Concat("type ", PrimitiveObject.FormatValue(typeId.Value));
    }

    #region Inferring types

    private enum Fit
    {
      None,
      /// <summary>Fewer values than properties (trailing nils trimmed, or properties left out of a map)</summary>
      Partial,
      Exact
    }

    /// <summary>
    /// The schema type of an array or a map with property indexes as keys, null when it is a collection (or dictionary).
    /// </summary>
    private SchemaType Infer(Node node, string slot)
    {
      int remembered;
      if (_slots.TryGetValue(slot, out remembered))
      {
        if (remembered == CollectionSlot && node.Kind == ValueKind.Array)
          return null;
        if (remembered >= 0 && GetFit(_types[remembered], node) != Fit.None && Score(_types[remembered], node, Fit.Partial) != int.MinValue)
        {
          MarkSeen(_types[remembered]);
          return _types[remembered];
        }
      }

      SchemaType best = null;
      int bestScore = int.MinValue;
      foreach (SchemaType candidate in Candidates())
      {
        Fit fit = GetFit(candidate, node);
        if (fit == Fit.None)
          continue;
        int score = Score(candidate, node, fit);
        if (score == int.MinValue)
          continue;
        if (score > bestScore) // the next type that was not seen yet comes first, so it wins a tie
        {
          best = candidate;
          bestScore = score;
        }
      }

      if (node.Kind == ValueKind.Array)
      {
        int collectionScore = CollectionScore(node);
        if (best is null || bestScore < collectionScore)
        {
          _slots[slot] = CollectionSlot;
          return null;
        }
      }

      if (best is null)
        return null;

      _slots[slot] = best.Index;
      MarkSeen(best);
      return best;
    }

    /// <summary>
    /// The elements of a collection (or values of a dictionary) have the same declared type, so they are of the same type when they have no type id.
    /// <para>Deciding for all of them at once sees more: objects of one type have the same number of values (unless trailing nils are trimmed), lists differ.</para>
    /// </summary>
    private void InferTogether(Node[] values, string slot)
    {
      if (_types is null || _slots.ContainsKey(slot))
        return;

      List<Node> nodes = new List<Node>(values.Length);
      for (int t = 0; t < values.Length; t++)
      {
        Node value = values[t];
        if (value.Kind == ValueKind.Array || value.Kind == ValueKind.Map && value.Entries.Length > 0 && AllKeysAreIndexes(value.Entries))
          nodes.Add(value);
      }
      if (nodes.Count < 2)
        return;
      for (int t = nodes.Count - 1; t > 0; t--)
        if (nodes[t].Kind != nodes[0].Kind)
          return;

      SchemaType best = null;
      long bestScore = long.MinValue;
      foreach (SchemaType candidate in Candidates())
      {
        long score = 0;
        for (int t = nodes.Count - 1; t >= 0; t--)
        {
          Node node = nodes[t];
          Fit fit = GetFit(candidate, node);
          int nodeScore = fit == Fit.None ? int.MinValue : Score(candidate, node, fit);
          if (nodeScore == int.MinValue)
          {
            score = long.MinValue;
            break;
          }
          score += nodeScore;
        }
        if (score != long.MinValue && score > bestScore)
        {
          best = candidate;
          bestScore = score;
        }
      }

      if (nodes[0].Kind == ValueKind.Array)
      {
        long collectionScore = 0;
        for (int t = nodes.Count - 1; t >= 0; t--)
        {
          int score = CollectionScore(nodes[t]);
          if (score == int.MinValue)
          {
            collectionScore = long.MinValue;
            break;
          }
          collectionScore += score == int.MaxValue ? 2 : score;
        }
        if (collectionScore != long.MinValue && (best is null || bestScore < collectionScore))
        {
          _slots[slot] = CollectionSlot;
          return;
        }
      }

      if (best != null)
        _slots[slot] = best.Index;
    }

    /// <summary>
    /// The types seen so far and the first type that was not seen yet (the only new type the next object can have).
    /// </summary>
    private IEnumerable<SchemaType> Candidates()
    {
      if (_nextUnseen < _types.Count)
        yield return _types[_nextUnseen];
      for (int t = 0; t < _types.Count; t++)
        if (_seen[t])
          yield return _types[t];
    }

    private static Fit GetFit(SchemaType type, Node node)
    {
      int count = type.Properties.Count;
      if (count == 0) // a type without properties is only in the schema for its type id
        return Fit.None;

      if (node.Kind == ValueKind.Array)
      {
        int length = node.Elements.Length;
        if (length == count)
          return Fit.Exact;
        // TrimTrailingNulls leaves out the nils at the end, so a shorter array never ends with nil
        if (length > 0 && length < count && node.Elements[length - 1].Kind != ValueKind.Nil)
          return Fit.Partial;
        return Fit.None;
      }

      if (node.Kind != ValueKind.Map)
        return Fit.None;

      for (int t = node.Entries.Length - 1; t >= 0; t--)
      {
        long index;
        if (!node.Entries[t].Key.TryGetIndex(out index) || index < 0 || index >= count)
          return Fit.None;
      }
      return node.Entries.Length == count ? Fit.Exact : Fit.Partial;
    }

    /// <returns><see cref="int.MinValue"/> when a value can not be of the type: a property has one encoding per declared type (a primitive value
    /// assigned to a property of another type gets a type id), so a primitive kind differing from one seen before at that position rules the
    /// type out. Objects and collections only count against it: a value of a derived type is wrapped for its type id.</returns>
    private int Score(SchemaType type, Node node, Fit fit)
    {
      int score = fit == Fit.Exact ? 2 : 0;

      ValueKind[] signature;
      if (!_signatures.TryGetValue(type.Index, out signature))
        return score;

      for (int t = 0; t < node.Count; t++)
      {
        Node value;
        int position;
        if (node.Kind == ValueKind.Array)
        {
          value = node.Elements[t];
          position = t;
        }
        else
        {
          long index;
          node.Entries[t].Key.TryGetIndex(out index);
          value = node.Entries[t].Value;
          position = (int)index;
        }

        ValueKind kind = ComparableKind(value);
        if (kind == ValueKind.Nil || position >= signature.Length || signature[position] == ValueKind.Nil)
          continue;
        if (kind == signature[position])
          score++;
        else if (IsContainerKind(kind) && IsContainerKind(signature[position]))
          score -= 3;
        else
          return int.MinValue;
      }
      return score;
    }

    /// <summary>
    /// How much an array looks like a collection: elements of one kind (an object's values usually differ), elements that are objects themselves.
    /// </summary>
    private int CollectionScore(Node node)
    {
      if (node.Elements.Length == 0)
        return int.MaxValue; // an object without values is not in the schema

      // An element of a derived type is wrapped for its type id: { "": typeId, "@": [values] } is in the place of an array
      Node first = null;
      ValueKind firstKind = ValueKind.Nil;
      for (int t = 0; t < node.Elements.Length; t++)
      {
        Node element = node.Elements[t];
        ValueKind kind = ComparableKind(element);
        if (kind == ValueKind.Nil)
          continue;
        if (first is null)
        {
          first = element;
          firstKind = kind;
        }
        else if (kind != firstKind)
          return int.MinValue;
      }

      if (first is null || !IsContainerKind(firstKind))
        return 2;

      // Elements that are objects themselves: when the first one can not be an object of the schema, this is more likely an object with object values
      return CouldBeObject(first) ? 3 : 0;
    }

    private bool CouldBeObject(Node node)
    {
      if (node.Kind == ValueKind.Map)
      {
        for (int t = node.Entries.Length - 1; t >= 0; t--)
          if (node.Entries[t].Key.IsString(TypeIdKey) || node.Entries[t].Key.Kind == ValueKind.String)
            return true;
      }

      // A map leaves out default values, an array only leaves out trailing nils with TrimTrailingNulls (it is too easily shorter by chance)
      Fit needed = node.Kind == ValueKind.Map ? Fit.Partial : Fit.Exact;
      foreach (SchemaType candidate in Candidates())
        if (GetFit(candidate, node) >= needed)
          return true;
      return false;
    }

    private void RecordSignature(SchemaType type, Node array)
    {
      ValueKind[] signature = GetSignature(type);
      for (int t = 0; t < array.Elements.Length && t < signature.Length; t++)
        if (signature[t] == ValueKind.Nil)
          signature[t] = ComparableKind(array.Elements[t]);
    }

    private void RecordSignature(SchemaType type, List<KeyValuePair<Node, Node>> entries)
    {
      ValueKind[] signature = GetSignature(type);
      for (int t = 0; t < entries.Count; t++)
      {
        KeyValuePair<Node, Node> entry = entries[t];
        long index;
        if (entry.Key.TryGetIndex(out index) && index >= 0 && index < signature.Length && signature[index] == ValueKind.Nil)
          signature[index] = ComparableKind(entry.Value);
      }
    }

    /// <summary>
    /// The kind a value is compared by: an object or collection wrapped for its type id by its content (an object of a derived type written as
    /// an array is <c>{ "": typeId, "@": [values] }</c>), an error as unknown (<see cref="ValueKind.Nil"/>). A wrapped primitive stays a map:
    /// a property declared as another type (e.g. object) always wraps its primitive values.
    /// </summary>
    private static ValueKind ComparableKind(Node node)
    {
      if (node.Kind == ValueKind.Error)
        return ValueKind.Nil;
      if (node.Kind != ValueKind.Map || node.Entries.Length == 0 || !node.Entries[0].Key.IsString(TypeIdKey))
        return node.Kind;
      for (int t = 0; t < node.Entries.Length; t++)
        if (node.Entries[t].Key.IsString(ContentKey))
          return node.Entries[t].Value.IsContainer ? node.Entries[t].Value.Kind : ValueKind.Map;
      return ValueKind.Map; // an object with its type id written as a map
    }

    private static bool IsContainerKind(ValueKind kind)
    {
      return kind == ValueKind.Array || kind == ValueKind.Map;
    }

    private ValueKind[] GetSignature(SchemaType type)
    {
      ValueKind[] signature;
      if (!_signatures.TryGetValue(type.Index, out signature))
      {
        signature = new ValueKind[type.Properties.Count];
        _signatures.Add(type.Index, signature);
      }
      return signature;
    }

    private void MarkSeen(SchemaType type)
    {
      type.IsUsed = true;
      if (type.Index < 0 || type.Index >= _seen.Length)
        return;
      _seen[type.Index] = true;
      while (_nextUnseen < _seen.Length && _seen[_nextUnseen])
        _nextUnseen++;
    }

    #endregion

    #region Names and slots

    /// <summary>
    /// The properties of objects of the same type share their slots. A collection's elements share a slot per place of the collection, its type name does not tell the element type (e.g. List`1).
    /// </summary>
    private static string TypeKey(ComplexObject target)
    {
      if (target.Kind == ObjectKind.Object && target.SchemaType != null)
        return string.Concat("#", target.SchemaType.Index.ToString(CultureInfo.InvariantCulture));
      if (target.Kind == ObjectKind.Object && target.Type != null)
        return target.Type;
      return target.Slot ?? string.Empty;
    }

    private static string MemberSlot(ComplexObject target, string name)
    {
      return string.Concat(TypeKey(target), ".", name);
    }

    private static string ElementSlot(ComplexObject target)
    {
      return TypeKey(target) + "[]";
    }

    private static string PropertyName(SchemaType type, int index)
    {
      return type.Properties[index] ?? string.Concat("#", index.ToString(CultureInfo.InvariantCulture));
    }

    private static string Index(int index)
    {
      return string.Concat("[", index.ToString(CultureInfo.InvariantCulture), "]");
    }

    private static string KeyText(Node key)
    {
      if (key.Kind == ValueKind.String)
        return (string)key.Value;
      return PrimitiveObject.FormatValue(key.Value);
    }

    private static bool AllKeysAreIndexes(List<KeyValuePair<Node, Node>> entries)
    {
      for (int t = entries.Count - 1; t >= 0; t--)
        if (entries[t].Key.Kind != ValueKind.Int)
          return false;
      return true;
    }

    private static bool AllKeysAreIndexes(KeyValuePair<Node, Node>[] entries)
    {
      for (int t = entries.Length - 1; t >= 0; t--)
        if (entries[t].Key.Kind != ValueKind.Int)
          return false;
      return true;
    }

    private static bool AllKeysAreStrings(List<KeyValuePair<Node, Node>> entries)
    {
      for (int t = entries.Count - 1; t >= 0; t--)
        if (entries[t].Key.Kind != ValueKind.String)
          return false;
      return true;
    }

    #endregion
  }
}
