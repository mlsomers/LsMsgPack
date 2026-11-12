using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LsMsgPack.Meta;
using System.Xml.Serialization;
#if KEEPTRACK
using System.ComponentModel;
#endif

namespace LsMsgPackDebug {
  [Serializable]
  public abstract class MsgPackVarLen: MsgPackItem {

    public MsgPackVarLen() : base() { }
    public MsgPackVarLen(MsgPackSettings settings) : base(settings) { }

    protected abstract MsgPackTypeId GetTypeId(long len);

    [XmlIgnore]
#if KEEPTRACK
    [Category("MetaData")]
    [DisplayName("Count")]
    [Description("The number of items in this collection.")]
#endif
    /// <summary>
    /// The number of items in this collection.
    /// </summary>
    public abstract int Count { get; }

#if KEEPTRACK
    internal bool _lengthUnknown;

    /// <summary>
    /// What the explorers show instead of the content of a value above <see cref="MsgPackSettings.TruncateValuesLargerThan"/>.
    /// </summary>
    public const string TooLargeText = "too large for practical debugging";

    /// <summary>
    /// The bytes kept of each end of a bin or ext, the bytes read at each end of a str: 100 characters of UTF-16 take up to 300 bytes in UTF-8, the rest of the 400 covers characters cut off at the edge.
    /// </summary>
    internal const int KeptBytes = 100;
    internal const int KeptCharacters = 100;
    internal const int BytesForKeptCharacters = 400;

    /// <summary>
    /// The length of the content in the data when only its ends were kept, 0 otherwise.
    /// </summary>
    internal long _truncatedLength;

    /// <summary>
    /// Only the first and last bytes (bin, ext) or characters (str) of this value were kept: it is above <see cref="MsgPackSettings.TruncateValuesLargerThan"/>. <see cref="OriginalLength"/> has its length, it cannot be written back.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public bool IsTruncated {
      get { return _truncatedLength > 0; }
    }

    /// <summary>
    /// The number of bytes of the content in the data, also when it was truncated.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)] // shown for the values that have a content in bytes, which override it
    [Category("MetaData")]
    [DisplayName("Original length")]
    [Description("The number of bytes of the content in the data. A value that is too large for practical debugging only keeps its first and last bytes or characters.")]
    public virtual long OriginalLength {
      get { return _truncatedLength > 0 ? _truncatedLength : Count; }
    }

    /// <summary>
    /// A value of <paramref name="len"/> bytes is truncated (when more than what it keeps of its ends).
    /// </summary>
    internal bool Truncates(long len, int keptBytes) {
      return len > _settings._truncateValuesLargerThan && len > 2L * keptBytes;
    }

    /// <summary>
    /// Reads the first and last <paramref name="keep"/> bytes of a content of <paramref name="len"/> bytes, the rest is passed without keeping it. Note what was left out with <see cref="MarkTruncated"/>.
    /// </summary>
    /// <param name="start">Gets the offset of the content</param>
    internal void ReadEnds(Stream data, long len, int keep, out byte[] head, out byte[] tail, out long start) {
      ThrowWhenNotInData(data, len);
      start = data.Position;
      head = new byte[keep];
      ReadExactly(data, head, keep);
      Pass(data, len - 2L * keep);
      tail = new byte[keep];
      ReadExactly(data, tail, keep);
    }

    /// <summary>
    /// Passes bytes without keeping them.
    /// </summary>
    private static void Pass(Stream data, long count) {
      if(data.CanSeek) {
        data.Seek(count, SeekOrigin.Current);
        return;
      }
      byte[] buffer = new byte[Math.Min(count, ReadStep)];
      while(count > 0) {
        int read = data.Read(buffer, 0, (int)Math.Min(count, buffer.Length));
        if(read <= 0)
          throw UnexpectedEnd(data);
        count -= read;
      }
    }

