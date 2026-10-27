using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static LsMsgPack.AnalyzerTests.AnalyzerHarness;

namespace LsMsgPack.AnalyzerTests
{
  [TestClass]
  public class AnalyzerTests
  {
    private const string Usings = "using System;\nusing System.Collections;\nusing System.Collections.Generic;\nusing System.ComponentModel;\nusing LsMsgPack;\nusing LtMsgPack;\n";

    private static void AssertDiagnostics(string source, params string[] expected)
    {
      AssertDiagnostics(Analyze(source), expected);
    }

    private static void AssertDiagnostics(ImmutableArray<Diagnostic> diagnostics, params string[] expected)
    {
      string[] actual = Summary(diagnostics);
      CollectionAssert.AreEqual(expected, actual, "Got:\n" + string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    #region LSMP001: initialized without [DefaultValue]

    private const string Initialized = Usings + @"
public enum Color { Red = 1, Green = 2 }
public class Order
{
  public int Retries { get; set; } = 3;
  public string Country { get; set; } = ""NL"";
  public long Big { get; set; } = 3;
  public decimal Rate { get; set; } = 1.5m;
  public Color Shade { get; set; } = Color.Green;
  public int? Count { get; set; } = 0;
  public byte Small { get; set; } = 4;
  public string Name { get; set; } = """";
  public int Zero { get; set; } = 0;
  public DateTime When { get; set; } = DateTime.Now;
  public int ReadOnly { get; } = 5;
  public int PrivateSet { get; private set; } = 5;
  [System.Text.Json.Serialization.JsonIgnore] public int Ignored { get; set; } = 5;
}
public static class Use { public static byte[] Write(Order o) => MsgPackSerializer.Serialize(o); }
";

    [TestMethod]
    public void MissingDefaultValue_OnlyWhereAValueIsLost()
    {
      ImmutableArray<Diagnostic> diagnostics = Analyze(Initialized);
      AssertDiagnostics(diagnostics, "LSMP001 Retries", "LSMP001 Country", "LSMP001 Big", "LSMP001 Rate", "LSMP001 Shade", "LSMP001 Count", "LSMP001 Small");
      Assert.AreEqual("'Order.Retries' is initialized to 3 but has no [DefaultValue(3)]: in a map (ObjectLayout.Map, application/msgpack) the serializers leave out the value 0, and a reader gets 3", diagnostics[0].GetMessage());
      Assert.AreEqual("'Order.Count' is initialized to 0 but has no [DefaultValue(0)]: in a map (ObjectLayout.Map, application/msgpack) the serializers leave out the value null, and a reader gets 0", diagnostics[5].GetMessage());
    }

    [TestMethod]
    [DataRow("Retries", "[DefaultValue(3)]\n  public int Retries { get; set; } = 3;")]
    [DataRow("Country", "[DefaultValue(\"NL\")]\n  public string Country")]
    [DataRow("Big", "[DefaultValue(3L)]\n  public long Big")]
    [DataRow("Rate", "[DefaultValue(typeof(decimal), \"1.5\")]\n  public decimal Rate")]
    [DataRow("Shade", "[DefaultValue(Color.Green)]\n  public Color Shade")]
    [DataRow("Count", "[DefaultValue(0)]\n  public int? Count")]
    [DataRow("Small", "[DefaultValue((byte)4)]\n  public byte Small")]
    public void MissingDefaultValue_Fix(string property, string expected)
    {
      ImmutableArray<Diagnostic> diagnostics = Analyze(Initialized);
      Diagnostic diagnostic = diagnostics.First(d => d.Location.SourceTree.GetText().ToString(d.Location.SourceSpan) == property);
      string argument = diagnostic.Properties["Argument"];
      // Only this property, so the first diagnostic is the one to fix
      string single = Usings.Replace("using System.ComponentModel;\n", "") + "public enum Color { Red = 1, Green = 2 }\npublic class Order\n{\n  /// <summary>Doc</summary>\n"
        + Initialized.Split('\n').First(l => l.Contains(" " + property + " {")) + "\n}\npublic static class Use { public static byte[] Write(Order o) => MsgPackSerializer.Serialize(o); }\n";
      string fixedSource = Fix(single, "LSMP001", "Add [DefaultValue(" + argument + ")]");
      StringAssert.Contains(fixedSource, "using System.ComponentModel;");
      StringAssert.Contains(fixedSource, "  /// <summary>Doc</summary>\n  " + expected, fixedSource);
      AssertFixed(fixedSource, "LSMP001");
    }

    [TestMethod]
    public void MissingDefaultValue_FixAfterOtherAttributes()
    {
      string source = Usings + "public class Item\n{\n  [Obsolete]\n  public int Level { get; set; } = 2;\n}\npublic static class Use { public static Item Read(byte[] b) => MsgPackSerializer.Deserialize<Item>(b); }\n";
      string fixedSource = Fix(source, "LSMP001", "Add [DefaultValue(2)]");
      StringAssert.Contains(fixedSource, "  [Obsolete]\n  [DefaultValue(2)]\n  public int Level { get; set; } = 2;", fixedSource);
      AssertFixed(fixedSource, "LSMP001");
    }

    [TestMethod]
    public void MissingDefaultValue_BackingFieldAndConstructor()
    {
      AssertDiagnostics(Usings + @"
public class Thing
{
  private int _level = 5;
  public int Level { get => _level; set => _level = value; }
  private string _name;
  public string Name { get { return _name; } set { _name = value; } }
  public bool Enabled { get; set; }
  public int Reset { get; set; } = 4;
  public Thing() { Name = ""x""; Enabled = true; Reset = 0; }
}
public static class Use { public static Thing Read(byte[] b) => new LtMsgPackSerializer().Deserialize<Thing>(b); }
", "LSMP001 Level", "LSMP001 Name", "LSMP001 Enabled");
    }

    [TestMethod]
    public void MissingDefaultValue_NotWithoutAParameterlessConstructor()
    {
      // A reader creates such objects without a constructor: a left out property keeps the type's default, as the writer assumes
      AssertDiagnostics(Usings + @"
public class Point { public Point(int x) { X = x; } public int X { get; set; } = 1; }
public struct Size { public int W { get; set; } = 1; public Size(int w) { W = w; } }
public static class Use { public static void Write(Point p, Size s) { MsgPackSerializer.Serialize(p); MsgPackSerializer.Serialize(s); } }
");
    }

    [TestMethod]
    public void MissingDefaultValue_ConstructorTakingValues()
    {
      // Created by the constructor that takes the values (as System.Text.Json): its parameters get the value the writer leaves out, the other properties keep what it and the initializers set
      AssertDiagnostics(Usings + @"
public class Owner
{
  public Owner(string name, int size = 2) { Name = name; Size = size; Mode = 3; }
  public string Name { get; set; } = ""n"";
  public int Size { get; set; }
  public int Mode { get; set; }
  public int Level { get; set; } = 2;
}
public record Item(string Name, int Count = 4) { public int Level { get; set; } = 2; }
public static class Use { public static void Write(Owner o, Item i) { MsgPackSerializer.Serialize(o); MsgPackSerializer.Serialize(i); } }
", "LSMP001 Mode", "LSMP001 Level", "LSMP001 Level");
    }

    #endregion

    #region Which types

    [TestMethod]
    public void TypesThatAreNotSerialized_AreLeftAlone()
    {
      string source = Usings + "public class ServerOptions { public int Port { get; set; } = 8080; public object Tag { get; set; } }\n";
      AssertDiagnostics(source);
      AssertDiagnostics(Analyze(source, msbuild: new Dictionary<string, string>() { { "LsMsgPackAnalyze", "AllTypes" } }), "LSMP001 Port", "LSMP004 Tag");
    }

    [TestMethod]
    public void ReachedTypes_PropertiesElementsDerivedClassesAndContexts()
    {
      AssertDiagnostics(Usings + @"
public abstract class Animal { public string Name { get; set; } }
public class Dog : Animal { public int Legs { get; set; } = 4; }
public class Leash { public int Length { get; set; } = 2; }
public class Owner { public Animal Pet { get; set; } public Dictionary<string, Leash> Leashes { get; set; } }
public class Unrelated { public int Size { get; set; } = 1; }
public class Listed { public int Width { get; set; } = 3; }
[LtMsgPackSerializable(typeof(Listed))]
public partial class AppContext { }
public static class Use { public static List<Owner> Read(byte[] b) => (List<Owner>)MsgPackSerializer.Deserialize(typeof(List<Owner>), b); }
", "LSMP001 Legs", "LSMP001 Length", "LSMP001 Width");
    }

    /// <summary>
    /// Generic methods that hand their type parameter to a serializer, also through an interface and an extension method: their callers' type arguments are serialized.
    /// </summary>
    [TestMethod]
    public void ReachedTypes_ThroughGenericWrappers()
    {
      AssertDiagnostics(Usings + @"
public interface IStore { byte[] Save<T>(T item); }
public class Store : IStore { public byte[] Save<T>(T item) => MsgPackSerializer.Serialize(item); }
public static class StoreExtensions { public static byte[] SaveTwice<TItem>(this IStore store, TItem item) { store.Save(item); return store.Save(item); } }
public class Saved { public int Version { get; set; } = 2; }
public class NotSaved { public int Version { get; set; } = 2; }
public static class Use { public static void Run(IStore store) => store.SaveTwice(new Saved()); }
", "LSMP001 Version");
    }

    [TestMethod]
    public void NeitherSerializerReferenced_NothingToAnalyze()
    {
      // The analyzer ships with LsMsgPack.Core: a project referencing only Core (e.g. for its options) is not analyzed
      AssertDiagnostics(Usings + "public class Model { public int Level { get; set; } = 2; }\n");
    }

    [TestMethod]
    public void OnlyCoreReferenced_AllTypesIsChecked()
    {
      // A project of models with the smallest package (LsMsgPack.Core) asks for its classes to be checked (and so marks its assembly for LSMP008)
      CSharpCompilation compilation = CSharpCompilation.Create("Models", new[] { CSharpSyntaxTree.ParseText("public class Model { public int Level { get; set; } = 2; }\n", path: "Model.cs") },
        new MetadataReference[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(MsgPackOptions).Assembly.Location) },
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
      AssertDiagnostics(Run(compilation, new Dictionary<string, string>() { { "LsMsgPackAnalyze", "AllTypes" } }), "LSMP001 Level");
    }

    #endregion

    #region LSMP002, LSMP003: [DefaultValue] that does not match

    private const string Mismatched = Usings + @"
public enum Color { Red = 1, Green = 2 }
public class Settings
{
  [DefaultValue(5)] public int Differs { get; set; } = 3;
  [DefaultValue(5)] public int NotSet { get; set; }
  [DefaultValue(1)] public long OtherType { get; set; } = 1;
  [DefaultValue(2)] public Color Shade { get; set; } = Color.Green;
  [DefaultValue(typeof(decimal), ""2.5"")] public decimal Rate { get; set; } = 2.5m;
  [DefaultValue(typeof(int), ""3"")] public long Typed { get; set; } = 3;
  [DefaultValue(4)] public int Matches { get; set; } = 4;
  [DefaultValue(null)] public string Nothing { get; set; }
  [DefaultValue(typeof(DateTime), ""2026-01-02"")] public DateTime Unset { get; set; }
}
public static class Use { public static byte[] Write(Settings s) => new LtMsgPackSerializer().Serialize(s); }
";

    [TestMethod]
    public void DefaultValue_Mismatches()
    {
      ImmutableArray<Diagnostic> diagnostics = Analyze(Mismatched);
      AssertDiagnostics(diagnostics, "LSMP002 DefaultValue(5)", "LSMP002 DefaultValue(5)", "LSMP003 DefaultValue(1)", "LSMP003 DefaultValue(2)", "LSMP003 DefaultValue(typeof(int), \"3\")", "LSMP002 DefaultValue(typeof(DateTime), \"2026-01-02\")");
      Assert.AreEqual("'Settings.Differs' has [DefaultValue(5)] but the constructor sets 3: in a map (ObjectLayout.Map, application/msgpack) the serializers leave out the value 5, and a reader gets 3", diagnostics[0].GetMessage());
      Assert.AreEqual("'Settings.NotSet' has [DefaultValue(5)] but the constructor sets 0: in a map (ObjectLayout.Map, application/msgpack) the serializers leave out the value 5, and a reader gets 0", diagnostics[1].GetMessage());
      Assert.AreEqual("The [DefaultValue] of 'Settings.OtherType' is 1 (int), not long: it never equals the value, so the value is always written", diagnostics[2].GetMessage());
    }

    [TestMethod]
    [DataRow("Differs", "Use [DefaultValue(3)]", "[DefaultValue(3)] public int Differs { get; set; } = 3;")]
    [DataRow("Differs", "Initialize Differs to 5", "[DefaultValue(5)] public int Differs { get; set; } = 5;")]
    [DataRow("NotSet", "Initialize NotSet to 5", "[DefaultValue(5)] public int NotSet { get; set; } = 5;")]
    [DataRow("OtherType", "Use [DefaultValue(1L)]", "[DefaultValue(1L)] public long OtherType { get; set; } = 1;")]
    [DataRow("Shade", "Use [DefaultValue(Color.Green)]", "[DefaultValue(Color.Green)] public Color Shade { get; set; } = Color.Green;")]
    public void DefaultValue_MismatchFixes(string property, string title, string expected)
    {
      string line = Mismatched.Split('\n').First(l => l.Contains(" " + property + " {"));
      string single = Usings + "public enum Color { Red = 1, Green = 2 }\npublic class Settings\n{\n" + line + "\n}\npublic static class Use { public static byte[] Write(Settings s) => new LtMsgPackSerializer().Serialize(s); }\n";
      string fixedSource = Fix(single, Analyze(single)[0].Id, title);
      StringAssert.Contains(fixedSource, "  " + expected, fixedSource);
      Assert.IsEmpty(Analyze(fixedSource), fixedSource);
    }

    #endregion

    #region LSMP004, LSMP005: the data picks the type

    [TestMethod]
    public void OpenProperties()
    {
      ImmutableArray<Diagnostic> diagnostics = Analyze(Usings + @"
public interface IShape { }
public class Square : IShape { public int Side { get; set; } }
public class Bag
{
  public object Value { get; set; }
  public List<object> Items { get; set; }
  public Dictionary<string, object> Extra { get; set; }
  public IDisposable Resource { get; set; }
  public System.IO.Stream Content { get; set; }
  public ArrayList Old { get; set; }
  public object[] Array { get; set; }
  public IShape Shape { get; set; }
  public List<IShape> Shapes { get; set; }
  public Type Kind { get; set; }
  public string Text { get; set; }
  public Dictionary<string, int> Counts { get; set; }
  public object ReadOnly { get; }
}
public static class Use { public static Bag Read(byte[] b) => MsgPackSerializer.Deserialize<Bag>(b); }
");
      AssertDiagnostics(diagnostics, "LSMP004 Value", "LSMP004 Items", "LSMP004 Extra", "LSMP004 Resource", "LSMP004 Content", "LSMP004 Old", "LSMP004 Array");
      StringAssert.StartsWith(diagnostics[1].GetMessage(), "'Bag.Items' is declared as List<object>: the data can make a reader create any type that fits object");
      StringAssert.StartsWith(diagnostics[3].GetMessage(), "'Bag.Resource' is declared as IDisposable: the data can make a reader create any type that fits IDisposable");
    }

    [TestMethod]
    public void OpenReads()
    {
      ImmutableArray<Diagnostic> diagnostics = Analyze(Usings + @"
public static class Use
{
  public static void Calls(byte[] b)
  {
    MsgPackSerializer.Deserialize<object>(b);
    new LtMsgPackSerializer().Deserialize<List<object>>(b);
    MsgPackSerializer.Deserialize(typeof(Dictionary<string, object>), b);
    MsgPackSerializer.Deserialize<List<int>>(b);
    MsgPackSerializer.Serialize<object>(1);
  }
}
");
      AssertDiagnostics(diagnostics, "LSMP005 object", "LSMP007 object", "LSMP005 List<object>", "LSMP005 typeof(Dictionary<string, object>)", "LSMP007 typeof(Dictionary<string, object>)");
      StringAssert.StartsWith(diagnostics[2].GetMessage(), "LtMsgPackSerializer.Deserialize reads List<object>: the data can make the reader create any type that fits object");
    }

    #endregion

    #region LSMP006: null read back as the initial value

    private const string NullableDefaults = Usings + @"
public class Options
{
  [DefaultValue(""auto"")] public string Mode { get; set; } = ""auto"";
  [DefaultValue(3)] public int? Retries { get; set; } = 3;
  [DefaultValue(typeof(decimal), ""1.5"")] public decimal? Rate { get; set; } = 1.5m;
  [DefaultValue(3)] public int Count { get; set; } = 3;
  [DefaultValue("""")] public string Empty { get; set; } = """";
  public string Name { get; set; }
  [DefaultValue(null)] public string Nothing { get; set; }
}
public static class Use { public static byte[] Write(Options o) => MsgPackSerializer.Serialize(o); }
";

    [TestMethod]
    public void NullReadAsInitial_NullablesWithTheirDefault()
    {
      ImmutableArray<Diagnostic> diagnostics = Analyze(NullableDefaults);
      AssertDiagnostics(diagnostics, "LSMP006 Mode", "LSMP006 Retries", "LSMP006 Rate", "LSMP006 Empty");
      Assert.AreEqual("'Options.Mode' is initialized to \"auto\": in an object written as an array of its values (ObjectLayout.Array, the default) null is nil, the same as the left out [DefaultValue], so a reader gets \"auto\". "
        + "Remove the initializer and the [DefaultValue] if null must arrive, or write objects as maps (ObjectLayout.Map).", diagnostics[0].GetMessage());
    }

    [TestMethod]
    public void NullReadAsInitial_AfterTheFixOfMissingDefaultValue()
    {
      // LSMP001's fix keeps null in maps, LSMP006 then tells about the array layout
      string source = Usings + "public class Item\n{\n  public string Mode { get; set; } = \"auto\";\n}\npublic static class Use { public static Item Read(byte[] b) => MsgPackSerializer.Deserialize<Item>(b); }\n";
      AssertDiagnostics(source, "LSMP001 Mode");
      AssertDiagnostics(Fix(source, "LSMP001", "Add [DefaultValue(\"auto\")]"), "LSMP006 Mode");
    }

    [TestMethod]
    public void NullReadAsInitial_NotWithoutAParameterlessConstructor()
    {
      AssertDiagnostics(Usings + @"
public class Point { public Point(int x) { } [DefaultValue(""a"")] public string Label { get; set; } = ""a""; }
public static class Use { public static byte[] Write(Point p) => MsgPackSerializer.Serialize(p); }
");
    }

    #endregion

    #region LSMP007: read without property names

    [TestMethod]
    public void UntypedReads()
    {
      ImmutableArray<Diagnostic> diagnostics = Analyze(Usings + @"
public static class Use
{
  public static void Calls(byte[] b)
  {
    MsgPackSerializer.Deserialize<IDictionary<string, object>>(b);
    MsgPackSerializer.Deserialize<IReadOnlyDictionary<string, dynamic>>(b);
    MsgPackSerializer.Deserialize<System.Dynamic.ExpandoObject>(b);
    MsgPackSerializer.Deserialize<Dictionary<string, int>>(b);
    MsgPackSerializer.Deserialize<Dictionary<int, object>>(b);
    MsgPackSerializer.Serialize<Dictionary<string, object>>(null);
  }
}
");
      string[] untyped = Summary(diagnostics).Where(s => s.StartsWith("LSMP007", System.StringComparison.Ordinal)).ToArray();
      CollectionAssert.AreEqual(new[] { "LSMP007 IDictionary<string, object>", "LSMP007 IReadOnlyDictionary<string, dynamic>", "LSMP007 System.Dynamic.ExpandoObject" }, untyped);
      StringAssert.StartsWith(diagnostics.First(d => d.Id == "LSMP007").GetMessage(),
        "MsgPackSerializer.Deserialize reads IDictionary<string, object>: an object written from a class arrives without its property names, and a dictionary cannot be filled from it (MsgPackException)");
    }

    #endregion

    #region Controllers

    private const string Controller = Usings + @"
using Microsoft.AspNetCore.Mvc;
public class Invoice { public int Copies { get; set; } = 1; }
public class Note { public int Stars { get; set; } = 5; }
public class InvoicesController : ControllerBase
{
  public ActionResult<Invoice> Get() => new Invoice();
  public void Post([FromBody] object body) { }
  public void Put([FromServices] IServiceProvider services, Note note) { }
  [NonAction] public Note Helper() => null;
}
";

    [TestMethod]
    public void Controllers_WithTheFormatters()
    {
      ImmutableArray<Diagnostic> diagnostics = Analyze(Controller, formatters: true);
      AssertDiagnostics(diagnostics, "LSMP001 Copies", "LSMP001 Stars", "LSMP005 body", "LSMP007 body");
      StringAssert.StartsWith(diagnostics[2].GetMessage(), "The action InvoicesController.Post reads object");
    }

    /// <summary>
    /// Actions returning IActionResult: the values passed to Ok(value), CreatedAtAction(..., value), BadRequest(error)... and to new ...ObjectResult(value) are serialized.
    /// </summary>
    [TestMethod]
    public void Controllers_ValuesOfResults()
    {
      string source = Usings + @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
public class Order { public int Copies { get; set; } = 1; }
public class Line { public int Quantity { get; set; } = 1; }
public class Created { public int Version { get; set; } = 1; }
public class Problem { public int Code { get; set; } = 400; }
public class Wrapped { public int Level { get; set; } = 2; }
public class Route { public int Id { get; set; } = 7; }
public class Untouched { public int Size { get; set; } = 3; }
public class OrdersController : ControllerBase
{
  public IActionResult Get() => Ok(new Order());
  public async Task<IActionResult> List() { await Task.Yield(); List<Line> lines = new List<Line>(); return Ok(lines); }
  public IActionResult Post() => CreatedAtAction(nameof(Get), new Route(), new Created());
  public IActionResult Fail() => BadRequest(new Problem());
  public IActionResult Wrap() => new OkObjectResult(new Wrapped());
  public IActionResult Nothing() { object value = new Untouched(); return Ok(value); }
  public IActionResult Empty() => Ok();
}
";
      AssertDiagnostics(Analyze(source, formatters: true), "LSMP001 Copies", "LSMP001 Quantity", "LSMP001 Version", "LSMP001 Code", "LSMP001 Level");
    }

    /// <summary>
    /// The properties of anonymous types are serialized (written only): the classes they hold are followed, the anonymous types themselves have nothing to fix.
    /// </summary>
    [TestMethod]
    public void Controllers_AnonymousResults()
    {
      string source = Usings + @"
using System.Linq;
using Microsoft.AspNetCore.Mvc;
public class Order { public int Copies { get; set; } = 1; }
public class Line { public int Quantity { get; set; } = 1; }
public class OrdersController : ControllerBase
{
  public IActionResult Get() => Ok(new { Id = 1, Order = new Order(), Lines = new List<Line>(), Nested = new { Total = 2 } });
  public IActionResult List() => Ok(new List<int>().Select(i => new { Id = i }));
}
";
      AssertDiagnostics(Analyze(source, formatters: true), "LSMP001 Copies", "LSMP001 Quantity");
    }

    /// <summary>
    /// Minimal APIs: MsgPackBody&lt;T&gt; parameters are read, the values of MsgPackResults.MsgPack(value) / Results.Extensions.MsgPack(value) and MsgPackResult&lt;T&gt; are written (as Ok(value)).
    /// </summary>
    [TestMethod]
    public void MinimalApis_BodiesAndResults()
    {
      string source = Usings + @"
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using LsMsgPackFormatters;
public class Order { public int Copies { get; set; } = 1; }
public class Line { public int Quantity { get; set; } = 1; }
public class Problem { public int Code { get; set; } = 400; }
public class Created { public int Version { get; set; } = 1; }
public class Typed { public int Level { get; set; } = 2; }
public class Untouched { public int Size { get; set; } = 3; }
public static class Endpoints
{
  public static void Map(WebApplication app)
  {
    app.MapPost(""/orders"", (MsgPackBody<Order> order) => MsgPackResults.MsgPack(new List<Line>()));
    app.MapPost(""/any"", (MsgPackBody<object> body) => Results.Extensions.MsgPack(new Problem(), 400));
    app.MapGet(""/created"", () => MsgPackResults.MsgPack<object>(new Created()));
    app.MapGet(""/untouched"", () => { object value = new Untouched(); return MsgPackResults.MsgPack(value); });
    app.MapGet(""/typed"", GetTyped);
  }
  private static Results<MsgPackResult<Typed>, NotFound> GetTyped() => TypedResults.NotFound();
}
";
      ImmutableArray<Diagnostic> diagnostics = Analyze(source, formatters: true);
      AssertDiagnostics(diagnostics, "LSMP001 Copies", "LSMP001 Quantity", "LSMP001 Code", "LSMP001 Version", "LSMP001 Level", "LSMP005 object", "LSMP007 object");
      StringAssert.StartsWith(diagnostics[5].GetMessage(), "MsgPackBody reads object");
    }

    /// <summary>
    /// Without the formatters MsgPackBody and MsgPackResult are other classes.
    /// </summary>
    [TestMethod]
    public void MinimalApis_OnlyWithTheFormatters()
    {
      string source = Usings + @"
public class MsgPackBody<T> { public T Value { get; set; } }
public class Order { public int Copies { get; set; } = 1; }
public static class Endpoints
{
  public static int Handle(MsgPackBody<object> body, MsgPackBody<Order> order) => 0;
  public static byte[] Write() => MsgPackSerializer.Serialize(1);
}
";
      AssertDiagnostics(Analyze(source));
    }

    #endregion

    #region LSMP008: classes of other projects

    private const string OtherProject = @"
namespace Dtos
{
  public class OrderDto { public int Retries { get; set; } = 3; }
  public class EntityBase { public string Id { get; set; } }
  public class ReadOnlyDto { public int Total { get; } }
}
";

    private const string UsesOtherProject = Usings + @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Dtos;
public class Customer : EntityBase { public List<OrderDto> Orders { get; set; } public ReadOnlyDto Totals { get; set; } }
public class CustomersController : ControllerBase
{
  public ActionResult<OrderDto> Get() => new OrderDto();
  public IActionResult Find() => Ok(new Customer());
  public Task<IActionResult> Post(OrderDto order) => Task.FromResult<IActionResult>(Ok(order));
}
";

    [TestMethod]
    public void UncheckedModels_OfAnotherProject()
    {
      ImmutableArray<Diagnostic> diagnostics = Analyze(UsesOtherProject, formatters: true, other: Compile("Dtos", OtherProject, "/src/Dtos/obj/Debug/net8.0/ref/Dtos.dll"));
      AssertDiagnostics(diagnostics, "LSMP008 Customer", "LSMP008 Orders", "LSMP008 Get", "LSMP008 order", "LSMP008 order");
      Assert.AreEqual("'EntityBase' is declared in Dtos, which the analyzers do not check: classes are checked in the project that declares them. "
        + "Reference the LsMsgPack.Core package in that project (or a package that depends on it) and set <LsMsgPackAnalyze>AllTypes</LsMsgPackAnalyze> there.", diagnostics[0].GetMessage());
    }

    [TestMethod]
    public void UncheckedModels_NotWhenTheProjectChecksThem()
    {
      string marked = "[assembly: System.Reflection.AssemblyMetadata(\"LsMsgPackAnalyze\", \"AllTypes\")]\n" + OtherProject;
      AssertDiagnostics(Analyze(UsesOtherProject, formatters: true, other: Compile("Dtos", marked)));
    }

    [TestMethod]
    [DataRow("/home/me/.nuget/packages/dtos/1.0.0/lib/net8.0/Dtos.dll", null)]
    [DataRow("C:\\Users\\me\\.nuget\\packages\\dtos\\1.0.0\\lib\\net8.0\\Dtos.dll", null)]
    [DataRow("/cache/pkgs/dtos/1.0.0/lib/net8.0/Dtos.dll", "/cache/pkgs")]
    public void UncheckedModels_NotOfPackages(string path, string packageRoot)
    {
      Dictionary<string, string> msbuild = new Dictionary<string, string>();
      if (packageRoot != null)
        msbuild.Add("NuGetPackageRoot", packageRoot + "/");
      AssertDiagnostics(Analyze(UsesOtherProject, formatters: true, msbuild: msbuild, other: Compile("Dtos", OtherProject, path)));
    }

    #endregion
  }
}
