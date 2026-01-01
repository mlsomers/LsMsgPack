using System;
using System.Collections.Generic;

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
      List<byte> bytes = new List<byte>(9);
      if (typeId == MsgPackTypeId.MpFloat)
      {
        bytes.AddRange(BitConverter.GetBytes(f32value));
      }
      else
      {
        bytes.AddRange(BitConverter.GetBytes(f64value));
      }
      ReorderIfLittleEndian(Settings, bytes);
      bytes.Insert(0, (byte)typeId);
      return bytes.ToArray();
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, System.IO.Stream data)
    {
      this.typeId = typeId;
      byte[] buffer;

      if (this.typeId == MsgPackTypeId.MpFloat)
      {
        buffer = new byte[4];
        data.Read(buffer, 0, 4);
      }
      else
      {
        buffer = new byte[8];
        data.Read(buffer, 0, 8);
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
