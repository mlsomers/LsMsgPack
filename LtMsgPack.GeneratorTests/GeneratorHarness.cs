using LtMsgPack.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LtMsgPack.GeneratorTests
{
  /// <summary>
  /// Runs the generator on source text, as the compiler does in a project referencing LtMsgPack.
  /// </summary>
  internal static class GeneratorHarness
  {
    internal sealed class Result
    {
      public CSharpCompilation Output;

      /// <summary>
      /// The diagnostics of the generator.
      /// </summary>
      public ImmutableArray<Diagnostic> GeneratorDiagnostics;

      /// <summary>
      /// The generated files by hint name.
      /// </summary>
      public SortedDictionary<string, string> Generated = new SortedDictionary<string, string>(StringComparer.Ordinal);

      /// <summary>
      /// The errors and warnings in the generated code.
      /// </summary>
      public Diagnostic[] CompileProblems
      {
        get { return Output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree?.FilePath != "Model.cs").ToArray(); }
      }

      public string AllGenerated
      {
        get
        {
          StringBuilder all = new StringBuilder();
          foreach (KeyValuePair<string, string> file in Generated)
            all.Append("// ---- ").Append(file.Key).Append('\n').Append(file.Value).Append('\n');
          return all.ToString();
        }
      }

      public void AssertCompiles()
      {
        Diagnostic[] errors = Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.ToString())) + "\n" + AllGenerated);
      }
    }

    /// <param name="netStandard20">Compile against .NET Standard 2.0 (and LtMsgPack's .NET Standard 2.0 build) instead of .NET 8</param>
    /// <param name="msbuild">MSBuild properties the generator sees (build_property.Name)</param>
    public static Result Run(string source, LanguageVersion language = LanguageVersion.Latest, bool netStandard20 = false, IDictionary<string, string> msbuild = null, string assemblyName = "TestModels")
    {
      CSharpParseOptions parse = new CSharpParseOptions(language);
      CSharpCompilation compilation = CSharpCompilation.Create(assemblyName,
        new[] { CSharpSyntaxTree.ParseText(source, parse, "Model.cs") },
        netStandard20 ? NetStandard20References() : Net8References(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: language >= LanguageVersion.CSharp8 ? NullableContextOptions.Enable : NullableContextOptions.Disable));

      Diagnostic[] syntaxErrors = compilation.SyntaxTrees[0].GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(); // the contexts only compile with the generated code
      Assert.IsEmpty(syntaxErrors, "The test source does not parse: " + string.Join("\n", syntaxErrors.Select(e => e.ToString())));

      GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new LtMsgPackGenerator().AsSourceGenerator() }, parseOptions: parse, optionsProvider: new Options(msbuild));
      driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);

      Result result = new Result() { Output = (CSharpCompilation)output, GeneratorDiagnostics = driver.GetRunResult().Diagnostics };
      foreach (GeneratedSourceResult generated in driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources))
        result.Generated[generated.HintName] = generated.SourceText.ToString();
      return result;
    }

    /// <summary>
    /// The generated files compared with Snapshots/<paramref name="name"/>.txt (written when missing, or when the environment variable UPDATE_SNAPSHOTS is set).
    /// </summary>
    public static void AssertSnapshot(Result result, string name)
    {
      string directory = typeof(GeneratorHarness).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "SnapshotDirectory").Value;
      string path = Path.Combine(directory, name + ".txt");
      string actual = result.AllGenerated;
      if (!File.Exists(path) || Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") != null)
      {
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, actual);
        Assert.Inconclusive("Snapshot written: " + path);
      }
      Assert.AreEqual(File.ReadAllText(path).Replace("\r\n", "\n"), actual, "The generated code differs from " + path + " (set UPDATE_SNAPSHOTS=1 to accept it)");
    }

    public static Assembly Load(Result result)
    {
      using (MemoryStream stream = new MemoryStream())
      {
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = result.Output.Emit(stream);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return Assembly.Load(stream.ToArray());
      }
    }

    private static IEnumerable<MetadataReference> Net8References()
    {
      string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
      List<MetadataReference> references = new List<MetadataReference>();
      foreach (string path in platform)
      {
        string file = Path.GetFileName(path);
        if (file.StartsWith("System.", StringComparison.Ordinal) || file == "netstandard.dll" || file == "mscorlib.dll")
          references.Add(MetadataReference.CreateFromFile(path));
      }
      references.Add(MetadataReference.CreateFromFile(typeof(LtMsgPackContext).Assembly.Location));
      references.Add(MetadataReference.CreateFromFile(typeof(LsMsgPack.Generated.GeneratedContext).Assembly.Location));
      return references;
    }

    private static IEnumerable<MetadataReference> NetStandard20References()
    {
      string directory = typeof(GeneratorHarness).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "LtMsgPackNetStandard20").Value;
      List<MetadataReference> references = new List<MetadataReference>(Basic.Reference.Assemblies.NetStandard20.References.All);
      references.Add(MetadataReference.CreateFromFile(Path.Combine(directory, "LtMsgPack.dll")));
      references.Add(MetadataReference.CreateFromFile(Path.Combine(directory, "LsMsgPack.Core.dll")));
      return references;
    }

    private sealed class Options : AnalyzerConfigOptionsProvider
    {
      private readonly GlobalOptions _global;

      public Options(IDictionary<string, string> msbuild)
      {
        _global = new GlobalOptions(msbuild);
      }

      public override AnalyzerConfigOptions GlobalOptions => _global;

      public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

      public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class GlobalOptions : AnalyzerConfigOptions
    {
      private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

      public GlobalOptions(IDictionary<string, string> msbuild)
      {
        if (msbuild != null)
          foreach (KeyValuePair<string, string> property in msbuild)
            _values["build_property." + property.Key] = property.Value;
      }

      public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value);
    }
  }
}
