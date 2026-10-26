using LsMsgPack.Analyzers;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackTypeScript
{
  /// <summary>
  /// The roots of a solution or project, found as LsMsgPack's analyzers find them (LsMsgPack.Analyzers ModelTypes.FindUses): the results and parameters of controller actions
  /// (with a reference to a formatter), the types handed to the serializers (also through generic wrappers), <c>[LtMsgPackSerializable]</c>.
  /// The types are then looked up in the loaded assemblies (the compiled code is what the server runs).
  /// </summary>
  internal static class SolutionRoots
  {
    internal sealed class Found
    {
      /// <summary>
      /// Written by the server (responses: the client reads them, the schema set has their schemas).
      /// </summary>
      internal readonly List<Type> Written = new List<Type>();

      /// <summary>
      /// Read by the server (request bodies: the client writes them, models only).
      /// </summary>
      internal readonly List<Type> Read = new List<Type>();
    }

    internal static Found Find(string path, List<string> warnings)
    {
      RegisterMsBuild();
      return FindAsync(path, warnings).GetAwaiter().GetResult();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterMsBuild()
    {
      if (!MSBuildLocator.IsRegistered)
        MSBuildLocator.RegisterDefaults(); // before any MSBuild type is loaded (FindAsync is another method for that reason)
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Found> FindAsync(string path, List<string> warnings)
    {
      Found found = new Found();
      string full = Path.GetFullPath(path);
      using (MSBuildWorkspace workspace = MSBuildWorkspace.Create())
      {
        workspace.WorkspaceFailed += (s, e) =>
        {
          if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            warnings.Add(string.Concat("Loading the solution: ", e.Diagnostic.Message));
        };
        IEnumerable<Project> projects;
        if (full.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
          projects = (await workspace.OpenSolutionAsync(full).ConfigureAwait(false)).Projects;
        else
          projects = new[] { await workspace.OpenProjectAsync(full).ConfigureAwait(false) };

        HashSet<Type> written = new HashSet<Type>(), read = new HashSet<Type>();
        foreach (Project project in projects.Where(p => p.Language == LanguageNames.CSharp))
        {
          Compilation compilation = await project.GetCompilationAsync().ConfigureAwait(false);
          if (compilation is null)
            continue;
          KnownSymbols known = new KnownSymbols(compilation);
          if (known.IsEmpty)
            continue; // does not reference LsMsgPack
          foreach (SerializedUse use in ModelTypes.FindUses(compilation, known, CancellationToken.None))
          {
            ITypeSymbol symbol = use.Reads ? use.Type : Unwrap(use.Type);
            if (symbol is null || HasAnonymousType(symbol, 0))
              continue; // Ok(new { ... }): no class to generate (and written without properties)
            Type type = ToType(symbol);
            if (type is null)
            {
              warnings.Add(string.Concat("Not found in the compiled assemblies: ", symbol.ToDisplayString(), " (", use.What, "). Build the project, and pass its assembly with --assembly."));
              continue;
            }
            if (IsUseless(type))
              continue;
            if (use.Reads ? read.Add(type) : written.Add(type))
              (use.Reads ? found.Read : found.Written).Add(type);
          }
        }
      }
      return found;
    }

    /// <summary>
    /// What a result holds: <c>Task&lt;ActionResult&lt;T&gt;&gt;</c> is T; results without a declared value (<c>IActionResult</c>) have none.
    /// </summary>
    private static ITypeSymbol Unwrap(ITypeSymbol type)
    {
      for (int t = 0; t < 8 && type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1; t++)
      {
        string name = named.ConstructedFrom.ToDisplayString();
        if (name == "System.Threading.Tasks.Task<TResult>" || name == "System.Threading.Tasks.ValueTask<TResult>" || name == "Microsoft.AspNetCore.Mvc.ActionResult<TValue>")
          type = named.TypeArguments[0];
        else
          break;
      }
      string display = type.ToDisplayString();
      if (display.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) || display.StartsWith("System.Web.", StringComparison.Ordinal) || display == "System.Threading.Tasks.Task"
        || display == "System.Threading.Tasks.ValueTask" || display == "System.Net.Http.HttpResponseMessage")
        return null;
      return type;
    }

    private static bool HasAnonymousType(ITypeSymbol symbol, int depth)
    {
      if (symbol is IArrayTypeSymbol array)
        return HasAnonymousType(array.ElementType, depth + 1);
      if (!(symbol is INamedTypeSymbol named) || depth > 16)
        return false;
      if (named.IsAnonymousType)
        return true;
      foreach (ITypeSymbol argument in named.TypeArguments)
        if (HasAnonymousType(argument, depth + 1))
          return true;
      return false;
    }

    private static bool IsUseless(Type type)
    {
      return type.IsPrimitive || type == typeof(string) || type == typeof(object) || type == typeof(CancellationToken) || type.ContainsGenericParameters;
    }

    /// <summary>
    /// The runtime type of a symbol, from the assembly of its name (loaded through the resolvers of the loaded assemblies), null for type parameters and types that are not there.
    /// </summary>
    internal static Type ToType(ITypeSymbol symbol)
    {
      if (symbol is IArrayTypeSymbol array)
      {
        Type element = ToType(array.ElementType);
        return element is null ? null : array.Rank == 1 ? element.MakeArrayType() : element.MakeArrayType(array.Rank);
      }
      if (!(symbol is INamedTypeSymbol named) || named.TypeKind == TypeKind.Error)
        return null;
      if (named.IsTupleType && named.TupleUnderlyingType != null)
        named = named.TupleUnderlyingType;

      INamedTypeSymbol definition = named.OriginalDefinition;
      Type type = DefinitionOf(definition);
      if (type is null || !named.IsGenericType)
        return type;
      List<Type> args = new List<Type>();
      for (INamedTypeSymbol level = named; level != null; level = level.ContainingType) // nested generic classes take the arguments of the classes they are in first
        args.InsertRange(0, level.TypeArguments.Select(ToType));
      if (args.Any(a => a is null))
        return null;
      try
      {
        return type.MakeGenericType(args.ToArray());
      }
      catch (ArgumentException)
      {
        return null;
      }
    }

    private static Type DefinitionOf(INamedTypeSymbol definition)
    {
      string name = definition.MetadataName;
      for (INamedTypeSymbol outer = definition.ContainingType; outer != null; outer = outer.ContainingType)
        name = string.Concat(outer.MetadataName, "+", name);
      string ns = definition.ContainingNamespace?.IsGlobalNamespace == false ? definition.ContainingNamespace.ToDisplayString() : null;
      string fullName = ns is null ? name : string.Concat(ns, ".", name);

      Type framework = Type.GetType(fullName, false);
      if (framework != null)
        return framework;
      string assemblyName = definition.ContainingAssembly?.Identity.Name;
      if (assemblyName is null)
        return null;
      Assembly assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal))
        ?? ModelAssemblies.TryLoad(new AssemblyName(assemblyName));
      return assembly?.GetType(fullName, false);
    }
  }
}
