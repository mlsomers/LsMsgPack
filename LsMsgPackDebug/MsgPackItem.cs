using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.IO;
using System.Collections;
using LsMsgPackDebug.Types.Extensions;

#if KEEPTRACK
using System.ComponentModel;
#endif

namespace LsMsgPackDebug
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

    /// <summary>
    /// The nesting level of an unpacked item (0 for the root), the arrays and maps unpack their items one level deeper (see <see cref="MsgPackOptions.MaxDepth"/>).
    /// </summary>
    internal int _depth;

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

    internal void SetStoredRange(long offset, long length)
    {
      storedOffset = offset;
      storedLength = length;
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
    internal virtual void WriteTo(LsMsgPack.Meta.ByteWriter target)
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
    /// <para>Data that ends before all bytes arrived throws (as LtMsgPack does): zeroing the rest made truncated data read as other values without an error.</para>
    /// </summary>
    protected static void ReadExactly(Stream data, byte[] buffer, int count)
    {
      int offset = 0;
      while (offset < count)
      {
        int read = data.Read(buffer, offset, count - offset);
        if (read <= 0)
          throw UnexpectedEnd(data);
        offset += read;
      }
    }

    /// <summary>
    /// Reads one byte, throws at the end of the data (<see cref="Stream.ReadByte"/> returns -1, which would read as a value).
    /// </summary>
    protected static byte ReadByteExactly(Stream data)
    {
      int value = data.ReadByte();
      if (value < 0)
        throw UnexpectedEnd(data);
      return (byte)value;
    }

    private protected static MsgPackException UnexpectedEnd(Stream data)
    {
      return new MsgPackException("Unexpected end of data.", data.Position, MsgPackTypeId.NeverUsed);
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

    /// <summary>
    /// [length0, length1, ..., [items]] (see MsgPackSerializer.SerializeMultidimensional).
    /// </summary>
    private static object[] Multidimensional(Array array)
    {
      object[] items = new object[array.Rank + 1];
      for (int t = 0; t < array.Rank; t++)
        items[t] = array.GetLength(t);
      object[] values = new object[array.Length];
      int index = 0;
      foreach (object value in array) // in the order of the indexes (the last one first), Array.Copy only copies between arrays of the same rank
        values[index++] = value;
      items[array.Rank] = values;
      return items;
    }

    private static object[] ToObjectArray(IEnumerable values)
    {
      if (values is Array array && array.Rank == 1)
      {
        object[] items = new object[array.Length];
        Array.Copy(array, items, items.Length); // boxes the elements of value types
        return items;
      }
      List<object> list = values is ICollection collection ? new List<object>(collection.Count) : new List<object>();
      foreach (object value in values)
        list.Add(value);
      return list.ToArray();
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
      if (value is DateTime) return new MpDateTime(settings) { Value = value };
      if (value is DateTimeOffset) return Meta.FrameworkTypes.PackDateTimeOffset((DateTimeOffset)value, settings);

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

      LsMsgPack.Meta.PlainForm plain = LsMsgPack.Meta.FrameworkTypeInfo.GetPlainForm(valuesType); // before the collections: ArraySegment<byte> is bin
      if (plain != LsMsgPack.Meta.PlainForm.None)
        return Meta.FrameworkTypes.PackPlain(LsMsgPack.Meta.FrameworkTypeInfo.ToPlain(value, plain), settings);

      if (valuesType.IsEnum) return new MpInt(settings).SetEnumVal(value);
      if (IsSubclassOfArrayOfRawGeneric(typeof(KeyValuePair<,>), valuesType)) return new MpMap(settings) { Value = value };
      if (value is IDictionary) return new MpMap(settings) { Value = value };
      if (value is IEnumerable enumerable && !valuesType.IsArray)
      {
        // Other collections of KeyValuePair elements (List<KeyValuePair<,>>, IDictionary<,> without IDictionary...) are a map too, their pairs have no settable properties
        LsMsgPack.Meta.CollectionInfo info = LsMsgPack.Meta.CollectionInfo.Get(valuesType);
        if (info.PairKey != null)
          return new MpMap(settings) { Value = info.ToPairs(enumerable) };
      }
      if (valuesType.IsArray && valuesType.GetArrayRank() > 1) return new MpArray(settings) { Value = Multidimensional((Array)value) };
      if (valuesType.IsArray) return new MpArray(settings) { Value = ToObjectArray((IEnumerable)value) };
      if (typeof(IEnumerable).IsAssignableFrom(valuesType)) return new MpArray(settings) { Value = ToObjectArray((IEnumerable)value) };

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
          MsgPackItem item = Unpack(stream, settings);
          if (settings._readStreams)
            item = RecognizeTail(item, items, stream, lastpos);
          items.Add(item);
          lastpos = stream.Position;
          // Unpack returns errors as items here instead of throwing: stop at the first one like the catch below does
          if (item is MpError && !settings._continueProcessingOnBreakingError)
            break;
        }
        catch (Exception ex)
        {
          items.Add(new MpError(settings, ex, "Offset after parsing error is ", stream.Position) { storedOffset = lastpos, storedLength = stream.Position - lastpos });
          if (settings._continueProcessingOnBreakingError)
          {
            // Nearly every byte can start a value, so there is nothing to search for: skip a byte when nothing was read, so the loop moves on
            if (lastpos == stream.Position)
              stream.ReadByte();
            lastpos = stream.Position;
          }
          else
          {
            break;
          }
        }
#else
        MsgPackItem item = Unpack(stream, settings);
        if (settings._readStreams)
          item = RecognizeTail(item, items, stream, lastpos);
        items.Add(item);
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
      return Unpack(stream, settings, 0);
    }

    /// <summary>
    /// The value after the index of a stream is its tail (<see cref="MsgPackSettings.ReadStreams"/>): an array of 4 values with fixed types, recognized by its bytes, which also tell whether the index matches its CRC-32.
    /// </summary>
    private static MsgPackItem RecognizeTail(MsgPackItem item, MpRoot items, Stream stream, long start)
    {
      MpStreamIndex index = items.Count > 0 ? items[items.Count - 1] as MpStreamIndex : null;
      MpArray array = item as MpArray;
      if (index is null || array is null || !stream.CanSeek || stream.Position - start != LsMsgPack.Streaming.MsgPackStreamIndex.TailLength)
        return item;

      long end = stream.Position;
      try
      {
        byte[] bytes = new byte[LsMsgPack.Streaming.MsgPackStreamIndex.TailLength];
        stream.Position = start;
        ReadExactly(stream, bytes, bytes.Length);
        LsMsgPack.Streaming.IndexContent.Tail tail;
        if (!LsMsgPack.Streaming.IndexContent.TryParseTail(bytes, 0, out tail))
          return item;

        long indexStart = start - tail.IndexLength;
        bool crcMatches = indexStart == index.Start && tail.IndexLength <= int.MaxValue && CrcOf(stream, indexStart, (int)tail.IndexLength) == tail.Crc;
        MpStreamTail result = new MpStreamTail(array, tail.IndexOffset, tail.IndexLength, tail.Crc, crcMatches);
        index.SetTail(result);
        return result;
      }
      finally
      {
        stream.Position = end;
      }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] // System.Memory (spans) is only loaded when a stream has an index
    private static uint CrcOf(Stream stream, long start, int length)
    {
      byte[] bytes = new byte[length];
      stream.Position = start;
      ReadExactly(stream, bytes, length);
      return LsMsgPack.Streaming.Crc32.Compute(bytes);
    }

    /// <summary>
    /// Marks (in <see cref="Exception.Data"/>) the exception thrown when the data is nested deeper than <see cref="MsgPackOptions.MaxDepth"/>: the levels above pass it on as it is instead of wrapping it each.
    /// </summary>
    private const string TooDeepKey = "LsMsgPack.TooDeep";

    private static MsgPackException TooDeep(int maxDepth, long offset, MsgPackTypeId typeId)
    {
      MsgPackException ex = new MsgPackException($"The data is nested deeper than {nameof(MsgPackSettings)}.{nameof(MsgPackSettings.MaxDepth)} ({maxDepth}).", offset, typeId);
      ex.Data[TooDeepKey] = true;
      return ex;
    }

    /// <param name="depth">The nesting level of the item (0 for the root), arrays and maps at <see cref="MsgPackOptions.MaxDepth"/> are refused: deeper nesting could exhaust the stack</param>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] // one call per item, as before the type byte was passed on
    internal static MsgPackItem Unpack(Stream stream, MsgPackSettings settings, int depth)
    {
      int typeByte = stream.ReadByte();
#if KEEPTRACK
      if (typeByte < 0) return new MpError(settings, stream.Position, MsgPackTypeId.NeverUsed, "Unexpected end of data.");
#else
      if (typeByte < 0) throw new MsgPackException("Unexpected end of data.", stream.Position, MsgPackTypeId.NeverUsed);
#endif
      return Unpack(stream, settings, depth, typeByte, CodeNotRead);
    }

    /// <summary>
    /// The code byte of an escape sequence (0xC1) is not read yet, see <see cref="Unpack(Stream, MsgPackSettings, int, int, int)"/>.
    /// </summary>
    internal const int CodeNotRead = -2;

    /// <param name="typeByte">The first byte of the item, already read (the containers of unknown length read it to recognize their end)</param>
    /// <param name="code">With <paramref name="typeByte"/> 0xC1: the code byte that follows it when already read, otherwise <see cref="CodeNotRead"/></param>
    internal static MsgPackItem Unpack(Stream stream, MsgPackSettings settings, int depth, int typeByte, int code)
    {
      MsgPackItem item = null;
      bool escaped = false;
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
              if (settings._readStreams)
              {
                long start = stream.Position - (code < 0 ? 1 : 2); // a code at the end of the data was not read either
                if (code == CodeNotRead) code = stream.ReadByte();
                item = StartOfUnknownLength(settings, code);
                if (item is null)
                  return EscapeError(stream, settings, start, code);
                escaped = true;
                break;
              }
              long pos = stream.Position - 1;
#if KEEPTRACK
              return new MpError(settings, pos, MsgPackTypeId.NeverUsed, NeverUsedMessage)
              {
                storedLength = (stream.Position - pos)
              };
#else
              throw new MsgPackException(NeverUsedMessage, pos, MsgPackTypeId.NeverUsed);
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
          item.storedOffset = stream.Position - (escaped ? 2 : 1);
#endif
          item._settings = settings; // maybe redundent, but want to be sure
          if (depth >= settings._maxDepth && (item is MpArray || item is MpMap))
            throw TooDeep(settings._maxDepth, stream.Position - (escaped ? 2 : 1), type);
          item._depth = depth;
          MsgPackItem ret = escaped ? ((MsgPackVarLen)item).ReadUnknownLength(stream) : item.Read(type, stream);
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
#if !KEEPTRACK
        when (!ex.Data.Contains(TooDeepKey))
#endif
      {
#if KEEPTRACK
        long pos = stream.Position - 1;
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

    private const string NeverUsedMessage = "The specification specifically states that the value 0xC1 should never be used. Streams use it for values of unknown length (an escape sequence, as proposed in msgpack/msgpack#347): set MsgPackSettings.ReadStreams to read them.";

    /// <summary>
    /// The empty item an escape sequence starts (0xC1 followed by <paramref name="code"/>), to read with <see cref="MsgPackVarLen.ReadUnknownLength"/>. Null for the end code, which only a container of unknown length expects, and for the reserved codes.
    /// </summary>
    private static MsgPackVarLen StartOfUnknownLength(MsgPackSettings settings, int code)
    {
      switch (code)
      {
        case LsMsgPack.Streaming.Escape.ArrayStart: return new MpArray(settings);
        case LsMsgPack.Streaming.Escape.MapStart: return new MpMap(settings);
        case LsMsgPack.Streaming.Escape.StringStart: return new MpString(settings);
        case LsMsgPack.Streaming.Escape.BinaryStart: return new MpBin(settings);
        default: return null;
      }
    }

    /// <summary>
    /// An escape sequence that does not start a value: the end code where no value of unknown length is open, a reserved code, or the end of the data after 0xC1.
    /// </summary>
    private static MsgPackItem EscapeError(Stream stream, MsgPackSettings settings, long start, int code)
    {
      MsgPackException ex;
      if (code < 0) ex = LsMsgPack.Streaming.Escape.AtEndOfData(start);
      else if (code == LsMsgPack.Streaming.Escape.End) ex = LsMsgPack.Streaming.Escape.MisplacedEnd("is not inside an array, map, str or bin of unknown length", start);
      else ex = LsMsgPack.Streaming.Escape.Reserved((byte)code, start);
#if KEEPTRACK
      return new MpError(settings, start, MsgPackTypeId.NeverUsed, ex.Message)
      {
        storedLength = (stream.Position - start)
      };
#else
      throw ex;
#endif
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
