using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.IO;
using System.ComponentModel;
using System.Collections;
using System.Linq;

namespace LsMsgPack
{
  [Serializable]
  [DefaultProperty("Value")]
  public abstract class MsgPackItem
  {

    public MsgPackItem() : base() { _settings = new MsgPackSettings(); }
    public MsgPackItem(MsgPackSettings settings)
    {
      _settings = settings;
      _isBestGuess = _settings.FileContainsErrors;
    }

    protected long storedOffset;
    protected long storedLength;
    protected MsgPackSettings _settings;

    [XmlIgnore]
    [Category("Control")]
    [DisplayName("Settings")]
    [Description("Settings belonging to this instance.")]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MsgPackSettings Settings { get { return _settings; } }

    private bool _isBestGuess = false;
    [XmlAttribute("Unreliable")]
    [DefaultValue(false)]
    [Category("MetaData")]
    [DisplayName("Is best guess")]
    [Description("If this is True, then a breaking error has preceded this item and the rest of the file may or may not be read correctly. Such items are not suitable for production use but may aid in debugging situations.")]
    public bool IsBestGuess
    {
      get { return _isBestGuess; }
    }

    /// <summary>
    /// The type of information held in this structure.
    /// </summary> 
    [XmlAttribute("TypeId", DataType = "byte")]
    [Category("MetaData")]
    [DisplayName("Type")]
    [Description("The type of information held in this structure.")]
    [TypeConverter(typeof(MsgPackTypeConverter))]
    [ReadOnly(true)]
    [Browsable(true)]
    public abstract MsgPackTypeId TypeId { get; }

    /// <summary>
    /// The actual piece of information held by this container.
    /// </summary>
    [XmlElement]
    [Category("Data")]
    [DisplayName("Data")]
    [Description("The actual piece of information held by this container.")]
    public abstract object Value { get; set; }

    public abstract byte[] ToBytes();

    public abstract MsgPackItem Read(MsgPackTypeId typeId, Stream data);

    [XmlIgnore]
    [Category("MetaData")]
    [DisplayName("Offset")]
    [Description("The number of bytes (0 bsaed) from the start of the file to the first byte of this node (is determined while reading).")]
    public long StoredOffset
    {
      get { return storedOffset; }
    }

    [XmlIgnore]
    [Category("MetaData")]
    [DisplayName("Length")]
    [Description("The number of bytes that this package occupied (is set determined reading).")]
    public long StoredLength
    {
      get { return storedLength; }
    }

    [XmlIgnore]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public object Tag { get; set; }

    public static bool SwapEndianChoice(MsgPackSettings settings, int length)
    {
      if (settings.EndianAction == EndianAction.NeverSwap || length <= 1)
        return false;
      if (settings.EndianAction == EndianAction.SwapIfCurrentSystemIsLittleEndian && !BitConverter.IsLittleEndian)
        return false;
      return true;
    }

    protected static void ReorderIfLittleEndian(MsgPackSettings settings, List<byte> bytes)
    {
      if (!SwapEndianChoice(settings, bytes.Count))
        return;
      byte[] swapped = new byte[bytes.Count];
      int c = 0;
      for (int t = swapped.Length - 1; t >= 0; t--)
      {
        swapped[t] = bytes[c];
        c++;
      }
      bytes.Clear();
      bytes.AddRange(swapped);
    }

    protected static void ReorderIfLittleEndian(MsgPackSettings settings, byte[] bytes)
    {
      if (!SwapEndianChoice(settings, bytes.Length))
        return;

      byte[] swapped = new byte[bytes.Length];
      int c = 0;
      for (int t = swapped.Length - 1; t >= 0; t--)
      {
        swapped[t] = bytes[c];
        c++;
      }
      for (int t = bytes.Length - 1; t >= 0; t--) bytes[t] = swapped[t];
    }

    protected static byte[] SwapIfLittleEndian(MsgPackSettings settings, byte[] bytes)
    {
      if (!SwapEndianChoice(settings, bytes.Length))
        return bytes;

      byte[] final = new byte[bytes.Length];
      int c = 0;
      for (int t = final.Length - 1; t >= 0; t--)
      {
        final[t] = bytes[c];
        c++;
      }

      return final;
    }

