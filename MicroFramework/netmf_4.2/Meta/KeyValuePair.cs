using System;

namespace LsMsgPackMicro {
  public class KeyValuePair {
    public KeyValuePair() { }
    public KeyValuePair(object key, object value) {
      Key = key;
      Value = value;
    }
    public Object Key;
    public object Value;

    public override bool Equals(object obj) {
      if (ReferenceEquals(obj, null)) return false;
      KeyValuePair ob = obj as KeyValuePair;
      if (ReferenceEquals(ob, null)) return false;
      return ((ReferenceEquals(Key, null) && ReferenceEquals(ob.Key, null)) || Key.Equals(ob.Key))
        && ((ReferenceEquals(Value, null) && ReferenceEquals(ob.Value, null)) || Key.Equals(ob.Value));
    }

    public override int GetHashCode() {
      int hash = base.GetHashCode();
      if (!ReferenceEquals(Key, null)) hash = hash ^ Key.GetHashCode();
      if (!ReferenceEquals(Value, null)) hash = hash + 1 ^ Value.GetHashCode();
      return hash;
    }
  }
}
