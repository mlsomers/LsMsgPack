using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using LsMsgPack.Meta;

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
      int byteCount = encoding.GetByteCount(value);
      ByteWriter bytes = new ByteWriter(byteCount + 5); // current max length limit is 4 bytes + string identifier
      WriteValue(bytes, byteCount);
      return bytes.ToArray();
    }

    internal override void WriteTo(ByteWriter target)
    {
      if (GetType() != typeof(MpString)) // a derived type may override ToBytes
      {
        base.WriteTo(target);
        return;
      }
      WriteValue(target, encoding.GetByteCount(value));
    }

    /// <summary>
    /// Writes the same bytes as <c>new MpString(settings) { Value = value }.ToBytes()</c> without creating the item (used for the indexed schema).
    /// </summary>
    internal static void Write(ByteWriter bytes, string value, MsgPackSettings settings)
    {
      Encoding encoding = DefaultEncoding;
      int byteCount = encoding.GetByteCount(value);
      if (byteCount < 32) bytes.Write((byte)((byte)MsgPackTypeId.MpStr5 | byteCount));
      else
      {
        bytes.Write((byte)(byteCount < 256 ? MsgPackTypeId.MpStr8 : byteCount <= ushort.MaxValue ? MsgPackTypeId.MpStr16 : MsgPackTypeId.MpStr32));
        WriteLength(bytes, byteCount, SupportedLengths.All, settings);
      }
      bytes.Write(value, encoding, byteCount);
    }

    /// <summary>
    /// Encodes the string directly into the target (instead of allocating <see cref="StrAsBytes"/>)
    /// </summary>
    private void WriteValue(ByteWriter bytes, int byteCount)
    {
      MsgPackTypeId typeId = GetTypeId(byteCount);

      if (typeId == MsgPackTypeId.MpStr5) bytes.Write(GetLengthBytes(typeId, byteCount));
      else {
        bytes.Write((byte)typeId);
        WriteLength(bytes, byteCount, SupportedLengths.All);
      }
      bytes.Write(value, encoding, byteCount);
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
