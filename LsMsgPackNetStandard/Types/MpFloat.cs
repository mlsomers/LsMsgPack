using System;
using System.Collections.Generic;
using LsMsgPack.Meta;
using System.Runtime.InteropServices;

namespace LsMsgPack
{
  [Serializable]
  public class MpFloat : MsgPackItem
  {

    public MpFloat() : base() { }
    public MpFloat(MsgPackSettings settings) : base(settings) { }

    private MsgPackTypeId typeId = MsgPackTypeId.MpFloat;
    private float f32value;
    private double f64value;

    public override MsgPackTypeId TypeId
    {
      get { return typeId; }
    }

    public override object Value
    {
      get
      {
        switch (typeId)
        {
          case MsgPackTypeId.MpFloat: return f32value;
          case MsgPackTypeId.MpDouble: return f64value;
        }
        throw new MsgPackException($"Type {GetOfficialTypeName(typeId)} is not a floating point.", 0, typeId);
      }
      set
      {
        if (value is float)
        {
          typeId = MsgPackTypeId.MpFloat;
          f32value = (float)value;
          f64value = 0;
        }
        else if (value is double)
        {
          typeId = MsgPackTypeId.MpDouble;
          f64value = (double)value;
          f32value = 0;
        }
        else throw new MsgPackException("Only floating point types are allowed in MpFloat.");
      }
    }

    public override byte[] ToBytes()
    {
      ByteWriter bytes = new ByteWriter(9);
      WriteValue(bytes);
      return bytes.ToArray();
    }

    internal override void WriteTo(ByteWriter target)
    {
      if (GetType() != typeof(MpFloat)) // a derived type may override ToBytes
      {
        base.WriteTo(target);
        return;
      }
      WriteValue(target);
    }

    private void WriteValue(ByteWriter bytes)
    {
      bytes.Write((byte)typeId);
      if (typeId == MsgPackTypeId.MpFloat)
      {
        bytes.WriteEndian(new SingleBits(f32value).Bits, 4, Settings);
      }
      else
      {
        bytes.WriteEndian((ulong)BitConverter.DoubleToInt64Bits(f64value), 8, Settings);
      }
    }

    /// <summary>
    /// BitConverter.SingleToInt32Bits is not available in .NET Standard 2.0
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct SingleBits
    {
      [FieldOffset(0)] private readonly float value;
      [FieldOffset(0)] public readonly uint Bits;

      public SingleBits(float value)
      {
        Bits = 0;
        this.value = value;
      }
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, System.IO.Stream data)
    {
      this.typeId = typeId;
      byte[] buffer;

      if (this.typeId == MsgPackTypeId.MpFloat)
      {
        buffer = Settings.Buffers.Bytes4;
        ReadExactly(data, buffer, 4);
      }
      else
      {
        buffer = Settings.Buffers.Bytes8;
        ReadExactly(data, buffer, 8);
      }

      ReorderIfLittleEndian(Settings, buffer);

      if (this.typeId == MsgPackTypeId.MpFloat)
      {
        f32value = BitConverter.ToSingle(buffer, 0);
      }
      else
      {
        f64value = BitConverter.ToDouble(buffer, 0);
      }

      return this;
    }

    public override string ToString()
    {
      return $"Floating point ({GetOfficialTypeName(typeId)}) with the value {Value}";
    }
  }
}
