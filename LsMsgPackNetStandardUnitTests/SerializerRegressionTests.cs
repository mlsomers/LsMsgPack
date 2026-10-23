using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;
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
  public abstract class SerializerRegressionTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

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

      byte[] buffer = Serializer.Serialize(org, settings);
      Cat ret = (Cat)Serializer.Deserialize(typeof(Cat), buffer, settings);
      Assert.AreEqual("Mia", ret.Name);

      MemoryStream ms = new MemoryStream();
      Serializer.Serialize(org, ms, settings);
      ms.Position = 0;
      ret = (Cat)Serializer.Deserialize(typeof(Cat), ms, settings);
      Assert.AreEqual("Mia", ret.Name);
    }

    [TestMethod]
    public void DeserializeByTypeWithDefaultSettings()
    {
      byte[] buffer = Serializer.Serialize(new Cat() { Name = "Mia" });
      Assert.AreEqual("Mia", ((Cat)Serializer.Deserialize(typeof(Cat), buffer)).Name);
    }

    [TestMethod]
    public void NullSettingsUseDefaults()
    {
      byte[] buffer = Serializer.Serialize(new Cat() { Name = "Mia" }, (MsgPackSettings)null);
      Assert.AreEqual("Mia", Serializer.Deserialize<Cat>(buffer, (MsgPackSettings)null).Name);

      MemoryStream ms = new MemoryStream();
      Serializer.Serialize(new Cat() { Name = "Mia" }, ms, (MsgPackSettings)null);
      CollectionAssert.AreEqual(buffer, ms.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NullIsSerializedTheSameToBytesAndStream(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      MemoryStream ms = new MemoryStream();
      Serializer.Serialize<Cat>(null, ms, settings);

      CollectionAssert.AreEqual(Serializer.Serialize<Cat>(null, settings), ms.ToArray());
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

      byte[] buffer = Serializer.Serialize(new Cat() { Name = "Mia" }, settings);
      Serializer.Deserialize<Cat>(buffer, settings);

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
        byte[] buffer = Serializer.Serialize(org, settings);
        HierarchyContainer ret = Serializer.Deserialize<HierarchyContainer>(buffer, settings);

        Assert.AreEqual($"Cat {i}", ret.ExplicitlyCat.Name);
        Assert.AreEqual($"Dog {i}", ((Dog)ret.PetBaseClass).Name);
      });
    }

    [TestMethod]
    public void SchemaPrimitiveRoots()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = true };
      Assert.AreEqual(70000, Serializer.Deserialize<int>(Serializer.Serialize(70000, settings), settings));
      Assert.AreEqual("x", Serializer.Deserialize<string>(Serializer.Serialize("x", settings), settings));
    }

    [TestMethod]
    public void ReadingDataWithoutSchemaUsingSchemaSettingsThrows()
    {
      MsgPackSettings withoutSchema = new MsgPackSettings() { UseInexedSchema = false };
      MsgPackSettings withSchema = new MsgPackSettings() { UseInexedSchema = true };

      byte[] array = Serializer.Serialize(new[] { 1, 2 }, withoutSchema); // { "@": [1, 2] }
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<int[]>(array, withSchema));

      byte[] number = Serializer.Serialize(5, withoutSchema); // Used to silently return default
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<int>(number, withSchema));
    }

    /// <summary>
    /// The schema's type id's used to be resolved by custom resolvers first, which could return a completely different type for the same number.
    /// </summary>
    [TestMethod]
    public void SchemaTypeIdsDoNotCollideWithCustomResolverIds()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = true, TypeResolvers = new IMsgPackTypeResolver[] { new NumberedPets() } };

      byte[] buffer = Serializer.Serialize(new PetBox() { Pet = new Cat() { Name = "Mia" }, Thing = new Other() { X = "x" } }, settings);
      PetBox ret = Serializer.Deserialize<PetBox>(buffer, settings);

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

      byte[] buffer = Serializer.Serialize(new PetBox() { Thing = new XmlCat() { Name = "Mia" } }, settings);
      PetBox ret = Serializer.Deserialize<PetBox>(buffer, settings);

      Assert.AreEqual("Mia", ((XmlCat)ret.Thing).Name);
    }

    [TestMethod]
    public void FullNameOfTypeOutsideCoreLibrary()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false, AddTypeIdOptions = AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName };
      byte[] buffer = Serializer.Serialize(new WithSorted() { Sorted = new SortedDictionary<string, long>() { { "a", 1 } } }, settings);

      WithSorted ret = Serializer.Deserialize<WithSorted>(buffer, settings);
      Assert.IsInstanceOfType<SortedDictionary<string, long>>(ret.Sorted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CollectionsWithoutGenericArguments(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };

      byte[] buffer = Serializer.Serialize(new WithArrayList() { Items = new ArrayList() { 1, "a" } }, settings);
      CollectionAssert.AreEqual(new object[] { (byte)1, "a" }, Serializer.Deserialize<WithArrayList>(buffer, settings).Items);

      buffer = Serializer.Serialize(new WithIntList() { Items = new IntList() { 1, 300 } }, settings);
      CollectionAssert.AreEqual(new[] { 1, 300 }, Serializer.Deserialize<WithIntList>(buffer, settings).Items);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HiddenPropertyUsesTheMostDerivedOne(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      byte[] buffer = Serializer.Serialize(new HidesX() { X = "a" }, settings);
      Assert.AreEqual("a", Serializer.Deserialize<HidesX>(buffer, settings).X);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IndexersAreIgnored(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      byte[] buffer = Serializer.Serialize(new WithIndexer() { Name = "a" }, settings);
      Assert.AreEqual("a", Serializer.Deserialize<WithIndexer>(buffer, settings).Name);
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

    [TestMethod]
    public void MapKeysThatCannotBeDecimalsDoNotThrow()
    {
      MapConversionEqualityComparer comparer = new MapConversionEqualityComparer();

      Assert.IsFalse(comparer.Equals(double.NaN, 1));
      Assert.IsFalse(comparer.Equals(1, float.NaN));
      Assert.IsFalse(comparer.Equals(double.PositiveInfinity, 1L));
      Assert.IsFalse(comparer.Equals(1e30, 1.5d));
      Assert.IsTrue(comparer.Equals(double.NaN, double.NaN));
    }

    public class AllZero
    {
      public string Name { get; set; } = "x";
      public int I { get; set; }
      public long L { get; set; }
      public float F { get; set; }
      public double D { get; set; }
      public byte B { get; set; }
      public sbyte SB { get; set; }
      public short S { get; set; }
      public ushort US { get; set; }
      public uint UI { get; set; }
      public ulong UL { get; set; }
      public decimal M { get; set; }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FilterDefaultValuesOmitsZerosOfAllNumericTypes(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings()
      {
        UseInexedSchema = useSchema,
        ObjectLayout = ObjectLayout.Map, // omitted values are only visible in a map (an array has nil in their place)
        DynamicFilters = new IMsgPackPropertyIncludeDynamically[] { new FilterDefaultValues() }
      };

      byte[] withoutSchema = Serializer.Serialize(new AllZero(), new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, DynamicFilters = settings.DynamicFilters });
      Assert.AreEqual(1, ((MpMap)MsgPackItem.Unpack(withoutSchema)).Count, "Only Name should be serialized");

      AllZero org = new AllZero() { L = 1, F = 2, D = 3, B = 4, SB = -5, S = 6, US = 7, UI = 8, UL = 9, M = 10 };
      AllZero ret = Serializer.Deserialize<AllZero>(Serializer.Serialize(org, settings), settings);
      Assert.AreEqual(1L, ret.L);
      Assert.AreEqual(2f, ret.F);
      Assert.AreEqual(3d, ret.D);
      Assert.AreEqual((byte)4, ret.B);
      Assert.AreEqual((sbyte)-5, ret.SB);
      Assert.AreEqual((short)6, ret.S);
      Assert.AreEqual((ushort)7, ret.US);
      Assert.AreEqual(8u, ret.UI);
      Assert.AreEqual(9ul, ret.UL);
      Assert.AreEqual(10m, ret.M);
    }

    /// <summary>
    /// Returns at most one byte per read, like a network stream that has not received all data yet.
    /// </summary>
    private class TrickleStream : MemoryStream
    {
      public TrickleStream(byte[] buffer) : base(buffer) { }

      public override int Read(byte[] buffer, int offset, int count)
      {
        return base.Read(buffer, offset, Math.Min(count, 1));
      }
    }

    public class Assorted
    {
      public string Name { get; set; }
      public short S { get; set; }
      public int I { get; set; }
      public long L { get; set; }
      public float F { get; set; }
      public double D { get; set; }
      public byte[] Bin { get; set; }
      public DateTime When { get; set; }
      public List<int> Numbers { get; set; }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReadsFromStreamsThatReturnPartialData(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema };
      Assorted org = new Assorted()
      {
        Name = new string('n', 300), // str16
        S = -1000,
        I = 100000,
        L = long.MaxValue,
        F = 1.5f,
        D = 2.25,
        Bin = new byte[] { 1, 2, 3 },
        When = new DateTime(2021, 1, 1, 0, 0, 0, 500, DateTimeKind.Utc),
        Numbers = new List<int>(new int[20]) { 1 } // array16
      };

      byte[] buffer = Serializer.Serialize(org, settings);
      Assorted ret = Serializer.Deserialize<Assorted>(new TrickleStream(buffer), settings);

      Assert.AreEqual(org.Name, ret.Name);
      Assert.AreEqual(org.S, ret.S);
      Assert.AreEqual(org.I, ret.I);
      Assert.AreEqual(org.L, ret.L);
      Assert.AreEqual(org.F, ret.F);
      Assert.AreEqual(org.D, ret.D);
      CollectionAssert.AreEqual(org.Bin, ret.Bin);
      Assert.AreEqual(org.When, ret.When.ToUniversalTime());
      CollectionAssert.AreEqual(org.Numbers, ret.Numbers);
    }

    public class TwoNames
    {
      public string First { get; set; }
      public string Second { get; set; }
    }

    /// <summary>
    /// Returns the same id for every property of <see cref="TwoNames"/>
    /// </summary>
    private class FixedPropertyId : IMsgPackPropertyIdResolver
    {
      private readonly object _id;
      public FixedPropertyId(object id) { _id = id; }

      public object GetId(FullPropertyInfo assignedTo, MsgPackOptions settings)
      {
        return assignedTo.PropertyInfo.DeclaringType == typeof(TwoNames) ? _id : null;
      }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DuplicatePropertyIdsThrow(bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new FixedPropertyId("same") } };

      // Even when one of them would be skipped (default value), the ids are checked once per type
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Serialize(new TwoNames() { First = "a" }, settings));
      StringAssert.Contains(ex.Message, "same id");
    }

    [TestMethod]
    [DataRow("", false)]
    [DataRow("@", false)]
    [DataRow("", true)]
    [DataRow("@", true)]
    public void ReservedPropertyIdsThrow(string id, bool useSchema)
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = useSchema, PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new FixedPropertyId(id) } };

      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Serialize(new TwoNames() { First = "a", Second = "b" }, settings));
      StringAssert.Contains(ex.Message, "reserved");
    }

    public class JsonNamed
    {
      [System.Text.Json.Serialization.JsonPropertyName("renamed")] public string A { get; set; }
      [Newtonsoft.Json.JsonProperty("newton")] public int B { get; set; }
      [Newtonsoft.Json.JsonProperty(Order = 1)] public string Unnamed { get; set; } // no name: the .NET name
      public string Plain { get; set; }
      [System.Text.Json.Serialization.JsonPropertyName("json")][XmlElement("xml")] public string Both { get; set; } // the released versions wrote the XML name
      [XmlArray("items")] public List<int> Items { get; set; }
      public ContractNamed Contract { get; set; }
      public NotAContract Loose { get; set; }
      public JsonNamed Child { get; set; }
    }

    [System.Runtime.Serialization.DataContract]
    public class ContractNamed
    {
      [System.Runtime.Serialization.DataMember(Name = "dm")] public string D { get; set; }
      [System.Runtime.Serialization.DataMember] public string E { get; set; } // no name: the .NET name
      public string F { get; set; }
    }

    public class NotAContract
    {
      [System.Runtime.Serialization.DataMember(Name = "ignored")] public string P { get; set; } // DataContractSerializer reads [DataMember] only in a [DataContract]
    }

    public class JsonNamedAsWritten
    {
      public string renamed { get; set; }
      public int newton { get; set; }
      public string Unnamed { get; set; }
      public string Plain { get; set; }
      public string json { get; set; }
      public List<int> items { get; set; }
      public ContractNamedAsWritten Contract { get; set; }
      public NotAContractAsWritten Loose { get; set; }
      public JsonNamedAsWritten Child { get; set; }
    }

    public class ContractNamedAsWritten
    {
      public string dm { get; set; }
      public string E { get; set; }
      public string F { get; set; }
    }

    public class NotAContractAsWritten
    {
      public string P { get; set; }
    }

    /// <summary>
    /// What AttributePropertyNameResolver did until October 2026: it did not find the attributes of the JSON serializers.
    /// </summary>
    private class ReleasedAttributeNames : IMsgPackPropertyIdResolver
    {
      public object GetId(FullPropertyInfo assignedTo, MsgPackOptions settings)
      {
        if (assignedTo.CustomAttributes.TryGetValue(nameof(XmlElementAttribute), out object element))
          return ((XmlElementAttribute)element).ElementName;
        return null;
      }
    }

    private static JsonNamed NewJsonNamed()
    {
      return new JsonNamed
      {
        A = "a", B = 7, Unnamed = "u", Plain = "p", Both = "b", Items = new List<int> { 1, 2 },
        Contract = new ContractNamed { D = "d", E = "e", F = "f" }, Loose = new NotAContract { P = "l" }, Child = new JsonNamed { A = "ca", B = 8 }
      };
    }

    private static void AssertJsonNamed(JsonNamed read)
    {
      Assert.AreEqual("a", read.A);
      Assert.AreEqual(7, read.B);
      Assert.AreEqual("u", read.Unnamed);
      Assert.AreEqual("p", read.Plain);
      Assert.AreEqual("b", read.Both);
      CollectionAssert.AreEqual(new[] { 1, 2 }, read.Items);
      Assert.AreEqual("d", read.Contract.D);
      Assert.AreEqual("e", read.Contract.E);
      Assert.AreEqual("f", read.Contract.F);
      Assert.AreEqual("l", read.Loose.P);
      Assert.AreEqual("ca", read.Child.A);
      Assert.AreEqual(8, read.Child.B);
    }

    private static MsgPackSettings JsonNamedSettings(bool schema, ObjectLayout layout, bool resolver, bool released = false)
    {
      MsgPackSettings settings = new MsgPackSettings { UseInexedSchema = schema, ObjectLayout = layout };
      if (resolver)
        settings.PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { released ? (IMsgPackPropertyIdResolver)new ReleasedAttributeNames() : new AttributePropertyNameResolver() };
      return settings;
    }

    /// <summary>
    /// AttributePropertyNameResolver writes the names of [JsonPropertyName] (System.Text.Json) and [JsonProperty] (Json.NET), it used to look them up by the wrong key, and of [XmlArray] and [DataMember] (in a [DataContract]), which it did not read.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AttributeNamesOfJsonSerializers(bool schema)
    {
      byte[] bytes = Serializer.Serialize(NewJsonNamed(), JsonNamedSettings(schema, ObjectLayout.Map, true));

      AssertJsonNamed(Serializer.Deserialize<JsonNamed>(bytes, JsonNamedSettings(schema, ObjectLayout.Map, true)));

      if (schema)
        return; // with the schema the renamed keys are written as names, the others as indexes: only readers with the same resolver understand them
      JsonNamedAsWritten written = Serializer.Deserialize<JsonNamedAsWritten>(bytes, JsonNamedSettings(false, ObjectLayout.Map, false));
      Assert.AreEqual("a", written.renamed);
      Assert.AreEqual(7, written.newton);
      Assert.AreEqual("u", written.Unnamed);
      Assert.AreEqual("p", written.Plain);
      Assert.AreEqual("b", written.json);
      CollectionAssert.AreEqual(new[] { 1, 2 }, written.items);
      Assert.AreEqual("d", written.Contract.dm);
      Assert.AreEqual("e", written.Contract.E);
      Assert.AreEqual("f", written.Contract.F);
      Assert.AreEqual("l", written.Loose.P);
      Assert.AreEqual("ca", written.Child.renamed);
    }

    /// <summary>
    /// Data written by the versions that ignored the JSON attributes, [XmlArray] and [DataMember] (the .NET names, or their schema indexes) is still read into the renamed properties.
    /// </summary>
    [TestMethod]
    [DataRow(false, ObjectLayout.Map)]
    [DataRow(true, ObjectLayout.Map)]
    [DataRow(false, ObjectLayout.Array)]
    [DataRow(true, ObjectLayout.Array)]
    public void AttributeNamesReadDataWithDotNetNames(bool schema, ObjectLayout layout)
    {
      byte[] old = Serializer.Serialize(NewJsonNamed(), JsonNamedSettings(schema, layout, true, true)); // as the resolver wrote them before

      AssertJsonNamed(Serializer.Deserialize<JsonNamed>(old, JsonNamedSettings(schema, layout, true)));

      Serializer.Deserialize<JsonNamed>(old, JsonNamedSettings(schema, layout, true), out ReadDifferences differences);
      Assert.IsNull(differences, differences?.ToString());
    }

    public class XmlUnnamed
    {
      [XmlElement] public string Element { get; set; }
      [XmlElement(Order = 1)] public string Ordered { get; set; }
      [XmlAttribute] public string Attribute { get; set; }
      [XmlAttribute(DataType = "string")] public string Typed { get; set; }
      [XmlElement("named")] public string Named { get; set; }
    }

    public class XmlUnnamedAsWritten
    {
      public string Element { get; set; }
      public string Ordered { get; set; }
      public string Attribute { get; set; }
      public string Typed { get; set; }
      public string named { get; set; }
    }

    /// <summary>
    /// [XmlElement] and [XmlAttribute] without a name keep the .NET name, as XmlSerializer does (the empty name was written as the reserved id "", which threw).
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AttributeNamesXmlWithoutName(bool schema)
    {
      XmlUnnamed value = new XmlUnnamed { Element = "e", Ordered = "o", Attribute = "a", Typed = "t", Named = "n" };
      byte[] bytes = Serializer.Serialize(value, JsonNamedSettings(schema, ObjectLayout.Map, true));

      XmlUnnamed read = Serializer.Deserialize<XmlUnnamed>(bytes, JsonNamedSettings(schema, ObjectLayout.Map, true));
      Assert.AreEqual("e", read.Element);
      Assert.AreEqual("o", read.Ordered);
      Assert.AreEqual("a", read.Attribute);
      Assert.AreEqual("t", read.Typed);
      Assert.AreEqual("n", read.Named);

      if (schema)
        return;
      XmlUnnamedAsWritten written = Serializer.Deserialize<XmlUnnamedAsWritten>(bytes, JsonNamedSettings(false, ObjectLayout.Map, false));
      Assert.AreEqual("e", written.Element);
      Assert.AreEqual("o", written.Ordered);
      Assert.AreEqual("a", written.Attribute);
      Assert.AreEqual("t", written.Typed);
      Assert.AreEqual("n", written.named);
    }

    public class JsonNamedOldAndNew
    {
      public string A { get; set; }
      public string renamed { get; set; }
    }

    /// <summary>
    /// The former id (the .NET name) only counts when the map does not have the property's id.
    /// </summary>
    [TestMethod]
    public void AttributeNamesPreferTheNewName()
    {
      MsgPackSettings settings = JsonNamedSettings(false, ObjectLayout.Map, false);
      byte[] bytes = Serializer.Serialize(new JsonNamedOldAndNew { A = "old", renamed = "new" }, settings);

      Assert.AreEqual("new", Serializer.Deserialize<JsonNamed>(bytes, JsonNamedSettings(false, ObjectLayout.Map, true)).A);
    }

    private class NumberedPets : IMsgPackTypeResolver
    {
      public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackOptions settings)
      {
        if (type == typeof(Dog))
          return 1;
        if (type == typeof(Cat))
          return 2;
        return null;
      }

      public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackOptions settings)
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

  [TestClass]
  public class LsSerializerRegressionTests : SerializerRegressionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializerRegressionTests : SerializerRegressionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenSerializerRegressionTests : SerializerRegressionTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
