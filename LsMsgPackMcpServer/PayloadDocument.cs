using LsMsgPack;
using ObjectDebugger;
using System;
using System.Collections.Generic;

namespace LsMsgPackMcp
{
  public class DecodeOptions
  {
    public EndianAction Endian { get; set; } = EndianAction.SwapIfCurrentSystemIsLittleEndian;

    /// <summary>
    /// Keep reading after a breaking error (the rest is a best guess). On by default: an agent debugging broken data wants to see as much as can be read.
    /// </summary>
    public bool ContinueOnError { get; set; } = true;

    /// <summary>
    /// The schemas the data may refer to (<c>WriteSchemaReference</c>), optional.
    /// </summary>
    public SchemaStore Schemas { get; set; }
  }

  /// <summary>
  /// An item of the MsgPack tree with where it is in the tree.
  /// </summary>
  public sealed class ItemNode
  {
    public MsgPackItem Item { get; set; }
    public ItemNode Parent { get; set; }
    public int Depth { get; set; }

    /// <summary>
    /// "key" or "value" for the items of a map entry, "[n]" for the elements of an array or the items of the root, null for the root.
    /// </summary>
    public string Role { get; set; }

    /// <summary>
    /// Part of the indexed schema (or the reference to it), not of the objects.
    /// </summary>
    public bool IsSchema { get; set; }

    public List<ItemNode> Children { get; } = new List<ItemNode>();

    /// <summary>
    /// An item that could not be read and its error cover the item from its start to where the reading stopped: the error's own offset is
    /// the last byte read (the offset in its exception is where the reader went on after it), the item has no length.
    /// </summary>
    public long Offset
    {
      get
      {
        MpError error = CutOffBy;
        return error != null ? error.PartialItem.StoredOffset : Item is null ? -1 : Item.StoredOffset;
      }
    }

    public long Length
    {
      get
      {
        MpError error = CutOffBy;
        if (error != null)
          return Math.Max(error.StoredOffset + error.StoredLength, error.PartialItem.StoredOffset + error.PartialItem.StoredLength) - error.PartialItem.StoredOffset;
        return Item is null ? 0 : Item.StoredLength;
      }
    }

    /// <summary>
    /// The error of an item that could not be read, when this is that error or that item (not for a container holding an error).
    /// </summary>
    private MpError CutOffBy
    {
      get
      {
        if (Item is MpError error && !error.IsInNestedItem && !(error.PartialItem is null) && error.PartialItem.StoredOffset >= 0)
          return error;
        if (Parent?.Item is MpError around && !around.IsInNestedItem && ReferenceEquals(around.PartialItem, Item) && Item.StoredOffset >= 0)
          return around;
        return null;
      }
    }
  }

  public sealed class Issue
  {
    public ItemNode Node { get; set; }
    public MsgPackValidation.ValidationSeverity Severity { get; set; }
    public int WastedBytes { get; set; }
    public string Message { get; set; }

    public bool IsError
    {
      get { return Severity == MsgPackValidation.ValidationSeverity.Error || Severity == MsgPackValidation.ValidationSeverity.ReadAbortError; }
    }
  }

