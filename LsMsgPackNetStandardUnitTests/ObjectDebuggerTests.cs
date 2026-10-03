using LsMsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ObjectDebugger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LsMsgPackUnitTests
{
  /// <summary>
  /// <see cref="RootObject.Reconstruct"/>: the object tree read back from the data alone, without the types.
  /// </summary>
  [TestClass]
  public class ObjectDebuggerTests
  {
    public class OdAddress
    {
      public string Street { get; set; }
      public int Number { get; set; }
      public string City { get; set; }
    }

    public class OdLine
    {
      public string Product { get; set; }
      public int Quantity { get; set; }
      public decimal Price { get; set; }
    }

    public class OdInvoice
    {
      public int Id { get; set; }
      public string Customer { get; set; }
      public OdAddress Billing { get; set; }
      public OdAddress Shipping { get; set; }
      public List<OdLine> Lines { get; set; }
      public DateTime Date { get; set; }
      public List<string> Tags { get; set; }
    }

    public abstract class OdAnimal
    {
      public string Name { get; set; }
    }

    public class OdDog : OdAnimal
    {
      public bool Barks { get; set; }
    }

    public class OdCat : OdAnimal
    {
      public int Lives { get; set; }
    }

    public class OdZoo
    {
      public List<OdAnimal> Animals { get; set; }
      public object Anything { get; set; }
    }

    public class OdName
    {
      public string First { get; set; }
      public string Last { get; set; }
    }

    /// <summary>
    /// All values are objects (looks like a collection), the same type twice.
    /// </summary>
    public class OdShipment
    {
      public OdAddress From { get; set; }
      public OdAddress To { get; set; }
      public List<List<int>> Boxes { get; set; }
      public OdName Contact { get; set; }
      public List<OdLine> Empty { get; set; }
    }

    private static OdInvoice CreateInvoice()
    {
      return new OdInvoice()
      {
        Id = 42,
        Customer = "Alice",
        Billing = new OdAddress() { Street = "Main street", Number = 1, City = "Springfield" },
        Shipping = new OdAddress() { Street = "Side street", Number = 22, City = "Shelbyville" },
        Lines = new List<OdLine>()
        {
          new OdLine() { Product = "Apple", Quantity = 3, Price = 0.5m },
          new OdLine() { Product = "Pear", Quantity = 1, Price = 0.75m }
        },
        Date = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
        Tags = new List<string>() { "fruit", "fresh" }
      };
    }

    private static MsgPackSettings Settings(ObjectLayout layout, bool schema)
    {
      return new MsgPackSettings() { ObjectLayout = layout, UseInexedSchema = schema };
    }

    /// <summary>
    /// Unpacks the way the explorer does (with KEEPTRACK the items of arrays and maps are kept, so the values refer to their items).
    /// </summary>
    private static RootObject Reconstruct(byte[] bytes, SchemaStore store = null)
    {
      MsgPackSettings settings = new MsgPackSettings();
      PropertyInfo preserve = typeof(MsgPackSettings).GetProperty("PreservePackages"); // only with KEEPTRACK
      if (preserve != null)
        preserve.SetValue(settings, true);

      RootObject root = new RootObject();
      root.Reconstruct(MsgPackItem.UnpackMultiple(bytes, settings), store);
      return root;
    }

    private static PrimitiveObject Member(ComplexObject obj, string name)
    {
      PrimitiveObject member = obj.Members.FirstOrDefault(m => m.Name == name);
      Assert.IsNotNull(member, $"{obj.Path} has no member {name}, it has: {string.Join(", ", obj.Members.Select(m => m.Name))}");
      return member;
    }

    private static ComplexObject Complex(ComplexObject obj, string name)
    {
      ComplexObject member = Member(obj, name) as ComplexObject;
      Assert.IsNotNull(member, $"{obj.Path}.{name} is not an object or collection");
      return member;
    }

    private static void AssertValue(object expected, ComplexObject obj, string name)
    {
      PrimitiveObject member = Member(obj, name);
      Assert.IsNull(member.Error, member.Error);
      Assert.AreEqual(Convert.ToString(expected, System.Globalization.CultureInfo.InvariantCulture), Convert.ToString(member.Value, System.Globalization.CultureInfo.InvariantCulture), member.Path);
    }

    private static void AssertNoIssues(RootObject root)
    {
      Assert.IsNull(root.Error, root.Error);
      Assert.AreEqual(0, root.Warnings.Count, string.Join(Environment.NewLine, root.Warnings));
      foreach (PrimitiveObject member in All(root))
        Assert.IsNull(member.Error, $"{member.Path}: {member.Error}");
    }

    private static IEnumerable<PrimitiveObject> All(ComplexObject obj)
    {
      foreach (PrimitiveObject member in obj.Members)
      {
        yield return member;
        if (member is ComplexObject complex)
          foreach (PrimitiveObject child in All(complex))
            yield return child;
      }
    }

    /// <param name="typed">With a schema the type names are known (inferred when there is no type id)</param>
    private static void AssertInvoice(RootObject root, bool typed)
    {
      AssertNoIssues(root);
      Assert.AreEqual(ObjectKind.Object, root.Kind);
      AssertValue(42, root, "Id");
      AssertValue("Alice", root, "Customer");

      ComplexObject billing = Complex(root, "Billing");
      Assert.AreEqual(ObjectKind.Object, billing.Kind);
      AssertValue("Main street", billing, "Street");
      AssertValue(1, billing, "Number");
      AssertValue("Springfield", billing, "City");

      ComplexObject shipping = Complex(root, "Shipping");
      AssertValue("Side street", shipping, "Street");
      AssertValue(22, shipping, "Number");

      ComplexObject lines = Complex(root, "Lines");
      Assert.AreEqual(ObjectKind.Collection, lines.Kind);
      Assert.AreEqual(2, lines.Members.Count);
      ComplexObject line = Complex(lines, "[1]");
      Assert.AreEqual(ObjectKind.Object, line.Kind);
      AssertValue("Pear", line, "Product");
      AssertValue(1, line, "Quantity");
      AssertValue(0.75m, line, "Price");
      StringAssert.EndsWith(Member(line, "Price").Path, "Lines[1].Price");

      ComplexObject tags = Complex(root, "Tags");
      Assert.AreEqual(ObjectKind.Collection, tags.Kind);
      AssertValue("fresh", tags, "[1]");

      Assert.AreEqual(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), ((DateTime)Member(root, "Date").Value).ToUniversalTime());

      CollectionAssert.AreEqual(new[] { "Billing", "Shipping", "Lines", "Tags" }, root.Children.Select(c => c.Name).ToArray());
      CollectionAssert.AreEqual(new[] { "Id", "Customer", "Date" }, root.PrimitiveProperties.Select(c => c.Name).ToArray());

      if (!typed)
        return;
      Assert.AreEqual(nameof(OdInvoice), root.Type);
      Assert.AreEqual(nameof(OdAddress), billing.Type);
      Assert.AreEqual(nameof(OdAddress), shipping.Type);
      Assert.AreEqual(nameof(OdLine), line.Type);
      Assert.IsTrue(line.TypeIsGuess);
    }

    [TestMethod]
    public void ArraysWithInlineSchema()
    {
      RootObject root = Reconstruct(MsgPackSerializer.Serialize(CreateInvoice(), Settings(ObjectLayout.Array, true)));

      Assert.IsNotNull(root.Schema);
      Assert.AreEqual(SchemaSource.Inline, root.Schema.Source);
      CollectionAssert.AreEqual(new[] { nameof(OdInvoice), nameof(OdAddress), nameof(OdLine) }, root.Schema.Types.Select(t => t.Name).ToArray());
      AssertInvoice(root, true);
    }

    [TestMethod]
    public void MapsWithInlineSchema()
    {
      RootObject root = Reconstruct(MsgPackSerializer.Serialize(CreateInvoice(), Settings(ObjectLayout.Map, true)));
      AssertInvoice(root, true);
    }

    [TestMethod]
    public void MapsWithoutSchema()
    {
      RootObject root = Reconstruct(MsgPackSerializer.Serialize(CreateInvoice(), Settings(ObjectLayout.Map, false)));

      Assert.IsNull(root.Schema);
      AssertInvoice(root, false);
    }

    [TestMethod]
    public void ArraysWithoutSchema_ValuesByPosition()
    {
      RootObject root = Reconstruct(MsgPackSerializer.Serialize(CreateInvoice(), Settings(ObjectLayout.Array, false)));

      // Nothing tells the names, so the objects are shown as their values
      AssertNoIssues(root);
      AssertValue(42, root, "[0]");
      AssertValue("Springfield", Complex(root, "[2]"), "[2]");
    }

    [TestMethod]
    public void TrimmedTrailingNulls()
    {
      OdInvoice invoice = CreateInvoice();
      invoice.Billing.City = null;
      MsgPackSettings settings = Settings(ObjectLayout.Array, true);
      settings.TrimTrailingNulls = true;

      RootObject root = Reconstruct(MsgPackSerializer.Serialize(invoice, settings));

      ComplexObject billing = Complex(root, "Billing");
      Assert.AreEqual(nameof(OdAddress), billing.Type);
      Assert.AreEqual(2, billing.Members.Count);
      AssertValue("Side street", Complex(root, "Shipping"), "Street");
    }

    [TestMethod]
    public void SchemaReference()
    {
      SchemaStore store = new SchemaStore();
      MsgPackSettings settings = Settings(ObjectLayout.Array, true);
      settings.SchemaStore = store;
      settings.WriteSchemaReference = true;
      byte[] bytes = MsgPackSerializer.Serialize(CreateInvoice(), settings);

      RootObject withStore = Reconstruct(bytes, store);
      Assert.AreEqual(SchemaSource.Reference, withStore.Schema.Source);
      Assert.IsTrue(withStore.Schema.IsAvailable);
      Assert.AreEqual(store.GetSchemaIds()[0].ToString(), withStore.Schema.Id);
      AssertInvoice(withStore, true);

      // Without the schema there are no names, but the data is still shown
      RootObject withoutStore = Reconstruct(bytes);
      Assert.IsFalse(withoutStore.Schema.IsAvailable);
      Assert.IsNull(withoutStore.Error, withoutStore.Error);
      AssertValue("Alice", withoutStore, "[1]");
    }

    [TestMethod]
    public void TypeIds()
    {
      OdZoo zoo = new OdZoo()
      {
        Animals = new List<OdAnimal>() { new OdDog() { Name = "Rex", Barks = true }, new OdCat() { Name = "Tom", Lives = 9 } },
        Anything = 12.5m // not a primitive, so it gets a type id
      };

      foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        foreach (bool schema in new[] { true, false })
        {
          RootObject root = Reconstruct(MsgPackSerializer.Serialize(zoo, Settings(layout, schema)));
          string context = $"{layout}, schema {schema}";
          Assert.IsNull(root.Error, context);

          bool names = layout == ObjectLayout.Map || schema; // arrays without the schema have no names
          ComplexObject animals = Complex(root, names ? "Animals" : "[0]");
          ComplexObject dog = Complex(animals, "[0]");
          ComplexObject cat = Complex(animals, "[1]");
          Assert.AreEqual(nameof(OdDog), dog.Type, context);
          Assert.IsFalse(dog.TypeIsGuess, context);
          Assert.AreEqual(nameof(OdCat), cat.Type, context);

          PrimitiveObject anything = Member(root, names ? "Anything" : "[1]");
          Assert.AreEqual("Decimal", anything.Type, context);
          Assert.AreEqual(12.5m, anything.Value, context);

          if (!names)
            continue;
          AssertValue("Rex", dog, "Name");
          AssertValue(true, dog, "Barks");
          AssertValue(9, cat, "Lives");
        }
    }

    [TestMethod]
    public void CollectionAndPrimitiveRoots()
    {
      List<OdLine> lines = CreateInvoice().Lines;
      RootObject list = Reconstruct(MsgPackSerializer.Serialize(lines, Settings(ObjectLayout.Array, true)));
      Assert.AreEqual(ObjectKind.Collection, list.Kind);
      AssertValue("Apple", Complex(list, "[0]"), "Product");

      // An empty schema and one byte
      RootObject number = Reconstruct(MsgPackSerializer.Serialize(5, Settings(ObjectLayout.Array, true)));
      Assert.AreEqual(ObjectKind.Value, number.Kind);
      Assert.AreEqual(5, Convert.ToInt32(number.Value));

      Dictionary<string, OdAddress> byName = new Dictionary<string, OdAddress>() { { "home", CreateInvoice().Billing } };
      RootObject dictionary = Reconstruct(MsgPackSerializer.Serialize(byName, Settings(ObjectLayout.Array, true)));
      Assert.AreEqual(ObjectKind.Dictionary, dictionary.Kind);
      ComplexObject home = Complex(dictionary, "home");
      Assert.AreEqual(nameof(OdAddress), home.Type);
      AssertValue("Main street", home, "Street");
    }

    [TestMethod]
    public void InferringLookalikes()
    {
      OdShipment shipment = new OdShipment()
      {
        From = new OdAddress() { Street = "A", Number = 1, City = "X" },
        To = new OdAddress() { Street = "B", Number = 2, City = "Y" },
        Boxes = new List<List<int>>() { new List<int>() { 1, 2 }, new List<int>() { 3 } },
        Contact = new OdName() { First = "Bob", Last = "Smith" },
        Empty = new List<OdLine>()
      };

      foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
      {
        RootObject root = Reconstruct(MsgPackSerializer.Serialize(shipment, Settings(layout, true)));
        string context = layout.ToString();
        AssertNoIssues(root);
        Assert.AreEqual(nameof(OdShipment), root.Type, context);
        Assert.AreEqual(nameof(OdAddress), Complex(root, "To").Type, context);
        AssertValue("Y", Complex(root, "To"), "City");
        ComplexObject boxes = Complex(root, "Boxes");
        Assert.AreEqual(ObjectKind.Collection, boxes.Kind, context);
        AssertValue(3, Complex(boxes, "[1]"), "[0]");
        Assert.AreEqual(nameof(OdName), Complex(root, "Contact").Type, context);
        AssertValue("Smith", Complex(root, "Contact"), "Last");
        Assert.AreEqual(ObjectKind.Collection, Complex(root, "Empty").Kind, context);
      }
    }

    [TestMethod]
    public void BenchmarkInvoices_AllLayouts()
    {
      MethodInfo create = typeof(BenchmarkInvoices).GetMethod("CreateInvoices", BindingFlags.NonPublic | BindingFlags.Static);
      BenchmarkInvoices.Invoice[] invoices = (BenchmarkInvoices.Invoice[])create.Invoke(null, new object[] { 6 });

      foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
        foreach (bool trim in new[] { false, true })
        {
          MsgPackSettings settings = Settings(layout, true);
          settings.TrimTrailingNulls = trim;
          RootObject root = Reconstruct(MsgPackSerializer.Serialize(new List<BenchmarkInvoices.Invoice>(invoices), settings));
          string context = $"{layout}, trim {trim}";
          AssertNoIssues(root);
          Assert.AreEqual(ObjectKind.Collection, root.Kind, context);
          Assert.AreEqual(invoices.Length, root.Members.Count, context);

          for (int t = 0; t < invoices.Length; t++)
          {
            ComplexObject invoice = Complex(root, $"[{t}]");
            Assert.AreEqual(nameof(BenchmarkInvoices.Invoice), invoice.Type, context);
            AssertValue(invoices[t].InvoiceNumber, invoice, "InvoiceNumber");

            ComplexObject customer = Complex(invoice, "Customer");
            Assert.AreEqual(nameof(BenchmarkInvoices.Customer), customer.Type, context);
            AssertValue(invoices[t].Customer.Email, customer, "Email");
            Assert.AreEqual(nameof(BenchmarkInvoices.Address), Complex(customer, "ShippingAddress").Type, context);
            AssertValue(invoices[t].Customer.ShippingAddress.City, Complex(customer, "ShippingAddress"), "City");

            ComplexObject lines = Complex(invoice, "Lines");
            Assert.AreEqual(invoices[t].Lines.Count, lines.Members.Count, context);
            ComplexObject line = Complex(lines, "[0]");
            Assert.AreEqual(nameof(BenchmarkInvoices.InvoiceLine), line.Type, context);
            AssertValue(invoices[t].Lines[0].LineTotal, line, "LineTotal");
          }
        }
    }

    [TestMethod]
    public void SeveralPayloads()
    {
      MsgPackSettings settings = Settings(ObjectLayout.Array, true);
      byte[] first = MsgPackSerializer.Serialize(CreateInvoice(), settings);
      byte[] second = MsgPackSerializer.Serialize(CreateInvoice().Billing, settings);

      RootObject root = Reconstruct(first.Concat(second).ToArray());

      Assert.AreEqual(ObjectKind.Sequence, root.Kind);
      Assert.AreEqual(2, root.Members.Count);
      AssertInvoice((RootObject)root.Members[0], true);
      RootObject address = (RootObject)root.Members[1];
      Assert.AreEqual(nameof(OdAddress), address.Type);
      AssertValue("Springfield", address, "City");
    }

    [TestMethod]
    public void FindSchemaItems()
    {
      MsgPackSettings inline = Settings(ObjectLayout.Array, true);
      MsgPackSettings reference = Settings(ObjectLayout.Array, true);
      reference.SchemaStore = new SchemaStore();
      reference.WriteSchemaReference = true;
      byte[] first = MsgPackSerializer.Serialize(CreateInvoice(), inline);
      byte[] second = MsgPackSerializer.Serialize(CreateInvoice().Billing, reference);

      MpRoot root = MsgPackItem.UnpackMultiple(first.Concat(second).ToArray(), new MsgPackSettings());
      List<MsgPackItem> schemas = RootObject.FindSchemaItems(root);

      Assert.HasCount(2, schemas);
      Assert.AreSame(root[0], schemas[0]);
      Assert.IsInstanceOfType(schemas[0], typeof(MpMap));
      Assert.AreSame(root[2], schemas[1]);
      Assert.IsInstanceOfType(schemas[1], typeof(MpExt));

      Assert.IsEmpty(RootObject.FindSchemaItems(MsgPackItem.UnpackMultiple(MsgPackSerializer.Serialize(CreateInvoice(), Settings(ObjectLayout.Map, false)), new MsgPackSettings())));
    }

    [TestMethod]
    public void ItemReferences()
    {
      RootObject root = Reconstruct(MsgPackSerializer.Serialize(CreateInvoice(), Settings(ObjectLayout.Map, true)));
      Assert.IsNotNull(root.FirstItemRef);
      Assert.IsNotNull(root.LastItemRef);

      if (typeof(MsgPackSettings).GetProperty("PreservePackages") is null)
        return; // without KEEPTRACK the items of arrays and maps are not kept

      PrimitiveObject customer = Member(root, "Customer");
      Assert.IsInstanceOfType(customer.FirstItemRef, typeof(MpInt)); // the key: the property index
      Assert.IsInstanceOfType(customer.LastItemRef, typeof(MpString));
      Assert.AreEqual("Alice", customer.LastItemRef.Value);
    }
  }
}
