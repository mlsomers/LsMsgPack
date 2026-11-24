using LsMsgPack;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Reading into existing instances (docs/Populate.md, <see cref="MsgPackOptions.Populate"/>, <see cref="MsgPackPopulateAttribute"/>, the Populate methods):
  /// objects of members without a setter by default, settable members with <see cref="PopulateMembers.All"/> or an attribute, the root value with Populate.
  /// </summary>
  public abstract class PopulateTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public class PopAddress
    {
      public string Street { get; set; }
      public string City { get; set; }

      /// <summary>
      /// Not serialized (internal): which code made the instance.
      /// </summary>
      internal string Origin;
    }

    public class PopLine
    {
      public decimal Amount { get; set; }
    }

    public class PopOrder
    {
      public string Number { get; set; }
      public PopAddress ShipTo { get; } = new PopAddress() { Origin = "constructor" };
      public List<PopLine> Lines { get; } = new List<PopLine>();
      public decimal Total { get { return Lines.Sum(l => l.Amount); } }
      public PopAddress Billing { get; set; } = new PopAddress() { Origin = "constructor", City = "Initial" };
      public List<int> Codes { get; set; } = new List<int>() { 99 };
      public int[] Fixed { get; set; } = new[] { 1 };
    }

    /// <summary>
    /// Writes the values a reader of <see cref="PopOrder"/> gets: the same members.
    /// </summary>
    public class PopOrderDto
    {
      public string Number { get; set; }
      public PopAddressDto ShipTo { get; set; }
      public List<PopLine> Lines { get; set; }
      public decimal Total { get; set; }
      public PopAddressDto Billing { get; set; }
      public List<int> Codes { get; set; }
      public int[] Fixed { get; set; }
    }

    /// <summary>
    /// An address without its City: a reader keeps the City it has.
    /// </summary>
    public class PopAddressDto
    {
      public string Street { get; set; }
    }

    public class PopSettings
    {
      private static readonly PopAddress SharedAddress = new PopAddress() { Origin = "shared" };

      /// <summary>
      /// A getter written by hand: shared state, left alone by the option.
      /// </summary>
      public PopAddress Shared { get { return SharedAddress; } }

      [MsgPackPopulate]
      public PopAddress OptedIn { get { return _optedIn; } }
      private readonly PopAddress _optedIn = new PopAddress() { Origin = "constructor" };

      [MsgPackPopulate(false)]
      public PopAddress OptedOut { get; } = new PopAddress() { Origin = "constructor" };

      [MsgPackPopulate(false)]
      public List<int> OptedOutList { get; } = new List<int>() { 7 };

      [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
      public PopAddress JsonPopulate { get; set; } = new PopAddress() { Origin = "constructor" };

      [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Reuse)]
      public PopAddress NewtonsoftReuse { get; set; } = new PopAddress() { Origin = "constructor" };

      [System.Text.Json.Serialization.JsonInclude]
      public readonly PopAddress ReadonlyField = new PopAddress() { Origin = "constructor" };

      public static PopAddress SharedInstance { get { return SharedAddress; } }
    }

    public class PopSettingsDto
    {
      public PopAddressDto Shared { get; set; }
      public PopAddressDto OptedIn { get; set; }
      public PopAddressDto OptedOut { get; set; }
      public List<int> OptedOutList { get; set; }
      public PopAddressDto JsonPopulate { get; set; }
      public PopAddressDto NewtonsoftReuse { get; set; }
      [System.Text.Json.Serialization.JsonInclude]
      public PopAddressDto ReadonlyField;
    }

    /// <summary>
    /// The class an option or attribute opts out as a whole: the members it declares are replaced (skipped without a setter).
    /// </summary>
    [MsgPackPopulate(false)]
    public class PopOptedOutClass
    {
      public PopAddress Address { get; } = new PopAddress() { Origin = "constructor" };
    }

    public abstract class PopShape
    {
      public string Name { get; set; }
    }

    public class PopCircle : PopShape
    {
      public double Radius { get; set; }
    }

    public class PopSquare : PopShape
    {
      public double Side { get; set; }
    }

    public class PopDrawing
    {
      public PopShape Shape { get; } = new PopCircle() { Name = "initial" };
    }

    public class PopDrawingDto
    {
      public PopShape Shape { get; set; }
    }

    /// <summary>
    /// An immutable class (created by its constructor taking the values): never read into, a get-only member holding one is skipped.
    /// </summary>
    public class PopMoney
    {
      public PopMoney(decimal amount, string currency)
      {
        Amount = amount;
        Currency = currency;
      }

      public decimal Amount { get; }
      public string Currency { get; }
    }

    public class PopPriced
    {
      public PopMoney Price { get; } = new PopMoney(0m, "EUR");
    }

    public class PopPricedDto
    {
      public PopMoney Price { get; set; }
    }

    public class PopCalled : IMsgPackOnDeserializing, IMsgPackOnDeserialized
    {
      public string Value { get; set; }
      internal int Deserializing;
      internal int Deserialized;

      public void OnDeserializing() { Deserializing++; }
      public void OnDeserialized() { Deserialized++; }
    }

    public class PopHolder
    {
      public PopCalled Inner { get; } = new PopCalled();
    }

    public class PopHolderDto
    {
      public PopCalled Inner { get; set; }
    }

    /// <summary>
    /// With ReferenceHandling.Preserve: the settable member is written first (the whole object), the get-only one refers to it.
    /// </summary>
    public class PopReferenceLast
    {
      public PopAddress First { get; set; }
      public PopAddress Owned { get; } = new PopAddress() { Origin = "constructor" };
    }

    public class PopReferenceLastDto
    {
      public PopAddress First { get; set; }
      public PopAddress Owned { get; set; }
    }

    /// <summary>
    /// With ReferenceHandling.Preserve: the get-only member is written first, the settable one refers to it.
    /// </summary>
    public class PopReferenceFirst
    {
      public PopAddress Owned { get; } = new PopAddress() { Origin = "constructor" };
      public PopAddress Other { get; set; }
    }

    public class PopReferenceFirstDto
    {
      public PopAddress Owned { get; set; }
      public PopAddress Other { get; set; }
    }

    private static MsgPackSettings Settings(bool schema, ObjectLayout layout, PopulateMembers populate = PopulateMembers.GetOnly)
    {
      return new MsgPackSettings() { UseIndexedSchema = schema, ObjectLayout = layout, Populate = populate };
    }

    private static IEnumerable<object[]> AllSettings()
    {
      foreach (bool schema in new[] { true, false })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          yield return new object[] { schema, layout };
    }

    private static PopOrderDto OrderDto()
    {
      return new PopOrderDto()
      {
        Number = "A-1",
        ShipTo = new PopAddressDto() { Street = "Kerkstraat 1" },
        Lines = new List<PopLine>() { new PopLine() { Amount = 4.5m } },
        Billing = new PopAddressDto() { Street = "Billstreet" },
        Codes = new List<int>() { 1, 2 },
        Fixed = new[] { 5, 6 }
      };
    }

    private byte[] Bytes<T>(T value, MsgPackSettings settings)
    {
      return Serializer.Serialize(value, settings);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void GetOnlyObject_ReadIntoByDefault(bool schema, ObjectLayout layout)
    {
      PopOrder original = new PopOrder() { Number = "A-1" };
      original.ShipTo.Street = "Kerkstraat 1";
      original.ShipTo.City = "Utrecht";
      original.Lines.Add(new PopLine() { Amount = 4.5m });

      PopOrder read = Serializer.Deserialize<PopOrder>(Bytes(original, Settings(schema, layout)), Settings(schema, layout), out ReadDifferences differences);
      Assert.AreEqual("Kerkstraat 1", read.ShipTo.Street);
      Assert.AreEqual("Utrecht", read.ShipTo.City);
      Assert.AreEqual("constructor", read.ShipTo.Origin, "the instance the constructor made");
      Assert.AreEqual(4.5m, read.Total);
      Assert.IsNull(differences);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Default_GetOnlyObjectSkipped(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Bytes(OrderDto(), Settings(schema, layout));
      Assert.AreEqual(PopulateMembers.Collections, new MsgPackSettings().Populate);
      PopOrder read = Serializer.Deserialize<PopOrder>(bytes, new MsgPackSettings() { UseIndexedSchema = schema, ObjectLayout = layout });
      Assert.IsNull(read.ShipTo.Street, "skipped as a computed property");
      Assert.AreEqual(1, read.Lines.Count, "get-only collections are still filled");
      Assert.AreEqual("Billstreet", read.Billing.Street);
      Assert.IsNull(read.Billing.Origin, "a settable member gets a new instance");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void GetOnly_SettableMembersReplaced(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Bytes(OrderDto(), Settings(schema, layout));
      PopOrder read = Serializer.Deserialize<PopOrder>(bytes, Settings(schema, layout));
      Assert.AreEqual("Kerkstraat 1", read.ShipTo.Street);
      Assert.IsNull(read.Billing.Origin, "a new instance");
      Assert.IsNull(read.Billing.City, "the data has no City: the new instance has none");
      CollectionAssert.AreEqual(new[] { 1, 2 }, read.Codes);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void All_SettableMembersReadInto(bool schema, ObjectLayout layout)
    {
      PopOrder blank = new PopOrder();
      List<int> codes = blank.Codes;
      byte[] bytes = Bytes(OrderDto(), Settings(schema, layout));
      PopOrder read = Serializer.Deserialize<PopOrder>(bytes, Settings(schema, layout, PopulateMembers.All));
      Assert.AreEqual("Billstreet", read.Billing.Street);
      Assert.AreEqual("constructor", read.Billing.Origin, "the instance the constructor made");
      Assert.AreEqual("Initial", read.Billing.City, "the data has no City: kept");
      CollectionAssert.AreEqual(new[] { 1, 2 }, read.Codes, "cleared, then filled");
      CollectionAssert.AreEqual(new[] { 5, 6 }, read.Fixed, "an array is replaced");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void All_NullMemberGetsANewInstance_NilSetsNull(bool schema, ObjectLayout layout)
    {
      PopOrderDto dto = OrderDto();
      dto.Codes = null;
      byte[] bytes = Bytes(dto, Settings(schema, layout));
      PopOrder read = Serializer.Deserialize<PopOrder>(bytes, Settings(schema, layout, PopulateMembers.All));
      if (layout == ObjectLayout.Array)
        Assert.IsNull(read.Codes, "nil sets a settable member to null");
      else
        CollectionAssert.AreEqual(new[] { 99 }, read.Codes, "a map leaves a null value out (FilterDefaultValues): kept, as without populating");

      PopOrder target = new PopOrder() { Billing = null };
      Serializer.Populate(bytes, target, Settings(schema, layout, PopulateMembers.All));
      Assert.AreEqual("Billstreet", target.Billing.Street, "a member holding null gets a new instance");
      Assert.IsNull(target.Billing.Origin);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Attributes(bool schema, ObjectLayout layout)
    {
      PopSettingsDto dto = new PopSettingsDto()
      {
        Shared = new PopAddressDto() { Street = "s" },
        OptedIn = new PopAddressDto() { Street = "in" },
        OptedOut = new PopAddressDto() { Street = "out" },
        OptedOutList = new List<int>() { 1 },
        JsonPopulate = new PopAddressDto() { Street = "stj" },
        NewtonsoftReuse = new PopAddressDto() { Street = "json.net" },
        ReadonlyField = new PopAddressDto() { Street = "field" }
      };
      byte[] bytes = Bytes(dto, Settings(schema, layout));
      PopSettings read = Serializer.Deserialize<PopSettings>(bytes, Settings(schema, layout, PopulateMembers.GetOnly));
      Assert.IsNull(PopSettings.SharedInstance.Street, "a getter written by hand is left alone");
      Assert.AreEqual("in", read.OptedIn.Street);
      Assert.AreEqual("constructor", read.OptedIn.Origin);
      Assert.IsNull(read.OptedOut.Street, "skipped");
      CollectionAssert.AreEqual(new[] { 7 }, read.OptedOutList, "a get-only collection opted out is skipped");
      Assert.AreEqual("stj", read.JsonPopulate.Street);
      Assert.AreEqual("constructor", read.JsonPopulate.Origin, "System.Text.Json's attribute");
      Assert.AreEqual("json.net", read.NewtonsoftReuse.Street);
      Assert.AreEqual("constructor", read.NewtonsoftReuse.Origin, "Json.NET's attribute");
      Assert.AreEqual("field", read.ReadonlyField.Street);
      Assert.AreEqual("constructor", read.ReadonlyField.Origin, "a readonly field's object");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ClassAttribute_OptsOutItsMembers(bool schema, ObjectLayout layout)
    {
      PopOptedOutClass original = new PopOptedOutClass();
      original.Address.Street = "x";
      PopOptedOutClass read = Serializer.Deserialize<PopOptedOutClass>(Bytes(original, Settings(schema, layout)), Settings(schema, layout));
      Assert.IsNull(read.Address.Street);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void TypeIds_TheInstancesClassOrAnError(bool schema, ObjectLayout layout)
    {
      PopDrawingDto circle = new PopDrawingDto() { Shape = new PopCircle() { Name = "c", Radius = 2 } };
      PopDrawing read = Serializer.Deserialize<PopDrawing>(Bytes(circle, Settings(schema, layout)), Settings(schema, layout));
      Assert.AreEqual("c", read.Shape.Name);
      Assert.AreEqual(2, ((PopCircle)read.Shape).Radius);

      byte[] square = Bytes(new PopDrawingDto() { Shape = new PopSquare() { Name = "s", Side = 3 } }, Settings(schema, layout));
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<PopDrawing>(square, Settings(schema, layout)));
      StringAssert.Contains(ex.Message, "PopSquare");

      MsgPackSettings reporting = Settings(schema, layout);
      reporting.ReadErrors = ReadErrorHandling.ReportAndContinue;
      read = Serializer.Deserialize<PopDrawing>(square, reporting, out ReadDifferences differences);
      Assert.AreEqual("initial", read.Shape.Name, "skipped");
      Assert.IsNotNull(differences);
      Assert.AreEqual(1, differences.ErrorCount, differences.GenerateReport());
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ImmutableObject_Skipped(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Bytes(new PopPricedDto() { Price = new PopMoney(3m, "USD") }, Settings(schema, layout));
      PopPriced read = Serializer.Deserialize<PopPriced>(bytes, Settings(schema, layout));
      Assert.AreEqual("EUR", read.Price.Currency, "created by its constructor taking the values: nothing to read into");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Callbacks_OnThePopulatedInstance(bool schema, ObjectLayout layout)
    {
      byte[] bytes = Bytes(new PopHolderDto() { Inner = new PopCalled() { Value = "v" } }, Settings(schema, layout));
      PopHolder read = Serializer.Deserialize<PopHolder>(bytes, Settings(schema, layout));
      Assert.AreEqual("v", read.Inner.Value);
      Assert.AreEqual(1, read.Inner.Deserializing);
      Assert.AreEqual(1, read.Inner.Deserialized);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Nil_LeavesAGetOnlyObject(bool schema, ObjectLayout layout)
    {
      PopOrderDto dto = OrderDto();
      dto.ShipTo = null;
      PopOrder read = Serializer.Deserialize<PopOrder>(Bytes(dto, Settings(schema, layout)), Settings(schema, layout));
      Assert.AreEqual("constructor", read.ShipTo.Origin);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void References_ReadIntoAndReferredTo(bool schema, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(schema, layout);
      settings.ReferenceHandling = ReferenceHandling.Preserve;
      PopAddress shared = new PopAddress() { Street = "same" };
      PopReferenceFirstDto first = new PopReferenceFirstDto() { Owned = shared, Other = shared };
      PopReferenceFirst read = Serializer.Deserialize<PopReferenceFirst>(Bytes(first, settings), settings);
      Assert.AreEqual("same", read.Owned.Street);
      Assert.AreEqual("constructor", read.Owned.Origin);
      Assert.AreSame(read.Owned, read.Other, "the reference resolves to the populated instance");

      PopReferenceLastDto last = new PopReferenceLastDto() { First = shared, Owned = shared };
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<PopReferenceLast>(Bytes(last, settings), settings));
      StringAssert.Contains(ex.Message, "no setter");
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Root_ReadIntoTheTarget(bool schema, ObjectLayout layout)
    {
      PopOrder target = new PopOrder() { Number = "old" };
      PopAddress shipTo = target.ShipTo;
      PopAddress billing = target.Billing;
      Serializer.Populate(Bytes(OrderDto(), Settings(schema, layout)), target, Settings(schema, layout), out ReadDifferences differences);
      Assert.AreEqual("A-1", target.Number);
      Assert.AreSame(shipTo, target.ShipTo);
      Assert.AreEqual("Kerkstraat 1", target.ShipTo.Street);
      Assert.AreNotSame(billing, target.Billing, "GetOnly: a settable member gets a new instance");
      Assert.IsNull(differences);

      target = new PopOrder();
      billing = target.Billing;
      Serializer.Populate(Bytes(OrderDto(), Settings(schema, layout)), target, Settings(schema, layout, PopulateMembers.All));
      Assert.AreSame(billing, target.Billing);
      Assert.AreEqual("Initial", target.Billing.City);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Root_MembersTheDataLacksKeepTheirValues(bool schema, ObjectLayout layout)
    {
      PopAddress target = new PopAddress() { Street = "old", City = "Utrecht" };
      Serializer.Populate(Bytes(new PopAddressDto() { Street = "new" }, Settings(schema, layout)), target, Settings(schema, layout));
      Assert.AreEqual("new", target.Street);
      Assert.AreEqual("Utrecht", target.City);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Root_Collection_ClearedAndFilled(bool schema, ObjectLayout layout)
    {
      List<int> target = new List<int>() { 9 };
      Serializer.Populate(Bytes(new List<int>() { 1, 2 }, Settings(schema, layout)), target, Settings(schema, layout));
      CollectionAssert.AreEqual(new[] { 1, 2 }, target);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Root_Nil_LeavesTheTarget(bool schema, ObjectLayout layout)
    {
      PopAddress target = new PopAddress() { Street = "old" };
      Serializer.Populate(Bytes<PopAddress>(null, Settings(schema, layout)), target, Settings(schema, layout));
      Assert.AreEqual("old", target.Street);
    }

    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void Root_AnotherClass_Throws(bool schema, ObjectLayout layout)
    {
      byte[] square = Bytes<PopShape>(new PopSquare() { Name = "s" }, Settings(schema, layout));
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Populate<PopShape>(square, new PopCircle(), Settings(schema, layout)));
    }

    [TestMethod]
    public void Root_TargetsThatCannotBeReadInto()
    {
      byte[] bytes = Bytes(new PopAddressDto(), Settings(true, ObjectLayout.Array));
      Assert.ThrowsExactly<ArgumentNullException>(() => Serializer.Populate<PopAddress>(bytes, null, null));
      Assert.ThrowsExactly<ArgumentException>(() => Serializer.Populate(bytes, "text", null));
      Assert.ThrowsExactly<ArgumentException>(() => Serializer.Populate(bytes, new PopMoney(1m, "EUR"), null));
      Assert.ThrowsExactly<ArgumentException>(() => Serializer.Populate(bytes, new int[2], null));
      Assert.ThrowsExactly<ArgumentException>(() => Serializer.Populate<IList<int>>(bytes, new List<int>().AsReadOnly(), null));
    }

    /// <summary>
    /// Enough objects for LsMsgPack to compile its readers (CompileReadersAfter): populated members stay with the runtime's reader.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(AllSettings))]
    public void ManyObjects_SameAsTheFirst(bool schema, ObjectLayout layout)
    {
      List<PopOrderDto> dtos = Enumerable.Range(0, 1500).Select(i => OrderDto()).ToList();
      byte[] bytes = Bytes(dtos, Settings(schema, layout));
      for (int round = 0; round < 3; round++)
      {
        List<PopOrder> read = Serializer.Deserialize<List<PopOrder>>(bytes, Settings(schema, layout, PopulateMembers.All));
        PopOrder last = read[read.Count - 1];
        Assert.AreEqual("Kerkstraat 1", last.ShipTo.Street);
        Assert.AreEqual("constructor", last.Billing.Origin);
        Assert.AreEqual("Initial", last.Billing.City);
      }
    }
  }

  [TestClass]
  public class DebugPopulateTests : PopulateTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsPopulateTests : PopulateTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class GenPopulateTests : PopulateTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
