using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace LsMsgPack
{

  /// <summary>
  /// Root is not part of the spec, it is a container for multiple sequential items.
  /// </summary>
  public class MpRoot : MsgPackVarLen, IList<MsgPackItem>, IEnumerable<MsgPackItem>, IEnumerable
  {

    private List<MsgPackItem> packedItems;

    internal MpRoot() : base()
    {
      packedItems = new List<MsgPackItem>();
    }

    public MpRoot(MsgPackSettings settings, List<MsgPackItem> packedItems) : base(settings)
    {
      this.packedItems = packedItems;
    }

    public MpRoot(MsgPackSettings settings, IEnumerable<MsgPackItem> packedItems) : base(settings)
    {
      this.packedItems = new List<MsgPackItem>(packedItems);
    }

    public MpRoot(MsgPackSettings settings, params MsgPackItem[] packedItems) : base(settings)
    {
      this.packedItems = new List<MsgPackItem>(packedItems);
    }

    public MpRoot(MsgPackSettings settings, int capacity) : base(settings)
    {
      this.packedItems = new List<MsgPackItem>(capacity);
    }

    /// <summary>
    /// Root is not part of the spec, it is a container for multiple sequential items.
    /// </summary>
    public override MsgPackTypeId TypeId
    {
      get
      {
        return MsgPackTypeId.NeverUsed;
      }
    }

    public override object Value
    {
      get { return packedItems.ToArray(); }
      set { packedItems = new List<MsgPackItem>(value as IEnumerable<MsgPackItem>); }
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data)
    {
      MsgPackItem item = UnpackMultiple(data, Settings);
      if (item is MpRoot)
        packedItems = ((MpRoot)item).packedItems;
      else
      {
        packedItems = new List<MsgPackItem>(1);
        packedItems.Add(item);
      }

      return item;
    }

    public override byte[] ToBytes()
    {
      List<byte> bytes = new List<byte>();
      for (int t = 0; t < packedItems.Count; t++)
      {
        bytes.AddRange(packedItems[t].ToBytes());
      }
      return bytes.ToArray();
    }

    protected override MsgPackTypeId GetTypeId(long len)
    {
      return MsgPackTypeId.NeverUsed;
    }

    public override string ToString()
    {
      return string.Concat("Root (", packedItems.Count, " items)");
    }

    // iList interface

    public override int Count
    {
      get
      {
        return packedItems.Count;
      }
    }

    public MsgPackItem this[int index]
    {
      get { return packedItems[index]; }
      set { packedItems[index] = value; }
    }

    public bool IsReadOnly { get { return false; } }

    public int IndexOf(MsgPackItem item)
    {
      return packedItems.IndexOf(item);
    }

    public void Insert(int index, MsgPackItem item)
    {
      packedItems.Insert(index, item);
    }

    public void RemoveAt(int index)
    {
      packedItems.RemoveAt(index);
    }

    public void Add(MsgPackItem item)
    {
      packedItems.Add(item);
    }

    public void Add(params MsgPackItem[] items)
    {
      packedItems.AddRange(items);
    }

    public void AddRange(IEnumerable<MsgPackItem> items)
    {
      packedItems.AddRange(items);
    }

    public void Clear()
    {
      packedItems.Clear();
    }

    public bool Contains(MsgPackItem item)
    {
      return packedItems.Contains(item);
    }

    public void CopyTo(MsgPackItem[] array, int arrayIndex)
    {
      packedItems.CopyTo(array, arrayIndex);
    }

    public bool Remove(MsgPackItem item)
    {
      return packedItems.Remove(item);
    }

    public IEnumerator<MsgPackItem> GetEnumerator()
    {
      return ((IEnumerable<MsgPackItem>)packedItems).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return ((IEnumerable)packedItems).GetEnumerator();
    }
  }
}
