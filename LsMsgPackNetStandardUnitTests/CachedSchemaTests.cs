using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// The <see cref="SchemaStore"/>: schema references instead of inline schemas, shared sessions that grow, and caching the inline schemas that are read.
  /// </summary>
  public abstract class CachedSchemaTests
  {
    protected abstract ISerializerUnderTest Serializer { get; }

    public CachedSchemaTests()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(ICsPet));
    }

    public interface ICsPet { string Name { get; set; } }

    public class CsDog : ICsPet
    {
      public string Name { get; set; }
      public bool Barks { get; set; }
    }

    public class CsCat : ICsPet
    {
      public string Name { get; set; }
      public int Lives { get; set; }
    }

    public class CsLine
    {
      public string Product { get; set; }
      public int Quantity { get; set; }
      public decimal Price { get; set; }
    }

    public class CsOrder
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      public List<CsLine> Lines { get; set; }
      public ICsPet Pet { get; set; }
      public DateTime Created { get; set; }
    }

    private static CsOrder CreateOrder(int id, ICsPet pet)
    {
      return new CsOrder()
      {
        Id = id,
        Customer = "Customer " + id,
        Lines = new List<CsLine>()
        {
          new CsLine() { Product = "Apples", Quantity = 3, Price = 1.25m },
          new CsLine() { Product = "Pears", Quantity = id, Price = 0.99m }
        },
        Pet = pet,
        Created = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc)
      };
    }

    private static MsgPackSettings Writer(SchemaStore store)
    {
      return new MsgPackSettings() { SchemaStore = store, WriteSchemaReference = true };
    }

    private static MsgPackSettings Reader(SchemaStore store)
    {
      return new MsgPackSettings() { SchemaStore = store };
    }

    private static readonly JsonSerializerSettings Utc = new JsonSerializerSettings() { DateTimeZoneHandling = DateTimeZoneHandling.Utc, TypeNameHandling = TypeNameHandling.Auto };

    private static void AssertSame(CsOrder expected, CsOrder actual)
    {
      Assert.AreEqual(JsonConvert.SerializeObject(expected, Utc), JsonConvert.SerializeObject(actual, Utc));
    }

    private static SchemaId ReferencedId(byte[] payload)
    {
      Assert.AreEqual(0xD8, payload[0], "fixext16");
      Assert.AreEqual(SchemaStore.ReferenceExtensionType, (sbyte)payload[1]);
      return new SchemaId(payload.Skip(2).Take(SchemaId.Length).ToArray());
    }

    [TestMethod]
    public void RoundTripWithReference()
    {
      SchemaStore store = new SchemaStore();
      CsOrder order = CreateOrder(1, new CsDog() { Name = "Rex", Barks = true });

      byte[] payload = Serializer.Serialize(order, Writer(store));
      SchemaId id = ReferencedId(payload);
      Assert.IsTrue(store.Contains(id));
      Assert.AreEqual(1, store.Count);

      AssertSame(order, Serializer.Deserialize<CsOrder>(payload, Reader(store)));
      AssertSame(order, Serializer.Deserialize<CsOrder>(new MemoryStream(payload), Writer(store))); // the writer's settings read too
    }

    [TestMethod]
    public void ReferenceReplacesTheInlineSchema()
    {
      SchemaStore store = new SchemaStore();
      CsOrder order = CreateOrder(2, new CsDog() { Name = "Rex" });

      byte[] inline = Serializer.Serialize(order, new MsgPackSettings());
      byte[] referenced = Serializer.Serialize(order, Writer(store));

      byte[] schema = store.GetSchema(ReferencedId(referenced));
      CollectionAssert.AreEqual(inline, schema.Concat(referenced.Skip(SchemaStore.ReferenceLength)).ToArray(), "the same schema and body");
      Assert.IsLessThan(inline.Length, referenced.Length);
    }

    [TestMethod]
    public void SameTypesKeepTheirSchema()
    {
      SchemaStore store = new SchemaStore();
      MsgPackSettings settings = Writer(store);

      byte[] first = Serializer.Serialize(CreateOrder(1, new CsDog() { Name = "Rex" }), settings);
      byte[] second = Serializer.Serialize(CreateOrder(2, new CsDog() { Name = "Fido", Barks = true }), Writer(store)); // other settings, same defaults

      Assert.AreEqual(ReferencedId(first), ReferencedId(second));
      Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void SchemaGrowsWithNewTypesAndOldDataStaysReadable()
    {
      SchemaStore store = new SchemaStore();
      CsOrder withDog = CreateOrder(1, new CsDog() { Name = "Rex", Barks = true });
      CsOrder withCat = CreateOrder(2, new CsCat() { Name = "Mia", Lives = 9 });

      byte[] dogPayload = Serializer.Serialize(withDog, Writer(store));
      byte[] catPayload = Serializer.Serialize(withCat, Writer(store));
      byte[] dogAgain = Serializer.Serialize(withDog, Writer(store));

      Assert.AreNotEqual(ReferencedId(dogPayload), ReferencedId(catPayload), "the cat was added to the schema");
      Assert.AreEqual(ReferencedId(catPayload), ReferencedId(dogAgain), "the grown schema is used from then on");
      Assert.AreEqual(2, store.Count);

      // Another process: its store gets the schemas of the writer
      SchemaStore readerStore = new SchemaStore();
      foreach (SchemaId id in store.GetSchemaIds())
        readerStore.Register(store.GetSchema(id));

      AssertSame(withDog, Serializer.Deserialize<CsOrder>(dogPayload, Reader(readerStore)));
      AssertSame(withCat, Serializer.Deserialize<CsOrder>(catPayload, Reader(readerStore)));
      AssertSame(withDog, Serializer.Deserialize<CsOrder>(dogAgain, Reader(readerStore)));
    }

    [TestMethod]
    public void EachRootTypeHasItsOwnSchema()
    {
      SchemaStore store = new SchemaStore();
      byte[] order = Serializer.Serialize(CreateOrder(1, null), Writer(store));
      byte[] line = Serializer.Serialize(new CsLine() { Product = "Plums", Quantity = 1 }, Writer(store));

      Assert.AreNotEqual(ReferencedId(order), ReferencedId(line));
      Assert.AreEqual("Plums", Serializer.Deserialize<CsLine>(line, Reader(store)).Product);
    }

    [TestMethod]
    public void MissingSchemaCanBeRegisteredAfterwards()
    {
      SchemaStore writerStore = new SchemaStore();
      CsOrder order = CreateOrder(3, new CsCat() { Name = "Mia" });
      byte[] payload = Serializer.Serialize(order, Writer(writerStore));
      SchemaId id = ReferencedId(payload);

      SchemaStore readerStore = new SchemaStore();
      MissingSchemaException missing = Assert.ThrowsExactly<MissingSchemaException>(() => Serializer.Deserialize<CsOrder>(payload, Reader(readerStore)));
      Assert.AreEqual(id, missing.SchemaId);

      Assert.AreEqual(id, readerStore.Register(writerStore.GetSchema(id)));
      AssertSame(order, Serializer.Deserialize<CsOrder>(payload, Reader(readerStore)));
    }

    [TestMethod]
    public void SchemaProviderIsAskedForMissingSchemas()
    {
      SchemaStore writerStore = new SchemaStore();
      CsOrder order = CreateOrder(4, new CsDog() { Name = "Rex" });
      byte[] payload = Serializer.Serialize(order, Writer(writerStore));

      int asked = 0;
      SchemaStore readerStore = new SchemaStore() { SchemaProvider = id => { asked++; return writerStore.GetSchema(id); } };
      AssertSame(order, Serializer.Deserialize<CsOrder>(payload, Reader(readerStore)));
      AssertSame(order, Serializer.Deserialize<CsOrder>(payload, Reader(readerStore)));
      Assert.AreEqual(1, asked);
    }

    [TestMethod]
    public void SchemaProviderCannotSubstituteAnotherSchema()
    {
      SchemaStore writerStore = new SchemaStore();
      byte[] payload = Serializer.Serialize(CreateOrder(5, null), Writer(writerStore));
      byte[] otherSchema = writerStore.GetSchema(ReferencedId(Serializer.Serialize(new CsLine() { Product = "x" }, Writer(writerStore))));

      SchemaStore readerStore = new SchemaStore() { SchemaProvider = id => otherSchema };
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<CsOrder>(payload, Reader(readerStore)));
      StringAssert.Contains(ex.Message, "returned the schema");
    }

    [TestMethod]
    public void TheIdIsComputedFromTheSchema()
    {
      SchemaStore writerStore = new SchemaStore();
      byte[] payload = Serializer.Serialize(CreateOrder(6, null), Writer(writerStore));
      SchemaId id = ReferencedId(payload);
      byte[] schema = writerStore.GetSchema(id);
      Assert.AreEqual(id, SchemaId.Compute(schema));

      // A modified schema (a property renamed) gets another id, it cannot take the place of the original
      byte[] tampered = (byte[])schema.Clone();
      int at = IndexOf(tampered, System.Text.Encoding.UTF8.GetBytes("Customer"));
      tampered[at] = (byte)'K';
      SchemaStore readerStore = new SchemaStore();
      Assert.AreNotEqual(id, readerStore.Register(tampered));
      Assert.ThrowsExactly<MissingSchemaException>(() => Serializer.Deserialize<CsOrder>(payload, Reader(readerStore)));
    }

    private static int IndexOf(byte[] bytes, byte[] part)
    {
      for (int t = 0; t <= bytes.Length - part.Length; t++)
        if (bytes.Skip(t).Take(part.Length).SequenceEqual(part))
          return t;
      return -1;
    }

    [TestMethod]
    public void RegisterRefusesWhatIsNotASchema()
    {
      SchemaStore store = new SchemaStore();
      Assert.ThrowsExactly<MsgPackException>(() => store.Register(new byte[] { 0x92, 0x01, 0x02 })); // an array
      Assert.ThrowsExactly<MsgPackException>(() => store.Register(new byte[] { 0x81, 0xA1, (byte)'T', 0x91, 0x05 })); // a property name that is a number
      Assert.ThrowsExactly<MsgPackException>(() => store.Register(new byte[] { 0x81, 0xA1, (byte)'T' })); // truncated
      Assert.ThrowsExactly<MsgPackException>(() => store.Register(new byte[] { 0x80, 0x00 })); // trailing bytes
      Assert.AreEqual(0, store.Count);
    }

    [TestMethod]
    public void MaxSchemasLimitsReceivedSchemas()
    {
      SchemaStore writerStore = new SchemaStore();
      byte[] a = writerStore.GetSchema(ReferencedId(Serializer.Serialize(CreateOrder(1, null), Writer(writerStore))));
      byte[] b = writerStore.GetSchema(ReferencedId(Serializer.Serialize(new CsLine() { Product = "x" }, Writer(writerStore))));

      SchemaStore readerStore = new SchemaStore() { MaxSchemas = 1 };
      readerStore.Register(a);
      readerStore.Register(a); // already known
      Assert.ThrowsExactly<MsgPackException>(() => readerStore.Register(b));
      Assert.AreEqual(1, readerStore.Count);
    }

    [TestMethod]
    public void ExportAndImport()
    {
      SchemaStore writerStore = new SchemaStore();
      byte[] dog = Serializer.Serialize(CreateOrder(1, new CsDog() { Name = "Rex" }), Writer(writerStore));
      byte[] cat = Serializer.Serialize(CreateOrder(2, new CsCat() { Name = "Mia" }), Writer(writerStore));

      MemoryStream exported = new MemoryStream();
      writerStore.Export(exported);
      exported.Position = 0;

      SchemaStore readerStore = new SchemaStore();
      Assert.AreEqual(2, readerStore.Import(exported));
      CollectionAssert.AreEquivalent(writerStore.GetSchemaIds(), readerStore.GetSchemaIds());
      Assert.AreEqual("Rex", Serializer.Deserialize<CsOrder>(dog, Reader(readerStore)).Pet.Name);
      Assert.AreEqual("Mia", Serializer.Deserialize<CsOrder>(cat, Reader(readerStore)).Pet.Name);
    }

    [TestMethod]
    public void SchemaIdText()
    {
      SchemaId id = SchemaId.Compute(new byte[] { 0x80 });
      Assert.AreEqual(32, id.ToString().Length);
      Assert.AreEqual(id, SchemaId.Parse(id.ToString()));
      Assert.AreEqual(id, new SchemaId(id.ToByteArray()));
    }

    [TestMethod]
    public void NullIsWrittenWithoutReference()
    {
      SchemaStore store = new SchemaStore();
      byte[] payload = Serializer.Serialize<CsOrder>(null, Writer(store));
      CollectionAssert.AreEqual(new byte[] { 0xC0 }, payload);
      Assert.IsNull(Serializer.Deserialize<CsOrder>(payload, Reader(store)));
      Assert.AreEqual(0, store.Count);
    }

    [TestMethod]
    public void ReferenceNeedsAStore()
    {
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Serialize(CreateOrder(1, null), new MsgPackSettings() { WriteSchemaReference = true }));

      byte[] payload = Serializer.Serialize(CreateOrder(1, null), Writer(new SchemaStore()));
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<CsOrder>(payload, new MsgPackSettings()));
      StringAssert.Contains(ex.Message, "SchemaStore");
    }

    [TestMethod]
    public void OtherExtensionIsNotAReference()
    {
      byte[] payload = Serializer.Serialize(CreateOrder(1, null), Writer(new SchemaStore()));
      payload[1] = 3;
      Assert.ThrowsExactly<MsgPackException>(() => Serializer.Deserialize<CsOrder>(payload, Reader(new SchemaStore())));
    }

    [TestMethod]
    public void WithoutIndexedSchemaTheReferenceIsNotUsed()
    {
      SchemaStore store = new SchemaStore();
      MsgPackSettings names = new MsgPackSettings() { UseInexedSchema = false, SchemaStore = store, WriteSchemaReference = true };
      CsOrder order = CreateOrder(7, null);
      byte[] payload = Serializer.Serialize(order, names);
      CollectionAssert.AreEqual(Serializer.Serialize(order, new MsgPackSettings() { UseInexedSchema = false }), payload);
      AssertSame(order, Serializer.Deserialize<CsOrder>(payload, names));
      Assert.AreEqual(0, store.Count);
    }

    [TestMethod]
    [DataRow(EndianAction.NeverSwap)]
    [DataRow(EndianAction.SwapIfCurrentSystemIsLittleEndian)]
    public void WriterByteOrderDoesNotChangeTheStoredSchema(EndianAction endian)
    {
      SchemaStore store = new SchemaStore();
      CsOrder order = CreateOrder(8, new CsDog() { Name = new string('x', 300) }); // a name with a length of 2 bytes
      MsgPackSettings settings = new MsgPackSettings() { SchemaStore = store, WriteSchemaReference = true, EndianAction = endian };
      byte[] payload = Serializer.Serialize(order, settings);

      SchemaStore reference = new SchemaStore();
      byte[] canonical = Serializer.Serialize(order, Writer(reference));
      Assert.AreEqual(ReferencedId(canonical), ReferencedId(payload));
      AssertSame(order, Serializer.Deserialize<CsOrder>(payload, new MsgPackSettings() { SchemaStore = store, EndianAction = endian }));
    }

    [TestMethod]
    public void InlineSchemasAreCachedByTheirBytes()
    {
      SchemaStore store = new SchemaStore();
      CsOrder dog = CreateOrder(1, new CsDog() { Name = "Rex" });
      CsOrder cat = CreateOrder(2, new CsCat() { Name = "Mia", Lives = 7 });
      byte[] dogPayload = Serializer.Serialize(dog, new MsgPackSettings());
      byte[] catPayload = Serializer.Serialize(cat, new MsgPackSettings());

      for (int t = 0; t < 3; t++)
      {
        AssertSame(dog, Serializer.Deserialize<CsOrder>(dogPayload, Reader(store)));
        AssertSame(cat, Serializer.Deserialize<CsOrder>(catPayload, Reader(store)));
      }
      Assert.AreEqual(0, store.Count, "inline schemas cannot be referred to");

      SchemaStore notCaching = new SchemaStore() { CacheInlineSchemas = false };
      AssertSame(dog, Serializer.Deserialize<CsOrder>(dogPayload, Reader(notCaching)));

      SchemaStore full = new SchemaStore() { MaxSchemas = 1 };
      AssertSame(dog, Serializer.Deserialize<CsOrder>(dogPayload, Reader(full)));
      AssertSame(cat, Serializer.Deserialize<CsOrder>(catPayload, Reader(full))); // read without caching
    }

    [TestMethod]
    public void ReaderSessionGrowsForOtherRootTypes()
    {
      // A reader session is shared per schema: reading the same schema as another type needs other properties
      SchemaStore store = new SchemaStore();
      CsDog dog = new CsDog() { Name = "Rex", Barks = true };
      byte[] payload = Serializer.Serialize<ICsPet>(dog, Writer(store));
      Assert.AreEqual("Rex", Serializer.Deserialize<ICsPet>(payload, Reader(store)).Name);
      Assert.IsTrue(Serializer.Deserialize<CsDog>(payload, Reader(store)).Barks);
      Assert.AreEqual("Rex", ((CsDog)Serializer.Deserialize<object>(payload, Reader(store))).Name);
    }

    [TestMethod]
    public void ConcurrentWritersAndReaders()
    {
      SchemaStore writerStore = new SchemaStore();
      SchemaStore readerStore = new SchemaStore() { SchemaProvider = id => writerStore.GetSchema(id) };
      MsgPackSettings writer = Writer(writerStore);
      MsgPackSettings reader = Reader(readerStore);
      int failures = 0;

      Parallel.For(0, 2000, i =>
      {
        ICsPet pet;
        switch (i % 3)
        {
          case 0: pet = new CsDog() { Name = "Dog " + i, Barks = true }; break;
          case 1: pet = new CsCat() { Name = "Cat " + i, Lives = i % 9 }; break;
          default: pet = null; break;
        }
        CsOrder order = CreateOrder(i, pet);
        byte[] payload = Serializer.Serialize(order, writer);
        CsOrder read = Serializer.Deserialize<CsOrder>(payload, reader);
        if (JsonConvert.SerializeObject(order, Utc) != JsonConvert.SerializeObject(read, Utc))
          System.Threading.Interlocked.Increment(ref failures);

        byte[] inline = Serializer.Serialize(order, new MsgPackSettings());
        if (JsonConvert.SerializeObject(order, Utc) != JsonConvert.SerializeObject(Serializer.Deserialize<CsOrder>(inline, reader), Utc))
          System.Threading.Interlocked.Increment(ref failures);
      });

      Assert.AreEqual(0, failures);
      Assert.IsLessThanOrEqualTo(3, writerStore.Count, "at most one schema per order of first use of dog and cat");
    }
  }

  [TestClass]
  public class LsCachedSchemaTests : CachedSchemaTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Ls; } }
  }

  [TestClass]
  public class LtCachedSchemaTests : CachedSchemaTests
  {
    protected override ISerializerUnderTest Serializer { get { return Serializers.Lt; } }
  }
}
