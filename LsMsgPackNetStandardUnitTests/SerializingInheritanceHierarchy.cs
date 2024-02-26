using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace LsMsgPackNetStandardUnitTests
{
  public interface iPet
  {
    string Name { get; set; }
  }

  public abstract class MyPetBaseClass : iPet
  {
    public string Name { get; set; }
    public int? AgeYears { get; set; }
  }

  public class Cat : MyPetBaseClass
  {
    public float ClawLengthMilimeters { get; set; }
  }

  public class Dog : MyPetBaseClass
  {
    public double BarkingDecibels { get; set; }
  }

  public class HierarchyContainer
  {
    public iPet PetInterface { get; set; }

    public MyPetBaseClass PetBaseClass { get; set; }

    public Cat ExplicitlyCat { get; set; }

    public Dog ExplicitDog { get; set; }
  }


  [TestClass]
  public class SerializingInheritanceHierarchy
  {

    [TestMethod]

    [DataRow(AddTypeNameOption.IfAmbiguious, false, false, 203)]
    [DataRow(AddTypeNameOption.IfAmbiguious, false, true, 170)]
    [DataRow(AddTypeNameOption.IfAmbiguious, true, false, 170)]
    [DataRow(AddTypeNameOption.IfAmbiguious, true, true, 170)]

    [DataRow(AddTypeNameOption.IfAmbiguiousFullName, false, false, 265)]
    [DataRow(AddTypeNameOption.IfAmbiguiousFullName, false, true, 232)]
    [DataRow(AddTypeNameOption.IfAmbiguiousFullName, true, false, 232)]
    [DataRow(AddTypeNameOption.IfAmbiguiousFullName, true, true, 232)]

    [DataRow(AddTypeNameOption.Always, false, false, 208)]
    [DataRow(AddTypeNameOption.Always, false, true, 175)]
    [DataRow(AddTypeNameOption.Always, true, false, 175)]
    [DataRow(AddTypeNameOption.Always, true, true, 175)]

    [DataRow(AddTypeNameOption.AlwaysFullName, false, false, 301)]
    [DataRow(AddTypeNameOption.AlwaysFullName, false, true, 268)]
    [DataRow(AddTypeNameOption.AlwaysFullName, true, false, 268)]
    [DataRow(AddTypeNameOption.AlwaysFullName, true, true, 268)]
    public void Hirarchical_Inheritance(AddTypeNameOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = new HierarchyContainer()
      {
        ExplicitlyCat = new Cat() { Name = "Mia", ClawLengthMilimeters = 2.2f },
        PetBaseClass = new Dog() { Name = "Haw", BarkingDecibels = 76.3, AgeYears = 3 },
        PetInterface = new Dog() { Name = "Polcoy", BarkingDecibels = 132.8 }
      };

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeName = addTypeName,
        OmitDefault = omitDefault,
        OmitNull = omitNull,
      };

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer);
      
      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
    }

    [TestMethod]
    [DataRow(AddTypeNameOption.UseCustomIdWhenAmbiguious, false, false, 199)]
    [DataRow(AddTypeNameOption.UseCustomIdWhenAmbiguious, false, true, 166)]
    [DataRow(AddTypeNameOption.UseCustomIdWhenAmbiguious, true, false, 166)]
    [DataRow(AddTypeNameOption.UseCustomIdWhenAmbiguious, true, true, 166)]

    [DataRow(AddTypeNameOption.UseCustomIdAlways, false, false, 202)]
    [DataRow(AddTypeNameOption.UseCustomIdAlways, false, true, 169)]
    [DataRow(AddTypeNameOption.UseCustomIdAlways, true, false, 169)]
    [DataRow(AddTypeNameOption.UseCustomIdAlways, true, true, 169)]
    public void Hirarchical_CustomId(AddTypeNameOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = new HierarchyContainer()
      {
        ExplicitlyCat = new Cat() { Name = "Mia", ClawLengthMilimeters = 2.2f },
        PetBaseClass = new Dog() { Name = "Haw", BarkingDecibels = 76.3, AgeYears = 3 },
        PetInterface = new Dog() { Name = "Polcoy", BarkingDecibels = 132.8 }
      };

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeName = addTypeName,
        OmitDefault = omitDefault,
        OmitNull = omitNull,
      };
      Resolver res = new Resolver();
      settings.TypeIdentifiers.Add(res);
      settings.TypeResolvers.Add(res);

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer, settings);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
    }

    private class Resolver : IMsgPackTypeResolver, IMsgPackTypeIdentifier
    {
      public string IdForType(Type type)
      {
        if (type == typeof(Dog))
          return "1";
        if (type == typeof(Cat))
          return "2";
        return null;
      }

      public Type Resolve(string typeId, Type assignedTo, Dictionary<string, object> properties)
      {
        if (typeId == null)
          return null;
        int id;
        if (int.TryParse(typeId, out id))
        {
          switch (id)
          {
            case 1: return typeof(Dog);
            case 2: return typeof(Cat);
          }
        }
        return null;
      }
    }

    [TestMethod]
    [DataRow(AddTypeNameOption.Never, false, false, 193)]
    [DataRow(AddTypeNameOption.Never, false, true, 160)]
    [DataRow(AddTypeNameOption.Never, true, false, 160)]
    [DataRow(AddTypeNameOption.Never, true, true, 160)]
    public void Hirarchical_ResolveBySignature(AddTypeNameOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = new HierarchyContainer()
      {
        ExplicitlyCat = new Cat() { Name = "Mia", ClawLengthMilimeters = 2.2f },
        PetBaseClass = new Dog() { Name = "Haw", BarkingDecibels = 76.3, AgeYears = 3 },
        PetInterface = new Dog() { Name = "Polcoy", BarkingDecibels = 132.8 }
      };

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeName = addTypeName,
        OmitDefault = omitDefault,
        OmitNull = omitNull,
      };
      Resolver2 res = new Resolver2();
      settings.TypeResolvers.Add(res);

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer, settings);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
    }

    private class Resolver2 : IMsgPackTypeResolver
    {
      public Type Resolve(string typeId, Type assignedTo, Dictionary<string, object> properties)
      {
        if (properties.ContainsKey("ClawLengthMilimeters"))
          return typeof(Cat);
        if (properties.ContainsKey("BarkingDecibels"))
          return typeof(Dog);

        return null;
      }
    }
  }
}
