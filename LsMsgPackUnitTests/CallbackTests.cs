using LsMsgPack;
using LsMsgPack.Streaming;
using LsMsgPackDebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Serialization callbacks (docs/Callbacks.md): IMsgPackOnSerializing, IMsgPackOnSerialized, IMsgPackOnDeserializing, IMsgPackOnDeserialized and System.Text.Json's IJsonOn* interfaces.
  /// </summary>
  public abstract class CallbackTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    #region Model

    /// <summary>
    /// The callbacks in the order they were called (per thread).
    /// </summary>
    public static class CbLog
    {
      [ThreadStatic]
      private static List<string> _entries;

      public static List<string> Entries { get { return _entries ?? (_entries = new List<string>()); } }

      public static void Add(string entry) { Entries.Add(entry); }
    }

    /// <summary>
    /// How often each callback was called on this object (private fields: not serialized).
    /// </summary>
    public abstract class CbCounted : IMsgPackOnSerializing, IMsgPackOnSerialized, IMsgPackOnDeserializing, IMsgPackOnDeserialized
    {
      private int _serializing;
      private int _serialized;
      private int _deserializing;
      private int _deserialized;

      public string Counts() { return _serializing + "," + _serialized + "," + _deserializing + "," + _deserialized; }

      protected abstract string Label { get; }

      public virtual void OnSerializing() { _serializing++; CbLog.Add(Label + " serializing"); }
      public virtual void OnSerialized() { _serialized++; CbLog.Add(Label + " serialized"); }
      public virtual void OnDeserializing() { _deserializing++; CbLog.Add(Label + " deserializing"); }
      public virtual void OnDeserialized() { _deserialized++; CbLog.Add(Label + " deserialized"); }
    }

    public class CbAddress : CbCounted
    {
      public string Street { get; set; }
      protected override string Label { get { return "Address " + (Street ?? "-"); } }
    }

    public class CbOrder : CbCounted
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      public CbAddress ShipTo { get; set; }
      public List<CbAddress> Stops { get; set; }

      /// <summary>Set by OnSerializing (written), checked by OnDeserialized.</summary>
      public string Stamp { get; set; }

      private string _seenWhenDeserializing;
      private string _seenWhenDeserialized;

      public string SeenWhenDeserializing() { return _seenWhenDeserializing; }
      public string SeenWhenDeserialized() { return _seenWhenDeserialized; }

      protected override string Label { get { return "Order"; } }

      public override void OnSerializing()
      {
        Stamp = "stamped " + Id;
        base.OnSerializing();
      }

      public override void OnDeserializing()
      {
        _seenWhenDeserializing = Id + "/" + (Customer ?? "null");
        base.OnDeserializing();
      }

      public override void OnDeserialized()
      {
        _seenWhenDeserialized = Id + "/" + Customer + "/" + Stamp + "/" + (ShipTo?.Street ?? "null") + "/" + (Stops?.Count ?? -1);
        base.OnDeserialized();
      }
    }

    public struct CbPoint : IMsgPackOnSerializing, IMsgPackOnDeserialized
    {
      public int X { get; set; }

      /// <summary>Set by OnSerializing on the box that is written.</summary>
      public int Twice { get; set; }

      public void OnSerializing() { Twice = X * 2; }
      public void OnDeserialized() { X += 1000; }
    }

    public class CbPlot
    {
      public CbPoint At { get; set; }
      public CbPoint? Maybe { get; set; }
      public List<CbPoint> Points { get; set; }
    }

    /// <summary>Created by the constructor that takes its values.</summary>
    public class CbMoney : IMsgPackOnDeserializing, IMsgPackOnDeserialized
    {
      public CbMoney(decimal amount, string currency)
      {
        Amount = amount;
        Currency = currency;
      }

      public decimal Amount { get; }
      public string Currency { get; }
      public string Note { get; set; }

      private string _seen;
      public string Seen() { return _seen; }

      public void OnDeserializing() { _seen = "ctor " + Amount.ToString(CultureInfo.InvariantCulture) + " " + Currency + " note " + (Note ?? "null"); }
      public void OnDeserialized() { _seen += "; done note " + Note; }
    }

    public class CbJson : IJsonOnSerializing, IJsonOnSerialized, IJsonOnDeserializing, IJsonOnDeserialized
    {
      public string Name { get; set; }

      private string _calls = "";
      public string Calls() { return _calls; }

      void IJsonOnSerializing.OnSerializing() { _calls += "serializing;"; Name = Name + "!"; }
      void IJsonOnSerialized.OnSerialized() { _calls += "serialized;"; }
      void IJsonOnDeserializing.OnDeserializing() { _calls += "deserializing;"; }
      void IJsonOnDeserialized.OnDeserialized() { _calls += "deserialized " + Name + ";"; }
    }

    /// <summary>Both interfaces for the same callback: only the own one is called.</summary>
    public class CbBoth : IMsgPackOnDeserialized, IJsonOnDeserialized
    {
      public string Name { get; set; }

      private string _calls = "";
      public string Calls() { return _calls; }

      public void OnDeserialized() { _calls += "own;"; }
      void IJsonOnDeserialized.OnDeserialized() { _calls += "json;"; }
    }

    public class CbValidated : IMsgPackOnDeserialized
    {
      public int Quantity { get; set; }

      public void OnDeserialized()
      {
        if (Quantity < 0)
          throw new ArgumentOutOfRangeException(nameof(Quantity), Quantity, "The quantity can't be negative.");
      }
    }

    /// <summary>Validation by the attributes of System.ComponentModel.DataAnnotations (the recommendation for required members and ranges).</summary>
    public class CbAnnotated : IMsgPackOnDeserialized
    {
      [Required]
      public string Name { get; set; }

      [Range(1, 10)]
      public int Rating { get; set; }

      public void OnDeserialized()
      {
        Validator.ValidateObject(this, new ValidationContext(this), validateAllProperties: true);
      }
    }

    public class CbValidatedList
    {
      public List<CbValidated> Items { get; set; }
      public int Other { get; set; }
    }

    public abstract class CbShape : CbCounted
    {
      public string Color { get; set; }
    }

    public class CbSquare : CbShape
    {
      public double Side { get; set; }
      protected override string Label { get { return "Square " + (Color ?? "-"); } }
    }

    public class CbCircle : CbShape
    {
      public double Radius { get; set; }
      protected override string Label { get { return "Circle " + (Color ?? "-"); } }
    }

    public class CbDrawing
    {
      public CbShape Main { get; set; }
      public object Any { get; set; }
      public List<CbShape> Shapes { get; set; }
      public CbAddress Plain { get; set; }
    }

    public class CbNode : CbCounted
    {
      public string Name { get; set; }
      public CbNode Next { get; set; }
      protected override string Label { get { return "Node " + (Name ?? "-"); } }
    }

    public class CbNewer
    {
      public int Id { get; set; }
      public string Note { get; set; }
    }

    public class CbOlder : IMsgPackOnDeserialized
    {
      public int Id { get; set; }
      public MsgPackExtensionData Extra { get; set; }

      private bool _keptWhenDeserialized;
      public bool KeptWhenDeserialized() { return _keptWhenDeserialized; }

      public void OnDeserialized() { _keptWhenDeserialized = Extra != null && Extra.TryGetRaw("Note", out byte[] _); }
    }

    /// <summary>A collection with the interfaces: never called (objects only, as System.Text.Json).</summary>
    public class CbList : List<int>, IMsgPackOnSerializing, IMsgPackOnDeserialized
    {
      public void OnSerializing() { CbLog.Add("list serializing"); }
      public void OnDeserialized() { CbLog.Add("list deserialized"); }
    }

    #endregion

    public static IEnumerable<object[]> Modes()
    {
      foreach (string mode in new[] { "names", "none", "v1", "v2", "v2-reference" })
        foreach (ObjectLayout layout in new[] { ObjectLayout.Map, ObjectLayout.Array })
          yield return new object[] { mode, layout };
    }

    public static MsgPackSettings Settings(string mode, ObjectLayout layout)
    {
      return ExtensionDataTests.Settings(mode, layout);
    }

    protected static CbOrder NewOrder()
    {
      return new CbOrder
      {
        Id = 7,
        Customer = "Ann",
        ShipTo = new CbAddress { Street = "Main" },
        Stops = new List<CbAddress> { new CbAddress { Street = "S1" }, new CbAddress { Street = "S2" } }
      };
    }

    /// <summary>
    /// The parent's first callback comes before, its second after those of the objects below it, each of those has its pair together, every object once.
    /// (The order of the siblings is the order in which a serializer writes or sets the properties: LsMsgPackDebug sets them last to first.)
    /// </summary>
    private static void AssertNested(List<string> log, string first, string last, params string[] children)
    {
      Assert.HasCount(2 + 2 * children.Length, log, string.Join(" | ", log));
      Assert.AreEqual("Order " + first, log[0]);
      Assert.AreEqual("Order " + last, log[log.Count - 1]);
      List<string> pairs = new List<string>();
      for (int t = 1; t < log.Count - 1; t += 2)
      {
        string child = log[t].Substring(0, log[t].Length - first.Length - 1);
        Assert.AreEqual(child + " " + first, log[t]);
        Assert.AreEqual(child + " " + last, log[t + 1], string.Join(" | ", log));
        pairs.Add(child);
      }
      CollectionAssert.AreEquivalent(children, pairs);
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void Callbacks_CalledOncePerObject_ParentAroundChildren(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      CbOrder order = NewOrder();
      CbLog.Entries.Clear();
      byte[] data = Serializer.Serialize(order, settings);
      AssertNested(CbLog.Entries, "serializing", "serialized", "Address Main", "Address S1", "Address S2");
      Assert.AreEqual("1,1,0,0", order.Counts());
      Assert.AreEqual("1,1,0,0", order.Stops[1].Counts());
      Assert.AreEqual("stamped 7", order.Stamp);

      CbLog.Entries.Clear();
      CbOrder read = Serializer.Deserialize<CbOrder>(data, settings);
      // Created before its members are set: the labels of the addresses are "-" when deserializing, their streets when deserialized
      List<string> log = CbLog.Entries.Select(e => e.Replace("Address - deserializing", "Address deserializing")).ToList();
      Assert.AreEqual("Order deserializing", log[0]);
      Assert.AreEqual("Order deserialized", log[log.Count - 1]);
      Assert.HasCount(8, log, string.Join(" | ", log));
      for (int t = 1; t < 7; t += 2)
      {
        Assert.AreEqual("Address deserializing", log[t]);
        Assert.StartsWith("Address ", log[t + 1]);
        Assert.EndsWith(" deserialized", log[t + 1]);
      }
      Assert.AreEqual("0,0,1,1", read.Counts());
      Assert.AreEqual("0,0,1,1", read.ShipTo.Counts());
      Assert.AreEqual("0,0,1,1", read.Stops[0].Counts());
      Assert.AreEqual("0/null", read.SeenWhenDeserializing(), "before the members are set");
      Assert.AreEqual("7/Ann/stamped 7/Main/2", read.SeenWhenDeserialized(), "after all members, the objects below it done");
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void Callbacks_ReportingDifferences_CalledOnce(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      byte[] data = Serializer.Serialize(NewOrder(), settings);
      CbOrder read = Serializer.Deserialize<CbOrder>(data, settings, out ReadDifferences differences);
      Assert.IsTrue(differences is null || differences.Differences.Count == 0);
      Assert.AreEqual("0,0,1,1", read.Counts());
      Assert.AreEqual("0,0,1,1", read.ShipTo.Counts());
      Assert.AreEqual("0,0,1,1", read.Stops[1].Counts());
      Assert.AreEqual("7/Ann/stamped 7/Main/2", read.SeenWhenDeserialized());
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void Struct_CallbacksChangeTheBox(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      CbPoint point = new CbPoint { X = 1 };
      CbPoint read = Serializer.Deserialize<CbPoint>(Serializer.Serialize(point, settings), settings);
      Assert.AreEqual(2, read.Twice, "OnSerializing changed the box that was written");
      Assert.AreEqual(1001, read.X, "OnDeserialized changed the box that was read");
      Assert.AreEqual(0, point.Twice, "the caller's copy is not changed");

      CbPlot plot = new CbPlot { At = new CbPoint { X = 2 }, Maybe = new CbPoint { X = 3 }, Points = new List<CbPoint> { new CbPoint { X = 4 }, new CbPoint { X = 5 } } };
      CbPlot back = Serializer.Deserialize<CbPlot>(Serializer.Serialize(plot, settings), settings);
      Assert.AreEqual(1002, back.At.X);
      Assert.AreEqual(4, back.At.Twice);
      Assert.AreEqual(1003, back.Maybe.Value.X);
      Assert.AreEqual(6, back.Maybe.Value.Twice);
      Assert.AreEqual(1004, back.Points[0].X);
      Assert.AreEqual(10, back.Points[1].Twice);
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void ConstructorWithValues_DeserializingAfterTheConstructor(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      CbMoney read = Serializer.Deserialize<CbMoney>(Serializer.Serialize(new CbMoney(2.5m, "EUR") { Note = "tip" }, settings), settings);
      Assert.AreEqual("ctor 2.5 EUR note null; done note tip", read.Seen());
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void SystemTextJsonInterfaces_Called(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      CbJson json = new CbJson { Name = "a" };
      byte[] data = Serializer.Serialize(json, settings);
      Assert.AreEqual("serializing;serialized;", json.Calls());
      CbJson read = Serializer.Deserialize<CbJson>(data, settings);
      Assert.AreEqual("deserializing;deserialized a!;", read.Calls());

      CbBoth both = Serializer.Deserialize<CbBoth>(Serializer.Serialize(new CbBoth { Name = "b" }, settings), settings);
      Assert.AreEqual("own;", both.Calls(), "the own interface wins over System.Text.Json's");
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void Validation_ExceptionPassedOnWhateverReadErrorsSays(string mode, ObjectLayout layout)
    {
      foreach (ReadErrorHandling handling in new[] { ReadErrorHandling.FailFast, ReadErrorHandling.FailDeferred, ReadErrorHandling.ReportAndContinue })
      {
        MsgPackSettings settings = Settings(mode, layout);
        settings.ReadErrors = handling;
        byte[] bad = Serializer.Serialize(new CbValidatedList { Items = new List<CbValidated> { new CbValidated { Quantity = 1 }, new CbValidated { Quantity = -1 } }, Other = 3 }, settings);
        ArgumentOutOfRangeException ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Serializer.Deserialize<CbValidatedList>(bad, settings), handling.ToString());
        Assert.AreEqual(-1, ex.ActualValue);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Serializer.Deserialize<CbValidatedList>(bad, settings, out ReadDifferences _), handling + ", reporting");

        byte[] good = Serializer.Serialize(new CbValidatedList { Items = new List<CbValidated> { new CbValidated { Quantity = 1 } } }, settings);
        Assert.AreEqual(1, Serializer.Deserialize<CbValidatedList>(good, settings).Items[0].Quantity);
      }
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void DataAnnotations_ValidatedInOnDeserialized(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      Assert.AreEqual(3, Serializer.Deserialize<CbAnnotated>(Serializer.Serialize(new CbAnnotated { Name = "n", Rating = 3 }, settings), settings).Rating);
      ValidationException range = Assert.ThrowsExactly<ValidationException>(() => Serializer.Deserialize<CbAnnotated>(Serializer.Serialize(new CbAnnotated { Name = "n", Rating = 11 }, settings), settings));
      Assert.Contains("Rating", range.Message);
      ValidationException required = Assert.ThrowsExactly<ValidationException>(() => Serializer.Deserialize<CbAnnotated>(Serializer.Serialize(new CbAnnotated { Rating = 2 }, settings), settings));
      Assert.Contains("Name", required.Message);
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void Polymorphic_TheDerivedClassesCallbacks(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      CbDrawing drawing = new CbDrawing
      {
        Main = new CbSquare { Color = "red", Side = 2 },
        Any = new CbCircle { Color = "blue", Radius = 1 },
        Shapes = new List<CbShape> { new CbCircle { Color = "green" }, new CbSquare { Color = "black" } },
        Plain = new CbAddress { Street = "x" }
      };
      byte[] data = Serializer.Serialize(drawing, settings);
      Assert.AreEqual("1,1,0,0", drawing.Main.Counts());
      Assert.AreEqual("1,1,0,0", ((CbCircle)drawing.Any).Counts());
      Assert.AreEqual("1,1,0,0", drawing.Shapes[0].Counts());
      Assert.AreEqual("1,1,0,0", drawing.Shapes[1].Counts());
      Assert.AreEqual("1,1,0,0", drawing.Plain.Counts());

      CbDrawing read = Serializer.Deserialize<CbDrawing>(data, settings);
      Assert.IsInstanceOfType<CbSquare>(read.Main);
      Assert.AreEqual("0,0,1,1", read.Main.Counts());
      Assert.AreEqual("0,0,1,1", ((CbCircle)read.Any).Counts());
      Assert.AreEqual("0,0,1,1", read.Shapes[0].Counts());
      Assert.AreEqual("0,0,1,1", read.Shapes[1].Counts());
      Assert.AreEqual("0,0,1,1", read.Plain.Counts());
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void AlwaysTypeIds_CalledOnce(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      settings.AddTypeIdOptions = AddTypeIdOption.Always;
      CbOrder order = NewOrder();
      CbOrder read = Serializer.Deserialize<CbOrder>(Serializer.Serialize(order, settings), settings);
      Assert.AreEqual("1,1,0,0", order.Counts());
      Assert.AreEqual("1,1,0,0", order.Stops[0].Counts());
      Assert.AreEqual("0,0,1,1", read.Counts());
      Assert.AreEqual("0,0,1,1", read.Stops[0].Counts());
      Assert.AreEqual("7/Ann/stamped 7/Main/2", read.SeenWhenDeserialized());
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void IgnoreCycles_AnObjectWrittenAsNilGetsNoCallbacks(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      settings.ReferenceHandling = ReferenceHandling.IgnoreCycles;
      CbNode a = new CbNode { Name = "a" };
      CbNode b = new CbNode { Name = "b", Next = a };
      a.Next = b;
      CbNode read = Serializer.Deserialize<CbNode>(Serializer.Serialize(a, settings), settings);
      Assert.AreEqual("1,1,0,0", a.Counts());
      Assert.AreEqual("1,1,0,0", b.Counts());
      Assert.AreEqual("b", read.Next.Name);
      Assert.IsNull(read.Next.Next);
      Assert.AreEqual("0,0,1,1", read.Counts());
      Assert.AreEqual("0,0,1,1", read.Next.Counts());
    }

    /// <summary>
    /// ReferenceHandling.Preserve: an object written again as a reference gets no callbacks there, so each object is called once (a cycle, and an address shared by two members);
    /// read back, a reference gives the object already made, which is not called again.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void Preserve_EachObjectCalledOnce(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      settings.ReferenceHandling = ReferenceHandling.Preserve;
      CbNode a = new CbNode { Name = "a" };
      CbNode b = new CbNode { Name = "b", Next = a };
      a.Next = b;
      CbNode read = Serializer.Deserialize<CbNode>(Serializer.Serialize(a, settings), settings);
      Assert.AreEqual("1,1,0,0", a.Counts());
      Assert.AreEqual("1,1,0,0", b.Counts());
      Assert.AreSame(read, read.Next.Next);
      Assert.AreEqual("0,0,1,1", read.Counts());
      Assert.AreEqual("0,0,1,1", read.Next.Counts());

      CbOrder order = NewOrder();
      order.Stops[1] = order.ShipTo;
      CbOrder readOrder = Serializer.Deserialize<CbOrder>(Serializer.Serialize(order, settings), settings);
      Assert.AreEqual("1,1,0,0", order.ShipTo.Counts());
      Assert.AreSame(readOrder.ShipTo, readOrder.Stops[1]);
      Assert.AreEqual("0,0,1,1", readOrder.ShipTo.Counts());
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void ExtensionData_KeptBeforeOnDeserialized(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      CbOlder read = Serializer.Deserialize<CbOlder>(Serializer.Serialize(new CbNewer { Id = 3, Note = "n" }, settings), settings);
      Assert.AreEqual(3, read.Id);
      if (!settings.UseInexedSchema && layout == ObjectLayout.Array)
        Assert.IsTrue(read.Extra.TryGetRaw(1, out byte[] _), "without the schema by position (OnDeserialized looks for the name)");
      else
        Assert.IsTrue(read.KeptWhenDeserialized());
    }

    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void Collections_NotCalled(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      CbLog.Entries.Clear();
      CbList read = Serializer.Deserialize<CbList>(Serializer.Serialize(new CbList { 1, 2 }, settings), settings);
      CollectionAssert.AreEqual(new[] { 1, 2 }, read);
      Assert.IsEmpty(CbLog.Entries, string.Join(" | ", CbLog.Entries));
    }

    /// <summary>
    /// A cached schema grows when a later call meets a type it does not have yet (a frozen session: the work is repeated with a copy, see LsMsgPackCore/CLAUDE.md).
    /// </summary>
    [TestMethod]
    [DataRow(ObjectLayout.Map)]
    [DataRow(ObjectLayout.Array)]
    public void SchemaGrowth_CallsOfTheRepeatedWork(ObjectLayout layout)
    {
      MsgPackSettings settings = Settings("v2-reference", layout);
      for (int round = 0; round < 3; round++)
      {
        CbDrawing drawing = new CbDrawing { Main = new CbSquare { Color = "red" } };
        if (round > 0)
          drawing.Shapes = new List<CbShape> { new CbCircle { Color = "c" + round } }; // round 1: a new type for the session
        if (round > 1)
          drawing.Any = new CbNode { Name = "n" }; // round 2: another one
        byte[] data = Serializer.Serialize(drawing, settings);
        CbDrawing read = Serializer.Deserialize<CbDrawing>(data, settings);
        Assert.AreEqual("1,1,0,0", drawing.Main.Counts(), "round " + round);
        Assert.AreEqual("0,0,1,1", read.Main.Counts(), "round " + round);
        if (round > 0)
        {
          Assert.AreEqual("1,1,0,0", drawing.Shapes[0].Counts(), "round " + round);
          Assert.AreEqual("0,0,1,1", read.Shapes[0].Counts(), "round " + round);
        }
      }
    }

    [TestMethod]
    public void AnonymousRoot_ObjectsBelowItByNames_Called()
    {
      CbOrder order = NewOrder();
      Serializer.Serialize(new { Order = order, Any = (object)new CbAddress { Street = "y" } }, new MsgPackSettings());
      Assert.AreEqual("1,1,0,0", order.Counts());
      Assert.AreEqual("1,1,0,0", order.ShipTo.Counts());
      Assert.AreEqual("stamped 7", order.Stamp);
    }
  }

  [TestClass]
  public class DebugCallbackTests : CallbackTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Debug; } }
  }

  [TestClass]
  public class LsCallbackTests : CallbackTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }

    /// <summary>
    /// The compiled writers and readers (from the first object) run inside the callbacks.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public void CompiledReadersAndWriters_Called(string mode, ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(mode, layout);
      LsMsgPackOptions options = new LsMsgPackOptions
      {
        ObjectLayout = layout, UseInexedSchema = settings.UseInexedSchema, SchemaVersion = settings.SchemaVersion, SchemaStore = settings.SchemaStore, WriteSchemaReference = settings.WriteSchemaReference,
        UseRegisteredContexts = false, CompileWritersAfter = 0, CompileReadersAfter = 0
      };
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(options);
      for (int t = 0; t < 3; t++)
      {
        CbOrder order = NewOrder();
        CbOrder read = serializer.Deserialize<CbOrder>(serializer.Serialize(order));
        Assert.AreEqual("1,1,0,0", order.Counts());
        Assert.AreEqual("1,1,0,0", order.Stops[1].Counts());
        Assert.AreEqual("0,0,1,1", read.Counts());
        Assert.AreEqual("0,0,1,1", read.Stops[1].Counts());
        Assert.AreEqual("7/Ann/stamped 7/Main/2", read.SeenWhenDeserialized());
        CbPoint point = serializer.Deserialize<CbPoint>(serializer.Serialize(new CbPoint { X = 1 }));
        Assert.AreEqual(1001, point.X);
        Assert.AreEqual(2, point.Twice);
      }
    }

    /// <summary>
    /// The body of a stream's head is written again with a grown copy of the frozen head session when it needs cache entries (Serializer.SerializeBody): callbacks once per object.
    /// </summary>
    [TestMethod]
    public void StreamHead_RepeatedBody_CalledOnce()
    {
      LsMsgPackSerializer serializer = new LsMsgPackSerializer(new LsMsgPackOptions { UseRegisteredContexts = false });
      List<CbDrawing> drawings = new List<CbDrawing>
      {
        new CbDrawing { Main = new CbSquare { Color = "a" }, Plain = new CbAddress { Street = "s" } },
        new CbDrawing { Main = new CbCircle { Color = "b" }, Shapes = new List<CbShape> { new CbSquare { Color = "c" }, new CbCircle { Color = "d" } } },
      };
      System.IO.MemoryStream ms = new System.IO.MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, serializer, MsgPackStreamSchemas.PerObject, true))
      {
        writer.WriteHead<List<CbDrawing>>();
        writer.WriteArrayHeader(drawings.Count);
        for (int t = 0; t < drawings.Count; t++)
          writer.WriteObject(drawings[t]);
      }
      Assert.AreEqual("1,1,0,0", drawings[0].Main.Counts());
      Assert.AreEqual("1,1,0,0", drawings[0].Plain.Counts());
      Assert.AreEqual("1,1,0,0", drawings[1].Main.Counts());
      Assert.AreEqual("1,1,0,0", drawings[1].Shapes[0].Counts());
      Assert.AreEqual("1,1,0,0", drawings[1].Shapes[1].Counts());

      using (MsgPackStreamReader reader = new MsgPackStreamReader(new System.IO.MemoryStream(ms.ToArray()), serializer))
      {
        reader.ReadHead<List<CbDrawing>>();
        reader.Read();
        CbDrawing first = reader.ReadObject<CbDrawing>();
        CbDrawing second = reader.ReadObject<CbDrawing>();
        Assert.AreEqual("0,0,1,1", first.Main.Counts());
        Assert.AreEqual("0,0,1,1", first.Plain.Counts());
        Assert.AreEqual("0,0,1,1", second.Shapes[1].Counts());
        Assert.IsInstanceOfType<CbCircle>(second.Shapes[1]);
      }
    }
  }

  [TestClass]
  public class GenCallbackTests : CallbackTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
