using LsMsgPack;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Names;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LsMsgPackUnitTests
{
  namespace DifferencesWriter
  {
    public class DiffOrderDto
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      public decimal Discount { get; set; }
      public DiffAddressDto Address { get; set; }
      public List<DiffLineDto> Lines { get; set; }
    }

    public class DiffAddressDto
    {
      public string Street { get; set; }
      public string Zip { get; set; }
    }

    public class DiffLineDto
    {
      public string Product { get; set; }
      public int Quantity { get; set; }
      public string Colour { get; set; }
    }

    public class DiffPointDto
    {
      public int X { get; set; }
      public int Y { get; set; }
      public int Z { get; set; }
    }

    public class DiffThrowDto
    {
      public string Extra { get; set; }
      public string Boom { get; set; }
    }
  }

  namespace DifferencesReader
  {
    /// <summary>
    /// The writer's order without Discount, and Address misspelled.
    /// </summary>
    public class DiffOrderEntity
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      public DiffAddressEntity Adress { get; set; }
      public List<DiffLineEntity> Lines { get; set; }
    }

    public class DiffAddressEntity
    {
      public string Street { get; set; }
      public string Zip { get; set; }
    }

    public class DiffLineEntity
    {
      public string Product { get; set; }
      public int Quantity { get; set; }
    }

    public class DiffPointEntity
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public class DiffThrowEntity
    {
      public string Boom
      {
        get { return null; }
        set { throw new InvalidOperationException("Boom"); }
      }
    }
  }

  /// <summary>
  /// The Deserialize overloads with an out ReadDifferences: what did not match between the data and the classes, counted per class and name, with the paths of the objects.
  /// </summary>
  public abstract class ReportingDifferencesTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    internal static List<DifferencesWriter.DiffOrderDto> Orders(int count)
    {
      List<DifferencesWriter.DiffOrderDto> orders = new List<DifferencesWriter.DiffOrderDto>();
      for (int i = 0; i < count; i++)
      {
        DifferencesWriter.DiffOrderDto order = new DifferencesWriter.DiffOrderDto()
        {
          Id = i + 1,
          Customer = "Ann",
          Discount = 5m,
          Address = new DifferencesWriter.DiffAddressDto() { Street = "Main street", Zip = "1234 AB" },
          Lines = new List<DifferencesWriter.DiffLineDto>()
        };
        for (int l = 0; l <= i; l++) // the first line of each order has no colour: i lines with a colour
          order.Lines.Add(new DifferencesWriter.DiffLineDto() { Product = "Apples", Quantity = l + 1, Colour = l == 0 ? null : "red" });
        orders.Add(order);
      }
      return orders;
    }

    /// <param name="schema">names (no schema), inline, store (inline, sessions shared by the calls) or reference</param>
    internal static MsgPackSettings Settings(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = new MsgPackSettings() { ObjectLayout = layout };
      if (schema == "names")
        settings.UseInexedSchema = false;
      else if (schema != "inline")
        settings.SchemaStore = new SchemaStore();
      if (schema == "reference")
        settings.WriteSchemaReference = true;
      return settings;
    }

    private static Difference Find(ReadDifferences differences, Type type, DifferenceKind kind, string name)
    {
      Difference found = differences.Differences.SingleOrDefault(d => d.Class == type && d.Kind == kind && d.Name == name);
      Assert.IsNotNull(found, $"{type.Name}.{name} ({kind}) not found in:\r\n{differences}");
      return found;
    }

    private static string[] Paths(ReadDifferences differences, Difference difference)
    {
      return difference.Samples.Select(differences.PathOf).ToArray();
    }

    [TestMethod]
    [DataRow(ObjectLayout.Map, "names")]
    [DataRow(ObjectLayout.Map, "inline")]
    [DataRow(ObjectLayout.Array, "inline")]
    [DataRow(ObjectLayout.Map, "store")]
    [DataRow(ObjectLayout.Array, "store")]
    [DataRow(ObjectLayout.Map, "reference")]
    [DataRow(ObjectLayout.Array, "reference")]
    public void UnknownPropertiesAreCounted(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = Settings(layout, schema);
      for (int call = 0; call < 3; call++) // later calls use the shared session of the store
      {
        byte[] bytes = Serializer.Serialize(Orders(3), settings);
        List<DifferencesReader.DiffOrderEntity> read = Serializer.Deserialize<List<DifferencesReader.DiffOrderEntity>>(bytes, settings, out ReadDifferences differences);

        Assert.AreEqual(3, read.Count);
        Assert.AreEqual(3, read[2].Id);
        Assert.AreEqual(3, read[2].Lines.Count);
        Assert.IsNull(read[0].Adress);

        Assert.IsNotNull(differences);
        Assert.AreSame(read, differences.Root);
        Assert.AreEqual(3, differences.Differences.Count, differences.ToString());
        Assert.AreEqual(0, differences.Omitted);

        Difference discount = Find(differences, typeof(DifferencesReader.DiffOrderEntity), DifferenceKind.UnknownProperty, "Discount");
        Assert.AreEqual(3, discount.Count);
        Difference address = Find(differences, typeof(DifferencesReader.DiffOrderEntity), DifferenceKind.UnknownProperty, "Address");
        Assert.AreEqual(3, address.Count);
        CollectionAssert.AreEquivalent(new[] { "$[0]", "$[1]", "$[2]" }, Paths(differences, address));
        Difference colour = Find(differences, typeof(DifferencesReader.DiffLineEntity), DifferenceKind.UnknownProperty, "Colour"); // the lines without a colour do not count (left out, or nil in an array)
        Assert.AreEqual(3, colour.Count);
        CollectionAssert.AreEquivalent(new[] { "$[1].Lines[1]", "$[2].Lines[1]", "$[2].Lines[2]" }, Paths(differences, colour));
      }
    }

    /// <summary>
    /// Custom property id resolvers: LtMsgPack reads the objects as LsMsgPack does (LsMsgPack.Core), which collects the differences too.
    /// </summary>
    [TestMethod]
    [DataRow("names")]
    [DataRow("inline")]
    public void CustomPropertyIds(string schema)
    {
      MsgPackSettings settings = Settings(ObjectLayout.Map, schema);
      settings.PropertyNameResolvers = new IMsgPackPropertyIdResolver[] { new AttributePropertyNameResolver() };
      Serializer.Deserialize<List<DifferencesReader.DiffOrderEntity>>(Serializer.Serialize(Orders(3), settings), settings, out ReadDifferences differences);
      Assert.AreEqual(3, differences.Differences.Count, differences?.ToString());
      Assert.AreEqual(3, Find(differences, typeof(DifferencesReader.DiffOrderEntity), DifferenceKind.UnknownProperty, "Address").Count);
      Assert.AreEqual(3, Find(differences, typeof(DifferencesReader.DiffLineEntity), DifferenceKind.UnknownProperty, "Colour").Count);
    }

    [TestMethod]
    [DataRow(ObjectLayout.Map, "names")]
    [DataRow(ObjectLayout.Array, "names")]
    [DataRow(ObjectLayout.Map, "inline")]
    [DataRow(ObjectLayout.Array, "store")]
    public void NoDifferencesGivesNull(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = Settings(layout, schema);
      List<DifferencesWriter.DiffOrderDto> read = Serializer.Deserialize<List<DifferencesWriter.DiffOrderDto>>(Serializer.Serialize(Orders(2), settings), settings, out ReadDifferences differences);
      Assert.AreEqual(2, read.Count);
      Assert.IsNull(differences, differences?.ToString());
    }

    [TestMethod]
    public void ReportNamesTheProperty()
    {
      MsgPackSettings settings = Settings(ObjectLayout.Map, "inline");
      Serializer.Deserialize<List<DifferencesReader.DiffOrderEntity>>(Serializer.Serialize(Orders(2), settings), settings, out ReadDifferences differences);
      string report = differences.GenerateReport();
      StringAssert.Contains(report, "DiffOrderEntity.Address: not a property of the class, skipped, 2 times");
      StringAssert.Contains(report, "did you mean Adress? (left at its default)");
      StringAssert.Contains(report, "left at their default (first object): Adress");
      StringAssert.Contains(report, "DiffLineEntity.Colour");
      StringAssert.Contains(report, "$[1].Lines[1]");
      Assert.AreEqual(report, differences.ToString());
    }

    /// <summary>
    /// Without the schema, an object written as an array is read by position: values after the last property are extra.
    /// </summary>
    [TestMethod]
    public void ExtraValuesWithoutSchema()
    {
      MsgPackSettings settings = Settings(ObjectLayout.Array, "names");
      DifferencesWriter.DiffPointDto[] points = { new DifferencesWriter.DiffPointDto() { X = 1, Y = 2, Z = 3 }, new DifferencesWriter.DiffPointDto() { X = 4, Y = 5, Z = 6 } };
      DifferencesReader.DiffPointEntity[] read = Serializer.Deserialize<DifferencesReader.DiffPointEntity[]>(Serializer.Serialize(points, settings), settings, out ReadDifferences differences);
      Assert.AreEqual(5, read[1].Y);
      Assert.AreEqual(1, differences.Differences.Count, differences.ToString());
      Difference extra = differences.Differences[0];
      Assert.AreEqual(DifferenceKind.ExtraValue, extra.Kind);
      Assert.AreEqual(typeof(DifferencesReader.DiffPointEntity), extra.Class);
      Assert.AreEqual(2, extra.Position);
      Assert.IsNull(extra.Name);
      Assert.AreEqual(2, extra.Count);
      StringAssert.Contains(differences.GenerateReport(), "DiffPointEntity[2]: value after the last property");
    }

    /// <summary>
    /// A class that could not be paired with one of the writer's classes (one reader class for two writer classes) throws without the differences, and is skipped and reported with them.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map, "inline")]
    [DataRow(ObjectLayout.Array, "inline")]
    [DataRow(ObjectLayout.Map, "store")]
    [DataRow(ObjectLayout.Array, "reference")]
    public void UnmatchedClassIsSkipped(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = Settings(layout, schema);
      OtherClassesWriter.TwoKindsDto written = new OtherClassesWriter.TwoKindsDto()
      {
        Home = new OtherClassesWriter.AddressDto() { Street = "Main street", Zip = "1234 AB" },
        Work = new OtherClassesWriter.LineDto() { Product = "Apples", Quantity = 3 }
      };
      byte[] bytes = Serializer.Serialize(written, settings);
      Assert.Throws<MsgPackException>(() => Serializer.Deserialize<OtherClassesReader.TwoKindsEntity>(bytes, settings));

      for (int call = 0; call < 2; call++)
      {
        OtherClassesReader.TwoKindsEntity read = Serializer.Deserialize<OtherClassesReader.TwoKindsEntity>(bytes, settings, out ReadDifferences differences);
        Assert.IsNotNull(read);
        Assert.IsNull(read.Home);
        Assert.IsNull(read.Work);
        Assert.AreEqual(2, differences.Differences.Count, differences.ToString());
        foreach (string property in new[] { "Home", "Work" })
        {
          Difference unmatched = Find(differences, typeof(OtherClassesReader.AddressEntity), DifferenceKind.UnmatchedClass, property);
          Assert.AreEqual(1, unmatched.Count);
          CollectionAssert.AreEqual(new[] { "$" }, Paths(differences, unmatched));
        }
        StringAssert.Contains(differences.GenerateReport(), "AddressEntity (Home): no schema entry, the object was skipped, 1 time\r\n    in $");
      }
    }

    /// <summary>
    /// The sessions of a store are shared by the calls, the differences belong to each call.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void ConcurrentCallsCountTheirOwn(ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(layout, "store");
      byte[][] payloads = Enumerable.Range(1, 4).Select(n => Serializer.Serialize(Orders(n), settings)).ToArray();
      Parallel.For(0, 200, i =>
      {
        int n = i % 4 + 1;
        Serializer.Deserialize<List<DifferencesReader.DiffOrderEntity>>(payloads[n - 1], settings, out ReadDifferences differences);
        Assert.AreEqual(n, Find(differences, typeof(DifferencesReader.DiffOrderEntity), DifferenceKind.UnknownProperty, "Discount").Count);
        Assert.AreEqual(n * (n - 1) / 2, n == 1 ? 0 : Find(differences, typeof(DifferencesReader.DiffLineEntity), DifferenceKind.UnknownProperty, "Colour").Count);
      });
    }

    /// <summary>
    /// The names come from the data: their number and length are bounded.
    /// </summary>
    [TestMethod]
    public void ManyAndLongNamesAreBounded()
    {
      MsgPackSettings settings = Settings(ObjectLayout.Map, "names");
      Dictionary<string, int> map = new Dictionary<string, int>() { { "Street", 1 } };
      for (int t = 0; t < 1000; t++)
        map.Add("name" + t, t);
      map.Add(new string('x', 5000), 1);

      DifferencesReader.DiffAddressEntity read = Serializer.Deserialize<DifferencesReader.DiffAddressEntity>(Serializer.Serialize(map, settings), settings, out ReadDifferences differences);
      Assert.IsNotNull(read);
      Assert.AreEqual(ReadDifferences.MaxDifferences, differences.Differences.Count);
      Assert.AreEqual(1001 - ReadDifferences.MaxDifferences, differences.Omitted); // the long name too
      StringAssert.Contains(differences.GenerateReport(), "more, not kept");

      map = new Dictionary<string, int>() { { new string('x', 5000), 1 } };
      Serializer.Deserialize<DifferencesReader.DiffAddressEntity>(Serializer.Serialize(map, settings), settings, out differences);
      Assert.AreEqual(ReadDifferences.MaxNameLength + 1, differences.Differences.Single().Name.Length);
    }

    /// <summary>
    /// When reading throws, the differences found until then are on the exception (e.g. the cause: a property that was not set because its name was misspelled).
    /// </summary>
    [TestMethod]
    public void ExceptionCarriesTheDifferences()
    {
      MsgPackSettings settings = Settings(ObjectLayout.Map, "names");
      byte[] bytes = Serializer.Serialize(new DifferencesWriter.DiffThrowDto() { Extra = "extra", Boom = "boom" }, settings);
      InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Serializer.Deserialize<DifferencesReader.DiffThrowEntity>(bytes, settings, out ReadDifferences unused));
      ReadDifferences differences = (ReadDifferences)ex.Data[ReadDifferences.ExceptionDataKey];
      Assert.IsNotNull(differences);
      Assert.IsNull(differences.Root);
      Assert.AreEqual("Extra", differences.Differences.Single().Name);
      StringAssert.Contains(differences.GenerateReport(), "did not finish");
    }
  }

  [TestClass]
  public class LsReportingDifferencesTests : ReportingDifferencesTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtReportingDifferencesTests : ReportingDifferencesTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  /// <summary>
  /// Both serializers report the same differences for the same data (the order of the differences and of their samples may differ: LsMsgPack converts from the last value to the first).
  /// </summary>
  [TestClass]
  public class ReportingDifferencesCrossLibraryTests
  {
    private static IEnumerable<object[]> Modes()
    {
      foreach (string schema in new[] { "names", "inline", "store", "reference" })
        foreach (ObjectLayout layout in Enum.GetValues(typeof(ObjectLayout)))
          foreach (bool compact in new[] { true, false })
            yield return new object[] { schema, layout, compact };
    }

    private static string[] Normalized(ReadDifferences differences)
    {
      if (differences is null)
        return new string[0];
      return differences.Differences
        .Select(d => $"{d.Class?.Name} {d.Kind} {d.Name} {d.Position} {d.Count} [{string.Join(", ", d.Samples.Select(differences.PathOf).OrderBy(p => p, StringComparer.Ordinal))}]")
        .OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }

    private static void Compare<T>(byte[] bytes, MsgPackSettings settings)
    {
      Serializers.Ls.Deserialize<T>(bytes, settings, out ReadDifferences ls);
      Serializers.Lt.Deserialize<T>(bytes, settings, out ReadDifferences lt);
      CollectionAssert.AreEqual(Normalized(ls), Normalized(lt), $"LsMsgPack:\r\n{ls}\r\nLtMsgPack:\r\n{lt}");
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void SameDifferences(string schema, ObjectLayout layout, bool compact)
    {
      MsgPackSettings settings = ReportingDifferencesTests.Settings(layout, schema);
      settings.DynamicallyCompact = compact;

      if (schema != "names" || layout == ObjectLayout.Map) // without the schema an array is read by position, other classes do not fit
        Compare<List<DifferencesReader.DiffOrderEntity>>(MsgPackSerializer.Serialize(ReportingDifferencesTests.Orders(4), settings), settings);

      DifferencesWriter.DiffPointDto[] points = { new DifferencesWriter.DiffPointDto() { X = 1, Y = 2, Z = 3 }, new DifferencesWriter.DiffPointDto() { X = 4, Y = 0, Z = 0 } };
      Compare<DifferencesReader.DiffPointEntity[]>(MsgPackSerializer.Serialize(points, settings), settings);

      if (schema != "names")
      {
        OtherClassesWriter.TwoKindsDto twoKinds = new OtherClassesWriter.TwoKindsDto()
        {
          Home = new OtherClassesWriter.AddressDto() { Street = "Main street" },
          Work = new OtherClassesWriter.LineDto() { Product = "Apples" }
        };
        Compare<OtherClassesReader.TwoKindsEntity>(MsgPackSerializer.Serialize(twoKinds, settings), settings);
      }
    }
  }
}
