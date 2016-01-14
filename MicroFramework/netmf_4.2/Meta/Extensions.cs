using System;
using System.Collections;
using System.Reflection;
using System.Text;

namespace LsMsgPackMicro {
  static class Extensions {

    public static void AddRange(this ArrayList list, byte[] bytes){
      for (int t = 0; t < bytes.Length; t++) {
        list.Add(bytes[t]);
      }
    }

    public static void AddRange(this ArrayList list, object[] bytes) {
      for (int t = 0; t < bytes.Length; t++) {
        list.Add(bytes[t]);
      }
    }

    public static bool Contains(this Array array, object value) {
      bool isnull=ReferenceEquals(value,null);
      for (int t = array.Length - 1; t >= 0; t--) {
        object item = array.GetValue(t);
        if (ReferenceEquals(item, null)) {
          if (isnull) return true;
        }
        else if (item.Equals(value)) return true;
      }
      return false;
    }

    public static bool TryGetValue(this KeyValuePair[] dictionary, object key, out object value) {
      bool isnull = ReferenceEquals(key, null);
      for (int t = dictionary.Length - 1; t >= 0; t--) {
        if (ReferenceEquals(dictionary[t], null)) continue;
        if (ReferenceEquals(dictionary[t].Key, null) && isnull) {
          value = dictionary[t].Value;
          return true;
        }
        if (dictionary[t].Key.Equals(key)) {
          value = dictionary[t].Value;
          return true;
        }
      }
      value = null;
      return false;
    }

    public class PropInf : PropertyInfo {
      public PropInf(Type propType, Type decType, string name, MethodInfo getter, MethodInfo setter) {
        this.propType = propType;
        this.decType = decType;
        this.name = name;
        this.getter = getter;
        this.setter = setter;
      }

      private Type propType;
      private Type decType;
      private string name;
      private MethodInfo getter;
      private MethodInfo setter;

      public override Type PropertyType {
        get { return propType; }
      }

      public override Type DeclaringType {
        get { return decType; }
      }

      public override MemberTypes MemberType {
        get { return MemberTypes.Property; }
      }

      public override string Name {
        get { return name; }
      }

      public override object GetValue(object obj, object[] index) {
        return getter.Invoke(obj, index);
        //return base.GetValue(obj, index);
      }

      public override void SetValue(object obj, object value, object[] index) {
        //base.SetValue(obj, value, index);
        setter.Invoke(obj, new object[] { value });
      }
    }

    public static PropInf[] GetProperties(this Type type) {
      MethodInfo[] methods = type.GetMethods();
      ArrayList props = new ArrayList();
      for (int t = methods.Length - 1; t >= 0; t--) {
        MethodInfo gm = methods[t];
        if (gm.Name.IndexOf("get_")==0) {
          if (gm.IsAbstract) continue;

          if (gm.IsAbstract ||
             (gm.ReturnType == typeof(System.Delegate)) ||
             (gm.ReturnType == typeof(System.MulticastDelegate)) ||
             (gm.ReturnType == typeof(System.Reflection.MethodInfo)) ||
             (gm.DeclaringType == typeof(System.Delegate)) ||
             (gm.DeclaringType == typeof(System.MulticastDelegate))) {
            continue;
          }

          string name = gm.Name.Substring(4);

          string setName = "set_" + name;
          MethodInfo sm = null;
          bool found = false;
          for (int s = methods.Length - 1; s >= 0; s--) {
            if (methods[s].Name == setName) {
              sm = methods[s];
              if (sm.IsAbstract) break;
              found = true;
              break;
            }
          }
          if (!found) continue;

          PropInf inf = new PropInf(gm.ReturnType, gm.DeclaringType, name, gm, sm);
          props.Add(inf);
        }
      }
      return (PropInf[])props.ToArray(typeof(PropInf));
    }

    public static object CreateInstance(this Type type) {
      ConstructorInfo constructor = type.GetConstructor(new Type[0]);
      return constructor.Invoke(new object[0]);
    }
    
    public static object CreateInstance(this Type type, params object[] args) {
      Type[] argTypes = new Type[args.Length];
      for (int t = argTypes.Length - 1; t >= 0; t--) argTypes[t] = args[t].GetType();
      ConstructorInfo constructor = type.GetConstructor(argTypes);
      return constructor.Invoke(args);
    }

  }
}
