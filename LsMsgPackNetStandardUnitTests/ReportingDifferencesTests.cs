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

    /// <summary>
    /// Polymorphic values, written with type ids (the tests rename the type names in the bytes so they are not found).
    /// </summary>
    public class DiffPenDto
    {
      public string Name { get; set; }
      public DiffAnimal Pet { get; set; }
      public DiffShape Shape { get; set; }
    }

    public class DiffAnimal
    {
      public string Name { get; set; }
    }

    public class DiffUnicorn : DiffAnimal
    {
      public int Horn { get; set; }
    }

    public abstract class DiffShape
    {
      public int Size { get; set; }
    }

    public class DiffSquare : DiffShape
    {
      public int Side { get; set; }
    }

    /// <summary>
    /// Values the reader's DiffErrorsEntity cannot convert (all but Name).
    /// </summary>
    public class DiffErrorsDto
    {
      public string Id { get; set; }
      public int Count { get; set; }
      public string Colour { get; set; }
      public string Name { get; set; }
      public List<string> Lines { get; set; }
      public DiffInnerDto Inner { get; set; }
    }

    public class DiffInnerDto
    {
      public string Amount { get; set; }
      public string Note { get; set; }
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

    public enum DiffColour { Red, Green }

    public class DiffErrorsEntity
    {
      public int Id { get; set; } = 7;
      public byte Count { get; set; }
      public DiffColour Colour { get; set; } = DiffColour.Green;
      public string Name { get; set; }
      public List<int> Lines { get; set; }
      public DiffInnerEntity Inner { get; set; }
    }

    public class DiffInnerEntity
    {
      public int Amount { get; set; } = 5;
      public string Note { get; set; }
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

    /// <summary>
    /// A pen with a DiffUnicorn and a DiffSquare whose type names are renamed in the bytes (the same length), as if the reader did not have those classes.
    /// </summary>
    internal static byte[] PenWithUnknownTypes(ISerializerUnderTest serializer, MsgPackSettings settings)
    {
      DifferencesWriter.DiffPenDto pen = new DifferencesWriter.DiffPenDto()
      {
        Name = "pen",
        Pet = new DifferencesWriter.DiffUnicorn() { Name = "Sparkle", Horn = 3 },
        Shape = new DifferencesWriter.DiffSquare() { Size = 2, Side = 4 }
      };
      byte[] bytes = serializer.Serialize(pen, settings);
      Rename(bytes, "DiffUnicorn", "DiffUnicorX");
      Rename(bytes, "DiffSquare", "DiffSquarX");
      return bytes;
    }

    private static void Rename(byte[] bytes, string name, string other)
    {
      byte[] find = System.Text.Encoding.UTF8.GetBytes(name);
      byte[] replace = System.Text.Encoding.UTF8.GetBytes(other);
      int found = 0;
      for (int t = 0; t + find.Length <= bytes.Length; t++)
      {
        if (bytes.Skip(t).Take(find.Length).SequenceEqual(find))
        {
          Array.Copy(replace, 0, bytes, t, replace.Length);
          found++;
        }
      }
      Assert.AreEqual(1, found, name);
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
    /// A class that could not be paired with one of the writer's classes (one reader class for two writer classes) is an error: it throws (FailFast, with the differences on the exception),
    /// or is skipped and reported with ReportAndContinue, naming the writer's classes.
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
      MsgPackException thrown = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<OtherClassesReader.TwoKindsEntity>(bytes, settings));
      StringAssert.Contains(thrown.Message, "where the writer had different classes (AddressDto, LineDto)");
      thrown = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<OtherClassesReader.TwoKindsEntity>(bytes, settings, out ReadDifferences unused));
      Assert.AreEqual(1, ((ReadDifferences)thrown.Data[ReadDifferences.ExceptionDataKey]).ErrorCount);

      settings.ReadErrors = ReadErrorHandling.ReportAndContinue;
      for (int call = 0; call < 2; call++)
      {
        OtherClassesReader.TwoKindsEntity read = Serializer.Deserialize<OtherClassesReader.TwoKindsEntity>(bytes, settings, out ReadDifferences differences);
        Assert.IsNotNull(read);
        Assert.AreEqual(2, differences.ErrorCount);
        Assert.IsNull(read.Home);
        Assert.IsNull(read.Work);
        Assert.AreEqual(2, differences.Differences.Count, differences.ToString());
        foreach (string property in new[] { "Home", "Work" })
        {
          Difference unmatched = Find(differences, typeof(OtherClassesReader.AddressEntity), DifferenceKind.UnmatchedClass, property);
          Assert.AreEqual(1, unmatched.Count);
          Assert.IsTrue(unmatched.IsError);
          CollectionAssert.AreEqual(new[] { typeof(OtherClassesWriter.AddressDto), typeof(OtherClassesWriter.LineDto) }, unmatched.WriterClasses.ToArray());
          CollectionAssert.AreEqual(new[] { "$" }, Paths(differences, unmatched));
        }
        StringAssert.Contains(differences.GenerateReport(), "AddressEntity (Home): no schema entry (read where the writer had AddressDto and LineDto, so neither was used), the object was skipped, 1 time\r\n    in $");
      }
    }

    /// <summary>
    /// A type id that is not found throws (without the schema only when the declared type cannot be created, otherwise it is read as the declared type and reported).
    /// With ReportAndContinue it is reported: skipped with the schema (the values are indexes into the writer's class) and for an abstract declared type, otherwise read as the declared type.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map, "names")]
    [DataRow(ObjectLayout.Array, "names")]
    [DataRow(ObjectLayout.Map, "inline")]
    [DataRow(ObjectLayout.Array, "inline")]
    [DataRow(ObjectLayout.Map, "store")]
    public void UnresolvedTypeIdsAreReported(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = Settings(layout, schema);
      byte[] bytes = PenWithUnknownTypes(Serializer, settings);
      Assert.Throws<Exception>(() => Serializer.Deserialize<DifferencesWriter.DiffPenDto>(bytes, settings));
      Assert.Throws<Exception>(() => Serializer.Deserialize<DifferencesWriter.DiffPenDto>(bytes, settings, out ReadDifferences unused));

      settings.ReadErrors = ReadErrorHandling.ReportAndContinue;
      for (int call = 0; call < 2; call++)
      {
        DifferencesWriter.DiffPenDto read = Serializer.Deserialize<DifferencesWriter.DiffPenDto>(bytes, settings, out ReadDifferences differences);
        Assert.AreEqual("pen", read.Name);
        Assert.IsNull(read.Shape);

        Difference shape = Find(differences, typeof(DifferencesWriter.DiffShape), DifferenceKind.UnresolvedType, "DiffSquarX");
        Assert.IsTrue(shape.IsError);
        Assert.AreEqual(1, shape.Count);
        CollectionAssert.AreEqual(new[] { "$" }, Paths(differences, shape));

        Difference pet = Find(differences, typeof(DifferencesWriter.DiffAnimal), DifferenceKind.UnresolvedType, "DiffUnicorX");
        CollectionAssert.AreEqual(new[] { "$" }, Paths(differences, pet));
        if (schema == "names")
        {
          Assert.IsFalse(pet.IsError);
          Assert.AreEqual(typeof(DifferencesWriter.DiffAnimal), read.Pet.GetType());
          if (layout == ObjectLayout.Map) // an array is read by position, the subclass's values do not fit
            Assert.AreEqual("Sparkle", read.Pet.Name);
          StringAssert.Contains(differences.GenerateReport(), "DiffAnimal (\"DiffUnicorX\"): type not found, read as DiffAnimal, 1 time\r\n    in $");
        }
        else
        {
          Assert.IsTrue(pet.IsError);
          Assert.IsNull(read.Pet);
          Assert.AreEqual(2, differences.Differences.Count, differences.ToString());
        }
        StringAssert.Contains(differences.GenerateReport(), "DiffShape (\"DiffSquarX\"): type not found, the object was skipped, 1 time\r\n    in $\r\n    if the class exists here, register its assembly: MsgPackTypes.CacheAssemblyTypes(typeof(DiffSquarX))");
      }
    }

    /// <summary>
    /// A type that is found but does not fit where it goes still throws (see docs/security.md), with or without the differences.
    /// </summary>
    [TestMethod]
    [DataRow("names")]
    [DataRow("inline")]
    public void TypeThatDoesNotFitStillThrows(string schema)
    {
      MsgPackSettings settings = Settings(ObjectLayout.Map, schema);
      byte[] bytes = Serializer.Serialize(new DifferencesWriter.DiffPenDto() { Pet = new DifferencesWriter.DiffUnicorn() { Name = "Sparkle" } }, settings);
      Rename(bytes, "DiffUnicorn", "DiffLineDto"); // found, not a DiffAnimal
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<DifferencesWriter.DiffPenDto>(bytes, settings, out ReadDifferences unused));
      StringAssert.Contains(ex.Message, "cannot be assigned to");
    }

    internal static byte[] Errors(ISerializerUnderTest serializer, MsgPackSettings settings)
    {
      DifferencesWriter.DiffErrorsDto written = new DifferencesWriter.DiffErrorsDto()
      {
        Id = "abc", // not an int
        Count = 300, // too large for a byte
        Colour = "Purple", // not a DiffColour
        Name = "ok",
        Lines = new List<string>() { "1", "x" }, // "x" is not an int: the list is skipped
        Inner = new DifferencesWriter.DiffInnerDto() { Amount = "12a", Note = "fine" }
      };
      return serializer.Serialize(written, settings);
    }

    private static readonly string[] ErrorProperties = { "Id", "Count", "Colour", "Lines" };

    /// <summary>
    /// The default (FailFast) throws at the first value that does not convert, with the differences found until then (including that error) when they are collected.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map, "names")]
    [DataRow(ObjectLayout.Array, "inline")]
    public void FailFastThrowsAtTheFirstError(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = Settings(layout, schema);
      byte[] bytes = Errors(Serializer, settings);
      Exception plain = Assert.Throws<Exception>(() => Serializer.Deserialize<DifferencesReader.DiffErrorsEntity>(bytes, settings));
      Assert.IsFalse(plain.Data.Contains(ReadDifferences.ExceptionDataKey));
      Assert.IsFalse(plain.Data.Contains(ReadDifferences.NotConvertedKey), "not marked without the differences");

      Exception ex = Assert.Throws<Exception>(() => Serializer.Deserialize<DifferencesReader.DiffErrorsEntity>(bytes, settings, out ReadDifferences unused));
      ReadDifferences differences = (ReadDifferences)ex.Data[ReadDifferences.ExceptionDataKey];
      Assert.AreEqual(1, differences.ErrorCount, differences.ToString());
      Difference error = differences.Differences.Single(d => d.IsError);
      Assert.AreEqual(DifferenceKind.InvalidValue, error.Kind);
      Assert.AreSame(ex, error.Error);
      Assert.AreEqual(plain.GetType(), ex.GetType(), "the same exception as without the differences");
    }

    /// <summary>
    /// ReportAndContinue skips the values that do not convert (the properties keep the constructor's values) and reports them; FailDeferred does the same and then throws with all of them.
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map, "names")]
    [DataRow(ObjectLayout.Array, "names")]
    [DataRow(ObjectLayout.Map, "inline")]
    [DataRow(ObjectLayout.Array, "inline")]
    [DataRow(ObjectLayout.Map, "store")]
    [DataRow(ObjectLayout.Array, "reference")]
    public void ErrorsAreSkippedAndReported(ObjectLayout layout, string schema)
    {
      MsgPackSettings settings = Settings(layout, schema);
      settings.ReadErrors = ReadErrorHandling.ReportAndContinue;
      byte[] bytes = Errors(Serializer, settings);

      for (int call = 0; call < 2; call++)
      {
        DifferencesReader.DiffErrorsEntity read = Serializer.Deserialize<DifferencesReader.DiffErrorsEntity>(bytes, settings, out ReadDifferences differences);
        Assert.AreEqual(7, read.Id);
        Assert.AreEqual(0, read.Count);
        Assert.AreEqual(DifferencesReader.DiffColour.Green, read.Colour);
        Assert.AreEqual("ok", read.Name);
        Assert.IsNull(read.Lines);
        Assert.AreEqual(5, read.Inner.Amount);
        Assert.AreEqual("fine", read.Inner.Note);

        Assert.AreEqual(5, differences.ErrorCount, differences.ToString());
        Assert.AreEqual(5, differences.Differences.Count, differences.ToString());
        foreach (string property in ErrorProperties)
        {
          Difference error = Find(differences, typeof(DifferencesReader.DiffErrorsEntity), DifferenceKind.InvalidValue, property);
          Assert.IsTrue(error.IsError);
          Assert.IsNotNull(error.Error);
          CollectionAssert.AreEqual(new[] { "$" }, Paths(differences, error));
        }
        Difference amount = Find(differences, typeof(DifferencesReader.DiffInnerEntity), DifferenceKind.InvalidValue, "Amount");
        Assert.IsInstanceOfType(amount.Error, typeof(FormatException));
        CollectionAssert.AreEqual(new[] { "$.Inner" }, Paths(differences, amount));
        Assert.IsInstanceOfType(Find(differences, typeof(DifferencesReader.DiffErrorsEntity), DifferenceKind.InvalidValue, "Count").Error, typeof(OverflowException));
        string report = differences.GenerateReport();
        StringAssert.Contains(report, "5 differences (5 values could not be read) between the data and the classes:");
        StringAssert.Contains(report, "DiffInnerEntity.Amount: could not be read (");
        StringAssert.Contains(report, "), skipped, 1 time\r\n    at $.Inner");
      }

      DifferencesReader.DiffErrorsEntity withoutOut = Serializer.Deserialize<DifferencesReader.DiffErrorsEntity>(bytes, settings); // the same object
      Assert.AreEqual(7, withoutOut.Id);
      Assert.AreEqual("ok", withoutOut.Name);

      settings.ReadErrors = ReadErrorHandling.FailDeferred;
      foreach (bool withOut in new[] { true, false })
      {
        ReadErrorsException deferred = withOut
          ? Assert.Throws<ReadErrorsException>(() => Serializer.Deserialize<DifferencesReader.DiffErrorsEntity>(bytes, settings, out ReadDifferences unused))
          : Assert.Throws<ReadErrorsException>(() => Serializer.Deserialize<DifferencesReader.DiffErrorsEntity>(bytes, settings));
        Assert.AreEqual(5, deferred.Differences.ErrorCount);
        Assert.AreSame(deferred.Differences, deferred.Data[ReadDifferences.ExceptionDataKey]);
        Assert.IsNotNull(deferred.Differences.Root, "read to the end, so the paths are known");
        Assert.IsNotNull(deferred.InnerException);
        StringAssert.StartsWith(deferred.Message, "5 values could not be read (ReadErrors = FailDeferred). 5 differences");
        StringAssert.Contains(deferred.Message, "at $.Inner");
      }
    }

    /// <summary>
    /// What is not a value that does not convert still throws in every mode: exceptions of setters, types that do not fit (docs/security.md), data that ends early.
    /// </summary>
    [TestMethod]
    [DataRow(ReadErrorHandling.FailDeferred)]
    [DataRow(ReadErrorHandling.ReportAndContinue)]
    public void OtherErrorsStillThrow(ReadErrorHandling mode)
    {
      MsgPackSettings settings = Settings(ObjectLayout.Map, "names");
      settings.ReadErrors = mode;
      byte[] boom = Serializer.Serialize(new DifferencesWriter.DiffThrowDto() { Boom = "boom" }, settings);
      Assert.Throws<InvalidOperationException>(() => Serializer.Deserialize<DifferencesReader.DiffThrowEntity>(boom, settings, out ReadDifferences unused));

      byte[] notFitting = Serializer.Serialize(new DifferencesWriter.DiffPenDto() { Pet = new DifferencesWriter.DiffUnicorn() { Name = "Sparkle" } }, settings);
      Rename(notFitting, "DiffUnicorn", "DiffLineDto");
      StringAssert.Contains(Assert.Throws<MsgPackException>(() => Serializer.Deserialize<DifferencesWriter.DiffPenDto>(notFitting, settings, out ReadDifferences unused)).Message, "cannot be assigned to");

      byte[] errors = Errors(Serializer, settings);
      Assert.Throws<Exception>(() => Serializer.Deserialize<DifferencesReader.DiffErrorsEntity>(errors.Take(errors.Length - 3).ToArray(), settings, out ReadDifferences unused)); // KEEPTRACK: the error becomes the value, which does not cast
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
      settings.ReadErrors = ReadErrorHandling.ReportAndContinue;

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

      if (schema != "reference" && (schema != "names" || layout == ObjectLayout.Map)) // the names are in the bytes
        Compare<DifferencesWriter.DiffPenDto>(ReportingDifferencesTests.PenWithUnknownTypes(Serializers.Ls, settings), settings);

      Compare<DifferencesReader.DiffErrorsEntity>(ReportingDifferencesTests.Errors(Serializers.Ls, settings), settings);
    }
  }
}
