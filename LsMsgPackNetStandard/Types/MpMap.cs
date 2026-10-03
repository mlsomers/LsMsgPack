using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LsMsgPack.Meta;
#if KEEPTRACK
using System.ComponentModel;
using System.Xml.Serialization;
#endif

namespace LsMsgPack
{
  [Serializable]
  public class MpMap : MsgPackVarLen
  {

    public MpMap() : base() { }
    public MpMap(MsgPackSettings settings) : base(settings) { }

    public MpMap(KeyValuePair<object, object>[] val, MsgPackSettings settings) : this(settings)
    {
      value = val;
    }
    public MpMap(KeyValuePair<object, object>[] val, bool dynamicallyCompact = true) : this()
    {
      _settings = new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact };
      value = val;
    }

    KeyValuePair<object, object>[] value = new KeyValuePair<object, object>[0];
#if KEEPTRACK
    private KeyValuePair<MsgPackItem, MsgPackItem>[] packedItems = new KeyValuePair<MsgPackItem, MsgPackItem>[0];
#endif

    public override int Count
    {
      get { return value.Length; }
    }

    public override MsgPackTypeId TypeId {
      get {
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
        return GetTypeId(value.LongLength);
#else
        return GetTypeId(value.Length);
#endif
      }
    }

    protected override MsgPackTypeId GetTypeId(long len)
    {
      if (len < 16) return MsgPackTypeId.MpMap4;
      if (len <= ushort.MaxValue) return MsgPackTypeId.MpMap16;
      return MsgPackTypeId.MpMap32;
    }

    public override object Value
    {
      get { return value; }
      set
      {
        if (ReferenceEquals(value, null))
        {
          this.value = new KeyValuePair<object, object>[0];
          return;
        }
        if (value.GetType() == typeof(Dictionary<object, object>)) // the generic enumerator does not box every entry
        {
          Dictionary<object, object> generic = (Dictionary<object, object>)value;
          this.value = new KeyValuePair<object, object>[generic.Count];
          int t = 0;
          foreach (KeyValuePair<object, object> entry in generic)
          {
            this.value[t] = entry;
            t++;
          }
        }
        else if (value is IDictionary dict)
        {
          this.value = new KeyValuePair<object, object>[dict.Count];
          int t = 0;
          foreach (DictionaryEntry entry in dict)
          {
            this.value[t] = new KeyValuePair<object, object>(entry.Key, entry.Value);
            t++;
          }
        }
        else if (value is KeyValuePair<object, object>[] pairs)
          this.value = pairs;
        else if (IsSubclassOfArrayOfRawGeneric(typeof(KeyValuePair<,>), value.GetType())) // KeyValuePair<TKey, TValue>[]
        {
          Array arr = (Array)value;
          Type pairType = arr.GetType().GetElementType();
          System.Reflection.PropertyInfo keyProp = pairType.GetProperty(nameof(KeyValuePair<object, object>.Key));
          System.Reflection.PropertyInfo valueProp = pairType.GetProperty(nameof(KeyValuePair<object, object>.Value));

          this.value = new KeyValuePair<object, object>[arr.Length];
          for (int t = arr.Length - 1; t >= 0; t--)
          {
            object pair = arr.GetValue(t);
            this.value[t] = new KeyValuePair<object, object>(keyProp.GetValue(pair), valueProp.GetValue(pair));
          }
        }
        else this.value = (KeyValuePair<object, object>[])value;
      }
    }

    public override T GetTypedValue<T>()
    {
      if (IsSubclassOfRawGeneric(typeof(Dictionary<,>), typeof(T)))
      {
        IDictionary dict = (IDictionary)Activator.CreateInstance(typeof(T), new object[] { value.Length });
        FillDictionary(dict);
        return (T)dict;
      }
      return base.GetTypedValue<T>();
    }

    internal void FillDictionary<T>(T dict) where T: IDictionary
    {
      for (int t = value.Length - 1; t >= 0; t--)
      {
        dict.Add(value[t].Key, value[t].Value);
      }
    }

#if KEEPTRACK
    /// <summary>
    /// Preserved containers after reading the data (contains offset metadata for debugging).
    /// Depends on MsgPackVarLen.PreservePackages.
    /// </summary>
    [XmlIgnore]
    [Category("Data")]
    [DisplayName("Preserved Data")]
    [Description("Preserved containers after reading the data (contains offset metadata for debugging).\r\nDepends on MsgPackVarLen.PreservePackages.")]
    [Browsable(false)]
    public KeyValuePair<MsgPackItem, MsgPackItem>[] PackedValues
    {
      get
      {
        return packedItems;
      }
    }
#endif

