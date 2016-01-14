using System;
using System.Collections;
using System.Runtime.CompilerServices;
using HydraMF;

namespace LsMsgPackMicro {
  [Serializable]
  public class MpInt : MsgPackItem {
    
    private ulong uvalue;
    private long svalue;

    public static readonly MsgPackTypeId[] SignedTypeIds = new MsgPackTypeId[] { MsgPackTypeId.MpSByte, MsgPackTypeId.MpShort, MsgPackTypeId.MpInt, MsgPackTypeId.MpLong };

    /// <summary>
    /// Note! will just return false if the value is positive
    /// </summary>
    private bool IsSigned() {
      return svalue < 0;
    }
    
    public override MsgPackTypeId TypeId {
      get {
        if(IsSigned()) {
          if((svalue >= -0x1F) && ((svalue <= 0x1F))) return MsgPackTypeId.MpSBytePart;
          if((svalue >= sbyte.MinValue) && (svalue <= sbyte.MaxValue)) return MsgPackTypeId.MpSByte;
          if((svalue >= short.MinValue) && (svalue <= short.MaxValue)) return MsgPackTypeId.MpShort;
          if((svalue >= int.MinValue) && (svalue <= int.MaxValue)) return MsgPackTypeId.MpInt;
          return MsgPackTypeId.MpLong;
        } else {
          if(uvalue <= 0x7F) return MsgPackTypeId.MpBytePart;
          if(uvalue <= 255) return MsgPackTypeId.MpUByte;
          if(uvalue <= 0xFFFF) return MsgPackTypeId.MpUShort;
          if(uvalue <= 0xFFFFFFFF) return MsgPackTypeId.MpUInt;
          return MsgPackTypeId.MpULong;
        }
      }
    }

    internal MpInt SetEnumVal(object value){

      long val = long.Parse(value.ToString());
      Value = val;

      // After trying many things, had to revert to this common-case optimised trial-and-error hack
      /*
      try {
        svalue = (int)value;
        uvalue = 0;
      }catch {
        try {
          svalue = (short)value;
          uvalue = 0;
        } catch {
          try {
            svalue = 0;
            uvalue = (byte)value;
          } catch {
            try {
              svalue = (long)value;
              uvalue = 0;
            } catch {
              try {
                svalue = 0;
                uvalue = (uint)value;
              } catch {
                try {
                  svalue = 0;
                  uvalue = (ushort)value;
                } catch {
                  try {
                    svalue = 0;
                    uvalue = (ulong)value;
                  } catch {
                    try {
                      svalue = (sbyte)value;
                      uvalue = 0;
                    } catch {
                      throw new MsgPackException(string.Concat("Unable to convert \"", value, "\" to an integer type"));
                    }
                  }
                }
              }
            }
          }
        }
      }
      // */
      //Type typ = value.GetType();

      //if (typ.IsInstanceOfType(typeof(sbyte))) {
      //  svalue = (sbyte)value;
      //  uvalue = 0;
      //} else if (typ.IsInstanceOfType(typeof(short))) {
      //  svalue = (short)value;
      //  uvalue = 0;
      //} else if (typ.IsInstanceOfType(typeof(int))) {
      //  svalue = (int)value;
      //  uvalue = 0;
      //} else if (typ.IsInstanceOfType(typeof(long))) {
      //  svalue = (long)value;
      //  uvalue = 0;
      //} else if (typ.IsInstanceOfType(typeof(byte))) {
      //  svalue = 0;
      //  uvalue = (byte)value;
      //} else if (typ.IsInstanceOfType(typeof(ushort))) {
      //  svalue = 0;
      //  uvalue = (ushort)value;
      //} else if (typ.IsInstanceOfType(typeof(uint))) {
      //  svalue = 0;
      //  uvalue = (uint)value;
      //} else if (typ.IsInstanceOfType(typeof(ulong))) {
      //  svalue = 0;
      //  uvalue = (ulong)value;
      //} else throw new MsgPackException(string.Concat("Unable to convert \"", value, "\" to an integer type"));

      if (svalue > 0 && uvalue == 0) {
        uvalue = (ulong)svalue;
      }

      return this;
    }

