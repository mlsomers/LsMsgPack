using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LsMsgPack.Meta;
using System.Xml.Serialization;
#if KEEPTRACK
using System.ComponentModel;
#endif

namespace LsMsgPack {
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
      if(typeByte == Streaming.Escape.Byte) {
        code = data.ReadByte();
        if(code == Streaming.Escape.End) {
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
      while(true) {
        long start = data.Position;
        int b = data.ReadByte();
        if(b < 0)
          throw new MsgPackException($"Unexpected end of data: the {kind} of unknown length has no end (0xC1 0xC0).", data.Position, MsgPackTypeId.NeverUsed);
        if(b == Streaming.Escape.Byte) {
          int code = data.ReadByte();
          if(code == Streaming.Escape.End)
            return;
          if(code < 0)
            throw Streaming.Escape.AtEndOfData(start);
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
        if(content.Length + len > uint.MaxValue)
          throw new MsgPackException($"The {kind} of unknown length is longer than {uint.MaxValue} bytes, the most a {kind} can hold.", start, (MsgPackTypeId)b);
        byte[] raw = ReadBytes(data, len);
        content.Write(raw, 0, raw.Length);
#if KEEPTRACK
        if(chunks != null) {
          MsgPackVarLen chunk = text ? (MsgPackVarLen)new MpString(_settings) { Value = MpString.DefaultEncoding.GetString(raw) } : new MpBin(_settings) { Value = raw };
          chunk._depth = _depth + 1;
          chunk.SetStoredRange(start, data.Position - start);
          chunks.Add(chunk);
        }
#endif
      }
    }

    protected byte[] GetLengthBytes(long length, SupportedLengths supported) {
      // if(length < 0) return new byte[0];
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
    /// of the data cannot hold throws before anything is allocated (as LtMsgPack does). Under KEEPTRACK (the explorers show truncated data
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

    protected byte[] ReadBytes(Stream data, long len) {
      if(data.CanSeek && len > data.Length - data.Position) {
        // What reading would do (consume the rest, then fail), without allocating the claimed length first
        data.Seek(0, SeekOrigin.End);
        throw UnexpectedEnd(data);
      }
      byte[] buffer = new byte[len];
      if(len < int.MaxValue) { // TODO: implement reading larger portions.
        ReadExactly(data, buffer, (int)len);
      } else throw new MsgPackException(
        $"Not implemented. At this time we cannot read chunks larger than {int.MaxValue} bytes in one stread. This is a \"ToDo\" item.{data.Position}{TypeId}");
      return buffer;
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
