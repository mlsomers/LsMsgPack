using LsMsgPack;
using LsMsgPack.TypeResolving;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

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
    public iPet[] PetInterface { get; set; }

    public MyPetBaseClass PetBaseClass { get; set; }

    public Cat ExplicitlyCat { get; set; }

    public Dog ExplicitDog { get; set; }

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

    [TestMethod]

    [DataRow(AddTypeNameOption.IfAmbiguious, false, false, 415)]
    [DataRow(AddTypeNameOption.IfAmbiguious, false, true, 392)]
    [DataRow(AddTypeNameOption.IfAmbiguious, true, false, 307)]
    [DataRow(AddTypeNameOption.IfAmbiguious, true, true, 307)]

    [DataRow(AddTypeNameOption.IfAmbiguiousFullName, false, false, 491)]
    [DataRow(AddTypeNameOption.IfAmbiguiousFullName, false, true, 468)]
    [DataRow(AddTypeNameOption.IfAmbiguiousFullName, true, false, 383)]
    [DataRow(AddTypeNameOption.IfAmbiguiousFullName, true, true, 383)]

    [DataRow(AddTypeNameOption.Always, false, false, 420)]
    [DataRow(AddTypeNameOption.Always, false, true, 397)]
    [DataRow(AddTypeNameOption.Always, true, false, 312)]
    [DataRow(AddTypeNameOption.Always, true, true, 312)]

    [DataRow(AddTypeNameOption.AlwaysFullName, false, false, 515)]
    [DataRow(AddTypeNameOption.AlwaysFullName, false, true, 492)]
    [DataRow(AddTypeNameOption.AlwaysFullName, true, false, 407)]
    [DataRow(AddTypeNameOption.AlwaysFullName, true, true, 407)]
    public void Hirarchical_Inheritance(AddTypeNameOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = GetDefault();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeName = addTypeName
      };
      if (!omitDefault)
        settings.DynamicFilters.Clear();
      if (omitNull)
        settings.DynamicFilters.Add(new FilterNullValues());

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
    }

    [TestMethod]
    [DataRow(AddTypeNameOption.UseCustomIdWhenAmbiguious, false, false, 407)]
    [DataRow(AddTypeNameOption.UseCustomIdWhenAmbiguious, false, true, 384)]
    [DataRow(AddTypeNameOption.UseCustomIdWhenAmbiguious, true, false, 299)]
    [DataRow(AddTypeNameOption.UseCustomIdWhenAmbiguious, true, true, 299)]

    [DataRow(AddTypeNameOption.UseCustomIdAlways, false, false, 410)]
    [DataRow(AddTypeNameOption.UseCustomIdAlways, false, true, 387)]
    [DataRow(AddTypeNameOption.UseCustomIdAlways, true, false, 302)]
    [DataRow(AddTypeNameOption.UseCustomIdAlways, true, true, 302)]
    public void Hirarchical_CustomId(AddTypeNameOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = GetDefault();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeName = addTypeName,
      };
      if (!omitDefault)
        settings.DynamicFilters.Clear();
      if (omitNull)
        settings.DynamicFilters.Add(new FilterNullValues());

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
      public string IdForType(Type type, FullPropertyInfo assignedTo)
      {
        if (type == typeof(Dog))
          return "1";
        if (type == typeof(Cat))
          return "2";
        return null;
      }

      public Type Resolve(string typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<string, object> properties)
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
    [DataRow(AddTypeNameOption.Never, false, false, 395)]
    [DataRow(AddTypeNameOption.Never, false, true, 372)]
    [DataRow(AddTypeNameOption.Never, true, false, 287)]
    [DataRow(AddTypeNameOption.Never, true, true, 287)]
    public void Hirarchical_ResolveBySignature(AddTypeNameOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = GetDefault();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeName = addTypeName,
      };
      if (!omitDefault)
        settings.DynamicFilters.Clear();
      if (omitNull)
        settings.DynamicFilters.Add(new FilterNullValues());
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
      public Type Resolve(string typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<string, object> properties)
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
