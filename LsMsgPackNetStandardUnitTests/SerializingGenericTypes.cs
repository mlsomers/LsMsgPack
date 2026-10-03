using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;


namespace LsMsgPackUnitTests
{
  public abstract class SerializingGenericTypes
  {
    protected abstract ISerializerUnderTest Serializer { get; }


    
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

      MsgPackSettings settings = new MsgPackSettings(){ UseInexedSchema=false};

      byte[] buffer = Serializer.Serialize(collection, settings);

      //if(preregister)
      //  MsgPackSerializer.CacheAssemblyTypes(generic);

      object ret = Serializer.Deserialize(generic, buffer, settings);

      Assert.AreEqual(generic, ret.GetType());

      int count= (int)countProp.GetValue(ret);
      Assert.AreEqual(1, count);


    }
  }

  [TestClass]
  public class LsSerializingGenericTypes : SerializingGenericTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingGenericTypes : SerializingGenericTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
