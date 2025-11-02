using LsMsgPack.Meta;
using System;
using System.Collections.Generic;
using System.IO;

#if KEEPTRACK
using System.ComponentModel;
using System.Xml.Serialization;
#endif

namespace LsMsgPack
{
  [Serializable]
  public class MpArray : MsgPackVarLen
  {

    public MpArray() : base() { }
    public MpArray(MsgPackSettings settings) : base(settings) { }

    private Array value = new object[0];

#if KEEPTRACK
    private MsgPackItem[] packedItems = new MsgPackItem[0];
#endif

    public override MsgPackTypeId TypeId {
      get {
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
        return GetTypeId(value.LongLength);
#else
        return GetTypeId(value.Length);
#endif
      }
    }

    public override int Count
    {
      get { return value.Length; }
    }

    protected override MsgPackTypeId GetTypeId(long len)
    {
      if (len < 16) return MsgPackTypeId.MpArray4;
      if (len <= ushort.MaxValue) return MsgPackTypeId.MpArray16;
      return MsgPackTypeId.MpArray32;
    }

    public override object Value
    {
      get { return value; }
      set
      {
        if (ReferenceEquals(value, null))
        {
          this.value = (new object[0]);
        }
        else
        {
          this.value = (Array)value;
        }
      }
    }

#if KEEPTRACK
    /// <summary>
    /// Preserved containers after reading the data (contains offset metadata for debugging).
    /// Depends on MsgPackVarLen.PreservePackages.
    /// </summary>
    [XmlIgnore]
    [Category("Data")]
    [DisplayName("Data")]
    [Description("Preserved containers after reading the data (contains offset metadata for debugging).\r\nDepends on MsgPackVarLen.PreservePackages.")]
    public MsgPackItem[] PackedValues
    {
      get
      {
        return packedItems;
      }
    }
#endif

    public override byte[] ToBytes()
    {
      List<byte> bytes = new List<byte>();// cannot estimate this one
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
      MsgPackTypeId typeId = GetTypeId(value.LongLength);
#else
      MsgPackTypeId typeId = GetTypeId(value.Length);
#endif
      if (typeId == MsgPackTypeId.MpArray4) bytes.Add(GetLengthBytes(typeId, value.Length));
      else {
        bytes.Add((byte)typeId);
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
        bytes.AddRange(GetLengthBytes(value.LongLength, SupportedLengths.FromShortUpward));
#else
        bytes.AddRange(GetLengthBytes(value.Length, SupportedLengths.FromShortUpward));
#endif
      }
      Type elementType=value.GetType().GetElementType();
      FullPropertyInfo asgnType=new FullPropertyInfo(elementType);
      for (int t = 0; t < value.Length; t++)
      {
        object instance=value.GetValue(t);
        MsgPackItem item = MsgPackItem.Pack(instance, _settings, elementType) ?? MsgPackSerializer.SerializeObject(instance, _settings, asgnType);
        bytes.AddRange(item.ToBytes());
      }
      return bytes.ToArray();
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data)
    {
      long len;
      if (!IsMasked(MsgPackTypeId.MpArray4, typeId, 0x0F, out len))
      {
        switch (typeId)
        {
          case MsgPackTypeId.MpArray16: len = ReadLen(data, 2); break;
          case MsgPackTypeId.MpArray32: len = ReadLen(data, 4); break;
          default: throw new MsgPackException(string.Concat("MpArray does not support a type ID of ", GetOfficialTypeName(typeId), "."), data.Position - 1, typeId);
        }
      }

      value = new object[len];
#if KEEPTRACK
      packedItems = new MsgPackItem[len];
      bool errorOccurred = false; // keep a local copy in order not to wrap all items after an error in error nodes (just the one the error occurred in, and all parents)
#endif
      for (int t = 0; t < len; t++)
      {
        MsgPackItem item = Unpack(data, _settings);
        value.SetValue(item.Value,t);
#if KEEPTRACK
        if (_settings._preservePackages) packedItems[t] = item;
        if (item is MpError)
        {
          if (_settings._continueProcessingOnBreakingError)
          {
            _settings.FileContainsErrors = true;
            errorOccurred = true;
            if (data.Position >= data.Length) return new MpError(_settings, this);
          }
          else return new MpError(_settings, this);
        }
      }
      if (errorOccurred) return new MpError(_settings, this);
#else
      }
#endif
      return this;
    }

    public override string ToString()
    {
      return string.Concat("Array (", GetOfficialTypeName(TypeId), ") of ", value.Length, " items.");
    }
  }
}
