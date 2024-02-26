using System;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPack {
  public static class MsgPackMeta {

    public static readonly PackDef[] AllPacks;
    public static readonly Dictionary<MsgPackTypeId, PackDef> FromTypeId;
    public static readonly Dictionary<string, PackDef> FromName;

#if KEEPTRACK
    private static readonly HashSet<byte> ValidPackageStartBytes;
#endif

    public class PackDef {
      public PackDef(MsgPackTypeId typeId, string officialName, string description) {
        OfficialName = officialName;
        Description = description;
        TypeId = typeId;
      }
      public string OfficialName;
      public string Description;
      public MsgPackTypeId TypeId;
    }

    public static readonly MsgPackTypeId[] IntTypeFamily;
    public static readonly MsgPackTypeId[] StrTypeFamily;
    public static readonly MsgPackTypeId[] BoolTypeFamily;
    public static readonly MsgPackTypeId[] FloatTypeFamily;
    public static readonly MsgPackTypeId[] BinTypeFamily;
    public static readonly MsgPackTypeId[] ArrayTypeFamily;
    public static readonly MsgPackTypeId[] MapTypeFamily;
    public static readonly MsgPackTypeId[] ExtTypeFamily;

    private static readonly MsgPackTypeId[][] typeFamilys;

    static MsgPackMeta() {
      AllPacks = new PackDef[] {
       new PackDef(MsgPackTypeId.MpNull,     "nil", "Unassigned (null) value"),

       new PackDef(MsgPackTypeId.MpBoolFalse,"false","Boolean false"),
       new PackDef(MsgPackTypeId.MpBoolTrue, "true", "Boolean true"),

       new PackDef(MsgPackTypeId.MpBytePart, "positive fixint", "7-bit positive integer (part of a byte)"),
       new PackDef(MsgPackTypeId.MpSBytePart,"negative fixnum", "5-bit negative integer (part of an sbyte)"),
       new PackDef(MsgPackTypeId.MpUByte,    "uint 8", "8-bit unsigned integer (byte)"),
       new PackDef(MsgPackTypeId.MpUShort,   "uint 16", "16-bit big-endian unsigned integer (ushort)"),
       new PackDef(MsgPackTypeId.MpUInt,     "uint 32", "32-bit big-endian unsigned integer (uint)"),
       new PackDef(MsgPackTypeId.MpULong,    "uint 64", "64-bit big-endian unsigned integer (ulong)"),
       new PackDef(MsgPackTypeId.MpSByte,    "int 8", "8-bit signed integer (sbyte)"),
       new PackDef(MsgPackTypeId.MpShort,    "int 16", "16-bit big-endian signed integer (short)"),
       new PackDef(MsgPackTypeId.MpInt,      "int 32", "32-bit big-endian signed integer (int)"),
       new PackDef(MsgPackTypeId.MpLong,     "int 64", "64-bit big-endian signed integer (long)"),

       new PackDef(MsgPackTypeId.MpFloat,    "float 32", "floating point number in IEEE 754 single precision floating point number format (float)"),
       new PackDef(MsgPackTypeId.MpDouble,   "float 64", "floating point number in IEEE 754 double precision floating point number format (double)"),

       new PackDef(MsgPackTypeId.MpStr5,     "fixstr", "byte array whose length is upto 31 bytes (string)"),
       new PackDef(MsgPackTypeId.MpStr8,     "str 8", "byte array whose length is upto (2^8)-1 (=255) bytes (string)"),
       new PackDef(MsgPackTypeId.MpStr16,    "str 16", "byte array whose length is upto (2^16)-1 bytes (string)"),
       new PackDef(MsgPackTypeId.MpStr32,    "str 32", "byte array whose length is upto (2^32)-1 bytes (string)"),

       new PackDef(MsgPackTypeId.MpBin8,     "bin 8", "byte array whose length is upto (2^8)-1 (=255) bytes (byte[])"),
       new PackDef(MsgPackTypeId.MpBin16,    "bin 16", "byte array whose length is upto (2^16)-1 bytes (byte[])"),
       new PackDef(MsgPackTypeId.MpBin32,    "bin 32", "byte array whose length is upto (2^32)-1 bytes (byte[])"),

       new PackDef(MsgPackTypeId.MpArray4,   "fixarray", "array whose length is upto 15 elements (obect[])"),
       new PackDef(MsgPackTypeId.MpArray16,  "array 16", "array whose length is upto (2^16)-1 elements (obect[])"),
       new PackDef(MsgPackTypeId.MpArray32,  "array 32", "array whose length is upto (2^32)-1 elements (obect[])"),

       new PackDef(MsgPackTypeId.MpMap4,     "fixmap", "map whose length is upto 15 elements (KeyValuePair<,>[])"),
       new PackDef(MsgPackTypeId.MpMap16,    "map 16", "map whose length is upto (2^16)-1 elements (KeyValuePair<,>[])"),
       new PackDef(MsgPackTypeId.MpMap32,    "map 32", "map whose length is upto (2^32)-1 elements (KeyValuePair<,>[])"),

       new PackDef(MsgPackTypeId.MpFExt1,    "fixext 1", "integer and a byte array whose length is 1 byte"),
       new PackDef(MsgPackTypeId.MpFExt2,    "fixext 2", "integer and a byte array whose length is 2 bytes"),
       new PackDef(MsgPackTypeId.MpFExt4,    "fixext 4", "integer and a byte array whose length is 4 bytes"),
       new PackDef(MsgPackTypeId.MpFExt8,    "fixext 8", "integer and a byte array whose length is 8 bytes"),
       new PackDef(MsgPackTypeId.MpFExt16,   "fixext 16", "integer and a byte array whose length is 16 bytes"),

       new PackDef(MsgPackTypeId.MpExt8,     "ext 8", "integer and a byte array whose length is upto (2^8)-1 (=255) bytes"),
       new PackDef(MsgPackTypeId.MpExt16,    "ext 16", "integer and a byte array whose length is upto (2^16)-1 bytes"),
       new PackDef(MsgPackTypeId.MpExt32,    "ext 32", "integer and a byte array whose length is upto (2^32)-1 bytes"),

       new PackDef(MsgPackTypeId.NeverUsed,  "(never used)", "a specific value that is explicitally not allowed to be used according to the specifications")
      };

      FromTypeId = new Dictionary<MsgPackTypeId, PackDef>(AllPacks.Length);
      for(int t = AllPacks.Length - 1; t >= 0; t--) FromTypeId.Add(AllPacks[t].TypeId, AllPacks[t]);
      FromName = new Dictionary<string, PackDef>(AllPacks.Length);
      for(int t = AllPacks.Length - 1; t >= 0; t--) FromName.Add(AllPacks[t].OfficialName, AllPacks[t]);

      IntTypeFamily = new MsgPackTypeId[]{
          MsgPackTypeId.MpBytePart,
          MsgPackTypeId.MpSBytePart,
          MsgPackTypeId.MpUByte,
          MsgPackTypeId.MpUShort,
          MsgPackTypeId.MpUInt,
          MsgPackTypeId.MpULong,
          MsgPackTypeId.MpSByte,
          MsgPackTypeId.MpShort,
          MsgPackTypeId.MpInt,
          MsgPackTypeId.MpLong,
        };
      StrTypeFamily = new MsgPackTypeId[] {
          MsgPackTypeId.MpStr5,
          MsgPackTypeId.MpStr8,
          MsgPackTypeId.MpStr16,
          MsgPackTypeId.MpStr32
        };
      BoolTypeFamily = new MsgPackTypeId[]{
        MsgPackTypeId.MpBoolFalse,
          MsgPackTypeId.MpBoolTrue
        };
      FloatTypeFamily = new MsgPackTypeId[] {
          MsgPackTypeId.MpFloat,
          MsgPackTypeId.MpDouble
        };
      BinTypeFamily = new MsgPackTypeId[] {
          MsgPackTypeId.MpBin8,
          MsgPackTypeId.MpBin16,
          MsgPackTypeId.MpBin32
        };
      ArrayTypeFamily = new MsgPackTypeId[] {
          MsgPackTypeId.MpArray4,
          MsgPackTypeId.MpArray16,
          MsgPackTypeId.MpArray32
        };
      MapTypeFamily = new MsgPackTypeId[] {
          MsgPackTypeId.MpMap4,
          MsgPackTypeId.MpMap16,
          MsgPackTypeId.MpMap32
        };
      ExtTypeFamily = new MsgPackTypeId[] {
          MsgPackTypeId.MpFExt1,
          MsgPackTypeId.MpFExt2,
          MsgPackTypeId.MpFExt4,
          MsgPackTypeId.MpFExt8,
          MsgPackTypeId.MpFExt16,
          MsgPackTypeId.MpExt8,
          MsgPackTypeId.MpExt16,
          MsgPackTypeId.MpExt32
        };

      typeFamilys = new MsgPackTypeId[][] {
        BoolTypeFamily,
        IntTypeFamily,
        FloatTypeFamily,
        StrTypeFamily,
        BinTypeFamily,
        ArrayTypeFamily,
        MapTypeFamily,
        ExtTypeFamily
      };

#if KEEPTRACK
      ValidPackageStartBytes = new HashSet<byte>(Enum.GetValues(typeof(MsgPackTypeId)).Cast<byte>());
      ValidPackageStartBytes.Remove((byte)MsgPackTypeId.NeverUsed);
#endif
    }

#if KEEPTRACK
    public static bool IsValidPackageStartByte(byte b) {
      if(ValidPackageStartBytes.Contains(b)) return true;
      if((b & 0xE0) == 0xE0 || ((b & 0x80) == 0)) return true; // int
      else if((b & 0xA0) == 0xA0) return true; // string
      else if((b & 0x90) == 0x90) return true; // array
      else if((b & 0x80) == 0x80) return true; // map
      return false;
    }
#endif

    public static bool AreInSameFamily(MsgPackTypeId a, MsgPackTypeId b, bool NullIsEqual = true) {
      if(a == b) return true;
      if(a == MsgPackTypeId.MpNull || b == MsgPackTypeId.MpNull) return NullIsEqual;
      for(int t = typeFamilys.GetLength(0) - 1; t >= 0; t--) {
        if(typeFamilys[t].Contains(a)) return typeFamilys[t].Contains(b);
      }
      return false;
    }

  }
}
