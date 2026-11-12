using LsMsgPack;
using LsMsgPack.Generated;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LsMsgPack.GeneratorTests
{
  /// <summary>
  /// Generated code of every supported contract version (Fixtures/v*, see Fixtures/README.md) still works with this runtime:
  /// an additive change of the contract must not break code generated before it (see GeneratedContract).
  /// </summary>
  [TestClass]
  public class ContractFixtureTests
  {
    private static string FixtureDirectory
    {
      get { return typeof(ContractFixtureTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "FixtureDirectory").Value; }
    }

    [TestMethod]
    public void EverySupportedVersion_HasAFixture()
    {
      for (int version = GeneratedContract.MinSupported; version <= GeneratedContract.Current; version++)
        Assert.IsTrue(Directory.Exists(Path.Combine(FixtureDirectory, "v" + version)), $"Fixtures/v{version} is missing (see Fixtures/README.md)");
    }

    [TestMethod]
    public void GeneratedCodeOfSupportedVersions_IsUsedAndGivesTheSameData()
    {
      // All versions are loaded first and their types registered: the fixtures share their class names (Invoice, Money...), and short type names are cached for the process
      // when only one class has the name (TypeResolver), so a later version would read the schema's names as the classes of an earlier one. With all registered, every name is ambiguous
      // and the readers pair the entries with their own classes (BindReaderTypes, entries found by the name of the class read), as with duplicate names in an application.
      Assembly[] assemblies = new Assembly[GeneratedContract.Current + 1];
      for (int version = GeneratedContract.MinSupported; version <= GeneratedContract.Current; version++)
      {
        string directory = Path.Combine(FixtureDirectory, "v" + version);
        string[] sources = Directory.GetFiles(directory, "*.txt").OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText).ToArray();
        assemblies[version] = GeneratorHarness.CompileAndLoad("FixtureV" + version, sources);
        MsgPackTypes.CacheAssemblyTypes(assemblies[version].GetType("Shop.Invoice"));
      }

      for (int version = GeneratedContract.MinSupported; version <= GeneratedContract.Current; version++)
      {
        Assembly assembly = assemblies[version];

        LsMsgPackContext context = (LsMsgPackContext)assembly.GetType("Shop.ShopMsgPackContext").GetProperty("Default").GetValue(null);
        Assert.AreEqual(version, context.ContractVersion);
        Assert.IsTrue(context.IsUsable, $"version {version} is supported");

        object invoice = Sample(assembly);
        Type type = invoice.GetType();
        foreach (bool schema in new[] { false, true })
          foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          {
            LsMsgPackSerializer runtime = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, UseRegisteredContexts = false });
            LsMsgPackSerializer generated = new LsMsgPackSerializer(new LsMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, UseRegisteredContexts = false, Contexts = new[] { context } });
            byte[] expected = runtime.Serialize(invoice, type);
            CollectionAssert.AreEqual(expected, generated.Serialize(invoice, type), $"v{version} schema={schema} {layout}: bytes");
            CollectionAssert.AreEqual(expected, runtime.Serialize(generated.Deserialize(type, expected), type), $"v{version} schema={schema} {layout}: values");
          }
      }
    }

    /// <summary>
    /// An invoice of the fixture's model (GeneratorTests.InvoiceModel), filled by reflection (the struct Money and the fields only where the model has them).
    /// </summary>
    private static object Sample(Assembly assembly)
    {
      bool fields = assembly.GetType("Shop.Address").GetField("PostalCode") != null; // version 4 and later
      object address = fields
        ? Create(assembly, "Shop.Address", ("Street", "Main street 1"), ("City", "Utrecht"), ("PostalCode", "3511 AB"))
        : Create(assembly, "Shop.Address", ("Street", "Main street 1"), ("City", "Utrecht"));
      object customer = Create(assembly, "Shop.Customer", ("CustomerId", 7), ("CompanyName", "Infotopie"), ("BillingAddress", address));
      Type lineType = assembly.GetType("Shop.InvoiceLine");
      IList lines = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(lineType));
      object first = Create(assembly, "Shop.InvoiceLine", ("LineNumber", 1), ("Quantity", 2.5m), ("Unit", Enum.ToObject(assembly.GetType("Shop.UnitOfMeasure"), 1)));
      if (assembly.GetType("Shop.Money") != null) // version 3 and later: a struct
        lineType.GetProperty("UnitPrice").SetValue(first, fields
          ? Create(assembly, "Shop.Money", ("Amount", 100.99m), ("Currency", "EUR"), ("Precision", (byte)2))
          : Create(assembly, "Shop.Money", ("Amount", 100.99m), ("Currency", "EUR")));
      if (fields)
        Set(first, "_discountPercent", 15);
      lines.Add(first);
      lines.Add(Create(assembly, "Shop.InvoiceLine", ("LineNumber", 2)));
      return Create(assembly, "Shop.Invoice",
        ("Id", new Guid("0f8fad5b-d9cb-469f-a165-70867728950e")),
        ("InvoiceDate", new DateTime(2026, 10, 12, 8, 30, 0, DateTimeKind.Utc)),
        ("PaidDate", (DateTime?)new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)),
        ("Status", Enum.ToObject(assembly.GetType("Shop.InvoiceStatus"), 2)),
        ("Customer", customer),
        ("Lines", lines),
        ("Total", 252.47m));
    }

    private static object Create(Assembly assembly, string typeName, params (string Name, object Value)[] values)
    {
      Type type = assembly.GetType(typeName);
      object instance = Activator.CreateInstance(type);
      foreach ((string name, object value) in values)
        Set(instance, name, value);
      return instance;
    }

    /// <summary>
    /// Sets a property, or a field (also a private one).
    /// </summary>
    private static void Set(object instance, string name, object value)
    {
      PropertyInfo property = instance.GetType().GetProperty(name);
      if (property != null)
        property.SetValue(instance, value);
      else
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);
    }
  }
}
