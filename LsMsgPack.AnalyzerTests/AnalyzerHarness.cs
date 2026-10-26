using LsMsgPack.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPack.AnalyzerTests
{
  /// <summary>
  /// Runs the analyzer on source text as the compiler does in a project referencing LsMsgPack and LtMsgPack (and, when asked, the ASP.NET Core formatters), and applies the code fixes as the IDE does.
  /// </summary>
  internal static class AnalyzerHarness
  {
    private const string FileName = "Model.cs";

    public static ImmutableArray<Diagnostic> Analyze(string source, bool formatters = false, IDictionary<string, string> msbuild = null, MetadataReference other = null)
    {
      MetadataReference[] references = References(formatters);
      if (other != null)
        references = references.Append(other).ToArray();
      CSharpCompilation compilation = CSharpCompilation.Create("TestModels", new[] { CSharpSyntaxTree.ParseText(source, path: FileName) }, references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
      Diagnostic[] errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
      Assert.IsEmpty(errors, "The test source does not compile: " + string.Join("\n", errors.Select(e => e.ToString())));
      return Run(compilation, msbuild);
    }

    public static ImmutableArray<Diagnostic> Run(Compilation compilation, IDictionary<string, string> msbuild)
    {
      AnalyzerOptions options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new Options(msbuild));
      CompilationWithAnalyzers analyzed = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ModelAnalyzer()), options);
      ImmutableArray<Diagnostic> diagnostics = analyzed.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
      return diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ToImmutableArray();
    }

    /// <summary>
    /// "LSMP001 Order.Retries" per diagnostic: the id and the word of the source the diagnostic is on (for a property: its name).
    /// </summary>
    public static string[] Summary(ImmutableArray<Diagnostic> diagnostics)
    {
      return diagnostics.Select(d => string.Concat(d.Id, " ", d.Location.SourceTree.GetText().ToString(d.Location.SourceSpan))).ToArray();
    }

    /// <summary>
    /// Applies the code fix with the title to the first diagnostic with the id, returns the fixed source.
    /// </summary>
    public static string Fix(string source, string id, string title, bool formatters = false)
    {
      using (AdhocWorkspace workspace = new AdhocWorkspace())
      {
        ProjectInfo info = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "TestModels", "TestModels", LanguageNames.CSharp,
          compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: References(formatters));
        workspace.TryApplyChanges(workspace.CurrentSolution.WithOptions(workspace.Options.WithChangedOption(Microsoft.CodeAnalysis.Formatting.FormattingOptions.NewLine, LanguageNames.CSharp, "\n")));
        Project project = workspace.AddProject(info);
        Document document = workspace.AddDocument(project.Id, FileName, SourceText.From(source));

        Compilation compilation = document.Project.GetCompilationAsync().GetAwaiter().GetResult();
        Diagnostic diagnostic = Run(compilation, null).FirstOrDefault(d => d.Id == id);
        Assert.IsNotNull(diagnostic, "No " + id + " in:\n" + source);

        List<CodeAction> actions = new List<CodeAction>();
        CodeFixContext context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        new DefaultValueCodeFix().RegisterCodeFixesAsync(context).GetAwaiter().GetResult();
        CodeAction chosen = actions.FirstOrDefault(a => a.Title == title);
        Assert.IsNotNull(chosen, "No fix \"" + title + "\", there are: " + string.Join(", ", actions.Select(a => a.Title)));

        ImmutableArray<CodeActionOperation> operations = chosen.GetOperationsAsync(CancellationToken.None).GetAwaiter().GetResult();
        Solution changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        return changed.GetDocument(document.Id).GetTextAsync().GetAwaiter().GetResult().ToString();
      }
    }

    /// <summary>
    /// The fixed source has no diagnostic with the id left, and compiles.
    /// </summary>
    public static void AssertFixed(string fixedSource, string id, bool formatters = false)
    {
      string[] left = Summary(Analyze(fixedSource, formatters)).Where(s => s.StartsWith(id, StringComparison.Ordinal)).ToArray();
      Assert.IsEmpty(left, string.Join("\n", left) + "\n" + fixedSource);
    }

    /// <summary>
    /// Another project's assembly: the source compiled into a reference (as a file at <paramref name="filePath"/>, e.g. in a NuGet package folder).
    /// </summary>
    public static MetadataReference Compile(string assemblyName, string source, string filePath = null)
    {
      CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, new[] { CSharpSyntaxTree.ParseText(source) }, References(false),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
      using (MemoryStream stream = new MemoryStream())
      {
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray(), filePath: filePath);
      }
    }

    private static MetadataReference[] References(bool formatters)
    {
      string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
      List<MetadataReference> references = new List<MetadataReference>();
      foreach (string path in platform)
      {
        string file = Path.GetFileName(path);
        bool aspNetCore = file.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) || file.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal);
        if (file.StartsWith("System.", StringComparison.Ordinal) || file == "netstandard.dll" || file == "mscorlib.dll" || (formatters && aspNetCore))
          references.Add(MetadataReference.CreateFromFile(path));
      }
      references.Add(MetadataReference.CreateFromFile(typeof(MsgPackSerializer).Assembly.Location));
      references.Add(MetadataReference.CreateFromFile(typeof(MsgPackOptions).Assembly.Location));
      references.Add(MetadataReference.CreateFromFile(typeof(LtMsgPack.LtMsgPackSerializer).Assembly.Location));
      if (formatters)
        references.Add(MetadataReference.CreateFromFile(typeof(LsMsgPackFormatters.LsMsgPackInputFormatter).Assembly.Location));
      return references.ToArray();
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
