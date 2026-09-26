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

    private class Result
    {
      public string Name;
      public long Bytes;
      public TimeSpan Serialize;
      public TimeSpan Deserialize;
    }

    private static Result Measure<TPayload>(string name, Invoice[] invoices, Func<Invoice, TPayload> serialize, Func<TPayload, Invoice> deserialize, Func<TPayload, int> size)
    {
      TPayload[] payloads = new TPayload[invoices.Length];

      Stopwatch sw = Stopwatch.StartNew();
      for (int r = 0; r < Rounds; r++)
        for (int t = 0; t < invoices.Length; t++)
          payloads[t] = serialize(invoices[t]);
      TimeSpan serializeTime = sw.Elapsed;

      sw.Restart();
      for (int r = 0; r < Rounds; r++)
        for (int t = 0; t < invoices.Length; t++)
          deserialize(payloads[t]);
      TimeSpan deserializeTime = sw.Elapsed;

      long bytes = 0;
      for (int t = 0; t < payloads.Length; t++)
        bytes += size(payloads[t]);

      return new Result() { Name = name, Bytes = bytes, Serialize = serializeTime, Deserialize = deserializeTime };
    }

    [TestMethod]
    public void Invoices_LsMsgPack_vs_JsonNet()
    {
      // PreservePackages only exists when LsMsgPack is compiled with KEEPTRACK (DebugKeepTrack / ReleaseKeepTrack), the debugging overhead would skew the results
      if (typeof(MsgPackSettings).GetProperty("PreservePackages") != null)
        Assert.Inconclusive("LsMsgPack is compiled with KEEPTRACK, run the benchmark using the Release (or Debug) configuration.");

      Invoice[] invoices = CreateInvoices(InvoiceCount);

      MsgPackSettings indexed = new MsgPackSettings() { UseInexedSchema = true };
      MsgPackSettings named = new MsgPackSettings() { UseInexedSchema = false };

      // Make sure all serializers produce an equivalent object before timing them
      // (MsgPack timestamps do not preserve DateTimeKind, so compare the dates as UTC)
      JsonSerializerSettings compare = new JsonSerializerSettings() { DateTimeZoneHandling = DateTimeZoneHandling.Utc };
      string expected = JsonConvert.SerializeObject(invoices[0], compare);
      Assert.AreEqual(expected, JsonConvert.SerializeObject(JsonConvert.DeserializeObject<Invoice>(JsonConvert.SerializeObject(invoices[0])), compare), "Json.NET round-trip");
      Assert.AreEqual(expected, JsonConvert.SerializeObject(MsgPackSerializer.Deserialize<Invoice>(MsgPackSerializer.Serialize(invoices[0], indexed), indexed), compare), "LsMsgPack (indexed schema) round-trip");
      Assert.AreEqual(expected, JsonConvert.SerializeObject(MsgPackSerializer.Deserialize<Invoice>(MsgPackSerializer.Serialize(invoices[0], named), named), compare), "LsMsgPack (property names) round-trip");

      Func<Result>[] candidates =
      {
        () => Measure("Json.NET (string)", invoices,
          i => JsonConvert.SerializeObject(i),
          s => JsonConvert.DeserializeObject<Invoice>(s),
          s => Encoding.UTF8.GetByteCount(s)),

        () => Measure("Json.NET (UTF-8 bytes)", invoices,
          i => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(i)),
          b => JsonConvert.DeserializeObject<Invoice>(Encoding.UTF8.GetString(b)),
          b => b.Length),

        () => Measure("LsMsgPack (indexed schema)", invoices,
          i => MsgPackSerializer.Serialize(i, indexed),
          b => MsgPackSerializer.Deserialize<Invoice>(b, indexed),
          b => b.Length),

        () => Measure("LsMsgPack (property names)", invoices,
          i => MsgPackSerializer.Serialize(i, named),
          b => MsgPackSerializer.Deserialize<Invoice>(b, named),
          b => b.Length)
      };

      // Warm up all candidates first so JIT (tiered compilation) and caches do not penalize whoever runs first
      foreach (Func<Result> candidate in candidates)
        candidate();
      Thread.Sleep(500);
      foreach (Func<Result> candidate in candidates)
        candidate();

      // Keep the fastest of several passes, interleaving the candidates to spread out background noise
      Result[] results = new Result[candidates.Length];
      for (int p = 0; p < Passes; p++)
      {
        for (int c = 0; c < candidates.Length; c++)
        {
          Result r = candidates[c]();
          if (results[c] == null)
            results[c] = r;
          else
          {
            if (r.Serialize < results[c].Serialize) results[c].Serialize = r.Serialize;
            if (r.Deserialize < results[c].Deserialize) results[c].Deserialize = r.Deserialize;
          }
        }
      }

      int totalLines = 0;
      foreach (Invoice invoice in invoices)
        totalLines += invoice.Lines.Count;

      Result baseline = results[0];
      StringBuilder sb = new StringBuilder();
      sb.AppendLine($"{InvoiceCount} invoices ({totalLines} lines), {Rounds} rounds each, fastest of {Passes} passes");
      sb.AppendLine();
      sb.AppendLine(string.Format("{0,-28} {1,12} {2,8} {3,14} {4,8} {5,16} {6,8}", "Serializer", "Bytes", "Size", "Serialize ms", "Speed", "Deserialize ms", "Speed"));
      sb.AppendLine(new string('-', 100));
      foreach (Result r in results)
      {
        sb.AppendLine(string.Format("{0,-28} {1,12:N0} {2,7:P0} {3,14:N1} {4,7:N2}x {5,16:N1} {6,7:N2}x",
          r.Name,
          r.Bytes,
          (double)r.Bytes / baseline.Bytes,
          r.Serialize.TotalMilliseconds,
          baseline.Serialize.TotalMilliseconds / r.Serialize.TotalMilliseconds,
          r.Deserialize.TotalMilliseconds,
          baseline.Deserialize.TotalMilliseconds / r.Deserialize.TotalMilliseconds));
      }
      sb.AppendLine();
      sb.AppendLine("Size is relative to Json.NET, Speed is how many times faster than Json.NET (higher is better).");

      TestContext.WriteLine(sb.ToString());
    }
  }
}
