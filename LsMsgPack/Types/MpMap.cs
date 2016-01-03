using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Xml.Serialization;

namespace LsMsgPack {
  [Serializable]
  public class MpMap: MsgPackVarLen {

    public MpMap() : base() { }
    public MpMap(MsgPackSettings settings):base(settings) { }
    public MpMap(KeyValuePair<object, object>[] val, MsgPackSettings settings) : this(settings) {
      value = val;
    }
    public MpMap(KeyValuePair<object, object>[] val, bool dynamicallyCompact = true) : this() {
      _settings = new MsgPackSettings() { _dynamicallyCompact = dynamicallyCompact };
      value = val;
    }

    KeyValuePair<object, object>[] value = new KeyValuePair<object, object>[0];
    private KeyValuePair<MsgPackItem, MsgPackItem>[] packedItems = new KeyValuePair<MsgPackItem, MsgPackItem>[0];

    

    public override int Count {
      get { return value.Length; }
    }

    public override MsgPackTypeId TypeId {
      get {
        return GetTypeId(value.LongLength);
      }
    }

    protected override MsgPackTypeId GetTypeId(long len) {
      if(len < 16) return MsgPackTypeId.MpMap4;
      if(len <= ushort.MaxValue) return MsgPackTypeId.MpMap16;
      return MsgPackTypeId.MpMap32;
    }

    public override object Value {
      get { return value; }
      set {
        if(ReferenceEquals(value, null)) {
          value = new KeyValuePair<object, object>[0];
          return;
        }
        if(IsSubclassOfRawGeneric(typeof(Dictionary<,>), value.GetType())) {
          IDictionary dict = (IDictionary)value;
          this.value = new KeyValuePair<object, object>[dict.Count];
          int t = 0;
          foreach(object key in dict.Keys) {
            this.value[t] = new KeyValuePair<object, object>(key, dict[key]);
            t++;
          }
        } else this.value = (KeyValuePair<object, object>[])value;
      }
    }

    public override T GetTypedValue<T>() {
      if(IsSubclassOfRawGeneric(typeof(Dictionary<,>), typeof(T))) {
        IDictionary dict = (IDictionary)Activator.CreateInstance(typeof(T), new object[] { value.Length });
        for(int t = value.Length - 1; t >= 0; t--) {
          dict.Add(value[t].Key, value[t].Value);
        }
        return (T)dict;
      }
      return base.GetTypedValue<T>();
    }

    /// <summary>
    /// Preserved containers after reading the data (contains offset metadata for debugging).
    /// Depends on MsgPackVarLen.PreservePackages.
    /// </summary>
    [XmlIgnore]
    [Category("Data")]
    [DisplayName("Preserved Data")]
    [Description("Preserved containers after reading the data (contains offset metadata for debugging).\r\nDepends on MsgPackVarLen.PreservePackages.")]
    [Browsable(false)]
    public KeyValuePair<MsgPackItem, MsgPackItem>[] PackedValues {
      get {
        return packedItems;
      }
    }

    public override byte[] ToBytes() {
      List<byte> bytes = new List<byte>();// cannot estimate this one
      MsgPackTypeId typeId = GetTypeId(value.LongLength);
      if(typeId == MsgPackTypeId.MpMap4) bytes.Add(GetLengthBytes(typeId, value.Length));
      else {
        bytes.Add((byte)typeId);
        bytes.AddRange(GetLengthBytes(value.LongLength, SupportedLengths.FromShortUpward));
      }
      for(int t = 0; t < value.Length; t++) {
        MsgPackItem key = MsgPackItem.Pack(value[t].Key, _settings);
        MsgPackItem val = MsgPackItem.Pack(value[t].Value, _settings);
        bytes.AddRange(key.ToBytes());
        bytes.AddRange(val.ToBytes());
      }
      return bytes.ToArray();
    }

    public override MsgPackItem Read(MsgPackTypeId typeId, Stream data) {
      long len;
      if(!IsMasked(MsgPackTypeId.MpMap4, typeId, 0x0F, out len)) {
        switch(typeId) {
          case MsgPackTypeId.MpMap16: len = ReadLen(data, 2); break;
          case MsgPackTypeId.MpMap32: len = ReadLen(data, 4); break;
          default: throw new MsgPackException(string.Concat("MpMap does not support a type ID of ", GetOfficialTypeName(typeId), "."), data.Position - 1, typeId);
        }
      }

      value = new KeyValuePair<object, object>[len];
      
      packedItems = new KeyValuePair<MsgPackItem, MsgPackItem>[len];
      bool errorOccurred = false;
      for(int t = 0; t < len; t++) {
        MsgPackItem key = MsgPackItem.Unpack(data, _settings);
        MsgPackItem val;
        if(key is MpError) {
          if(_settings.ContinueProcessingOnBreakingError) {
            _settings.FileContainsErrors = true;
            errorOccurred = true;
            if(data.Position >= data.Length) val = new MpNull(_settings);
            else val = MsgPackItem.Unpack(data, _settings);
          } else val = new MpNull(_settings);
        } else val = MsgPackItem.Unpack(data, _settings);
        if(_settings._preservePackages) packedItems[t] = new KeyValuePair<MsgPackItem, MsgPackItem>(key, val);
        value[t] = new KeyValuePair<object, object>(key.Value, val.Value);
        if(!_settings.ContinueProcessingOnBreakingError && (key is MpError || val is MpError)) {
          return new MpError(_settings, this);
        }
        if(val is MpError) {
          _settings.FileContainsErrors = true;
          errorOccurred = true;
          if(data.Position >= data.Length) return new MpError(_settings, this);
        }
      }
      if(errorOccurred) return new MpError(_settings, this);
      return this;
    }

    public override string ToString() {
      return string.Concat("Map (", GetOfficialTypeName(TypeId), ") of ", value.LongLength.ToString(), " key-value pairs.");
    }

  }
}
