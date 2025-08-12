using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LsMsgPackUnitTests
{
  [TestClass]
  public class  SerializingGenericTypes
  {

    
    [TestMethod]
    [DataRow(typeof(List<string>), typeof(string), "test")]
    [DataRow(typeof(ConcurrentBag<string>), typeof(string), "test")]
    [DataRow(typeof(ObservableCollection<string>), typeof(string), "test")]

    [DataRow(typeof(List<int>), typeof(int), 42)]
    [DataRow(typeof(ConcurrentBag<int>), typeof(int), 42)]
    [DataRow(typeof(ObservableCollection<int>), typeof(int), 42)]
    public void GenericCollections(Type generic, Type item, object instance)
    {
      object collection = Activator.CreateInstance(generic);
      System.Reflection.MethodInfo addMethod = generic.GetMethod("Add");
      addMethod.Invoke(collection, new object[] { instance });
      System.Reflection.PropertyInfo countProp = generic.GetProperty("Count");

      byte[] buffer = MsgPackSerializer.Serialize(collection);

      //if(preregister)
      //  MsgPackSerializer.CacheAssemblyTypes(generic);

      object ret = MsgPackSerializer.Deserialize(generic, buffer);

      Assert.AreEqual(generic, ret.GetType());

      int count= (int)countProp.GetValue(ret);
      Assert.AreEqual(1, count);


    }
  }
}
