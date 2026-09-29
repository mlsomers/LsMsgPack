using MessagePack;
using System;
using System.Collections.Generic;

namespace LsMsgPackInteropTests
{
  // The invoice model and data of the BenchmarkInvoices unit test (seed 42)

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

  // The same model the way MessagePack-CSharp recommends it: integer keys, serialized as arrays

  [MessagePackObject]
  public class AddressK
  {
    [Key(0)] public string Street { get; set; }
    [Key(1)] public string HouseNumber { get; set; }
    [Key(2)] public string PostalCode { get; set; }
    [Key(3)] public string City { get; set; }
    [Key(4)] public string Region { get; set; }
    [Key(5)] public string CountryCode { get; set; }
  }

  [MessagePackObject]
  public class CustomerK
  {
    [Key(0)] public int CustomerId { get; set; }
    [Key(1)] public string CompanyName { get; set; }
    [Key(2)] public string ContactName { get; set; }
    [Key(3)] public string Email { get; set; }
    [Key(4)] public string Phone { get; set; }
    [Key(5)] public string VatNumber { get; set; }
    [Key(6)] public AddressK BillingAddress { get; set; }
    [Key(7)] public AddressK ShippingAddress { get; set; }
  }

  [MessagePackObject]
  public class InvoiceLineK
  {
    [Key(0)] public int LineNumber { get; set; }
    [Key(1)] public string ProductCode { get; set; }
    [Key(2)] public string Description { get; set; }
    [Key(3)] public decimal Quantity { get; set; }
    [Key(4)] public UnitOfMeasure Unit { get; set; }
    [Key(5)] public decimal UnitPrice { get; set; }
    [Key(6)] public decimal DiscountPercentage { get; set; }
    [Key(7)] public decimal VatPercentage { get; set; }
    [Key(8)] public decimal LineTotal { get; set; }
  }

  [MessagePackObject]
  public class InvoiceK
  {
    [Key(0)] public Guid Id { get; set; }
    [Key(1)] public string InvoiceNumber { get; set; }
    [Key(2)] public DateTime InvoiceDate { get; set; }
    [Key(3)] public DateTime DueDate { get; set; }
    [Key(4)] public DateTime? PaidDate { get; set; }
    [Key(5)] public InvoiceStatus Status { get; set; }
    [Key(6)] public string Currency { get; set; }
    [Key(7)] public string Reference { get; set; }
    [Key(8)] public string Notes { get; set; }
    [Key(9)] public CustomerK Customer { get; set; }
    [Key(10)] public List<InvoiceLineK> Lines { get; set; }
    [Key(11)] public decimal SubTotal { get; set; }
    [Key(12)] public decimal VatTotal { get; set; }
    [Key(13)] public decimal Total { get; set; }
  }

  // Polymorphism: LsMsgPack needs nothing, MessagePack-CSharp needs [Union] on the base type

  [Union(0, typeof(Dog))]
  [Union(1, typeof(Cat))]
  public abstract class Animal
  {
    public string Name { get; set; }
  }

  public class Dog : Animal
  {
    public int Barks { get; set; }
  }

  public class Cat : Animal
  {
    public bool Indoor { get; set; }
  }

  public class Zoo
  {
    public List<Animal> Animals { get; set; }
  }

  /// <summary>
  /// LsMsgPack omits values equal to the default of their type (FilterDefaultValues), a reader then keeps what the constructor set.
  /// </summary>
  public class DefaultsProbe
  {
    public string Text { get; set; }
    public int Retries { get; set; } = 3;
    public bool Enabled { get; set; } = true;
    public List<int> Items { get; set; } = new List<int>();
  }

  public static class Invoices
  {
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

    public static Invoice[] Create(int count)
    {
      Random rnd = new Random(42); // fixed seed, comparable runs
      Invoice[] invoices = new Invoice[count];
      for (int t = 0; t < count; t++)
        invoices[t] = CreateInvoice(rnd, t + 1);
      return invoices;
    }
  }
}