    public static object CovertIntType(Type destType, object value) {
      if (destType == typeof(sbyte)) return sbyte.Parse(value.ToString());
      else if (destType == typeof(short)) return short.Parse(value.ToString());
      else if (destType == typeof(int)) return int.Parse(value.ToString());
      else if (destType == typeof(long)) return long.Parse(value.ToString());
      else if (destType == typeof(byte)) return byte.Parse(value.ToString());
      else if (destType == typeof(ushort)) return ushort.Parse(value.ToString());
      else if (destType == typeof(uint)) return uint.Parse(value.ToString());
      else if (destType == typeof(ulong)) return ulong.Parse(value.ToString());
      else throw new MsgPackException(string.Concat("Unable to convert \"", value, "\" to an integer type"));
    }

    public override object Value {
      get {
        switch (TypeId) {
          case MsgPackTypeId.MpSBytePart:
          case MsgPackTypeId.MpSByte:
            return (sbyte)svalue;
          case MsgPackTypeId.MpShort:
            return (short)svalue;
          case MsgPackTypeId.MpInt:
            return (int)svalue;
          case MsgPackTypeId.MpLong:
            return (long)svalue;
          case MsgPackTypeId.MpBytePart:
          case MsgPackTypeId.MpUByte:
            return (byte)uvalue;
          case MsgPackTypeId.MpUShort:
            return (ushort)uvalue;
          case MsgPackTypeId.MpUInt:
            return (uint)uvalue;
          case MsgPackTypeId.MpULong:
            return (ulong)uvalue;
          default:
            if (svalue != 0) return svalue;
            return uvalue;
        }
      }
      set { // preseve original type in typeId
        if (value is sbyte) {
          svalue = (sbyte)value;
          uvalue = 0;
        } else if (value is short) {
          svalue = (short)value;
          uvalue = 0;
        } else if (value is int) {
          svalue = (int)value;
          uvalue = 0;
        } else if (value is long) {
          svalue = (long)value;
          uvalue = 0;
        } else if (value is byte) {
          svalue = 0;
          uvalue = (byte)value;
        } else if (value is ushort) {
          svalue = 0;
          uvalue = (ushort)value;
        } else if (value is uint) {
          svalue = 0;
          uvalue = (uint)value;
        } else if (value is ulong) {
          svalue = 0;
          uvalue = (ulong)value;
        } else if (value.GetType().IsEnum) {
          SetEnumVal(value);
        } else throw new MsgPackException(string.Concat("Unable to convert \"", value, "\" to an integer type"));

        if (svalue > 0 && uvalue == 0) {
          uvalue = (ulong)svalue;
        }
      }
    }

