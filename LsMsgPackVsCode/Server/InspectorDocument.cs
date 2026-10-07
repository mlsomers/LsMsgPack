using LsMsgPack;
using ObjectDebugger;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace LsMsgPackInspector
{
  public enum ObjectsMode
  {
    /// <summary>
    /// Shown when the data was likely written from objects (<see cref="InspectorSettings.ShowObjectsAt"/>), as the explorer does when data is loaded.
    /// </summary>
    Auto,
    Show,
    Hide
  }

  public class InspectorSettings
  {
    /// <summary>
    /// Keep reading after a breaking error ("Ignore errors" of the explorer).
    /// </summary>
    public bool ContinueOnError { get; set; }
    public EndianAction Endian { get; set; } = EndianAction.SwapIfCurrentSystemIsLittleEndian;
    public long DisplayLimit { get; set; } = 1000;
    public ObjectsMode Objects { get; set; } = ObjectsMode.Auto;

    /// <summary>
    /// <see cref="ObjectsMode.Auto"/> shows the objects from this confidence on (see <see cref="ObjectAssessment"/>).
    /// </summary>
    public ObjectConfidence ShowObjectsAt { get; set; } = ObjectConfidence.High;
  }

  /// <summary>
  /// One payload shown in the extension: what the explorer control (MsgPackExplorer's LsMsgPackExplorer) computes for its tree, hex view, property grid, validation list and objects pane, as a model for the webview.
  /// </summary>
  public class InspectorDocument
  {
    private byte[] _data = new byte[0];
    private MsgPackItem _item;
    private long _displayLimit;
    private long _nodeCount;
    private List<MsgPackItem> _schemaItems = new List<MsgPackItem>();

    /// <summary>
    /// The displayed items (the tree nodes), their index is the id in the model.
    /// </summary>
    private readonly List<MsgPackItem> _items = new List<MsgPackItem>();
    private readonly Dictionary<MsgPackItem, int> _ids = new Dictionary<MsgPackItem, int>(ReferenceEqualityComparer.Instance);
    private readonly List<List<int>> _children = new List<List<int>>();

    public byte[] Data
    {
      get { return _data; }
    }

    /// <summary>
    /// Reads the data (or the data loaded before when null) with the settings.
    /// </summary>
    public LoadResult Load(byte[] data, InspectorSettings settings)
    {
      if (data != null)
        _data = data;
      _items.Clear();
      _ids.Clear();
      _children.Clear();
      _item = null;
      _schemaItems = new List<MsgPackItem>();
      _displayLimit = settings.DisplayLimit <= 0 ? long.MaxValue : settings.DisplayLimit;

      LoadResult result = new LoadResult() { Length = _data.Length, DisplayLimit = _displayLimit };
      if (_data.Length == 0)
        return result;

      MsgPackSettings msgPackSettings = new MsgPackSettings()
      {
        DynamicallyCompact = false,
        PreservePackages = true,
        ContinueProcessingOnBreakingError = settings.ContinueOnError,
        EndianAction = settings.Endian
      };

      try
      {
        MpRoot root = MsgPackItem.UnpackMultiple(_data, msgPackSettings);
        _item = root?.Count == 1 ? root[0] : root;
      }
      catch (Exception ex)
      {
        result.Error = ex.Message;
        return result;
      }
      if (ReferenceEquals(_item, null))
        return result;

      _schemaItems = RootObject.FindSchemaItems(_item);
      result.HasSchema = _schemaItems.Count > 0;
      ObjectAssessment assessment = ObjectAssessment.Assess(_item);
      result.ObjectConfidence = assessment.Confidence.ToString();
      result.ObjectReason = assessment.Reason;

      _nodeCount = 0;
      int rootId = AddItem(result, _item, -1, null, false);
      Traverse(result, rootId, _item);
      result.Truncated = _nodeCount > _displayLimit;

      Validate(result, rootId);

      bool showObjects = settings.Objects == ObjectsMode.Show || settings.Objects == ObjectsMode.Auto && assessment.Confidence >= settings.ShowObjectsAt;
      if (showObjects)
        result.Objects = BuildObjects();
      return result;
    }

    /// <summary>
    /// The items holding the text or the value it converts to (see <see cref="ItemSearch"/>).
    /// </summary>
    public SearchResultModel Search(string text, bool matchCase)
    {
      SearchResultModel result = new SearchResultModel();
      if (ReferenceEquals(_item, null) || string.IsNullOrEmpty(text))
        return result;

      List<MsgPackItem> matches = new List<MsgPackItem>();
      new ItemSearch(text, matchCase).FindAll(_item, matches);
      result.Total = matches.Count;
      foreach (MsgPackItem match in matches)
      {
        int id;
        if (_ids.TryGetValue(match, out id))
          result.Displayed.Add(id);
      }
      return result;
    }

    #region The MsgPack tree

    private int AddItem(LoadResult result, MsgPackItem item, int parent, string role, bool schema)
    {
      int id = _items.Count;
      _items.Add(item);
      _children.Add(new List<int>());
      if (!ReferenceEquals(item, null))
        _ids[item] = id;
      if (parent >= 0)
        _children[parent].Add(id);

      ItemModel model = new ItemModel()
      {
        Id = id,
        Parent = parent,
        Role = role,
        Kind = GetKind(item),
        Schema = schema
      };

      if (ReferenceEquals(item, null))
      {
        model.Text = "NULL";
        model.Guess = true;
      }
      else
      {
        model.Text = FirstLine(item.ToString());
        model.Guess = item.IsBestGuess;
        model.Offset = item.StoredOffset;
        model.Length = item.StoredLength;
        MpError error = item as MpError;
        model.TypeBytes = item is MpRoot || error != null && !ReferenceEquals(error.PartialItem, null) ? 0 : 1;
        model.LengthBytes = item is MsgPackVarLen ? GetLengthBytes(item.TypeId) : 0;
        model.Props = GetProperties(item);
      }
      result.Items.Add(model);
      return id;
    }

    /// <summary>
    /// The nodes of the explorer's tree (LsMsgPackExplorer.Traverse), up to the display limit.
    /// </summary>
    private void Traverse(LoadResult result, int id, MsgPackItem item)
    {
      _nodeCount++;
      if (_nodeCount > _displayLimit)
        return;
      if (ReferenceEquals(item, null))
        return;

      // The schema (or schema reference) is a direct child of the root, everything in it belongs to it
      bool isTreeRoot = id == 0;
      bool schema = result.Items[id].Schema;

      if (item is MpRoot root)
      {
        for (int t = 0; t < root.Count; t++)
        {
          int child = AddItem(result, root[t], id, null, schema || isTreeRoot && IsSchemaItem(root[t]));
          Traverse(result, child, root[t]);
          if (_nodeCount > _displayLimit)
            return;
        }
      }
      else if (item is MpArray array)
      {
        MsgPackItem[] elements = array.PackedValues;
        for (int t = 0; t < elements.Length; t++)
        {
          int child = AddItem(result, elements[t], id, null, schema || isTreeRoot && IsSchemaItem(elements[t]));
          Traverse(result, child, elements[t]);
          if (_nodeCount > _displayLimit)
            return;
        }
      }
      else if (item is MpMap map)
      {
        KeyValuePair<MsgPackItem, MsgPackItem>[] entries = map.PackedValues;
        for (int t = 0; t < entries.Length; t++)
        {
          int key = AddItem(result, entries[t].Key, id, "key", schema);
          Traverse(result, key, entries[t].Key);
          if (_nodeCount > _displayLimit)
            return;
          int value = AddItem(result, entries[t].Value, key, "value", schema);
          Traverse(result, value, entries[t].Value);
          if (_nodeCount > _displayLimit)
            return;
        }
      }
      else if (item is MpError error && !ReferenceEquals(error.PartialItem, null))
      {
        int child = AddItem(result, error.PartialItem, id, null, schema);
        Traverse(result, child, error.PartialItem);
      }
    }

    private bool IsSchemaItem(MsgPackItem item)
    {
      foreach (MsgPackItem schemaItem in _schemaItems)
        if (ReferenceEquals(schemaItem, item))
          return true;
      return false;
    }

    private static string FirstLine(string text)
    {
      if (text is null)
        return string.Empty;
      int pos = text.IndexOfAny(new char[] { '\r', '\n' });
      return pos >= 0 ? text.Substring(0, pos) : text;
    }

    internal static string GetKind(MsgPackItem item)
    {
      if (ReferenceEquals(item, null)) return "nil";
      Type typ = item.GetType();
      if (typ == typeof(MpNull)) return "nil";
      if (typ == typeof(MpBool)) return "bool";
      if (typ == typeof(MpInt)) return "int";
      if (typ == typeof(MpFloat)) return "float";
      if (typ == typeof(MpBin)) return "bin";
      if (typ == typeof(MpString)) return "str";
      if (typ == typeof(MpArray)) return "array";
      if (typ == typeof(MpMap)) return "map";
      if (typ == typeof(MpError)) return "error";
      if (typ == typeof(MpRoot)) return "root";
      if (item is MpExt) return "ext"; // also timestamps and custom extensions
      return "other";
    }

    /// <summary>
    /// The bytes holding the length after the type byte (blue in the hex view).
    /// </summary>
    private static int GetLengthBytes(MsgPackTypeId typeId)
    {
      switch (typeId)
      {
        case MsgPackTypeId.MpBin8: return 1;
        case MsgPackTypeId.MpBin16: return 2;
        case MsgPackTypeId.MpBin32: return 4;
        case MsgPackTypeId.MpStr8: return 1;
        case MsgPackTypeId.MpStr16: return 2;
        case MsgPackTypeId.MpStr32: return 4;
        case MsgPackTypeId.MpMap16: return 2;
        case MsgPackTypeId.MpMap32: return 4;
        case MsgPackTypeId.MpArray16: return 2;
        case MsgPackTypeId.MpArray32: return 4;
        case MsgPackTypeId.MpExt8: return 1;
        case MsgPackTypeId.MpExt16: return 2;
        case MsgPackTypeId.MpExt32: return 4;
        default: return 0;
      }
    }

    /// <summary>
    /// The browsable properties of the item, as the property grid of the explorer shows them.
    /// </summary>
    private static List<PropModel> GetProperties(MsgPackItem item)
    {
      List<PropModel> props = new List<PropModel>();
      foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(item, new Attribute[] { BrowsableAttribute.Yes }))
      {
        string text;
        try
        {
          text = FormatProperty(descriptor, descriptor.GetValue(item));
        }
        catch (Exception ex)
        {
          text = string.Concat("(", ex.Message, ")");
        }
        props.Add(new PropModel(descriptor.Category, descriptor.DisplayName, descriptor.Description, text));
      }
      return props;
    }

    private static string FormatProperty(PropertyDescriptor descriptor, object value)
    {
      const int maxText = 10000;
      if (value is null)
        return "(null)";
      if (value is byte[] || value is MpExt)
        return PrimitiveObject.FormatValue(value);
      if (value is string text)
        return text.Length > maxText ? string.Concat(text.Substring(0, maxText), "...") : text;
      if (value is Array array)
        return string.Concat(value.GetType().GetElementType().Name, "[] (", array.Length.ToString(CultureInfo.InvariantCulture), array.Length == 1 ? " item)" : " items)");
      if (value is MsgPackItem item)
        return FirstLine(item.ToString());
      if (value is ICollection collection)
        return string.Concat(value.GetType().Name, " (", collection.Count.ToString(CultureInfo.InvariantCulture), " items)");

      TypeConverter converter = descriptor.Converter;
      if (converter != null && converter.CanConvertTo(typeof(string)))
      {
        string converted = converter.ConvertToString(null, CultureInfo.CurrentCulture, value);
        if (converted != null)
          return converted;
      }
      return PrimitiveObject.FormatValue(value);
    }

    #endregion

    #region Validation

    /// <summary>
    /// The issues of the displayed items, in the order of the explorer's list (an item after the items in it).
    /// </summary>
    private void Validate(LoadResult result, int id)
    {
      foreach (int child in _children[id])
        Validate(result, child);

      MsgPackItem item = _items[id];
      if (ReferenceEquals(item, null))
        return;

      MsgPackValidation.ValidationItem[] issues = MsgPackValidation.ValidateItem(item, _displayLimit);
      for (int t = issues.Length - 1; t >= 0; t--)
      {
        if (issues.Length - t > _displayLimit)
          return;
        result.Issues.Add(new IssueModel()
        {
          Item = id,
          Bytes = issues[t].WaistedBytes,
          Severity = issues[t].Severity.ToString(),
          Message = issues[t].Message
        });
      }
    }

    #endregion

    #region Objects

    private ObjectsModel BuildObjects()
    {
      ObjectsModel model = new ObjectsModel();
      RootObject root = new RootObject();
      try
      {
        root.Reconstruct(_item);
      }
      catch (Exception ex)
      {
        // Errors are normally kept with the value they occurred in, this keeps what was reconstructed anyway
        root.AddError(ex.Message);
      }

      Dictionary<ComplexObject, int> objectIds = new Dictionary<ComplexObject, int>(ReferenceEqualityComparer.Instance);
      int rootId = AddObject(model, root, -1, objectIds);
      int count = 0;
      AddObjects(model, root, rootId, ref count, objectIds);
      model.Truncated = count >= _displayLimit;

      for (int t = 0; t < _items.Count; t++)
        model.ItemObjects.Add(-1);
      RegisterObjectItems(model, root, objectIds);
      return model;
    }

    private void AddObjects(ObjectsModel model, ComplexObject obj, int id, ref int count, Dictionary<ComplexObject, int> objectIds)
    {
      foreach (ComplexObject child in obj.Children)
      {
        if (count >= _displayLimit)
          return;
        count++;
        int childId = AddObject(model, child, id, objectIds);
        AddObjects(model, child, childId, ref count, objectIds);
      }
    }

    private int AddObject(ObjectsModel model, ComplexObject obj, int parent, Dictionary<ComplexObject, int> objectIds)
    {
      bool isRoot = obj is RootObject && obj.Parent is null;
      string text = obj.ToString();
      if (isRoot)
        text = string.Concat("Root : ", text);

      long start = -1;
      long end = -1;
      foreach (MsgPackItem item in new[] { obj.FirstItemRef, obj.LastItemRef })
      {
        if (ReferenceEquals(item, null))
          continue;
        if (start < 0 || item.StoredOffset < start)
          start = item.StoredOffset;
        end = Math.Max(end, item.StoredOffset + item.StoredLength);
      }

      ObjectModel objectModel = new ObjectModel()
      {
        Id = model.Nodes.Count,
        Parent = parent,
        Text = text,
        Kind = GetObjectKind(obj, isRoot),
        Error = obj.Error,
        Guess = obj.Error is null && obj.TypeIsGuess,
        Item = GetDisplayedId(obj.FirstItemRef) ?? GetDisplayedId(obj.LastItemRef) ?? -1,
        Start = start,
        End = end,
        Props = GetObjectProperties(obj)
      };
      model.Nodes.Add(objectModel);
      objectIds[obj] = objectModel.Id;
      return objectModel.Id;
    }

    private int? GetDisplayedId(MsgPackItem item)
    {
      int id;
      if (!ReferenceEquals(item, null) && _ids.TryGetValue(item, out id))
        return id;
      return null;
    }

    private static string GetObjectKind(ComplexObject obj, bool isRoot)
    {
      if (obj.Error != null) return "error";
      if (isRoot) return "root";
      switch (obj.Kind)
      {
        case ObjectKind.Collection:
        case ObjectKind.Sequence: return "array";
        case ObjectKind.Entry: return "entry";
        case ObjectKind.Value: return GetValueKind(obj.ValueKind);
        default: return "map";
      }
    }

    private static string GetValueKind(ValueKind kind)
    {
      switch (kind)
      {
        case ValueKind.Nil: return "nil";
        case ValueKind.Bool: return "bool";
        case ValueKind.Int: return "int";
        case ValueKind.Float: return "float";
        case ValueKind.Bin: return "bin";
        case ValueKind.String: return "str";
        case ValueKind.Array: return "array";
        case ValueKind.Map: return "map";
        case ValueKind.Error: return "error";
        default: return "ext"; // extensions (timestamps, decimals)
      }
    }

    /// <summary>
    /// Inner objects are registered after their parent, so an item that belongs to both (the key of an entry and the key itself) selects the innermost.
    /// </summary>
    private void RegisterObjectItems(ObjectsModel model, ComplexObject obj, Dictionary<ComplexObject, int> objectIds)
    {
      int node;
      if (!objectIds.TryGetValue(obj, out node))
        return; // beyond the display limit, its items select the parent that holds them

      RegisterItem(model, obj.FirstItemRef, node);
      RegisterItem(model, obj.LastItemRef, node);
      foreach (PrimitiveObject member in obj.Members)
      {
        ComplexObject complex = member as ComplexObject;
        if (complex != null)
        {
          RegisterObjectItems(model, complex, objectIds);
        }
        else
        {
          RegisterItem(model, member.FirstItemRef, node);
          RegisterItem(model, member.LastItemRef, node);
        }
      }
    }

    private void RegisterItem(ObjectsModel model, MsgPackItem item, int node)
    {
      int? id = GetDisplayedId(item);
      if (id.HasValue)
        model.ItemObjects[id.Value] = node;
    }

    /// <summary>
    /// The primitive members of an object (the objects and collections are in the tree), as the property grid of the explorer's objects pane shows them.
    /// </summary>
    private static List<PropModel> GetObjectProperties(ComplexObject obj)
    {
      List<PropModel> props = new List<PropModel>();

      const string objectCategory = "Object";
      props.Add(new PropModel(objectCategory, "Name", "The property name, [index] for an element of a collection.", string.IsNullOrEmpty(obj.Name) ? "(root)" : obj.Name));
      props.Add(new PropModel(objectCategory, "Path", "Where the object is in the tree.", obj.Path));
      props.Add(new PropModel(objectCategory, "Type", "The type name when the data tells it (a type id, or inferred from the indexed schema).", obj.TypeIsGuess ? string.Concat(obj.TypeText, " (inferred)") : obj.TypeText));
      props.Add(new PropModel(objectCategory, "Kind", "An object, collection, dictionary...", obj.Kind.ToString()));
      props.Add(new PropModel(objectCategory, "Members", "The number of properties, elements or entries.", obj.Members.Count.ToString(CultureInfo.InvariantCulture)));
      if (obj.FirstItemRef != null)
        props.Add(new PropModel(objectCategory, "Offset", "The offset of the first byte (of the key when it is a property of a map).", obj.FirstItemRef.StoredOffset.ToString(CultureInfo.InvariantCulture)));
      if (obj.Error != null)
        props.Add(new PropModel(objectCategory, "Error", obj.Error, obj.Error));

      RootObject root = obj as RootObject;
      if (root != null)
        AddSchema(root, props);

      foreach (PrimitiveObject member in obj.Members)
      {
        if (member is ComplexObject)
          continue;
        props.Add(new PropModel(GetCategory(obj, member), member.Name, Describe(member), member.ValueText));
      }
      return props;
    }

    private static void AddSchema(RootObject root, List<PropModel> props)
    {
      for (int t = 0; t < root.Warnings.Count; t++)
        props.Add(new PropModel("Warnings", string.Concat("Warning ", (t + 1).ToString(CultureInfo.InvariantCulture)), root.Warnings[t], root.Warnings[t]));

      if (root.Schema is null)
        return;

      const string schemaCategory = "Schema";
      props.Add(new PropModel(schemaCategory, "Schema", "The indexed schema: type ids and property ids in the data are indexes of these names.", root.Schema.ToString()));
      foreach (SchemaType type in root.Schema.Types)
      {
        string text = string.Join(", ", type.Properties);
        if (!type.IsUsed)
          text = string.Concat(text, " (no value found)");
        props.Add(new PropModel(schemaCategory, string.Concat("#", type.Index.ToString(CultureInfo.InvariantCulture), " ", type.Name), "The properties of the type, a property id is the index in this list.", text));
      }
    }

    private static string GetCategory(ComplexObject obj, PrimitiveObject member)
    {
      switch (obj.Kind)
      {
        case ObjectKind.Dictionary: return "Entries";
        case ObjectKind.Value: return "Value";
        case ObjectKind.Collection:
        case ObjectKind.Sequence:
          return member.Name != null && member.Name.StartsWith("[", StringComparison.Ordinal) ? "Elements" : "Properties";
        default: return "Properties";
      }
    }

    private static string Describe(PrimitiveObject member)
    {
      StringBuilder sb = new StringBuilder(member.TypeText);
      MsgPackItem valueItem = member.LastItemRef;
      if (valueItem != null)
        sb.Append(", MsgPack ").Append(MsgPackItem.GetOfficialTypeName(valueItem.TypeId)).Append(" at offset ").Append(valueItem.StoredOffset);
      if (member.Error != null)
        sb.Append(Environment.NewLine).Append(member.Error);
      return sb.ToString();
    }

    #endregion
  }
}
