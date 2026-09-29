using LsMsgPack;
using LtMsgPack;
using LtMsgPack.Extensions;
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
  }
}
