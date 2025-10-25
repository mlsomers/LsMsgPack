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

    public int NotNullable { get; set; }
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

    public TestContext TestContext { get; private set; }

    SerializingInheritanceHierarchy(TestContext context)
    {
      TestContext = context;
    }

    public SerializingInheritanceHierarchy()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(IIPet)); // or else the type will not be found (only needed once).
    }

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

    [DataRow(AddTypeIdOption.IfAmbiguious, false, false, 410)]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, true, 387)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, false, 302)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, true, 302)]

    [DataRow(AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName, false, false, 448)]
    [DataRow(AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName, false, true, 425)]
    [DataRow(AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName, true, false, 340)]
    [DataRow(AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName, true, true, 340)]

    [DataRow(AddTypeIdOption.Always, false, false, 450)]
    [DataRow(AddTypeIdOption.Always, false, true, 427)]
    [DataRow(AddTypeIdOption.Always, true, false, 342)]
    [DataRow(AddTypeIdOption.Always, true, true, 342)]

    [DataRow(AddTypeIdOption.Always | AddTypeIdOption.FullName, false, false, 584)]
    [DataRow(AddTypeIdOption.Always | AddTypeIdOption.FullName, false, true, 561)]
    [DataRow(AddTypeIdOption.Always | AddTypeIdOption.FullName, true, false, 476)]
    [DataRow(AddTypeIdOption.Always | AddTypeIdOption.FullName, true, true, 476)]
    public void Hirarchical_Inheritance(AddTypeIdOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      HierarchyContainer container = GetDefault();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeIdOptions = addTypeName
      };
      SetFilters(settings, omitDefault, omitNull);

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);

      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
      Assert.HasCount(expectedLength, buffer, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, false, 402)]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, true, 379)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, false, 294)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, true, 294)]

    [DataRow(AddTypeIdOption.Always, false, false, 430)]
    [DataRow(AddTypeIdOption.Always, false, true, 407)]
    [DataRow(AddTypeIdOption.Always, true, false, 322)]
    [DataRow(AddTypeIdOption.Always, true, true, 322)]
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
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer, settings);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
      Assert.HasCount(expectedLength, buffer, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
    }

    private class Resolver : IMsgPackTypeResolver
    {
      public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackSettings settings)
      {
        if (type == typeof(Dog))
          return 1;
        if (type == typeof(Cat))
          return 2;
        if (type == typeof(Dog[]))
          return 3;
        if (type == typeof(Cat[]))
          return 4;
        return null;
      }

      public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
      {
        if (typeId == null)
          return null;

        int id = Convert.ToInt32(typeId);

        switch (id)
        {
          case 1: return typeof(Dog);
          case 2: return typeof(Cat);
          case 3: return typeof(Dog[]);
          case 4: return typeof(Cat[]);
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
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer, settings);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
      Assert.HasCount(expectedLength, buffer, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
    }

    private class Resolver2 : IMsgPackTypeResolver
    {
      public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackSettings settings)
      {
        return null; // use default
      }

      public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
      {
        if (properties.ContainsKey("ClawLengthMilimeters"))
          return typeof(Cat);
        if (properties.ContainsKey("BarkingDecibels"))
          return typeof(Dog);

        return null;
      }
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, false, 626)]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, true, 603)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, false, 498)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, true, 498)]

    [DataRow(AddTypeIdOption.Always, false, false, 672)]
    [DataRow(AddTypeIdOption.Always, false, true, 649)]
    [DataRow(AddTypeIdOption.Always, true, false, 544)]
    [DataRow(AddTypeIdOption.Always, true, true, 544)]
    public void Hirarchical_NextLevel(AddTypeIdOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      NextLevelHierarchyContainer container = GetNextLevel();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeIdOptions = addTypeName,
      };
      SetFilters(settings, omitDefault, omitNull);

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      NextLevelHierarchyContainer ret = MsgPackSerializer.Deserialize<NextLevelHierarchyContainer>(buffer, settings);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
      Assert.HasCount(expectedLength, buffer, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
    }

    [TestMethod]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, false, 570)]
    [DataRow(AddTypeIdOption.IfAmbiguious, false, true, 566)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, false, 536)]
    [DataRow(AddTypeIdOption.IfAmbiguious, true, true, 536)]

    [DataRow(AddTypeIdOption.Always, false, false, 607)]
    [DataRow(AddTypeIdOption.Always, false, true, 603)]
    [DataRow(AddTypeIdOption.Always, true, false, 573)]
    [DataRow(AddTypeIdOption.Always, true, true, 573)]
    public void Hirarchical_NextLevelWithSchema(AddTypeIdOption addTypeName, bool omitDefault, bool omitNull, int expectedLength)
    {
      NextLevelHierarchyContainer container = GetNextLevel();

      MsgPackSettings settings = new MsgPackSettings()
      {
        AddTypeIdOptions = addTypeName,
      };
      SetFilters(settings, omitDefault, omitNull);

      byte[] buffer = MsgPackSerializer.SerializeWithSchema(container, settings);
      NextLevelHierarchyContainer ret = MsgPackSerializer.DeserializeWithSchema<NextLevelHierarchyContainer>(buffer, settings);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
      Assert.HasCount(expectedLength, buffer, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
    }

    [TestMethod]
    public void SchemaBecomesSmallerWithMoreData()
    {
      NextLevelHierarchyContainer container = GetNextLevel();
      string org = JsonConvert.SerializeObject(container);

      byte[] buffer = MsgPackSerializer.Serialize(container);
      NextLevelHierarchyContainer ret = MsgPackSerializer.Deserialize<NextLevelHierarchyContainer>(buffer);

      string returned = JsonConvert.SerializeObject(ret);
      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));

      byte[] bufferSchema = MsgPackSerializer.SerializeWithSchema(container);
      ret = MsgPackSerializer.DeserializeWithSchema<NextLevelHierarchyContainer>(bufferSchema);

      returned = JsonConvert.SerializeObject(ret);
      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));

      Assert.IsGreaterThan(buffer.Length, bufferSchema.Length); // with just a few items, adding a schema makes the file larger

      TestContext.WriteLine($"1 pet: Normal: {buffer.Length}  With schema: {bufferSchema.Length}  = {100d - ((buffer.Length * 100d) / bufferSchema.Length):N2} % larger");

      // -- check the difference with 100 pets

      Dictionary<int, IIPet> dict = container.Pets as Dictionary<int, IIPet>;
      for (int t = 100 - 1; t >= 4; t--)  
        dict.Add(t, dict[t % 3]);
      org = JsonConvert.SerializeObject(container);

      buffer = MsgPackSerializer.Serialize(container);
      ret = MsgPackSerializer.Deserialize<NextLevelHierarchyContainer>(buffer);

      returned = JsonConvert.SerializeObject(ret);
      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));

      bufferSchema = MsgPackSerializer.SerializeWithSchema(container);
      ret = MsgPackSerializer.DeserializeWithSchema<NextLevelHierarchyContainer>(bufferSchema);

      returned = JsonConvert.SerializeObject(ret);
      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));

      Assert.IsGreaterThan(bufferSchema.Length, buffer.Length); // with just a few items, adding a schema makes the file larger

      TestContext.WriteLine($"100 pets: Normal: {buffer.Length}  With schema: {bufferSchema.Length}  = {100d - ((bufferSchema.Length * 100d) / buffer.Length):N2} % smaller");
    }
  }
}
