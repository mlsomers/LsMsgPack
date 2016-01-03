using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.IO;
using System.Collections;
using System.Linq;

namespace LsMsgPack {
  [Serializable]
  public abstract class MsgPackItem {

    /// <summary>
    /// The type of information held in this structure.
    /// </summary>
    [XmlAttribute("TypeId", DataType = "byte")]
    public abstract MsgPackTypeId TypeId { get; }

    /// <summary>
    /// The actual piece of information held by this container.
    /// </summary>
    [XmlElement]
    public abstract object Value { get; set; }

    public abstract byte[] ToBytes();

    public abstract MsgPackItem Read(MsgPackTypeId typeId, Stream data);

    [XmlIgnore]
    public object Tag { get; set; }

    protected static void ReorderIfLittleEndian(List<byte> bytes) {
      if(BitConverter.IsLittleEndian && bytes.Count > 1) {
        byte[] swapped = new byte[bytes.Count];
        int c = 0;
        for(int t = swapped.Length - 1; t >= 0; t--) {
          swapped[t] = bytes[c];
          c++;
        }
        bytes.Clear();
        bytes.AddRange(swapped);
      }
    }

    protected static void ReorderIfLittleEndian(byte[] bytes) {
      if(BitConverter.IsLittleEndian && bytes.Length > 1) {
        byte[] swapped = new byte[bytes.Length];
        int c = 0;
        for(int t = swapped.Length - 1; t >= 0; t--) {
          swapped[t] = bytes[c];
          c++;
        }
        for(int t = bytes.Length - 1; t >= 0; t--) bytes[t] = swapped[t];
      }
    }

    public static MsgPackItem Pack(object value) {
      if(ReferenceEquals(value, null)) return new MpNull();
      if(value is bool) return new MpBool() { Value = value };
      if(value is sbyte
        || value is short
        || value is int
        || value is long
        || value is byte
        || value is ushort
        || value is uint
        || value is ulong) return new MpInt() { Value = value };
      if(value is float
        || value is double) return new MpFloat() { Value = value };
      if(value is string) return new MpString() { Value = value };
      if(value is byte[]) return new MpBin() { Value = value };
      if(value is object[]) return new MpArray() { Value = value };

      Type valuesType = value.GetType();

      if(valuesType.IsEnum) return new MpInt().SetEnumVal(value);
      if(IsSubclassOfArrayOfRawGeneric(typeof(KeyValuePair<,>), valuesType)) return new MpMap() { Value = value };
      if(IsSubclassOfRawGeneric(typeof(Dictionary<,>), valuesType)) return new MpMap() { Value = value };
      if(valuesType.IsArray) return new MpArray() { Value = ((IEnumerable)value).Cast<Object>().ToArray() };
      if(typeof(IEnumerable).IsAssignableFrom(valuesType)) return new MpArray() { Value = ((IEnumerable)value).Cast<Object>().ToArray() };

      // Extension types will come in like this most of the time:
      MsgPackItem val = value as MsgPackItem;
      if(!ReferenceEquals(val,null)) return val;

      return MsgPackSerializer.SerializeObject(value);
    }

    static protected bool IsSubclassOfRawGeneric(Type generic, Type toCheck) {
      while(toCheck != null && toCheck != typeof(object)) {
        var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
        if(generic == cur) {
          return true;
        }
        toCheck = toCheck.BaseType;
      }
      return false;
    }

    static protected bool IsSubclassOfArrayOfRawGeneric(Type generic, Type toCheck) {
      if(!toCheck.IsArray) return false;
      toCheck = toCheck.GetElementType();

      while(toCheck != null && toCheck != typeof(object)) {
        var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
        if(generic == cur) {
          return true;
        }
        toCheck = toCheck.BaseType;
      }
      return false;
    }
    
    public static MsgPackItem Unpack(byte[] data) {
      using(MemoryStream ms = new MemoryStream(data)) {
        return Unpack(ms);
      }
    }

