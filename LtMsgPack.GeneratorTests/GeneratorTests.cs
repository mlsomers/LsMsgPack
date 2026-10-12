using LsMsgPack.Generated;
using LtMsgPack.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LtMsgPack.GeneratorTests
{
  [TestClass]
  public class GeneratorTests
  {
    /// <summary>
    /// The invoice model of the benchmarks (BenchmarkInvoices), with a context.
    /// </summary>
    internal const string InvoiceModel = @"
using System;
using System.Collections.Generic;
using LtMsgPack;

namespace Shop
{
  public enum InvoiceStatus { Draft, Sent, Paid }
  public enum UnitOfMeasure : byte { Piece, Hour, Kilogram }

  public class Address
  {
    public string Street { get; set; }
    public string City { get; set; }
  }

  public class Customer
  {
    public int CustomerId { get; set; }
    public string CompanyName { get; set; }
    public Address BillingAddress { get; set; }
  }

  public class InvoiceLine
  {
    public int LineNumber { get; set; }
    public decimal Quantity { get; set; }
    public UnitOfMeasure Unit { get; set; }
  }

  public class Invoice
  {
    public Guid Id { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public InvoiceStatus Status { get; set; }
    public Customer Customer { get; set; }
    public List<InvoiceLine> Lines { get; set; }
    public decimal Total { get; set; }
  }

  [LtMsgPackSerializable(typeof(Invoice))]
  public partial class ShopMsgPackContext : LtMsgPackContext { }
}
";

    [TestMethod]
    public void InvoiceModel_Snapshot()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(InvoiceModel);
      result.AssertCompiles();
      Assert.IsEmpty(result.CompileProblems, string.Join("\n", result.CompileProblems.Select(p => p.ToString())) + "\n" + result.AllGenerated);
      GeneratorHarness.AssertSnapshot(result, nameof(InvoiceModel_Snapshot));
    }

    [TestMethod]
    public void InvoiceModel_CompilesAsCSharp73ForNetStandard20()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(InvoiceModel, LanguageVersion.CSharp7_3, netStandard20: true);
      result.AssertCompiles();
      Assert.IsFalse(result.Generated.ContainsKey("LtMsgPack.Registration.g.cs"), "C# 7.3 has no module initializers");
      Assert.IsTrue(result.GeneratorDiagnostics.Any(d => d.Id == "LTMP009"), "Reports that the context is not registered");
      Assert.DoesNotContain("DynamicDependency", result.AllGenerated, ".NET Standard 2.0 does not have it");
    }

    [TestMethod]
    public void CSharp9ForNetStandard20_WritesTheModuleInitializerAttribute()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(InvoiceModel, LanguageVersion.CSharp9, netStandard20: true);
      result.AssertCompiles();
      Assert.Contains("class ModuleInitializerAttribute", result.Generated["LtMsgPack.Registration.g.cs"]);
    }

    [TestMethod]
    public void Net8_RegistersWithoutPolyfills()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(InvoiceModel);
      string registration = result.Generated["LtMsgPack.Registration.g.cs"];
      Assert.Contains("GeneratedTypes.Register(global::Shop.ShopMsgPackContext.Default)", registration);
      Assert.DoesNotContain("class ModuleInitializerAttribute", registration);
      Assert.Contains("DynamicDependency", result.AllGenerated);
    }

    [TestMethod]
    public void ReachableTypes_AreIncluded()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(InvoiceModel);
      string code = result.AllGenerated;
      foreach (string expected in new[] {
        "Object<global::Shop.Invoice>(() => new global::Shop.Invoice()",
        "Object<global::Shop.Customer>(",
        "Object<global::Shop.Address>(",
        "Object<global::Shop.InvoiceLine>(",
        "Enum<global::Shop.InvoiceStatus>()",
        "Enum<global::Shop.UnitOfMeasure>()",
        "Nullable<global::System.DateTime>()",
        "List<global::Shop.InvoiceLine>()",
        "Property<global::Shop.Invoice, global::System.Guid>(\"Id\", o => o.Id, (o, v) => o.Id = v)" })
        Assert.Contains(expected, code);
    }

    [TestMethod]
    public void ContractVersion_IsTheRuntimes()
    {
      Assert.AreEqual(GeneratedContract.Current, Emitter.ContractVersion);
    }

    [TestMethod]
    public void GeneratedContext_IsUsableAndRegistered()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(InvoiceModel.Replace("namespace Shop", "namespace ShopLoaded"), assemblyName: "ShopLoaded");
      Assembly assembly = GeneratorHarness.Load(result);
      Type contextType = assembly.GetType("ShopLoaded.ShopMsgPackContext");
      LtMsgPackContext context = (LtMsgPackContext)contextType.GetProperty("Default").GetValue(null); // runs the module initializer
      Assert.IsTrue(context.IsUsable);
      Assert.AreEqual(GeneratedContract.Current, context.ContractVersion);

      MethodInfo find = typeof(GeneratedTypes).GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Type) }, null);
      Assert.IsNotNull(find.Invoke(null, new object[] { assembly.GetType("ShopLoaded.Invoice") }), "The module initializer registered the context");
    }

    #region Properties

    [TestMethod]
    public void Setters_InitOnlyAndNotAccessible_AreLeftToReflection()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(@"
using LtMsgPack;
namespace M
{
  public class Settable
  {
    public string Init { get; init; }
    public string PrivateSet { get; private set; }
    public string PrivateGet { private get; set; }
    public string ReadOnly { get; }
    internal string Internal { get; set; }
    public static string Static { get; set; }
    public string this[int index] { get { return null; } set { } }
    public string @class { get; set; }
  }

  [LtMsgPackSerializable(typeof(Settable))]
  public partial class Context : LtMsgPackContext { }
}");
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.Contains("Property<global::M.Settable, string>(\"Init\", o => o.Init, null)", code);
      Assert.Contains("Property<global::M.Settable, string>(\"PrivateSet\", o => o.PrivateSet, null)", code);
      Assert.Contains("Property<global::M.Settable, string>(\"PrivateGet\", null, (o, v) => o.PrivateGet = v)", code);
      Assert.Contains("Property<global::M.Settable, string>(\"ReadOnly\", o => o.ReadOnly, null)", code);
      Assert.Contains("Property<global::M.Settable, string>(\"class\", o => o.@class, (o, v) => o.@class = v)", code);
      Assert.DoesNotContain("\"Internal\"", code, "not public: the runtime does not see it");
      Assert.DoesNotContain("\"Static\"", code);
      Assert.AreEqual(2, result.GeneratorDiagnostics.Count(d => d.Id == "LTMP004"), "Init and PrivateSet are set by reflection");
    }

    [TestMethod]
    public void Creator_OnlyWithAnAccessibleParameterlessConstructor()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(@"
using LtMsgPack;
namespace M
{
  public class WithArguments { public WithArguments(int a) { } public int A { get; set; } }
  public class PrivateConstructor { private PrivateConstructor() { } public int A { get; set; } }
  public class Required { public required int A { get; set; } }
  public abstract class Abstract { public int A { get; set; } }
  public class Plain { public int A { get; set; } }
  public class Root
  {
    public WithArguments W { get; set; }
    public PrivateConstructor P { get; set; }
    public Required R { get; set; }
    public Abstract X { get; set; }
    public Plain Y { get; set; }
  }

  [LtMsgPackSerializable(typeof(Root))]
  public partial class Context : LtMsgPackContext { }
}");
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.Contains("Object<global::M.WithArguments>(null,", code);
      Assert.Contains("Object<global::M.PrivateConstructor>(null,", code);
      Assert.Contains("Object<global::M.Required>(null,", code);
      Assert.Contains("Object<global::M.Abstract>(null,", code);
      Assert.Contains("Object<global::M.Plain>(() => new global::M.Plain(),", code);
    }

    [TestMethod]
    public void Inheritance_Generics_Collections_Structs()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(@"
using System.Collections.Generic;
using LtMsgPack;
namespace M
{
  public class Base { public int Id { get; set; } public virtual string Name { get; set; } }
  public class Derived : Base { public string Extra { get; set; } public override string Name { get; set; } }
  public class Wrapper<T> { public T Value { get; set; } }
  public struct Point { public int X { get; set; } public Base Owner { get; set; } }
  public interface IShape { int Corners { get; } }
  public class Outer { public class Inner { public int A { get; set; } } }
  public class Root
  {
    public Derived D { get; set; }
    public Wrapper<int> W { get; set; }
    public Dictionary<string, Outer.Inner> Map { get; set; }
    public HashSet<long> Set { get; set; }
    public Point[] Points { get; set; }
    public Point? Maybe { get; set; }
    public IShape Shape { get; set; }
    public IList<int> Numbers { get; set; }
    public int[,] Grid { get; set; }
    public byte[] Bytes { get; set; }
    public KeyValuePair<string, int> Pair { get; set; }
    public object Anything { get; set; }
  }

  [LtMsgPackSerializable(typeof(Root))]
  public partial class Context : LtMsgPackContext { }
}");
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.Contains("Object<global::M.Base>(() => new global::M.Base(),", code);
      Assert.Contains("Property<global::M.Derived, string>(\"Name\"", code, "an override belongs to the derived class (its declaring type at runtime)");
      Assert.Contains("Property<global::M.Base, string>(\"Name\"", code);
      Assert.Contains("Object<global::M.Wrapper<int>>(() => new global::M.Wrapper<int>(),", code);
      Assert.Contains("Property<global::M.Wrapper<int>, int>(\"Value\"", code);
      Assert.Contains("Collection<global::System.Collections.Generic.Dictionary<string, global::M.Outer.Inner>>(() => new global::System.Collections.Generic.Dictionary<string, global::M.Outer.Inner>())", code);
      Assert.Contains("Object<global::M.Outer.Inner>(", code);
      Assert.Contains("Collection<global::System.Collections.Generic.HashSet<long>>(", code);
      Assert.Contains("Array<global::M.Point>()", code);
      Assert.Contains("Boxed<global::M.Point>()", code);
      Assert.Contains("Nullable<global::M.Point>()", code);
      Assert.Contains("Object<global::M.IShape>(null)", code);
      Assert.Contains("Collection<global::System.Collections.Generic.IList<int>>(null)", code);
      Assert.Contains("Boxed<int[,]>()", code);
      Assert.Contains("Boxed<global::System.Collections.Generic.KeyValuePair<string, int>>()", code);
      Assert.Contains("Boxed<object>()", code);
      Assert.DoesNotContain("<byte[]>", code, "bin is not generic");
      Assert.DoesNotContain("Property<global::M.Point", code, "structs are written and read boxed");
    }

    #endregion

    #region Diagnostics

    private static void AssertDiagnostic(string source, string id)
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(source);
      Assert.IsTrue(result.GeneratorDiagnostics.Any(d => d.Id == id), id + " expected, got: " + string.Join("\n", result.GeneratorDiagnostics));
      Assert.IsEmpty(result.Generated, "nothing is written for a context with errors");
    }

    [TestMethod]
    public void Diagnostic_NotPartial()
    {
      AssertDiagnostic(@"using LtMsgPack; public class A { public int X { get; set; } } [LtMsgPackSerializable(typeof(A))] public abstract class C : LtMsgPackContext { protected C() : base(1) { } }", "LTMP001");
    }

    [TestMethod]
    public void Diagnostic_NotAContext()
    {
      AssertDiagnostic(@"using LtMsgPack; public class A { public int X { get; set; } } [LtMsgPackSerializable(typeof(A))] public partial class C { }", "LTMP002");
    }

    [TestMethod]
    public void Diagnostic_Abstract()
    {
      AssertDiagnostic(@"using LtMsgPack; public class A { public int X { get; set; } } [LtMsgPackSerializable(typeof(A))] public abstract partial class C : LtMsgPackContext { }", "LTMP005");
    }

    [TestMethod]
    public void Diagnostic_DeclaresConstructor()
    {
      AssertDiagnostic(@"using LtMsgPack; public class A { public int X { get; set; } } [LtMsgPackSerializable(typeof(A))] public partial class C : LtMsgPackContext { public C() : base(1) { } }", "LTMP007");
    }

    [TestMethod]
    public void Diagnostic_OpenGeneric()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(@"using LtMsgPack; public class W<T> { public T X { get; set; } } [LtMsgPackSerializable(typeof(W<>))] public partial class C : LtMsgPackContext { }");
      Assert.IsTrue(result.GeneratorDiagnostics.Any(d => d.Id == "LTMP008"));
      result.AssertCompiles(); // the context is written, without the open type
    }

    #endregion

    #region The context of the assembly (compile-time options)

    private const string Markers = @"