    /// <summary>
    /// Registers what the hex views leave out: the bytes between the kept ends of a content.
    /// </summary>
    internal void MarkTruncated(long start, long len, int headBytes, int tailBytes) {
      _truncatedLength = len;
      _settings.SkippedRanges.Add(new SkippedRange(start + headBytes, len - headBytes - tailBytes));
    }

    internal void ThrowWhenTruncated() {
      if(_truncatedLength > 0)
        throw new MsgPackException($"This {GetType().Name} only has the ends of its {_truncatedLength} bytes ({TooLargeText}), it cannot be written.", 0, TypeId);
    }

    /// <summary>
    /// An array or map writes the values of its items, not the items: a truncated value would be written as its ends without any notice.
    /// </summary>
    internal void ThrowWhenItemsTruncated() {
      if(_settings != null && _settings.SkippedRanges.Count > 0)
        throw new MsgPackException($"This {GetType().Name} may hold a value of which only the ends were kept ({TooLargeText}), it cannot be written.", 0, TypeId);
    }

    /// <summary>
    /// The joined chunks of a str or bin of unknown length once they are above the limit: only the first and last bytes stay in memory.
    /// </summary>
    private sealed class ChunkEnds {
      private readonly byte[] _head;
      private readonly byte[] _tail;
      private int _headCount;
      private int _tailCount;

      public ChunkEnds(int keep) {
        _head = new byte[keep];
        _tail = new byte[keep];
      }

      public void Add(byte[] bytes) {
        Add(bytes, bytes);
      }

      /// <param name="first">The first bytes of the next part</param>
      /// <param name="last">Its last bytes (the same array when nothing is left out)</param>
      public void Add(byte[] first, byte[] last) {
        int headBytes = Math.Min(_head.Length - _headCount, first.Length);
        Array.Copy(first, 0, _head, _headCount, headBytes);
        _headCount += headBytes;

        if(last.Length >= _tail.Length) {
          Array.Copy(last, last.Length - _tail.Length, _tail, 0, _tail.Length);
          _tailCount = _tail.Length;
          return;
        }
        int stay = Math.Min(_tail.Length - last.Length, _tailCount);
        Array.Copy(_tail, _tailCount - stay, _tail, 0, stay);
        Array.Copy(last, 0, _tail, stay, last.Length);
        _tailCount = stay + last.Length;
      }

      /// <summary>
      /// The content holds both ends afterwards: <see cref="Middle"/> tells where the first end stops.
      /// </summary>
      public void WriteTo(MemoryStream content) {
        content.SetLength(0);
        content.Write(_head, 0, _headCount);
        content.Write(_tail, 0, _tailCount);
        Middle = _headCount;
      }

      public int Middle { get; private set; }
    }

    /// <summary>
    /// Where the first end stops in the content of a str or bin of unknown length that was truncated.
    /// </summary>
    internal int _truncatedHead;

    /// <summary>
    /// The data did not have the length before the content: it was written as an escape sequence (0xC1 0x90, 0x80, 0xA0 or 0xC4 ... 0xC1 0xC0) as proposed in msgpack/msgpack#347, read with <see cref="MsgPackSettings.ReadStreams"/>.
    /// </summary>
    [XmlIgnore]
    [Category("MetaData")]
    [DisplayName("Unknown length")]
    [Description("Written without the length before the content: an escape sequence (0xC1 followed by 0x90 for an array, 0x80 a map, 0xA0 a str or 0xC4 a bin in chunks), ended by 0xC1 0xC0, as proposed in msgpack/msgpack#347 and written by stream writers.")]
    public bool LengthUnknown {
      get { return _lengthUnknown; }
    }
#endif

    /// <summary>
    /// Reads the content of a value of unknown length after its escape sequence (<see cref="MsgPackSettings.ReadStreams"/>), up to and including its end (0xC1 0xC0).
    /// </summary>
    internal virtual MsgPackItem ReadUnknownLength(Stream data) {
      throw new MsgPackException($"{GetType().Name} has no unknown length.", data.Position, TypeId);
    }

