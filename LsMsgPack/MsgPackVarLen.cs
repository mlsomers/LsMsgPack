using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;

namespace LsMsgPack {
  [Serializable]
  public abstract class MsgPackVarLen: MsgPackItem {

    public MsgPackVarLen() : base() { }
    public MsgPackVarLen(MsgPackSettings settings):base(settings) { }

    protected abstract MsgPackTypeId GetTypeId(long len);

    [XmlIgnore]
    [Category("MetaData")]
    [DisplayName("Count")]
    [Description("The number of items in this collection.")]
    public abstract int Count { get; }

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
      byte[] buffer = new byte[bytes];
      data.Read(buffer, 0, bytes);
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
      throw new MsgPackException(string.Concat("Only 1, 2, 4 or 8 byte lenths are allowed. ", bytes.ToString(CultureInfo.InvariantCulture), " is not implemented."), data.Position, TypeId);
    }

    protected byte[] ReadBytes(Stream data, long len) {
      byte[] buffer = new byte[len];
      if(len < int.MaxValue) { // TODO: implement reading larger portions.
        data.Read(buffer, 0, (int)len);
      } else throw new MsgPackException(string.Concat("Not implemented. At this time we cannot read chunks larger than ", int.MaxValue, " bytes in one stread. This is a \"ToDo\" item.", data.Position, TypeId));
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
