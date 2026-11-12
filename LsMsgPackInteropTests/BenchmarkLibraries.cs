using LsMsgPack;
using LsMsgPackDebug;
using LsMsgPackUnitTests;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyType;
using PolyType.ReflectionProvider;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using NB = Nerdbank.MessagePack;

namespace LsMsgPackInteropTests
{
  /// <summary>
  /// The invoices with string keys, MessagePack-CSharp's way to write maps keyed by property names with its source generator.
  /// </summary>
  [MessagePackObject(keyAsPropertyName: true)]
  public class AddressN
  {
    public string Street { get; set; }
    public string HouseNumber { get; set; }
    public string PostalCode { get; set; }
    public string City { get; set; }
    public string Region { get; set; }
    public string CountryCode { get; set; }
  }

  [MessagePackObject(keyAsPropertyName: true)]
  public class CustomerN
  {
    public int CustomerId { get; set; }
    public string CompanyName { get; set; }
    public string ContactName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string VatNumber { get; set; }
    public AddressN BillingAddress { get; set; }
    public AddressN ShippingAddress { get; set; }
  }

  [MessagePackObject(keyAsPropertyName: true)]
  public class InvoiceLineN
  {
    public int LineNumber { get; set; }
    public string ProductCode { get; set; }
    public string Description { get; set; }
    public decimal Quantity { get; set; }
    public UnitOfMeasure Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal VatPercentage { get; set; }
    public decimal LineTotal { get; set; }
  }

  [MessagePackObject(keyAsPropertyName: true)]
  public class InvoiceN
  {
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public InvoiceStatus Status { get; set; }
    public string Currency { get; set; }
    public string Reference { get; set; }
    public string Notes { get; set; }
    public CustomerN Customer { get; set; }
    public List<InvoiceLineN> Lines { get; set; }
    public decimal SubTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
  }

  /// <summary>
  /// Nerdbank.MessagePack's shapes of the plain model, written by PolyType's source generator.
  /// </summary>
  [GenerateShapeFor<Invoice>]
  [GenerateShapeFor<Address>]
  public partial class BenchmarkShapes
  {
  }

  /// <summary>
  /// Compares LsMsgPack with MessagePack-CSharp and Nerdbank.MessagePack on the invoices of BenchmarkInvoices (System.Text.Json is the baseline), each in its fastest documented setup:
  /// <list type="bullet">
  /// <item>MessagePack-CSharp: [MessagePackObject] types with the formatters of its source generator (GeneratedMessagePackResolver), integer keys (arrays, its recommendation) or keyAsPropertyName (maps).
  /// The "native" row also takes NativeGuidResolver and NativeDecimalResolver (binary instead of strings, what LsMsgPack writes).</item>
  /// <item>Nerdbank.MessagePack: the shapes of PolyType's source generator, PerfOverSchemaStability for arrays, its defaults for maps.</item>
  /// <item>LsMsgPack: the plain classes (no attributes), with the runtime's plans and with the code of its source generator (TestMsgPackContext).</item>
  /// </list>
  /// The "no attributes" rows are what each library does with plain classes without any setup: MessagePack-CSharp contractless (code emitted at runtime), Nerdbank with PolyType's reflection provider.
  /// <para>Results are written to the test output (docs/Comparison.md has a run), timings are not asserted.</para>
  /// </summary>
  [TestClass]
  [TestCategory("Benchmark")]
  public class BenchmarkLibraries
  {
    public TestContext TestContext { get; set; }

    private const int InvoiceCount = 100;
    private const int Rounds = 20;
    private const int Passes = 7;
    private const int WarmUpMilliseconds = 500;
    private const int SmallCalls = 100_000;

    private interface ICandidate
    {
      string Name { get; }

      /// <summary>Asserts the round trip of every invoice and the address, keeps the payloads, returns their total size</summary>
      long Prepare();

      int SmallSize { get; }

      /// <summary>The payloads of <see cref="Prepare"/></summary>
      byte[][] Payloads { get; }

      /// <summary>Serializes all invoices <see cref="Rounds"/> times</summary>
      TimeSpan Write();

