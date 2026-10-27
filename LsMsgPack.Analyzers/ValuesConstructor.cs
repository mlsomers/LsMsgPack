using Microsoft.CodeAnalysis;
using System;

namespace LsMsgPack.Analyzers
{
  /// <summary>
  /// The constructor a reader calls with the values of properties, as LsMsgPack.Core's ObjectConstructor chooses it: the one with [JsonConstructor] or [SerializationConstructor],
  /// otherwise the only public one when there is no public parameterless constructor; used when it takes a property without a public setter, or when every parameter takes a property
  /// or has a default value and the type is a class or the constructor is marked. Its parameters take the property with their name (ignoring case, the exact name first) and type.
  /// </summary>
  internal static class ValuesConstructor
  {
    /// <returns>null when the reader calls the parameterless constructor or none</returns>
    public static IMethodSymbol Of(INamedTypeSymbol type)
    {
      if (type.IsAbstract || type.IsAnonymousType || (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct) || IsEnumerable(type))
        return null;

      IMethodSymbol marked = null, single = null;
      int markedCount = 0, publicCount = 0;
      bool publicParameterless = false;
      foreach (IMethodSymbol constructor in type.InstanceConstructors)
      {
        if (type.TypeKind == TypeKind.Struct && constructor.IsImplicitlyDeclared)
          continue; // reflection does not list the default constructor of a struct
        if (IsMarked(constructor))
        {
          marked = constructor;
          markedCount++;
        }
        if (constructor.DeclaredAccessibility != Accessibility.Public)
          continue;
        publicCount++;
        single = constructor;
        publicParameterless |= constructor.Parameters.Length == 0;
      }

      IMethodSymbol chosen;
      if (markedCount > 0)
        chosen = markedCount == 1 && marked.Parameters.Length > 0 ? marked : null;
      else
        chosen = publicCount == 1 && !publicParameterless ? single : null;
      if (chosen is null)
        return null;

      bool setsReadOnly = false, allTaken = true;
      foreach (IParameterSymbol parameter in chosen.Parameters)
      {
        IPropertySymbol property = Find(type, parameter);
        if (property is null)
        {
          allTaken &= parameter.HasExplicitDefaultValue || parameter.IsOptional;
          continue;
        }
        setsReadOnly |= property.SetMethod is null || property.SetMethod.DeclaredAccessibility != Accessibility.Public;
      }
      return setsReadOnly || (allTaken && (markedCount > 0 || type.TypeKind == TypeKind.Class)) ? chosen : null;
    }

    /// <summary>
    /// A parameter of the constructor takes the property.
    /// </summary>
    public static bool Takes(IMethodSymbol constructor, IPropertySymbol property)
    {
      foreach (IParameterSymbol parameter in constructor.Parameters)
        if (SymbolEqualityComparer.Default.Equals(Find(constructor.ContainingType, parameter), property))
          return true;
      return false;
    }

    /// <returns>The public instance property with a getter that the parameter takes, null when none (or several with the name ignoring case)</returns>
    private static IPropertySymbol Find(INamedTypeSymbol type, IParameterSymbol parameter)
    {
      if (string.IsNullOrEmpty(parameter.Name))
        return null;
      IPropertySymbol found = null;
      bool ambiguous = false;
      for (INamedTypeSymbol at = type; at != null; at = at.BaseType)
      {
        foreach (ISymbol member in at.GetMembers())
        {
          if (!(member is IPropertySymbol property) || property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is null
            || !SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type) || !string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
            continue;
          if (string.Equals(property.Name, parameter.Name, StringComparison.Ordinal))
            return property;
          if (found != null && found.Name != property.Name)
            ambiguous = true;
          found = found ?? property;
        }
      }
      return ambiguous ? null : found;
    }

    private static bool IsMarked(IMethodSymbol constructor)
    {
      foreach (AttributeData attribute in constructor.GetAttributes())
      {
        switch (attribute.AttributeClass?.ToDisplayString())
        {
          case "System.Text.Json.Serialization.JsonConstructorAttribute":
          case "Newtonsoft.Json.JsonConstructorAttribute":
          case "MessagePack.SerializationConstructorAttribute":
            return true;
        }
      }
      return false;
    }

    private static bool IsEnumerable(INamedTypeSymbol type)
    {
      foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        if (implemented.SpecialType == SpecialType.System_Collections_IEnumerable)
          return true;
      return false;
    }
  }
}
