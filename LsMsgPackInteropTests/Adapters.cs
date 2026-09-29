using LsMsgPack;
using LsMsgPack.Types.Extensions;
using MessagePack;
using MessagePack.Formatters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace LsMsgPackInteropTests
{
  /// <summary>
  /// Lets MessagePack-CSharp read (and write) LsMsgPack's decimal: extension type 1 with the 16 bytes of System.Decimal (little-endian).
  /// </summary>
  public sealed class LsMsgPackDecimalFormatter : IMessagePackFormatter<decimal>
  {
    public static readonly LsMsgPackDecimalFormatter Instance = new LsMsgPackDecimalFormatter();

    public void Serialize(ref MessagePackWriter writer, decimal value, MessagePackSerializerOptions options)
    {
      byte[] bytes = new byte[16];
      MemoryMarshal.Write(bytes, in value);
      writer.WriteExtensionFormat(new ExtensionResult(1, bytes));
    }

    public decimal Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
      ExtensionResult ext = reader.ReadExtensionFormat();
      if (ext.TypeCode != 1 || ext.Data.Length != 16)
        throw new MessagePackSerializationException($"Expected an LsMsgPack decimal (extension type 1, 16 bytes) but found extension type {ext.TypeCode} ({ext.Data.Length} bytes)");
      return MemoryMarshal.Read<decimal>(ext.Data.ToArray());
    }
  }

  /// <summary>
  /// Lets LsMsgPack read the Guids Nerdbank.MessagePack writes: extension type 2 with the bytes in big-endian (RFC 4122) order.
  /// <para>LsMsgPack itself keeps writing a Guid as bin 16 (the order of Guid.ToByteArray()), a custom extension is only used for reading them.</para>
  /// </summary>
  public class NerdbankGuidExtension : BaseCustomExt<NerdbankGuidExtension, Guid>
  {
    public NerdbankGuidExtension() : base() { }

    public NerdbankGuidExtension(MsgPackSettings settings) : base(settings) { }

    protected override sbyte DefaultTypeSpecifier { get { return 2; } }

    public override Guid FromBytes(byte[] bytes)
    {
      return new Guid(bytes, bigEndian: true);
    }

    public override byte[] GetBytes(Guid item)
    {
      byte[] bytes = new byte[16];
      item.TryWriteBytes(bytes, bigEndian: true, out _);
      return bytes;
    }
  }

  internal static class Same
  {
    private static readonly JsonSerializerSettings Utc = new JsonSerializerSettings() { DateTimeZoneHandling = DateTimeZoneHandling.Utc };

    /// <summary>
    /// Compares as JSON with the dates in UTC: MsgPack timestamps do not keep the DateTimeKind (LsMsgPack returns local time, the others UTC).
    /// </summary>
    public static void AssertEqual(object expected, object actual, string message = "")
    {
      Assert.AreEqual(JsonConvert.SerializeObject(expected, Utc), JsonConvert.SerializeObject(actual, Utc), message);
    }
  }

  [TestClass]
  public static class LtEquivalents
  {
    [AssemblyInitialize]
    public static void Register(TestContext context)
    {
      LsMsgPackUnitTests.LtSerializer.ExtensionEquivalents.Add(ext => ext is NerdbankGuidExtension ? new LtMsgPack.Extensions.NerdbankGuidExtension() : null); // LtMsgPack has it built in
    }
  }
}