      /// <summary>Deserializes all payloads <see cref="Rounds"/> times</summary>
      TimeSpan Read();

      /// <summary>Serializes or deserializes the address <see cref="SmallCalls"/> times</summary>
      TimeSpan Small(bool write);

      /// <summary>Bytes allocated per invoice</summary>
      double Allocated(bool write);
    }

    private sealed class Candidate<TInvoice, TAddress> : ICandidate
    {
      private readonly TInvoice[] _invoices;
      private readonly TAddress _address;
      private readonly Func<TInvoice, byte[]> _serialize;
      private readonly Func<byte[], TInvoice> _deserialize;
      private readonly Func<TAddress, byte[]> _serializeSmall;
      private readonly Func<byte[], TAddress> _deserializeSmall;
      private byte[][] _payloads;
      private byte[] _small;

      public Candidate(string name, Func<TInvoice, byte[]> serialize, Func<byte[], TInvoice> deserialize, Func<TAddress, byte[]> serializeSmall, Func<byte[], TAddress> deserializeSmall)
      {
        Name = name;
        _serialize = serialize;
        _deserialize = deserialize;
        _serializeSmall = serializeSmall;
        _deserializeSmall = deserializeSmall;
        _invoices = new TInvoice[AllInvoices.Length];
        for (int t = 0; t < _invoices.Length; t++)
          _invoices[t] = As<TInvoice>(AllInvoices[t]);
        _address = As<TAddress>(AllInvoices[1].Customer.BillingAddress);
      }

      public string Name { get; }

      public int SmallSize => _small.Length;

      public byte[][] Payloads => _payloads;

      public long Prepare()
      {
        _payloads = new byte[_invoices.Length][];
        long bytes = 0;
        for (int t = 0; t < _invoices.Length; t++)
        {
          bytes += (_payloads[t] = _serialize(_invoices[t])).Length;
          Same.AssertEqual(_invoices[t], _deserialize(_payloads[t]), Name + " round trip");
        }
        _small = _serializeSmall(_address);
        Same.AssertEqual(_address, _deserializeSmall(_small), Name + " round trip of the address");
        return bytes;
      }

      public TimeSpan Write()
      {
        byte[][] payloads = new byte[_invoices.Length][];
        Stopwatch sw = Stopwatch.StartNew();
        for (int r = 0; r < Rounds; r++)
          for (int t = 0; t < _invoices.Length; t++)
            payloads[t] = _serialize(_invoices[t]);
        return sw.Elapsed;
      }

      public TimeSpan Read()
      {
        Stopwatch sw = Stopwatch.StartNew();
        for (int r = 0; r < Rounds; r++)
          for (int t = 0; t < _payloads.Length; t++)
            _deserialize(_payloads[t]);
        return sw.Elapsed;
      }

      public TimeSpan Small(bool write)
      {
        Stopwatch sw = Stopwatch.StartNew();
        if (write)
          for (int t = 0; t < SmallCalls; t++)
            _serializeSmall(_address);
        else
          for (int t = 0; t < SmallCalls; t++)
            _deserializeSmall(_small);
        return sw.Elapsed;
      }

      public double Allocated(bool write)
      {
        long before = GC.GetAllocatedBytesForCurrentThread();
        if (write)
          for (int t = 0; t < _invoices.Length; t++)
            _serialize(_invoices[t]);
        else
          for (int t = 0; t < _payloads.Length; t++)
            _deserialize(_payloads[t]);
        return (double)(GC.GetAllocatedBytesForCurrentThread() - before) / _invoices.Length;
      }
    }

    private static readonly Invoice[] AllInvoices = Invoices.Create(InvoiceCount);

    /// <summary>
    /// The same data in another model with the same property names (copied, not through JSON: Json.NET changes the scale of decimals, 21m becomes 21.0m)
    /// </summary>
    internal static T As<T>(object value)
    {
      return (T)Copy(value, typeof(T));
    }

