using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPack.Extensions;
using LsMsgPack.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// What LsMsgPack does beyond LsMsgPack: limits for untrusted data, cycles, custom extensions.
  /// </summary>
  [TestClass]
  public class LsMsgPackTests
  {
    public class LsNode
    {
      public string Name { get; set; }
      public LsNode Next { get; set; }
    }

    public struct LsPoint
    {
      public int X;
      public int Y;
    }

    public class LsWithPoint
    {
      public LsPoint Point { get; set; }
      public object Anything { get; set; }
    }

    public sealed class PointExtension : LsExtension<LsPoint>
    {
      public override sbyte TypeCode { get { return 42; } }
      public override int GetMaxLength(LsPoint value) { return 8; }
      public override int Write(LsPoint value, Span<byte> destination)
      {
        BitConverter.TryWriteBytes(destination, value.X);
        BitConverter.TryWriteBytes(destination.Slice(4), value.Y);
        return 8;
      }
      public override LsPoint Read(ReadOnlySpan<byte> data)
      {
        return new LsPoint { X = BitConverter.ToInt32(data), Y = BitConverter.ToInt32(data.Slice(4)) };
      }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CycleIsRefused(bool schema)
    {
      LsNode node = new LsNode { Name = "loop" };
      node.Next = node;
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = schema });
      Assert.ThrowsExactly<MsgPackException>(() => lt.Serialize(node));
    }

    [TestMethod]
    public void DeepNestingIsRefused()
    {
      byte[] data = new byte[1000];
      for (int t = 0; t < data.Length; t++)
        data[t] = 0x91; // an array holding an array holding ...
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false });
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<object>(data));
    }

    [TestMethod]
    public void ClaimedLengthsAreChecked()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false });
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<object>(new byte[] { 0xDD, 0x7F, 0xFF, 0xFF, 0xFF })); // an array of 2^31 items in 5 bytes
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<List<int>>(new byte[] { 0xDD, 0x7F, 0xFF, 0xFF, 0xFF }));
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<string>(new byte[] { 0xDB, 0x7F, 0xFF, 0xFF, 0xFF, 0x41 }));
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<LsNode>(new byte[] { 0x82, 0xA4, (byte)'N', (byte)'a' })); // truncated
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CustomExtension(bool schema)
    {
      LsMsgPackOptions options = new LsMsgPackOptions { UseIndexedSchema = schema };
      options.Extensions = new LsExtension[] { new DecimalExtension(), new PointExtension() };
      LsMsgPackSerializer lt = new LsMsgPackSerializer(options);

      LsWithPoint value = new LsWithPoint { Point = new LsPoint { X = 3, Y = -4 }, Anything = new LsPoint { X = 5, Y = 6 } };
      byte[] bytes = lt.Serialize(value);
      LsWithPoint back = lt.Deserialize<LsWithPoint>(bytes);
      Assert.AreEqual(3, back.Point.X);
      Assert.AreEqual(-4, back.Point.Y);
      Assert.AreEqual(6, ((LsPoint)back.Anything).Y); // the extension is read back as the point, also into object

      // Without the extension it cannot be read as the point (as in LsMsgPack)
      LsMsgPackSerializer without = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = schema });
      Assert.ThrowsExactly<MsgPackException>(() => without.Deserialize<LsWithPoint>(bytes));
    }

    [TestMethod]
    public void UnknownExtensionInObjectMember()
    {
      // As another library writes it: an extension without a type id
      byte[] data = new MpMap(new[] { new KeyValuePair<object, object>("Anything", new MpExt { TypeSpecifier = 42, Value = new byte[] { 1, 2, 3 } }) }, new MsgPackSettings()).ToBytes();
      LsWithPoint read = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false }).Deserialize<LsWithPoint>(data);
      MsgPackExtension extension = (MsgPackExtension)read.Anything;
      Assert.AreEqual((sbyte)42, extension.TypeCode);
      CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, extension.Data);
    }

    [TestMethod]
    public void OptionsAreCopied()
    {
      LsMsgPackOptions options = new LsMsgPackOptions { UseIndexedSchema = false };
      LsMsgPackSerializer lt = new LsMsgPackSerializer(options);
      options.UseIndexedSchema = true; // too late for this serializer
      Assert.IsFalse(lt.Options.UseIndexedSchema);
      Assert.AreEqual((byte)0x92, lt.Serialize(new LsNode { Name = "x" })[0]); // the object as an array (Name, Next), no schema
    }

    [TestMethod]
    public void ThreadsShareOneSerializer()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { SchemaStore = new SchemaStore(), WriteSchemaReference = true });
      int failures = 0;
      System.Threading.Tasks.Parallel.For(0, 2000, i =>
      {
        LsNode node = new LsNode { Name = "n" + i, Next = i % 2 == 0 ? new LsNode { Name = "next" } : null };
        LsNode back = lt.Deserialize<LsNode>(lt.Serialize(node));
        if (back.Name != node.Name || (back.Next?.Name ?? "") != (node.Next?.Name ?? ""))
          System.Threading.Interlocked.Increment(ref failures);
      });
      Assert.AreEqual(0, failures);
    }
    private static readonly LsMsgPackSerializer Names = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false });

    public class LsFormats
    {
      public Guid Id { get; set; }
      public decimal Amount { get; set; }
      public DateTimeOffset When { get; set; }
      public DateTime Created { get; set; }
    }

    [TestMethod]
    public void GuidAsString()
    {
      LsMsgPackOptions options = new LsMsgPackOptions { UseIndexedSchema = false, GuidFormat = GuidFormat.String };
      LsMsgPackSerializer lt = new LsMsgPackSerializer(options);
      Guid id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

      byte[] bytes = lt.Serialize(id);
      CollectionAssert.AreEqual(new byte[] { 0xD9, 36 }, new[] { bytes[0], bytes[1] });
      Assert.AreEqual(id, lt.Deserialize<Guid>(bytes));
      Assert.AreEqual(id, lt.Deserialize<Guid>(Names.Serialize(id)), "bin 16 is read as well");
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<Guid>(lt.Serialize("not a guid")));
    }

    [TestMethod]
    [DataRow((sbyte)1)]
    [DataRow((sbyte)4)]
    [DataRow((sbyte)-5)]
    public void DecimalAsStringAlsoReadsTheExtension(sbyte typeCode)
    {
      decimal[] values = { 12.50m, -1m, 0m, decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m };
      LsExtension[] extensions = { new DecimalExtension(typeCode) };
      LsMsgPackSerializer writer = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, Extensions = extensions });
      LsMsgPackSerializer reader = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, Extensions = extensions, DecimalFormat = DecimalFormat.String });
      for (int t = values.Length - 1; t >= 0; t--)
      {
        decimal back = reader.Deserialize<decimal>(writer.Serialize(values[t]));
        Assert.AreEqual(values[t], back);
        Assert.AreEqual(values[t].ToString(System.Globalization.CultureInfo.InvariantCulture), back.ToString(System.Globalization.CultureInfo.InvariantCulture), "The scale is kept");
      }
    }

    [TestMethod]
    public void GenericReadsDecimalsAndGuidsAsDeclared()
    {
      Guid id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
      LsMsgPackSerializer asDeclared = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, ObjectLayout = ObjectLayout.Map });
      LsMsgPackSerializer generic = new LsMsgPackSerializer(LsMsgPackPresets.Generic());
      Assert.AreEqual(12.50m, generic.Deserialize<decimal>(asDeclared.Serialize(12.50m)));
      Assert.AreEqual(id, generic.Deserialize<Guid>(asDeclared.Serialize(id)));
    }

    [TestMethod]
    public void DecimalAsString()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, DecimalFormat = DecimalFormat.String });
      foreach (decimal value in new[] { 0m, 1234.50m, -0.0000001m, decimal.MaxValue })
      {
        byte[] bytes = lt.Serialize(value);
        Assert.AreEqual(value.ToString(System.Globalization.CultureInfo.InvariantCulture), lt.Deserialize<string>(bytes));
        Assert.AreEqual(value, lt.Deserialize<decimal>(bytes));
      }
      Assert.AreEqual(1.5m, lt.Deserialize<decimal>(Names.Serialize(1.5m)), "the extension is read as well");
    }

    [TestMethod]
    public void DateTimeOffsetWithOffset()
    {
      LsMsgPackSerializer lt = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, DateTimeOffsetFormat = DateTimeOffsetFormat.ClockTimeAndOffset });
      DateTimeOffset when = new DateTimeOffset(2026, 9, 29, 14, 30, 15, 250, TimeSpan.FromMinutes(-150));

      byte[] bytes = lt.Serialize(when);
      Assert.AreEqual((byte)0x92, bytes[0]);
      DateTimeOffset back = lt.Deserialize<DateTimeOffset>(bytes);
      Assert.AreEqual(when, back);
      Assert.AreEqual(when.Offset, back.Offset);
      LsMsgPackSerializer timestamp = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, DateTimeOffsetFormat = DateTimeOffsetFormat.Timestamp });
      Assert.AreEqual(when, lt.Deserialize<DateTimeOffset>(timestamp.Serialize(when)), "a timestamp is read as well");

      LsFormats formats = lt.Deserialize<LsFormats>(lt.Serialize(new LsFormats { When = when }));
      Assert.AreEqual(when.Offset, formats.When.Offset);
    }

    [TestMethod]
    public void UnspecifiedAsUtc()
    {
      DateTime unspecified = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Unspecified);
      LsMsgPackSerializer timestamps = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp });
      LsMsgPackSerializer local = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp, UnspecifiedDateTimeKind = DateTimeKind.Local });
      CollectionAssert.AreEqual(timestamps.Serialize(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc)), timestamps.Serialize(unspecified), "the default: the clock time in UTC, as LsMsgPackDebug");
      CollectionAssert.AreEqual(timestamps.Serialize(DateTime.SpecifyKind(unspecified, DateTimeKind.Local)), local.Serialize(unspecified));
    }

    /// <summary>
    /// <see cref="DateTimeFormat.Native"/> (the default) uses extension type 6: a custom extension or converter of that code is refused, with timestamps it is allowed (and read first).
    /// </summary>
    [TestMethod]
    public void NativeDateTime_RefusesExtensionCode6()
    {
      LsMsgPackOptions native = new LsMsgPackOptions { Extensions = new LsExtension[] { new DecimalExtension(6) } };
      InvalidOperationException refused = Assert.ThrowsExactly<InvalidOperationException>(() => new LsMsgPackSerializer(native));
      StringAssert.Contains(refused.Message, "DateTimeFormat");

      LsMsgPackSerializer timestamps = new LsMsgPackSerializer(new LsMsgPackOptions { UseIndexedSchema = false, DateTimeFormat = DateTimeFormat.Timestamp, Extensions = new LsExtension[] { new DecimalExtension(6) } });
      byte[] bytes = timestamps.Serialize(12.5m);
      Assert.AreEqual((byte)6, bytes[1]);
      Assert.AreEqual(12.5m, timestamps.Deserialize<object>(bytes), "the registered extension wins");
    }

    [TestMethod]
    public void NerdbankGuidExtensionReadsBigEndian()
    {
      Guid id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
      byte[] data = new byte[] { 0xD8, 2, 0x0f, 0x8f, 0xad, 0x5b, 0xd9, 0xcb, 0x46, 0x9f, 0xa1, 0x65, 0x70, 0x86, 0x77, 0x28, 0x95, 0x0e };
      Assert.AreEqual(id, new LsMsgPackSerializer(LsMsgPackPresets.Nerdbank()).Deserialize<Guid>(data));
    }

    public class LsOrder
    {
      public int Number { get; set; }
      public string Customer { get; set; }
      public string Remarks { get; set; }
    }

    [TestMethod]
    [DataRow(EndianAction.SwapIfCurrentSystemIsLittleEndian)]
    [DataRow(EndianAction.NeverSwap)]
    public void HttpNegotiation(EndianAction endian)
    {
      LsMsgPackHttpOptions options = new LsMsgPackHttpOptions();
      options.XLsMsgPack.EndianAction = endian;
      LsMsgPackHttpSerializer server = new LsMsgPackHttpSerializer(options);
      LsMsgPackHttpSerializer client = new LsMsgPackHttpSerializer(options);
      Uri uri = new Uri("https://example.org/api/orders/1");
      LsOrder order = new LsOrder { Number = 1, Customer = "Infotopie", Remarks = "none" };

      MsgPackPayload inline = server.Serialize(order, typeof(LsOrder), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(uri));
      Assert.IsFalse(inline.IsReference);
      byte[] inlineBytes = inline.ToArray();
      CollectionAssert.AreEqual(new LsMsgPackSerializer(new LsMsgPackOptions { EndianAction = endian }).Serialize(order), inlineBytes, "the same as without negotiation");
      Assert.IsTrue(client.LearnSchema(uri, inline.SchemaId, inlineBytes, 0, inlineBytes.Length, MsgPackMediaTypes.XLsMsgPack));
      Assert.AreEqual(inline.SchemaId, client.GetSchemasHeader(new Uri("https://example.org/other")), "per server, not per path");
      Assert.IsNull(client.GetSchemasHeader(new Uri("https://example.com/")));

      MsgPackPayload reference = server.Serialize(order, typeof(LsOrder), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(uri));
      Assert.IsTrue(reference.IsReference);
      byte[] referenceBytes = reference.ToArray();
      Assert.AreEqual(SchemaStore.ReferenceLength + inline.Body.Count, referenceBytes.Length);
      Assert.AreEqual("Infotopie", ((LsOrder)client.Deserialize(typeof(LsOrder), referenceBytes, 0, referenceBytes.Length, MsgPackMediaTypes.XLsMsgPack)).Customer);

      // A header that claims another schema is not learned
      LsMsgPackHttpSerializer other = new LsMsgPackHttpSerializer(options);
      Assert.IsFalse(other.LearnSchema(uri, "00112233445566778899aabbccddeeff", inlineBytes, 0, inlineBytes.Length, MsgPackMediaTypes.XLsMsgPack));
      Assert.IsFalse(other.LearnSchema(uri, "not an id", inlineBytes, 0, inlineBytes.Length, MsgPackMediaTypes.XLsMsgPack));
      Assert.IsNull(other.GetSchemasHeader(uri));

      // Plain media types and null are not negotiated
      Assert.IsNull(server.Serialize(order, typeof(LsOrder), MsgPackMediaTypes.MsgPack, client.GetSchemasHeader(uri)).SchemaId);
      CollectionAssert.AreEqual(new byte[] { 0xC0 }, server.Serialize(null, typeof(LsOrder), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(uri)).ToArray());
    }

    [TestMethod]
    public void HttpNegotiationAdvertisesTheMostRecentSchemas()
    {
      LsMsgPackHttpOptions options = new LsMsgPackHttpOptions() { MaxAdvertisedSchemas = 2 };
      LsMsgPackHttpSerializer server = new LsMsgPackHttpSerializer(options);
      LsMsgPackHttpSerializer client = new LsMsgPackHttpSerializer(options);
      Uri uri = new Uri("https://example.org/");

      string[] ids = new string[3];
      object[] values = { new LsOrder { Number = 1, Customer = "a", Remarks = "b" }, new LsNode { Name = "node", Next = new LsNode() }, new LsWithPoint { Anything = "x" } };
      for (int t = 0; t < values.Length; t++)
      {
        MsgPackPayload payload = server.Serialize(values[t], values[t].GetType(), MsgPackMediaTypes.XLsMsgPack, null);
        byte[] bytes = payload.ToArray();
        Assert.IsTrue(client.LearnSchema(uri, payload.SchemaId, bytes, 0, bytes.Length, MsgPackMediaTypes.XLsMsgPack));
        ids[t] = payload.SchemaId;
      }
      Assert.AreEqual(ids[2] + "," + ids[1], client.GetSchemasHeader(uri));
    }

    [TestMethod]
    public void HttpNegotiationHeaderFollowsTheSchemasPerServer()
    {
      LsMsgPackHttpSerializer server = new LsMsgPackHttpSerializer();
      LsMsgPackHttpSerializer client = new LsMsgPackHttpSerializer();
      Uri first = new Uri("https://example.org/api/orders/1"), second = new Uri("http://example.net:8080/nodes");
      object[] values = { new LsOrder { Number = 1, Customer = "a", Remarks = "b" }, new LsNode { Name = "node", Next = new LsNode() }, new LsWithPoint { Anything = "x" } };
      string[] ids = new string[values.Length];
      Func<Uri, int, bool> learn = (uri, t) =>
      {
        MsgPackPayload payload = server.Serialize(values[t], values[t].GetType(), MsgPackMediaTypes.XLsMsgPack, null);
        byte[] bytes = payload.ToArray();
        ids[t] = payload.SchemaId;
        return client.LearnSchema(uri, payload.SchemaId, bytes, 0, bytes.Length, MsgPackMediaTypes.XLsMsgPack);
      };

      // the servers in turn: each keeps its own list (the client remembers the last server it saw)
      Assert.IsTrue(learn(first, 0));
      Assert.IsTrue(learn(second, 1));
      Assert.AreEqual(ids[0], client.GetSchemasHeader(first));
      Assert.AreEqual(ids[1], client.GetSchemasHeader(second));
      Assert.IsTrue(learn(first, 2));
      Assert.AreEqual(ids[2] + "," + ids[0], client.GetSchemasHeader(first));
      Assert.AreEqual(ids[1], client.GetSchemasHeader(second));
      Assert.AreEqual(ids[2] + "," + ids[0], client.GetSchemasHeader(new Uri("HTTPS://EXAMPLE.ORG:443/other?x=1")), "the same origin written otherwise");
      Assert.IsNull(client.GetSchemasHeader(new Uri("https://example.org.evil.com/")));
      Assert.IsNull(client.GetSchemasHeader(new Uri("https://example.org:8443/")));

      // the header is built again when the list changes, not before
      string header = client.GetSchemasHeader(first);
      Assert.AreSame(header, client.GetSchemasHeader(first));
      Assert.IsTrue(learn(first, 2)); // the most recent one already
      Assert.AreSame(header, client.GetSchemasHeader(first));
      Assert.IsTrue(learn(first, 0));
      Assert.AreEqual(ids[0] + "," + ids[2], client.GetSchemasHeader(first));

      // a schema id written in upper case is learned and held as well
      Assert.IsTrue(client.LearnSchema(second, ids[0].ToUpperInvariant(), null, 0, 0, MsgPackMediaTypes.XLsMsgPack), "held: no body needed");
      Assert.AreEqual(ids[0] + "," + ids[1], client.GetSchemasHeader(second));
      Assert.IsTrue(server.Serialize(values[0], values[0].GetType(), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(second).ToUpperInvariant()).IsReference, "a header in upper case");
      Assert.IsFalse(server.Serialize(values[2], values[2].GetType(), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(second).ToUpperInvariant()).IsReference);
      Assert.IsFalse(new LsMsgPackHttpSerializer().LearnSchema(first, ids[0], null, 0, 0, MsgPackMediaTypes.XLsMsgPack), "not held: needs the body");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void HttpNegotiationAdvertisesPerRoute(bool byRoute)
    {
      LsMsgPackHttpSerializer server = new LsMsgPackHttpSerializer();
      LsMsgPackHttpSerializer client = new LsMsgPackHttpSerializer(new LsMsgPackHttpOptions { AdvertiseByRoute = byRoute });
      object order = new LsOrder { Number = 1, Customer = "a", Remarks = "b" }, node = new LsNode { Name = "node", Next = new LsNode() }, point = new LsWithPoint { Anything = "x" };
      Func<string, object, string> learn = (url, value) =>
      {
        MsgPackPayload payload = server.Serialize(value, value.GetType(), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(new Uri(url)));
        byte[] bytes = payload.ToArray();
        Assert.IsTrue(client.LearnSchema(new Uri(url), payload.SchemaId, bytes, 0, bytes.Length, MsgPackMediaTypes.XLsMsgPack));
        return payload.SchemaId;
      };
      string orderId = learn("https://example.org/api/orders/17", order);
      string nodeId = learn("https://example.org/api/nodes", node);
      string pointId = learn("https://example.org/api/points/3f2504e0-4f89-11d3-9a0c-0305e82c3301", point);
      string recent = pointId + "," + nodeId + "," + orderId;

      Assert.AreEqual(byRoute ? orderId : recent, client.GetSchemasHeader(new Uri("https://example.org/api/orders/18")), "a segment of digits is an id");
      Assert.AreEqual(byRoute ? orderId : recent, client.GetSchemasHeader(new Uri("https://example.org/api/orders/18?expand=lines#top")));
      Assert.AreEqual(byRoute ? nodeId : recent, client.GetSchemasHeader(new Uri("https://example.org/api/nodes")));
      Assert.AreEqual(byRoute ? pointId : recent, client.GetSchemasHeader(new Uri("https://example.org/api/points/00000000-0000-0000-0000-00000000000A")), "a Guid is an id");
      Assert.AreEqual(recent, client.GetSchemasHeader(new Uri("https://example.org/api/orders/18/lines")), "a route not called yet: the most recent ones");
      Assert.AreEqual(recent, client.GetSchemasHeader(new Uri("https://example.org/api/nodes/next")));
      Assert.AreEqual(recent, client.GetSchemasHeader(new Uri("https://example.org/v2/api/nodes")));
      Assert.IsNull(client.GetSchemasHeader(new Uri("https://example.com/api/nodes")), "per server");

      // a route that returns several types lists them all, most recent first; the list of recent ones changes only for schemas new to a route
      Assert.AreEqual(orderId, learn("https://example.org/api/nodes", order));
      Assert.AreEqual(byRoute ? orderId + "," + nodeId : orderId + "," + pointId + "," + nodeId, client.GetSchemasHeader(new Uri("https://example.org/api/nodes")));
      Assert.AreEqual(orderId + "," + pointId + "," + nodeId, client.GetSchemasHeader(new Uri("https://example.org/unknown")));
      learn("https://example.org/api/points/17", point);
      Assert.AreEqual(byRoute ? orderId + "," + pointId + "," + nodeId : pointId + "," + orderId + "," + nodeId, client.GetSchemasHeader(new Uri("https://example.org/unknown")));

      // the server refers to the schema of the route
      Assert.IsTrue(server.Serialize(node, typeof(LsNode), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(new Uri("https://example.org/api/nodes"))).IsReference);
      Assert.IsTrue(server.Serialize(order, typeof(LsOrder), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(new Uri("https://example.org/api/orders/1"))).IsReference);
    }

    [TestMethod]
    public void SchemaIdParse()
    {
      byte[] bytes = { 0x0a, 0xbc, 0xde, 0xf0, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0, 0x01, 0x23, 0x45, 0xff };
      SchemaId id = new SchemaId(bytes);
      string hex = id.ToString();
      Assert.AreEqual("0abcdef0123456789abcdef0012345ff", hex);
      Assert.AreEqual(id, SchemaId.Parse(hex));
      Assert.AreEqual(id, SchemaId.Parse(hex.ToUpperInvariant()));
      CollectionAssert.AreEqual(bytes, SchemaId.Parse(hex).ToByteArray());
      Assert.AreEqual(id, SchemaId.Parse(" a" + hex.Substring(2)), "byte.Parse takes white space around a pair");
      Assert.ThrowsExactly<FormatException>(() => SchemaId.Parse(hex.Substring(1)));
      Assert.ThrowsExactly<FormatException>(() => SchemaId.Parse("g" + hex.Substring(1)));
      Assert.ThrowsExactly<FormatException>(() => SchemaId.Parse(hex.Substring(0, 31) + "-"));
    }
    [TestMethod]
    public void CacheAssemblyTypes()
    {
      LsMsgPackSerializer.CacheAssemblyTypes(typeof(LsNode));
      LsMsgPackSerializer.CacheAssemblyTypes(typeof(LsNode).Assembly); // again: nothing to do
      Assert.ThrowsExactly<ArgumentNullException>(() => LsMsgPackSerializer.CacheAssemblyTypes((Type)null));
      Assert.ThrowsExactly<ArgumentNullException>(() => LsMsgPackSerializer.CacheAssemblyTypes((System.Reflection.Assembly)null));
    }
  }
}
