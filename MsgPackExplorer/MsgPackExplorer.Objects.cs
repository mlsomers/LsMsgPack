using LsMsgPack;
using ObjectDebugger;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MsgPackExplorer {
  // In this partial class all the complex object parsing and displaying code
  partial class LsMsgPackExplorer {

    private bool _objectsVisible;

    /// <summary>
    /// Reconstructs the objects of the data (types and property names, as far as the data tells them) and shows them in the objects pane, only while it is visible.
    /// </summary>
    public void RefreshObjects() {
      treeViewObjects.BeginUpdate();
      try {
        treeViewObjects.Nodes.Clear();
        propertyGridObjects.SelectedObject = null;
        if (!_objectsVisible || ReferenceEquals(item, null))
          return;

        Cursor = Cursors.WaitCursor;
        RootObject root = new RootObject();
        try {
          root.Reconstruct(item);
        } catch (Exception ex) {
          // Errors are normally kept with the value they occurred in, this keeps what was reconstructed anyway
          root.AddError(ex.Message);
        }

        TreeNode rootNode = CreateObjectNode(root);
        int count = 0;
        AddObjectNodes(rootNode, root, ref count);
        if (count >= _displayLimit)
          rootNode.Nodes.Add(string.Concat("Limit of ", _displayLimit, " displayed items reached..."));

        treeViewObjects.ShowNodeToolTips = true;
        treeViewObjects.Nodes.Add(rootNode);
        treeViewObjects.ExpandAll();
        treeViewObjects.SelectedNode = rootNode;
      } finally {
        treeViewObjects.EndUpdate();
        Cursor = Cursors.Default;
      }
    }

    private void AddObjectNodes(TreeNode node, ComplexObject obj, ref int count) {
      foreach (ComplexObject child in obj.Children) {
        if (count >= _displayLimit)
          return;
        count++;
        TreeNode childNode = CreateObjectNode(child);
        node.Nodes.Add(childNode);
        AddObjectNodes(childNode, child, ref count);
      }
    }

    private TreeNode CreateObjectNode(ComplexObject obj) {
      int imgIdx = GetObjectIcon(obj);
      string text = obj.ToString();
      if (obj is RootObject && obj.Parent is null)
        text = string.Concat("Root : ", text);

      TreeNode node = new TreeNode(text, imgIdx, imgIdx) { Tag = obj };
      obj.Tag = node;
      if (obj.Error != null) {
        node.ForeColor = Color.Red;
        node.ToolTipText = obj.Error;
      } else if (obj.TypeIsGuess) {
        node.ForeColor = Color.DimGray;
        node.ToolTipText = "The type is inferred from the shape of the data (no type id).";
      }
      return node;
    }

    private static int GetObjectIcon(ComplexObject obj) {
      if (obj.Error != null) return 11;
      if (obj is RootObject && obj.Parent is null) return 12;
      switch (obj.Kind) {
        case ObjectKind.Collection:
        case ObjectKind.Sequence: return 6;
        case ObjectKind.Entry: return 8;
        case ObjectKind.Value: return GetValueIcon(obj.ValueKind);
        default: return 7;
      }
    }

    private static int GetValueIcon(ValueKind kind) {
      switch (kind) {
        case ValueKind.Nil: return 0;
        case ValueKind.Bool: return 1;
        case ValueKind.Int: return 2;
        case ValueKind.Float: return 3;
        case ValueKind.Bin: return 4;
        case ValueKind.String: return 5;
        case ValueKind.Array: return 6;
        case ValueKind.Map: return 7;
        case ValueKind.Error: return 11;
        default: return 10; // extensions (timestamps, decimals)
      }
    }

    private void treeViewObjects_AfterSelect(object sender, TreeViewEventArgs e) {
      ComplexObject obj = e.Node?.Tag as ComplexObject;
      propertyGridObjects.SelectedObject = obj is null ? null : new ObjectPropertiesView(obj);
    }

    /// <summary>
    /// Shows the primitive members of an object (not the objects and collections, they are in the tree) as read-only properties of the property grid.
    /// </summary>
    private sealed class ObjectPropertiesView : ICustomTypeDescriptor {
      private readonly ComplexObject _obj;
      private readonly PropertyDescriptorCollection _properties;

      public ObjectPropertiesView(ComplexObject obj) {
        _obj = obj;
        List<PropertyDescriptor> props = new List<PropertyDescriptor>();

        const string objectCategory = "Object";
        props.Add(new ValueDescriptor("o.name", "Name", objectCategory, "The property name, [index] for an element of a collection.", string.IsNullOrEmpty(obj.Name) ? "(root)" : obj.Name));
        props.Add(new ValueDescriptor("o.path", "Path", objectCategory, "Where the object is in the tree.", obj.Path));
        props.Add(new ValueDescriptor("o.type", "Type", objectCategory, "The type name when the data tells it (a type id, or inferred from the indexed schema).", obj.TypeIsGuess ? string.Concat(obj.TypeText, " (inferred)") : obj.TypeText));
        props.Add(new ValueDescriptor("o.kind", "Kind", objectCategory, "An object, collection, dictionary...", obj.Kind.ToString()));
        props.Add(new ValueDescriptor("o.count", "Members", objectCategory, "The number of properties, elements or entries.", obj.Members.Count));
        if (obj.FirstItemRef != null)
          props.Add(new ValueDescriptor("o.offset", "Offset", objectCategory, "The offset of the first byte (of the key when it is a property of a map).", obj.FirstItemRef.StoredOffset));
        if (obj.Error != null)
          props.Add(new ValueDescriptor("o.error", "Error", objectCategory, obj.Error, obj.Error));

        RootObject root = obj as RootObject;
        if (root != null)
          AddSchema(root, props);

        for (int t = 0; t < obj.Members.Count; t++) {
          PrimitiveObject member = obj.Members[t];
          if (member is ComplexObject)
            continue;
          props.Add(new ValueDescriptor(string.Concat("m", t.ToString(CultureInfo.InvariantCulture)), member.Name, GetCategory(obj, member), Describe(member), GetDisplayValue(member)));
        }

        _properties = new PropertyDescriptorCollection(props.ToArray(), true);
      }

      private static void AddSchema(RootObject root, List<PropertyDescriptor> props) {
        for (int t = 0; t < root.Warnings.Count; t++)
          props.Add(new ValueDescriptor(string.Concat("w", t.ToString(CultureInfo.InvariantCulture)), string.Concat("Warning ", (t + 1).ToString(CultureInfo.InvariantCulture)), "Warnings", root.Warnings[t], root.Warnings[t]));

        if (root.Schema is null)
          return;

        const string schemaCategory = "Schema";
        props.Add(new ValueDescriptor("s", "Schema", schemaCategory, "The indexed schema: type ids and property ids in the data are indexes of these names.", root.Schema.ToString()));
        foreach (SchemaType type in root.Schema.Types) {
          string text = string.Join(", ", type.Properties);
          if (!type.IsUsed)
            text = string.Concat(text, " (no value found)");
          props.Add(new ValueDescriptor(string.Concat("s", type.Index.ToString(CultureInfo.InvariantCulture)), string.Concat("#", type.Index.ToString(CultureInfo.InvariantCulture), " ", type.Name), schemaCategory, "The properties of the type, a property id is the index in this list.", text));
        }
      }

      private static string GetCategory(ComplexObject obj, PrimitiveObject member) {
        switch (obj.Kind) {
          case ObjectKind.Dictionary: return "Entries";
          case ObjectKind.Value: return "Value";
          case ObjectKind.Collection:
          case ObjectKind.Sequence:
            return member.Name != null && member.Name.StartsWith("[", StringComparison.Ordinal) ? "Elements" : "Properties";
          default: return "Properties";
        }
      }

      private static string Describe(PrimitiveObject member) {
        StringBuilder sb = new StringBuilder(member.TypeText);
        MsgPackItem valueItem = member.LastItemRef;
        if (valueItem != null)
          sb.Append(", MsgPack ").Append(MsgPackItem.GetOfficialTypeName(valueItem.TypeId)).Append(" at offset ").Append(valueItem.StoredOffset);
        if (member.Error != null)
          sb.Append(Environment.NewLine).Append(member.Error);
        return sb.ToString();
      }

      /// <summary>
      /// Simple values as they are (the grid shows them in their own way), anything else as text.
      /// </summary>
      private static object GetDisplayValue(PrimitiveObject member) {
        object value = member.Value;
        if (value is bool || value is string || value is DateTime || value is decimal || value is float || value is double
          || value is sbyte || value is byte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong)
          return value;
        return member.ValueText;
      }

      public AttributeCollection GetAttributes() { return AttributeCollection.Empty; }
      public string GetClassName() { return _obj.TypeText; }
      public string GetComponentName() { return _obj.Name; }
      public TypeConverter GetConverter() { return new TypeConverter(); }
      public EventDescriptor GetDefaultEvent() { return null; }
      public PropertyDescriptor GetDefaultProperty() { return null; }
      public object GetEditor(Type editorBaseType) { return null; }
      public EventDescriptorCollection GetEvents() { return EventDescriptorCollection.Empty; }
      public EventDescriptorCollection GetEvents(Attribute[] attributes) { return EventDescriptorCollection.Empty; }
      public PropertyDescriptorCollection GetProperties() { return _properties; }
      public PropertyDescriptorCollection GetProperties(Attribute[] attributes) { return _properties; }
      public object GetPropertyOwner(PropertyDescriptor pd) { return this; }

      public override string ToString() { return _obj.ToString(); }
    }

    /// <summary>
    /// A read-only value in the property grid. The names are unique (a dictionary read from damaged data may have the same key twice), the display name is the member name.
    /// </summary>
    private sealed class ValueDescriptor : PropertyDescriptor {
      private readonly string _displayName;
      private readonly string _category;
      private readonly string _description;
      private readonly object _value;

      public ValueDescriptor(string name, string displayName, string category, string description, object value) : base(name, null) {
        _displayName = displayName ?? string.Empty;
        _category = category;
        _description = description;
        _value = value;
      }

      public override string DisplayName { get { return _displayName; } }
      public override string Category { get { return _category; } }
      public override string Description { get { return _description; } }
      public override Type ComponentType { get { return typeof(ObjectPropertiesView); } }
      public override bool IsReadOnly { get { return true; } }
      public override Type PropertyType { get { return _value is null ? typeof(string) : _value.GetType(); } }
      public override bool CanResetValue(object component) { return false; }
      public override object GetValue(object component) { return _value; }
      public override void ResetValue(object component) { }
      public override void SetValue(object component, object value) { }
      public override bool ShouldSerializeValue(object component) { return false; }
    }
  }
}
