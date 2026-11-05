using System;
using System.Collections.Generic;
using System.IO;
using LsMsgPack.Meta;
#if KEEPTRACK
using System.ComponentModel;
using System.Xml.Serialization;
#endif

namespace LsMsgPack {
  [Serializable]
  public class MpBin: MsgPackVarLen {

    public MpBin() : base() { }
    public MpBin(MsgPackSettings settings) : base(settings) { }

    private byte[] value = new byte[0];

    public override MsgPackTypeId TypeId {
      get {
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
#if KEEPTRACK
        if(_truncatedLength > 0)
          return GetTypeId(_truncatedLength);
#endif
        return GetTypeId(value.LongLength);
#else
        return GetTypeId(value.Length);
#endif
      }
    }

    public override int Count {
      get { return value.Length; }
    }

#if KEEPTRACK
    [Browsable(true)]
    public override long OriginalLength {
      get { return base.OriginalLength; }
    }

    /// <summary>
    /// A chunk of a bin of unknown length of which only the ends were kept.
    /// </summary>
    internal static MpBin Truncated(MsgPackSettings settings, byte[] head, byte[] tail, long start, long len) {
      MpBin bin = new MpBin(settings);
      bin.SetEnds(head, tail);
      bin.MarkTruncated(start, len, head.Length, tail.Length);
      return bin;
    }

    private void SetEnds(byte[] head, byte[] tail) {
      value = new byte[head.Length + tail.Length];
      Array.Copy(head, 0, value, 0, head.Length);
      Array.Copy(tail, 0, value, head.Length, tail.Length);
    }
#endif

    protected override MsgPackTypeId GetTypeId(long len) {
      if(len < 256) return MsgPackTypeId.MpBin8;
      if(len <= ushort.MaxValue) return MsgPackTypeId.MpBin16;
      return MsgPackTypeId.MpBin32;
    }

    public override object Value {
      get { return value; }
      set
      {
        if (ReferenceEquals(value, null))
          this.value = new byte[0];
        else if (value is Guid)
          this.value = ((Guid)value).ToByteArray();
        else
          this.value = (byte[])value;
      }
    }

    public override T GetTypedValue<T>()
    {
      Type targetType = typeof(T);
      if (targetType == typeof(Guid))
        return (T)(object)(new Guid(value));
      return base.GetTypedValue<T>();
    }

    public override byte[] ToBytes() {
      ByteWriter bytes = new ByteWriter(value.Length + 5); // current max length limit is 4 bytes + identifier
      WriteValue(bytes);
      return bytes.ToArray();
    }

    internal override void WriteTo(ByteWriter target) {
      if (GetType() != typeof(MpBin)) { // a derived type may override ToBytes
        base.WriteTo(target);
        return;
      }
      WriteValue(target);
    }

    private void WriteValue(ByteWriter bytes) {
#if KEEPTRACK
      ThrowWhenTruncated();
#endif
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
      MsgPackTypeId typeId = GetTypeId(value.LongLength);
#else
      MsgPackTypeId typeId = GetTypeId(value.Length);
#endif
      bytes.Write((byte)typeId);
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
      WriteLength(bytes, value.LongLength, SupportedLengths.All);
#else
      WriteLength(bytes, value.Length, SupportedLengths.All);
#endif
      bytes.Write(value);
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data) {
      long len;

      switch(typeId) {
        case MsgPackTypeId.MpBin8: len = ReadLen(data, 1); break;
        case MsgPackTypeId.MpBin16: len = ReadLen(data, 2); break;
        case MsgPackTypeId.MpBin32: len = ReadLen(data, 4); break;
        default: throw new MsgPackException($"MpBin does not support a type ID of {GetOfficialTypeName(typeId)}.", data.Position - 1, typeId);
      }
#if KEEPTRACK
      if(Truncates(len, KeptBytes)) {
        byte[] head;
        byte[] tail;
        long start;
        ReadEnds(data, len, KeptBytes, out head, out tail, out start);
        SetEnds(head, tail);
        MarkTruncated(start, len, head.Length, tail.Length);
        return this;
      }
#endif
      value = ReadBytes(data, len);
      return this;
    }

#if KEEPTRACK
    private MsgPackItem[] chunks;

    /// <summary>
    /// The chunks of a bin of unknown length (<see cref="MsgPackVarLen.LengthUnknown"/>), null otherwise.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public MsgPackItem[] Chunks {
      get { return chunks; }
    }
#endif

    internal override MsgPackItem ReadUnknownLength(Stream data) {
      MemoryStream content = new MemoryStream();
#if KEEPTRACK
      _lengthUnknown = true;
      List<MsgPackItem> parts = new List<MsgPackItem>();
#else
      List<MsgPackItem> parts = null;
#endif
      try {
        ReadChunks(data, false, content, parts);
      } finally {
        value = content.ToArray();
#if KEEPTRACK
        chunks = parts.ToArray();
#endif
      }
      return this;
    }

    public override string ToString() {
#if KEEPTRACK
      if(_truncatedLength > 0)
        return $"Blob ({GetOfficialTypeName(TypeId)}) of {_truncatedLength} bytes, {TooLargeText}.";
      if(_lengthUnknown)
        return $"Blob of unknown length in {chunks.Length} chunks (0xC1 0xC4 ... 0xC1 0xC0) of {value.Length} bytes.";
#endif
      return $"Blob ({GetOfficialTypeName(TypeId)}) of {value.Length} bytes.";
    }
  }
}
