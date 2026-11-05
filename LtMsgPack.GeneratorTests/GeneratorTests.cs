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

  public struct Money
  {
    public decimal Amount { get; set; }
    public string Currency { get; set; }
  }

  public class InvoiceLine
  {
    public int LineNumber { get; set; }
    public decimal Quantity { get; set; }
    public UnitOfMeasure Unit { get; set; }
    public Money UnitPrice { get; set; }
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
        "Object<global::Shop.Invoice>(() => new global::Shop.Invoice(), new LtMsgPackCode0()",
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

    /// <summary>
    /// Collections the runtime fills itself (LsMsgPack.Core's CollectionInfo): what it makes for them must exist under NativeAOT.
    /// </summary>
    private const string CollectionsModel = @"
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using LtMsgPack;

namespace Shelf
{
  public class Item
  {
    public string Name { get; set; }
  }

  public class OnlyCollections // no member the generated code reads itself
  {
    public HashSet<string> Tags { get; set; }
  }

  public class Box
  {
    public IList<int> Counts { get; set; }
    public IReadOnlyList<Item> Items { get; set; }
    public HashSet<string> Tags { get; set; }
    public ImmutableArray<int> Fixed { get; set; }
    public IImmutableSet<string> Codes { get; set; }
    public FrozenSet<string> Frozen { get; set; }
    public ImmutableDictionary<string, int> Index { get; set; }
    public Dictionary<string, double> Weights { get; set; }
    public OnlyCollections Inner { get; set; }
  }

  [LtMsgPackSerializable(typeof(Box))]
  public partial class ShelfMsgPackContext : LtMsgPackContext { }
}
";

    [TestMethod]
    public void Collections_TheTypesTheRuntimeMakesAreDescribed()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(CollectionsModel);
      result.AssertCompiles();
      Assert.IsEmpty(result.CompileProblems, string.Join("\n", result.CompileProblems.Select(p => p.ToString()))); // no empty switch blocks (CS1522)
      string code = result.AllGenerated;
      foreach (string expected in new[] {
        "Array<int>()", // the elements are read into an array
        "Array<string>()",
        "Array<global::System.Collections.Generic.KeyValuePair<string, int>>()", // an immutable dictionary's factory takes the pairs
        "List<int>()", // created for IList<int>
        "CollectionInterface<global::System.Collections.Generic.IList<int>>()", // their typed readers
        "HashSet<string>()",
        "Dictionary<string, double>()",
        "List<global::Shelf.Item>()", // created for IReadOnlyList<Item>
        "Collection<global::System.Collections.Immutable.ImmutableArray<int>, int>(items => global::System.Collections.Immutable.ImmutableArray.CreateRange<int>(items))",
        "Collection<global::System.Collections.Immutable.ImmutableHashSet<string>, string>(items => global::System.Collections.Immutable.ImmutableHashSet.CreateRange<string>(items))", // created for IImmutableSet<string>
        "Collection<global::System.Collections.Frozen.FrozenSet<string>, string>(items => global::System.Collections.Frozen.FrozenSet.ToFrozenSet<string>(items))",
        "Collection<global::System.Collections.Immutable.ImmutableDictionary<string, int>, global::System.Collections.Generic.KeyValuePair<string, int>>(items => global::System.Collections.Immutable.ImmutableDictionary.CreateRange<string, int>(items))",
        "DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods, typeof(global::System.Collections.Immutable.ImmutableArray))" })
        Assert.Contains(expected, code, expected);
      Assert.DoesNotContain("Array<global::System.Collections.Generic.KeyValuePair<string, double>>()", code, "A dictionary is filled by its Add");
    }

    [TestMethod]
    public void ContractVersion_IsTheRuntimes()
    {
#pragma warning disable MSTEST0032 // both are constants, always equal when this compiles: it fails when one changes without the other
      Assert.AreEqual(GeneratedContract.Current, Emitter.ContractVersion);
#pragma warning restore MSTEST0032
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
    public void Setters_NotAccessible_AreLeftToReflection()
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
      Assert.Contains("Property<global::M.Settable, string>(\"Init\", o => o.Init, (o, v) => LtMsgPackSet", code);
      Assert.Contains("Property<global::M.Settable, string>(\"PrivateSet\", o => o.PrivateSet, null)", code);
      Assert.Contains("Property<global::M.Settable, string>(\"PrivateGet\", null, (o, v) => o.PrivateGet = v)", code);
      Assert.Contains("Property<global::M.Settable, string>(\"ReadOnly\", o => o.ReadOnly, null)", code);
      Assert.Contains("Property<global::M.Settable, string>(\"class\", o => o.@class, (o, v) => o.@class = v)", code);
      Assert.DoesNotContain("\"Internal\"", code, "not public: the runtime does not see it");
      Assert.DoesNotContain("\"Static\"", code);
      Assert.AreEqual(1, result.GeneratorDiagnostics.Count(d => d.Id == "LTMP004"), "PrivateSet is set by reflection");
      Assert.IsTrue(result.GeneratorDiagnostics.Any(d => d.Id == "LTMP004" && d.GetMessage().Contains("PrivateSet")));
    }

    // Names of their own (InitRoot, InitPair): a schema pairs classes by their short names across the assemblies loaded in the process, and Structs_SameDataAsTheRuntime loads a Root and a Pair
    private const string InitModel = @"
