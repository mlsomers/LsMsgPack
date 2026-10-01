using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.IO;
using System.Collections;
using System.Linq;
using LsMsgPack.Types.Extensions;

#if KEEPTRACK
using System.ComponentModel;
#endif

namespace LsMsgPack
{
  [Serializable]
#if KEEPTRACK
  [DefaultProperty("Value")]
#endif
  public abstract class MsgPackItem
  {

    public MsgPackItem() : base() { _settings = new MsgPackSettings(); }
    public MsgPackItem(MsgPackSettings settings)
    {
      _settings = settings;
#if KEEPTRACK
      _isBestGuess = _settings?.FileContainsErrors ?? false;
#endif
    }

#if KEEPTRACK
    protected long storedOffset;
    protected long storedLength;
#endif
    protected MsgPackSettings _settings;

    [XmlIgnore]
#if KEEPTRACK
    [Category("Control")]
    [DisplayName("Settings")]
    [Description("Settings belonging to this instance.")]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
#endif
    public MsgPackSettings Settings { get { return _settings; } set { _settings = value; } }

#if KEEPTRACK
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
#endif

    /// <summary>
    /// The type of information held in this structure.
    /// </summary>
    [XmlAttribute("TypeId", DataType = "byte")]
#if KEEPTRACK
    [Category("MetaData")]
    [DisplayName("Type")]
    [Description("The type of information held in this structure.")]
    [TypeConverter(typeof(MsgPackTypeConverter))]
    [ReadOnly(true)]
    [Browsable(true)]
#endif
    public abstract MsgPackTypeId TypeId { get; }

    /// <summary>
    /// The actual piece of information held by this container.
    /// </summary>
    [XmlElement]
#if KEEPTRACK
    [Category("Data")]
    [DisplayName("Data")]
    [Description("The actual piece of information held by this container.")]
#endif
    public abstract object Value { get; set; }

    public abstract byte[] ToBytes();

    /// <summary>
    /// The value unpacked containers hold (and the serializer converts): <see cref="Value"/>, except for an extension without a registered type (see <see cref="MsgPackSettings.CustomExtentionTypes"/>).
    /// <para>Its value would be indistinguishable from binary data (e.g. a 16 byte extension would be read as a Guid), so the <see cref="MpExt"/> itself is kept, including its type.</para>
    /// </summary>
    internal object UnpackedValue
    {
      get { return GetType() == typeof(MpExt) ? this : Value; }
    }

    /// <summary>
    /// Appends the same bytes as <see cref="ToBytes"/> to the target.
    /// <para>Containers override this to write their items directly into the target instead of copying the bytes of every nesting level.</para>
    /// </summary>
    internal virtual void WriteTo(Meta.ByteWriter target)
    {
      target.Write(ToBytes());
    }

    public abstract MsgPackItem Read(MsgPackTypeId typeId, Stream data);

    [XmlIgnore]
#if KEEPTRACK
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
#endif
    public object Tag { get; set; }

    public static bool SwapEndianChoice(MsgPackSettings settings, int length)
    {
      return MsgPackOptions.SwapEndianChoice(settings, length);
    }

    protected static void ReorderIfLittleEndian(MsgPackSettings settings, List<byte> bytes)
    {
      if (!SwapEndianChoice(settings, bytes.Count))
        return;

      bytes.Reverse();
    }

    protected static void ReorderIfLittleEndian(MsgPackSettings settings, byte[] bytes)
    {
      if (!SwapEndianChoice(settings, bytes.Length))
        return;

      Array.Reverse(bytes);
    }

    /// <summary>
    /// Reads count bytes into the buffer. <see cref="Stream.Read(byte[], int, int)"/> may return fewer bytes than requested (e.g. network streams), so keep reading until all bytes arrived.
    /// <para>At the end of the data the remaining bytes are zeroed (the same as reading into a new buffer).</para>
    /// </summary>
    protected static void ReadExactly(Stream data, byte[] buffer, int count)
    {
      int offset = 0;
      while (offset < count)
      {
        int read = data.Read(buffer, offset, count - offset);
        if (read <= 0)
        {
          Array.Clear(buffer, offset, count - offset);
          return;
        }
        offset += read;
      }
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
      return PackMultiple(new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact }, values);
    }

