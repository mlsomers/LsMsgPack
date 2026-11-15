using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace LsMsgPack.CodeAnalysis
{
  /// <summary>
  /// The members that keep the members of the data a class does not have (Core's ExtensionDataMember, docs/ExtensionData.md), as the source can tell them.
  /// <para>Shared by the source generator (<c>TypeWalker</c>) and the analyzers (<c>ModelTypes</c>, <c>ModelAnalyzer</c>: LSMP011): the file is linked into both projects.</para>
  /// </summary>
  internal static class ExtensionDataMembers
  {
    /// <summary>
    /// A class or struct with an extension data member, as Core's ExtensionDataMember finds it (docs/ExtensionData.md): a public MsgPackExtensionData property or field, or any instance member
    /// with [MsgPackExtensionData] or [JsonExtensionData] (by name), in the class or its base classes. The runtime writes and reads such a class itself: no code is generated for its members.
    /// </summary>
    internal static bool HasExtensionData(INamedTypeSymbol type)
    {
      return Candidates(type).Count > 0;
    }

    /// <summary>
    /// The extension data members of the type as Core's ExtensionDataMember.Find sees them: the type's own first, then those of its base classes (not of the framework's), each class's properties
    /// before its fields, a name only once (an overridden property). More than one, or one of another type (<see cref="IsValidType"/>), and the runtime throws for the type.
    /// </summary>
    internal static List<ISymbol> Candidates(INamedTypeSymbol type)
    {
      List<ISymbol> found = new List<ISymbol>();
      HashSet<string> names = new HashSet<string>();
      for (INamedTypeSymbol t = type; t != null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType)
      {
        if (IsFramework(t))
          continue;
        for (int pass = 0; pass < 2; pass++) // the properties, then the fields (as reflection lists them there)
        {
          foreach (ISymbol member in t.GetMembers())
          {
            bool property = member is IPropertySymbol;
            if (property != (pass == 0) || !(property || member is IFieldSymbol) || member.IsStatic || member.IsImplicitlyDeclared || names.Contains(member.Name))
              continue;
            if ((member is IPropertySymbol indexer && indexer.IsIndexer) || member.Name.IndexOf('<') >= 0 || member.Name.IndexOf('$') >= 0)
              continue;
            if (!IsCandidate(member))
              continue;
            names.Add(member.Name);
            found.Add(member);
          }
        }
      }
      return found;
    }

    /// <summary>
    /// Core's ExtensionDataMember.IsCandidate: marked as extension data (any visibility), or a MsgPackExtensionData with a public getter (property) or public (field).
    /// </summary>
    private static bool IsCandidate(ISymbol member)
    {
      if (IsMarkedExtensionData(member))
        return true;
      ITypeSymbol type = TypeOf(member);
      bool isPublic = member is IPropertySymbol property ? property.GetMethod?.DeclaredAccessibility == Accessibility.Public : member.DeclaredAccessibility == Accessibility.Public;
      return isPublic && type != null && IsExtensionDataType(type);
    }

    /// <summary>
    /// A type the runtime keeps extension data in (Core's ExtensionDataMember.DictionaryTypeOf): MsgPackExtensionData, a type a Dictionary&lt;string, object&gt; can be assigned to
    /// (IDictionary&lt;string, object&gt;, the dictionary itself...), or a class implementing IDictionary&lt;string, object&gt; with a public parameterless constructor.
    /// </summary>
    internal static bool IsValidType(ITypeSymbol type, Compilation compilation)
    {
      if (IsExtensionDataType(type) || type.SpecialType == SpecialType.System_Object)
        return true;
      INamedTypeSymbol definition = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2");
      INamedTypeSymbol idictionary = compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
      ITypeSymbol stringType = compilation.GetSpecialType(SpecialType.System_String);
      ITypeSymbol objectType = compilation.GetSpecialType(SpecialType.System_Object);
      if (definition is null || idictionary is null)
        return true; // can't tell: no report
      INamedTypeSymbol dictionary = definition.Construct(stringType, objectType);
      if (SymbolEqualityComparer.Default.Equals(type, dictionary))
        return true;
      foreach (INamedTypeSymbol implemented in dictionary.AllInterfaces)
        if (SymbolEqualityComparer.Default.Equals(type, implemented))
          return true;

      if (!(type is INamedTypeSymbol named) || named.TypeKind != TypeKind.Class || named.IsAbstract)
        return false;
      INamedTypeSymbol wanted = idictionary.Construct(stringType, objectType);
      bool implements = false;
      foreach (INamedTypeSymbol implemented in named.AllInterfaces)
        implements |= SymbolEqualityComparer.Default.Equals(implemented, wanted);
      if (!implements)
        return false;
      foreach (IMethodSymbol constructor in named.InstanceConstructors)
        if (constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public)
          return true;
      return false;
    }

    /// <summary>
    /// A member that is never a property in the data (FullPropertyInfo.FindMembers leaves it out): of the type MsgPackExtensionData, or marked as extension data.
    /// </summary>
    internal static bool IsExtensionMember(ISymbol member)
    {
      ITypeSymbol type = TypeOf(member);
      return type != null && (IsExtensionDataType(type) || IsMarkedExtensionData(member));
    }

    internal static bool IsExtensionDataType(ITypeSymbol type)
    {
      return type.Name == "MsgPackExtensionData" && type.ContainingNamespace?.ToDisplayString() == "LsMsgPack";
    }

    internal static ITypeSymbol TypeOf(ISymbol member)
    {
      return (member as IPropertySymbol)?.Type ?? (member as IFieldSymbol)?.Type;
    }

    private static bool IsMarkedExtensionData(ISymbol member)
    {
      foreach (AttributeData attribute in member.GetAttributes())
      {
        string name = attribute.AttributeClass?.Name;
        if (name == "MsgPackExtensionDataAttribute" || name == "JsonExtensionDataAttribute")
          return true;
      }
      return false;
    }

    /// <summary>
    /// A class of the framework (TypeResolver.IsFrameworkAssembly): Core does not look for extension data members there.
    /// </summary>
    private static bool IsFramework(INamedTypeSymbol type)
    {
      string name = type.ContainingAssembly?.Name;
      return name != null && (name == "mscorlib" || name == "netstandard" || name == "System" || name.StartsWith("System.", System.StringComparison.Ordinal) || name.StartsWith("Microsoft.", System.StringComparison.Ordinal));
    }
  }
}
