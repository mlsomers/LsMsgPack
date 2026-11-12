using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using LsMsgPack.TypeResolving.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Fields and the schema: complete schemas, reading into other classes, the cached schema, the differences and what the data must not do.
  /// </summary>
  public abstract class SerializingFieldsSchema : FldTestBase
  {
    public SerializingFieldsSchema()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(SerializingFieldsSchema));
    }

    // Case 19: complete schemas

    public interface IFldShape { string Label { get; set; } }

    public class FldCircle : IFldShape
    {
      public string Label { get; set; }
      public double Radius;
    }

    public class FldSquare : IFldShape
    {
      public string Label { get; set; }
      public double Side;
    }

    public class FldCompleteTag
    {
      public string Text;
    }

    public class FldCompleteRoot
    {
      public int Id;
      public IFldShape Shape; // only the field refers to the implementations
      public FldCompleteTag Tag;
    }

    private static MsgPackSettings Writer(SchemaStore store, SerializedFields fields)
    {
      MsgPackSettings settings = Settings(true, ObjectLayout.Array, fields);
      settings.SchemaStore = store;
      settings.WriteSchemaReference = true;
      settings.CompleteSchemas = true;
      return settings;
    }

    private static SchemaId ReferencedId(byte[] payload)
    {
      Assert.AreEqual(0xD8, payload[0], "fixext16");
      return new SchemaId(payload.Skip(2).Take(SchemaId.Length).ToArray());
    }

    private static List<ComplexTypeDef> Entries(SchemaStore store, SchemaId id)
    {
      return SchemaBytes.Parse(store.GetSchema(id), null, out SchemaVersion _);
    }

    [TestMethod]
    public void CompleteSchema_HasTheClassesReachedByFields()
    {
      SchemaStore store = new SchemaStore();
      byte[] payload = Serializer.Serialize(new FldCompleteRoot() { Id = 1, Shape = new FldCircle() { Label = "c", Radius = 2 }, Tag = new FldCompleteTag() { Text = "t" } }, Writer(store, SerializedFields.Public));
      List<ComplexTypeDef> entries = Entries(store, ReferencedId(payload));
      CollectionAssert.AreEqual(new[] { "FldCompleteRoot", "FldCircle", "FldSquare", "FldCompleteTag" }, entries.Select(d => d.TypeName).ToArray());
      Assert.AreEqual(1, store.Count, "nothing grew");
    }

    [TestMethod]
    public void CompleteSchema_HasTheNamesOfTheFields()
    {
      SchemaStore store = new SchemaStore();
      byte[] payload = Serializer.Serialize(new FldCompleteRoot() { Id = 1, Shape = new FldSquare() { Label = "c", Side = 2 }, Tag = new FldCompleteTag() { Text = "t" } }, Writer(store, SerializedFields.Public));
      List<ComplexTypeDef> entries = Entries(store, ReferencedId(payload));
      CollectionAssert.AreEqual(new[] { "Id", "Shape", "Tag" }, entries[0].Props.ToArray());
      CollectionAssert.AreEqual(new[] { "Label", "Radius" }, entries[1].Props.ToArray());
      CollectionAssert.AreEqual(new[] { "Text" }, entries[3].Props.ToArray());
    }

    [TestMethod]
    public void CompleteSchema_AttributedModeDoesNotReachUnmarkedFields()
    {
      SchemaStore store = new SchemaStore();
      byte[] payload = Serializer.Serialize(new FldCompleteRoot() { Id = 1 }, Writer(store, SerializedFields.Attributed));
      CollectionAssert.AreEqual(new string[0], Entries(store, ReferencedId(payload)).Select(d => d.TypeName).ToArray(),
        "no serialized members: the root gets no entry (a class without properties never does), and the classes only its fields reach are not followed");
    }

    [TestMethod]
    public void CompleteSchema_RoundTrip()
    {
      SchemaStore store = new SchemaStore();
      MsgPackSettings settings = Writer(store, SerializedFields.Public);
      FldCompleteRoot read = Serializer.Deserialize<FldCompleteRoot>(Serializer.Serialize(new FldCompleteRoot() { Id = 1, Shape = new FldSquare() { Label = "s", Side = 3 }, Tag = new FldCompleteTag() { Text = "t" } }, settings), settings);
      Assert.AreEqual(3d, ((FldSquare)read.Shape).Side);
      Assert.AreEqual("t", read.Tag.Text);
    }

    // Case 19: BindReaderTypes follows the type of a field

    public class FldA
    {
      public FldB Child;
      public int Value;
    }

    public class FldB
    {
      public string Text;
      public int Number;
      public List<FldC> Items;
    }

    public class FldC
    {
      public int Id;
    }

    public class FldReaderA
    {
      public int Value;
      public FldReaderB Child;
    }

    public class FldReaderB
    {
      public int Number;
      public string Text;
      public List<FldReaderC> Items;
    }

    public class FldReaderC
    {
      public int Id;
    }

    [TestMethod]
    [DataRow(ObjectLayout.Array)]
    [DataRow(ObjectLayout.Map)]
    public void BindReaderTypes_FollowsTheTypeOfAField(ObjectLayout layout)
    {
      MsgPackSettings settings = Settings(true, layout, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldA() { Value = 1, Child = new FldB() { Text = "t", Number = 2, Items = new List<FldC> { new FldC() { Id = 3 } } } }, settings);
      FldReaderA read = Serializer.Deserialize<FldReaderA>(bytes, settings);
      Assert.AreEqual(1, read.Value);
      Assert.AreEqual("t", read.Child.Text);
      Assert.AreEqual(2, read.Child.Number);
      Assert.AreEqual(3, read.Child.Items[0].Id);
    }

    // Case 20: differences

    public class FldDiffWriter
    {
      public FldDiffWriterChild Child;
      public int Value;
      public string Unknown;
    }

    public class FldDiffWriterChild
    {
      public string Value; // an int in the reader's class
      public string Extra;
    }

    public class FldDiffReader
    {
      public FldDiffReaderChild Child;
      public int Value;
    }

    public class FldDiffReaderChild
    {
      public int Value;
    }

    [TestMethod]
    public void Differences_UnknownKeyNextToFields()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Map, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldDiffWriter() { Value = 1, Unknown = "u", Child = new FldDiffWriterChild() { Value = "5", Extra = "e" } }, settings);
      FldDiffReader read = Serializer.Deserialize<FldDiffReader>(bytes, settings, out ReadDifferences differences);
      Assert.AreEqual(1, read.Value);
      Difference unknown = differences.Differences.Single(d => d.Kind == DifferenceKind.UnknownProperty && d.Name == "Unknown");
      Assert.AreEqual(typeof(FldDiffReader), unknown.Class);
      Assert.AreEqual("$", differences.PathOf(unknown.Samples[0]));
      Difference extra = differences.Differences.Single(d => d.Kind == DifferenceKind.UnknownProperty && d.Name == "Extra");
      Assert.AreEqual(typeof(FldDiffReaderChild), extra.Class);
      Assert.AreEqual("$.Child", differences.PathOf(extra.Samples[0]), "through a field");
    }

    [TestMethod]
    public void Differences_InvalidValueForAField()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Map, SerializedFields.Public);
      settings.ReadErrors = ReadErrorHandling.ReportAndContinue;
      byte[] bytes = Serializer.Serialize(new FldDiffWriter() { Value = 1, Child = new FldDiffWriterChild() { Value = "abc" } }, settings);
      FldDiffReader read = Serializer.Deserialize<FldDiffReader>(bytes, settings, out ReadDifferences differences);
      Assert.AreEqual(1, read.Value);
      Assert.AreEqual(0, read.Child.Value);
      Difference invalid = differences.Differences.Single(d => d.Kind == DifferenceKind.InvalidValue);
      Assert.AreEqual("Value", invalid.Name, "the field is named");
      Assert.AreEqual(typeof(FldDiffReaderChild), invalid.Class);
      Assert.AreEqual("$.Child", differences.PathOf(invalid.Samples[0]));
      StringAssert.Contains(differences.GenerateReport(), "FldDiffReaderChild");
    }

    [TestMethod]
    public void Differences_ArrayLayoutExtraValuesAfterTheFields()
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Array, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldDiffWriter() { Value = 1, Unknown = "u", Child = new FldDiffWriterChild() { Value = "5", Extra = "e" } }, settings);
      FldDiffReader read = Serializer.Deserialize<FldDiffReader>(bytes, settings, out ReadDifferences differences);
      Assert.IsNotNull(read);
      Assert.IsTrue(differences.Differences.Any(d => d.Kind == DifferenceKind.ExtraValue), differences.ToString());
    }

    // Case 21: the cached schema

    public class FldCached
    {
      public int P { get; set; }
      public int Field;
      [DataMember]
      public string Marked;
    }

    [TestMethod]
    public void CachedSchema_SettingsDifferingInTheFieldsModeGetTheirOwnSchemas()
    {
      SchemaStore store = new SchemaStore();
      FldCached value = new FldCached() { P = 1, Field = 2, Marked = "m" };
      MsgPackSettings attributed = Writer(store, SerializedFields.Attributed);
      attributed.CompleteSchemas = false;
      MsgPackSettings publicFields = Writer(store, SerializedFields.Public);
      publicFields.CompleteSchemas = false;
      MsgPackSettings none = Writer(store, SerializedFields.None);
      none.CompleteSchemas = false;

      byte[] first = Serializer.Serialize(value, attributed);
      byte[] second = Serializer.Serialize(value, publicFields);
      byte[] third = Serializer.Serialize(value, none);
      SchemaId a = ReferencedId(first);
      SchemaId b = ReferencedId(second);
      SchemaId c = ReferencedId(third);
      Assert.AreNotEqual(a, b);
      Assert.AreNotEqual(a, c);
      Assert.AreNotEqual(b, c);
      Assert.AreEqual(3, store.Count);
      CollectionAssert.AreEqual(new[] { "P", "Marked" }, Entries(store, a)[0].Props.ToArray());
      CollectionAssert.AreEqual(new[] { "P", "Field", "Marked" }, Entries(store, b)[0].Props.ToArray());
      CollectionAssert.AreEqual(new[] { "P" }, Entries(store, c)[0].Props.ToArray());

      // each payload is read with the schema it names
      FldCached readPublic = Serializer.Deserialize<FldCached>(second, publicFields);
      Assert.AreEqual(2, readPublic.Field);
      FldCached readAttributed = Serializer.Deserialize<FldCached>(first, attributed);
      Assert.AreEqual("m", readAttributed.Marked);
      Assert.AreEqual(0, readAttributed.Field);
      FldCached readOther = Serializer.Deserialize<FldCached>(second, attributed);
      Assert.AreEqual(0, readOther.Field, "the reader's mode decides which members it sets (as for non-public properties): the data cannot set a field the reader does not serialize");
      Assert.AreEqual("m", readOther.Marked);
    }

    [TestMethod]
    public void CachedSchema_SameSettingsShareTheSchema()
    {
      SchemaStore store = new SchemaStore();
      FldCached value = new FldCached() { P = 1, Field = 2, Marked = "m" };
      SchemaId a = ReferencedId(Serializer.Serialize(value, Writer(store, SerializedFields.Public)));
      SchemaId b = ReferencedId(Serializer.Serialize(value, Writer(store, SerializedFields.Public)));
      Assert.AreEqual(a, b);
    }

    // Case 22: security

    public class FldStatics
    {
      public static int Shared = 5;
      public static string SharedText = "text";
      public int Normal;
    }

    public class FldPrivateSecret
    {
      private int _secret;
      public int Normal;

      public int GetSecret() { return _secret; }
      public void SetSecret(int secret) { _secret = secret; }
    }

    public class FldCanary
    {
      public static int Created;
      public string Label;

      public FldCanary() { Created++; }
    }

    public class FldSafe
    {
      public string Label;
    }

    public class FldObjectField
    {
      public object Anything;
    }

    private static MsgPackSettings WithGuard(bool schema, IMsgPackTypeGuard guard)
    {
      MsgPackSettings settings = Settings(schema, ObjectLayout.Map, SerializedFields.Public);
      settings.TypeGuard = guard;
      return settings;
    }

    [TestMethod]
    [DataRow(SerializedFields.Public)]
    [DataRow(SerializedFields.All)]
    public void Security_StaticFieldsAreNeverWrittenOrSet(SerializedFields fields)
    {
      MsgPackSettings settings = Settings(false, ObjectLayout.Map, fields);
      Assert.AreEqual("Normal", WrittenNames(new FldStatics() { Normal = 1 }, fields));
      byte[] bytes = Serializer.Serialize(new Dictionary<string, object>() { { "Shared", 99 }, { "SharedText", "changed" }, { "Normal", 2 } }, settings);
      FldStatics read = Serializer.Deserialize<FldStatics>(bytes, settings);
      Assert.AreEqual(2, read.Normal);
      Assert.AreEqual(5, FldStatics.Shared);
      Assert.AreEqual("text", FldStatics.SharedText);
    }

    [TestMethod]
    public void Security_StaticFieldsWithTheDefaultMode()
    {
      MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };
      byte[] bytes = Serializer.Serialize(new Dictionary<string, object>() { { "Shared", 99 }, { "Normal", 2 } }, settings);
      FldStatics read = Serializer.Deserialize<FldStatics>(bytes, settings);
      Assert.AreEqual(0, read.Normal, "not attributed");
      Assert.AreEqual(5, FldStatics.Shared);
    }

    [TestMethod]
    public void Security_AllSetsPrivateFields()
    {
      MsgPackSettings all = Settings(false, ObjectLayout.Map, SerializedFields.All);
      byte[] bytes = Serializer.Serialize(new Dictionary<string, object>() { { "_secret", 7 }, { "Normal", 2 } }, all);
      FldPrivateSecret read = Serializer.Deserialize<FldPrivateSecret>(bytes, all);
      Assert.AreEqual(7, read.GetSecret(), "SerializedFields.All is documented to set private fields");
      Assert.AreEqual(2, read.Normal);
      FldPrivateSecret publicOnly = Serializer.Deserialize<FldPrivateSecret>(bytes, Settings(false, ObjectLayout.Map, SerializedFields.Public));
      Assert.AreEqual(0, publicOnly.GetSecret(), "not with the public fields");
      Assert.AreEqual(2, publicOnly.Normal);
      FldPrivateSecret defaultMode = Serializer.Deserialize<FldPrivateSecret>(bytes, new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map });
      Assert.AreEqual(0, defaultMode.GetSecret(), "not by default");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Security_GuardRefusesATypeIdForAnObjectField(bool schema)
    {
      MsgPackSettings writer = Settings(schema, ObjectLayout.Map, SerializedFields.Public);
      byte[] bytes = Serializer.Serialize(new FldObjectField() { Anything = new FldCanary() { Label = "x" } }, writer);

      FldCanary.Created = 0;
      Assert.IsInstanceOfType<FldCanary>(Serializer.Deserialize<FldObjectField>(bytes, WithGuard(schema, null)).Anything, "without a guard object accepts what the resolvers find");
      Assert.AreEqual(1, FldCanary.Created);

      FldCanary.Created = 0;
      MsgPackException ex = Assert.Throws<MsgPackException>(() => Serializer.Deserialize<FldObjectField>(bytes, WithGuard(schema, new AllowedTypesGuard(typeof(FldSafe)))));
      StringAssert.Contains(ex.Message, nameof(MsgPackOptions.TypeGuard));
      StringAssert.Contains(ex.Message, typeof(FldCanary).FullName);
      Assert.AreEqual(0, FldCanary.Created, "never created");

      FldObjectField allowed = Serializer.Deserialize<FldObjectField>(bytes, WithGuard(schema, new AllowedTypesGuard(typeof(FldCanary))));
      Assert.AreEqual("x", ((FldCanary)allowed.Anything).Label);
    }
  }

  [TestClass]
  public class LsSerializingFieldsSchema : SerializingFieldsSchema
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtSerializingFieldsSchema : SerializingFieldsSchema
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }

  [TestClass]
  public class GenSerializingFieldsSchema : SerializingFieldsSchema
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Gen; } }
  }
}
