using LsMsgPack;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Linq;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
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


  [TestFixture]
  public class SerializingInheritanceHierarchy
  {
    [TestCase(AddTypeNameOption.IfAmbiguious, false, false, 203)]
    [TestCase(AddTypeNameOption.IfAmbiguious, false, true, 170)]
    [TestCase(AddTypeNameOption.IfAmbiguious, true, false, 170)]
    [TestCase(AddTypeNameOption.IfAmbiguious, true, true, 170)]

    [TestCase(AddTypeNameOption.IfAmbiguiousFullName, false, false, 241)]
    [TestCase(AddTypeNameOption.IfAmbiguiousFullName, false, true, 208)]
    [TestCase(AddTypeNameOption.IfAmbiguiousFullName, true, false, 208)]
    [TestCase(AddTypeNameOption.IfAmbiguiousFullName, true, true, 208)]

    [TestCase(AddTypeNameOption.Always, false, false, 208)]
    [TestCase(AddTypeNameOption.Always, false, true, 175)]
    [TestCase(AddTypeNameOption.Always, true, false, 175)]
    [TestCase(AddTypeNameOption.Always, true, true, 175)]
    
    [TestCase(AddTypeNameOption.AlwaysFullName, false, false, 265)]
    [TestCase(AddTypeNameOption.AlwaysFullName, false, true, 232)]
    [TestCase(AddTypeNameOption.AlwaysFullName, true, false, 232)]
    [TestCase(AddTypeNameOption.AlwaysFullName, true, true, 232)]
    
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

    [TestCase(AddTypeNameOption.UseCustomIdWhenAmbiguious, false, false, 199)]
    [TestCase(AddTypeNameOption.UseCustomIdWhenAmbiguious, false, true, 166)]
    [TestCase(AddTypeNameOption.UseCustomIdWhenAmbiguious, true, false, 166)]
    [TestCase(AddTypeNameOption.UseCustomIdWhenAmbiguious, true, true, 166)]

    [TestCase(AddTypeNameOption.UseCustomIdAlways, false, false, 202)]
    [TestCase(AddTypeNameOption.UseCustomIdAlways, false, true, 169)]
    [TestCase(AddTypeNameOption.UseCustomIdAlways, true, false, 169)]
    [TestCase(AddTypeNameOption.UseCustomIdAlways, true, true, 169)]
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

    [TestCase(AddTypeNameOption.Never, false, false, 193)]
    [TestCase(AddTypeNameOption.Never, false, true, 160)]
    [TestCase(AddTypeNameOption.Never, true, false, 160)]
    [TestCase(AddTypeNameOption.Never, true, true, 160)]
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