    public override byte[] ToBytes() {
      MsgPackTypeId targetType = TypeId;
      byte type = (byte)targetType;
      if(targetType == MsgPackTypeId.MpBytePart || targetType == MsgPackTypeId.MpSBytePart) {
        if((type & 0x7F) == 0) return new byte[1] { (byte)(type | (Byte)uvalue) };
        if((type & 0xE0) == 0xE0) return new byte[1] { (byte)(type | BitConverter.GetBytes(svalue)[0]) }; 
      }
      ArrayList bytes = new ArrayList();

      switch(targetType) {
        case MsgPackTypeId.MpSByte:
          bytes.Add((byte)((sbyte)(svalue)));
          break;
        case MsgPackTypeId.MpShort:
          bytes.AddRange(BitConverter.GetBytes((short)svalue));
          break;
        case MsgPackTypeId.MpInt:
          bytes.AddRange(BitConverter.GetBytes((int)svalue));
          break;
        case MsgPackTypeId.MpLong:
          bytes.AddRange(BitConverter.GetBytes((long)svalue));
          break;
        case MsgPackTypeId.MpUByte:
          bytes.Add((byte)uvalue);
          break;
        case MsgPackTypeId.MpUShort:
          bytes.AddRange(BitConverter.GetBytes((ushort)uvalue));
          break;
        case MsgPackTypeId.MpUInt:
          bytes.AddRange(BitConverter.GetBytes((uint)uvalue));
          break;
        case MsgPackTypeId.MpULong:
          bytes.AddRange(BitConverter.GetBytes(uvalue));
          break;
      }

      ReorderIfLittleEndian(bytes);

      bytes.Insert(0, type);
      return (byte[])bytes.ToArray(typeof(byte));
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, System.IO.Stream data) {
      svalue = 0; // in case of reuse
      if(((byte)typeId & 0xE0) == 0xE0) { // 5-bit negative integer
        svalue = (sbyte)typeId;
        if(svalue > 0) uvalue = (ulong)svalue;
        return this;
      }
      if(((byte)typeId & 0x80) == 0) { // 7-bit positive integer
        uvalue = ((uint)((byte)typeId & 0x7F));
        return this;
      }
      ArrayList bytes = new ArrayList();
      switch((MsgPackTypeId)typeId) {
        case MsgPackTypeId.MpSByte:
          svalue = (sbyte)data.ReadByte();
          if(svalue > 0) uvalue = (ulong)svalue;
          return this;
        case MsgPackTypeId.MpUByte:
          uvalue = (byte)data.ReadByte();
          return this;

        case MsgPackTypeId.MpShort:
        case MsgPackTypeId.MpUShort:
          byte[] buffer=new byte[2];
          data.Read(buffer, 0, 2);
          bytes.AddRange(buffer);
          break;
        case MsgPackTypeId.MpInt:
        case MsgPackTypeId.MpUInt:
          buffer=new byte[4];
          data.Read(buffer, 0, 4);
          bytes.AddRange(buffer);
          break;
        case MsgPackTypeId.MpLong:
        case MsgPackTypeId.MpULong:
          buffer=new byte[8];
          data.Read(buffer, 0, 8);
          bytes.AddRange(buffer);
          break;
      }

      byte[] final;
      if(BitConverter.IsLittleEndian && bytes.Count > 1) {
        final = new byte[bytes.Count];
        int c = 0;
        for(int t = final.Length - 1; t >= 0; t--) {
          final[t] = (byte)bytes[c];
          c++;
        }
      } else final = (byte[])bytes.ToArray(typeof(byte));

      switch(typeId) {
        case MsgPackTypeId.MpShort:
          svalue = BitConverter.ToInt16(final, 0);
          break;
        case MsgPackTypeId.MpInt:
          svalue = BitConverter.ToInt32(final, 0);
          break;
        case MsgPackTypeId.MpLong:
          svalue = BitConverter.ToInt64(final, 0);
          break;
        case MsgPackTypeId.MpUShort:
          uvalue = BitConverter.ToUInt16(final, 0);
          break;
        case MsgPackTypeId.MpUInt:
          uvalue = BitConverter.ToUInt32(final, 0);
          break;
        case MsgPackTypeId.MpULong:
          uvalue = BitConverter.ToUInt64(final, 0);
          break;
        default:
          throw new MsgPackException(string.Concat("The type ", GetOfficialTypeName(typeId), " is not supported."), data.Position - 1, typeId);
      }
      if(svalue > 0) uvalue = (ulong)svalue;
      return this;
    }

    public static int GetByteLengthForType(MsgPackTypeId intType) {
      switch(intType) {
        case MsgPackTypeId.MpSBytePart:
        case MsgPackTypeId.MpBytePart: return 1;
        case MsgPackTypeId.MpSByte:
        case MsgPackTypeId.MpUByte: return 2;
        case MsgPackTypeId.MpShort:
        case MsgPackTypeId.MpUShort: return 3;
        case MsgPackTypeId.MpInt:
        case MsgPackTypeId.MpUInt: return 5;
        case MsgPackTypeId.MpLong:
        case MsgPackTypeId.MpULong: return 9;
      }
      throw new MsgPackException(string.Concat("The type ", GetOfficialTypeName(intType), " is not an integer type."), 0, intType);
    }

    public override string ToString() {
      return string.Concat("Integer (", GetOfficialTypeName(TypeId), ") with the value ", IsSigned() ? svalue.ToString() : uvalue.ToString());
    }
  }
}