    protected static byte[] SwapIfLittleEndian(MsgPackSettings settings, byte[] bytes, int start, int count)
    {
      if (bytes.Length <= 1)
        return bytes;

      byte[] final = new byte[count];
      int last = count - 1;

      if (!SwapEndianChoice(settings, count))
      {
        int offset = start + last;
        for (int t = last; t >= 0; t--)
        {
          final[t] = bytes[offset];
          offset--;
        }
        return final;
      }

      int c = start;
      for (int t = last; t >= 0; t--)
      {
        final[t] = bytes[c];
        c++;
      }

      return final;
    }

    /// <param name="dynamicallyCompact">Will store a long with value 3 as a nibble (using only one byte)</param>
    public static MpRoot PackMultiple(bool dynamicallyCompact, params object[] values)
    {
      return PackMultiple(new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact }, values);
    }

    public static MpRoot PackMultiple(MsgPackSettings settings, params object[] values)
    {
      MpRoot root = new MpRoot(settings, values.Length);
      for (int t = 0; t < values.Length; t++)
        root.Add(Pack(values[t], settings));
      return root;
    }

    /// <param name="dynamicallyCompact">Will store a long with value 3 as a nibble (using only one byte)</param>
    public static MpRoot PackMultiple(bool dynamicallyCompact, IEnumerable values)
    {
      return PackMultiple(new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact }, values);
    }

    public static MpRoot PackMultiple(MsgPackSettings settings, IEnumerable values)
    {
      MpRoot root = new MpRoot(settings);
      foreach (object item in values)
      {
        root.Add(Pack(item, settings));
      }
      return root;
    }

    public static MsgPackItem Pack(object value, bool dynamicallyCompact = true)
    {
      return Pack(value, new MsgPackSettings() { DynamicallyCompact = dynamicallyCompact });
    }

    public static MsgPackItem Pack(object value, MsgPackSettings settings)
    {
      if (ReferenceEquals(value, null)) return new MpNull(settings);
      if (value is bool) return new MpBool(settings) { Value = value };
      if (value is sbyte
        || value is short
        || value is int
        || value is long
        || value is byte
        || value is ushort
        || value is uint
        || value is ulong) return new MpInt(settings) { Value = value };
      if (value is float
        || value is double) return new MpFloat(settings) { Value = value };
      if (value is string) return new MpString(settings) { Value = value };
      if (value is byte[]
        || value is Guid) return new MpBin(settings) { Value = value };
      if (value is object[]) return new MpArray(settings) { Value = value };
      if (value is DateTime
        || value is DateTimeOffset) return new MpDateTime(settings) { Value = value };

      Type valuesType = value.GetType();

      if (valuesType.IsEnum) return new MpInt(settings).SetEnumVal(value);
      if (IsSubclassOfArrayOfRawGeneric(typeof(KeyValuePair<,>), valuesType)) return new MpMap(settings) { Value = value };
      if (IsSubclassOfRawGeneric(typeof(Dictionary<,>), valuesType)) return new MpMap(settings) { Value = value };
      if (valuesType.IsArray) return new MpArray(settings) { Value = ((IEnumerable)value).Cast<Object>().ToArray() };
      if (typeof(IEnumerable).IsAssignableFrom(valuesType)) return new MpArray(settings) { Value = ((IEnumerable)value).Cast<Object>().ToArray() };

      // Extension types will come in like this most of the time:
      MsgPackItem val = value as MsgPackItem;
      if (!ReferenceEquals(val, null))
      {
        val._settings = settings;
        return val;
      }

      return MsgPackSerializer.SerializeObject(value, settings);
    }

    static protected bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
    {
      while (toCheck != null && toCheck != typeof(object))
      {
        var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
        if (generic == cur)
        {
          return true;
        }
        toCheck = toCheck.BaseType;
      }
      return false;
    }

    static protected bool IsSubclassOfArrayOfRawGeneric(Type generic, Type toCheck)
    {
      if (!toCheck.IsArray) return false;
      toCheck = toCheck.GetElementType();

      while (toCheck != null && toCheck != typeof(object))
      {
        var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
        if (generic == cur)
        {
          return true;
        }
        toCheck = toCheck.BaseType;
      }
      return false;
    }

    public static MsgPackItem Unpack(byte[] data, bool dynamicallyCompact = true, bool preservePackages = false, bool continueProcessingOnBreakingError = false)
    {
      using (MemoryStream ms = new MemoryStream(data))
      {
        return Unpack(ms, dynamicallyCompact, preservePackages, continueProcessingOnBreakingError);
      }
    }

    public static MsgPackItem Unpack(Stream stream, bool dynamicallyCompact = true, bool preservePackages = false, bool continueProcessingOnBreakingError = false)
    {
      return Unpack(stream, new MsgPackSettings()
      {
        DynamicallyCompact = dynamicallyCompact,
        PreservePackages = preservePackages,
        ContinueProcessingOnBreakingError = continueProcessingOnBreakingError
      });
    }

    public static MpRoot UnpackMultiple(byte[] data, bool dynamicallyCompact = true, bool preservePackages = false, bool continueProcessingOnBreakingError = false)
    {
      using (MemoryStream ms = new MemoryStream(data))
      {
        return UnpackMultiple(ms, dynamicallyCompact, preservePackages, continueProcessingOnBreakingError);
      }
    }

    public static MpRoot UnpackMultiple(Stream stream, bool dynamicallyCompact = true, bool preservePackages = false, bool continueProcessingOnBreakingError = false)
    {
      return UnpackMultiple(stream, new MsgPackSettings()
      {
        DynamicallyCompact = dynamicallyCompact,
        PreservePackages = preservePackages,
        ContinueProcessingOnBreakingError = continueProcessingOnBreakingError
      });
    }

    public static MpRoot UnpackMultiple(byte[] data, MsgPackSettings settings)
    {
      using (MemoryStream ms = new MemoryStream(data))
      {
        return UnpackMultiple(ms, settings);
      }
    }

    public static MpRoot UnpackMultiple(Stream stream, MsgPackSettings settings)
    {
      MpRoot items = new MpRoot(settings) { storedOffset = stream.Position };
      long len = stream.Length - 1;
      long lastpos = stream.Position;
      while (stream.Position < len)
      {
        try
        {
          items.Add(Unpack(stream, settings));
          lastpos = stream.Position;
        }
        catch (Exception ex)
        {
          items.Add(new MpError(settings, ex, "Offset after parsing error is ", stream.Position) { storedOffset = lastpos, storedLength = stream.Position - lastpos });
          if (settings.ContinueProcessingOnBreakingError)
          {
            if (lastpos == stream.Position && stream.Position < len)
              FindNextValidTypeId(stream);
          }
          else
          {
            break;
          }
        }
      }

      items.storedLength = stream.Position - items.storedOffset;
      return items;
    }

    public static MsgPackItem Unpack(Stream stream, MsgPackSettings settings)
    {
      int typeByte = stream.ReadByte();
      if (typeByte < 0) return new MpError(settings, stream.Position, MsgPackTypeId.NeverUsed, "Unexpected end of data.");
      MsgPackItem item = null;
      try
      {
        MsgPackTypeId type = (MsgPackTypeId)typeByte;
        switch (type)
        {
          case MsgPackTypeId.MpNull: item = new MpNull(settings); break;
          case MsgPackTypeId.MpBoolFalse:
          case MsgPackTypeId.MpBoolTrue: item = new MpBool(settings); break;
          //case MsgPackTypes.MpBytePart:
          //case MsgPackTypes.MpSBytePart:
          case MsgPackTypeId.MpSByte:
          case MsgPackTypeId.MpShort:
          case MsgPackTypeId.MpInt:
          case MsgPackTypeId.MpLong:
          case MsgPackTypeId.MpUByte:
          case MsgPackTypeId.MpUShort:
          case MsgPackTypeId.MpUInt:
          case MsgPackTypeId.MpULong: item = new MpInt(settings); break;
          case MsgPackTypeId.MpFloat:
          case MsgPackTypeId.MpDouble: item = new MpFloat(settings); break;
          //case MsgPackTypeId.MpStr5:
          case MsgPackTypeId.MpStr8:
          case MsgPackTypeId.MpStr16:
          case MsgPackTypeId.MpStr32: item = new MpString(settings); break;
          case MsgPackTypeId.MpBin8:
          case MsgPackTypeId.MpBin16:
          case MsgPackTypeId.MpBin32: item = new MpBin(settings); break;
          //case MsgPackTypeId.MpArray4:
          case MsgPackTypeId.MpArray16:
          case MsgPackTypeId.MpArray32: item = new MpArray(settings); break;
          //case MsgPackTypeId.MpMap4:
          case MsgPackTypeId.MpMap16:
          case MsgPackTypeId.MpMap32: item = new MpMap(settings); break;
          case MsgPackTypeId.MpFExt1:
          case MsgPackTypeId.MpFExt2:
          case MsgPackTypeId.MpFExt4:
          case MsgPackTypeId.MpFExt8:
          case MsgPackTypeId.MpFExt16:
          case MsgPackTypeId.MpExt8:
          case MsgPackTypeId.MpExt16:
          case MsgPackTypeId.MpExt32: item = new MpExt(settings); break;
          case MsgPackTypeId.NeverUsed:
            {
              long pos = stream.Position - 1;
              if (settings.ContinueProcessingOnBreakingError) FindNextValidTypeId(stream);
              return new MpError(settings, pos, MsgPackTypeId.NeverUsed, "The specification specifically states that the value 0xC1 should never be used.")
              {
                storedLength = (stream.Position - pos)
              };
            }
        }

        if (ReferenceEquals(item, null))
        {
          if (((byte)type & 0xE0) == 0xE0 || (((byte)type & 0x80) == 0)) item = new MpInt(settings);
          else if (((byte)type & 0xA0) == 0xA0) item = new MpString(settings);
          else if (((byte)type & 0x90) == 0x90) item = new MpArray(settings);
          else if (((byte)type & 0x80) == 0x80) item = new MpMap(settings);
        }

        if (!ReferenceEquals(item, null))
        {
          item.storedOffset = stream.Position - 1;
          item._settings = settings; // maybe redundent, but want to be sure
          MsgPackItem ret = item.Read(type, stream);
          item.storedLength = stream.Position - item.storedOffset;
          if (!ReferenceEquals(item, ret)) ret.storedLength = item.storedLength;
          return ret;
        }
        else
        {
          long pos = stream.Position - 1;
          if (settings.ContinueProcessingOnBreakingError) FindNextValidTypeId(stream);
          return new MpError(settings, pos, type, "The type identifier with value 0x", BitConverter.ToString(new byte[] { (byte)type }),
            " is either new or invalid. It is not (yet) implemented in this version of LsMsgPack.")
          {
            storedLength = (stream.Position - pos)
          };
        }
      }
      catch (Exception ex)
      {
        long pos = stream.Position - 1;
        if (settings.ContinueProcessingOnBreakingError) FindNextValidTypeId(stream);
        return new MpError(settings, new MsgPackException("Error while reading data.", ex, stream.Position, (MsgPackTypeId)typeByte))
        {
          storedOffset = pos,
          storedLength = (stream.Position - pos),
          PartialItem = item
        };
      }
    }

    /// <summary>
    /// Is called after a breaking error occurred and the setting ContinueProcessingOnBreakingError is true (in order to find the beginning of the next item).
    /// </summary>
    protected static bool FindNextValidTypeId(Stream stream)
    {
      long lastPos = stream.Position;
      int typeByte = stream.ReadByte();

      while (typeByte >= 0 && !MsgPackMeta.IsValidPackageStartByte((byte)typeByte)) typeByte = stream.ReadByte();

      bool result = (typeByte >= 0);
      if (result) stream.Seek(stream.Position - 1, SeekOrigin.Begin);
      return result;
    }

    public virtual T GetTypedValue<T>()
    {
      return (T)Value;
    }

    public override string ToString()
    {
      return Value.ToString();
    }

    public static string GetOfficialTypeName(MsgPackTypeId typeId)
    {
      MsgPackMeta.PackDef def;
      if (MsgPackMeta.FromTypeId.TryGetValue(typeId, out def)) return def.OfficialName;
      //if(typeId == MsgPackTypeId.NeverUsed) return "[\"Officially never used\"] (0xC1)";
      return string.Concat("Undefined (0x", BitConverter.ToString(new byte[] { (byte)typeId }), ")");
    }

    internal static MsgPackMeta.PackDef GetTypeDescriptor(MsgPackTypeId typeId)
    {
      MsgPackMeta.PackDef def;
      if (MsgPackMeta.FromTypeId.TryGetValue(typeId, out def)) return def;
      return new MsgPackMeta.PackDef(typeId, string.Concat("Undefined (0x", BitConverter.ToString(new byte[] { (byte)typeId }), ")"),
        "This value is either invalid or new to the specification since the implementation of this library. Check the specification and check for updates if the value is defined.");
    }
  }


  public enum MsgPackTypeId : byte
  {
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
    NeverUsed = 0xc1,
  }
}
