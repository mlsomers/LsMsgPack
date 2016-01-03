using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.ComponentModel;

namespace LsMsgPack {
  [Serializable]
  public class MpInt : MsgPackItem {

    public MpInt() : base() { }
    public MpInt(MsgPackSettings settings):base(settings) { }
    
    private MsgPackTypeId typeId = MsgPackTypeId.MpBytePart;
    private ulong uvalue;
    private long svalue;

    public static readonly MsgPackTypeId[] SignedTypeIds = new MsgPackTypeId[] { MsgPackTypeId.MpSByte, MsgPackTypeId.MpShort, MsgPackTypeId.MpInt, MsgPackTypeId.MpLong };

    /// <summary>
    /// Note! will just return false if the value is positive
    /// </summary>
    private bool IsSigned() {
      if(_settings._dynamicallyCompact) return svalue < 0;
      if(((byte)typeId & 0xE0) == 0xE0) return true; // 5-bit negative integer
      if(((byte)typeId & 0x7F) == 0) return false; // 7-bit positive integer
      return SignedTypeIds.Contains(typeId);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    [XmlIgnore]
    public MsgPackTypeId PreservedType {
      get { return typeId; }
    }

    public override MsgPackTypeId TypeId {
      get {
        if(_settings._dynamicallyCompact) {
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
        } else {
          return typeId;
        }
      }
    }

    internal MpInt SetEnumVal(object value){ 
      Type valuesType = value.GetType();
      Type typ = Enum.GetUnderlyingType(valuesType);
      if(typ == typeof(int)) Value = (int)value;
      else if(typ == typeof(short)) Value = (short)value;
      else if(typ == typeof(byte)) Value = (byte)value;
      else if(typ == typeof(sbyte)) Value = (sbyte)value;
      else if(typ == typeof(long)) Value = (long)value;
      else if(typ == typeof(uint)) Value = (uint)value;
      else if(typ == typeof(ushort)) Value = (ushort)value;
      else if(typ == typeof(ulong)) Value = (ulong)value;
      else throw new MsgPackException(string.Concat("Unable to convert \"", value, "\" (\"", typ, "\") to an integer type"));
      return this;
    }

    public override object Value {
      get {
        switch(typeId) { 
          case MsgPackTypeId.MpSBytePart:
          case MsgPackTypeId.MpSByte:
            return Convert.ToSByte(svalue);
          case MsgPackTypeId.MpShort:
            return Convert.ToInt16(svalue);
          case MsgPackTypeId.MpInt:
            return Convert.ToInt32(svalue);
          case MsgPackTypeId.MpLong:
            return Convert.ToInt64(svalue);
          case MsgPackTypeId.MpBytePart:
          case MsgPackTypeId.MpUByte:
            return Convert.ToByte(uvalue);
          case MsgPackTypeId.MpUShort:
            return Convert.ToUInt16(uvalue);
          case MsgPackTypeId.MpUInt:
            return Convert.ToUInt32(uvalue);
          case MsgPackTypeId.MpULong:
            return Convert.ToUInt64(uvalue);
          default:
            if(svalue != 0) return svalue;
            return uvalue;
        }
      }
      set { // preseve original type in typeId
        if(value is sbyte) {
          typeId = ((sbyte)value >= -0x1F) && ((sbyte)value <= 0x1F) ? MsgPackTypeId.MpSBytePart : MsgPackTypeId.MpSByte;
          svalue = Convert.ToInt64(value);
          uvalue = 0;
        } else if(value is Int16) {
          typeId = MsgPackTypeId.MpShort;
          svalue = Convert.ToInt64(value);
          uvalue = 0;
        } else if(value is Int32) {
          typeId = MsgPackTypeId.MpInt;
          svalue = Convert.ToInt64(value);
          uvalue = 0;
        } else if(value is Int64) {
          typeId = MsgPackTypeId.MpLong;
          svalue = Convert.ToInt64(value);
          uvalue = 0;
        } else if(value is byte) {
          typeId = ((byte)value <= 0x7F) ? MsgPackTypeId.MpBytePart : MsgPackTypeId.MpUByte;
          svalue = 0;
          uvalue = Convert.ToUInt64(value);
        } else if(value is ushort) {
          typeId = MsgPackTypeId.MpUShort;
          svalue = 0;
          uvalue = Convert.ToUInt64(value);
        } else if(value is uint) {
          typeId = MsgPackTypeId.MpUInt;
          svalue = 0;
          uvalue = Convert.ToUInt64(value);
        } else if(value is ulong) {
          typeId = MsgPackTypeId.MpULong;
          svalue = 0;
          uvalue = Convert.ToUInt64(value);
        } else if(value.GetType().IsEnum) {
          SetEnumVal(value);
        } else throw new MsgPackException(string.Concat("Unable to convert \"", value, "\" to an integer type"));

        if(svalue>0 && uvalue == 0) {
          uvalue = (ulong)svalue;
        }
      }
    }

    public override T GetTypedValue<T>() {
      Type targetType = typeof(T);
      if(targetType.IsEnum) {
        return (T)Enum.ToObject(targetType, Value);
      }
      if(targetType == typeof(sbyte)
        || targetType == typeof(short)
        || targetType == typeof(int)
        || targetType == typeof(long)
        || targetType == typeof(byte)
        || targetType == typeof(ushort)
        || targetType == typeof(uint)
        || targetType == typeof(ulong)) {
        if(_settings._dynamicallyCompact) { // Direct cast may not be allowed in this case
          return (T)Convert.ChangeType(Value, typeof(T));
        } else {
          return (T)Value;
        }
      }
      return base.GetTypedValue<T>();
    }

    public override byte[] ToBytes() {
      MsgPackTypeId targetType = _settings._dynamicallyCompact ? TypeId : typeId;
      byte type = (byte)targetType;
      if(_settings._dynamicallyCompact || targetType == MsgPackTypeId.MpBytePart || targetType == MsgPackTypeId.MpSBytePart) {
        if((type & 0x7F) == 0) return new byte[1] { (byte)(type | Convert.ToByte(uvalue)) };
        if((type & 0xE0) == 0xE0) return new byte[1] { (byte)(type | BitConverter.GetBytes(svalue)[0]) }; 
      }
      List<byte> bytes = new List<byte>(9);

      switch(targetType) {
        case MsgPackTypeId.MpSByte:
          bytes.Add((byte)Convert.ToSByte(svalue));
          break;
        case MsgPackTypeId.MpShort:
          bytes.AddRange(BitConverter.GetBytes(Convert.ToInt16(svalue)));
          break;
        case MsgPackTypeId.MpInt:
          bytes.AddRange(BitConverter.GetBytes(Convert.ToInt32(svalue)));
          break;
        case MsgPackTypeId.MpLong:
          bytes.AddRange(BitConverter.GetBytes(Convert.ToInt64(svalue)));
          break;
        case MsgPackTypeId.MpUByte:
          bytes.Add(Convert.ToByte(uvalue));
          break;
        case MsgPackTypeId.MpUShort:
          bytes.AddRange(BitConverter.GetBytes(Convert.ToUInt16(uvalue)));
          break;
        case MsgPackTypeId.MpUInt:
          bytes.AddRange(BitConverter.GetBytes(Convert.ToUInt32(uvalue)));
          break;
        case MsgPackTypeId.MpULong:
          bytes.AddRange(BitConverter.GetBytes(Convert.ToUInt64(uvalue)));
          break;
      }

      ReorderIfLittleEndian(bytes);

      bytes.Insert(0, type);
      return bytes.ToArray();
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, System.IO.Stream data) {
      svalue = 0; // in case of reuse
      this.typeId = typeId;
      if(((byte)typeId & 0xE0) == 0xE0) { // 5-bit negative integer
        svalue = (sbyte)typeId;
        this.typeId = MsgPackTypeId.MpSBytePart;
        if(svalue > 0) uvalue = (ulong)svalue;
        return this;
      }
      if(((byte)typeId & 0x80) == 0) { // 7-bit positive integer
        uvalue = ((uint)((byte)typeId & 0x7F));
        this.typeId = MsgPackTypeId.MpBytePart;
        return this;
      }
      List<byte> bytes = new List<byte>(8);
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
          final[t] = bytes[c];
          c++;
        }
      } else final = bytes.ToArray();

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
      return string.Concat("Integer (", GetOfficialTypeName(typeId), ") with the value ", IsSigned() ? svalue.ToString() : uvalue.ToString());
    }
  }
}
