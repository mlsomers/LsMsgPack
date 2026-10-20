using LsMsgPack;
using LtMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// <see cref="MsgPackOptions.CompleteSchemas"/>: the schema of a root type has every class its values can hold at once, in a fixed order, so its id does not depend on the data.
  /// </summary>
  public abstract class CompleteSchemaTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public interface ICpShape { string Label { get; set; } }

    public class CpCircle : ICpShape
    {
      public string Label { get; set; }
      public double Radius { get; set; }
    }

    public class CpSquare : ICpShape
    {
      public string Label { get; set; }
      public double Side { get; set; }
    }

    public abstract class CpAnimal
    {
      public string Name { get; set; }
    }

    public class CpDog : CpAnimal
    {
      public bool Barks { get; set; }
    }

    public class CpPuppy : CpDog { }

    public class CpTag
    {
      public string Text { get; set; }
    }

    public class CpNote
    {
      public string Text { get; set; }
    }

    public struct CpPoint
    {
      public int X { get; set; }
      public int Y { get; set; }
    }

    public class CpDrawing
    {
      public int Id { get; set; }
      public List<ICpShape> Shapes { get; set; }
      public Dictionary<string, CpTag> Tags { get; set; }
      public CpAnimal Pet { get; set; }
      public CpPoint? Origin { get; set; }
      public object Anything { get; set; }
    }

    private static MsgPackSettings Writer(SchemaStore store)
    {
      return new MsgPackSettings() { SchemaStore = store, WriteSchemaReference = true, CompleteSchemas = true };
    }

    private static SchemaId ReferencedId(byte[] payload)
    {
      Assert.AreEqual(0xD8, payload[0], "fixext16");
      return new SchemaId(payload.Skip(2).Take(SchemaId.Length).ToArray());
    }

    private static string[] EntryNames(SchemaStore store, SchemaId id)
    {
      return SchemaBytes.Parse(store.GetSchema(id), null, out SchemaVersion _).Select(d => d.TypeName).ToArray();
    }

    private static CpDrawing Drawing(ICpShape shape)
    {
      return new CpDrawing() { Id = 7, Shapes = new List<ICpShape>() { shape }, Pet = new CpDog() { Name = "Rex" } };
    }

    [TestMethod]
    public void EveryClassAtOnceDepthFirst()
    {
      SchemaStore store = new SchemaStore();
      byte[] payload = Serializer.Serialize(Drawing(new CpCircle() { Label = "c", Radius = 2 }), Writer(store));

      // The root first, then its properties in order: the implementations of ICpShape (by full name), the dictionary values, the classes deriving from CpAnimal, the struct
      CollectionAssert.AreEqual(new[] { "CpDrawing", "CpCircle", "CpSquare", "CpTag", "CpDog", "CpPuppy", "CpPoint" }, EntryNames(store, ReferencedId(payload)));
      Assert.AreEqual(1, store.Count, "nothing grew");
    }

    [TestMethod]
    public void TheIdDoesNotDependOnTheData()
    {
      SchemaStore first = new SchemaStore();
      SchemaStore second = new SchemaStore();
      byte[] circle = Serializer.Serialize(Drawing(new CpCircle() { Label = "c" }), Writer(first));
      byte[] square = Serializer.Serialize(new CpDrawing() { Shapes = new List<ICpShape>() { new CpSquare(), new CpCircle() } }, Writer(second));

      Assert.AreEqual(ReferencedId(circle), ReferencedId(square));
      Assert.AreEqual(ReferencedId(circle), new SchemaStore().PrepareWriter(typeof(CpDrawing), Writer(null)), "known before anything is written");
    }

    [TestMethod]
    public void WithoutTheOptionTheValuesAddTheirClasses()
    {
      SchemaStore store = new SchemaStore();
      MsgPackSettings settings = Writer(store);
      settings.CompleteSchemas = false;
      byte[] payload = Serializer.Serialize(Drawing(new CpCircle()), settings);
      CollectionAssert.AreEqual(new[] { "CpDrawing", "CpCircle", "CpDog" }, EntryNames(store, ReferencedId(payload)));
    }

    [TestMethod]
    public void RoundTripAndGrowth()
    {
      SchemaStore store = new SchemaStore();
      CpDrawing drawing = Drawing(new CpSquare() { Label = "s", Side = 3 });
      drawing.Tags = new Dictionary<string, CpTag>() { { "a", new CpTag() { Text = "A" } } };
      drawing.Origin = new CpPoint() { X = 1, Y = 2 };
      byte[] complete = Serializer.Serialize(drawing, Writer(store));

      drawing.Anything = new CpNote() { Text = "object" }; // declared as object: not in the complete schema, it grows
      byte[] grown = Serializer.Serialize(drawing, Writer(store));
      Assert.AreNotEqual(ReferencedId(complete), ReferencedId(grown));
      Assert.AreEqual(2, store.Count);

      MsgPackSettings reader = new MsgPackSettings() { SchemaStore = store };
      CpDrawing read = Serializer.Deserialize<CpDrawing>(complete, reader);
      Assert.AreEqual(3d, ((CpSquare)read.Shapes[0]).Side);
      Assert.AreEqual("A", read.Tags["a"].Text);
      Assert.AreEqual(2, read.Origin.Value.Y);
      Assert.IsInstanceOfType(read.Pet, typeof(CpDog));
      Assert.AreEqual("object", ((CpNote)Serializer.Deserialize<CpDrawing>(grown, reader).Anything).Text);
    }

    [TestMethod]
    public void DescribedSchemaIsCompleteToo()
    {
      SchemaStore store = new SchemaStore();
      MsgPackSettings settings = Writer(store);
      settings.SchemaVersion = SchemaVersion.Described;
      byte[] payload = Serializer.Serialize(Drawing(new CpCircle()), settings); // and the root entry "" (left out by Parse)
      CollectionAssert.AreEqual(new[] { "CpDrawing", "CpCircle", "CpSquare", "CpTag", "CpDog", "CpPuppy", "CpPoint" }, EntryNames(store, ReferencedId(payload)));
    }
  }

  [TestClass]
  public class LsCompleteSchemaTests : CompleteSchemaTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtCompleteSchemaTests : CompleteSchemaTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }

    [TestMethod]
    public void PrepareSchemaGivesTheIdOfTheFirstValue()
    {
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions() { SchemaStore = new SchemaStore(), WriteSchemaReference = true, CompleteSchemas = true });
      SchemaId prepared = serializer.PrepareSchema(typeof(CpDrawing));
      byte[] payload = serializer.Serialize(new CpDrawing());
      Assert.AreEqual(prepared, new SchemaId(payload.Skip(2).Take(SchemaId.Length).ToArray()));
      Assert.ThrowsExactly<InvalidOperationException>(() => new LtMsgPackSerializer(new LtMsgPackOptions()).PrepareSchema(typeof(CpDrawing)));
    }

    [TestMethod]
    public void SameSchemaAsLsMsgPack()
    {
      foreach (SchemaVersion version in new[] { SchemaVersion.Names, SchemaVersion.Described })
      {
        SchemaStore ls = new SchemaStore();
        SchemaStore lt = new SchemaStore();
        MsgPackSettings lsSettings = new MsgPackSettings() { SchemaStore = ls, WriteSchemaReference = true, CompleteSchemas = true, SchemaVersion = version };
        MsgPackSettings ltSettings = new MsgPackSettings() { SchemaStore = lt, WriteSchemaReference = true, CompleteSchemas = true, SchemaVersion = version };
        CpDrawing drawing = new CpDrawing() { Id = 1, Shapes = new List<ICpShape>() { new CpCircle() { Radius = 1 } } };
        byte[] lsBytes = Serializers.Ls.Serialize(drawing, lsSettings);
        byte[] ltBytes = Serializers.Lt.Serialize(drawing, ltSettings);
        CollectionAssert.AreEqual(lsBytes, ltBytes, version.ToString());
        CollectionAssert.AreEqual(ls.GetSchema(ls.GetSchemaIds()[0]), lt.GetSchema(lt.GetSchemaIds()[0]), version.ToString());
      }
    }
  }

  [TestClass]
  public class GenCompleteSchemaTests : CompleteSchemaTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