    /// <summary>
    /// The next item of a container of unknown length, or null (with <paramref name="end"/>) at its end (0xC1 0xC0).
    /// </summary>
    internal MsgPackItem UnpackOrEnd(Stream data, out bool end) {
      end = false;
      int typeByte = data.ReadByte();
      if(typeByte < 0) {
#if KEEPTRACK
        return new MpError(_settings, data.Position, MsgPackTypeId.NeverUsed, "Unexpected end of data: the ", GetType().Name, " of unknown length has no end (0xC1 0xC0).");
#else
        throw new MsgPackException($"Unexpected end of data: the {GetType().Name} of unknown length has no end (0xC1 0xC0).", data.Position, MsgPackTypeId.NeverUsed);
#endif
      }
      int code = CodeNotRead;
      if(typeByte == LsMsgPack.Streaming.Escape.Byte) {
        code = data.ReadByte();
        if(code == LsMsgPack.Streaming.Escape.End) {
          end = true;
          return null;
        }
      }
      return Unpack(data, _settings, _depth + 1, typeByte, code);
    }

    /// <summary>
    /// The chunks of a str (<paramref name="text"/>) or bin of unknown length after its escape sequence, up to and including its end (0xC1 0xC0): only chunks of the same family may follow.
    /// </summary>
    /// <param name="content">Gets the bytes of the chunks (also when an error stops the reading)</param>
    /// <param name="chunks">Gets an item per chunk (KEEPTRACK), null otherwise</param>
    protected void ReadChunks(Stream data, bool text, MemoryStream content, List<MsgPackItem> chunks) {
      string kind = text ? "str" : "bin";
      long total = 0;
#if KEEPTRACK
      long limit = _settings._truncateValuesLargerThan;
      int keep = text ? BytesForKeptCharacters : KeptBytes;
      ChunkEnds ends = null;
      try {
#endif
      while(true) {
        long start = data.Position;
        int b = data.ReadByte();
        if(b < 0)
          throw new MsgPackException($"Unexpected end of data: the {kind} of unknown length has no end (0xC1 0xC0).", data.Position, MsgPackTypeId.NeverUsed);
        if(b == LsMsgPack.Streaming.Escape.Byte) {
          int code = data.ReadByte();
          if(code == LsMsgPack.Streaming.Escape.End)
            return;
          if(code < 0)
            throw LsMsgPack.Streaming.Escape.AtEndOfData(start);
          throw new MsgPackException($"Only {kind} chunks may follow the start of a {kind} of unknown length, not the escape sequence 0xC1 0x{code:X2}.", start, MsgPackTypeId.NeverUsed);
        }
        long len;
        if(text && (b & 0xE0) == 0xA0)
          len = b & 0x1F;
        else {
          switch(b) {
            case 0xD9 when text: len = ReadLen(data, 1); break;
            case 0xDA when text: len = ReadLen(data, 2); break;
            case 0xDB when text: len = ReadLen(data, 4); break;
            case 0xC4 when !text: len = ReadLen(data, 1); break;
            case 0xC5 when !text: len = ReadLen(data, 2); break;
            case 0xC6 when !text: len = ReadLen(data, 4); break;
            default: throw new MsgPackException($"Only {kind} chunks may follow the start of a {kind} of unknown length, not 0x{b:X2} ({GetOfficialTypeName((MsgPackTypeId)b)}).", start, (MsgPackTypeId)b);
          }
        }
        if(total + len > uint.MaxValue)
          throw new MsgPackException($"The {kind} of unknown length is longer than {uint.MaxValue} bytes, the most a {kind} can hold.", start, (MsgPackTypeId)b);
#if KEEPTRACK
        if(ends is null && total + len > limit && total + len > 2L * keep) {
          // From here on the ends are enough, also of what was joined before
          byte[] joined = content.ToArray();
          ends = new ChunkEnds(keep);
          ends.Add(joined);
          content.SetLength(0);
        }
#endif
#if KEEPTRACK
        if(ends is null)
#endif
        if(total + len > MaxArrayLength)
          throw TooLongForArray(total + len, start);
        total += len;
        byte[] raw;
#if KEEPTRACK
        MsgPackVarLen chunk = null;
        if(ends != null) {
          if(len > 2L * keep) {
            byte[] head;
            byte[] tail;
            long contentStart;
            ReadEnds(data, len, keep, out head, out tail, out contentStart);
            ends.Add(head, tail);
            chunk = text ? (MsgPackVarLen)MpString.Truncated(_settings, head, tail, contentStart, len) : MpBin.Truncated(_settings, head, tail, contentStart, len);
            raw = null;
          }
          else {
            raw = ReadBytes(data, len);
            ends.Add(raw);
          }
        }
        else
#endif
        {
          raw = ReadBytes(data, len);
          content.Write(raw, 0, raw.Length);
        }
#if KEEPTRACK
        if(chunks != null) {
          if(chunk is null)
            chunk = text ? (MsgPackVarLen)new MpString(_settings) { Value = MpString.DefaultEncoding.GetString(raw) } : new MpBin(_settings) { Value = raw };
          chunk._depth = _depth + 1;
          chunk.SetStoredRange(start, data.Position - start);
          chunks.Add(chunk);
        }
#endif
      }
#if KEEPTRACK
      }
      finally {
        if(ends != null) {
          _truncatedLength = total;
          ends.WriteTo(content);
          _truncatedHead = ends.Middle;
        }
      }
#endif
    }

