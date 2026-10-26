using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Concurrent;

namespace LsMsgPack.Analyzers
{
  /// <summary>
  /// Serialized classes declared in referenced assemblies that no analyzer checked (LSMP008): the rules need the source of a class, so a class is checked in the project that declares it,
  /// and only when that project asks for it (<c>LsMsgPackAnalyze</c> = <c>AllTypes</c>, which buildTransitive/LsMsgPack.Core.props writes into the assembly as
  /// <c>[assembly: AssemblyMetadata("LsMsgPackAnalyze", "AllTypes")]</c>).
  /// <para>Left out: the framework's types, LsMsgPack's own assemblies and the assemblies of NuGet packages (their classes cannot be changed by the project).</para>
  /// </summary>
  internal sealed class OtherAssemblies
  {
    /// <summary>
    /// The key of the <c>AssemblyMetadata</c> attribute that marks an assembly whose classes were checked, and the MSBuild property it comes from.
    /// </summary>
    public const string MarkerKey = "LsMsgPackAnalyze";

    private readonly Compilation _compilation;
    private readonly string _packageRoot;
    private readonly ConcurrentDictionary<IAssemblySymbol, bool> _unchecked = new ConcurrentDictionary<IAssemblySymbol, bool>(SymbolEqualityComparer.Default);

    public OtherAssemblies(Compilation compilation, AnalyzerOptions options)
    {
      _compilation = compilation;
      if (options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.NuGetPackageRoot", out string root) && !string.IsNullOrWhiteSpace(root))
        _packageRoot = Folder(root.Trim());
    }

    /// <summary>
    /// The first class of another assembly that no analyzer checked in the type: the type itself, the elements of an array, the generic arguments (<c>List&lt;Invoice&gt;</c>). Null when there is none.
    /// </summary>
    public INamedTypeSymbol FindUnchecked(ITypeSymbol type)
    {
      return FindUnchecked(type, 0);
    }

    private INamedTypeSymbol FindUnchecked(ITypeSymbol type, int depth)
    {
      if (type is null || depth > 16)
        return null;
      if (type is IArrayTypeSymbol array)
        return FindUnchecked(array.ElementType, depth + 1);
      if (!(type is INamedTypeSymbol named) || named.TypeKind == TypeKind.Error)
        return null;
      if (IsUncheckedModel(named))
        return named;
      for (int t = 0; t < named.TypeArguments.Length; t++)
      {
        INamedTypeSymbol found = FindUnchecked(named.TypeArguments[t], depth + 1);
        if (found != null)
          return found;
      }
      return null;
    }

    private bool IsUncheckedModel(INamedTypeSymbol type)
    {
      if ((type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct) || type.IsStatic || type.IsAnonymousType || ModelTypes.IsInSource(type) || ModelAnalyzer.IsFramework(type))
        return false;
      IAssemblySymbol assembly = type.ContainingAssembly;
      if (assembly is null || SymbolEqualityComparer.Default.Equals(assembly, _compilation.Assembly) || !HasSettableProperties(type))
        return false;
      return _unchecked.GetOrAdd(assembly, IsUnchecked);
    }

    /// <summary>
    /// The class has properties the serializers write (public, with a public setter), declared by it or a base class that is not the framework's.
    /// </summary>
    private static bool HasSettableProperties(INamedTypeSymbol type)
    {
      for (INamedTypeSymbol t = type; t != null && !ModelAnalyzer.IsFramework(t); t = t.BaseType)
        foreach (ISymbol member in t.GetMembers())
          if (member is IPropertySymbol property && !property.IsStatic && !property.IsIndexer && property.DeclaredAccessibility == Accessibility.Public
            && property.GetMethod != null && property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public)
            return true;
      return false;
    }

    private bool IsUnchecked(IAssemblySymbol assembly)
    {
      switch (assembly.Name)
      {
        case "LsMsgPack":
        case "LsMsgPack.Core":
        case "LtMsgPack":
        case "LsMsgPackFormatters":
        case "LsMsgPackWebApiFormatters":
        case "LsMsgPackMvc":
          return false;
      }
      foreach (AttributeData attribute in assembly.GetAttributes())
      {
        if (attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" && attribute.ConstructorArguments.Length == 2
          && attribute.ConstructorArguments[0].Value as string == MarkerKey
          && string.Equals((attribute.ConstructorArguments[1].Value as string)?.Trim(), "AllTypes", StringComparison.OrdinalIgnoreCase))
          return false;
      }
      return !IsPackage(_compilation.GetMetadataReference(assembly) as PortableExecutableReference);
    }

    /// <summary>
    /// The reference is a file of a NuGet package (the package folder of the build, or the usual places of the global packages folder).
    /// A project reference is a file in the other project's obj or bin folder, or in the IDE a reference without a file.
    /// </summary>
    private bool IsPackage(PortableExecutableReference reference)
    {
      string path = reference?.FilePath;
      if (string.IsNullOrEmpty(path))
        return false;
      path = path.Replace('\\', '/');
      if (_packageRoot != null && path.StartsWith(_packageRoot, StringComparison.OrdinalIgnoreCase))
        return true;
      return path.IndexOf("/.nuget/packages/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("/NuGetFallbackFolder/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Folder(string path)
    {
      path = path.Replace('\\', '/');
      return path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
    }
  }
}
