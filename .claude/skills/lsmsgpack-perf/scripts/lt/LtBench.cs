// A/B benchmark of LtMsgPack (and the HTTP serializer of the formatters) on the invoice model, System.Text.Json as the baseline.
// Usage: <exe> table [seconds]                    all candidates, fastest write/read (20 rounds x 100 invoices), relative to System.Text.Json
//        <exe> write|read|both <candidate> <seconds>   one candidate in a loop (for perf), prints the fastest
//        <exe> small <seconds>                    per call cost of a single Address / tiny object
using LsMsgPack;
using LtMsgPack;
using LtMsgPack.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

public static class Bench
{
  static Guid NewGuid(Random rnd) { byte[] b = new byte[16]; rnd.NextBytes(b); return new Guid(b); }
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
        Id = NewGuid(rnd),
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

  public class Tiny { public int Id { get; set; } }

  sealed class Candidate
  {
    public string Name;
    public Func<Invoice, byte[]> Ser;
    public Func<byte[], Invoice> De;
    public Func<Address, byte[]> SerSmall;
    public Func<byte[], Address> DeSmall;
    public byte[][] Payloads;
  }

  static Candidate Lt(string name, LtMsgPackOptions o)
  {
    LtMsgPackSerializer s = new LtMsgPackSerializer(o);
    return new Candidate { Name = name, Ser = i => s.Serialize(i), De = b => s.Deserialize<Invoice>(b), SerSmall = a => s.Serialize(a), DeSmall = b => s.Deserialize<Address>(b) };
  }

  static Candidate Http(string name, string mediaType, bool clientHolds)
  {
    LtMsgPackHttpSerializer h = new LtMsgPackHttpSerializer();
    string header = null;
    if (clientHolds) // the response names its schema, the client then lists it
      header = h.Serialize(CreateInvoices(1)[0], typeof(Invoice), mediaType, null).SchemaId;
    return new Candidate
    {
      Name = name,
      Ser = i => h.Serialize(i, typeof(Invoice), mediaType, header).ToArray(),
      De = b => (Invoice)h.Deserialize(typeof(Invoice), b, 0, b.Length, mediaType),
      SerSmall = a => h.Serialize(a, typeof(Address), mediaType, header).ToArray(),
      DeSmall = b => (Address)h.Deserialize(typeof(Address), b, 0, b.Length, mediaType),
    };
  }