    public static MsgPackItem Unpack(Stream stream) {
      int typeByte = stream.ReadByte();
      if(typeByte < 0) throw new MsgPackException("Unexpected end of data.", stream.Position);
      MsgPackItem item = null;
      try {
        MsgPackTypeId type = (MsgPackTypeId)typeByte;
        switch(type) {
          case MsgPackTypeId.MpNull: item = new MpNull(); break;
          case MsgPackTypeId.MpBoolFalse:
          case MsgPackTypeId.MpBoolTrue: item = new MpBool(); break;
          //case MsgPackTypes.MpBytePart:
          //case MsgPackTypes.MpSBytePart:
          case MsgPackTypeId.MpSByte:
          case MsgPackTypeId.MpShort:
          case MsgPackTypeId.MpInt:
          case MsgPackTypeId.MpLong:
          case MsgPackTypeId.MpUByte:
          case MsgPackTypeId.MpUShort:
          case MsgPackTypeId.MpUInt:
          case MsgPackTypeId.MpULong: item = new MpInt(); break;
          case MsgPackTypeId.MpFloat:
          case MsgPackTypeId.MpDouble: item = new MpFloat(); break;
          //case MsgPackTypeId.MpStr5:
          case MsgPackTypeId.MpStr8:
          case MsgPackTypeId.MpStr16:
          case MsgPackTypeId.MpStr32: item = new MpString(); break;
          case MsgPackTypeId.MpBin8:
          case MsgPackTypeId.MpBin16:
          case MsgPackTypeId.MpBin32: item = new MpBin(); break;
          //case MsgPackTypeId.MpArray4:
          case MsgPackTypeId.MpArray16:
          case MsgPackTypeId.MpArray32: item = new MpArray(); break;
          //case MsgPackTypeId.MpMap4:
          case MsgPackTypeId.MpMap16:
          case MsgPackTypeId.MpMap32: item = new MpMap(); break;
          case MsgPackTypeId.MpFExt1:
          case MsgPackTypeId.MpFExt2:
          case MsgPackTypeId.MpFExt4:
          case MsgPackTypeId.MpFExt8:
          case MsgPackTypeId.MpFExt16:
          case MsgPackTypeId.MpExt8:
          case MsgPackTypeId.MpExt16:
          case MsgPackTypeId.MpExt32: item = new MpExt(); break;
          case MsgPackTypeId.NeverUsed: throw new MsgPackException("The specification specifically states that the value 0xC1 should never be used.", 
            stream.Position-1, MsgPackTypeId.NeverUsed);
        }

        if(ReferenceEquals(item, null)) {
          if(((byte)type & 0xE0) == 0xE0 || (((byte)type & 0x80) == 0)) item = new MpInt();
          else if(((byte)type & 0xA0) == 0xA0) item = new MpString();
          else if(((byte)type & 0x90) == 0x90) item = new MpArray();
          else if(((byte)type & 0x80) == 0x80) item = new MpMap();
        }

        if(!ReferenceEquals(item, null)) {
          return item.Read(type, stream);
        } else {
          throw new MsgPackException(string.Concat("The type identifier with value 0x", BitConverter.ToString(new byte[] { (byte)type }),
            " is either new or invalid. It is not (yet) implemented in this version of LsMsgPack."), stream.Position, type);
        }
      }catch(Exception ex) {
        if(!(ex is MsgPackException)) {
          MsgPackException mpex = new MsgPackException("Error while reading data.", ex, stream.Position, (MsgPackTypeId)typeByte);
          if(!ReferenceEquals(item,null)) mpex.Data.Add("PartialMessage", item);
          throw mpex;
        } else throw;
      }
    }

    public virtual T GetTypedValue<T>() {
      return (T)Value;
    }

    public override string ToString() {
      return Value.ToString();
    }
    
    public static string GetOfficialTypeName(MsgPackTypeId typeId) {
      MsgPackMeta.PackDef def;
      if(MsgPackMeta.FromTypeId.TryGetValue(typeId, out def)) return def.OfficialName;
      //if(typeId == MsgPackTypeId.NeverUsed) return "[\"Officially never used\"] (0xC1)";
      return string.Concat("Undefined (0x", BitConverter.ToString(new byte[] { (byte)typeId }), ")");
    }

    internal static MsgPackMeta.PackDef GetTypeDescriptor(MsgPackTypeId typeId) {
      MsgPackMeta.PackDef def;
      if(MsgPackMeta.FromTypeId.TryGetValue(typeId, out def)) return def;
      return new MsgPackMeta.PackDef(typeId, string.Concat("Undefined (0x", BitConverter.ToString(new byte[] { (byte)typeId }), ")"), 
        "This value is either invalid or new to the specification since the implementation of this library. Check the specification and check for updates if the value is defined.");
    }
  }