    private static object Copy(object value, Type to)
    {
      if (value is null || value is string)
        return value;
      Type from = value.GetType();
      if (from.IsValueType && (from == to || from == Nullable.GetUnderlyingType(to))) // a struct of another model is copied by its properties (into the boxed copy)
        return value;
      if (value is System.Collections.IList list)
      {
        System.Collections.IList copy = (System.Collections.IList)Activator.CreateInstance(to);
        Type element = to.GetGenericArguments()[0];
        for (int t = 0; t < list.Count; t++)
          copy.Add(Copy(list[t], element));
        return copy;
      }
      object target = Activator.CreateInstance(to);
      System.Reflection.PropertyInfo[] properties = to.GetProperties();
      for (int t = 0; t < properties.Length; t++)
      {
        if (properties[t].GetIndexParameters().Length == 0 && properties[t].CanWrite) // not Vector3's indexer
          properties[t].SetValue(target, Copy(ValueOf(value, properties[t].Name), properties[t].PropertyType));
      }
      // Models with fields (and System.Numerics.Vector3)
      System.Reflection.FieldInfo[] fields = to.GetFields();
      for (int t = 0; t < fields.Length; t++)
      {
        if (!fields[t].IsStatic)
          fields[t].SetValue(target, Copy(ValueOf(value, fields[t].Name), fields[t].FieldType));
      }
      return target;
    }

    private static object ValueOf(object value, string name)
    {
      System.Reflection.PropertyInfo property = value.GetType().GetProperty(name);
      return property != null ? property.GetValue(value) : value.GetType().GetField(name).GetValue(value);
    }

    private static ICandidate[] CreateCandidates()
    {
      // MessagePack-CSharp: the formatters of its source generator first (StandardResolver would find them too, this makes sure no dynamic formatter is used)
      Assert.IsNotNull(GeneratedMessagePackResolver.Instance.GetFormatter<InvoiceK>(), "MessagePack-CSharp's source generator did not run");
      Assert.IsNotNull(GeneratedMessagePackResolver.Instance.GetFormatter<InvoiceN>(), "MessagePack-CSharp's source generator did not run");
      MessagePackSerializerOptions mpGenerated = MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(GeneratedMessagePackResolver.Instance, StandardResolver.Instance));
      MessagePackSerializerOptions mpNative = MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(NativeGuidResolver.Instance, NativeDecimalResolver.Instance, GeneratedMessagePackResolver.Instance, StandardResolver.Instance));
      MessagePackSerializerOptions mpContractless = ContractlessStandardResolver.Options;

      NB.MessagePackSerializer nbArrays = new NB.MessagePackSerializer() { PerfOverSchemaStability = true };
      NB.MessagePackSerializer nbMaps = new NB.MessagePackSerializer();
      NB.MessagePackSerializer nbReflection = new NB.MessagePackSerializer();
      ITypeShape<Invoice> reflectedInvoice = ReflectionTypeShapeProvider.Default.GetTypeShape<Invoice>();
      ITypeShape<Address> reflectedAddress = ReflectionTypeShapeProvider.Default.GetTypeShape<Address>();

