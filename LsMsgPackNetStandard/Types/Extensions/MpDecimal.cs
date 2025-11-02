using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LsMsgPack.Types.Extensions
{
  public class MpDecimal : AbstractCustomExt<MpDecimal>
  {
    public static sbyte Default_TypeSpecifier = 1;

    private DecimalStruct value;

    public MpDecimal() : base() { } // TypeId = MsgPackTypeId.MpFExt16;

    public MpDecimal(MsgPackSettings settings):base(settings) { } // TypeId = MsgPackTypeId.MpFExt16; 

    public MpDecimal(MpExt ext) : this()
    {
      CopyBaseDataFrom(ext);
      value = new DecimalStruct(BaseValue);
    }

    public MpDecimal(MsgPackSettings settings, decimal value) : this(settings)
    {
      Value = value;
    }

    public override object Value
    {
      get
      {
        return value.value;
      }
      set
      {
        this.value = new DecimalStruct((decimal)value);
        BaseValue = this.value.ToBytes();
      }
    }

    protected override sbyte DefaultTypeSpecifier { get { return Default_TypeSpecifier; } }

    public override T GetTypedValue<T>()
    {
      return (T)(object)value.value;
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data)
    {
      base.Read(typeId, data);
      value = new DecimalStruct(BaseValue);
      return this;
    }

    public override byte[] ToBytes()
    {
      if (BaseValue is null || (BaseValue).Length != 16)
        BaseValue = value.ToBytes();

      return base.ToBytes();
    }

    public override string ToString()
    {
      return $"Decimal ({GetOfficialTypeName(TypeId)}) extension type {TypeSpecifier} with value {value.value.ToString()}";
    }

    public override bool SupportsType(Type type)
    {
      return type == typeof(decimal);
    }

    protected override void CopyBaseDataFrom(MpExt generic)
    {
      base.CopyBaseDataFrom(generic);
      this.value = new DecimalStruct(BaseValue);
    }

    [StructLayout(LayoutKind.Explicit)]
    private readonly struct DecimalStruct
    {
      [FieldOffset(0)] public readonly decimal value;

      [FieldOffset(0)] public readonly byte b01;
      [FieldOffset(1)] public readonly byte b02;
      [FieldOffset(2)] public readonly byte b03;
      [FieldOffset(3)] public readonly byte b04;
      [FieldOffset(4)] public readonly byte b05;
      [FieldOffset(5)] public readonly byte b06;
      [FieldOffset(6)] public readonly byte b07;
      [FieldOffset(7)] public readonly byte b08;
      [FieldOffset(8)] public readonly byte b09;
      [FieldOffset(9)] public readonly byte b10;
      [FieldOffset(10)] public readonly byte b11;
      [FieldOffset(11)] public readonly byte b12;
      [FieldOffset(12)] public readonly byte b13;
      [FieldOffset(13)] public readonly byte b14;
      [FieldOffset(14)] public readonly byte b15;
      [FieldOffset(15)] public readonly byte b16;

      [FieldOffset(0)] public readonly int Flags;
      [FieldOffset(4)] public readonly int Hi;
      [FieldOffset(8)] public readonly int Lo;
      [FieldOffset(12)] public readonly int Mid;

      [FieldOffset(0)] public readonly ulong FlagsHi;
      [FieldOffset(8)] public readonly ulong LoMid;

      public DecimalStruct(decimal val)
      {
        this = default;
        value = val;
      }

      public DecimalStruct(byte[] val)
      {
        this = default;
        b01 = val[0];
        b02 = val[1];
        b03 = val[2];
        b04 = val[3];
        b05 = val[4];
        b06 = val[5];
        b07 = val[6];
        b08 = val[7];
        b09 = val[8];
        b10 = val[9];
        b11 = val[10];
        b12 = val[11];
        b13 = val[12];
        b14 = val[13];
        b15 = val[14];
        b16 = val[15];
      }

      public byte[] ToBytes()
      {
        return new byte[16] {
        b01,
        b02,
        b03,
        b04,
        b05,
        b06,
        b07,
        b08,
        b09,
        b10,
        b11,
        b12,
        b13,
        b14,
        b15,
        b16
        };
      }
    }
  }
}
