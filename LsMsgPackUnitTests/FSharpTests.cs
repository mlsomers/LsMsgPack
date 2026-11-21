using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPackFSharpTestTypes;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPackUnitTests
{
  #region Model

  /// <summary>
  /// A C# class with members of F# types (the source generator describes it, the F# converters take over its F# members).
  /// </summary>
  public class FsCsHolder
  {
    public FSharpOption<string> Nick { get; set; }
    public FSharpValueOption<int> Count { get; set; }
    public FsShape Shape { get; set; }
    public FSharpList<int> Numbers { get; set; }
    public FsColour Colour { get; set; }
    public FsEmail Mail { get; set; }
    public FSharpMap<string, FsShape> ByName { get; set; }

    public override bool Equals(object obj)
    {
      return obj is FsCsHolder other && Equals(Nick, other.Nick) && Count.Equals(other.Count) && Equals(Shape, other.Shape) && Equals(Numbers, other.Numbers)
        && Equals(Colour, other.Colour) && Equals(Mail, other.Mail) && Equals(ByName, other.ByName);
    }

    public override int GetHashCode() { return 0; }
  }

  /// <summary>
  /// The plain form of FsPerson: the same names, the options as their values.
  /// </summary>
  public class FsPersonPlain
  {
    public string Name { get; set; }
    public string Nick { get; set; }
    public int? Age { get; set; }
    public FsAddress Home { get; set; }
    public double? Score { get; set; }
  }

  #endregion

  /// <summary>
  /// F# options, unions, records and collections (docs/FSharp.md), with the types of LsMsgPackFSharpTestTypes.
  /// </summary>
  public abstract class FSharpTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    #region Values

    private static FSharpOption<T> Some<T>(T value) { return FSharpOption<T>.Some(value); }

    private static FSharpList<T> List<T>(params T[] items) { return ListModule.OfSeq(items); }

    internal static FsPerson PersonWithAll()
    {
      return new FsPerson("Ann", Some("annie"), FSharpValueOption<int>.NewValueSome(0), Some(new FsAddress("Main", 5)), Some(1.5));
    }

    internal static FsPerson PersonWithNone()
    {
      return new FsPerson("Bob", null, FSharpValueOption<int>.ValueNone, null, null);
    }

    internal static FsHolder Holder()
    {
      return new FsHolder(
        List(FsShape.NewCircle(2.0), FsShape.NewRect(1.0, 3.5), FsShape.Empty),
        SetModule.OfSeq(new[] { "b", "a" }),
        MapModule.OfSeq(new[] { Tuple.Create("one", 1), Tuple.Create("two", 2) }),
        Tuple.Create(7, "seven"),
        FsColour.Blue,
        FsEmail.NewFsEmail("a@b.nl"),
        Some(FsShape.Empty),
        FSharpChoice<int, string>.NewChoice2Of2("second"),
        new[] { FsBig.A, FsBig.NewE(3), FsBig.F });
    }

    internal static FsWithOutcome WithOutcome()
    {
      return new FsWithOutcome(
        FsOutcome.NewOk(200),
        FsAnswer.NewOk("yes"),
        FsGeneric<string>.NewValue("v"),
        List(FsUnnamed.NewOne(1), FsUnnamed.NewTwo(2, "two"), FsUnnamed.Nothing),
        FsTree.NewNode(List(FsTree.NewLeaf(1), FsTree.NewNode(List(FsTree.NewLeaf(2))))),
        FsQualified.NewSecond(2),
        FsOrderId.NewFsOrderId(new Guid("0b0e9b8e-1b3c-4d6e-8f00-112233445566")),
        FsStructColour.Down,
        Some(Some(3)));
    }

    internal static FsCsHolder CsHolder()
    {
      return new FsCsHolder
      {
        Nick = Some("n"),
        Count = FSharpValueOption<int>.NewValueSome(4),
        Shape = FsShape.NewRect(2, 3),
        Numbers = List(1, 2, 3),
        Colour = FsColour.Green,
        Mail = FsEmail.NewFsEmail("c@d.nl"),
        ByName = MapModule.OfSeq(new[] { Tuple.Create("c", FsShape.NewCircle(1)), Tuple.Create("e", FsShape.Empty) })
      };
    }

    /// <summary>
    /// The values every test reads and writes in every setting.
    /// </summary>
    internal static IEnumerable<object> Values()
    {
      yield return new FsAddress("Main", 5);
      yield return new FsPoint(1.5, -2);
      yield return new FsMutable(1, "b");
      yield return PersonWithAll();
      yield return PersonWithNone();
      yield return Holder();
      yield return WithOutcome();
      yield return CsHolder();
      yield return FsShape.NewCircle(2.0);
      yield return FsShape.Empty;
      yield return FsColour.Red;
      yield return FsEmail.NewFsEmail("x@y.nl");
      yield return FsPair.NewFsPair(1, "one");
      yield return List(FsGeneric<int>.NewValue(5), FsGeneric<int>.NoValue);
      yield return List(FsShape.NewCircle(1), FsShape.Empty);
      yield return Some(42);
      yield return Some(FsShape.NewRect(1, 2));
    }

    /// <summary>
    /// The settings every value is read and written with.
    /// </summary>
    internal static IEnumerable<MsgPackSettings> AllSettings()
    {
      yield return new MsgPackSettings();
      yield return new MsgPackSettings() { ObjectLayout = ObjectLayout.Map };
      yield return new MsgPackSettings() { UseInexedSchema = false };
      yield return MsgPackMediaTypes.ToPlain(new MsgPackSettings());
      yield return new MsgPackSettings() { SchemaVersion = SchemaVersion.Described };
      yield return new MsgPackSettings() { AddTypeIdOptions = AddTypeIdOption.IfAmbiguious | AddTypeIdOption.FullName };
      yield return new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve };
    }

    internal static string Describe(MsgPackSettings settings)
    {
      return $"schema {settings.UseInexedSchema} v{(int)settings.SchemaVersion}, {settings.ObjectLayout}, type ids {settings.AddTypeIdOptions}, references {settings.ReferenceHandling}";
    }

    #endregion

    [TestMethod]
    public void AllValues_RoundTrip_InEverySetting()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        foreach (object value in Values())
        {
          string message = $"{value.GetType().Name} ({Describe(settings)})";
          byte[] bytes = Serializer.Serialize(value, value.GetType(), settings);
          object read = Serializer.Deserialize(value.GetType(), bytes, settings);
          Assert.AreEqual(value, read, message);
        }
      }
    }

    [TestMethod]
    public void AllValues_SameBytesAsDebug()
    {
      foreach (MsgPackSettings settings in AllSettings())
      {
        foreach (object value in Values())
        {
          byte[] expected = Serializers.Debug.Serialize(value, value.GetType(), settings);
          CollectionAssert.AreEqual(expected, Serializer.Serialize(value, value.GetType(), settings), $"{value.GetType().Name} ({Describe(settings)})");
        }
      }
    }

    [TestMethod]
    public void ManyCalls_CompiledAndTypedPaths()
    {
      // LsMsgPack compiles its plans and accessors after the first calls: the results stay the same
      MsgPackSettings settings = new MsgPackSettings();
      object[] values = Values().ToArray();
      for (int round = 0; round < 150; round++)
      {
        for (int t = 0; t < values.Length; t++)
        {
          byte[] bytes = Serializer.Serialize(values[t], values[t].GetType(), settings);
          Assert.AreEqual(values[t], Serializer.Deserialize(values[t].GetType(), bytes, settings), $"round {round}: {values[t].GetType().Name}");
        }
      }
    }

    [TestMethod]
    public void Options_WrittenAsTheirValues()
    {
      MsgPackSettings plain = MsgPackMediaTypes.ToPlain(new MsgPackSettings());
      FsPersonPlain all = new FsPersonPlain() { Name = "Ann", Nick = "annie", Age = 0, Home = new FsAddress("Main", 5), Score = 1.5 };
      CollectionAssert.AreEqual(Serializer.Serialize(all, plain), Serializer.Serialize(PersonWithAll(), plain), "Some x is x (also Some 0)");
      FsPersonPlain none = new FsPersonPlain() { Name = "Bob" };
      CollectionAssert.AreEqual(Serializer.Serialize(none, plain), Serializer.Serialize(PersonWithNone(), plain), "None is nil");

      Assert.AreEqual(PersonWithAll(), Serializer.Deserialize<FsPerson>(Serializer.Serialize(all, plain), plain), "read from their values");
    }

    [TestMethod]
    public void Options_OfOptions_SomeNoneIsNone()
    {
      // Some None is nil, as System.Text.Json and MessagePack.FSharpExtensions write it (documented)
      FSharpOption<FSharpOption<int>> someNone = Some<FSharpOption<int>>(null);
      Assert.IsNull(Serializer.Deserialize<FSharpOption<FSharpOption<int>>>(Serializer.Serialize(someNone)));
      FSharpOption<FSharpOption<int>> someSome = Some(Some(3));
      Assert.AreEqual(someSome, Serializer.Deserialize<FSharpOption<FSharpOption<int>>>(Serializer.Serialize(someSome)));
    }

    [TestMethod]
    public void Unions_PlainFormat()
    {
      MsgPackSettings plain = MsgPackMediaTypes.ToPlain(new MsgPackSettings());
      AssertPlain(new Dictionary<string, object>() { { "", "Circle" }, { "radius", 2.0 } }, FsShape.NewCircle(2.0), typeof(FsShape), plain, "a case with fields: its name as the type id (also with AddTypeIdOption.Never)");
      AssertPlain(new Dictionary<string, object>() { { "", "Two" }, { "Item1", 2 }, { "Item2", "b" } }, FsUnnamed.NewTwo(2, "b"), typeof(FsUnnamed), plain, "unnamed fields");
      AssertPlain("Empty", FsShape.Empty, typeof(FsShape), plain, "a fieldless case: its name");
      AssertPlain("Blue", FsColour.Blue, typeof(FsColour), plain, "a union of fieldless cases");
      AssertPlain("A", FsBig.A, typeof(FsBig), plain, "a fieldless case of a larger union (an instance of the union type)");
      AssertPlain("a@b.nl", FsEmail.NewFsEmail("a@b.nl"), typeof(FsEmail), plain, "one case with one field: the field");
      AssertPlain(new Dictionary<string, object>() { { "left", 1 }, { "right", "x" } }, FsPair.NewFsPair(1, "x"), typeof(FsPair), plain, "one case with several fields: an object of them, no type id");
      AssertPlain(new Dictionary<string, object>() { { "", "Value" }, { "Item", 5 } }, FsGeneric<int>.NewValue(5), typeof(FsGeneric<int>), plain, "a case of a generic union: the case name");
      AssertPlain(new object[] { 1, 2 }, List(1, 2), typeof(FSharpList<int>), plain, "an F# list: an array");
    }

    /// <param name="declared">The type the value is declared as (a case class declared as itself has no type id)</param>
    private void AssertPlain(object expected, object value, Type declared, MsgPackSettings settings, string message)
    {
      byte[] bytes = Serializer.Serialize(value, declared, settings);
      CollectionAssert.AreEqual(MsgPackSerializer.Serialize(expected, expected.GetType(), settings), bytes, message);
      Assert.AreEqual(value, Serializer.Deserialize(declared, bytes, settings), message + ": read back");
    }

    [TestMethod]
    public void Unions_TheTypeIdIsTheCaseName()
    {
      MsgPackSettings byNames = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };
      FsGeneric<int>[] values = { FsGeneric<int>.NewValue(5) };
      Dictionary<string, object>[] expected = { new Dictionary<string, object>() { { "", "Value" }, { "Item", 5 } } };
      CollectionAssert.AreEqual(MsgPackSerializer.Serialize(expected, byNames), Serializer.Serialize(values, byNames));
    }

    [TestMethod]
    public void Unions_CaseNamesOfOtherUnions_ResolvedInTheDeclaredUnion()
    {
      // FsOutcome.Ok and FsAnswer.Ok: the same case name, each read as its own union's case
      foreach (MsgPackSettings settings in AllSettings())
      {
        FsWithOutcome value = WithOutcome();
        Assert.AreEqual(value, Serializer.Deserialize<FsWithOutcome>(Serializer.Serialize(value, settings), settings), Describe(settings));
      }
    }

    [TestMethod]
    public void Unions_FieldlessCase_ReadByItsNameIgnoringCase()
    {
      Assert.AreEqual(FsColour.Blue, Serializer.Deserialize<FsColour>(Serializer.Serialize("blue")));
      Assert.AreEqual(FsShape.Empty, Serializer.Deserialize<FsShape>(Serializer.Serialize("EMPTY")));
    }

    [TestMethod]
    public void Unions_UnknownCaseName_Throws()
    {
      Exception ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<FsColour>(Serializer.Serialize("Purple")));
      StringAssert.Contains(ex.Message, "Purple");
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<FsShape>(Serializer.Serialize("Circle")), "a case with fields is not a name");
    }

    [TestMethod]
    public void Unions_StructUnionWithFields_Refused()
    {
      FsWithStructUnion value = new FsWithStructUnion(FsStructShape.NewSCircle(1));
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(value));
      StringAssert.Contains(ex.Message, "struct union");
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(new FsWithResult(FSharpResult<int, string>.NewOk(1))));
    }

    [TestMethod]
    public void Unions_FieldlessCaseDeclaredAsObject_ReadAsItsName()
    {
      // Without the declared union a name is a string (as an enum written by name, documented)
      FsWithObject value = new FsWithObject(FsShape.Empty);
      Assert.AreEqual("Empty", Serializer.Deserialize<FsWithObject>(Serializer.Serialize(value)).Value);
      FsWithObject withFields = new FsWithObject(FsShape.NewCircle(3));
      Assert.AreEqual(FsShape.NewCircle(3), Serializer.Deserialize<FsWithObject>(Serializer.Serialize(withFields)).Value, "a case with fields has its type id");
    }

    [TestMethod]
    public void ReadDifferences_NothingToReport()
    {
      FsHolder value = Holder();
      FsHolder read = Serializer.Deserialize<FsHolder>(Serializer.Serialize(value), new MsgPackSettings(), out ReadDifferences differences);
      Assert.AreEqual(value, read);
      Assert.IsTrue(differences is null || differences.Differences.Count == 0, differences?.GenerateReport());
    }

    [TestMethod]
    public void Preserve_ASharedCaseIsOneInstance()
    {
      FsShape circle = FsShape.NewCircle(1);
      FsShape[] shapes = { circle, circle, FsShape.Empty, FsShape.Empty };
      MsgPackSettings settings = new MsgPackSettings() { ReferenceHandling = ReferenceHandling.Preserve };
      FsShape[] read = Serializer.Deserialize<FsShape[]>(Serializer.Serialize(shapes, settings), settings);
      CollectionAssert.AreEqual(shapes, read);
      Assert.AreSame(read[0], read[1]);
      Assert.AreSame(FsShape.Empty, read[2], "a fieldless case is F#'s own instance");
    }

    [TestMethod]
    public void Collections_ListMapSet()
    {
      FSharpList<FsAddress> list = List(new FsAddress("a", 1), new FsAddress("b", 2));
      Assert.AreEqual(list, Serializer.Deserialize<FSharpList<FsAddress>>(Serializer.Serialize(list)));
      Assert.AreEqual(FSharpList<int>.Empty, Serializer.Deserialize<FSharpList<int>>(Serializer.Serialize(FSharpList<int>.Empty)));
      FSharpMap<int, string> map = MapModule.OfSeq(new[] { Tuple.Create(2, "two"), Tuple.Create(1, "one") });
      Assert.AreEqual(map, Serializer.Deserialize<FSharpMap<int, string>>(Serializer.Serialize(map, new MsgPackSettings() { UseInexedSchema = false }), new MsgPackSettings() { UseInexedSchema = false }));
      FSharpSet<FsColour> set = SetModule.OfSeq(new[] { FsColour.Green, FsColour.Red });
      Assert.AreEqual(set, Serializer.Deserialize<FSharpSet<FsColour>>(Serializer.Serialize(set)));
      FSharpMap<FsColour, FSharpOption<int>> byColour = MapModule.OfSeq(new[] { Tuple.Create(FsColour.Red, Some(1)), Tuple.Create(FsColour.Blue, (FSharpOption<int>)null) });
      foreach (MsgPackSettings settings in new[] { new MsgPackSettings(), new MsgPackSettings() { UseInexedSchema = false } })
        Assert.AreEqual(byColour, Serializer.Deserialize<FSharpMap<FsColour, FSharpOption<int>>>(Serializer.Serialize(byColour, settings), settings), "union keys by name, options as values");
    }
  }

  [TestClass]
  public class DebugFSharpTests : FSharpTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsFSharpTests : FSharpTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class GenFSharpTests : FSharpTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