using LtMsgPack;
using System.Runtime.Serialization;
namespace I
{
  public class Parent
  {
    public string Inherited { get; init; }
  }

  public class Limits : Parent
  {
    public int Min { get; init; }
    public int? Max { get; init; }
    public string Name { get; init; }
    public System.Collections.Generic.List<int> Steps { get; init; }
    public int Settable { get; set; }
    public int InternalInit { get; internal init; }
    public int PrivateInit { get; private init; }
    [DataMember] public int OptedIn { get; private init; }
    public virtual int Virtual { get; init; }
  }

  public class Page<T>
  {
    public T Item { get; init; }
  }

  public readonly struct Point
  {
    public int X { get; init; }
    public int Y { get; init; }
  }

  public readonly record struct InitPair(int A, string B);

  public record Person(string Name, int Age);

  public class InitRoot
  {
    public Limits Limits { get; set; }
    public Point Point { get; set; }
    public InitPair Pair { get; set; }
    public Person Person { get; set; }
    public Page<int> Page { get; set; }
    public System.Collections.Generic.List<Point> Points { get; set; }
  }

  [LtMsgPackSerializable(typeof(InitRoot))]
  public partial class Context : LtMsgPackContext { }
}";

    [TestMethod]
    public void DebugBuild_Reported()
    {
      const string model = @"
using LtMsgPack;
namespace D
{
  public class Plain { public int Number { get; set; } }
  [LtMsgPackSerializable(typeof(Plain))] public partial class First : LtMsgPackContext { }
  [LtMsgPackSerializable(typeof(Plain))] public partial class Second : LtMsgPackContext { }
}";
      GeneratorHarness.Result debug = GeneratorHarness.Run(model, optimization: OptimizationLevel.Debug);
      Assert.AreEqual(1, debug.GeneratorDiagnostics.Count(d => d.Id == "LTMP011"), "once per compilation");
      Assert.AreEqual(DiagnosticSeverity.Info, debug.GeneratorDiagnostics.First(d => d.Id == "LTMP011").Severity);

      Assert.IsFalse(GeneratorHarness.Run(model, optimization: OptimizationLevel.Release).GeneratorDiagnostics.Any(d => d.Id == "LTMP011"), "a Release build");
      Assert.IsFalse(GeneratorHarness.Run("namespace D { public class Plain { } }", optimization: OptimizationLevel.Debug).GeneratorDiagnostics.Any(d => d.Id == "LTMP011"), "nothing generated");
    }

    [TestMethod]
    public void InitOnly_ThroughUnsafeAccessors()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(InitModel);
      result.AssertCompiles();
      Assert.IsEmpty(result.CompileProblems, string.Join("\n", result.CompileProblems.Select(p => p.ToString())) + "\n" + result.AllGenerated);
      string code = result.AllGenerated;
      foreach (string setter in new[] { "set_Min", "set_Max", "set_Name", "set_Steps", "set_Inherited", "set_InternalInit", "set_OptedIn", "set_X", "set_Y", "set_A", "set_B" })
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"" + setter + "\")]", code);
      Assert.Contains("(global::I.Parent o, string value);", code, "the accessor of a base class's property takes the base class");
      Assert.Contains("(ref global::I.Point o, int value);", code, "UnsafeAccessor takes a struct by reference");
      Assert.Contains("Property<global::I.Limits, int>(\"Min\", o => o.Min, (o, v) => LtMsgPackSet", code);
      Assert.Contains("StructProperty<global::I.Point, int>(\"X\", (ref global::I.Point o) => o.X, (ref global::I.Point o, int v) => LtMsgPackSet", code);
      Assert.DoesNotContain("o.Min = ", code, "does not compile outside an object initializer");

      // Left to reflection: what the UnsafeAccessor can't call, a non-public setter nothing opts in for
      Assert.DoesNotContain("set_Virtual", code);
      Assert.DoesNotContain("set_Item", code);
      Assert.DoesNotContain("set_PrivateInit", code);
      string[] reported = result.GeneratorDiagnostics.Where(d => d.Id == "LTMP004").Select(d => d.GetMessage()).ToArray();
      foreach (string name in new[] { "Virtual", "PrivateInit", "Item" })
        Assert.IsTrue(reported.Any(m => m.Contains("." + name + "'")), name + ": " + string.Join("\n", reported));
      Assert.HasCount(3, reported, string.Join("\n", reported));
    }

    [TestMethod]
    public void InitOnly_NotDependingOnNonPublicProperties()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(InitModel, msbuild: new Dictionary<string, string>() { { "LtMsgPackNonPublicProperties", "None" } });
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.Contains("Name = \"set_Min\")]", code, "the runtime sets a public init-only setter with every option");
      Assert.Contains("Name = \"set_InternalInit\")]", code, "as an accessible setter that is not init-only");
      Assert.DoesNotContain("set_OptedIn", code, "a non-public setter: only with NonPublicProperties");
    }

    [TestMethod]
    public void InitOnly_NetStandard20_LeftToReflection()
    {
      string model = "namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }\n"
        + "namespace I { public class Limits { public int Min { get; init; } } [LtMsgPack.LtMsgPackSerializable(typeof(Limits))] public partial class Context : LtMsgPack.LtMsgPackContext { } }";
      GeneratorHarness.Result result = GeneratorHarness.Run(model, LanguageVersion.CSharp9, netStandard20: true);
      result.AssertCompiles();
      Assert.DoesNotContain("UnsafeAccessor", result.AllGenerated);
      Assert.Contains("Property<global::I.Limits, int>(\"Min\", o => o.Min, null)", result.AllGenerated);
      Assert.IsTrue(result.GeneratorDiagnostics.Any(d => d.Id == "LTMP004" && d.GetMessage().Contains("init-only")));
    }

    [TestMethod]
    public void InitOnly_SameDataAsTheRuntime()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(InitModel.Replace("namespace I", "namespace ILoaded"), assemblyName: "ILoaded");
      Assembly assembly = GeneratorHarness.Load(result);
      LtMsgPackContext context = (LtMsgPackContext)assembly.GetType("ILoaded.Context").GetProperty("Default").GetValue(null);
      Type rootType = assembly.GetType("ILoaded.InitRoot");
      Type limitsType = assembly.GetType("ILoaded.Limits");
      Type pointType = assembly.GetType("ILoaded.Point");

      object limits = Activator.CreateInstance(limitsType);
      foreach ((string name, object value) in new (string, object)[] { ("Min", 3), ("Max", (int?)9), ("Name", "n"), ("Steps", new List<int>() { 1, 2 }), ("Settable", 4), ("Virtual", 5), ("Inherited", "i"), ("OptedIn", 6) })
        limitsType.GetProperty(name).SetValue(limits, value);
      object point = Activator.CreateInstance(pointType);
      pointType.GetProperty("X").SetValue(point, 7);
      pointType.GetProperty("Y").SetValue(point, -7);
      object root = Activator.CreateInstance(rootType);
      rootType.GetProperty("Limits").SetValue(root, limits);
      rootType.GetProperty("Point").SetValue(root, point);
      rootType.GetProperty("Pair").SetValue(root, Activator.CreateInstance(assembly.GetType("ILoaded.InitPair"), 3, "three"));
      rootType.GetProperty("Person").SetValue(root, Activator.CreateInstance(assembly.GetType("ILoaded.Person"), "p", 40));
      System.Collections.IList points = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pointType));
      points.Add(point);
      points.Add(Activator.CreateInstance(pointType));
      rootType.GetProperty("Points").SetValue(root, points);

      foreach (bool schema in new[] { false, true })
        foreach (LsMsgPack.ObjectLayout layout in new[] { LsMsgPack.ObjectLayout.Array, LsMsgPack.ObjectLayout.Map })
          foreach (bool trim in new[] { false, true })
          {
            LtMsgPackOptions options = new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, TrimTrailingNulls = trim, UseRegisteredContexts = false };
            LtMsgPackSerializer runtime = new LtMsgPackSerializer(options);
            options.Contexts = new[] { context };
            LtMsgPackSerializer generated = new LtMsgPackSerializer(options);
            string what = $"schema={schema} {layout} trim={trim}";
            byte[] expected = runtime.Serialize(root, rootType);
            CollectionAssert.AreEqual(expected, generated.Serialize(root, rootType), what + ": bytes");
            object read = generated.Deserialize(rootType, expected);
            CollectionAssert.AreEqual(expected, runtime.Serialize(read, rootType), what + ": values");

            object readLimits = rootType.GetProperty("Limits").GetValue(read);
            Assert.AreEqual(3, limitsType.GetProperty("Min").GetValue(readLimits), what);
            Assert.AreEqual("i", limitsType.GetProperty("Inherited").GetValue(readLimits), what);
            Assert.AreEqual(-7, pointType.GetProperty("Y").GetValue(rootType.GetProperty("Point").GetValue(read)), what);
          }
    }

    private const string PrivateModel = @"
