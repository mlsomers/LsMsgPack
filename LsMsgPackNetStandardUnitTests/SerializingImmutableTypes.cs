using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Immutable classes and structs: properties without a public setter that a constructor takes (<c>Money(decimal amount, string currency)</c>) are written, and read by calling that constructor,
  /// as System.Text.Json does (they used to be left out, the object arrived empty). Also classes whose only constructor takes settable properties (they used to be created uninitialized).
  /// </summary>
  public abstract class SerializingImmutableTypes
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class ImmMoney
    {
      public ImmMoney(decimal amount, string currency)
      {
        Amount = amount;
        Currency = currency;
      }

      public decimal Amount { get; }
      public string Currency { get; }
      public string Text { get { return $"{Amount} {Currency}"; } } // not taken by the constructor: computed (written, skipped when read)
    }

    public class ImmOrder
    {
      public int Id { get; set; }
      public ImmMoney Total { get; set; }
      public List<ImmMoney> Lines { get; set; }
      public Dictionary<string, ImmMoney> ByName { get; set; }
      public ImmPoint Position { get; set; }
      public ImmPoint? Optional { get; set; }
      public object Anything { get; set; }
    }

    /// <summary>
    /// The constructor takes some properties, a public setter sets another.
    /// </summary>
    public class ImmMixed
    {
      public ImmMixed(int id)
      {
        Id = id;
        Note = "constructor";
      }

      public int Id { get; }
      public string Name { get; set; }
      public string Note { get; set; } // set by the constructor, written, and set again from the data
    }

    /// <summary>
    /// Private setters and a private parameterless constructor (as Entity Framework wants them): the public constructor is called.
    /// </summary>
    public class ImmEntity
    {
      private ImmEntity() { }

      public ImmEntity(Guid key, string label)
      {
        Key = key;
        Label = label;
      }

      public Guid Key { get; private set; }
      public string Label { get; private set; }
    }

    public readonly struct ImmPoint
    {
      public ImmPoint(int x, int y)
      {
        X = x;
        Y = y;
      }

      public int X { get; }
      public int Y { get; }
    }

    /// <summary>
    /// Several constructors: [JsonConstructor] picks one.
    /// </summary>
    public class ImmMarked
    {
      public ImmMarked(string code) : this(code, 1) { }

      [System.Text.Json.Serialization.JsonConstructor]
      public ImmMarked(string code, int count)
      {
        Code = code;
        Count = count;
      }

      public string Code { get; }
      public int Count { get; }
    }

    /// <summary>
    /// Several public constructors, none marked: no constructor is called, the properties are computed ones (written, not read).
    /// </summary>
    public class ImmUnmarked
    {
      public ImmUnmarked(string code) { Code = code; }
      public ImmUnmarked(string code, int count) { Code = code; Count = count; }

      public string Code { get; }
      public int Count { get; }
    }

    public class ImmValidated
    {
      public ImmValidated(string name)
      {
        if (string.IsNullOrEmpty(name))
          throw new ArgumentException("A name is required.", nameof(name));
        Name = name;
      }

      public string Name { get; }
    }

    public class ImmDefaults
    {
      public ImmDefaults(string currency = "EUR", int scale = 2, [DefaultValue(5)] int unused = 5)
      {
        Currency = currency;
        Scale = scale;
      }

      public string Currency { get; }
      public int Scale { get; }
    }

    public record ImmRecord(int Id, string Name); // init setters: read by the constructor as System.Text.Json does (until October 2026 created uninitialized and set)

    /// <summary>
    /// Only a constructor taking a settable property, which initializes a field: called with the value (until October 2026 created uninitialized, the list was null).
    /// </summary>
    public class ImmOwner
    {
      private readonly List<string> _items;

      public ImmOwner(string name)
      {
        Name = name;
        _items = new List<string>() { "created" };
      }

      public string Name { get; set; }
      public int Level { get; set; } = 5;

      public int Count() { return _items.Count; }
    }

    /// <summary>
    /// The property the constructor takes comes after the others: their values are read before the object exists (LtMsgPack collects them), then set.
    /// </summary>
    public class ImmLateName
    {
      public ImmLateName(string name) { Name = name; }

      public int Count { get; set; }
      public decimal Price { get; set; }
      public DateTime When { get; set; }
      public string Name { get; }
    }

    /// <summary>
    /// A struct whose constructor takes settable properties: created by its default constructor as System.Text.Json does, the constructor is not called.
    /// </summary>
    public struct ImmSettablePoint
    {
      private readonly bool _constructed;

      public ImmSettablePoint(int x, int y)
      {
        X = x;
        Y = y;
        _constructed = true;
      }

      public int X { get; set; }
      public int Y { get; set; }

      public bool Constructed() { return _constructed; }
    }

    private static MsgPackSettings Settings(bool schema, ObjectLayout layout, bool trim = false)
    {
      return new MsgPackSettings() { UseInexedSchema = schema, ObjectLayout = layout, TrimTrailingNulls = trim };
    }

    private static IEnumerable<object[]> AllSettings()
    {
      foreach (bool schema in new[] { true, false })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          yield return new object[] { schema, layout };
    }

    private static string Text(ImmMoney money)
    {
      return money is null ? "null" : $"{money.Amount} {money.Currency ?? "null"}";
    }

    private T RoundTrip<T>(T value, MsgPackSettings settings)
    {
      byte[] bytes = Serializer.Serialize(value, settings);
      return Serializer.Deserialize<T>(bytes, settings);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void GetOnly_ReadByConstructor(bool schema, ObjectLayout layout)
    {
      ImmMoney read = RoundTrip(new ImmMoney(12.5m, "EUR"), Settings(schema, layout));
      Assert.AreEqual("12.5 EUR", Text(read));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void DefaultValues_LeftOutAndPassed(bool schema, ObjectLayout layout)
    {
      Assert.AreEqual("0 null", Text(RoundTrip(new ImmMoney(0m, null), Settings(schema, layout))));
      Assert.AreEqual("0 null", Text(RoundTrip(new ImmMoney(0m, null), Settings(schema, layout, trim: true))));
      Assert.AreEqual("3 null", Text(RoundTrip(new ImmMoney(3m, null), Settings(schema, layout, trim: true))));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ParameterDefaults_NotForLeftOutValues(bool schema, ObjectLayout layout)
    {
      ImmDefaults read = RoundTrip(new ImmDefaults(null, 0), Settings(schema, layout));
      Assert.IsNull(read.Currency, "a left out null is null, not the parameter's default");
      Assert.AreEqual(0, read.Scale);
      read = RoundTrip(new ImmDefaults(), Settings(schema, layout));
      Assert.AreEqual("EUR", read.Currency);
      Assert.AreEqual(2, read.Scale);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void InGraph_RoundTrips(bool schema, ObjectLayout layout)
    {
      ImmOrder order = new ImmOrder()
      {
        Id = 4,
        Total = new ImmMoney(30m, "USD"),
        Lines = new List<ImmMoney>() { new ImmMoney(10m, "USD"), null, new ImmMoney(20m, "USD") },
        ByName = new Dictionary<string, ImmMoney>() { { "a", new ImmMoney(1m, "GBP") } },
        Position = new ImmPoint(3, -4),
        Optional = new ImmPoint(0, 7),
        Anything = new ImmMoney(2m, "CHF"), // with a type id
      };
      ImmOrder read = RoundTrip(order, Settings(schema, layout));
      Assert.AreEqual(4, read.Id);
      Assert.AreEqual("30 USD", Text(read.Total));
      Assert.AreEqual("10 USD, null, 20 USD", string.Join(", ", read.Lines.Select(Text)));
      Assert.AreEqual("1 GBP", Text(read.ByName["a"]));
      Assert.AreEqual(3, read.Position.X);
      Assert.AreEqual(-4, read.Position.Y);
      Assert.AreEqual(7, read.Optional.Value.Y);
      Assert.AreEqual("2 CHF", Text(read.Anything as ImmMoney));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Struct_ReadByConstructor(bool schema, ObjectLayout layout)
    {
      ImmPoint read = RoundTrip(new ImmPoint(5, 6), Settings(schema, layout));
      Assert.AreEqual(5, read.X);
      Assert.AreEqual(6, read.Y);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Mixed_ConstructorThenSetters(bool schema, ObjectLayout layout)
    {
      ImmMixed read = RoundTrip(new ImmMixed(9) { Name = "n", Note = "data" }, Settings(schema, layout));
      Assert.AreEqual(9, read.Id);
      Assert.AreEqual("n", read.Name);
      Assert.AreEqual("data", read.Note, "the setter runs after the constructor");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void PrivateSetters_ReadByPublicConstructor(bool schema, ObjectLayout layout)
    {
      Guid key = Guid.NewGuid();
      ImmEntity read = RoundTrip(new ImmEntity(key, "label"), Settings(schema, layout));
      Assert.AreEqual(key, read.Key);
      Assert.AreEqual("label", read.Label);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void MarkedConstructor_Chosen(bool schema, ObjectLayout layout)
    {
      ImmMarked read = RoundTrip(new ImmMarked("c", 3), Settings(schema, layout));
      Assert.AreEqual("c", read.Code);
      Assert.AreEqual(3, read.Count);
    }

    [TestMethod]
    public void SeveralConstructors_NotChosen()
    {
      ImmUnmarked read = RoundTrip(new ImmUnmarked("c", 3), Settings(true, ObjectLayout.Array));
      Assert.IsNull(read.Code, "as before: no constructor is chosen, the properties are not read");
      Assert.AreEqual(0, read.Count);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Record_ReadByConstructor(bool schema, ObjectLayout layout)
    {
      ImmRecord read = RoundTrip(new ImmRecord(1, "r"), Settings(schema, layout));
      Assert.AreEqual(new ImmRecord(1, "r"), read);
      Assert.AreEqual(new ImmRecord(0, null), RoundTrip(new ImmRecord(0, null), Settings(schema, layout, trim: true)));
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void SettableProperties_ConstructorCalled(bool schema, ObjectLayout layout)
    {
      ImmOwner read = RoundTrip(new ImmOwner("o") { Level = 7 }, Settings(schema, layout));
      Assert.AreEqual("o", read.Name);
      Assert.AreEqual(7, read.Level);
      Assert.AreEqual(1, read.Count(), "the constructor initialized the field");

      read = RoundTrip(new List<ImmOwner>() { new ImmOwner("a") { Level = 0 }, new ImmOwner(null) }, Settings(schema, layout))[0];
      Assert.AreEqual("a", read.Name);
      Assert.AreEqual(layout == ObjectLayout.Array ? 0 : 5, read.Level, "the nil of the array is set after the constructor, a value left out of a map keeps the initializer's (as with a parameterless constructor, LSMP001)");
      Assert.AreEqual(1, read.Count());
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ParameterAfterOtherProperties_ValuesSetAfterTheConstructor(bool schema, ObjectLayout layout)
    {
      DateTime when = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
      List<ImmLateName> read = RoundTrip(new List<ImmLateName>()
      {
        new ImmLateName("late") { Count = 3, Price = 1.25m, When = when },
        new ImmLateName(null) { Count = 4 }, // the name left out: the constructor is called at the end of the object
      }, Settings(schema, layout, trim: true));
      Assert.AreEqual("late", read[0].Name);
      Assert.AreEqual(3, read[0].Count);
      Assert.AreEqual(1.25m, read[0].Price);
      Assert.AreEqual(when, read[0].When.ToUniversalTime());
      Assert.IsNull(read[1].Name);
      Assert.AreEqual(4, read[1].Count);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void StructWithSettableProperties_DefaultConstructor(bool schema, ObjectLayout layout)
    {
      ImmSettablePoint read = RoundTrip(new ImmSettablePoint(2, 3), Settings(schema, layout));
      Assert.AreEqual(2, read.X);
      Assert.AreEqual(3, read.Y);
      Assert.IsFalse(read.Constructed(), "as System.Text.Json: a struct is created by its default constructor unless one is marked");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ConstructorException_PassedOn(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Serializer.Serialize(new ImmMixedName() { Name = "" }, Settings(schema, layout));
      ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(() => Serializer.Deserialize<ImmValidated>(bytes, Settings(schema, layout)));
      Assert.AreEqual("name", ex.ParamName);
    }

    public class ImmMixedName
    {
      public string Name { get; set; }
      public int Extra { get; set; }
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ReadIntoImmutable_FromMutableWriter(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Serializer.Serialize(new ImmMixedName() { Name = "x", Extra = 2 }, Settings(schema, layout));
      ImmValidated read = Serializer.Deserialize<ImmValidated>(bytes, Settings(schema, layout), out ReadDifferences differences);
      Assert.AreEqual("x", read.Name);
      if (schema || layout == ObjectLayout.Map) // without names the extra value is a value after the end of the reader's properties
      {
        string report = differences.GenerateReport();
        StringAssert.Contains(report, nameof(ImmValidated));
        StringAssert.Contains(report, "Extra");
        Assert.IsFalse(report.Contains("Arguments", StringComparison.Ordinal), report);
        Assert.AreSame(read, differences.Differences[0].Samples[0], "the sample is the object that was created");
      }
    }

    [TestMethod]
    public void UninitializedSetting_StillCallsTheConstructor()
    {
      MsgPackSettings settings = Settings(true, ObjectLayout.Array);
      settings.ObjectCreation = ObjectCreation.Uninitialized;
      Assert.AreEqual("1 EUR", Text(RoundTrip(new ImmMoney(1m, "EUR"), settings)));
    }
  }

  [TestClass]
  public class LsSerializingImmutableTypes : SerializingImmutableTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingImmutableTypes : SerializingImmutableTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenSerializingImmutableTypes : SerializingImmutableTypes
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
