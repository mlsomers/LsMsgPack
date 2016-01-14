using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace LsMsgPackMicro {
  public static class MsgPackSerializer {

    public static Type[] _nativelySupportedTypes;

    /// <remarks>
    /// Since static constructor or initialiser is not being called, made it a lazy loading thing
    /// </remarks>
    public static Type[] NativelySupportedTypes {
      get {
        if (ReferenceEquals(_nativelySupportedTypes, null)) {
          _nativelySupportedTypes = new Type[15];

          _nativelySupportedTypes[0] = typeof(bool);
          _nativelySupportedTypes[1] = typeof(sbyte);
          _nativelySupportedTypes[2] = typeof(short);
          _nativelySupportedTypes[3] = typeof(int);
          _nativelySupportedTypes[4] = typeof(long);
          _nativelySupportedTypes[5] = typeof(byte);
          _nativelySupportedTypes[6] = typeof(ushort);
          _nativelySupportedTypes[7] = typeof(uint);
          _nativelySupportedTypes[8] = typeof(ulong);
          _nativelySupportedTypes[9] = typeof(float);
          _nativelySupportedTypes[10] = typeof(string);
          _nativelySupportedTypes[11] = typeof(byte[]); // NetMF 4.1 cannot handle this
          _nativelySupportedTypes[12] = typeof(ArrayList);
          _nativelySupportedTypes[13] = typeof(object[]);
          _nativelySupportedTypes[14] = typeof(KeyValuePair[]);
          
        }
        return _nativelySupportedTypes;
      }
    }


    public static byte[] Serialize(object item) {
      if (MsgPackSerializer.NativelySupportedTypes.Contains(item.GetType())) return MsgPackItem.Pack(item).ToBytes();
      MemoryStream ms = new MemoryStream();
      Serialize(item, ms);
      return ms.ToArray();
    }

    public static void Serialize(object item, Stream target) {
      MsgPackItem packed = SerializeObject(item);
      byte[] buffer = packed.ToBytes();
      target.Write(buffer, 0, buffer.Length);
      return;
    }

    public static MsgPackItem SerializeObject(object item) {
      if(ReferenceEquals(item, null)) return new MpNull();
      Type tType = item.GetType();
      if(MsgPackSerializer.NativelySupportedTypes.Contains(tType)) {
        return MsgPackItem.Pack(item);
        // Maybe we should rather throw an exception here
      }
      PropertyInfo[] props = GetSerializedProps(tType);
      KeyValuePair[] propVals = new KeyValuePair[props.Length];
      for(int t = props.Length - 1; t >= 0; t--) {
        propVals[t]=new KeyValuePair(props[t].Name, props[t].GetValue(item, null));
      }
      return new MpMap() { Value = propVals };
    }

    public static object Deserialize(Type tType, byte[] source) {
      using(MemoryStream ms = new MemoryStream(source)) {
        return Deserialize(tType, ms);
      }
    }

    public static object Deserialize(Type tType, Stream stream) {
      if(MsgPackSerializer.NativelySupportedTypes.Contains(tType)) {
        return MsgPackItem.Unpack(stream).Value;
      }
      MpMap map = (MpMap)MsgPackItem.Unpack(stream);
      object result = Materialize(tType, map);
      return result;
    }

    private static object Materialize(Type tType, MpMap map) {
      PropertyInfo[] props = GetSerializedProps(tType);
      KeyValuePair[] propVals = (KeyValuePair[])map.Value;

      object result = tType.CreateInstance();

      for(int t = props.Length - 1; t >= 0; t--) {
        object val;
        if(propVals.TryGetValue(props[t].Name, out val)) {
          Type propType = props[t].PropertyType;
          if(!ReferenceEquals(val, null)) {
            if(!MsgPackSerializer.NativelySupportedTypes.Contains(propType)
              && val is KeyValuePair[]) {
              val = Materialize(propType, new MpMap((KeyValuePair[])val));
            }
            if(propType.IsArray && !(propType == typeof(object))) {
              // Need to cast object[] to whatever[]
              object[] valAsArr = (object[])val;
              Type arrayElementType = propType.GetElementType();
              //Array newInstance = Array.CreateInstance(propType, valAsArr.Length);
              ArrayList newInstance = new ArrayList();

              // Part of the check for complex types can be done outside the loop
              bool complexTypes = !MsgPackSerializer.NativelySupportedTypes.Contains(arrayElementType);
              bool intType = MsgPackMeta.NativeIntTypeFamily.Contains(arrayElementType);
              for (int i = 0; i < valAsArr.Length; i++) {
                if (complexTypes && !ReferenceEquals(valAsArr[i], null)
                  && valAsArr[i] is KeyValuePair[]) {
                  valAsArr[i] = Materialize(arrayElementType, new MpMap((KeyValuePair[])valAsArr[i]));
                } else if (intType) valAsArr[i] = MpInt.CovertIntType(arrayElementType, valAsArr[i]);
                //newInstance.SetValue(valAsArr[i], i); // <- Got stuck on this one, using ArrayList instead.
                newInstance.Add(valAsArr[i]);
              }

              props[t].SetValue(result, newInstance.ToArray(arrayElementType), null); 
              continue;
            }
            if(propType.IsInstanceOfType(typeof(IList))) {
              IList newInstance = (IList)propType.CreateInstance();

              object[] valAsArr = (object[])val;

              // Part of the check for complex types can be done outside the loop
              bool complexTypes = !MsgPackSerializer.NativelySupportedTypes.Contains(propType);
              bool intType = MsgPackMeta.NativeIntTypeFamily.Contains(propType);
              for(int i = 0; i < valAsArr.Length; i++) {
                if(complexTypes && !ReferenceEquals(valAsArr[i], null)
                  && valAsArr[i] is KeyValuePair[]) {
                  valAsArr[i] = Materialize(propType, new MpMap((KeyValuePair[])valAsArr[i]));
                } else if (intType) valAsArr[i] = MpInt.CovertIntType(propType, valAsArr[i]);
                newInstance.Add(valAsArr[i]);
              }
              props[t].SetValue(result, newInstance, null);
              continue;
            }
            if (MsgPackMeta.NativeIntTypeFamily.Contains(propType)) val = MpInt.CovertIntType(propType, val);
          }
          props[t].SetValue(result, val, null);
        }
      }

      return result;
    }

    private static PropertyInfo[] GetSerializedProps(Type type) {
      LsMsgPackMicro.Extensions.PropInf[] props = type.GetProperties();
      ArrayList keptProps = new ArrayList();
      for(int t = props.Length - 1; t >= 0; t--) {
        Type typ=props[t].PropertyType;
        if (typ.IsNotPublic) continue;
        keptProps.Add(props[t]);
      }
      return (PropertyInfo[])keptProps.ToArray(typeof(PropertyInfo));
    }

  }
}
