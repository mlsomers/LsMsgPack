using LsMsgPack;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace LsMsgPackUnitTests {
  [TestFixture]
  public class SerializingObjects {

    public class MyComplexObject {

      public string Name { get; set; }
      public int Value { get; set; }

      public MyComplexObject ChildObject { get; set; }

      public int[] ArrayOfInts { get; set; }

      [XmlIgnore]
      public int runtimeThing { get; set; }

      public MyComplexObject[] ArrayOfChildObject { get; set; }

      public int? NullableType { get; set; }
      public int? NullableTypeWithValue { get; set; }

      public DateTime Date { get; set; }
    }

    [Test]
    public void Hirarchical_Integration_test() {

      MyComplexObject testObj = new MyComplexObject() {
        Name = "Root",
        Value = 1,
        runtimeThing = 30,// should not be preserved
        ArrayOfInts = new int[] { 1, 2, 3, 4 },
        ChildObject = new MyComplexObject() {
          Name = "branch",
          Value = 3,
          ArrayOfInts = new int[] { 999, 200 },
          ArrayOfChildObject = new MyComplexObject[] {
            new MyComplexObject() {Name="1st ArrayItem"},
            new MyComplexObject() {Name="2nd ArrayItem"},
            new MyComplexObject() {Name="3rd ArrayItem, with more than 31 chars (extra byte needed)"},
            null,
            new MyComplexObject() {Name="5th ArrayItem, with a string that takes more than 255 bytes so we need two bytes to contain the length of this nice and lengthy example, and we are not there yet... well it is a little chatty example I must admit, but we'll get there eventually, maybe, Yes!"},
          }
        },
        Date = new DateTime(2021, 3, 5, 11, 32, 20),
        NullableTypeWithValue = 3
      };

      byte[] buffer = MsgPackSerializer.Serialize(testObj);

      MyComplexObject ret = MsgPackSerializer.Deserialize<MyComplexObject>(buffer);

      Assert.AreEqual(testObj.Name, ret.Name);
      Assert.AreEqual(testObj.Value, ret.Value);
      Assert.AreNotEqual(testObj.runtimeThing, ret.runtimeThing);

      Assert.AreEqual(testObj.ChildObject.Name, ret.ChildObject.Name);
      Assert.AreEqual(testObj.ChildObject.ArrayOfChildObject[2].Name, ret.ChildObject.ArrayOfChildObject[2].Name);
      Assert.AreEqual(testObj.ChildObject.ArrayOfChildObject[3], ret.ChildObject.ArrayOfChildObject[3]);
      Assert.AreEqual(testObj.ChildObject.ArrayOfChildObject[4].Name, ret.ChildObject.ArrayOfChildObject[4].Name);
      Assert.AreEqual(testObj.NullableType, ret.NullableType);
      Assert.AreEqual(testObj.NullableTypeWithValue, ret.NullableTypeWithValue);
      Assert.AreEqual(testObj.Date, ret.Date);
    }

    class MyClass {
      public string Name { get; set; }
      public int Quantity { get; set; }
      public List<object> Anything { get; set; }
    }

    [Test]
    public void small_test() {
      MyClass message = new MyClass() {
        Name = "TestMessage",
        Quantity = 35,
        Anything = new List<object>(new object[] { "First", 2, false, null, 5.5d, "last" })
      };

      // Serialize
      byte[] buffer = MsgPackSerializer.Serialize(message);

      // Deserialize
      MyClass creceiveMessage = MsgPackSerializer.Deserialize<MyClass>(buffer);

      Assert.AreEqual(message.Name, creceiveMessage.Name);
      Assert.AreEqual(message.Quantity, creceiveMessage.Quantity);
      Assert.AreEqual(message.Anything, creceiveMessage.Anything);
    }
  }
}