  static List<Candidate> Candidates()
  {
    JsonSerializerOptions stj = new JsonSerializerOptions();
    var list = new List<Candidate>
    {
      new Candidate { Name = "stj", Ser = i => JsonSerializer.SerializeToUtf8Bytes(i, stj), De = b => JsonSerializer.Deserialize<Invoice>(b, stj),
        SerSmall = a => JsonSerializer.SerializeToUtf8Bytes(a, stj), DeSmall = b => JsonSerializer.Deserialize<Address>(b, stj) },
      Lt("lt-default", new LtMsgPackOptions()),
      Lt("lt-map-indexed", new LtMsgPackOptions { UseInexedSchema = true, ObjectLayout = ObjectLayout.Map }),
      Lt("lt-map-reference", new LtMsgPackOptions { UseInexedSchema = true, SchemaStore = new SchemaStore(), WriteSchemaReference = true, ObjectLayout = ObjectLayout.Map }),
      Lt("lt-map-named", new LtMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Map }),
      Lt("lt-arr-indexed", new LtMsgPackOptions { UseInexedSchema = true, ObjectLayout = ObjectLayout.Array }),
      Lt("lt-arr-reference", new LtMsgPackOptions { UseInexedSchema = true, SchemaStore = new SchemaStore(), WriteSchemaReference = true, ObjectLayout = ObjectLayout.Array }),
      Lt("lt-arr-noschema", new LtMsgPackOptions { UseInexedSchema = false, ObjectLayout = ObjectLayout.Array, PropertyOrder = PropertyOrder.Declaration }),
      Lt("lt-mpcsharp", LtMsgPackPresets.MessagePackCSharp()),
      Http("http-lsmsgpack", "application/x-lsmsgpack", false),
      Http("http-lsmsgpack-ref", "application/x-lsmsgpack", true),
      Http("http-plain", "application/msgpack", false),
    };
    return list;
  }

  static Invoice[] _invoices = CreateInvoices(100);

  static (double write, double read) Measure(Candidate c, string mode, double seconds, int minRounds)
  {
    Invoice[] invoices = _invoices;
    if (c.Payloads is null)
      c.Payloads = invoices.Select(c.Ser).ToArray();
    byte[][] payloads = c.Payloads;
    byte[][] sink = new byte[invoices.Length][];
    Stopwatch total = Stopwatch.StartNew();
    int rounds = 0;
    double minWrite = double.MaxValue, minRead = double.MaxValue;
    do
    {
      Stopwatch sw = Stopwatch.StartNew();
      if (mode != "read")
        for (int r = 0; r < 20; r++)
          for (int t = 0; t < invoices.Length; t++)
            sink[t] = c.Ser(invoices[t]);
      double write = sw.Elapsed.TotalMilliseconds;
      sw.Restart();
      if (mode != "write")
        for (int r = 0; r < 20; r++)
          for (int t = 0; t < payloads.Length; t++)
            c.De(payloads[t]);
      double read = sw.Elapsed.TotalMilliseconds;
      if (++rounds > 2)
      {
        minWrite = Math.Min(minWrite, write);
        minRead = Math.Min(minRead, read);
      }
    } while (total.Elapsed.TotalSeconds < seconds || rounds < minRounds);
    return (minWrite, minRead);
  }

  static (double write, double read) MeasureSmall(Candidate c, double seconds)
  {
    Address a = _invoices[1].Customer.BillingAddress;
    byte[] payload = c.SerSmall(a);
    const int N = 200000;
    double minW = double.MaxValue, minR = double.MaxValue;
    Stopwatch total = Stopwatch.StartNew();
    int rounds = 0;
    do
    {
      Stopwatch sw = Stopwatch.StartNew();
      for (int t = 0; t < N; t++) c.SerSmall(a);
      double w = sw.Elapsed.TotalMilliseconds * 1000.0 / N;
      sw.Restart();
      for (int t = 0; t < N; t++) c.DeSmall(payload);
      double r = sw.Elapsed.TotalMilliseconds * 1000.0 / N;
      if (++rounds > 1) { minW = Math.Min(minW, w); minR = Math.Min(minR, r); }
    } while (total.Elapsed.TotalSeconds < seconds || rounds < 3);
    return (minW, minR);
  }

  public static void Main(string[] args)
  {
    string mode = args.Length > 0 ? args[0] : "table";
    long exceptions = 0;
    AppDomain.CurrentDomain.FirstChanceException += (s, e) => Interlocked.Increment(ref exceptions);
    List<Candidate> all = Candidates();

    if (mode == "table" || mode == "small")
    {
      double seconds = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 2;
      string filter = args.Length > 2 ? args[2] : null;
      var rows = new List<(string, long, double, double)>();
      foreach (Candidate c in all)
      {
        if (filter != null && c.Name != "stj" && !c.Name.StartsWith(filter, StringComparison.Ordinal)) continue;
        (double w, double r) = mode == "table" ? Measure(c, "both", seconds, 5) : MeasureSmall(c, seconds);
        long bytes = mode == "table" ? c.Payloads.Sum(p => (long)p.Length) : c.SerSmall(_invoices[1].Customer.BillingAddress).Length;
        rows.Add((c.Name, bytes, w, r));
      }
      var b = rows[0];
      string unit = mode == "table" ? "ms" : "us";
      foreach (var (name, bytes, w, r) in rows)
        Console.WriteLine($"{name,-20} {bytes,9:N0} {(double)bytes / b.Item2,5:P0}  write {w,8:N2} {unit} {b.Item3 / w,5:N2}x   read {r,8:N2} {unit} {b.Item4 / r,5:N2}x");
      Console.WriteLine($"({exceptions} first chance exceptions)");
      return;
    }

    if (mode == "small1")
    {
      Candidate c = all.First(x => x.Name == args[1]);
      (double w, double r) = MeasureSmall(c, double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
      Console.WriteLine($"{c.Name,-20} small write {w,6:N3} us   read {r,6:N3} us   ({exceptions} first chance exceptions)");
      return;
    }

    {
      string name = args[1];
      double seconds = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
      Candidate c = all.First(x => x.Name == name);
      (double w, double r) = Measure(c, mode, seconds, 4);
      Console.WriteLine($"{name,-20} write {w,8:N2} ms   read {r,8:N2} ms   ({exceptions} first chance exceptions)");
    }
  }
}
