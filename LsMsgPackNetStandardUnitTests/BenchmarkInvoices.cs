using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// Compares the performance and payload size of LsMsgPack with Json.NET using a set of typical invoices.
  /// <para>Results are written to the test output; timings are not asserted since they depend on the machine (and build configuration).</para>
  /// </summary>
  [TestClass]
  [TestCategory("Benchmark")]
  public class BenchmarkInvoices
  {
    public TestContext TestContext { get; set; }

    private const int InvoiceCount = 100;
    private const int Rounds = 20;
    private const int Passes = 3;

    #region Invoice model

    public enum InvoiceStatus { Draft, Sent, Paid, Overdue, Cancelled }

    public enum UnitOfMeasure { Piece, Hour, Kilogram, Meter, Liter, Box }

    public class Address
    {
      public string Street { get; set; }
      public string HouseNumber { get; set; }
      public string PostalCode { get; set; }
      public string City { get; set; }
      public string Region { get; set; }
      public string CountryCode { get; set; }
    }

    public class Customer
    {
      public int CustomerId { get; set; }
      public string CompanyName { get; set; }
      public string ContactName { get; set; }
      public string Email { get; set; }
      public string Phone { get; set; }
      public string VatNumber { get; set; }
      public Address BillingAddress { get; set; }
      public Address ShippingAddress { get; set; }
    }

    public class InvoiceLine
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

    public class Invoice
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
      public Customer Customer { get; set; }
      public List<InvoiceLine> Lines { get; set; }
      public decimal SubTotal { get; set; }
      public decimal VatTotal { get; set; }
      public decimal Total { get; set; }
    }

    #endregion

    #region Mock data

    private static readonly string[] Companies = { "Acme Corporation", "Globex B.V.", "Initech Ltd.", "Umbrella GmbH", "Stark Industries", "Wayne Enterprises", "Hooli Inc.", "Vandelay Industries" };
    private static readonly string[] Contacts = { "Jan de Vries", "Maria Jansen", "Peter Smith", "Anna Müller", "Luca Rossi", "Sofia García", "Tom Bakker", "Emma Visser" };
    private static readonly string[] Streets = { "Keizersgracht", "Main Street", "Hauptstraße", "Rue de la Paix", "Via Roma", "Kalverstraat", "Baker Street", "Coolsingel" };
    private static readonly string[] Cities = { "Amsterdam", "Rotterdam", "London", "Berlin", "Paris", "Rome", "Madrid", "Utrecht" };
    private static readonly string[] Countries = { "NL", "NL", "GB", "DE", "FR", "IT", "ES", "NL" };
    private static readonly string[] Products = { "Consultancy services", "Software license (annual)", "Office chair, ergonomic", "USB-C docking station", "Printer paper A4, 500 sheets", "Travel expenses", "On-site support", "Cloud hosting, standard tier", "Coffee beans, 1kg", "Network cable Cat6, 5m" };
    private static readonly decimal[] VatRates = { 21m, 9m, 0m };

    private static Address CreateAddress(Random rnd, int i)
    {
      int c = rnd.Next(Cities.Length);
      return new Address()
      {
        Street = Streets[rnd.Next(Streets.Length)],
        HouseNumber = rnd.Next(1, 500).ToString() + (i % 5 == 0 ? "a" : string.Empty),
        PostalCode = rnd.Next(1000, 9999).ToString() + " " + (char)('A' + rnd.Next(26)) + (char)('A' + rnd.Next(26)),
        City = Cities[c],
        Region = i % 3 == 0 ? null : "Region " + c,
        CountryCode = Countries[c]
      };
    }

    private static Customer CreateCustomer(Random rnd, int i)
    {
      string company = Companies[rnd.Next(Companies.Length)];
      Address billing = CreateAddress(rnd, i);
      return new Customer()
      {
        CustomerId = 10000 + i,
        CompanyName = company,
        ContactName = Contacts[rnd.Next(Contacts.Length)],
        Email = "invoices@" + company.Split(' ')[0].ToLowerInvariant() + ".example.com",
        Phone = "+31 " + rnd.Next(10, 99) + " " + rnd.Next(1000000, 9999999),
        VatNumber = "NL" + rnd.Next(100000000, 999999999) + "B01",
        BillingAddress = billing,
        ShippingAddress = i % 4 == 0 ? CreateAddress(rnd, i + 1) : billing
      };
    }

    private static Invoice CreateInvoice(Random rnd, int i)
    {
      DateTime invoiceDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(rnd.Next(365));
      InvoiceStatus status = (InvoiceStatus)rnd.Next(5);

      Invoice invoice = new Invoice()
      {
        Id = Guid.NewGuid(),
        InvoiceNumber = "INV-2026-" + i.ToString("D5"),
        InvoiceDate = invoiceDate,
        DueDate = invoiceDate.AddDays(30),
        PaidDate = status == InvoiceStatus.Paid ? invoiceDate.AddDays(rnd.Next(1, 30)) : (DateTime?)null,
        Status = status,
        Currency = "EUR",
        Reference = "PO-" + rnd.Next(100000, 999999),
        Notes = i % 2 == 0 ? "Please mention the invoice number when making the payment. Thank you for your business!" : null,
        Customer = CreateCustomer(rnd, i),
        Lines = new List<InvoiceLine>()
      };

      int lineCount = rnd.Next(3, 25);
      for (int l = 1; l <= lineCount; l++)
      {
        decimal quantity = rnd.Next(1, 100);
        decimal unitPrice = Math.Round((decimal)(rnd.NextDouble() * 500), 2);
        decimal discount = l % 4 == 0 ? 10m : 0m;
        decimal vat = VatRates[rnd.Next(VatRates.Length)];
        decimal lineTotal = Math.Round(quantity * unitPrice * (100m - discount) / 100m, 2);

        invoice.Lines.Add(new InvoiceLine()
        {
          LineNumber = l,
          ProductCode = "P" + rnd.Next(1000, 9999),
          Description = Products[rnd.Next(Products.Length)],
          Quantity = quantity,
          Unit = (UnitOfMeasure)rnd.Next(6),
          UnitPrice = unitPrice,
          DiscountPercentage = discount,
          VatPercentage = vat,
          LineTotal = lineTotal
        });

        invoice.SubTotal += lineTotal;
        invoice.VatTotal += Math.Round(lineTotal * vat / 100m, 2);
      }
      invoice.Total = invoice.SubTotal + invoice.VatTotal;

      return invoice;
    }

    private static Invoice[] CreateInvoices(int count)
    {
      Random rnd = new Random(42); // fixed seed, comparable runs
      Invoice[] invoices = new Invoice[count];
      for (int t = 0; t < count; t++)
        invoices[t] = CreateInvoice(rnd, t + 1);
      return invoices;
    }

    #endregion

    private interface ICandidate
    {
      string Name { get; }

      /// <summary>Serialize and deserialize a single invoice (to verify the serializer before timing it)</summary>
      Invoice RoundTrip(Invoice invoice);

      /// <summary>Serialize all invoices once (not timed), the payloads are used by <see cref="Read"/>. Returns the total payload size in bytes.</summary>
      long Prepare(Invoice[] invoices);

      /// <summary>Time serializing all invoices <see cref="Rounds"/> times</summary>
      TimeSpan Write(Invoice[] invoices);

      /// <summary>Time deserializing all prepared payloads <see cref="Rounds"/> times</summary>
      TimeSpan Read();
    }

    private class Candidate<TPayload> : ICandidate
    {
      private readonly Func<Invoice, TPayload> _serialize;
      private readonly Func<TPayload, Invoice> _deserialize;
      private readonly Func<TPayload, int> _size;
      private TPayload[] _payloads;

      public Candidate(string name, Func<Invoice, TPayload> serialize, Func<TPayload, Invoice> deserialize, Func<TPayload, int> size)
      {
        Name = name;
        _serialize = serialize;
        _deserialize = deserialize;
        _size = size;
      }

      public string Name { get; }

      public Invoice RoundTrip(Invoice invoice)
      {
        return _deserialize(_serialize(invoice));
      }

      public long Prepare(Invoice[] invoices)
      {
        _payloads = new TPayload[invoices.Length];
        long bytes = 0;
        for (int t = 0; t < invoices.Length; t++)
          bytes += _size(_payloads[t] = _serialize(invoices[t]));
        return bytes;
      }

      public TimeSpan Write(Invoice[] invoices)
      {
        TPayload[] payloads = new TPayload[invoices.Length];
        Stopwatch sw = Stopwatch.StartNew();
        for (int r = 0; r < Rounds; r++)
          for (int t = 0; t < invoices.Length; t++)
            payloads[t] = _serialize(invoices[t]);
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
    }

    private static ICandidate[] CreateCandidates()
    {
      // The rows without "arrays" in their name write maps (ObjectLayout.Map), the layout of the measurements so far
      MsgPackSettings indexed = new MsgPackSettings() { UseInexedSchema = true, ObjectLayout = ObjectLayout.Map };
      MsgPackSettings named = new MsgPackSettings() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };
      SchemaStore store = new SchemaStore(); // one store for writing and reading: in practice the reader has its own, holding the writer's schemas
      MsgPackSettings reference = new MsgPackSettings() { UseInexedSchema = true, SchemaStore = store, WriteSchemaReference = true, ObjectLayout = ObjectLayout.Map };
      MsgPackSettings inlineStore = new MsgPackSettings() { UseInexedSchema = true, SchemaStore = store, ObjectLayout = ObjectLayout.Map };
      LtMsgPack.LtMsgPackSerializer ltIndexed = new LtMsgPack.LtMsgPackSerializer(new LtMsgPack.LtMsgPackOptions() { UseInexedSchema = true, ObjectLayout = ObjectLayout.Map });
      LtMsgPack.LtMsgPackSerializer ltReference = new LtMsgPack.LtMsgPackSerializer(new LtMsgPack.LtMsgPackOptions() { UseInexedSchema = true, SchemaStore = new SchemaStore(), WriteSchemaReference = true, ObjectLayout = ObjectLayout.Map });
      LtMsgPack.LtMsgPackSerializer ltNamed = new LtMsgPack.LtMsgPackSerializer(new LtMsgPack.LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map });
      MsgPackSettings indexedArrays = new MsgPackSettings() { UseInexedSchema = true, ObjectLayout = ObjectLayout.Array };
      LtMsgPack.LtMsgPackSerializer ltIndexedArrays = new LtMsgPack.LtMsgPackSerializer(new LtMsgPack.LtMsgPackOptions() { UseInexedSchema = true, ObjectLayout = ObjectLayout.Array });
      LtMsgPack.LtMsgPackSerializer ltReferenceArrays = new LtMsgPack.LtMsgPackSerializer(new LtMsgPack.LtMsgPackOptions() { UseInexedSchema = true, SchemaStore = new SchemaStore(), WriteSchemaReference = true, ObjectLayout = ObjectLayout.Array });
      LtMsgPack.LtMsgPackSerializer ltIndexedTrimmed = new LtMsgPack.LtMsgPackSerializer(new LtMsgPack.LtMsgPackOptions() { UseInexedSchema = true, ObjectLayout = ObjectLayout.Array, TrimTrailingNulls = true });
      LtMsgPack.LtMsgPackSerializer ltPositional = new LtMsgPack.LtMsgPackSerializer(new LtMsgPack.LtMsgPackOptions() { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration });

      return new ICandidate[]
      {
        new Candidate<string>("Json.NET (string)",
          i => JsonConvert.SerializeObject(i),
          s => JsonConvert.DeserializeObject<Invoice>(s),
          s => Encoding.UTF8.GetByteCount(s)),

        new Candidate<byte[]>("Json.NET (UTF-8 bytes)",
          i => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(i)),
          b => JsonConvert.DeserializeObject<Invoice>(Encoding.UTF8.GetString(b)),
          b => b.Length),

        new Candidate<byte[]>("LsMsgPack (indexed schema)",
          i => MsgPackSerializer.Serialize(i, indexed),
          b => MsgPackSerializer.Deserialize<Invoice>(b, indexed),
          b => b.Length),

        new Candidate<byte[]>("LsMsgPack (inline, store)", // the schema is written like the indexed schema, the reader recognizes it
          i => MsgPackSerializer.Serialize(i, inlineStore),
          b => MsgPackSerializer.Deserialize<Invoice>(b, inlineStore),
          b => b.Length),

        new Candidate<byte[]>("LsMsgPack (schema reference)",
          i => MsgPackSerializer.Serialize(i, reference),
          b => MsgPackSerializer.Deserialize<Invoice>(b, reference),
          b => b.Length),

        new Candidate<byte[]>("LsMsgPack (property names)",
          i => MsgPackSerializer.Serialize(i, named),
          b => MsgPackSerializer.Deserialize<Invoice>(b, named),
          b => b.Length),

        new Candidate<byte[]>("LtMsgPack (indexed schema)",
          i => ltIndexed.Serialize(i),
          b => ltIndexed.Deserialize<Invoice>(b),
          b => b.Length),

        new Candidate<byte[]>("LtMsgPack (schema reference)",
          i => ltReference.Serialize(i),
          b => ltReference.Deserialize<Invoice>(b),
          b => b.Length),

        new Candidate<byte[]>("LtMsgPack (property names)",
          i => ltNamed.Serialize(i),
          b => ltNamed.Deserialize<Invoice>(b),
          b => b.Length),

        // ObjectLayout.Array: objects as arrays of their values
        new Candidate<byte[]>("LsMsgPack (arrays, indexed)",
          i => MsgPackSerializer.Serialize(i, indexedArrays),
          b => MsgPackSerializer.Deserialize<Invoice>(b, indexedArrays),
          b => b.Length),

        new Candidate<byte[]>("LtMsgPack (arrays, indexed)",
          i => ltIndexedArrays.Serialize(i),
          b => ltIndexedArrays.Deserialize<Invoice>(b),
          b => b.Length),

        new Candidate<byte[]>("LtMsgPack (arrays, trimmed)", // indexed schema, TrimTrailingNulls (the invoices end with values that are never left out)
          i => ltIndexedTrimmed.Serialize(i),
          b => ltIndexedTrimmed.Deserialize<Invoice>(b),
          b => b.Length),

        new Candidate<byte[]>("LtMsgPack (arrays, reference)",
          i => ltReferenceArrays.Serialize(i),
          b => ltReferenceArrays.Deserialize<Invoice>(b),
          b => b.Length),

        new Candidate<byte[]>("LtMsgPack (arrays, no schema)", // positional only: the reader needs the same order
          i => ltPositional.Serialize(i),
          b => ltPositional.Deserialize<Invoice>(b),
          b => b.Length)
      };
    }

    [TestMethod]
    public void Invoices_Write_LsMsgPack_vs_JsonNet()
    {
      Benchmark("Serialize", (candidate, invoices) => candidate.Write(invoices));
    }

    [TestMethod]
    public void Invoices_Read_LsMsgPack_vs_JsonNet()
    {
      Benchmark("Deserialize", (candidate, invoices) => candidate.Read());
    }

    private void Benchmark(string operation, Func<ICandidate, Invoice[], TimeSpan> measure)
    {
      // PreservePackages only exists when LsMsgPack is compiled with KEEPTRACK (DebugKeepTrack / ReleaseKeepTrack), the debugging overhead would skew the results
      if (typeof(MsgPackSettings).GetProperty("PreservePackages") != null)
        Assert.Inconclusive("LsMsgPack is compiled with KEEPTRACK, run the benchmark using the Release (or Debug) configuration.");

      Invoice[] invoices = CreateInvoices(InvoiceCount);
      ICandidate[] candidates = CreateCandidates();

      // Make sure all serializers produce an equivalent object before timing them
      // (MsgPack timestamps do not preserve DateTimeKind, so compare the dates as UTC)
      JsonSerializerSettings compare = new JsonSerializerSettings() { DateTimeZoneHandling = DateTimeZoneHandling.Utc };
      string expected = JsonConvert.SerializeObject(invoices[0], compare);
      foreach (ICandidate candidate in candidates)
        Assert.AreEqual(expected, JsonConvert.SerializeObject(candidate.RoundTrip(invoices[0]), compare), candidate.Name + " round-trip");

      long[] bytes = new long[candidates.Length];
      for (int c = 0; c < candidates.Length; c++)
        bytes[c] = candidates[c].Prepare(invoices);

      // Warm up all candidates first so JIT (tiered compilation) and caches do not penalize whoever runs first
      foreach (ICandidate candidate in candidates)
        measure(candidate, invoices);
      Thread.Sleep(500);
      foreach (ICandidate candidate in candidates)
        measure(candidate, invoices);

      // Keep the fastest of several passes, interleaving the candidates to spread out background noise
      TimeSpan[] fastest = new TimeSpan[candidates.Length];
      for (int p = 0; p < Passes; p++)
      {
        for (int c = 0; c < candidates.Length; c++)
        {
          TimeSpan elapsed = measure(candidates[c], invoices);
          if (p == 0 || elapsed < fastest[c])
            fastest[c] = elapsed;
        }
      }

      int totalLines = 0;
      foreach (Invoice invoice in invoices)
        totalLines += invoice.Lines.Count;

      StringBuilder sb = new StringBuilder();
      sb.AppendLine($"{operation}: {InvoiceCount} invoices ({totalLines} lines), {Rounds} rounds each, fastest of {Passes} passes");
      sb.AppendLine();
      sb.AppendLine(string.Format("{0,-28} {1,12} {2,8} {3,16} {4,8}", "Serializer", "Bytes", "Size", operation + " ms", "Speed"));
      sb.AppendLine(new string('-', 76));
      for (int c = 0; c < candidates.Length; c++)
      {
        sb.AppendLine(string.Format("{0,-28} {1,12:N0} {2,7:P0} {3,16:N1} {4,7:N2}x",
          candidates[c].Name,
          bytes[c],
          (double)bytes[c] / bytes[0],
          fastest[c].TotalMilliseconds,
          fastest[0].TotalMilliseconds / fastest[c].TotalMilliseconds));
      }
      sb.AppendLine();
      sb.AppendLine("Size is relative to Json.NET, Speed is how many times faster than Json.NET (higher is better).");

      TestContext.WriteLine(sb.ToString());
    }
  }
}
