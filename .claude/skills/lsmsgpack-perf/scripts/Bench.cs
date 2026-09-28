// A/B benchmark of the invoice model (the same model and data as LsMsgPackUnitTests.BenchmarkInvoices).
// Usage: <exe> write|read|both|phases <seconds> [indexed|named] [nocompile]
//   Prints the fastest write and read of 20 rounds over 100 invoices (the first rounds are warm-up).
//   nocompile switches off MsgPackSettings.CompilePropertyAccessors (A/B within one build).
//   phases (named settings): object tree build vs WriteTo/ToBytes vs raw MsgPackItem.Unpack.
using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

public static class Bench
{
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

  public static void Main(string[] args)
  {
    string mode = args.Length > 0 ? args[0] : "both";
    int seconds = args.Length > 1 ? int.Parse(args[1]) : 10;
    bool indexed = args.Length > 2 ? args[2] != "named" : true;
    if (args.Length > 3 && args[3] == "nocompile") // by reflection, older builds (a base) do not have it
      typeof(MsgPackSettings).GetProperty("CompilePropertyAccessors")?.SetValue(null, false);

    Invoice[] invoices = CreateInvoices(100);
    MsgPackSettings settings = new MsgPackSettings() { UseInexedSchema = indexed };

    long exceptions = 0;
    AppDomain.CurrentDomain.FirstChanceException += (s, e) => Interlocked.Increment(ref exceptions);

    byte[][] payloads = new byte[invoices.Length][];
    for (int t = 0; t < invoices.Length; t++)
      payloads[t] = MsgPackSerializer.Serialize(invoices[t], settings);

    if (mode == "phases")
    {
      MsgPackSettings named = new MsgPackSettings() { UseInexedSchema = false };
      for (int pass = 0; pass < 8; pass++)
      {
        Stopwatch sw = Stopwatch.StartNew();
        for (int r = 0; r < 20; r++)
          foreach (Invoice invoice in invoices)
            MsgPackSerializer.SerializeObject(invoice, named);
        double build = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        for (int r = 0; r < 20; r++)
          foreach (Invoice invoice in invoices)
            MsgPackSerializer.SerializeObject(invoice, named).ToBytes();
        double both = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        for (int r = 0; r < 20; r++)
          foreach (byte[] payload in payloads)
          {
            MemoryStream ms = new MemoryStream(payload);
            MsgPackItem.Unpack(ms, named); // the schema (or the whole object without it)
            if (ms.Position < ms.Length)
              MsgPackItem.Unpack(ms, named); // the body
          }
        double unpack = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"tree build {build,7:N1} ms   +ToBytes {both - build,7:N1} ms   raw Unpack {unpack,7:N1} ms");
      }
      return;
    }

    exceptions = 0;
    Stopwatch total = Stopwatch.StartNew();
    int rounds = 0;
    double minWrite = double.MaxValue, minRead = double.MaxValue;
    do
    {
      Stopwatch sw = Stopwatch.StartNew();
      if (mode != "read")
        for (int r = 0; r < 20; r++)
          for (int t = 0; t < invoices.Length; t++)
            payloads[t] = MsgPackSerializer.Serialize(invoices[t], settings);
      double write = sw.Elapsed.TotalMilliseconds;
      sw.Restart();
      if (mode != "write")
        for (int r = 0; r < 20; r++)
          for (int t = 0; t < payloads.Length; t++)
            MsgPackSerializer.Deserialize<Invoice>(payloads[t], settings);
      double read = sw.Elapsed.TotalMilliseconds;
      if (++rounds > 2)
      {
        minWrite = Math.Min(minWrite, write);
        minRead = Math.Min(minRead, read);
      }
    } while (total.Elapsed.TotalSeconds < seconds || rounds < 4);

    Console.WriteLine($"{(indexed ? "indexed" : "named  ")}: min write {minWrite,7:N1} ms   min read {minRead,7:N1} ms   ({rounds} rounds, {exceptions} first chance exceptions)");
  }
}