  public enum MsgPackTypeId:byte {
    /// <summary>
    /// NULL
    /// </summary>
    MpNull = 0xc0,
    /// <summary>
    /// True
    /// </summary>
    MpBoolTrue = 0xc3,
    /// <summary>
    /// False
    /// </summary>
    MpBoolFalse = 0xc2,
    /// <summary>
    /// 5-bit negative (signed) number (up to 31)
    /// </summary>
    MpSBytePart = 0xE0,
    /// <summary>
    /// Unsigned up to 127
    /// </summary>
    MpBytePart = 0x00,
    /// <summary>
    /// Normal unsigned Byte
    /// </summary>
    MpUByte = 0xcc,
    /// <summary>
    /// Unsigned Short (UInt16)
    /// </summary>
    MpUShort = 0xcd,
    /// <summary>
    /// Unsigned UInt32
    /// </summary>
    MpUInt = 0xce,
    /// <summary>
    /// Unsigned UInt64
    /// </summary>
    MpULong = 0xcf,
    /// <summary>
    /// Signed Byte
    /// </summary>
    MpSByte = 0xd0,
    /// <summary>
    /// Signed Short (Int16)
    /// </summary>
    MpShort = 0xd1,
    /// <summary>
    /// Signd Int (int32)
    /// </summary>
    MpInt = 0xd2,
    /// <summary>
    /// Signed Long (Int64)
    /// </summary>
    MpLong = 0xd3,
    /// <summary>
    /// 32bit Float
    /// </summary>
    MpFloat = 0xca,
    /// <summary>
    /// 64bit Float
    /// </summary>
    MpDouble = 0xcb,
    /// <summary>
    /// String up to 31 bytes
    /// </summary>
    MpStr5 = 0xa0,
    /// <summary>
    /// String up to 255 bytes
    /// </summary>
    MpStr8 = 0xd9,
    /// <summary>
    /// String with a length (in bytes) that fits in 16 bits
    /// </summary>
    MpStr16 = 0xda,
    /// <summary>
    /// String with a length (in bytes) that fits in 32 bits
    /// </summary>
    MpStr32 = 0xdb,
    /// <summary>
    /// Byte array with less than 256 bytes
    /// </summary>
    MpBin8 = 0xc4,
    /// <summary>
    /// Byte array where the length fits in 16 bits
    /// </summary>
    MpBin16 = 0xc5,
    /// <summary>
    /// Byte array where the length fits in 32 bits
    /// </summary>
    MpBin32 = 0xc6,
    /// <summary>
    /// Array with less than 16 items
    /// </summary>
    MpArray4 = 0x90,
    /// <summary>
    /// Array where the number of items fits in 16 bits
    /// </summary>
    MpArray16 = 0xdc,
    /// <summary>
    /// Array where the number of items fits in 32 bits
    /// </summary>
    MpArray32 = 0xdd,
    /// <summary>
    /// Array of key-value pairs with less than 16 items
    /// </summary>
    MpMap4 = 0x80,
    /// <summary>
    /// Array of key-value pairs where the number of items fits in 16 bits
    /// </summary>
    MpMap16 = 0xde,
    /// <summary>
    /// Array of key-value pairs where the number of items fits in 32 bits
    /// </summary>
    MpMap32 = 0xdf,

    /// <summary>
    /// fixext 1 stores an Integer and a byte array whose length is 1 byte
    /// </summary>
    MpFExt1 = 0xd4,
    /// <summary>
    /// fixext 2 stores an integer and a byte array whose length is 2 bytes
    /// </summary>
    MpFExt2 = 0xd5,
    /// <summary>
    /// fixext 4 stores an integer and a byte array whose length is 4 bytes
    /// </summary>
    MpFExt4 = 0xd6,
    /// <summary>
    /// fixext 8 stores an integer and a byte array whose length is 8 bytes
    /// </summary>
    MpFExt8 = 0xd7,
    /// <summary>
    /// fixext 16 stores an integer and a byte array whose length is 16 bytes
    /// </summary>
    MpFExt16 = 0xd8,

    /// <summary>
    /// ext 8 stores an integer and a byte array whose length is upto (2^8)-1 bytes
    /// </summary>
    MpExt8 = 0xc7,
    /// <summary>
    /// ext 16 stores an integer and a byte array whose length is upto (2^16)-1 bytes
    /// </summary>
    MpExt16 = 0xc8,
    /// <summary>
    /// ext 32 stores an integer and a byte array whose length is upto (2^32)-1 bytes
    /// </summary>
    MpExt32 = 0xc9,

    /// <summary>
    /// An uninitialised ext might have this value, but it should actually never be used
    /// </summary>
    NeverUsed = 0xc1
  }
}