    public static MpRoot PackMultiple(MsgPackSettings settings, params object[] values)
    {
      MpRoot root = new MpRoot(settings, values.Length);
      for (int t = 0; t < values.Length; t++)
        root.Add(Pack(values[t], settings) ?? MsgPackSerializer.SerializeObject(values[t], settings));
      return root;
    }

    /// <param name="dynamicallyCompact">Will store a long with value 3 as a nibble (using only one byte)</param>
    public static MpRoot PackMultiple(bool dynamicallyCompact, IEnumerable values)
    {
      return PackMultiple(new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact }, values);
    }

    public static MpRoot PackMultiple(MsgPackSettings settings, IEnumerable values)
    {
      MpRoot root = new MpRoot(settings);
      foreach (object item in values)
      {
        root.Add(Pack(item, settings) ?? MsgPackSerializer.SerializeObject(item, settings));
      }
      return root;
    }

    public static MsgPackItem Pack(object value, bool dynamicallyCompact = true)
    {
      MsgPackSettings sett = new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact };
      return Pack(value, sett) ?? MsgPackSerializer.SerializeObject(value, sett);
    }

    public static MsgPackItem Pack(object value, MsgPackSettings settings, Type valuesType = null)
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

      if (valuesType is null)
        valuesType = value.GetType();

      if (settings._customExtentionTypes != null)
      {
        for (int t = 0; t < settings._customExtentionTypes.Length; t++)
        {
          ICustomExt ext = settings._customExtentionTypes[t];
          if (ext is null)
            continue;

          if (ext.SupportsType(valuesType))
            return ext.Create(settings, null, value);
        }
      }

      if (valuesType.IsEnum) return new MpInt(settings).SetEnumVal(value);
      if (IsSubclassOfArrayOfRawGeneric(typeof(KeyValuePair<,>), valuesType)) return new MpMap(settings) { Value = value };
      if (value is IDictionary) return new MpMap(settings) { Value = value };
      if (valuesType.IsArray) return new MpArray(settings) { Value = ((IEnumerable)value).Cast<Object>().ToArray() };
      if (typeof(IEnumerable).IsAssignableFrom(valuesType)) return new MpArray(settings) { Value = ((IEnumerable)value).Cast<Object>().ToArray() };

      // Extension types will come in like this most of the time:
      MsgPackItem val = value as MsgPackItem;
      if (!ReferenceEquals(val, null))
      {
        val._settings = settings;
        return val;
      }

      MsgPackItem framework = Meta.FrameworkTypes.Pack(value, valuesType, settings); // char, TimeSpan, DateOnly, TimeOnly and Uri (no settable properties)
      if (!ReferenceEquals(framework, null))
        return framework;

      return null; // not natively supported  // MsgPackSerializer.SerializeObject(value, settings);
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

#if KEEPTRACK
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
        _dynamicallyCompact = dynamicallyCompact,
        _preservePackages = preservePackages,
        _continueProcessingOnBreakingError = continueProcessingOnBreakingError
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
        _dynamicallyCompact = dynamicallyCompact,
        _preservePackages = preservePackages,
        _continueProcessingOnBreakingError = continueProcessingOnBreakingError
      });
    }
#else
    public static MsgPackItem Unpack(byte[] data, bool dynamicallyCompact = true)
    {
      using (MemoryStream ms = new MemoryStream(data))
      {
        return Unpack(ms, dynamicallyCompact);
      }
    }

    public static MsgPackItem Unpack(Stream stream, bool dynamicallyCompact = true)
    {
      return Unpack(stream, new MsgPackSettings()
      {
        _dynamicallyCompact = dynamicallyCompact
      });
    }

    public static MpRoot UnpackMultiple(byte[] data, bool dynamicallyCompact = true)
    {
      using (MemoryStream ms = new MemoryStream(data))
      {
        return UnpackMultiple(ms, dynamicallyCompact);
      }
    }

    public static MpRoot UnpackMultiple(Stream stream, bool dynamicallyCompact = true)
    {
      return UnpackMultiple(stream, new MsgPackSettings()
      {
        _dynamicallyCompact = dynamicallyCompact
      });
    }
