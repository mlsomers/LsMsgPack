using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LsMsgPackUnitTests
{
  public interface IIPet
  {
    string Name { get; set; }
  }

  public abstract class MyPetBaseClass : IIPet
  {
    public string Name { get; set; }
    public int? AgeYears { get; set; }
  }

  public class Cat : MyPetBaseClass
  {
    public float ClawLengthMilimeters { get; set; }

    public int NotNullable {  get; set; }
  }

  public class Dog : MyPetBaseClass
  {
    public double BarkingDecibels { get; set; }

    [DefaultValue(7)]
    public int Standard { get; set; } = 7;

    [DefaultValue("Woof")]
    public string BarkVerb { get; set; } = "Woof"; // Null should be serialized here!
  }

  public class HierarchyContainer
  {
    public IIPet[] PetInterface { get; set; }

    public MyPetBaseClass PetBaseClass { get; set; }

    public Cat ExplicitlyCat { get; set; }

    public Dog ExplicitDog { get; set; }

  }

  public class NextLevelHierarchyContainer : HierarchyContainer
  {
    public object Pets { get; set; }
  }


  [TestClass]
  public class SerializingInheritanceHierarchy
  {

    public static HierarchyContainer GetDefault()
    {
      return new HierarchyContainer()
      {
        ExplicitlyCat = new Cat() { Name = "Mia", ClawLengthMilimeters = 2.2f },
        PetBaseClass = new Dog() { Name = "Haw", BarkingDecibels = 76.3, AgeYears = 3 },
        PetInterface = new[] {
          new Dog() { Name = "Polcoy", BarkingDecibels = 132.8, AgeYears = 0 },
          new Dog() { Name = "Polcoy", BarkingDecibels = 132.8, AgeYears = 0, Standard = 6 },
          new Dog() { Name = "Polcoy", BarkingDecibels = 132.8, AgeYears = 0, BarkVerb=null },
        }
      };
    }

    public static NextLevelHierarchyContainer GetNextLevel()
    {
      return new NextLevelHierarchyContainer()
      {
        ExplicitlyCat = new Cat() { Name = "M", ClawLengthMilimeters = 2.2f },
        PetBaseClass = new Dog() { Name = "H", BarkingDecibels = 76.3, AgeYears = 3 },
        PetInterface = new IIPet[] {
          new Dog() { Name = "A", BarkingDecibels = 132.8, AgeYears = 0 },
          new Cat() { Name = "B", AgeYears = 0, NotNullable = 9 },
          new Dog() { Name = "C", BarkingDecibels = 132.8, AgeYears = 0, BarkVerb = null },
        },
        Pets = new Dictionary<int, IIPet>(new[] {
          new KeyValuePair<int, IIPet>(0, new Dog() { Name = "D", BarkingDecibels = 132.8, AgeYears = 0 }),
          new KeyValuePair<int, IIPet>(1, new Cat() { Name = "E", AgeYears = 0, NotNullable = 9 }),
          new KeyValuePair<int, IIPet>(2, new Dog() { Name = "F", BarkingDecibels = 132.8, AgeYears = 0, BarkVerb = null })
          }
        )
      };
    }

    private void SetFilters(MsgPackSettings settings, bool omitDefault, bool omitNull)
    {
      if (!omitDefault)
        settings.DynamicFilters = Array.Empty<IMsgPackPropertyIncludeDynamically>();
      if (omitNull)
        settings.DynamicFilters = new[] { new FilterNullValues() };
      if (omitDefault && omitNull)
        settings.DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues(), new FilterNullValues() };
    }


    [TestMethod]

    [DataRow(AddTypeIdOption.IfAmbiguious, false, false, 415)]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, true, 392)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, false, 307)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, true, 307)]

    [DataRow(AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName, false, false, 491)]
    [DataRow(AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName, false, true, 468)]
    [DataRow(AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName, true, false, 383)]
    [DataRow(AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName, true, true, 383)]

    [DataRow(AddTypeIdOption.Always, false, false, 420)]
    [DataRow(AddTypeIdOption.Always, false, true, 397)]
    [DataRow(AddTypeIdOption.Always, true, false, 312)]
    [DataRow(AddTypeIdOption.Always, true, true, 312)]

    [DataRow(AddTypeIdOption.Always | AddTypeIdOption.FullName, false, false, 515)]
    [DataRow(AddTypeIdOption.Always | AddTypeIdOption.FullName, false, true, 492)]
    [DataRow(AddTypeIdOption.Always | AddTypeIdOption.FullName, true, false, 407)]
    [DataRow(AddTypeIdOption.Always | AddTypeIdOption.FullName, true, true, 407)]
    public void Hirarchical_Inheritance(AddTypeIdOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = GetDefault();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeIdOptions = addTypeName
      };
      SetFilters(settings, omitDefault, omitNull);

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, false, 403)]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, true, 380)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, false, 295)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, true, 295)]

    [DataRow(AddTypeIdOption.Always, false, false, 405)]
    [DataRow(AddTypeIdOption.Always, false, true, 382)]
    [DataRow(AddTypeIdOption.Always, true, false, 297)]
    [DataRow(AddTypeIdOption.Always, true, true, 297)]
    public void Hirarchical_CustomId(AddTypeIdOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = GetDefault();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeIdOptions = addTypeName,
      };
      SetFilters(settings, omitDefault, omitNull);

      Resolver res = new Resolver();
      settings.TypeResolvers = new[] { res };

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer, settings);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
    }

    private class Resolver : IMsgPackTypeResolver
    {
      public object IdForType(Type type, FullPropertyInfo assignedTo)
      {
        if (type == typeof(Dog))
          return 1;
        if (type == typeof(Cat))
          return 2;
        return null;
      }

      public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<string, object> properties)
      {
        if (typeId == null)
          return null;

        int id= Convert.ToInt32(typeId);

        switch (id)
        {
          case 1: return typeof(Dog);
          case 2: return typeof(Cat);
        }
        
        return null;
      }
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.Never, false, false, 395)]
    [DataRow(AddTypeIdOption.Never, false, true, 372)]
    [DataRow(AddTypeIdOption.Never, true, false, 287)]
    [DataRow(AddTypeIdOption.Never, true, true, 287)]
    public void Hirarchical_ResolveBySignature(AddTypeIdOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = GetDefault();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeIdOptions = addTypeName,
      };
      SetFilters(settings, omitDefault, omitNull);
      Resolver2 res = new Resolver2();
      settings.TypeResolvers = new[] { res };

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer, settings);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
    }

    private class Resolver2 : IMsgPackTypeResolver
    {
      public object IdForType(Type type, FullPropertyInfo assignedTo)
      {
        return null; // use default
      }

      public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<string, object> properties)
      {
        if (properties.ContainsKey("ClawLengthMilimeters"))
          return typeof(Cat);
        if (properties.ContainsKey("BarkingDecibels"))
          return typeof(Dog);

        return null;
      }
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, false, 612)]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, true, 589)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, false, 484)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, true, 484)]

    [DataRow(AddTypeIdOption.Always, false, false, 617)]
    [DataRow(AddTypeIdOption.Always, false, true, 594)]
    [DataRow(AddTypeIdOption.Always, true, false, 489)]
    [DataRow(AddTypeIdOption.Always, true, true, 489)]
    public void Hirarchical_NextLevel(AddTypeIdOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      NextLevelHierarchyContainer container = GetNextLevel();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeIdOptions = addTypeName,
      };
      SetFilters(settings, omitDefault, omitNull);

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      //Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
      MsgPackSerializer.CacheAssemblyTypes(typeof(IIPet)); // or else the type will not be found (only needed once).
      NextLevelHierarchyContainer ret = MsgPackSerializer.Deserialize<NextLevelHierarchyContainer>(buffer, settings);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
    }
  }
}