      LsMsgPackContext[] generated = new LsMsgPackContext[] { TestMsgPackContext.Default };
      LsMsgPackSerializer ltDefault = new LsMsgPackSerializer(new LsMsgPackOptions());
      LsMsgPackSerializer ltDefaultGen = new LsMsgPackSerializer(new LsMsgPackOptions() { Contexts = generated, UseRegisteredContexts = false });
      LsMsgPackSerializer ltArrays = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration });
      LsMsgPackSerializer ltArraysGen = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration, Contexts = generated, UseRegisteredContexts = false });
      LsMsgPackSerializer ltNames = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map });
      LsMsgPackSerializer ltNamesGen = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map, Contexts = generated, UseRegisteredContexts = false });
      LsMsgPackSerializer ltIndexedMap = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = true, ObjectLayout = ObjectLayout.Map });

      // The presets write what the other libraries write (asserted in Prepare)
      LsMsgPackOptions mpArrays = LsMsgPackPresets.MessagePackCSharp();
      mpArrays.ObjectLayout = ObjectLayout.Array;
      mpArrays.PropertyOrder = PropertyOrder.Declaration;
      LsMsgPackSerializer ltLikeMpArrays = new LsMsgPackSerializer(mpArrays);
      LsMsgPackSerializer ltLikeMp = new LsMsgPackSerializer(LsMsgPackPresets.MessagePackCSharp());
      LsMsgPackSerializer ltLikeNerdbank = new LsMsgPackSerializer(LsMsgPackPresets.Nerdbank());

      JsonSerializerOptions stj = new JsonSerializerOptions();

      return new ICandidate[]
      {
        new Candidate<Invoice, Address>("System.Text.Json", // the baseline
          i => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(i, stj), b => System.Text.Json.JsonSerializer.Deserialize<Invoice>(b, stj),
          a => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(a, stj), b => System.Text.Json.JsonSerializer.Deserialize<Address>(b, stj)),

        // Objects as arrays (positional)
        new Candidate<InvoiceK, AddressK>("MessagePack-CSharp [Key(n)], generated",
          i => MessagePackSerializer.Serialize(i, mpGenerated), b => MessagePackSerializer.Deserialize<InvoiceK>(b, mpGenerated),
          a => MessagePackSerializer.Serialize(a, mpGenerated), b => MessagePackSerializer.Deserialize<AddressK>(b, mpGenerated)),
        new Candidate<InvoiceK, AddressK>("MessagePack-CSharp [Key(n)], gen., native",
          i => MessagePackSerializer.Serialize(i, mpNative), b => MessagePackSerializer.Deserialize<InvoiceK>(b, mpNative),
          a => MessagePackSerializer.Serialize(a, mpNative), b => MessagePackSerializer.Deserialize<AddressK>(b, mpNative)),
        new Candidate<Invoice, Address>("Nerdbank PerfOverSchemaStability, shapes",
          i => nbArrays.Serialize<Invoice, BenchmarkShapes>(i), b => nbArrays.Deserialize<Invoice, BenchmarkShapes>(b),
          a => nbArrays.Serialize<Address, BenchmarkShapes>(a), b => nbArrays.Deserialize<Address, BenchmarkShapes>(b)),
        new Candidate<Invoice, Address>("LsMsgPack MessagePackCSharp preset, arrays", // the bytes of the first row
          i => ltLikeMpArrays.Serialize(i), b => ltLikeMpArrays.Deserialize<Invoice>(b),
          a => ltLikeMpArrays.Serialize(a), b => ltLikeMpArrays.Deserialize<Address>(b)),
        new Candidate<Invoice, Address>("LsMsgPack arrays, no schema",
          i => ltArrays.Serialize(i), b => ltArrays.Deserialize<Invoice>(b),
          a => ltArrays.Serialize(a), b => ltArrays.Deserialize<Address>(b)),
        new Candidate<Invoice, Address>("LsMsgPack arrays, no schema, generated",
          i => ltArraysGen.Serialize(i), b => ltArraysGen.Deserialize<Invoice>(b),
          a => ltArraysGen.Serialize(a), b => ltArraysGen.Deserialize<Address>(b)),
        new Candidate<Invoice, Address>("LsMsgPack default (arrays, schema)",
          i => ltDefault.Serialize(i), b => ltDefault.Deserialize<Invoice>(b),
          a => ltDefault.Serialize(a), b => ltDefault.Deserialize<Address>(b)),
        new Candidate<Invoice, Address>("LsMsgPack default, generated",
          i => ltDefaultGen.Serialize(i), b => ltDefaultGen.Deserialize<Invoice>(b),
          a => ltDefaultGen.Serialize(a), b => ltDefaultGen.Deserialize<Address>(b)),

        // Objects as maps keyed by property names
        new Candidate<InvoiceN, AddressN>("MessagePack-CSharp names, generated",
          i => MessagePackSerializer.Serialize(i, mpGenerated), b => MessagePackSerializer.Deserialize<InvoiceN>(b, mpGenerated),
          a => MessagePackSerializer.Serialize(a, mpGenerated), b => MessagePackSerializer.Deserialize<AddressN>(b, mpGenerated)),
        new Candidate<Invoice, Address>("Nerdbank names, shapes",
          i => nbMaps.Serialize<Invoice, BenchmarkShapes>(i), b => nbMaps.Deserialize<Invoice, BenchmarkShapes>(b),
          a => nbMaps.Serialize<Address, BenchmarkShapes>(a), b => nbMaps.Deserialize<Address, BenchmarkShapes>(b)),
        new Candidate<Invoice, Address>("LsMsgPack Nerdbank preset",
          i => ltLikeNerdbank.Serialize(i), b => ltLikeNerdbank.Deserialize<Invoice>(b),
          a => ltLikeNerdbank.Serialize(a), b => ltLikeNerdbank.Deserialize<Address>(b)),
        new Candidate<Invoice, Address>("LsMsgPack names",
          i => ltNames.Serialize(i), b => ltNames.Deserialize<Invoice>(b),
          a => ltNames.Serialize(a), b => ltNames.Deserialize<Address>(b)),
        new Candidate<Invoice, Address>("LsMsgPack names, generated",
          i => ltNamesGen.Serialize(i), b => ltNamesGen.Deserialize<Invoice>(b),
          a => ltNamesGen.Serialize(a), b => ltNamesGen.Deserialize<Address>(b)),
        new Candidate<Invoice, Address>("LsMsgPack maps, indexed schema",
          i => ltIndexedMap.Serialize(i), b => ltIndexedMap.Deserialize<Invoice>(b),
          a => ltIndexedMap.Serialize(a), b => ltIndexedMap.Deserialize<Address>(b)),

        // Plain classes without attributes or generated code (the LsMsgPack rows above without "generated" are that too)
        new Candidate<Invoice, Address>("MessagePack-CSharp contractless",
          i => MessagePackSerializer.Serialize(i, mpContractless), b => MessagePackSerializer.Deserialize<Invoice>(b, mpContractless),
          a => MessagePackSerializer.Serialize(a, mpContractless), b => MessagePackSerializer.Deserialize<Address>(b, mpContractless)),
        new Candidate<Invoice, Address>("LsMsgPack MessagePackCSharp preset", // the bytes of contractless
          i => ltLikeMp.Serialize(i), b => ltLikeMp.Deserialize<Invoice>(b),
          a => ltLikeMp.Serialize(a), b => ltLikeMp.Deserialize<Address>(b)),
        new Candidate<Invoice, Address>("Nerdbank names, reflection",
          i => nbReflection.Serialize(i, reflectedInvoice), b => nbReflection.Deserialize(b, reflectedInvoice),
          a => nbReflection.Serialize(a, reflectedAddress), b => nbReflection.Deserialize(b, reflectedAddress)),
      };
    }

    [TestMethod]
    public void Invoices_Write_Libraries()
    {
      Benchmark("Serialize", true);
    }

    [TestMethod]
    public void Invoices_Read_Libraries()
    {
      Benchmark("Deserialize", false);
    }

    [TestMethod]
    public void Address_PerCall_Libraries()
    {
      ICandidate[] candidates = Prepare(out _);
      foreach (ICandidate candidate in candidates)
        WarmUp(() => { candidate.Small(true); candidate.Small(false); });

      TimeSpan[] write = new TimeSpan[candidates.Length];
      TimeSpan[] read = new TimeSpan[candidates.Length];
      for (int p = 0; p < Passes; p++)
      {
        for (int c = 0; c < candidates.Length; c++)
        {
          TimeSpan w = candidates[c].Small(true);
          TimeSpan r = candidates[c].Small(false);
          if (p == 0 || w < write[c])
            write[c] = w;
          if (p == 0 || r < read[c])
            read[c] = r;
        }
      }

      StringBuilder sb = new StringBuilder();
      sb.AppendLine($"One Address (6 strings) per call, {SmallCalls:N0} calls, fastest of {Passes} passes");
      sb.AppendLine();
      sb.AppendLine(string.Format("{0,-44} {1,6} {2,12} {3,12}", "Serializer", "Bytes", "Write ns", "Read ns"));
      sb.AppendLine(new string('-', 77));
      for (int c = 0; c < candidates.Length; c++)
        sb.AppendLine(string.Format("{0,-44} {1,6} {2,12:N0} {3,12:N0}", candidates[c].Name, candidates[c].SmallSize,
          write[c].TotalMilliseconds * 1_000_000 / SmallCalls, read[c].TotalMilliseconds * 1_000_000 / SmallCalls));
      TestContext.WriteLine(sb.ToString());
    }

    internal static void WarmUp(Action run)
    {
      Stopwatch sw = Stopwatch.StartNew();
      do
        run();
      while (sw.ElapsedMilliseconds < WarmUpMilliseconds);
      Thread.Sleep(50);
    }

    private static ICandidate[] Prepare(out long[] bytes)
    {
      // PreservePackages only exists when LsMsgPack is compiled with KEEPTRACK, its overhead would skew the results
      if (typeof(MsgPackSettings).GetProperty("PreservePackages") != null)
        Assert.Inconclusive("Compiled with KEEPTRACK, run the benchmark using the Release (or Debug) configuration.");

      ICandidate[] candidates = CreateCandidates();
      bytes = new long[candidates.Length];
      for (int c = 0; c < candidates.Length; c++)
        bytes[c] = candidates[c].Prepare();

      // Like for like: the presets write the bytes of the library they are named after
      AssertSameBytes(candidates, "MessagePack-CSharp [Key(n)], generated", "LsMsgPack MessagePackCSharp preset, arrays");
      AssertSameBytes(candidates, "MessagePack-CSharp contractless", "LsMsgPack MessagePackCSharp preset");
      return candidates;
    }

    private static void AssertSameBytes(ICandidate[] candidates, string expected, string actual)
    {
      byte[][] e = Array.Find(candidates, c => c.Name == expected).Payloads;
      byte[][] a = Array.Find(candidates, c => c.Name == actual).Payloads;
      for (int t = 0; t < e.Length; t++)
        CollectionAssert.AreEqual(e[t], a[t], $"{actual} writes other bytes than {expected} ({AllInvoices[t].InvoiceNumber})");
    }

    private void Benchmark(string operation, bool write)
    {
      ICandidate[] candidates = Prepare(out long[] bytes);

      // Warm up each candidate for a while: the JIT recompiles hot methods with their profile (tiered PGO) in the background,
      // a few rounds were not enough for MessagePack-CSharp's decimal strings (its writes were 40% slower than after seconds in a loop)
      foreach (ICandidate candidate in candidates)
        WarmUp(() => _ = write ? candidate.Write() : candidate.Read());

      // The fastest of several passes, interleaving the candidates to spread out background noise
      TimeSpan[] fastest = new TimeSpan[candidates.Length];
      for (int p = 0; p < Passes; p++)
      {
        for (int c = 0; c < candidates.Length; c++)
        {
          TimeSpan elapsed = write ? candidates[c].Write() : candidates[c].Read();
          if (p == 0 || elapsed < fastest[c])
            fastest[c] = elapsed;
        }
      }

      StringBuilder sb = new StringBuilder();
      sb.AppendLine($"{operation}: {InvoiceCount} invoices, {Rounds} rounds each, fastest of {Passes} passes");
      sb.AppendLine();
      sb.AppendLine(string.Format("{0,-44} {1,9} {2,6} {3,10} {4,7} {5,12}", "Serializer", "Bytes", "Size", "ms", "Speed", "Alloc/inv."));
      sb.AppendLine(new string('-', 93));
      for (int c = 0; c < candidates.Length; c++)
      {
        sb.AppendLine(string.Format("{0,-44} {1,9:N0} {2,5:P0} {3,10:N2} {4,6:N2}x {5,10:N0} B",
          candidates[c].Name,
          bytes[c],
          (double)bytes[c] / bytes[0],
          fastest[c].TotalMilliseconds,
          fastest[0].TotalMilliseconds / fastest[c].TotalMilliseconds,
          candidates[c].Allocated(write)));
      }
      sb.AppendLine();
      sb.AppendLine("Size is relative to System.Text.Json, Speed is how many times faster than System.Text.Json (higher is better).");

      TestContext.WriteLine(sb.ToString());
    }
  }
}
