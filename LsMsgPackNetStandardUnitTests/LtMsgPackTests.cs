using LsMsgPack;
using LtMsgPack;
using LtMsgPack.Extensions;
using LtMsgPack.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// What LtMsgPack does beyond LsMsgPack: limits for untrusted data, cycles, custom extensions.
  /// </summary>
  [TestClass]
  public class LtMsgPackTests
  {
    public class LtNode
    {
      public string Name { get; set; }
      public LtNode Next { get; set; }
    }

    public struct LtPoint
    {
      public int X;
      public int Y;
    }

    public class LtWithPoint
    {
      public LtPoint Point { get; set; }
      public object Anything { get; set; }
    }

    public sealed class PointExtension : LtExtension<LtPoint>
    {
      public override sbyte TypeCode { get { return 42; } }
      public override int GetMaxLength(LtPoint value) { return 8; }
      public override int Write(LtPoint value, Span<byte> destination)
      {
        BitConverter.TryWriteBytes(destination, value.X);
        BitConverter.TryWriteBytes(destination.Slice(4), value.Y);
        return 8;
      }
      public override LtPoint Read(ReadOnlySpan<byte> data)
      {
        return new LtPoint { X = BitConverter.ToInt32(data), Y = BitConverter.ToInt32(data.Slice(4)) };
      }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CycleIsRefused(bool schema)
    {
      LtNode node = new LtNode { Name = "loop" };
      node.Next = node;
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = schema });
      Assert.ThrowsExactly<MsgPackException>(() => lt.Serialize(node));
    }

    [TestMethod]
    public void DeepNestingIsRefused()
    {
      byte[] data = new byte[1000];
      for (int t = 0; t < data.Length; t++)
        data[t] = 0x91; // an array holding an array holding ...
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false });
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<object>(data));
    }

    [TestMethod]
    public void ClaimedLengthsAreChecked()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false });
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<object>(new byte[] { 0xDD, 0x7F, 0xFF, 0xFF, 0xFF })); // an array of 2^31 items in 5 bytes
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<List<int>>(new byte[] { 0xDD, 0x7F, 0xFF, 0xFF, 0xFF }));
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<string>(new byte[] { 0xDB, 0x7F, 0xFF, 0xFF, 0xFF, 0x41 }));
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<LtNode>(new byte[] { 0x82, 0xA4, (byte)'N', (byte)'a' })); // truncated
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CustomExtension(bool schema)
    {
      LtMsgPackOptions options = new LtMsgPackOptions { UseInexedSchema = schema };
      options.Extensions = new LtExtension[] { new DecimalExtension(), new PointExtension() };
      LtMsgPackSerializer lt = new LtMsgPackSerializer(options);

      LtWithPoint value = new LtWithPoint { Point = new LtPoint { X = 3, Y = -4 }, Anything = new LtPoint { X = 5, Y = 6 } };
      byte[] bytes = lt.Serialize(value);
      LtWithPoint back = lt.Deserialize<LtWithPoint>(bytes);
      Assert.AreEqual(3, back.Point.X);
      Assert.AreEqual(-4, back.Point.Y);
      Assert.AreEqual(6, ((LtPoint)back.Anything).Y); // the extension is read back as the point, also into object

      // Without the extension it cannot be read as the point (as in LsMsgPack)
      LtMsgPackSerializer without = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = schema });
      Assert.ThrowsExactly<MsgPackException>(() => without.Deserialize<LtWithPoint>(bytes));
    }

    [TestMethod]
    public void UnknownExtensionInObjectMember()
    {
      // As another library writes it: an extension without a type id
      byte[] data = new MpMap(new[] { new KeyValuePair<object, object>("Anything", new MpExt { TypeSpecifier = 42, Value = new byte[] { 1, 2, 3 } }) }, new MsgPackSettings()).ToBytes();
      LtWithPoint read = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false }).Deserialize<LtWithPoint>(data);
      MsgPackExtension extension = (MsgPackExtension)read.Anything;
      Assert.AreEqual((sbyte)42, extension.TypeCode);
      CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, extension.Data);
    }

    [TestMethod]
    public void OptionsAreCopied()
    {
      LtMsgPackOptions options = new LtMsgPackOptions { UseInexedSchema = false };
      LtMsgPackSerializer lt = new LtMsgPackSerializer(options);
      options.UseInexedSchema = true; // too late for this serializer
      Assert.IsFalse(lt.Options.UseInexedSchema);
      Assert.AreEqual((byte)0x81, lt.Serialize(new LtNode { Name = "x" })[0]); // a map, no schema
    }

    [TestMethod]
    public void ThreadsShareOneSerializer()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { SchemaStore = new SchemaStore(), WriteSchemaReference = true });
      int failures = 0;
      System.Threading.Tasks.Parallel.For(0, 2000, i =>
      {
        LtNode node = new LtNode { Name = "n" + i, Next = i % 2 == 0 ? new LtNode { Name = "next" } : null };
        LtNode back = lt.Deserialize<LtNode>(lt.Serialize(node));
        if (back.Name != node.Name || (back.Next?.Name ?? "") != (node.Next?.Name ?? ""))
          System.Threading.Interlocked.Increment(ref failures);
      });
      Assert.AreEqual(0, failures);
    }
    private static readonly LtMsgPackSerializer Names = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false });

    public class LtFormats
    {
      public Guid Id { get; set; }
      public decimal Amount { get; set; }
      public DateTimeOffset When { get; set; }
      public DateTime Created { get; set; }
    }

    [TestMethod]
    public void GuidAsString()
    {
      LtMsgPackOptions options = new LtMsgPackOptions { UseInexedSchema = false, GuidFormat = GuidFormat.String };
      LtMsgPackSerializer lt = new LtMsgPackSerializer(options);
      Guid id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

      byte[] bytes = lt.Serialize(id);
      CollectionAssert.AreEqual(new byte[] { 0xD9, 36 }, new[] { bytes[0], bytes[1] });
      Assert.AreEqual(id, lt.Deserialize<Guid>(bytes));
      Assert.AreEqual(id, lt.Deserialize<Guid>(Names.Serialize(id)), "bin 16 is read as well");
      Assert.ThrowsExactly<MsgPackException>(() => lt.Deserialize<Guid>(lt.Serialize("not a guid")));
    }

    [TestMethod]
    public void DecimalAsString()
    {
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, DecimalFormat = DecimalFormat.String });
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
      LtMsgPackSerializer lt = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, DateTimeOffsetFormat = DateTimeOffsetFormat.ClockTimeAndOffset });
      DateTimeOffset when = new DateTimeOffset(2026, 9, 29, 14, 30, 15, 250, TimeSpan.FromMinutes(-150));

      byte[] bytes = lt.Serialize(when);
      Assert.AreEqual((byte)0x92, bytes[0]);
      DateTimeOffset back = lt.Deserialize<DateTimeOffset>(bytes);
      Assert.AreEqual(when, back);
      Assert.AreEqual(when.Offset, back.Offset);
      Assert.AreEqual(when, lt.Deserialize<DateTimeOffset>(Names.Serialize(when)), "a timestamp is read as well");

      LtFormats formats = lt.Deserialize<LtFormats>(lt.Serialize(new LtFormats { When = when }));
      Assert.AreEqual(when.Offset, formats.When.Offset);
    }

    [TestMethod]
    public void UnspecifiedAsUtc()
    {
      DateTime unspecified = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Unspecified);
      LtMsgPackSerializer utc = new LtMsgPackSerializer(new LtMsgPackOptions { UseInexedSchema = false, UnspecifiedDateTimeKind = DateTimeKind.Utc });
      CollectionAssert.AreEqual(Names.Serialize(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc)), utc.Serialize(unspecified));
      CollectionAssert.AreEqual(Names.Serialize(DateTime.SpecifyKind(unspecified, DateTimeKind.Local)), Names.Serialize(unspecified), "the default: local time, as LsMsgPack");
    }

    [TestMethod]
    public void NerdbankGuidExtensionReadsBigEndian()
    {
      Guid id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
      byte[] data = new byte[] { 0xD8, 2, 0x0f, 0x8f, 0xad, 0x5b, 0xd9, 0xcb, 0x46, 0x9f, 0xa1, 0x65, 0x70, 0x86, 0x77, 0x28, 0x95, 0x0e };
      Assert.AreEqual(id, new LtMsgPackSerializer(LtMsgPackPresets.Nerdbank()).Deserialize<Guid>(data));
    }

    public class LtOrder
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
      LtMsgPackHttpOptions options = new LtMsgPackHttpOptions();
      options.XLsMsgPack.EndianAction = endian;
      LtMsgPackHttpSerializer server = new LtMsgPackHttpSerializer(options);
      LtMsgPackHttpSerializer client = new LtMsgPackHttpSerializer(options);
      Uri uri = new Uri("https://example.org/api/orders/1");
      LtOrder order = new LtOrder { Number = 1, Customer = "Infotopie", Remarks = "none" };

      MsgPackPayload inline = server.Serialize(order, typeof(LtOrder), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(uri));
      Assert.IsFalse(inline.IsReference);
      byte[] inlineBytes = inline.ToArray();
      CollectionAssert.AreEqual(new LtMsgPackSerializer(new LtMsgPackOptions { EndianAction = endian }).Serialize(order), inlineBytes, "the same as without negotiation");
      Assert.IsTrue(client.LearnSchema(uri, inline.SchemaId, inlineBytes, 0, inlineBytes.Length, MsgPackMediaTypes.XLsMsgPack));
      Assert.AreEqual(inline.SchemaId, client.GetSchemasHeader(new Uri("https://example.org/other")), "per server, not per path");
      Assert.IsNull(client.GetSchemasHeader(new Uri("https://example.com/")));

      MsgPackPayload reference = server.Serialize(order, typeof(LtOrder), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(uri));
      Assert.IsTrue(reference.IsReference);
      byte[] referenceBytes = reference.ToArray();
      Assert.AreEqual(SchemaStore.ReferenceLength + inline.Body.Count, referenceBytes.Length);
      Assert.AreEqual("Infotopie", ((LtOrder)client.Deserialize(typeof(LtOrder), referenceBytes, 0, referenceBytes.Length, MsgPackMediaTypes.XLsMsgPack)).Customer);

      // A header that claims another schema is not learned
      LtMsgPackHttpSerializer other = new LtMsgPackHttpSerializer(options);
      Assert.IsFalse(other.LearnSchema(uri, "00112233445566778899aabbccddeeff", inlineBytes, 0, inlineBytes.Length, MsgPackMediaTypes.XLsMsgPack));
      Assert.IsFalse(other.LearnSchema(uri, "not an id", inlineBytes, 0, inlineBytes.Length, MsgPackMediaTypes.XLsMsgPack));
      Assert.IsNull(other.GetSchemasHeader(uri));

      // Plain media types and null are not negotiated
      Assert.IsNull(server.Serialize(order, typeof(LtOrder), MsgPackMediaTypes.MsgPack, client.GetSchemasHeader(uri)).SchemaId);
      CollectionAssert.AreEqual(new byte[] { 0xC0 }, server.Serialize(null, typeof(LtOrder), MsgPackMediaTypes.XLsMsgPack, client.GetSchemasHeader(uri)).ToArray());
    }

    [TestMethod]
    public void HttpNegotiationAdvertisesTheMostRecentSchemas()
    {
      LtMsgPackHttpOptions options = new LtMsgPackHttpOptions() { MaxAdvertisedSchemas = 2 };
      LtMsgPackHttpSerializer server = new LtMsgPackHttpSerializer(options);
      LtMsgPackHttpSerializer client = new LtMsgPackHttpSerializer(options);
      Uri uri = new Uri("https://example.org/");

      string[] ids = new string[3];
      object[] values = { new LtOrder { Number = 1, Customer = "a", Remarks = "b" }, new LtNode { Name = "node", Next = new LtNode() }, new LtWithPoint { Anything = "x" } };
      for (int t = 0; t < values.Length; t++)
      {
        MsgPackPayload payload = server.Serialize(values[t], values[t].GetType(), MsgPackMediaTypes.XLsMsgPack, null);
        byte[] bytes = payload.ToArray();
        Assert.IsTrue(client.LearnSchema(uri, payload.SchemaId, bytes, 0, bytes.Length, MsgPackMediaTypes.XLsMsgPack));
        ids[t] = payload.SchemaId;
      }
      Assert.AreEqual(ids[2] + "," + ids[1], client.GetSchemasHeader(uri));
    }
  }
}
