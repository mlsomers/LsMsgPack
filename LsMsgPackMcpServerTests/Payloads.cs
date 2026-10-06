using LsMsgPack;
using System;
using System.Collections.Generic;

namespace LsMsgPackMcpServerTests
{
  public class McpAddress
  {
    public string Street { get; set; }
    public int Number { get; set; }
    public string City { get; set; }
  }

  public class McpLine
  {
    public string Product { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
  }

  public class McpInvoice
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public McpAddress Billing { get; set; }
    public List<McpLine> Lines { get; set; }
    public DateTime Date { get; set; }
    public Guid Reference { get; set; }
    public List<string> Tags { get; set; }
  }

  public abstract class McpAnimal
  {
    public string Name { get; set; }
  }

  public class McpDog : McpAnimal
  {
    public bool Barks { get; set; }
  }

  public class McpCat : McpAnimal
  {
    public int Lives { get; set; }
  }

  public class McpZoo
  {
    public List<McpAnimal> Animals { get; set; }
  }

  /// <summary>
  /// Payloads written by LsMsgPack in its different settings.
  /// </summary>
  public static class Payloads
  {
    public static readonly DateTime InvoiceDate = new DateTime(2026, 10, 5, 12, 30, 0, DateTimeKind.Utc);
    public static readonly Guid InvoiceReference = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

    public static McpInvoice Invoice()
    {
      return new McpInvoice()
      {
        Id = 1234,
        Customer = "Alice",
        Billing = new McpAddress() { Street = "Main street", Number = 12, City = "Amsterdam" },
        Lines = new List<McpLine>()
        {
          new McpLine() { Product = "Apples", Quantity = 3, Price = 1.25m },
          new McpLine() { Product = "Pears", Quantity = 2, Price = 0.99m }
        },
        Date = InvoiceDate,
        Reference = InvoiceReference,
        Tags = new List<string>() { "fruit", "fresh" }
      };
    }

    public static McpZoo Zoo()
    {
      return new McpZoo() { Animals = new List<McpAnimal>() { new McpDog() { Name = "Rex", Barks = true }, new McpCat() { Name = "Tom", Lives = 9 } } };
    }

    public static byte[] Serialize<T>(T value, ObjectLayout layout, bool schema)
    {
      return MsgPackSerializer.Serialize(value, new MsgPackSettings() { ObjectLayout = layout, UseInexedSchema = schema });
    }

    /// <summary>
    /// With a reference to the schema (<c>WriteSchemaReference</c>), the store holds it.
    /// </summary>
    public static byte[] SerializeWithReference<T>(T value, SchemaStore store)
    {
      MsgPackSettings settings = new MsgPackSettings() { ObjectLayout = ObjectLayout.Array, UseInexedSchema = true, SchemaStore = store, WriteSchemaReference = true };
      return MsgPackSerializer.Serialize(value, settings);
    }
  }
}