#endif

    public static MpRoot UnpackMultiple(byte[] data, MsgPackSettings settings)
    {
      using (MemoryStream ms = new MemoryStream(data))
      {
        return UnpackMultiple(ms, settings);
      }
    }

    public static MpRoot UnpackMultiple(Stream stream, MsgPackSettings settings)
    {
#if KEEPTRACK
      MpRoot items = new MpRoot(settings) { storedOffset = stream.Position };
#else
      MpRoot items = new MpRoot(settings);
#endif
      long len = stream.Length;
      long lastpos = stream.Position;
      while (stream.Position < len)
      {
#if KEEPTRACK
        try
        {
          items.Add(Unpack(stream, settings));
          lastpos = stream.Position;
        }
        catch (Exception ex)
        {
          items.Add(new MpError(settings, ex, "Offset after parsing error is ", stream.Position) { storedOffset = lastpos, storedLength = stream.Position - lastpos });
          if (settings._continueProcessingOnBreakingError)
          {
            if (lastpos == stream.Position && stream.Position < len)
              FindNextValidTypeId(stream);
          }
          else
          {
            break;
          }
        }
#else
        items.Add(Unpack(stream, settings));
        lastpos = stream.Position;
#endif
      }

#if KEEPTRACK
      items.storedLength = stream.Position - items.storedOffset;
#endif
      return items;
    }

    public static MsgPackItem Unpack(Stream stream, MsgPackSettings settings)
    {
      int typeByte = stream.ReadByte();
#if KEEPTRACK
      if (typeByte < 0) return new MpError(settings, stream.Position, MsgPackTypeId.NeverUsed, "Unexpected end of data.");
#else
      if (typeByte < 0) throw new MsgPackException("Unexpected end of data.", stream.Position, MsgPackTypeId.NeverUsed);
#endif
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
#if KEEPTRACK
              if (settings._continueProcessingOnBreakingError) FindNextValidTypeId(stream);
              return new MpError(settings, pos, MsgPackTypeId.NeverUsed, "The specification specifically states that the value 0xC1 should never be used.")
              {
                storedLength = (stream.Position - pos)
              };
#else
              throw new MsgPackException("The specification specifically states that the value 0xC1 should never be used.", pos, MsgPackTypeId.NeverUsed);
#endif
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
#if KEEPTRACK
          item.storedOffset = stream.Position - 1;
#endif
          item._settings = settings; // maybe redundent, but want to be sure
          MsgPackItem ret = item.Read(type, stream);
#if KEEPTRACK
          item.storedLength = stream.Position - item.storedOffset;
          if (!ReferenceEquals(item, ret)) ret.storedLength = item.storedLength;
#endif
          return ret;
        }
        else
        {
#if KEEPTRACK
          long pos = stream.Position - 1;
          if (settings._continueProcessingOnBreakingError) FindNextValidTypeId(stream);
          return new MpError(settings, pos, type, "The type identifier with value 0x", BitConverter.ToString(new byte[] { (byte)type }),
            " is either new or invalid. It is not (yet) implemented in this version of LsMsgPack.")
          {
            storedLength = (stream.Position - pos)
          };
#else
          throw new MsgPackException(
            $"The type identifier with value 0x{BitConverter.ToString(new byte[] { (byte)type })} is either new or invalid. It is not (yet) implemented in this version of LsMsgPack.", stream.Position - 1, type);
#endif
        }
      }
      catch (Exception ex)
      {
#if KEEPTRACK
        long pos = stream.Position - 1;
        if (settings._continueProcessingOnBreakingError) FindNextValidTypeId(stream);
        return new MpError(settings, new MsgPackException("Error while reading data.", ex, stream.Position, (MsgPackTypeId)typeByte))
        {
          storedOffset = pos,
          storedLength = (stream.Position - pos),
          PartialItem = item
        };
#else
        throw new MsgPackException("Error while reading data.", ex, stream.Position - 1, (MsgPackTypeId)typeByte);
#endif
      }
    }

#if KEEPTRACK

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

#endif

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
      return $"Undefined (0x{BitConverter.ToString(new byte[] { (byte)typeId })})";
    }

    internal static MsgPackMeta.PackDef GetTypeDescriptor(MsgPackTypeId typeId)
    {
      MsgPackMeta.PackDef def;
      if (MsgPackMeta.FromTypeId.TryGetValue(typeId, out def)) return def;
      return new MsgPackMeta.PackDef(typeId, $"Undefined (0x{BitConverter.ToString(new byte[] { (byte)typeId })})",
        "This value is either invalid or new to the specification since the implementation of this library. Check the specification and check for updates if the value is defined.");
    }
  }
}
