using System.Collections.Generic;

namespace LsMsgPackInspector
{
  // What the extension's webview shows, serialized as JSON (camelCase). Ids are indexes in the lists, -1 for none.

  /// <summary>
  /// The result of loading data: the MsgPack tree (within the display limit), the validation issues and the objects.
  /// </summary>
  public class LoadResult
  {
    public int Length { get; set; }

    /// <summary>
    /// The displayed items in the order of the tree (depth first: a map entry is its key with the value below it).
    /// </summary>
    public List<ItemModel> Items { get; set; } = new List<ItemModel>();

    /// <summary>
    /// More items than the display limit, the tree ends with a "Limit of ... reached" node.
    /// </summary>
    public bool Truncated { get; set; }

    public long DisplayLimit { get; set; }

    /// <summary>
    /// The data starts with an indexed schema (or a reference to one), so it was written from objects.
    /// </summary>
    public bool HasSchema { get; set; }

    /// <summary>
    /// How sure the inspector is that the data was written from objects (<see cref="ObjectDebugger.ObjectConfidence"/>: None, Low, Medium, High, Certain).
    /// </summary>
    public string ObjectConfidence { get; set; }

    /// <summary>
    /// Why (empty for None).
    /// </summary>
    public string ObjectReason { get; set; }

    public List<IssueModel> Issues { get; set; } = new List<IssueModel>();

    /// <summary>
    /// Null when the objects are not shown.
    /// </summary>
    public ObjectsModel Objects { get; set; }

    /// <summary>
    /// The data could not be read at all (without "Ignore errors").
    /// </summary>
    public string Error { get; set; }
  }

  public class ItemModel
  {
    public int Id { get; set; }
    public int Parent { get; set; }

    /// <summary>
    /// The first line of the item's ToString(), as the explorer's tree shows it.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// The icon: nil, bool, int, float, bin, str, array, map, ext, error, root or other.
    /// </summary>
    public string Kind { get; set; }

    /// <summary>
    /// "key" or "value" for the items of a map entry.
    /// </summary>
    public string Role { get; set; }

    /// <summary>
    /// Where the item is, the range selected in the hex view.
    /// </summary>
    public long Offset { get; set; }
    public long Length { get; set; }

    /// <summary>
    /// The number of bytes of the type (1, shown in red, 0 for the root and errors with a partial item) and of the length that follows (shown in blue).
    /// </summary>
    public int TypeBytes { get; set; }
    public int LengthBytes { get; set; }

    /// <summary>
    /// A breaking error came before this item (gray in the tree).
    /// </summary>
    public bool Guess { get; set; }

    /// <summary>
    /// Part of the indexed schema (navy in the tree), not of the objects.
    /// </summary>
    public bool Schema { get; set; }

    /// <summary>
    /// What the property grid of the explorer shows.
    /// </summary>
    public List<PropModel> Props { get; set; } = new List<PropModel>();
  }

  public class PropModel
  {
    public PropModel() { }

    public PropModel(string category, string name, string description, string value)
    {
      Category = category;
      Name = name;
      Description = description;
      Value = value;
    }

    public string Category { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Value { get; set; }
  }

  public class IssueModel
  {
    public int Item { get; set; }

    /// <summary>
    /// The bytes another encoding would have saved.
    /// </summary>
    public int Bytes { get; set; }

    /// <summary>
    /// Error, Warning, Comment or ReadAbortError.
    /// </summary>
    public string Severity { get; set; }
    public string Message { get; set; }
  }

  public class ObjectsModel
  {
    public List<ObjectModel> Nodes { get; set; } = new List<ObjectModel>();
    public bool Truncated { get; set; }

    /// <summary>
    /// Per item id the object node it belongs to (-1: look at the parent item, the root object when none has one).
    /// </summary>
    public List<int> ItemObjects { get; set; } = new List<int>();
  }

  public class ObjectModel
  {
    public int Id { get; set; }
    public int Parent { get; set; }
    public string Text { get; set; }

    /// <summary>
    /// The icon, as for items (an entry of a dictionary is "entry", an object "map", a collection "array").
    /// </summary>
    public string Kind { get; set; }
    public string Error { get; set; }

    /// <summary>
    /// The type is inferred from the shape of the data (no type id).
    /// </summary>
    public bool Guess { get; set; }

    /// <summary>
    /// The item selected in the MsgPack tree when the object is selected (-1 when it is not displayed).
    /// </summary>
    public int Item { get; set; }

    /// <summary>
    /// The bytes of the object (from its first to its last item), -1 when unknown.
    /// </summary>
    public long Start { get; set; }
    public long End { get; set; }

    public List<PropModel> Props { get; set; } = new List<PropModel>();
  }

  public class SearchResultModel
  {
    /// <summary>
    /// The matching items that are shown in the tree, in the order of the data.
    /// </summary>
    public List<int> Displayed { get; set; } = new List<int>();

    /// <summary>
    /// All matching items, also those beyond the display limit.
    /// </summary>
    public int Total { get; set; }
  }
}
