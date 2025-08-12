﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
#if KEEPTRACK
using System.Xml.Serialization;
using System.ComponentModel;
#endif

namespace LsMsgPack {
  public class MpExt: MsgPackVarLen {

    public MpExt() : base() { }
    public MpExt(MsgPackSettings settings) : base(settings) { }

    public static readonly MsgPackTypeId[] VarLenExtTypes = new MsgPackTypeId[] { MsgPackTypeId.MpExt8, MsgPackTypeId.MpExt16, MsgPackTypeId.MpExt32 };

    protected override MsgPackTypeId GetTypeId(long len) {
      if(len <= 0) return MsgPackTypeId.MpExt8;
      if(len == 1) return MsgPackTypeId.MpFExt1;
      if(len == 2) return MsgPackTypeId.MpFExt2;
      if(len == 3) return MsgPackTypeId.MpExt8;
      if(len == 4) return MsgPackTypeId.MpFExt4;
      if(len <= 7) return MsgPackTypeId.MpExt8;
      if(len == 8) return MsgPackTypeId.MpFExt8;
      if(len <= 15) return MsgPackTypeId.MpExt8;
      if(len == 16) return MsgPackTypeId.MpFExt16;
      if(len <= 255) return MsgPackTypeId.MpExt8;
      if(len <= ushort.MaxValue) return MsgPackTypeId.MpExt16;
      if(len <= uint.MaxValue) return MsgPackTypeId.MpExt32;
      throw new MsgPackException(string.Concat("Cannot store more than ", uint.MaxValue, " bytes in an Ext package."),0, MsgPackTypeId.MpExt32);
    }

    private MsgPackTypeId typeId = MsgPackTypeId.NeverUsed;
    private sbyte typeSpecifier = 0;
    private byte[] value;

    public override int Count {
      get { return value.Length; }
    }

    ///<summary>
    /// The type Extension type assigned to this container
    /// </summary>
#if KEEPTRACK
    [XmlAttribute("TypeSpecifier", DataType = "byte")]
    [Category("Data")]
    [DisplayName("Type")]
    [Description("The type Extension type assigned to this container")]
    [ReadOnly(true)]
#endif
    public sbyte TypeSpecifier {
      get { return typeSpecifier; }
      set { typeSpecifier = value; }
    }
    
    public override MsgPackTypeId TypeId {
      get {
        return typeId;
      }
    }

    public override object Value {
      get {
        if(!ReferenceEquals(value,null)) return value;
        return new byte[0];
      }
      set {
        byte[] bytes = value as byte[];
        this.value = bytes;
        if(ReferenceEquals(bytes, null)) return;
        if(TypeId == MsgPackTypeId.NeverUsed) typeId = GetTypeId(this.value.Length);
      }
    }

    protected byte[] BaseValue {
      get { return value ?? new byte[0]; }
      set { this.value = value; }
    }

    public override byte[] ToBytes() {
      List<byte> bytes = new List<byte>(value.Length + 6); // current max length limit is 4 bytes + specifier + identifier

#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
      if (typeId == MsgPackTypeId.NeverUsed) typeId = GetTypeId(value.LongLength);
#else
      if (typeId == MsgPackTypeId.NeverUsed) typeId = GetTypeId(value.Length);
#endif
      bytes.Add((byte)typeId);
      if(VarLenExtTypes.Contains(typeId)) {

#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
        bytes.AddRange(GetLengthBytes(value.LongLength, SupportedLengths.All));
#else
        bytes.AddRange(GetLengthBytes(value.Length, SupportedLengths.All));
#endif
      }
      bytes.Add((byte)typeSpecifier);
      bytes.AddRange(value);
      return bytes.ToArray();
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data) {
      this.typeId = typeId;
      long len;
      switch(typeId) {
        case MsgPackTypeId.MpFExt1 : len = 1; break;
        case MsgPackTypeId.MpFExt2 : len = 2; break;
        case MsgPackTypeId.MpFExt4 : len = 4; break;
        case MsgPackTypeId.MpFExt8 : len = 8; break;
        case MsgPackTypeId.MpFExt16: len = 16; break;
        case MsgPackTypeId.MpExt8:   len = ReadLen(data, 1); break;
        case MsgPackTypeId.MpExt16:  len = ReadLen(data, 2); break;
        case MsgPackTypeId.MpExt32:  len = ReadLen(data, 4); break;
        default: throw new MsgPackException(string.Concat("Ext does not support a type ID of ", GetOfficialTypeName(typeId), "."), data.Position-1, typeId);
      }
      typeSpecifier = (sbyte)data.ReadByte();
      value = ReadBytes(data, len);

      if (typeSpecifier == -1)
        return new MpDateTime(this);

      return this;
    }

    public override string ToString() {
      return string.Concat("Extension value (", GetOfficialTypeName(typeId),
        ") with a type specifier of ", typeSpecifier, " containing ", value.Length, " bytes.");
    }

    protected void CopyBaseDataFrom(MpExt generic) {
#if KEEPTRACK
      storedOffset = generic.storedOffset;
      storedLength = generic.storedLength;
#endif
      _settings = generic._settings;
      typeId = generic.typeId;
      typeSpecifier = generic.typeSpecifier;
      value = generic.value;
    }
  }
}
