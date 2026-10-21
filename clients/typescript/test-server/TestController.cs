using LtMsgPack;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LsMsgPackTsTestServer
{
  [ApiController]
  [Route("")]
  public class TestController : ControllerBase
  {
    [HttpGet("invoices")]
    public Invoice[] GetInvoices([FromQuery] int count = 10) => Data.Invoices(count);

    [HttpGet("invoice")]
    public Invoice GetInvoice() => Data.Invoices(1)[0];

    [HttpGet("kinds/defaults")]
    public Kinds GetDefaults() => new Kinds();

    [HttpGet("kinds/values")]
    public Kinds GetValues() => Data.KindValues();

    [HttpGet("zoo")]
    public Zoo GetZoo() => Data.Zoo();

    [HttpGet("addresses")]
    public List<Address> GetAddresses() => new List<Address> { Data.Invoices(1)[0].Customer.BillingAddress, new Address { City = "Utrecht" } };

    [HttpGet("preferences")]
    public Preferences GetPreferences() => new Preferences { Retries = 0, Name = null, Tags = null, Home = null, Origin = default, Timeout = TimeSpan.Zero };

    [HttpPost("echo/preferences")]
    public IActionResult EchoPreferences(Preferences preferences) => new JsonResult(preferences);

    [HttpGet("nothing")]
    public Address GetNothing() => null;

    /// <summary>
    /// System.Text.Json of what the endpoint returns (every property, with the names of the classes, or with <paramref name="camel"/> camelCase): the objects the client should make.
    /// </summary>
    [HttpGet("expected/{name}")]
    public IActionResult Expected(string name, [FromQuery] int count = 10, [FromQuery] bool camel = false)
    {
      object value = name switch
      {
        "invoices" => Data.Invoices(count),
        "invoice" => Data.Invoices(1)[0],
        "defaults" => new Kinds(),
        "values" => Data.KindValues(),
        "addresses" => GetAddresses(),
        _ => null
      };
      if (value is null)
        return NotFound();
      return camel ? new JsonResult(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) : new JsonResult(value);
    }

    /// <summary>
    /// What a .NET client reads from the bytes the formatters write (LtMsgPack with the same preset: properties the data does not set keep their constructor values), as System.Text.Json.
    /// The models of lsmsgpack-ts make the TypeScript client read the same.
    /// </summary>
    [HttpGet("dotnet/{name}")]
    public IActionResult DotNet(string name, [FromQuery] bool camel = false)
    {
      object value = name switch
      {
        "invoices" => Data.Invoices(10),
        "preferences" => GetPreferences(),
        "defaults" => new Kinds(),
        "values" => Data.KindValues(),
        _ => null
      };
      if (value is null)
        return NotFound();
      LtMsgPackSerializer serializer = new LtMsgPackSerializer(LtMsgPackPresets.LsMsgPack());
      object read = serializer.Deserialize(value.GetType(), serializer.Serialize(value, value.GetType()));
      return camel ? new JsonResult(read, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) : new JsonResult(read);
    }

    /// <summary>
    /// Reads a request body (plain MsgPack) and answers what it read as System.Text.Json.
    /// </summary>
    [HttpPost("echo/invoice")]
    public IActionResult EchoInvoice(Invoice invoice) => new JsonResult(invoice);

    [HttpPost("echo/kinds")]
    public IActionResult EchoKinds(Kinds kinds) => new JsonResult(kinds);

    /// <summary>
    /// A polymorphic request body (type ids): the class the server created for each animal, with its values.
    /// </summary>
    [HttpPost("echo/zoo")]
    public IActionResult EchoZoo(Zoo zoo) => new JsonResult(new
    {
      zoo.Title,
      Animals = zoo.Animals?.Select(Describe),
      Favorite = Describe(zoo.Favorite),
      Anything = Describe(zoo.Anything),
      Addresses = zoo.Addresses?.ToList(),
    });

    private static object Describe(object value) => value is null ? null : new { Type = value.GetType().Name, Value = value }; // Value declared as object: System.Text.Json writes the properties of the class
  }

  internal static class Data
  {
    private static readonly string[] Companies = { "Acme Corporation", "Globex B.V.", "Initech Ltd.", "Umbrella GmbH", "Stark Industries", "Wayne Enterprises", "Hooli Inc.", "Vandelay Industries" };
    private static readonly string[] Contacts = { "Jan de Vries", "Maria Jansen", "Peter Smith", "Anna Müller", "Luca Rossi", "Sofia García", "Tom Bakker", "Emma Visser" };
    private static readonly string[] Streets = { "Keizersgracht", "Main Street", "Hauptstraße", "Rue de la Paix", "Via Roma", "Kalverstraat", "Baker Street", "Coolsingel" };
    private static readonly string[] Cities = { "Amsterdam", "Rotterdam", "London", "Berlin", "Paris", "Rome", "Madrid", "Utrecht" };
    private static readonly string[] Countries = { "NL", "NL", "GB", "DE", "FR", "IT", "ES", "NL" };
    private static readonly string[] Products = { "Consultancy services", "Software license (annual)", "Office chair, ergonomic", "USB-C docking station", "Printer paper A4, 500 sheets", "Travel expenses", "On-site support", "Cloud hosting, standard tier", "Coffee beans, 1kg", "Network cable Cat6, 5m" };
    private static readonly decimal[] VatRates = { 21m, 9m, 0m };

    internal static Invoice[] Invoices(int count)
    {
      Random rnd = new Random(42); // the same data on every call
      Invoice[] invoices = new Invoice[count];
      for (int t = 0; t < count; t++)
        invoices[t] = CreateInvoice(rnd, t + 1);
      return invoices;
    }

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
      byte[] id = new byte[16];
      rnd.NextBytes(id);

      Invoice invoice = new Invoice()
      {
        Id = new Guid(id),
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

    internal static Kinds KindValues() => new Kinds
    {
      Flag = true,
      SByte = -5,
      Byte = 200,
      Short = -30000,
      UShort = 60000,
      Int = -123456,
      UInt = 4000000000,
      Long = -9000000000,
      ULong = 9000000000000,
      Float = 1.5f,
      Double = -2.25,
      Decimal = 1234.5600m,
      Char = 'é',
      Text = "Hello, wörld",
      Guid = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
      DateTime = new DateTime(2026, 10, 13, 12, 34, 56, 789, DateTimeKind.Utc),
      DateTimeOffset = new DateTimeOffset(2026, 10, 13, 14, 34, 56, 789, TimeSpan.FromHours(2)),
      TimeSpan = new TimeSpan(1, 2, 3, 4, 500),
      DateOnly = new DateOnly(2026, 10, 13),
      TimeOnly = new TimeOnly(12, 34, 56, 250),
      Bytes = new byte[] { 1, 2, 3, 250 },
      Uri = new Uri("https://example.com/path?q=1"),
      Unit = UnitOfMeasure.Kilogram,
      NullableInt = 0, // a value, not left out
      NullableDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
      Point = new Point { X = 3, Y = -4 },
      Address = new Address { Street = "Main Street", City = "Utrecht" },
      Numbers = new List<int> { 1, 0, -1 },
      Addresses = new[] { new Address { City = "Paris" }, null },
      Prices = new Dictionary<string, decimal> { { "apple", 0.5m }, { "pear", 0m } },
      Names = new Dictionary<int, string> { { 1, "one" }, { 2, null } },
      Sound = "Meow",
      Seven = 0,
    };

    internal static Zoo Zoo() => new Zoo
    {
      Title = "City zoo",
      Animals = new List<Animal> { new Dog { Name = "Rex", Barks = 3 }, new Cat { Name = "Tom", Indoor = true }, new Dog { Name = "Quiet" } },
      Favorite = new Cat { Name = "Felix" },
      Anything = new Dog { Name = "Boxed", Barks = 1 },
      Addresses = new List<Address> { new Address { City = "Rome" } },
    };
  }
}
