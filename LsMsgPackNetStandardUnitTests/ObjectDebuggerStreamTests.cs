using LsMsgPack;
using LsMsgPack.Streaming;
using LtMsgPack;
using LtMsgPack.Streaming;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ObjectDebugger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwOrder = LsMsgPackUnitTests.StreamWriterReaderTests.SwOrder;
using SwLine = LsMsgPackUnitTests.StreamWriterReaderTests.SwLine;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// <see cref="RootObject.Reconstruct(MsgPackItem, SchemaStore, byte[])"/> of streams (MsgPackSettings.ReadStreams): the index is no data, schemas written once per stream, heads with bodies of unknown length.
  /// </summary>
  [TestClass]
  public class ObjectDebuggerStreamTests
  {
    private static SwOrder Order(int n)
    {
      SwOrder order = new SwOrder { Customer = "Customer " + n, Date = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n) };
      for (int t = 0; t < n % 4; t++)
        order.Lines.Add(new SwLine { Product = "P" + t, Price = 1.25m * t, Quantity = t });
      return order;
    }

    private static byte[] Write(Action<MsgPackStreamWriter> write, LtMsgPackOptions options, MsgPackStreamSchemas schemas, MsgPackStreamIndexOptions index)
    {
      MemoryStream ms = new MemoryStream();
      using (MsgPackStreamWriter writer = new MsgPackStreamWriter(ms, new LtMsgPackSerializer(options), schemas, true, index))
        write(writer);
      return ms.ToArray();
    }

    private static MpRoot Unpack(byte[] bytes)
    {
#if KEEPTRACK
      MsgPackSettings settings = new MsgPackSettings { ReadStreams = true, PreservePackages = true };
#else
      MsgPackSettings settings = new MsgPackSettings { ReadStreams = true };
#endif
      return MsgPackItem.UnpackMultiple(bytes, settings);
    }

    private static string Value(ComplexObject obj, string name)
    {
      PrimitiveObject member = obj.Members.FirstOrDefault(m => m.Name == name);
      Assert.IsNotNull(member, $"{obj.Path} has no member {name}");
      return Convert.ToString(member.Value);
    }

    /// <summary>
    /// PerStream writes a schema inline at its first use and refers to it after that: with the data, the references find the schema written before them.
    /// </summary>
    [TestMethod]
    public void SchemasOncePerStream()
    {
      byte[] bytes = Write(w =>
      {
        for (int n = 1; n <= 3; n++)
        {
          w.BeginEntry("order " + n);
          w.WriteObject(Order(n));
        }
      }, new LtMsgPackOptions { WriteSchemaReference = true, SchemaStore = new SchemaStore() }, MsgPackStreamSchemas.PerStream, new MsgPackStreamIndexOptions());
      MpRoot items = Unpack(bytes);

      RootObject root = new RootObject();
      root.Reconstruct(items, null, bytes);
      Assert.AreEqual(ObjectKind.Sequence, root.Kind);
      Assert.HasCount(3, root.Members); // the index and its tail are no payloads
      for (int n = 1; n <= 3; n++)
      {
        RootObject order = (RootObject)root.Members[n - 1];
        Assert.AreEqual(n == 1 ? SchemaSource.Inline : SchemaSource.Reference, order.Schema.Source);
#if KEEPTRACK
        Assert.IsTrue(order.Schema.IsAvailable, "payload " + n);
        Assert.AreEqual(nameof(SwOrder), order.Type);
        Assert.AreEqual("Customer " + n, Value(order, "Customer"));
        Assert.IsNotNull(order.Entry);
        Assert.AreEqual("order " + n, order.Entry.Identifier);
#endif
      }

      // Without the data the references are not found (the store has no schemas)
      RootObject without = new RootObject();
      without.Reconstruct(items);
      Assert.IsFalse(((RootObject)without.Members[1]).Schema.IsAvailable);
    }

    /// <summary>
    /// A head (the complete schema of the root type) and its body, an array of unknown length whose objects have no schema of their own: the same as a payload of Serialize.
    /// </summary>
    [TestMethod]
    public void HeadWithABodyOfUnknownLength()
    {
      byte[] bytes = Write(w =>
      {
        w.BeginEntry("orders");
        w.WriteHead<List<SwOrder>>();
        w.WriteArrayStart();
        for (int n = 1; n <= 3; n++)
          w.WriteObject(Order(n));
        w.WriteEnd();
      }, new LtMsgPackOptions { SchemaVersion = SchemaVersion.Described, WriteSchemaReference = true, SchemaStore = new SchemaStore() }, MsgPackStreamSchemas.PerStream, new MsgPackStreamIndexOptions());

      RootObject root = new RootObject();
      root.Reconstruct(Unpack(bytes), null, bytes);
      Assert.AreEqual(ObjectKind.Collection, root.Kind);
      Assert.AreEqual(SchemaSource.Inline, root.Schema.Source);
      Assert.AreEqual(2, root.Schema.Version);
      Assert.HasCount(3, root.Members);
      ComplexObject second = (ComplexObject)root.Members[1];
      Assert.AreEqual(nameof(SwOrder), second.Type);
      Assert.IsFalse(second.TypeIsGuess);
      Assert.AreEqual("Customer 2", Value(second, "Customer"));
#if KEEPTRACK
      Assert.AreEqual("orders", root.Entry.Identifier);
#endif
    }

    [TestMethod]
    public void TheIndexIsNoSchemaNorData()
    {
      byte[] bytes = Write(w =>
      {
        w.Write("one value");
      }, new LtMsgPackOptions { UseInexedSchema = false }, MsgPackStreamSchemas.PerObject, new MsgPackStreamIndexOptions());
      MpRoot items = Unpack(bytes);
      Assert.HasCount(3, items); // the value, the index, its tail
      Assert.IsEmpty(RootObject.FindSchemaItems(items));

      RootObject root = new RootObject();
      root.Reconstruct(items, null, bytes);
      Assert.AreEqual(ObjectKind.Value, root.Kind);
      Assert.AreEqual("one value", root.Value);

#if KEEPTRACK
      List<MsgPackItem> found = new List<MsgPackItem>();
      new ItemSearch("LtMsgPack", false).FindAll(items, found);
      Assert.IsTrue(found.Any(i => i is MpString), "the writer in the index is searched");
#endif
    }
  }
}
