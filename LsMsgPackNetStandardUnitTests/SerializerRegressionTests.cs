using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Serializer / deserializer entry points, the indexed schema and type resolving.
  /// </summary>
  [TestClass]
  public class SerializerRegressionTests
  {
    public SerializerRegressionTests()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(IIPet));
    }

    public class WithArrayList { public ArrayList Items { get; set; } }

    public class IntList : List<int> { } // No generic arguments on the type itself

    public class WithIntList { public IntList Items { get; set; } }

    public class PetBox
    {
      public IIPet Pet { get; set; }
      public object Thing { get; set; }
    }

    public class Other { public string X { get; set; } }

    [XmlRoot("kitty")]
    public class XmlCat { public string Name { get; set; } }

    public class WithSorted { public object Sorted { get; set; } }

    public class BaseWithX { public int X { get; set; } }

    public class HidesX : BaseWithX { public new string X { get; set; } }

    public class WithIndexer
    {
      public string Name { get; set; }
      public int this[int i] { get { return i; } set { } }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeserializeByTypeRespectsUseIndexedSchema(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      Cat org = new Cat() { Name = "Mia", ClawLengthMilimeters = 2 };

      byte[] buffer = MsgPackSerializer.Serialize(org, settings);
      Cat ret = (Cat)MsgPackSerializer.Deserialize(typeof(Cat), buffer, settings);
      Assert.AreEqual("Mia", ret.Name);

      MemoryStream ms = new MemoryStream();
      MsgPackSerializer.Serialize(org, ms, settings);
      ms.Position = 0;
      ret = (Cat)MsgPackSerializer.Deserialize(typeof(Cat), ms, settings);
      Assert.AreEqual("Mia", ret.Name);
    }

    [TestMethod]
    public void DeserializeByTypeWithDefaultSettings()
    {
      byte[] buffer = MsgPackSerializer.Serialize(new Cat() { Name = "Mia" });
      Assert.AreEqual("Mia", ((Cat)MsgPackSerializer.Deserialize(typeof(Cat), buffer)).Name);
    }

    [TestMethod]
    public void NullSettingsUseDefaults()
    {
      byte[] buffer = MsgPackSerializer.Serialize(new Cat() { Name = "Mia" }, (MsgPackSettings)null);
      Assert.AreEqual("Mia", MsgPackSerializer.Deserialize<Cat>(buffer, (MsgPackSettings)null).Name);

      MemoryStream ms = new MemoryStream();
      MsgPackSerializer.Serialize(new Cat() { Name = "Mia" }, ms, (MsgPackSettings)null);
      CollectionAssert.AreEqual(buffer, ms.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NullIsSerializedTheSameToBytesAndStream(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      MemoryStream ms = new MemoryStream();
      MsgPackSerializer.Serialize<Cat>(null, ms, settings);

      CollectionAssert.AreEqual(MsgPackSerializer.Serialize<Cat>(null, settings), ms.ToArray());
      CollectionAssert.AreEqual(new byte[] { (byte)MsgPackTypeId.MpNull }, ms.ToArray());
    }

    [TestMethod]
    public void ClonePreservesUseIndexedSchema()
    {
      Assert.IsFalse(new MsgPackSettings() { UseInexedSchema = false }.Clone().UseInexedSchema);
      Assert.IsTrue(new MsgPackSettings() { UseInexedSchema = true }.Clone().UseInexedSchema);
    }

    [TestMethod]
    public void SchemaDoesNotModifySettings()
    {
      IMsgPackTypeResolver[] resolvers = new IMsgPackTypeResolver[0];
      IMsgPackPropertyIdResolver[] propResolvers = new IMsgPackPropertyIdResolver[0];
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = true, TypeResolvers = resolvers, PropertyNameResolvers = propResolvers };

      byte[] buffer = MsgPackSerializer.Serialize(new Cat() { Name = "Mia" }, settings);
      MsgPackSerializer.Deserialize<Cat>(buffer, settings);

      Assert.AreSame(resolvers, settings.TypeResolvers);
      Assert.AreSame(propResolvers, settings.PropertyNameResolvers);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SettingsCanBeSharedBetweenThreads(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      Parallel.For(0, 1000, i =>
      {
        HierarchyContainer org = new HierarchyContainer()
        {
          ExplicitlyCat = new Cat() { Name = $"Cat {i}" },
          PetBaseClass = new Dog() { Name = $"Dog {i}" },
        };
        byte[] buffer = MsgPackSerializer.Serialize(org, settings);
        HierarchyContainer ret = MsgPackSerializer.Deserialize<HierarchyContainer>(buffer, settings);

        Assert.AreEqual($"Cat {i}", ret.ExplicitlyCat.Name);
        Assert.AreEqual($"Dog {i}", ((Dog)ret.PetBaseClass).Name);
      });
    }

    [TestMethod]
    public void SchemaPrimitiveRoots()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = true };
      Assert.AreEqual(70000, MsgPackSerializer.Deserialize<int>(MsgPackSerializer.Serialize(70000, settings), settings));
      Assert.AreEqual("x", MsgPackSerializer.Deserialize<string>(MsgPackSerializer.Serialize("x", settings), settings));
    }

    [TestMethod]
    public void ReadingDataWithoutSchemaUsingSchemaSettingsThrows()
    {
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false };
      MsgPackSettings withSchema = new MsgPackSettings() { UseInexedSchema = true };

      byte[] array = MsgPackSerializer.Serialize(new[] { 1, 2 }, withoutSchema); // { "@": [1, 2] }
      Assert.ThrowsExactly<MsgPackException>(() => MsgPackSerializer.Deserialize<int[]>(array, withSchema));

      byte[] number = MsgPackSerializer.Serialize(5, withoutSchema); // Used to silently return default
      Assert.ThrowsExactly<MsgPackException>(() => MsgPackSerializer.Deserialize<int>(number, withSchema));
    }

    /// <summary>
    /// The schema's type id's used to be resolved by custom resolvers first, which could return a completely different type for the same number.
    /// </summary>
    [TestMethod]
    public void SchemaTypeIdsDoNotCollideWithCustomResolverIds()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = true, TypeResolvers = new IMsgPackTypeResolver[] { new NumberedPets() } };

      byte[] buffer = MsgPackSerializer.Serialize(new PetBox() { Pet = new Cat() { Name = "Mia" }, Thing = new Other() { X = "x" } }, settings);
      PetBox ret = MsgPackSerializer.Deserialize<PetBox>(buffer, settings);

      Assert.IsInstanceOfType<Cat>(ret.Pet);
      Assert.IsInstanceOfType<Other>(ret.Thing);
      Assert.AreEqual("x", ((Other)ret.Thing).X);
    }

    [TestMethod]
    [DataRow(false, AddTypeIdOption.IfAmbiguious)]
    [DataRow(false, AddTypeIdOption.Always)]
    [DataRow(true, AddTypeIdOption.IfAmbiguious)]
    [DataRow(true, AddTypeIdOption.Always)]
    public void XmlRootAttributeTypeResolver(bool useSchema, AddTypeIdOption option)
    {
      MsgPackSettings settings = new MsgPackSettings()
      {
        UseInexedSchema = useSchema,
        AddTypeIdOptions = option,
        TypeResolvers = new IMsgPackTypeResolver[] { new XmlRootAttributeTypeResolver() }
      };

      byte[] buffer = MsgPackSerializer.Serialize(new PetBox() { Thing = new XmlCat() { Name = "Mia" } }, settings);
      PetBox ret = MsgPackSerializer.Deserialize<PetBox>(buffer, settings);

      Assert.AreEqual("Mia", ((XmlCat)ret.Thing).Name);
    }

    [TestMethod]
    public void FullNameOfTypeOutsideCoreLibrary()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName };
      byte[] buffer = MsgPackSerializer.Serialize(new WithSorted() { Sorted = new SortedDictionary<string, long>() { { "a", 1 } } }, settings);

      WithSorted ret = MsgPackSerializer.Deserialize<WithSorted>(buffer, settings);
      Assert.IsInstanceOfType<SortedDictionary<string, long>>(ret.Sorted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CollectionsWithoutGenericArguments(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };

      byte[] buffer = MsgPackSerializer.Serialize(new WithArrayList() { Items = new ArrayList() { 1, "a" } }, settings);
      CollectionAssert.AreEqual(new object[] { (byte)1, "a" }, MsgPackSerializer.Deserialize<WithArrayList>(buffer, settings).Items);

      buffer = MsgPackSerializer.Serialize(new WithIntList() { Items = new IntList() { 1, 300 } }, settings);
      CollectionAssert.AreEqual(new[] { 1, 300 }, MsgPackSerializer.Deserialize<WithIntList>(buffer, settings).Items);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HiddenPropertyUsesTheMostDerivedOne(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      byte[] buffer = MsgPackSerializer.Serialize(new HidesX() { X = "a" }, settings);
      Assert.AreEqual("a", MsgPackSerializer.Deserialize<HidesX>(buffer, settings).X);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IndexersAreIgnored(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      byte[] buffer = MsgPackSerializer.Serialize(new WithIndexer() { Name = "a" }, settings);
      Assert.AreEqual("a", MsgPackSerializer.Deserialize<WithIndexer>(buffer, settings).Name);
    }

    [TestMethod]
    public void MapKeysOfDifferentNumericTypesMatch()
    {
      Dictionary<object, object> dict = new Dictionary<object, object>(new MapConversionEqualityComparer())
      {
        { (sbyte)-1, "minus one" },
        { (byte)200, "two hundred" },
        { 1.5d, "one and a half" }
      };

      Assert.AreEqual("minus one", dict[-1]);
      Assert.AreEqual("minus one", dict[-1L]);
      Assert.AreEqual("two hundred", dict[200]);
      Assert.AreEqual("two hundred", dict[(ulong)200]);
      Assert.AreEqual("one and a half", dict[1.5f]);
    }

    private class NumberedPets : IMsgPackTypeResolver
    {
      public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackSettings settings)
      {
        if (type == typeof(Dog))
          return 1;
        if (type == typeof(Cat))
          return 2;
        return null;
      }

      public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
      {
        if (typeId is null || typeId is string)
          return null;

        switch (Convert.ToInt32(typeId))
        {
          case 1: return typeof(Dog);
          case 2: return typeof(Cat);
        }
        return null;
      }
    }
  }
}
