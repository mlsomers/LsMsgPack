using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ObjectDebugger
{
  public enum ObjectKind
  {
    /// <summary>An object with properties (a map, or an array of its values with <c>ObjectLayout.Array</c>)</summary>
    Object,
    /// <summary>The elements of a collection (an array), with its properties when it was written with them</summary>
    Collection,
    /// <summary>The entries of a dictionary (a map whose keys are not property ids)</summary>
    Dictionary,
    /// <summary>An entry of a dictionary with a key that is not a primitive value (holds the members "Key" and "Value")</summary>
    Entry,
    /// <summary>A root that holds a single primitive value</summary>
    Value,
    /// <summary>A root holding several payloads that follow each other (each one a <see cref="RootObject"/>)</summary>
    Sequence
  }

  /// <summary>
  /// An object, collection or dictionary of the reconstructed object tree.
  /// </summary>
  public class ComplexObject : PrimitiveObject
  {
    public ObjectKind Kind { get; set; }

    /// <summary>
    /// The type in the indexed schema, when the data has one and the type is known.
    /// </summary>
    public SchemaType SchemaType { get; set; }

    /// <summary>
    /// The properties, elements or entries in the order of the data.
    /// </summary>
    public List<PrimitiveObject> Members { get; } = new List<PrimitiveObject>();

    /// <summary>
    /// The objects and collections in <see cref="Members"/> (the children in a tree view).
    /// </summary>
    public IEnumerable<ComplexObject> Children
    {
      get { return Members.OfType<ComplexObject>(); }
    }

    /// <summary>
    /// Shown as children in the tree view.
    /// </summary>
    public IEnumerable<ComplexObject> ComplexProperties
    {
      get { return Children.Where(c => !c.IsCollection); }
    }

    /// <summary>
    /// Shown as children in the tree view.
    /// </summary>
    public IEnumerable<ComplexObject> CollectionProperties
    {
      get { return Children.Where(c => c.IsCollection); }
    }

    /// <summary>
    /// The members that are neither objects nor collections (shown in the property grid).
    /// </summary>
    public IEnumerable<PrimitiveObject> PrimitiveProperties
    {
      get { return Members.Where(m => !(m is ComplexObject)); }
    }

    public bool IsCollection
    {
      get { return Kind == ObjectKind.Collection || Kind == ObjectKind.Dictionary || Kind == ObjectKind.Sequence; }
    }

    /// <summary>
    /// Identifies where the value is in the object model (the declared type of a property, or of the elements of a collection), so values in the same place get the same inferred type.
    /// </summary>
    internal string Slot { get; set; }

    public override string ValueText
    {
      get
      {
        switch (Kind)
        {
          case ObjectKind.Collection:
          case ObjectKind.Dictionary:
          case ObjectKind.Sequence:
            return string.Concat("Count = ", Members.Count.ToString(CultureInfo.InvariantCulture));
          default:
            return TypeText;
        }
      }
    }

    public override string TypeText
    {
      get
      {
        if (Type != null)
          return Type;
        switch (Kind)
        {
          case ObjectKind.Collection: return "collection";
          case ObjectKind.Dictionary: return "dictionary";
          case ObjectKind.Entry: return "entry";
          case ObjectKind.Sequence: return "sequence";
          case ObjectKind.Value: return base.TypeText;
          default: return "object";
        }
      }
    }

    public override string ToString()
    {
      string text = string.IsNullOrEmpty(Name) ? TypeText : string.Concat(Name, " : ", TypeText);
      if (IsCollection)
        text = string.Concat(text, " (", Members.Count.ToString(CultureInfo.InvariantCulture), ")");
      return text;
    }
  }
}