using LtMsgPack;
using System.Runtime.Serialization;
namespace M
{
  [System.AttributeUsage(System.AttributeTargets.Property)]
  public sealed class MyIncludeAttribute : System.Attribute { }

  public class Base
  {
    [DataMember] private int BaseNumber { get; set; }
  }

  public class Plain
  {
    public int Number { get; set; }
  }

  public class Account : Base
  {
    public string Name { get; set; }
    public Plain Other { get; set; }
    [DataMember] private string Secret { get; set; }
    [MyInclude] protected int Pin { get; set; }
    [DataMember] public string Owner { get; private set; }
    [DataMember] internal string Note { get; set; }
    [DataMember] protected virtual int Virtual { get; set; }
    private string Unmarked { get; set; }
    public string PrivateSet { get; private set; }
  }

  public class Page<T>
  {
    [DataMember] private T Item { get; set; }
  }

  [LtMsgPackSerializable(typeof(Account))]
  [LtMsgPackSerializable(typeof(Page<int>))]
  public partial class Context : LtMsgPackContext { }
}";

    [TestMethod]
    public void NonPublic_Attributed_ThroughUnsafeAccessors()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(PrivateModel);
      result.AssertCompiles();
      Assert.IsEmpty(result.CompileProblems, string.Join("\n", result.CompileProblems.Select(p => p.ToString())) + "\n" + result.AllGenerated);
      string code = result.AllGenerated;
      foreach (string accessor in new[] { "get_Secret", "set_Secret", "get_Pin", "set_Pin", "set_Owner", "get_BaseNumber", "set_BaseNumber" })
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"" + accessor + "\")]", code);
      Assert.Contains("private static extern string LtMsgPackGet", code);
      Assert.Contains("(global::M.Base o, int value);", code, "the accessor of a base class's property takes the base class");
      Assert.Contains("Property<global::M.Account, string>(\"Note\", o => o.Note, (o, v) => o.Note = v)", code, "internal: accessible to the generated code");
      Assert.Contains("Property<global::M.Account, string>(\"Owner\", o => o.Owner, (o, v) => LtMsgPackSet", code);
      Assert.DoesNotContain("get_Virtual", code, "virtual: the runtime calls the override");
      Assert.DoesNotContain("get_Item", code, "generic classes need .NET 9");
      Assert.DoesNotContain("Unmarked", code, "not attributed");
      Assert.DoesNotContain("set_PrivateSet", code, "not attributed");
      Assert.Contains("NonPublicProperties | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(global::M.Account))", code);
      Assert.Contains("DynamicallyAccessedMemberTypes.PublicProperties | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(global::M.Plain))", code,
        "a type without non-public properties keeps its public ones only");
      Assert.IsTrue(result.GeneratorDiagnostics.Any(d => d.Id == "LTMP004" && d.GetMessage().Contains("Page<int>.Item")), "set by reflection");
      Assert.IsFalse(result.GeneratorDiagnostics.Any(d => d.Id == "LTMP004" && d.GetMessage().Contains("Secret")));
    }

    private const string StructModel = @"