  /// <summary>
  /// A decoded payload: the item tree (KEEPTRACK: offsets, errors as <see cref="MpError"/> items), the reconstructed objects and the validation issues.
  /// Kept by the MCP server under <see cref="Id"/>, so follow-up questions (search, explain an offset, another part) do not read the bytes again.
  /// </summary>
  public sealed class PayloadDocument
  {
    private readonly Dictionary<MsgPackItem, ItemNode> _nodes = new Dictionary<MsgPackItem, ItemNode>(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MsgPackItem, PrimitiveObject> _objects = new Dictionary<MsgPackItem, PrimitiveObject>(ReferenceEqualityComparer.Instance);
    private MsgPackSettings _settings;

    public PayloadDocument(byte[] data, string source, DecodeOptions options)
    {
      Data = data ?? new byte[0];
      Source = source;
      Options = options ?? new DecodeOptions();
      Decode();
    }

    public string Id { get; set; }

    /// <summary>
    /// Where the bytes came from (a file, an expression in the debugger...).
    /// </summary>
    public string Source { get; }

    public byte[] Data { get; }
    public DecodeOptions Options { get; }

    /// <summary>
    /// Shown with the result, e.g. the debugger read only the first bytes.
    /// </summary>
    public List<string> Notes { get; } = new List<string>();

    /// <summary>
    /// The root of the item tree: an <see cref="MpRoot"/> when the data holds several items (e.g. a schema followed by the body), otherwise the item. Null when nothing could be read.
    /// </summary>
    public ItemNode Root { get; private set; }

    /// <summary>
    /// All items depth first (a map entry is its key followed by its value).
    /// </summary>
    public List<ItemNode> Items { get; } = new List<ItemNode>();

    public RootObject Objects { get; private set; }

    public List<Issue> Issues { get; } = new List<Issue>();

    /// <summary>
    /// Where the first error is (an <see cref="MpError"/> without a partial item), -1 when there is none. Values after it were read on at the next byte (a guess).
    /// </summary>
    public long FirstErrorOffset { get; private set; } = -1;

    /// <summary>
    /// The data could not be read at all (only without <see cref="DecodeOptions.ContinueOnError"/>).
    /// </summary>
    public string ReadError { get; private set; }

    public ItemNode GetNode(MsgPackItem item)
    {
      ItemNode node;
      return !(item is null) && _nodes.TryGetValue(item, out node) ? node : null;
    }

    /// <summary>
    /// The innermost object member whose value (or key) is this item or one of the items around it.
    /// </summary>
    public PrimitiveObject GetObject(ItemNode node)
    {
      for (ItemNode n = node; n != null; n = n.Parent)
      {
        PrimitiveObject obj;
        if (!(n.Item is null) && _objects.TryGetValue(n.Item, out obj))
          return obj;
        // The objects of an item that could not be read are made from its PartialItem
        if (n.Item is MpError error && !(error.PartialItem is null) && _objects.TryGetValue(error.PartialItem, out obj))
          return obj;
      }
      return null;
    }

    /// <summary>
    /// The innermost item holding the byte at the offset, null when the offset is outside the items (e.g. trailing bytes after an error).
    /// </summary>
    public ItemNode FindItemAt(long offset)
    {
      ItemNode found = null;
      ItemNode current = Root;
      while (current != null && Contains(current, offset))
      {
        found = current;
        ItemNode next = null;
        foreach (ItemNode child in current.Children)
        {
          if (Contains(child, offset))
          {
            next = child;
            break;
          }
        }
        current = next;
      }
      return found;
    }

    private static bool Contains(ItemNode node, long offset)
    {
      return !(node.Item is null) && offset >= node.Offset && offset < node.Offset + Math.Max(1, node.Length);
    }

    /// <summary>
    /// The encoding as the type byte in the data says. <see cref="MsgPackItem.TypeId"/> is computed from the value, so it names the smallest
    /// encoding (a str 16 holding 3 bytes became a fixstr) and the encoding of what could be read of an item that ends early.
    /// </summary>
    public MsgPackTypeId Encoding(MsgPackItem item)
    {
      long offset = item.StoredOffset;
      if (item is MpRoot || item is MpError || offset < 0 || offset >= Data.Length)
        return item.TypeId;
      byte b = Data[offset];
      if (b <= 0x7f) return MsgPackTypeId.MpBytePart;
      if (b >= 0xe0) return MsgPackTypeId.MpSBytePart;
      if (b >= 0xa0 && b <= 0xbf) return MsgPackTypeId.MpStr5;
      if (b >= 0x90 && b <= 0x9f) return MsgPackTypeId.MpArray4;
      if (b >= 0x80 && b <= 0x8f) return MsgPackTypeId.MpMap4;
      return (MsgPackTypeId)b;
    }

    /// <summary>
    /// The count of an array or map, or the length of a str, bin or ext, as its header says (the data may end before), -1 for other items or when the header itself is cut off.
    /// </summary>
    public long ClaimedLength(MsgPackItem item)
    {
      long offset = item.StoredOffset;
      if (item is MpRoot || item is MpError || offset < 0 || offset >= Data.Length)
        return -1;
      byte b = Data[offset];
      if (b >= 0x80 && b <= 0x8f || b >= 0x90 && b <= 0x9f)
        return b & 0x0f;
      if (b >= 0xa0 && b <= 0xbf)
        return b & 0x1f;
      int size;
      switch (b)
      {
        case 0xc4: case 0xc7: case 0xd9: size = 1; break;
        case 0xc5: case 0xc8: case 0xda: case 0xdc: case 0xde: size = 2; break;
        case 0xc6: case 0xc9: case 0xdb: case 0xdd: case 0xdf: size = 4; break;
        case 0xd4: return 1;
        case 0xd5: return 2;
        case 0xd6: return 4;
        case 0xd7: return 8;
        case 0xd8: return 16;
        default: return -1;
      }
      if (offset + 1 + size > Data.Length)
        return -1;
      byte[] bytes = new byte[size];
      Array.Copy(Data, offset + 1, bytes, 0, size);
      // As MsgPackVarLen.ReadLen reads it (one byte is never swapped)
      if (size > 1 && MsgPackItem.SwapEndianChoice(_settings, size))
        Array.Reverse(bytes);
      return size == 1 ? bytes[0] : size == 2 ? BitConverter.ToUInt16(bytes, 0) : (long)BitConverter.ToUInt32(bytes, 0);
    }

    /// <summary>
    /// The error an item was cut off by: the item is its <see cref="MpError.PartialItem"/>. Null for items that were read completely.
    /// </summary>
    public MpError ErrorAround(MsgPackItem item)
    {
      ItemNode node = GetNode(item);
      return node?.Parent?.Item is MpError error && ReferenceEquals(error.PartialItem, item) ? error : null;
    }

    /// <summary>
    /// A value that could not be read (e.g. a string whose data ends early): its value is not what the data holds.
    /// Containers holding an error are not: what they hold up to the error was read.
    /// </summary>
    public bool IsUnread(MsgPackItem item)
    {
      if (item is null || item is MpArray || item is MpMap || item is MpRoot)
        return false;
      MpError error = ErrorAround(item);
      return error != null && !error.IsInNestedItem;
    }

    /// <summary>
    /// The array or map of an object or collection that an error cut off (the slots after the error were not read), null when it was read completely.
    /// The item of an object can be the error around its container (the root's).
    /// </summary>
    public MsgPackItem CutOffContainer(MsgPackItem item)
    {
      if (item is MpError error)
        item = error.PartialItem;
      if (!(item is MpArray || item is MpMap))
        return null;
      return ErrorAround(item) != null ? item : null;
    }

    /// <summary>
    /// The objects, collections and values below the root, depth first.
    /// </summary>
    public static IEnumerable<PrimitiveObject> Walk(PrimitiveObject root)
    {
      Stack<PrimitiveObject> stack = new Stack<PrimitiveObject>();
      stack.Push(root);
      while (stack.Count > 0)
      {
        PrimitiveObject current = stack.Pop();
        yield return current;
        if (current is ComplexObject complex)
          for (int t = complex.Members.Count - 1; t >= 0; t--)
            stack.Push(complex.Members[t]);
      }
    }

    /// <summary>
    /// A member by its path as <see cref="PrimitiveObject.Path"/> writes it (e.g. "Lines[2].Product"), "" or "$" for the root.
    /// </summary>
    public PrimitiveObject FindObject(string path)
    {
      if (Objects is null)
        return null;
      string wanted = NormalizePath(path);
      if (wanted.Length == 0)
        return Objects;
      foreach (PrimitiveObject obj in Walk(Objects))
        if (string.Equals(obj.Path, wanted, StringComparison.Ordinal))
          return obj;
      return null;
    }

    internal static string NormalizePath(string path)
    {
      string wanted = (path ?? string.Empty).Trim();
      if (wanted.StartsWith("$", StringComparison.Ordinal))
        wanted = wanted.Substring(1);
      if (wanted.StartsWith(".", StringComparison.Ordinal))
        wanted = wanted.Substring(1);
      return wanted;
    }

    private void Decode()
    {
      if (Data.Length == 0)
        return;

      MsgPackSettings settings = new MsgPackSettings()
      {
        DynamicallyCompact = false,
        PreservePackages = true,
        ContinueProcessingOnBreakingError = Options.ContinueOnError,
        EndianAction = Options.Endian
      };
      _settings = settings;

      MsgPackItem item;
      try
      {
        MpRoot root = MsgPackItem.UnpackMultiple(Data, settings);
        item = root?.Count == 1 ? root[0] : root;
      }
      catch (Exception ex)
      {
        ReadError = ex.Message;
        return;
      }
      if (item is null)
        return;

      List<MsgPackItem> schemaItems = RootObject.FindSchemaItems(item);
      Root = AddNode(item, null, null, false);
      Traverse(Root, schemaItems);

      foreach (ItemNode node in Items)
      {
        Validate(node);
        if (node.Item is MpError error && !error.IsInNestedItem && (FirstErrorOffset < 0 || node.Offset < FirstErrorOffset))
          FirstErrorOffset = node.Offset;
      }

      RootObject objects = new RootObject();
      try
      {
        objects.Reconstruct(item, Options.Schemas);
      }
      catch (Exception ex)
      {
        // Errors are normally kept with the value they occurred in, this keeps what was reconstructed anyway
        objects.AddError(ex.Message);
      }
      Objects = objects;
      // Outer objects first, so an item that is the key of an entry and of the object in it maps to the innermost
      foreach (PrimitiveObject obj in Walk(objects))
      {
        if (!(obj.FirstItemRef is null))
          _objects[obj.FirstItemRef] = obj;
        if (!(obj.LastItemRef is null))
          _objects[obj.LastItemRef] = obj;
      }
    }

    private ItemNode AddNode(MsgPackItem item, ItemNode parent, string role, bool schema)
    {
      ItemNode node = new ItemNode()
      {
        Item = item,
        Parent = parent,
        Depth = parent is null ? 0 : parent.Depth + 1,
        Role = role,
        IsSchema = schema
      };
      Items.Add(node);
      if (!(item is null))
        _nodes[item] = node;
      if (parent != null)
        parent.Children.Add(node);
      return node;
    }

    private void Traverse(ItemNode node, List<MsgPackItem> schemaItems)
    {
      MsgPackItem item = node.Item;
      if (item is MpRoot root)
      {
        for (int t = 0; t < root.Count; t++)
          Traverse(AddNode(root[t], node, Index(t), node.IsSchema || schemaItems.Exists(s => ReferenceEquals(s, root[t]))), schemaItems);
      }
      else if (item is MpArray array)
      {
        MsgPackItem[] elements = array.PackedValues;
        for (int t = 0; t < elements.Length; t++)
          Traverse(AddNode(elements[t], node, Index(t), node.IsSchema), schemaItems);
      }
      else if (item is MpMap map)
      {
        KeyValuePair<MsgPackItem, MsgPackItem>[] entries = map.PackedValues;
        for (int t = 0; t < entries.Length; t++)
        {
          Traverse(AddNode(entries[t].Key, node, "key", node.IsSchema), schemaItems);
          Traverse(AddNode(entries[t].Value, node, "value", node.IsSchema), schemaItems);
        }
      }
      else if (item is MpError error && !(error.PartialItem is null))
      {
        Traverse(AddNode(error.PartialItem, node, "partial", node.IsSchema), schemaItems);
      }
    }

    private static string Index(int index)
    {
      return string.Concat("[", index.ToString(System.Globalization.CultureInfo.InvariantCulture), "]");
    }

    private void Validate(ItemNode node)
    {
      // The error says what is wrong with an item that could not be read, its value is not what the data holds (a uint 16 cut off is 0, "smaller encodings would save 2 bytes")
      if (node.Item is null || IsUnread(node.Item))
        return;
      MsgPackValidation.ValidationItem[] issues;
      try
      {
        // The limit of the explorers: the duplicate key check of a map costs about sqrt(limit) times its entries
        issues = MsgPackValidation.ValidateItem(node.Item, 1000);
      }
      catch (Exception ex)
      {
        issues = new MsgPackValidation.ValidationItem[] { new MsgPackValidation.ValidationItem(node.Item, MsgPackValidation.ValidationSeverity.Error, 0, "Validation failed: ", ex.Message) };
      }
      foreach (MsgPackValidation.ValidationItem issue in issues)
        Issues.Add(new Issue() { Node = node, Severity = issue.Severity, WastedBytes = issue.WaistedBytes, Message = issue.Message });
    }
  }
}