    protected byte[] GetLengthBytes(long length, SupportedLengths supported) {
      if(length < 256 && (supported & SupportedLengths.Byte1)>0) return new byte[1] { (byte)length };
      byte[] bytes; // from here we should worry about endianness
      if(length <= ushort.MaxValue && (supported & SupportedLengths.Short2) > 0) bytes = BitConverter.GetBytes((ushort)length);
      else if(length <= uint.MaxValue && (supported & SupportedLengths.Int4) > 0) bytes = BitConverter.GetBytes((uint)length);
      else bytes = BitConverter.GetBytes((ulong)length); 
      ReorderIfLittleEndian(Settings, bytes);
      return bytes;
    }

    /// <summary>
    /// Writes the same bytes as <see cref="GetLengthBytes(long, SupportedLengths)"/> without allocating them.
    /// </summary>
    internal void WriteLength(ByteWriter bytes, long length, SupportedLengths supported) {
      WriteLength(bytes, length, supported, Settings);
    }

    internal static void WriteLength(ByteWriter bytes, long length, SupportedLengths supported, MsgPackSettings settings) {
      if(length < 256 && (supported & SupportedLengths.Byte1) > 0) bytes.Write((byte)length);
      else if(length <= ushort.MaxValue && (supported & SupportedLengths.Short2) > 0) bytes.WriteEndian((ulong)length, 2, settings);
      else if(length <= uint.MaxValue && (supported & SupportedLengths.Int4) > 0) bytes.WriteEndian((ulong)length, 4, settings);
      else bytes.WriteEndian((ulong)length, 8, settings);
    }

    protected byte GetLengthBytes(MsgPackTypeId maskTypeId, int length) {
      byte len = (byte)length;
      return (byte)((byte)maskTypeId | len);
    }

    protected bool IsMasked(MsgPackTypeId definition, MsgPackTypeId value, byte valueMask, out long len) {
      byte def = (byte)definition;
      byte val = (byte)value;
      len = val & valueMask;

      if((val - len) == def) {
        return true;
      } else {
        len = 0;
        return false;
      }
    }

