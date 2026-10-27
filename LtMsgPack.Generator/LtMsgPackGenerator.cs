using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace LtMsgPack.Generator
{
  /// <summary>
  /// Writes the code of the contexts of LtMsgPack (see LtMsgPack.LtMsgPackContext): the context classes declared with [LtMsgPackSerializable],
  /// and the context of the assembly for [assembly: LtMsgPackGenerate(Include = ...)] or the MSBuild property LtMsgPackGenerate.
  /// </summary>
  [Generator(LanguageNames.CSharp)]
  public sealed class LtMsgPackGenerator : IIncrementalGenerator
  {
    internal const string SerializableAttribute = "LtMsgPack.LtMsgPackSerializableAttribute";
    internal const string GenerateAttribute = "LtMsgPack.LtMsgPackGenerateAttribute";
    internal const string ContextBase = "LtMsgPack.LtMsgPackContext";
    internal const string DefaultContextName = "GeneratedMsgPackContext";

    // LtMsgPack.LtMsgPackInclude
    internal const int IncludeAllTypes = 1;
    internal const int IncludeSerializable = 2;
    internal const int IncludeDataContract = 4;
    internal const int IncludeJsonSerializable = 8;

    // LsMsgPack.TypeResolving.Filters.NonPublicProperties
    internal const int NonPublicNone = 0;
    internal const int NonPublicAttributed = 1;
    internal const int NonPublicAll = 2;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
      IncrementalValueProvider<GenerateOptions> options = context.CompilationProvider
        .Combine(context.AnalyzerConfigOptionsProvider)
        .Combine(context.ParseOptionsProvider)
        .Select((input, _) => ReadOptions(input.Left.Left, input.Left.Right, input.Right));

      // The walk needs the options (which non-public properties are described): the context classes are combined with them
      IncrementalValuesProvider<ContextModel> declared = context.SyntaxProvider.ForAttributeWithMetadataName(SerializableAttribute,
        (node, _) => node is ClassDeclarationSyntax,
        (attributed, _) => attributed)
        .Combine(options)
        .Select((input, cancel) => DeclaredContext(input.Left, input.Right, cancel));

      IncrementalValueProvider<ContextModel> assembly = context.CompilationProvider
        .Combine(options)
        .Select((input, cancel) => AssemblyContext(input.Left, input.Right, cancel));

      context.RegisterSourceOutput(declared.Collect().Combine(assembly).Combine(options), (output, input) =>
        Emitter.Emit(output, input.Left.Left, input.Left.Right, input.Right));
    }

    #region Options

    private static GenerateOptions ReadOptions(Compilation compilation, AnalyzerConfigOptionsProvider config, ParseOptions parse)
    {
      AnalyzerConfigOptions global = config.GlobalOptions;
      int include = 0;
      if (global.TryGetValue("build_property.LtMsgPackGenerate", out string text) && !string.IsNullOrWhiteSpace(text))
        include = ParseInclude(text);
      string contextName = global.TryGetValue("build_property.LtMsgPackContextName", out string name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;
      bool autoRegister = !(global.TryGetValue("build_property.LtMsgPackAutoRegister", out string register) && string.Equals(register?.Trim(), "false", StringComparison.OrdinalIgnoreCase));
      string rootNamespace = global.TryGetValue("build_property.RootNamespace", out string root) && !string.IsNullOrWhiteSpace(root) ? root.Trim() : compilation.AssemblyName;
      int nonPublic = NonPublicAttributed;
      if (global.TryGetValue("build_property.LtMsgPackNonPublicProperties", out string nonPublicText) && !string.IsNullOrWhiteSpace(nonPublicText))
        nonPublic = ParseNonPublic(nonPublicText, nonPublic);

      foreach (AttributeData attribute in compilation.Assembly.GetAttributes()) // the assembly attribute wins
      {
        if (attribute.AttributeClass?.ToDisplayString() != GenerateAttribute)
          continue;
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
          switch (argument.Key)
          {
            case "Include": include = argument.Value.Value is int flags ? flags : include; break;
            case "ContextName": contextName = argument.Value.Value as string ?? contextName; break;
            case "AutoRegister": autoRegister = argument.Value.Value is bool on ? on : autoRegister; break;
            case "NonPublicProperties": nonPublic = argument.Value.Value is int value && value >= NonPublicNone && value <= NonPublicAll ? value : nonPublic; break;
          }
        }
      }

      INamedTypeSymbol initializer = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute");
      INamedTypeSymbol dependency = compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute");
      INamedTypeSymbol unsafeAccessor = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute");
      bool csharp9 = parse is CSharpParseOptions csharp && csharp.LanguageVersion >= LanguageVersion.CSharp9;
      bool trimmed = IsTrue(global, "PublishTrimmed") || IsTrue(global, "PublishAot") || IsTrue(global, "IsAotCompatible") || IsTrue(global, "IsTrimmable");
      return new GenerateOptions(include, contextName, autoRegister, rootNamespace, csharp9,
        initializer != null && compilation.IsSymbolAccessibleWithin(initializer, compilation.Assembly),
        dependency != null && compilation.IsSymbolAccessibleWithin(dependency, compilation.Assembly), trimmed, nonPublic,
        unsafeAccessor != null && compilation.IsSymbolAccessibleWithin(unsafeAccessor, compilation.Assembly));
    }

    private static bool IsTrue(AnalyzerConfigOptions global, string property)
    {
      return global.TryGetValue("build_property." + property, out string value) && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "Serializable;DataContract", "AllTypes", "true" (all types), flags separated by ; , | or spaces.
    /// </summary>
    internal static int ParseInclude(string text)
    {
      int include = 0;
      foreach (string part in text.Split(new[] { ';', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries))
      {
        switch (part.Trim().ToLowerInvariant())
        {
          case "true":
          case "all":
          case "alltypes": include |= IncludeAllTypes; break;
          case "serializable": include |= IncludeSerializable; break;
          case "datacontract": include |= IncludeDataContract; break;
          case "json":
          case "jsonserializable":
          case "systemtextjson": include |= IncludeJsonSerializable; break;
        }
      }
      return include;
    }

    /// <summary>
    /// "None", "Attributed" or "All" (ignoring case), <paramref name="fallback"/> for anything else.
    /// </summary>
    internal static int ParseNonPublic(string text, int fallback)
    {
      switch (text.Trim().ToLowerInvariant())
      {
        case "none":
        case "false": return NonPublicNone;
        case "attributed": return NonPublicAttributed;
        case "all":
        case "true": return NonPublicAll;
        default: return fallback;
      }
    }

    #endregion

    #region Contexts declared by the user

    private static ContextModel DeclaredContext(GeneratorAttributeSyntaxContext attributed, GenerateOptions options, CancellationToken cancel)
    {
      INamedTypeSymbol symbol = (INamedTypeSymbol)attributed.TargetSymbol;
      Compilation compilation = attributed.SemanticModel.Compilation;
      List<DiagnosticInfo> diagnostics = new List<DiagnosticInfo>();
      Location location = attributed.TargetNode is ClassDeclarationSyntax declaration ? declaration.Identifier.GetLocation() : symbol.Locations.FirstOrDefault();
      string display = symbol.ToDisplayString();

      // A partial class with the attribute on several declarations comes once per declaration (with all its attributes): the emitter writes the first
      INamedTypeSymbol contextBase = compilation.GetTypeByMetadataName(ContextBase);
      bool valid = true;
      if (contextBase is null || !DerivesFrom(symbol, contextBase))
      {
        diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotAContext.Id, location, display));
        valid = false;
      }

      List<StringValue> containing = new List<StringValue>();
      for (INamedTypeSymbol t = symbol; t != null; t = t.ContainingType)
      {
        if (!IsPartial(t, cancel))
        {
          diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotPartial.Id, location, t.ToDisplayString()));
          valid = false;
        }
        if (t.IsGenericType)
        {
          diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedContext.Id, location, display, SymbolEqualityComparer.Default.Equals(t, symbol) ? "generic" : "nested in a generic type"));
          valid = false;
        }
        if (!SymbolEqualityComparer.Default.Equals(t, symbol))
          containing.Insert(0, new StringValue(t.Name));
      }
      if (symbol.IsAbstract || symbol.IsStatic)
      {
        diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedContext.Id, location, display, symbol.IsStatic ? "static" : "abstract"));
        valid = false;
      }
      foreach (IMethodSymbol constructor in symbol.InstanceConstructors)
      {
        if (!constructor.IsImplicitlyDeclared && constructor.Parameters.Length == 0)
        {
          diagnostics.Add(DiagnosticInfo.Create(Diagnostics.HasConstructor.Id, constructor.Locations.FirstOrDefault() ?? location, display));
          valid = false;
        }
      }

      List<TypeModel> types = new List<TypeModel>();
      if (valid)
      {
        TypeWalker walker = new TypeWalker(compilation, symbol, display, diagnostics, options);
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
          cancel.ThrowIfCancellationRequested();
          if (attribute.AttributeClass?.ToDisplayString() != SerializableAttribute || attribute.ConstructorArguments.Length != 1)
            continue;
          Location at = attribute.ApplicationSyntaxReference?.GetSyntax(cancel).GetLocation() ?? location;
          walker.AddRoot(attribute.ConstructorArguments[0].Value as ITypeSymbol, at);
        }
        walker.Run();
        types = walker.Types;
      }

      bool canRegister = CanRegister(symbol, compilation);
      return new ContextModel(
        symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
        symbol.Name,
        containing.ToEquatable(),
        true,
        symbol.ToDisplayString(TypeWalker.Format),
        valid && canRegister,
        types.ToEquatable(),
        diagnostics.ToEquatable());
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
      for (INamedTypeSymbol t = type.BaseType; t != null; t = t.BaseType)
        if (SymbolEqualityComparer.Default.Equals(t, baseType))
          return true;
      return false;
    }

    private static bool IsPartial(INamedTypeSymbol type, CancellationToken cancel)
    {
      foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
        if (reference.GetSyntax(cancel) is TypeDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
          return false;
      return type.DeclaringSyntaxReferences.Length > 0;
    }

    /// <summary>
    /// The module initializer (a class of its own in the assembly) can reach the context's Default.
    /// </summary>
    private static bool CanRegister(INamedTypeSymbol context, Compilation compilation)
    {
      return compilation.IsSymbolAccessibleWithin(context, compilation.Assembly);
    }

    #endregion

    #region The context of the assembly

    private static ContextModel AssemblyContext(Compilation compilation, GenerateOptions options, CancellationToken cancel)
    {
      if (options.Include == 0 || compilation.GetTypeByMetadataName(ContextBase) is null)
        return null;

      string fullName = string.IsNullOrWhiteSpace(options.ContextName)
        ? (string.IsNullOrWhiteSpace(options.RootNamespace) ? DefaultContextName : options.RootNamespace + "." + DefaultContextName)
        : options.ContextName;
      int dot = fullName.LastIndexOf('.');
      string ns = dot < 0 ? null : fullName.Substring(0, dot);
      string name = dot < 0 ? fullName : fullName.Substring(dot + 1);

      List<DiagnosticInfo> diagnostics = new List<DiagnosticInfo>();
      if (!SyntaxFacts.IsValidIdentifier(name) || (ns != null && ns.Split('.').Any(part => !SyntaxFacts.IsValidIdentifier(part))))
      {
        diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NameConflict.Id, null, fullName, "not a valid class name"));
        return new ContextModel(ns, name, EquatableArray<StringValue>.Empty, false, null, false, EquatableArray<TypeModel>.Empty, diagnostics.ToEquatable());
      }
      if (compilation.GetTypeByMetadataName(fullName) != null)
      {
        diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NameConflict.Id, null, fullName, "a type of that name exists (set another one with LtMsgPackContextName or LtMsgPackGenerate.ContextName)"));
        return new ContextModel(ns, name, EquatableArray<StringValue>.Empty, false, null, false, EquatableArray<TypeModel>.Empty, diagnostics.ToEquatable());
      }

      INamedTypeSymbol contextBase = compilation.GetTypeByMetadataName(ContextBase);
      INamedTypeSymbol exception = compilation.GetTypeByMetadataName("System.Exception");
      INamedTypeSymbol attributeType = compilation.GetTypeByMetadataName("System.Attribute");
      INamedTypeSymbol jsonContext = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonSerializerContext");
      TypeWalker walker = new TypeWalker(compilation, compilation.Assembly, fullName, diagnostics, options);
      foreach (INamedTypeSymbol type in AllTypes(compilation.Assembly.GlobalNamespace))
      {
        cancel.ThrowIfCancellationRequested();
        if ((options.Include & IncludeJsonSerializable) != 0)
        {
          foreach (AttributeData attribute in type.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonSerializableAttribute" && attribute.ConstructorArguments.Length > 0)
              walker.AddRoot(attribute.ConstructorArguments[0].Value as ITypeSymbol, null);
        }

        if (type.TypeKind != TypeKind.Class || type.IsStatic || type.IsGenericType || !type.CanBeReferencedByName || type.IsImplicitlyDeclared
          || DerivesFrom(type, contextBase) || (exception != null && DerivesFrom(type, exception)) || (attributeType != null && DerivesFrom(type, attributeType))
          || (jsonContext != null && DerivesFrom(type, jsonContext)))
          continue;

        bool root = false;
        if ((options.Include & IncludeAllTypes) != 0)
          root = HasProperties(type, options.NonPublic);
        if ((options.Include & IncludeSerializable) != 0 && (type.IsSerializable || HasAttribute(type, "System.SerializableAttribute")))
          root = true;
        if ((options.Include & IncludeDataContract) != 0 && HasAttribute(type, "System.Runtime.Serialization.DataContractAttribute"))
          root = true;
        if (root && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
          walker.AddRoot(type, null);
      }
      walker.Run();

      return new ContextModel(ns, name, EquatableArray<StringValue>.Empty, false, "global::" + fullName, true, walker.Types.ToEquatable(), diagnostics.ToEquatable());
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
    {
      foreach (INamespaceOrTypeSymbol member in ns.GetMembers())
      {
        if (member is INamespaceSymbol child)
        {
          foreach (INamedTypeSymbol type in AllTypes(child))
            yield return type;
        }
        else if (member is INamedTypeSymbol type)
        {
          yield return type;
          foreach (INamedTypeSymbol nested in Nested(type))
            yield return nested;
        }
      }
    }

    private static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type)
    {
      foreach (INamedTypeSymbol nested in type.GetTypeMembers())
      {
        yield return nested;
        foreach (INamedTypeSymbol deeper in Nested(nested))
          yield return deeper;
      }
    }

    private static bool HasAttribute(INamedTypeSymbol type, string name)
    {
      foreach (AttributeData attribute in type.GetAttributes())
        if (attribute.AttributeClass?.ToDisplayString() == name)
          return true;
      return false;
    }

    /// <summary>
    /// Public instance properties, or non-public ones the runtime may serialize (<see cref="TypeWalker.IsNonPublicCandidate"/>).
    /// </summary>
    private static bool HasProperties(INamedTypeSymbol type, int nonPublic)
    {
      for (INamedTypeSymbol t = type; t != null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        foreach (ISymbol member in t.GetMembers())
          if (member is IPropertySymbol property && !property.IsStatic && !property.IsIndexer
            && (property.DeclaredAccessibility == Accessibility.Public || TypeWalker.IsNonPublicCandidate(property, nonPublic)))
            return true;
      return false;
    }

    #endregion
  }
}
