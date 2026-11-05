using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using LsMsgPack.Meta;
#if KEEPTRACK
using System.ComponentModel;
#endif

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
#if KEEPTRACK
        if (_truncatedLength > 0)
          return GetTypeId(_truncatedLength);
#endif
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

#if KEEPTRACK
    [Browsable(true)]
    public override long OriginalLength
    {
      get { return _truncatedLength > 0 ? _truncatedLength : encoding.GetByteCount(value); }
    }

    /// <summary>
    /// A chunk of a str of unknown length of which only the ends were kept.
    /// </summary>
    internal static MpString Truncated(MsgPackSettings settings, byte[] head, byte[] tail, long start, long len)
    {
      MpString str = new MpString(settings);
      str.SetEnds(head, tail, start, len);
      return str;
    }

    /// <summary>
    /// The first and last characters around <see cref="MsgPackVarLen.TooLargeText"/>, the hex views keep the bytes of these characters.
    /// </summary>
    private void SetEnds(byte[] head, byte[] tail, long start, long len)
    {
      string first;
      string last;
      Keep(head, 0, head.Length, tail, 0, tail.Length, out first, out last);
      value = string.Concat(first, " ... ", TooLargeText, " ... ", last);
      // An invalid byte becomes a character of 3 bytes: not more than what was read
      MarkTruncated(start, len, Math.Min(encoding.GetByteCount(first), head.Length), Math.Min(encoding.GetByteCount(last), tail.Length));
    }

    private void Keep(byte[] head, int headOffset, int headCount, byte[] tail, int tailOffset, int tailCount, out string first, out string last)
    {
      first = encoding.GetString(head, headOffset, headCount);
      if (first.Length > KeptCharacters)
        first = first.Substring(0, char.IsHighSurrogate(first[KeptCharacters - 1]) ? KeptCharacters - 1 : KeptCharacters);
      last = encoding.GetString(tail, tailOffset, tailCount);
      if (last.Length > KeptCharacters)
      {
        int from = last.Length - KeptCharacters;
        last = last.Substring(char.IsLowSurrogate(last[from]) ? from + 1 : from);
      }
    }
#endif

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

    /// <summary>
    /// Default string encoding will be UTF8 if this property is not changed
    /// </summary>
    public static Encoding DefaultEncoding
    {
      get { return MsgPackOptions.StringEncoding; } // shared with the indexed schema (LsMsgPack.Core)
      set { MsgPackOptions.StringEncoding = value; }
    }

    private Encoding encoding = DefaultEncoding;
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
#if KEEPTRACK
      ThrowWhenTruncated();
#endif
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
#if KEEPTRACK
      ThrowWhenTruncated();
#endif
      WriteValue(target, encoding.GetByteCount(value));
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
#if KEEPTRACK
      if (Truncates(len, BytesForKeptCharacters))
      {
        byte[] head;
        byte[] tail;
        long start;
        ReadEnds(data, len, BytesForKeptCharacters, out head, out tail, out start);
        SetEnds(head, tail, start, len);
        return this;
      }
#endif
      StrAsBytes = ReadBytes(data, len);
      return this;
    }

#if KEEPTRACK
    private MsgPackItem[] chunks;

    /// <summary>
    /// The chunks of a str of unknown length (<see cref="MsgPackVarLen.LengthUnknown"/>), null otherwise.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public MsgPackItem[] Chunks
    {
      get { return chunks; }
    }
#endif

    internal override MsgPackItem ReadUnknownLength(Stream data)
    {
      MemoryStream content = new MemoryStream();
#if KEEPTRACK
      _lengthUnknown = true;
      List<MsgPackItem> parts = new List<MsgPackItem>();
#else
      List<MsgPackItem> parts = null;
#endif
      try
      {
        ReadChunks(data, true, content, parts);
      }
      finally
      {
#if KEEPTRACK
        chunks = parts.ToArray();
        if (_truncatedLength > 0)
        {
          // The chunks noted what they left out
          byte[] ends = content.ToArray();
          string first;
          string last;
          Keep(ends, 0, _truncatedHead, ends, _truncatedHead, ends.Length - _truncatedHead, out first, out last);
          value = string.Concat(first, " ... ", TooLargeText, " ... ", last);
        }
        else
#endif
        StrAsBytes = content.ToArray(); // decoded at once: a chunk may end inside a character
      }
      return this;
    }

    public override string ToString()
    {
#if KEEPTRACK
      if (_truncatedLength > 0) // on one line: the explorers show the first line of it, the marker is in the middle
        return $"String ({GetOfficialTypeName(TypeId)}) of {_truncatedLength} bytes with the value \"{value.Replace('\r', ' ').Replace('\n', ' ')}\"";
      if (_lengthUnknown)
        return $"String of unknown length in {chunks.Length} chunks (0xC1 0xA0 ... 0xC1 0xC0) with the value \"{value}\"";
#endif
      return $"String ({GetOfficialTypeName(TypeId)}) with the value \"{value}\"";
    }
  }
}