    public override byte[] ToBytes()
    {
      ByteWriter bytes = new ByteWriter();// cannot estimate this one
      WriteTo(bytes);
      return bytes.ToArray();
    }

    internal override void WriteTo(ByteWriter bytes)
    {
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
      MsgPackTypeId typeId = GetTypeId(value.LongLength);
#else
      MsgPackTypeId typeId = GetTypeId(value.Length);
#endif
      if(typeId == MsgPackTypeId.MpMap4) bytes.Write(GetLengthBytes(typeId, value.Length));
      else {
        bytes.Write((byte)typeId);
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
        WriteLength(bytes, value.LongLength, SupportedLengths.FromShortUpward);
#else
        WriteLength(bytes, value.Length, SupportedLengths.FromShortUpward);
#endif
      }
      for (int t = 0; t < value.Length; t++)
      {
        // TODO: call MsgPackSerializer.GetTypedOrUntyped for keys as well as values
        MsgPackItem key = value[t].Key as MsgPackItem ?? MsgPackItem.Pack(value[t].Key, _settings) ?? MsgPackSerializer.SerializeObject(value[t].Key, _settings); // may already be packed by the serializer
        MsgPackItem val = value[t].Value as MsgPackItem ?? MsgPackItem.Pack(value[t].Value, _settings) ?? MsgPackSerializer.SerializeObject(value[t].Value, _settings);
        key.WriteTo(bytes);
        val.WriteTo(bytes);
      }
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data)
    {
      long len;
      if (!IsMasked(MsgPackTypeId.MpMap4, typeId, 0x0F, out len))
      {
        switch (typeId)
        {
          case MsgPackTypeId.MpMap16: len = ReadLen(data, 2); break;
          case MsgPackTypeId.MpMap32: len = ReadLen(data, 4); break;
          default: throw new MsgPackException($"MpMap does not support a type ID of {GetOfficialTypeName(typeId)}.", data.Position - 1, typeId);
        }
      }

      value = new KeyValuePair<object, object>[len];

#if KEEPTRACK
      packedItems = new KeyValuePair<MsgPackItem, MsgPackItem>[len];
      bool errorOccurred = false;
#endif
      for (int t = 0; t < len; t++)
      {
        MsgPackItem key = MsgPackItem.Unpack(data, _settings, _depth + 1);
#if KEEPTRACK
        MsgPackItem val;
        if (key is MpError)
        {
          if (_settings._continueProcessingOnBreakingError)
          {
            _settings.FileContainsErrors = true;
            errorOccurred = true;
            if (data.Position >= data.Length) val = new MpNull(_settings);
            else val = MsgPackItem.Unpack(data, _settings, _depth + 1);
          }
          else val = new MpNull(_settings);
        }
        else val = MsgPackItem.Unpack(data, _settings, _depth + 1);
        if (_settings._preservePackages) packedItems[t] = new KeyValuePair<MsgPackItem, MsgPackItem>(key, val);
#else
        MsgPackItem val = MsgPackItem.Unpack(data, _settings, _depth + 1);
#endif 

        value[t] = new KeyValuePair<object, object>(key.UnpackedValue, val.UnpackedValue);

#if KEEPTRACK
        if (!_settings._continueProcessingOnBreakingError && (key is MpError || val is MpError))
        {
          return new MpError(_settings, this);
        }
        if (val is MpError)
        {
          _settings.FileContainsErrors = true;
          errorOccurred = true;
          if (data.Position >= data.Length) return new MpError(_settings, this);
        }
      }
      if (errorOccurred) return new MpError(_settings, this);
#else
      }
#endif

      return this;
    }

    public override string ToString() {
#if !(SILVERLIGHT || WINDOWS_PHONE || NETFX_CORE || PORTABLE)
      return $"Map ({GetOfficialTypeName(TypeId)}) of {value.LongLength} key-value pairs.";
#else
      return string.Concat("Map (", GetOfficialTypeName(TypeId), ") of ", value.Length.ToString(), " key-value pairs.");
#endif
    }

  }
}
