using LsMsgPack.Analyzers;
using LsMsgPack.TypeResolving.Filters;
using LsMsgPack.TypeResolving.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using static LsMsgPack.AnalyzerTests.AnalyzerHarness;

namespace LsMsgPack.AnalyzerTests
{
  /// <summary>
  /// Fields: LSMP010 for public fields the default mode (SerializedFields.Attributed) leaves out, the mode from LsMsgPackFields / [assembly: LtMsgPackGenerate(Fields = ...)],
  /// the other rules on the fields the serializers write, and the code fixes of LSMP010.
  /// </summary>
  [TestClass]
  public class FieldTests
  {
    private const string Usings = "using System;\nusing System.Collections;\nusing System.Collections.Generic;\nusing System.ComponentModel;\nusing System.Runtime.Serialization;\nusing LsMsgPack;\nusing LtMsgPack;\n";
    private const string Write = "public static class Use { public static byte[] Write(Model m) => MsgPackSerializer.Serialize(m); }\n";

    private static void AssertDiagnostics(ImmutableArray<Diagnostic> diagnostics, params string[] expected)
    {
      string[] actual = Summary(diagnostics);
      CollectionAssert.AreEqual(expected, actual, "Got:\n" + string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    private static ImmutableArray<Diagnostic> AnalyzeWith(string source, string fields)
    {
      return Analyze(source, msbuild: fields is null ? null : new Dictionary<string, string>() { { "LsMsgPackFields", fields } });
    }

    #region LSMP010

    private const string LeftOut = Usings + @"
public class Model
{
  public int Count;
  public string Name, Code;
  [DataMember] public int Opted;
  [System.Text.Json.Serialization.JsonInclude] public int Included;
  [System.Text.Json.Serialization.JsonIgnore] public int Ignored;
  [IgnoreDataMember] public int IgnoredMember;
  [NonSerialized] public int NotSerialized;
  private int _private;
  internal int Internal;
  public static int Shared;
  public const int Constant = 1;
  public readonly int Computed = 3;
  public readonly List<int> Items = new List<int>();
  public Action Callback;
  public Type Kind;
  public System.Text.Json.JsonElement Payload;
  public event EventHandler Changed;
  public Inner Child;
  public int Property { get; set; }
  public int Use() => _private + Internal;
}
public class Inner { public int Level { get; set; } = 2; }
public struct Vec { public float X, Y; }
public class Options { public int Port; }
public static class Use { public static void Write(Model m, Vec v) { MsgPackSerializer.Serialize(m); MsgPackSerializer.Serialize(v); } }
";

    [TestMethod]
    public void LeftOut_PublicFieldsThatWouldRoundTrip()
    {
      // Not: opted in (serialized), ignored on purpose, not public, static/const, readonly without a constructor taking it (never read), types that are never written or JSON documents, events.
      // Not reached: Inner (only through a field that is not serialized), Options (not serialized).
      ImmutableArray<Diagnostic> diagnostics = Analyze(LeftOut);
      AssertDiagnostics(diagnostics, "LSMP010 Count", "LSMP010 Name", "LSMP010 Code", "LSMP010 Items", "LSMP010 Child", "LSMP010 X", "LSMP010 Y");
      Assert.AreEqual("'Model.Count' is a public field, which the serializers leave out: by default they only write fields with an attribute that opts them in, such as [DataMember] or [JsonInclude]. "
        + "Add [DataMember], make it a property, or ignore it ([IgnoreDataMember]). If your serializers write public fields (FilterNonSettable with SerializedFields.Public), set <LsMsgPackFields>Public</LsMsgPackFields>.",
        diagnostics[0].GetMessage());
    }

    [TestMethod]
    public void LeftOut_ReadonlyTakenByTheConstructor()
    {
      // The constructor takes the public readonly field (Core's ObjectConstructor binds public fields too): without the field in the data it gets 0
      AssertDiagnostics(Analyze(Usings + @"
public class Model
{
  public readonly int Total;
  public readonly int Other;
  public Model(int total) { Total = total; }
}
" + Write), "LSMP010 Total");
    }

    [TestMethod]
    [DataRow("[DataContract]")]
    [DataRow("[MessagePack.MessagePackObject]")]
    public void LeftOut_NotWhereTheTypeMarksItsMembers(string attribute)
    {
      string source = Usings + "namespace MessagePack { public class MessagePackObjectAttribute : Attribute { public MessagePackObjectAttribute(bool keyAsPropertyName = false) { } } }\n"
        + attribute + "\npublic class Model\n{\n  [DataMember] public int Kept;\n  public int Left;\n}\n" + Write;
      AssertDiagnostics(Analyze(source));
      // keyAsPropertyName: true writes every public member (MessagePack-CSharp)
      AssertDiagnostics(Analyze(source.Replace("[MessagePack.MessagePackObject]", "[MessagePack.MessagePackObject(true)]").Replace("[DataContract]", "[MessagePack.MessagePackObject(true)]")), "LSMP010 Left");
    }

    [TestMethod]
    [DataRow("Public")]
    [DataRow("all")]
    [DataRow("None")]
    public void LeftOut_NotWithAnotherMode(string mode)
    {
      AssertDiagnostics(AnalyzeWith(Usings + "public class Model { public int Count; }\n" + Write, mode));
    }

    [TestMethod]
    public void LeftOut_UnknownModeIsAttributed()
    {
      AssertDiagnostics(AnalyzeWith(Usings + "public class Model { public int Count; }\n" + Write, "Some"), "LSMP010 Count");
    }

    [TestMethod]
    public void Mode_AssemblyAttributeWins()
    {
      string source = Usings + "[assembly: LtMsgPackGenerate(Fields = LsMsgPack.TypeResolving.Filters.SerializedFields.Attributed)]\npublic class Model { public int Count; }\n" + Write;
      AssertDiagnostics(AnalyzeWith(source, "Public"), "LSMP010 Count");
      AssertDiagnostics(AnalyzeWith(source.Replace(".Attributed", ".All"), null));
    }

    [TestMethod]
    public void Parse_AsTheGenerator()
    {
      Assert.AreEqual(Fields.None, Fields.Parse(" none ", Fields.All));
      Assert.AreEqual(Fields.None, Fields.Parse("false", Fields.All));
      Assert.AreEqual(Fields.Attributed, Fields.Parse("Attributed", Fields.None));
      Assert.AreEqual(Fields.Public, Fields.Parse("PUBLIC", Fields.None));
      Assert.AreEqual(Fields.All, Fields.Parse("All", Fields.None));
      Assert.AreEqual(Fields.Public, Fields.Parse("?", Fields.Public));
      // The values are SerializedFields' (by name, so a new value of the enum fails here too)
      string[] names = Enum.GetNames(typeof(SerializedFields));
      for (int t = 0; t < names.Length; t++)
      {
        FieldInfo constant = typeof(Fields).GetField(names[t], BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(constant, "Fields." + names[t]);
        Assert.AreEqual((int)Enum.Parse(typeof(SerializedFields), names[t]), (int)constant.GetRawConstantValue(), names[t]);
      }
    }

    #endregion

    #region The other rules on serialized fields

    private const string Serialized = Usings + @"
public class Model
{
  [DataMember] public int Retries = 3;
  [DataMember] private int _level = 2;
  [DataMember] public int A = 1, B = 2;
  [DataMember, DefaultValue(5)] public int Limit;
  [DataMember, DefaultValue(1)] public long Big = 1;
  [DataMember, DefaultValue(""x"")] public string Mode = ""x"";
  [DataMember] public object Value;
  [DataMember] public System.Text.Json.JsonElement Payload;
  [DataMember] public readonly System.Text.Json.JsonElement Fixed;
  [DataMember] public readonly object Open;
  [DataMember] public Inner Child;
  public int Plain = 3;
  public int Use() => _level;
}
public class Inner { public int Depth { get; set; } = 4; }
" + Write;

    [TestMethod]
    public void Serialized_CheckedAsProperties()
    {
      // Plain: left out (LSMP010), so not checked for its initial value; readonly fields only for LSMP009 (never set from the data); Inner reached through the serialized field
      AssertDiagnostics(Analyze(Serialized), "LSMP001 Retries", "LSMP001 _level", "LSMP001 A", "LSMP001 B", "LSMP002 DefaultValue(5)", "LSMP003 DefaultValue(1)", "LSMP006 Mode",
        "LSMP004 Value", "LSMP009 Payload", "LSMP009 Fixed", "LSMP010 Plain", "LSMP001 Depth");
    }

    [TestMethod]
    public void Serialized_PublicMode()
    {
      // With public fields written: Plain is checked, private ones only when attributed
      string source = Usings + "public class Model\n{\n  public int Plain = 3;\n  private int _hidden = 4;\n  [DataMember] private int _opted = 5;\n  public int Use() => _hidden + _opted;\n}\n" + Write;
      AssertDiagnostics(AnalyzeWith(source, "Public"), "LSMP001 Plain", "LSMP001 _opted");
      AssertDiagnostics(AnalyzeWith(source, "All"), "LSMP001 Plain", "LSMP001 _hidden", "LSMP001 _opted");
      AssertDiagnostics(AnalyzeWith(source, "None"));
    }

    [TestMethod]
    public void Serialized_SharedDeclarationHasNoAttributeFix()
    {
      // int A = 1, B = 2: [DefaultValue] would be on both, so only the initializer fix is offered
      ImmutableArray<Diagnostic> diagnostics = Analyze(Serialized);
      Diagnostic a = diagnostics.Single(d => d.Id == "LSMP001" && d.GetMessage().StartsWith("'Model.A'", StringComparison.Ordinal));
      Assert.IsFalse(a.Properties.ContainsKey(Rules.ArgumentKey));
      Diagnostic retries = diagnostics.Single(d => d.Id == "LSMP001" && d.GetMessage().StartsWith("'Model.Retries'", StringComparison.Ordinal));
      Assert.AreEqual("3", retries.Properties[Rules.ArgumentKey]);
    }

    [TestMethod]
    public void Serialized_ConstructorTakingPublicFields()
    {
      // The constructor takes the readonly public field, so a reader calls it: Total gets the left out value (not checked), Level keeps its initializer (lost in a map)
      AssertDiagnostics(AnalyzeWith(Usings + @"
public class Model
{
  public readonly int Total;
  public int Level = 3;
  public int Size { get; set; } = 2;
  public Model(int total) { Total = total; }
}
" + Write, "Public"), "LSMP001 Level", "LSMP001 Size");
    }

    [TestMethod]
    [DataRow("Retries", "Add [DefaultValue(3)]", "[DataMember] [DefaultValue(3)] public int Retries = 3;")]
    [DataRow("Limit", "Initialize Limit to 5", "[DataMember, DefaultValue(5)] public int Limit = 5;")]
    [DataRow("Big", "Use [DefaultValue(1L)]", "[DataMember, DefaultValue(1L)] public long Big = 1;")]
    public void Serialized_DefaultValueFixes(string field, string title, string expected)
    {
      string line = Serialized.Split('\n').First(l => l.Contains(" " + field + " =") || l.Contains(" " + field + ";"));
      string single = Usings + "public class Model\n{\n" + line + "\n}\n" + Write;
      string fixedSource = Fix(single, Analyze(single)[0].Id, title);
      StringAssert.Contains(fixedSource, "  " + expected, fixedSource);
      Assert.IsEmpty(Analyze(fixedSource), fixedSource);
    }

    [TestMethod]
    public void UncheckedModels_WithPublicFields()
    {
      MetadataReference other = Compile("Dtos", "namespace Dtos { public class PointDto { public int X; public int Y; } public class ConstDto { public const int Max = 3; public readonly int Id; } }\n",
        "/src/Dtos/obj/Debug/net8.0/ref/Dtos.dll");
      AssertDiagnostics(Analyze(Usings + "public class Model { public Dtos.PointDto Point { get; set; } public Dtos.ConstDto Limits { get; set; } }\n" + Write, other: other), "LSMP008 Point");
    }

    #endregion

    #region Code fixes of LSMP010

    [TestMethod]
    public void Fix_AddDataMember()
    {
      string source = Usings.Replace("using System.Runtime.Serialization;\n", "") + "public class Model\n{\n  /// <summary>Doc</summary>\n  public int Count;\n}\n" + Write;
      string fixedSource = Fix(source, "LSMP010", "Add [DataMember]");
      StringAssert.Contains(fixedSource, "using System.Runtime.Serialization;");
      StringAssert.Contains(fixedSource, "  /// <summary>Doc</summary>\n  [DataMember]\n  public int Count;", fixedSource);
      AssertFixed(fixedSource, "LSMP010");
    }

    [TestMethod]
    [DataRow("Count", "  /// <summary>Doc</summary>\n  public int Count = 3;", "  /// <summary>Doc</summary>\n  public int Count { get; set; } = 3;")]
    [DataRow("Name", "  public string Name; // the name", "  public string Name { get; set; } // the name")]
    [DataRow("Hidden", "  [Obsolete] public new int Hidden;", "  [Obsolete] public new int Hidden { get; set; }")]
    [DataRow("Marked", "  [OnlyField] public int Marked;", "  [field: OnlyField] public int Marked { get; set; }")]
    [DataRow("Items", "  public readonly List<int> Items = new List<int>();", "  public List<int> Items { get; } = new List<int>();")]
    [DataRow("Items", "  readonly public List<int> Items = new List<int>();", "  public List<int> Items { get; } = new List<int>();")]
    public void Fix_MakeProperty(string name, string field, string expected)
    {
      string source = Usings + "[AttributeUsage(AttributeTargets.Field)] public class OnlyFieldAttribute : Attribute { }\npublic class BaseModel { public int Hidden { get; set; } }\n"
        + "public class Model : BaseModel\n{\n" + field + "\n}\n" + Write;
      string fixedSource = Fix(source, "LSMP010", "Make '" + name + "' a property");
      StringAssert.Contains(fixedSource, "\n" + expected + "\n", fixedSource);
      AssertFixed(fixedSource, "LSMP010");
    }

    [TestMethod]
    public void Fix_MakeProperty_ReadonlyTakenByTheConstructor()
    {
      string source = Usings + "public class Model\n{\n  public readonly int Total;\n  public Model(int total) { Total = total; }\n}\n" + Write;
      string fixedSource = Fix(source, "LSMP010", "Make 'Total' a property");
      StringAssert.Contains(fixedSource, "\n  public int Total { get; }\n", fixedSource);
      Assert.IsEmpty(Analyze(fixedSource), fixedSource);
    }

    [TestMethod]
    public void Fix_MakeProperty_OneOfSeveral()
    {
      string source = Usings + "public class Model\n{\n  /// <summary>Doc</summary>\n  public int A, B = 2;\n}\n" + Write;
      string fixedSource = Fix(source, "LSMP010", "Make 'A' a property");
      StringAssert.Contains(fixedSource, "  /// <summary>Doc</summary>\n  public int B = 2;\n  public int A { get; set; }\n}", fixedSource);
    }

    [TestMethod]
    public void Fix_NotForVolatile()
    {
      string source = Usings + "public class Model\n{\n  public volatile int Count;\n}\n" + Write;
      Fix(source, "LSMP010", "Add [DataMember]");
      Assert.ThrowsExactly<AssertFailedException>(() => Fix(source, "LSMP010", "Make 'Count' a property"));
    }

    #endregion

    #region The same fields as the runtime

    private const string MirrorSource = @"
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
namespace Mirror
{
  public class CustomIncludeAttribute : Attribute { }
  public class Base
  {
    public int BasePublic;
    [DataMember] private int _baseOpted;
    private int _basePrivate;
    public int Use() => _baseOpted + _basePrivate;
  }
  public class Sample : Base
  {
    public int Plain;
    public string Text;
    [DataMember] public int Member;
    [JsonInclude] public int Included;
    [CustomInclude] public int Custom;
    [DataMember] private int _privateOpted;
    private int _private;
    internal int Internal;
    protected int Protected;
    [JsonIgnore] public int Ignored;
    [DataMember, IgnoreDataMember] public int IgnoredMember;
    [NonSerialized] public int NotSerialized;
    [XmlIgnore] public int XmlIgnored;
    public readonly int ReadOnly;
    [DataMember] public readonly List<int> Items = new List<int>();
    public static int Static;
    public const int Const = 1;
    public event EventHandler Changed;
    public int Auto { get; set; }
    public int Use2() => _privateOpted + _private + (Changed == null ? 0 : 1);
  }
  public struct Vec { public float X; public float Y; private float _z; [DataMember] private float _w; public float Sum() => _z + _w; }
}
";

    /// <summary>
    /// What the analyzers take as serialized (<see cref="Fields.IsSerialized"/>, not ignored) is what the serializers write, per mode: the field names among the keys of a map without the schema.
    /// </summary>
    [TestMethod]
    [DataRow(SerializedFields.None)]
    [DataRow(SerializedFields.Attributed)]
    [DataRow(SerializedFields.Public)]
    [DataRow(SerializedFields.All)]
    public void SameFieldsAsTheRuntime(SerializedFields mode)
    {
      CSharpCompilation compilation = CSharpCompilation.Create("MirrorModels", new[] { CSharpSyntaxTree.ParseText(MirrorSource) },
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
          .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) == "netstandard.dll" || Path.GetFileName(p) == "mscorlib.dll")
          .Select(p => MetadataReference.CreateFromFile(p)),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
      Assembly assembly;
      using (MemoryStream stream = new MemoryStream())
      {
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        assembly = Assembly.Load(stream.ToArray());
      }

      MsgPackSettings settings = new MsgPackSettings()
      {
        StaticFilters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, mode), new FilterIgnoredAttribute(), new FilterStatic() },
        DynamicFilters = new IMsgPackPropertyIncludeDynamically[0],
        UseInexedSchema = false,
        ObjectLayout = ObjectLayout.Map,
      };
      foreach (string name in new[] { "Mirror.Sample", "Mirror.Vec" })
      {
        Type type = assembly.GetType(name);
        byte[] bytes = MsgPackSerializer.Serialize(Activator.CreateInstance(type), type, settings);
        Dictionary<string, object> written = MsgPackSerializer.Deserialize<Dictionary<string, object>>(bytes, settings);

        List<string> predicted = new List<string>();
        HashSet<string> fieldNames = new HashSet<string>(StringComparer.Ordinal);
        for (INamedTypeSymbol t = compilation.GetTypeByMetadataName(name); t != null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType)
        {
          foreach (IFieldSymbol field in t.GetMembers().OfType<IFieldSymbol>())
          {
            fieldNames.Add(field.Name);
            if (Fields.IsSerialized(field, (int)mode) && !ModelAnalyzer.IsIgnored(field))
              predicted.Add(field.Name);
          }
        }
        string[] runtime = written.Keys.Where(fieldNames.Contains).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(predicted.OrderBy(n => n, StringComparer.Ordinal).ToArray(), runtime, name + " " + mode + ": runtime " + string.Join(", ", runtime));
      }
    }

    private const string VisualBasicSource = @"
Imports System
Imports System.Runtime.Serialization
Namespace VbMirror
  Public Class Notifier
    Public Event Ping As EventHandler
  End Class
  Public Class Model
    Public Property Auto As Integer
    Public Property WithInit As String = ""x""
    Public ReadOnly Property ReadOnlyAuto As Integer = 3
    Public Field As Integer
    Private _private As Integer
    <DataMember> Private _secret As Integer
    Public Event Changed As EventHandler
    Public Event Typed(sender As Object, value As Integer)
    Public WithEvents Source As Notifier
    Private WithEvents _privateSource As Notifier
    Public Function Counter() As Integer
      Static count As Integer
      count += 1
      Return count + _private + _secret
    End Function
    Public Function CounterInit() As Integer
      Static started As Integer = 5
      started += 1
      Return started
    End Function
    Public Shared Function Anonymous() As Object
      Return New With {.Name = ""a"", Key .Id = 1}
    End Function
    Private Sub OnPing(sender As Object, e As EventArgs) Handles Source.Ping
    End Sub
  End Class
End Namespace
";

    /// <summary>
    /// VB marks the fields of auto-properties (<c>_Auto</c>), events (<c>ChangedEvent</c>) and WithEvents variables [CompilerGenerated], not those of <c>Static</c> locals
    /// (<c>$STATIC$Counter$2008$count</c>, with an initializer also <c>...$started$Init</c> of the type StaticLocalInitFlag, which the serializers refuse) nor of anonymous types (<c>$Name</c>):
    /// names with $ are compiler names (FullPropertyInfo.IsCompilerName). Both serializers write only the declared fields, and the analyzers' rules (the VB types as metadata of a C# compilation,
    /// as in a C# project referencing a VB library) predict the same.
    /// </summary>
    [TestMethod]
    [DataRow(SerializedFields.Attributed, new[] { "_secret" })]
    [DataRow(SerializedFields.Public, new[] { "Field", "_secret" })]
    [DataRow(SerializedFields.All, new[] { "Field", "_private", "_secret" })]
    public void VisualBasic_SameFieldsAsTheRuntime(SerializedFields mode, string[] expected)
    {
      MetadataReference[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
        .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p).StartsWith("Microsoft.VisualBasic", StringComparison.Ordinal)
          || Path.GetFileName(p) == "netstandard.dll" || Path.GetFileName(p) == "mscorlib.dll")
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
      Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation vb = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create("VbMirrorModels",
        new[] { Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(VisualBasicSource) }, platform,
        new Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
      byte[] image;
      using (MemoryStream stream = new MemoryStream())
      {
        Microsoft.CodeAnalysis.Emit.EmitResult result = vb.Emit(stream);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        image = stream.ToArray();
      }
      Assembly assembly = Assembly.Load(image);
      Type type = assembly.GetType("VbMirror.Model");
      object model = Activator.CreateInstance(type);
      type.GetMethod("Counter").Invoke(model, null);
      type.GetMethod("CounterInit").Invoke(model, null);
      string[] allFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(f => f.Name).ToArray();
      foreach (string generated in new[] { "_Auto", "ChangedEvent", "_Source", "$STATIC$Counter$2008$count", "$STATIC$CounterInit$2008$started$Init" })
        Assert.Contains(generated, allFields, "the fields VB generates (the test's premise)");

      IMsgPackPropertyIncludeStatically[] filters = new IMsgPackPropertyIncludeStatically[] { new FilterNonSettable(GetOnlyProperties.All, NonPublicProperties.None, mode), new FilterIgnoredAttribute(), new FilterStatic() };
      MsgPackSettings settings = new MsgPackSettings() { StaticFilters = filters, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0], UseInexedSchema = false, ObjectLayout = ObjectLayout.Map };
      LtMsgPack.LtMsgPackSerializer lt = new LtMsgPack.LtMsgPackSerializer(new LtMsgPack.LtMsgPackOptions() { StaticFilters = filters, DynamicFilters = new IMsgPackPropertyIncludeDynamically[0], UseInexedSchema = false, ObjectLayout = ObjectLayout.Map });
      string[] properties = { "Auto", "WithInit", "ReadOnlyAuto", "Source" };
      string[] all = properties.Concat(expected).ToArray();
      CollectionAssert.AreEqual(all, MsgPackSerializer.Deserialize<Dictionary<string, object>>(MsgPackSerializer.Serialize(model, type, settings), settings).Keys.ToArray(), "LsMsgPack " + mode);
      CollectionAssert.AreEqual(all, MsgPackSerializer.Deserialize<Dictionary<string, object>>(lt.Serialize(model, type), settings).Keys.ToArray(), "LtMsgPack " + mode);
      object anonymous = type.GetMethod("Anonymous").Invoke(null, null);
      CollectionAssert.AreEqual(new[] { "Name", "Id" }, MsgPackSerializer.Deserialize<Dictionary<string, object>>(MsgPackSerializer.Serialize(anonymous, anonymous.GetType(), settings), settings).Keys.ToArray(), "LsMsgPack, anonymous type " + mode);
      CollectionAssert.AreEqual(new[] { "Name", "Id" }, MsgPackSerializer.Deserialize<Dictionary<string, object>>(lt.Serialize(anonymous, anonymous.GetType()), settings).Keys.ToArray(), "LtMsgPack, anonymous type " + mode);

      // a project's compilation imports the public and protected members of its references only (the private fields are left to the runtime): all of them here, to compare the rules
      CSharpCompilation user = CSharpCompilation.Create("VbUser", new SyntaxTree[0], platform.Concat(new[] { MetadataReference.CreateFromImage(image) }),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithMetadataImportOptions(MetadataImportOptions.All));
      IFieldSymbol[] symbols = user.GetTypeByMetadataName("VbMirror.Model").GetMembers().OfType<IFieldSymbol>().ToArray();
      Assert.Contains("$STATIC$Counter$2008$count", symbols.Select(f => f.Name).ToArray(), "imported");
      string[] predicted = symbols.Where(f => Fields.IsSerialized(f, (int)mode) && !ModelAnalyzer.IsIgnored(f)).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
      CollectionAssert.AreEqual(expected, predicted, "analyzers " + mode + ": " + string.Join(", ", predicted));
    }

    #endregion
  }
}
