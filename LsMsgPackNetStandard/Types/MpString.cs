using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace LsMsgPack
{
  [Serializable]
  public class MpString : MsgPackVarLen
  {

    public MpString() : base() { }
    public MpString(MsgPackSettings settings) : base(settings) { }

    private string value = string.Empty;

    public override MsgPackTypeId TypeId {
      get {
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
        return GetTypeId(StrAsBytes.LongLength);
#else
        return GetTypeId(StrAsBytes.Length);
#endif
      }
    }

    public override int Count
    {
      get { return value.Length; }
    }

    protected override MsgPackTypeId GetTypeId(long len)
    {
      if (len < 32) return MsgPackTypeId.MpStr5;
      if (len < 256) return MsgPackTypeId.MpStr8;
      if (len <= ushort.MaxValue) return MsgPackTypeId.MpStr16;
      return MsgPackTypeId.MpStr32;
    }

    public override object Value
    {
      get { return value; }
      set { this.value = ReferenceEquals(value, null) ? string.Empty : value.ToString(); }
    }

    private static Encoding defaultEncoding = Encoding.UTF8;
    /// <summary>
    /// Default string encoding will be UTF8 if this property is not changed
    /// </summary>
    public static Encoding DefaultEncoding
    {
      get { return defaultEncoding; }
      set { defaultEncoding = value; }
    }

    private Encoding encoding = defaultEncoding;
    /// <summary>
    /// will initially be the statically defined DefaultEncoding, but may also be dynamically changed per instance (note that the chosen encoding will not be persisted)
    /// </summary>
    [XmlIgnore]
    public Encoding Encoding
    {
      get { return encoding; }
      set
      {
        if (ReferenceEquals(value, null)) return;
        encoding = value;
      }
    }

    private byte[] StrAsBytes
    {
      get
      {
        return encoding.GetBytes(value);
      }
      set
      {
        this.value = encoding.GetString(value);
      }
    }

    public override byte[] ToBytes()
    {
      byte[] strBytes = StrAsBytes;
      List<byte> bytes = new List<byte>(strBytes.Length + 5); // current max length limit is 4 bytes + string identifier

#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
      MsgPackTypeId typeId = GetTypeId(strBytes.LongLength);
#else
      MsgPackTypeId typeId = GetTypeId(strBytes.Length);
#endif


      if (typeId == MsgPackTypeId.MpStr5) bytes.Add(GetLengthBytes(typeId, strBytes.Length));
      else {
        bytes.Add((byte)typeId);
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
        bytes.AddRange(GetLengthBytes(strBytes.LongLength, SupportedLengths.All));
#else
        bytes.AddRange(GetLengthBytes(strBytes.Length, SupportedLengths.All));
#endif
      }
      bytes.AddRange(strBytes);
      return bytes.ToArray();
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data)
    {
      long len;
      if (!IsMasked(MsgPackTypeId.MpStr5, typeId, 0x1F, out len))
      {
        switch (typeId)
        {
          case MsgPackTypeId.MpStr8: len = ReadLen(data, 1); break;
          case MsgPackTypeId.MpStr16: len = ReadLen(data, 2); break;
          case MsgPackTypeId.MpStr32: len = ReadLen(data, 4); break;
          default: throw new MsgPackException($"MpString does not support a type ID of {GetOfficialTypeName(typeId)}.", data.Position - 1, typeId);
        }
      }
      StrAsBytes = ReadBytes(data, len);
      return this;
    }

    public override string ToString()
    {
      return $"String ({GetOfficialTypeName(TypeId)}) with the value \"{value}\"";
    }
  }
}