    protected long ReadLen(Stream data, int bytes) {
      if(bytes == 1)
        return ReadByteExactly(data);
      byte[] buffer = new byte[bytes];
      ReadExactly(data, buffer, bytes);
      if(bytes == 1) return (long)buffer[0];
      ReorderIfLittleEndian(Settings, buffer);
      switch(bytes) {
        case 2:
          return (long)BitConverter.ToUInt16(buffer, 0);
        case 4:
          return (long)BitConverter.ToUInt32(buffer, 0);
        case 8:
          return (long)BitConverter.ToUInt64(buffer, 0);
      }
      throw new MsgPackException(
        $"Only 1, 2, 4 or 8 byte lenths are allowed. {bytes.ToString(CultureInfo.InvariantCulture)} is not implemented.", data.Position, TypeId);
    }

    /// <summary>
    /// The slots to allocate for a count of items read from a header. A header of a few bytes can claim billions of items: a count the rest
    /// of the data cannot hold throws before anything is allocated (as LsMsgPack does). Under KEEPTRACK (the explorers show truncated data
    /// up to where it ends) it gets the slots the rest of the data can fill instead, plus one for the error at its end: every item takes at
    /// least one byte, an error included, except the one at the end of the data, which ends the loop.
    /// Only streams that know their length are checked, the others end with "Unexpected end of data." when the items do not arrive.
    /// </summary>
    /// <param name="bytesPerItem">The least each item takes: 1 for array items, 2 for map entries (key and value)</param>
    protected static long SlotsFor(Stream data, long count, int bytesPerItem) {
      if(!data.CanSeek)
        return count;
      long remaining = data.Length - data.Position;
      long fit = remaining / bytesPerItem;
      if(count <= fit)
        return count;
#if KEEPTRACK
      return fit + 1;
#else
      throw new MsgPackException($"The data claims {count} items, more than the remaining {remaining} bytes can hold.");
#endif
    }

    /// <summary>
    /// Array.MaxLength (.NET Standard does not have it), the specification allows up to <see cref="uint.MaxValue"/>: the explorers truncate longer values (<see cref="MsgPackSettings.TruncateValuesLargerThan"/>), a serializer cannot hold them.
    /// </summary>
    private const int MaxArrayLength = 0x7FFFFFC7;

    /// <summary>
    /// What is allocated at once for data that cannot tell how much it still has.
    /// </summary>
    private const int ReadStep = 1024 * 1024;

    private static MsgPackException TooLongForArray(long len, long offset) {
      return new MsgPackException($"The data claims a value of {len} bytes, longer than a .NET array can hold.", offset, MsgPackTypeId.NeverUsed);
    }

    private static void ThrowWhenNotInData(Stream data, long len) {
      if(data.CanSeek && len > data.Length - data.Position) {
        // What reading would do (consume the rest, then fail), without allocating the claimed length first
        data.Seek(0, SeekOrigin.End);
        throw UnexpectedEnd(data);
      }
    }

    protected byte[] ReadBytes(Stream data, long len) {
      ThrowWhenNotInData(data, len);
      if(len > MaxArrayLength)
        throw TooLongForArray(len, data.Position);
      if(len > ReadStep && !data.CanSeek)
        return ReadInSteps(data, (int)len);
      byte[] buffer = new byte[len];
      ReadExactly(data, buffer, (int)len);
      return buffer;
    }

    /// <summary>
    /// A header of a few bytes can claim 2 GB: without a known length the memory follows the data that really arrives.
    /// </summary>
    private static byte[] ReadInSteps(Stream data, int len) {
      byte[] buffer = new byte[ReadStep];
      int offset = 0;
      while(true) {
        int read = data.Read(buffer, offset, buffer.Length - offset);
        if(read <= 0)
          throw UnexpectedEnd(data);
        offset += read;
        if(offset == len)
          return buffer;
        if(offset == buffer.Length)
          Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, len));
      }
    }
  }

  [Flags]
  public enum SupportedLengths {
    Byte1 = 1,
    Short2 = 2,
    Int4 = 4,
    Long8 = 8,
    All = 15,
    FromShortUpward = 14,
    None = 0
  }
}