using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
namespace M
{
  [Serializable] public class SerializableOne { public int A { get; set; } }
  [DataContract] public class ContractOne { [DataMember] public int A { get; set; } }
  public class JsonOne { public int A { get; set; } }
  [JsonSerializable(typeof(JsonOne))] public partial class JsonContext { }
  public class Unmarked { public int A { get; set; } }
  [Serializable] public class MyException : Exception { public int Code { get; set; } }
}";

    [TestMethod]
    public void MsBuildProperty_Serializable()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(Markers, msbuild: new Dictionary<string, string>() { { "LtMsgPackGenerate", "Serializable" }, { "RootNamespace", "Root.Space" } });
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.Contains("namespace Root.Space", code);
      Assert.Contains("internal sealed partial class GeneratedMsgPackContext : global::LtMsgPack.LtMsgPackContext", code);
      Assert.Contains("Object<global::M.SerializableOne>", code);
      Assert.DoesNotContain("ContractOne", code);
      Assert.DoesNotContain("JsonOne", code);
      Assert.DoesNotContain("Unmarked", code);
      Assert.DoesNotContain("MyException", code, "exceptions are serializable for the binary formatter, not data");
    }

    [TestMethod]
    public void MsBuildProperty_DataContractAndJson()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(Markers, msbuild: new Dictionary<string, string>() { { "LtMsgPackGenerate", "DataContract;JsonSerializable" } });
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.Contains("Object<global::M.ContractOne>", code);
      Assert.Contains("Object<global::M.JsonOne>", code);
      Assert.DoesNotContain("SerializableOne", code);
      Assert.Contains("namespace TestModels", code, "the assembly name without a root namespace");
    }

    [TestMethod]
    public void AssemblyAttribute_WinsOverMsBuild()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(Markers.Replace("namespace M", "[assembly: LtMsgPack.LtMsgPackGenerate(Include = LtMsgPack.LtMsgPackInclude.AllTypes, ContextName = \"Other.Name.AllContext\", AutoRegister = false)]\nnamespace M"),
        msbuild: new Dictionary<string, string>() { { "LtMsgPackGenerate", "Serializable" } });
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.Contains("namespace Other.Name", code);
      Assert.Contains("partial class AllContext", code);
      foreach (string name in new[] { "SerializableOne", "ContractOne", "JsonOne", "Unmarked" })
        Assert.Contains("Object<global::M." + name + ">", code);
      Assert.DoesNotContain("MyException", code);
      Assert.IsFalse(result.Generated.ContainsKey("LtMsgPack.Registration.g.cs"), "AutoRegister = false");
    }

    [TestMethod]
    public void AssemblyContext_NameConflict()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(Markers + "namespace TestModels { public class GeneratedMsgPackContext { } }", msbuild: new Dictionary<string, string>() { { "LtMsgPackGenerate", "AllTypes" } });
      Assert.IsTrue(result.GeneratorDiagnostics.Any(d => d.Id == "LTMP006"));
      Assert.IsEmpty(result.Generated);
    }

    [TestMethod]
    public void NothingIsWrittenWithoutATrigger()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(Markers);
      Assert.IsEmpty(result.Generated);
    }

    [TestMethod]
    public void ParseInclude()
    {
      Assert.AreEqual(1 | 2 | 4 | 8, LtMsgPackGenerator.ParseInclude("AllTypes; serializable,DataContract | Json"));
      Assert.AreEqual(1, LtMsgPackGenerator.ParseInclude("true"));
      Assert.AreEqual(0, LtMsgPackGenerator.ParseInclude("nonsense"));
      Assert.AreEqual((int)(LtMsgPackInclude.AllTypes | LtMsgPackInclude.Serializable | LtMsgPackInclude.DataContract | LtMsgPackInclude.JsonSerializable), LtMsgPackGenerator.ParseInclude("AllTypes;Serializable;DataContract;JsonSerializable"));
    }

    #endregion
  }
}
