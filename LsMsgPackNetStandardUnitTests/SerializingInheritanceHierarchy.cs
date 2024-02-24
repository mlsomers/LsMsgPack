using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

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
    
    [DataRow(AddTypeNameOption.IfAmbiguious, false, false, false, 203)]
    [DataRow(AddTypeNameOption.IfAmbiguious, false, false, true, 265)]
    [DataRow(AddTypeNameOption.IfAmbiguious, false, true, false, 170)]
    [DataRow(AddTypeNameOption.IfAmbiguious, false, true, true, 232)]
    [DataRow(AddTypeNameOption.IfAmbiguious, true, false, false, 170)]
    [DataRow(AddTypeNameOption.IfAmbiguious, true, false, true, 232)]
    [DataRow(AddTypeNameOption.IfAmbiguious, true, true, false, 170)]
    [DataRow(AddTypeNameOption.IfAmbiguious, true, true, true, 232)]

    [DataRow(AddTypeNameOption.Always, false, false, false, 208)]
    [DataRow(AddTypeNameOption.Always, false, false, true, 301)]
    [DataRow(AddTypeNameOption.Always, false, true, false, 175)]
    [DataRow(AddTypeNameOption.Always, false, true, true, 268)]
    [DataRow(AddTypeNameOption.Always, true, false, false, 175)]
    [DataRow(AddTypeNameOption.Always, true, false, true, 268)]
    [DataRow(AddTypeNameOption.Always, true, true, false, 175)]
    [DataRow(AddTypeNameOption.Always, true, true, true, 268)]
    
    public void Hirarchical_Inheritance(AddTypeNameOption addTypeName, bool omitDefault, bool omitNull, bool useFullName, int expectedLength)
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
        FullName = useFullName
      };

      byte[] buffer = MsgPackSerializer.Serialize(container, settings);
      Assert.AreEqual(expectedLength, buffer.Length, string.Concat("Expected ", expectedLength, " bytes but got ", buffer.Length, " bytes."));
      HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer);

      string returned = JsonConvert.SerializeObject(ret);
      string org = JsonConvert.SerializeObject(container);

      Assert.AreEqual(org, returned, string.Concat("Not equal, Original - returned:\r\n", org, "\r\n", returned));
    }
  }
}
