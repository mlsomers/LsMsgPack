using LsMsgPack;
using LsMsgPackUnitTests;
using LsMsgPack.TypeResolving.Interfaces;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace LsMsgPackInteropTests
{
  /// <summary>
  /// What MessagePack-CSharp reads of LsMsgPack's output, and the other way around.
  /// <para>MsgPack standardizes the value types and their bytes (and the timestamp extension), not how objects are mapped onto them, so each library has its own conventions:</para>
  /// <list type="bullet">
  /// <item>Objects: LsMsgPack writes maps keyed by property names, or by indexes into a schema that precedes them (<see cref="MsgPackSettings.UseInexedSchema"/>, the default).
  /// MessagePack-CSharp writes arrays ([Key(int)], its recommendation) or maps keyed by names (contractless or [Key(string)]). LsMsgPack reads and writes such arrays with <see cref="ObjectLayout.Array"/> in the same order.</item>
  /// <item>Guid: LsMsgPack bin 16 in the order of Guid.ToByteArray(), MessagePack-CSharp a string (NativeGuidResolver writes the same bytes as LsMsgPack).</item>
  /// <item>decimal: LsMsgPack extension type 1 (the 16 bytes of System.Decimal), MessagePack-CSharp a string (LsMsgPack reads that).</item>
  /// <item>Polymorphism: LsMsgPack a type id in the object's map (key ""), MessagePack-CSharp [Union]: a two element array [key, object].</item>
  /// </list>
  /// </summary>
  public abstract class MessagePackCSharpTests
  {
    protected abstract LsMsgPackUnitTests.ISerializerUnderTest Serializer { get; }

    private static readonly MsgPackSettings Indexed = new MsgPackSettings() { UseInexedSchema = true };
    private static readonly MsgPackSettings Named = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }; // contractless reads maps keyed by names

    private static readonly MessagePackSerializerOptions Standard = MessagePackSerializerOptions.Standard;
    private static readonly MessagePackSerializerOptions Contractless = ContractlessStandardResolver.Options;

    /// <summary>
    /// Guid as bin 16 in the order of Guid.ToByteArray(), like LsMsgPack
    /// </summary>
    private static readonly MessagePackSerializerOptions ContractlessBinaryGuid = Standard.WithResolver(
      CompositeResolver.Create(NativeGuidResolver.Instance, ContractlessStandardResolver.Instance));

    /// <summary>
    /// Reads what LsMsgPack writes for the invoices (property names)
    /// </summary>
    private static readonly MessagePackSerializerOptions ContractlessLsMsgPack = Standard.WithResolver(CompositeResolver.Create(
      new IMessagePackFormatter[] { LsMsgPackDecimalFormatter.Instance },
      new IFormatterResolver[] { NativeGuidResolver.Instance, ContractlessStandardResolver.Instance }));

    /// <summary>
    /// Integer keys ([Key(n)], arrays), Guids and decimals as LsMsgPack writes them
    /// </summary>
    private static readonly MessagePackSerializerOptions StandardLsMsgPack = Standard.WithResolver(CompositeResolver.Create(
      new IMessagePackFormatter[] { LsMsgPackDecimalFormatter.Instance },
      new IFormatterResolver[] { NativeGuidResolver.Instance, StandardResolver.Instance }));

    /// <summary>
    /// What MessagePack-CSharp writes for integer keys: arrays in the order of the keys (here the declaration order), every value, no type ids
    /// </summary>
    private static readonly MsgPackSettings Positional = new MsgPackSettings()
    {
      UseInexedSchema = false,
      ObjectLayout = ObjectLayout.Array,
      PropertyOrder = PropertyOrder.Declaration,
      AddTypeIdOptions = AddTypeIdOption.Never,
      DynamicFilters = new IMsgPackPropertyIncludeDynamically[0]
    };

    private static readonly Invoice[] AllInvoices = Invoices.Create(100);

    [TestMethod]
    public void LsMsgPackPropertyNames_ReadByMessagePackCSharp()
    {
      foreach (Invoice invoice in AllInvoices)
        Same.AssertEqual(invoice, MessagePackSerializer.Deserialize<Invoice>(Serializer.Serialize(invoice, Named), ContractlessLsMsgPack), invoice.InvoiceNumber);
    }

    [TestMethod]
    public void MessagePackCSharpMap_ReadByLsMsgPack()
    {
      foreach (Invoice invoice in AllInvoices)
        Same.AssertEqual(invoice, Serializer.Deserialize<Invoice>(MessagePackSerializer.Serialize(invoice, ContractlessBinaryGuid), Named), invoice.InvoiceNumber);
    }

    [TestMethod]
    public void DefaultSettings_GuidAndDecimalDiffer()
    {
      Invoice invoice = AllInvoices[1];

      // Guid: bin in LsMsgPack, a string in MessagePack-CSharp
      Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<Invoice>(Serializer.Serialize(invoice, Named), Contractless));
      Assert.Throws<Exception>(() => Serializer.Deserialize<Invoice>(MessagePackSerializer.Serialize(invoice, Contractless), Named));

      // decimal: an extension in LsMsgPack, MessagePack-CSharp only reads strings
      Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<Invoice>(Serializer.Serialize(invoice, Named), ContractlessBinaryGuid));
    }

    /// <summary>
    /// The indexed schema (LsMsgPack's default) is a map of type names with their property names, followed by the object in which the property keys are indexes into it.
    /// Other libraries read one object: the schema (MessagePack-CSharp does not complain about the rest).
    /// </summary>
    [TestMethod]
    public void IndexedSchema_NotReadable()
    {
      byte[] bytes = Serializer.Serialize(AllInvoices[1], Indexed);
      Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<Invoice>(bytes, ContractlessLsMsgPack));

      MessagePackSerializer.Deserialize<object>(bytes, Contractless, out int bytesRead);
      Assert.IsTrue(bytesRead < bytes.Length, $"{bytesRead} of {bytes.Length} bytes read");

      // and LsMsgPack expects a schema by default
      Assert.Throws<MsgPackException>(() => Serializer.Deserialize<Invoice>(MessagePackSerializer.Serialize(AllInvoices[1], ContractlessBinaryGuid), Indexed));
    }

    /// <summary>
    /// MessagePack-CSharp's recommended layout (integer keys) writes objects as arrays, LsMsgPack does so with <see cref="ObjectLayout.Array"/> in the same order: the same bytes.
    /// </summary>
    [TestMethod]
    public void IntegerKeys_SameBytesAsArrays()
    {
      foreach (Invoice invoice in AllInvoices)
      {
        InvoiceK keyed = JsonConvert.DeserializeObject<InvoiceK>(JsonConvert.SerializeObject(invoice));
        byte[] mp = MessagePackSerializer.Serialize(keyed, StandardLsMsgPack);

        Same.AssertEqual(keyed, Serializer.Deserialize<InvoiceK>(mp, Positional), $"{invoice.InvoiceNumber} read by LsMsgPack");
        byte[] ls = Serializer.Serialize(keyed, Positional);
        Same.AssertEqual(keyed, MessagePackSerializer.Deserialize<InvoiceK>(ls, StandardLsMsgPack), $"{invoice.InvoiceNumber} read by MessagePack-CSharp");
        CollectionAssert.AreEqual(mp, ls, invoice.InvoiceNumber);
      }
    }

    [TestMethod]
    public void SameBytes_Primitives()
    {
      AssertSameBytes(5);
      AssertSameBytes(-32);
      AssertSameBytes(-33);
      AssertSameBytes(1_000_000_000_000L);
      AssertSameBytes(int.MinValue);
      AssertSameBytes(ulong.MaxValue);
      AssertSameBytes(0.1d);
      AssertSameBytes(1.5f);
      AssertSameBytes(true);
      AssertSameBytes(new string('x', 40));
      AssertSameBytes("héllo wörld ✓");
      AssertSameBytes(new byte[] { 1, 2, 3 });
      AssertSameBytes(UnitOfMeasure.Liter);
      AssertSameBytes(new[] { 1, 2, 3 });
      AssertSameBytes(new List<string>() { "a", "b" });
      AssertSameBytes(new Dictionary<string, int>() { { "a", 1 }, { "b", 2 } });
    }

    [TestMethod]
    public void SameBytes_DateTime()
    {
      AssertSameBytes(new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc)); // timestamp 32
      AssertSameBytes(new DateTime(2026, 3, 1, 10, 0, 0, 123, DateTimeKind.Utc)); // timestamp 64
      AssertSameBytes(new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Local));
      AssertSameBytes(new DateTime(1969, 12, 31, 23, 59, 58, 500, DateTimeKind.Utc)); // timestamp 96, the seconds are rounded down before 1970
      AssertSameBytes(new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc));
      AssertSameBytes(new DateTime(2600, 1, 1, 0, 0, 0, 1, DateTimeKind.Utc));
    }

    [TestMethod]
    public void SameBytes_FrameworkTypes()
    {
      AssertSameBytes('A');
      AssertSameBytes('€');
      AssertSameBytes(TimeSpan.FromMinutes(90));
      AssertSameBytes(TimeSpan.FromTicks(-12345));
      AssertSameBytes(new DateOnly(2026, 9, 29));
      AssertSameBytes(new TimeOnly(13, 45, 10, 5));
      AssertSameBytes(new Uri("https://example.com/x?y=1"));
      AssertSameBytes(new Uri("relative/path", UriKind.Relative));
    }

    private void AssertSameBytes<T>(T value)
    {
      byte[] ls = Serializer.Serialize(value, Named);
      byte[] mp = MessagePackSerializer.Serialize(value, Standard);
      CollectionAssert.AreEqual(mp, ls, $"{typeof(T).Name} {value}: LsMsgPack {Convert.ToHexString(ls)}, MessagePack-CSharp {Convert.ToHexString(mp)}");

      Same.AssertEqual(value, Serializer.Deserialize<T>(mp, Named), $"{typeof(T).Name} {value} read by LsMsgPack");
      Same.AssertEqual(value, MessagePackSerializer.Deserialize<T>(ls, Standard), $"{typeof(T).Name} {value} read by MessagePack-CSharp");
    }

    [TestMethod]
    public void Guid_BinaryInLsMsgPack_StringInMessagePackCSharp()
    {
      Guid id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
      byte[] ls = Serializer.Serialize(id, Named);

      CollectionAssert.AreEqual(Convert.FromHexString("C41033221100554477668899AABBCCDDEEFF"), ls); // bin 8, Guid.ToByteArray()
      Assert.AreEqual(id.ToString(), MessagePackSerializer.Deserialize<string>(MessagePackSerializer.Serialize(id, Standard), Standard));
      CollectionAssert.AreEqual(ls, MessagePackSerializer.Serialize(id, ContractlessBinaryGuid));

      Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<Guid>(ls, Standard));
      Assert.Throws<Exception>(() => Serializer.Deserialize<Guid>(MessagePackSerializer.Serialize(id, Standard), Named));
    }

    [TestMethod]
    public void Decimal_ExtensionInLsMsgPack_StringInMessagePackCSharp()
    {
      byte[] ls = Serializer.Serialize(1234.50m, Named);
      byte[] mp = MessagePackSerializer.Serialize(1234.50m, Standard);

      CollectionAssert.AreEqual(Convert.FromHexString("D80100000200000000003AE2010000000000"), ls); // fixext 16, type 1, the bytes of System.Decimal
      Assert.AreEqual("1234.50", MessagePackSerializer.Deserialize<string>(mp, Standard));

      Assert.AreEqual(1234.50m, Serializer.Deserialize<decimal>(mp, Named)); // LsMsgPack parses the string
      Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<decimal>(ls, Standard));
      Assert.AreEqual(1234.50m, MessagePackSerializer.Deserialize<decimal>(ls, ContractlessLsMsgPack));
    }

    /// <summary>
    /// LsMsgPack treats DateTimeKind.Unspecified as local time (like DateTime.ToUniversalTime), MessagePack-CSharp as UTC.
    /// The same value is a different moment unless the machine's time zone is UTC.
    /// </summary>
    [TestMethod]
    public void DateTimeUnspecified_LocalInLsMsgPack_UtcInMessagePackCSharp()
    {
      DateTime unspecified = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Unspecified);

      CollectionAssert.AreEqual(MessagePackSerializer.Serialize(DateTime.SpecifyKind(unspecified, DateTimeKind.Local), Standard), Serializer.Serialize(unspecified, Named));
      CollectionAssert.AreEqual(MessagePackSerializer.Serialize(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc), Standard), MessagePackSerializer.Serialize(unspecified, Standard));
    }

    /// <summary>
    /// LsMsgPack writes the moment as a timestamp (the offset is lost), MessagePack-CSharp an array of the local time (as if it were UTC) and the offset in minutes.
    /// </summary>
    [TestMethod]
    public void DateTimeOffset_Incompatible()
    {
      DateTimeOffset when = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(2));

      Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<DateTimeOffset>(Serializer.Serialize(when, Named), Standard));
      Assert.Throws<Exception>(() => Serializer.Deserialize<DateTimeOffset>(MessagePackSerializer.Serialize(when, Standard), Named));
    }

    [TestMethod]
    public void Polymorphism_Incompatible()
    {
      MsgPackSerializer.CacheAssemblyTypes(typeof(Animal));
      Zoo zoo = new Zoo() { Animals = new List<Animal>() { new Dog() { Name = "Rex", Barks = 3 }, new Cat() { Name = "Tom", Indoor = true } } };
      byte[] ls = Serializer.Serialize(zoo, Named); // { "Animals": [{ "": "Dog", "Barks": 3, "Name": "Rex" }, ...] }
      byte[] mp = MessagePackSerializer.Serialize(zoo, Contractless); // { "Animals": [[0, { "Name": "Rex", "Barks": 3 }], ...] }

      Same.AssertEqual(zoo, Serializer.Deserialize<Zoo>(ls, Named));
      Same.AssertEqual(zoo, MessagePackSerializer.Deserialize<Zoo>(mp, Contractless));

      Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<Zoo>(ls, Contractless));
      Assert.Throws<Exception>(() => Serializer.Deserialize<Zoo>(mp, Named));
    }

    /// <summary>
    /// LsMsgPack omits values that equal the default of their type (FilterDefaultValues), a reader then keeps what the constructor set.
    /// An empty string is not a default value (the default of a string is null), it is written as in the JSON serializers.
    /// MessagePack-CSharp writes every value.
    /// </summary>
    [TestMethod]
    public void DefaultValues_OmittedByLsMsgPack()
    {
      DefaultsProbe probe = new DefaultsProbe() { Text = "", Retries = 0, Enabled = false, Items = null };

      DefaultsProbe read = MessagePackSerializer.Deserialize<DefaultsProbe>(Serializer.Serialize(probe, Named), Contractless);
      Assert.AreEqual("", read.Text);
      Assert.AreEqual(3, read.Retries);
      Assert.IsTrue(read.Enabled);
      Assert.IsNotNull(read.Items);

      Same.AssertEqual(probe, Serializer.Deserialize<DefaultsProbe>(MessagePackSerializer.Serialize(probe, Contractless), Named));
    }

    /// <summary>
    /// Without the dynamic filters LsMsgPack writes every value (null, "", 0 and false too), then it writes the same bytes as MessagePack-CSharp.
    /// </summary>
    [TestMethod]
    public void DefaultValues_WrittenWithoutDynamicFilters()
    {
      MsgPackSettings everything = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0] };
      DefaultsProbe probe = new DefaultsProbe() { Text = "", Retries = 0, Enabled = false, Items = null };
      byte[] bytes = Serializer.Serialize(probe, everything);

      CollectionAssert.AreEqual(MessagePackSerializer.Serialize(probe, Contractless), bytes);
      Same.AssertEqual(probe, MessagePackSerializer.Deserialize<DefaultsProbe>(bytes, Contractless));
      Same.AssertEqual(probe, Serializer.Deserialize<DefaultsProbe>(bytes, everything));
    }

    public class WithObjectMember
    {
      public string Name { get; set; }
      public object When { get; set; }
    }

    /// <summary>
    /// A value assigned to object gets a type id: { "": "DateTime", "@": timestamp }, other libraries read that map as the value.
    /// With <see cref="AddTypeIdOption.Never"/> only the value is written.
    /// </summary>
    [TestMethod]
    public void TypeIds_NotWrittenWithNever()
    {
      WithObjectMember item = new WithObjectMember() { Name = "x", When = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

      object withTypeId = MessagePackSerializer.Deserialize<WithObjectMember>(Serializer.Serialize(item, Named), Contractless).When;
      Assert.IsInstanceOfType<IDictionary<object, object>>(withTypeId);

      MsgPackSettings noTypeIds = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, AddTypeIdOptions = AddTypeIdOption.Never };
      Assert.AreEqual(item.When, MessagePackSerializer.Deserialize<WithObjectMember>(Serializer.Serialize(item, noTypeIds), Contractless).When);
    }
  }

  [TestClass]
  public class LsMessagePackCSharpTests : MessagePackCSharpTests
  {
    protected override LsMsgPackUnitTests.ISerializerUnderTest Serializer { get { return LsMsgPackUnitTests.Serializers.Ls; } }
  }

  [TestClass]
  public class LtMessagePackCSharpTests : MessagePackCSharpTests
  {
    protected override LsMsgPackUnitTests.ISerializerUnderTest Serializer { get { return LsMsgPackUnitTests.Serializers.Lt; } }
  }
}