using LtMsgPack;
using System.Collections.Generic;
using System.Runtime.Serialization;
namespace S
{
  public struct Vec
  {
    public float X { get; set; }
    public float Y { get; set; }
    public float Sum => X + Y;
    [DataMember] private int Hidden { get; set; }
    public int? Maybe { get; set; }
  }

  public struct Counter
  {
    public Counter() { Count = 1; }
    public int Count { get; set; }
  }

  public readonly record struct Pair(int A, string B);

  public class Root
  {
    public Vec V { get; set; }
    public Counter C { get; set; }
    public Pair P { get; set; }
    public List<Vec> Path { get; set; }
    public Vec? Target { get; set; }
  }

  [LtMsgPackSerializable(typeof(Root))]
  public partial class Context : LtMsgPackContext { }
}";

    [TestMethod]
    public void Structs_ByReference()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(StructModel);
      result.AssertCompiles();
      Assert.IsEmpty(result.CompileProblems, string.Join("\n", result.CompileProblems.Select(p => p.ToString())) + "\n" + result.AllGenerated);
      string code = result.AllGenerated;
      Assert.Contains("Struct<global::S.Vec>(null, new LtMsgPackCode", code, "no declared constructor: read into its default");
      Assert.Contains("StructProperty<global::S.Vec, float>(\"Sum\", (ref global::S.Vec o) => o.Sum, null)", code);
      Assert.Contains("private static extern int LtMsgPackGet", code);
      Assert.Contains("(ref global::S.Vec o);", code, "UnsafeAccessor takes a struct by reference");
      Assert.Contains("(ref global::S.Vec o, int value);", code);
      Assert.Contains("(ref global::S.Vec o) => LtMsgPackGet", code);
      Assert.Contains("Struct<global::S.Counter>(() => new global::S.Counter(), new LtMsgPackCode", code, "a declared parameterless constructor");
      Assert.Contains("StructProperty<global::S.Pair, int>(\"A\", (ref global::S.Pair o) => o.A, (ref global::S.Pair o, int v) => LtMsgPackSet", code, "init-only: through an UnsafeAccessor");
      Assert.IsFalse(result.GeneratorDiagnostics.Any(d => d.Id == "LTMP004" && d.GetMessage().Contains("Pair")), "not set by reflection");
      Assert.Contains("GeneratedStructCode<global::S.Vec>", code);
      Assert.Contains("Nullable<global::S.Vec>()", code);
      Assert.Contains("List<global::S.Vec>()", code);

      // Compiled for .NET Standard 2.0 in C# 7.3 (without what needs later versions: declared struct constructors, records): explicitly typed ref lambdas, no UnsafeAccessor
      string model = StructModel.Replace("    public Counter() { Count = 1; }\n", "").Replace("  public readonly record struct Pair(int A, string B);\n", "").Replace("    public Pair P { get; set; }\n", "");
      GeneratorHarness.Result old = GeneratorHarness.Run(model, LanguageVersion.CSharp7_3, netStandard20: true);
      old.AssertCompiles();
      Assert.DoesNotContain("UnsafeAccessor", old.AllGenerated);
    }

    [TestMethod]
    public void Structs_SameDataAsTheRuntime()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(StructModel.Replace("namespace S", "namespace SLoaded"), assemblyName: "SLoaded");
      Assembly assembly = GeneratorHarness.Load(result);
      LtMsgPackContext context = (LtMsgPackContext)assembly.GetType("SLoaded.Context").GetProperty("Default").GetValue(null);
      Type rootType = assembly.GetType("SLoaded.Root");
      Type vecType = assembly.GetType("SLoaded.Vec");
      object vec = Activator.CreateInstance(vecType);
      vecType.GetProperty("X").SetValue(vec, 1.5f);
      vecType.GetProperty("Y").SetValue(vec, -2f);
      vecType.GetProperty("Maybe").SetValue(vec, (int?)7);
      object root = Activator.CreateInstance(rootType);
      rootType.GetProperty("V").SetValue(root, vec);
      rootType.GetProperty("Target").SetValue(root, vec);
      rootType.GetProperty("P").SetValue(root, Activator.CreateInstance(assembly.GetType("SLoaded.Pair"), 3, "three"));
      System.Collections.IList path = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vecType));
      path.Add(vec);
      path.Add(Activator.CreateInstance(vecType));
      rootType.GetProperty("Path").SetValue(root, path);

      foreach (bool schema in new[] { false, true })
        foreach (LsMsgPack.ObjectLayout layout in new[] { LsMsgPack.ObjectLayout.Array, LsMsgPack.ObjectLayout.Map })
          foreach (bool trim in new[] { false, true })
          {
            LtMsgPackOptions options = new LtMsgPackOptions() { UseInexedSchema = schema, ObjectLayout = layout, TrimTrailingNulls = trim, UseRegisteredContexts = false };
            LtMsgPackSerializer runtime = new LtMsgPackSerializer(options);
            options.Contexts = new[] { context };
            LtMsgPackSerializer generated = new LtMsgPackSerializer(options);
            string what = $"schema={schema} {layout} trim={trim}";
            byte[] expected = runtime.Serialize(root, rootType);
            CollectionAssert.AreEqual(expected, generated.Serialize(root, rootType), what + ": bytes");
            // Against the runtime's reading: not always the original (Counter's constructor sets Count, which trimming may leave out)
            CollectionAssert.AreEqual(runtime.Serialize(runtime.Deserialize(rootType, expected), rootType), runtime.Serialize(generated.Deserialize(rootType, expected), rootType), what + ": values");
          }
    }

    [TestMethod]
    public void NonPublic_All_ByMsBuildProperty()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(PrivateModel, msbuild: new Dictionary<string, string>() { { "LtMsgPackNonPublicProperties", "All" } });
      result.AssertCompiles();
      string code = result.AllGenerated;
      foreach (string accessor in new[] { "get_Unmarked", "set_Unmarked", "set_PrivateSet", "get_Secret" })
        Assert.Contains("Name = \"" + accessor + "\")]", code);
    }

    [TestMethod]
    public void NonPublic_None_ByTheAssemblyAttribute()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(PrivateModel.Replace("namespace M", "[assembly: LtMsgPack.LtMsgPackGenerate(NonPublicProperties = LsMsgPack.TypeResolving.Filters.NonPublicProperties.None)]\nnamespace M"),
        msbuild: new Dictionary<string, string>() { { "LtMsgPackNonPublicProperties", "All" } });
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.DoesNotContain("UnsafeAccessor", code);
      Assert.DoesNotContain("Secret", code);
      Assert.DoesNotContain("NonPublicProperties", code);
    }

    [TestMethod]
    public void NonPublic_NetStandard20_LeftToReflection()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(PrivateModel.Replace("public class Page<T>", "public class Page<T> where T : struct"), LanguageVersion.CSharp7_3, netStandard20: true);
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.DoesNotContain("UnsafeAccessor", code);
      Assert.Contains("Property<global::M.Account, string>(\"Note\", o => o.Note, (o, v) => o.Note = v)", code);
      Assert.DoesNotContain("\"Secret\", o", code, "the runtime reads it by reflection");
      Assert.IsTrue(result.GeneratorDiagnostics.Any(d => d.Id == "LTMP004" && d.GetMessage().Contains("Secret")));
    }

    [TestMethod]
    public void NonPublic_RecordsAndExplicitImplementations_Never()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(@"
using LtMsgPack;
namespace M
{
  public interface IFace { int Face { get; set; } }

  public record Rec(string Name) : IFace
  {
    private int Hidden { get; set; }
    int IFace.Face { get; set; }
  }

  [LtMsgPackSerializable(typeof(Rec))]
  public partial class Context : LtMsgPackContext { }
}", msbuild: new Dictionary<string, string>() { { "LtMsgPackNonPublicProperties", "All" } });
      result.AssertCompiles();
      string code = result.AllGenerated;
      Assert.Contains("Name = \"get_Hidden\")]", code);
      Assert.DoesNotContain("EqualityContract", code);
      Assert.DoesNotContain("Face", code);
    }

    [TestMethod]
    public void ParseNonPublic()
    {
      Assert.AreEqual(0, LtMsgPackGenerator.ParseNonPublic(" none ", 1));
      Assert.AreEqual(1, LtMsgPackGenerator.ParseNonPublic("Attributed", 0));
      Assert.AreEqual(2, LtMsgPackGenerator.ParseNonPublic("ALL", 1));
      Assert.AreEqual(1, LtMsgPackGenerator.ParseNonPublic("nonsense", 1));
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
      Assert.Contains("Object<global::M.WithArguments>(null, new LtMsgPackCode", code);
      Assert.Contains("Object<global::M.PrivateConstructor>(null, new LtMsgPackCode", code);
      Assert.Contains("Object<global::M.Required>(null, new LtMsgPackCode", code);
      Assert.Contains("Object<global::M.Abstract>(null, null,", code, "no writer and reader for an abstract class (no instances of exactly its type)");
      Assert.Contains("Object<global::M.Plain>(() => new global::M.Plain(), new LtMsgPackCode", code);
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
      Assert.Contains("Object<global::M.Base>(() => new global::M.Base(), new LtMsgPackCode", code);
      Assert.Contains("Property<global::M.Derived, string>(\"Name\"", code, "an override belongs to the derived class (its declaring type at runtime)");
      Assert.Contains("Property<global::M.Base, string>(\"Name\"", code);
      Assert.Contains("Object<global::M.Wrapper<int>>(() => new global::M.Wrapper<int>(), new LtMsgPackCode", code);
      Assert.Contains("Property<global::M.Wrapper<int>, int>(\"Value\"", code);
      Assert.Contains("Dictionary<string, global::M.Outer.Inner>()", code);
      Assert.Contains("Object<global::M.Outer.Inner>(", code);
      Assert.Contains("HashSet<long>()", code);
      Assert.Contains("Array<global::M.Point>()", code);
      Assert.Contains("Struct<global::M.Point>(null, new LtMsgPackCode", code);
      Assert.Contains("StructProperty<global::M.Point, int>(\"X\", (ref global::M.Point o) => o.X, (ref global::M.Point o, int v) => o.X = v)", code);
      Assert.Contains("GeneratedStructCode<global::M.Point>", code);
      Assert.Contains("Nullable<global::M.Point>()", code);
      Assert.Contains("Object<global::M.IShape>(null, null)", code);
      Assert.Contains("CollectionInterface<global::System.Collections.Generic.IList<int>>()", code);
      Assert.Contains("Boxed<int[,]>()", code);
      Assert.Contains("Boxed<global::System.Collections.Generic.KeyValuePair<string, int>>()", code);
      Assert.Contains("Boxed<object>()", code);
      Assert.DoesNotContain("<byte[]>", code.Replace("p.Value<byte[]>", ""), "bin is not generic (its value goes to the runtime's handler)");
      Assert.Contains("p.Runtime(t, c, ref o, key, infos[t])", code, "the runtime writes Owner, by reference");
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

    #region Types only known at runtime (LTMP010)

    private const string Polymorphic = @"
using System.Collections.Generic;
using LtMsgPack;
namespace M
{
  public interface IShape { string Label { get; set; } }
  public abstract class Animal { public string Name { get; set; } }
  public class Circle : IShape { public string Label { get; set; } public double Radius { get; set; } }
  public class Dog : Animal { public bool Barks { get; set; } }
  public class Drawing
  {
    public IShape Main { get; set; }
    public List<IShape> Shapes { get; set; }
    public Dictionary<string, Animal> Pets { get; set; }
    public object Tag { get; set; }
    public string Title { get; set; }
  }

  [LtMsgPackSerializable(typeof(Drawing))]
  ROOTS
  public partial class Context : LtMsgPackContext { }
}";

    private static string[] Uncovered(GeneratorHarness.Result result)
    {
      return result.GeneratorDiagnostics.Where(d => d.Id == "LTMP010").Select(d => d.GetMessage()).OrderBy(m => m, StringComparer.Ordinal).ToArray();
    }

    [TestMethod]
    public void Uncovered_ReportedWhenPublishedAheadOfTime()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(Polymorphic.Replace("ROOTS", ""), msbuild: new Dictionary<string, string>() { { "PublishAot", "true" } });
      result.AssertCompiles();
      string[] messages = Uncovered(result);
      Assert.HasCount(4, messages, messages.Length + ": " + string.Join(" | ", messages));
      Assert.IsTrue(messages.Any(m => m.Contains("'M.Drawing.Main' holds values of 'M.IShape'")));
      Assert.IsTrue(messages.Any(m => m.Contains("'M.Drawing.Shapes' holds values of 'M.IShape'")), "the elements of a list");
      Assert.IsTrue(messages.Any(m => m.Contains("'M.Drawing.Pets' holds values of 'M.Animal'")), "the values of a dictionary");
      Assert.IsTrue(messages.Any(m => m.Contains("'M.Drawing.Tag' holds values of 'object'")));
      Assert.AreEqual("Model.cs", result.GeneratorDiagnostics.First(d => d.Id == "LTMP010").Location.GetLineSpan().Path, "at the property");
    }

    [TestMethod]
    public void Uncovered_NotReportedWithoutTrimmingOrAot()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(Polymorphic.Replace("ROOTS", ""));
      Assert.IsEmpty(Uncovered(result));
      foreach (string property in new[] { "PublishTrimmed", "IsAotCompatible", "IsTrimmable" })
        Assert.HasCount(4, Uncovered(GeneratorHarness.Run(Polymorphic.Replace("ROOTS", ""), msbuild: new Dictionary<string, string>() { { property, "true" } })), property);
    }

    [TestMethod]
    public void Uncovered_NotReportedForListedClasses()
    {
      GeneratorHarness.Result result = GeneratorHarness.Run(Polymorphic.Replace("ROOTS", "[LtMsgPackSerializable(typeof(Circle))] [LtMsgPackSerializable(typeof(Dog))]"),
        msbuild: new Dictionary<string, string>() { { "PublishAot", "true" } });
      result.AssertCompiles();
      string[] messages = Uncovered(result);
      Assert.HasCount(1, messages, "Circle implements IShape, Dog derives from Animal");
      Assert.Contains("'M.Drawing.Tag' holds values of 'object'", messages[0], "what an object holds cannot be told from the context");
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
