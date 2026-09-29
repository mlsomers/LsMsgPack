using LsMsgPack;
using LsMsgPack.Meta;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Extensions without a registered type (see <see cref="MsgPackSettings.CustomExtentionTypes"/>) are not converted to anything but their bytes (byte[]) or themselves (object).
  /// <para>Their bytes used to be converted like binary data, e.g. a Guid written by Nerdbank.MessagePack (extension type 2, big-endian) was silently read as a different Guid.</para>
  /// </summary>
  [TestClass]
  public class UnknownExtensions
  {
    // 0f8fad5b-d9cb-469f-a165-70867728950e in big-endian (RFC 4122) byte order, not the order of Guid.ToByteArray()
    private static readonly byte[] GuidBigEndian = { 0x0f, 0x8f, 0xad, 0x5b, 0xd9, 0xcb, 0x46, 0x9f, 0xa1, 0x65, 0x70, 0x86, 0x77, 0x28, 0x95, 0x0e };

    private static readonly MsgPackSettings WithoutSchema = new MsgPackSettings() { UseInexedSchema = false };

    public class WithUnknownExtension
    {
      public Guid Id { get; set; }
      public byte[] Raw { get; set; }
      public object Anything { get; set; }
      public List<object> Items { get; set; }
    }

    private static byte[] MapWith(string key, MsgPackItem value)
    {
      return new MpMap(new[] { new KeyValuePair<object, object>(key, value) }, new MsgPackSettings()).ToBytes();
    }

    private static MpExt Extension()
    {
      return new MpExt() { TypeSpecifier = 2, Value = GuidBigEndian };
    }

    [TestMethod]
    public void NotReadAsGuid()
    {
      MsgPackException ex = Assert.ThrowsExactly<MsgPackException>(() => MsgPackSerializer.Deserialize<WithUnknownExtension>(MapWith(nameof(WithUnknownExtension.Id), Extension()), WithoutSchema));
      StringAssert.Contains(ex.Message, "extension type 2");

      Assert.ThrowsExactly<MsgPackException>(() => MsgPackSerializer.Deserialize<Guid>(Extension().ToBytes(), WithoutSchema));
    }

    [TestMethod]
    public void BinaryIsStillReadAsGuid()
    {
      Guid id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
      byte[] bytes = MapWith(nameof(WithUnknownExtension.Id), new MpBin() { Value = id.ToByteArray() });
      Assert.AreEqual(id, MsgPackSerializer.Deserialize<WithUnknownExtension>(bytes, WithoutSchema).Id);
    }

    [TestMethod]
    public void ReadAsBytes()
    {
      WithUnknownExtension ret = MsgPackSerializer.Deserialize<WithUnknownExtension>(MapWith(nameof(WithUnknownExtension.Raw), Extension()), WithoutSchema);
      CollectionAssert.AreEqual(GuidBigEndian, ret.Raw);
    }

    /// <summary>
    /// Assigned to object the extension keeps its type, and is written back as the same extension.
    /// </summary>
    [TestMethod]
    public void ReadAsItselfAndWrittenBack()
    {
      byte[] bytes = MapWith(nameof(WithUnknownExtension.Anything), Extension());
      WithUnknownExtension ret = MsgPackSerializer.Deserialize<WithUnknownExtension>(bytes, WithoutSchema);

      MpExt ext = ret.Anything as MpExt;
      Assert.IsNotNull(ext);
      Assert.AreEqual((sbyte)2, ext.TypeSpecifier);
      CollectionAssert.AreEqual(GuidBigEndian, (byte[])ext.Value);

      WithUnknownExtension again = MsgPackSerializer.Deserialize<WithUnknownExtension>(MsgPackSerializer.Serialize(ret, WithoutSchema), WithoutSchema);
      Assert.AreEqual((sbyte)2, ((MpExt)again.Anything).TypeSpecifier);
      CollectionAssert.AreEqual(GuidBigEndian, (byte[])((MpExt)again.Anything).Value);

      Assert.IsInstanceOfType<MpExt>(MsgPackSerializer.Deserialize<object>(Extension().ToBytes(), WithoutSchema));
    }

    [TestMethod]
    public void InCollections()
    {
      MpArray array = new MpArray(new MsgPackSettings()) { Value = new object[] { 1, Extension(), "x" } };
      WithUnknownExtension ret = MsgPackSerializer.Deserialize<WithUnknownExtension>(MapWith(nameof(WithUnknownExtension.Items), array), WithoutSchema);

      Assert.HasCount(3, ret.Items);
      Assert.AreEqual((sbyte)2, ((MpExt)ret.Items[1]).TypeSpecifier);

      object[] raw = (object[])MsgPackItem.Unpack(array.ToBytes()).Value;
      Assert.IsInstanceOfType<MpExt>(raw[1]);
    }

    /// <summary>
    /// A registered extension type is converted as before (the decimal extension is registered by default).
    /// </summary>
    [TestMethod]
    public void RegisteredExtensionsAreConverted()
    {
      object[] decimals = (object[])MsgPackItem.Unpack(new MpArray(new MsgPackSettings()) { Value = new decimal[] { 1.5m } }.ToBytes()).Value;
      Assert.AreEqual(1.5m, decimals[0]);

      object[] dates = (object[])MsgPackItem.Unpack(new MpArray(new MsgPackSettings()) { Value = new DateTime[] { DateTime.UnixEpoch } }.ToBytes()).Value;
      Assert.IsInstanceOfType<DateTime>(dates[0]);
    }
  }
}
