using LsMsgPack;
using LsMsgPack.Generated;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LtMsgPack.GeneratorTests
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
      for (int version = GeneratedContract.MinSupported; version <= GeneratedContract.Current; version++)
      {
        string directory = Path.Combine(FixtureDirectory, "v" + version);
        string[] sources = Directory.GetFiles(directory, "*.txt").OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText).ToArray();
        Assembly assembly = GeneratorHarness.CompileAndLoad("FixtureV" + version, sources);

        LtMsgPackContext context = (LtMsgPackContext)assembly.GetType("Shop.ShopMsgPackContext").GetProperty("Default").GetValue(null);
        Assert.AreEqual(version, context.ContractVersion);
        Assert.IsTrue(context.IsUsable, $"version {version} is supported");

        object invoice = Sample(assembly);
        Type type = invoice.GetType();
        foreach (bool schema in new[] { false, true })
          foreach (ObjectLayout layout in new[] { ObjectLayout.Array, ObjectLayout.Map })
          {
            LtMsgPackSerializer runtime = new LtMsgPackSerializer(new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, UseRegisteredContexts = false });
            LtMsgPackSerializer generated = new LtMsgPackSerializer(new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, UseRegisteredContexts = false, Contexts = new[] { context } });
            byte[] expected = runtime.Serialize(invoice, type);
            CollectionAssert.AreEqual(expected, generated.Serialize(invoice, type), $"v{version} schema={schema} {layout}: bytes");
            CollectionAssert.AreEqual(expected, runtime.Serialize(generated.Deserialize(type, expected), type), $"v{version} schema={schema} {layout}: values");
          }
      }
    }

    /// <summary>
    /// An invoice of the fixture's model (GeneratorTests.InvoiceModel), filled by reflection.
    /// </summary>
    private static object Sample(Assembly assembly)
    {
      object address = Create(assembly, "Shop.Address", ("Street", "Main street 1"), ("City", "Utrecht"));
      object customer = Create(assembly, "Shop.Customer", ("CustomerId", 7), ("CompanyName", "Infotopie"), ("BillingAddress", address));
      Type lineType = assembly.GetType("Shop.InvoiceLine");
      IList lines = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(lineType));
      lines.Add(Create(assembly, "Shop.InvoiceLine", ("LineNumber", 1), ("Quantity", 2.5m), ("Unit", Enum.ToObject(assembly.GetType("Shop.UnitOfMeasure"), 1))));
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
        type.GetProperty(name).SetValue(instance, value);
      return instance;
    }
  }
}
