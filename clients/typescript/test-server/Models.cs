using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LsMsgPackTsTestServer
{
  // The invoice model of BenchmarkInvoices

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

  // A property of every kind the described schema names

  public struct Point
  {
    public int X { get; set; }
    public int Y { get; set; }
  }

  public class Kinds
  {
    public bool Flag { get; set; }
    public sbyte SByte { get; set; }
    public byte Byte { get; set; }
    public short Short { get; set; }
    public ushort UShort { get; set; }
    public int Int { get; set; }
    public uint UInt { get; set; }
    public long Long { get; set; }
    public ulong ULong { get; set; }
    public float Float { get; set; }
    public double Double { get; set; }
    public decimal Decimal { get; set; }
    public char Char { get; set; }
    public string Text { get; set; }
    public Guid Guid { get; set; }
    public DateTime DateTime { get; set; }
    public DateTimeOffset DateTimeOffset { get; set; }
    public TimeSpan TimeSpan { get; set; }
    public DateOnly DateOnly { get; set; }
    public TimeOnly TimeOnly { get; set; }
    public byte[] Bytes { get; set; }
    public Uri Uri { get; set; }
    public UnitOfMeasure Unit { get; set; }
    public int? NullableInt { get; set; }
    public DateTime? NullableDate { get; set; }
    public Point Point { get; set; }
    public Address Address { get; set; }
    public List<int> Numbers { get; set; }
    public Address[] Addresses { get; set; }
    public Dictionary<string, decimal> Prices { get; set; }
    public Dictionary<int, string> Names { get; set; }
    [DefaultValue("Woof")] public string Sound { get; set; } = "Woof";
    [DefaultValue(7)] public int Seven { get; set; } = 7;
  }

  // Polymorphism: type ids where the type differs from the declared one

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
    public string Title { get; set; }
    public List<Animal> Animals { get; set; }
    public Animal Favorite { get; set; }
    public object Anything { get; set; }
    public IEnumerable<Address> Addresses { get; set; } // a List: the collection gets a type id
  }
}
