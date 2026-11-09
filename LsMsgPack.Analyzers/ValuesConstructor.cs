using Microsoft.CodeAnalysis;
using System;

namespace LsMsgPack.Analyzers
{
  /// <summary>
  /// The constructor a reader calls with the values of properties, as LsMsgPack.Core's ObjectConstructor chooses it: the one with [JsonConstructor] or [SerializationConstructor],
  /// otherwise the only public one when there is no public parameterless constructor; used when it takes a property without a public setter or a readonly field, or when every parameter takes a property
  /// (or public field) or has a default value and the type is a class or the constructor is marked. Its parameters take the property or public field with their name (ignoring case, the exact name first)
  /// and type, whatever fields the serializers write (ObjectConstructor is per type).
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
        ISymbol member = Find(type, parameter);
        if (member is null)
        {
          allTaken &= parameter.HasExplicitDefaultValue || parameter.IsOptional;
          continue;
        }
        if (member is IFieldSymbol field)
          setsReadOnly |= field.IsReadOnly;
        else
        {
          IPropertySymbol property = (IPropertySymbol)member;
          setsReadOnly |= property.SetMethod is null || property.SetMethod.DeclaredAccessibility != Accessibility.Public;
        }
      }
      return setsReadOnly || (allTaken && (markedCount > 0 || type.TypeKind == TypeKind.Class)) ? chosen : null;
    }

    /// <summary>
    /// A parameter of the constructor takes the property or field.
    /// </summary>
    public static bool Takes(IMethodSymbol constructor, ISymbol member)
    {
      foreach (IParameterSymbol parameter in constructor.Parameters)
        if (SymbolEqualityComparer.Default.Equals(Find(constructor.ContainingType, parameter), member))
          return true;
      return false;
    }

    /// <returns>The public instance property with a getter or the public field (FullPropertyInfo.PublicMembers, not of the framework's types) that the parameter takes, null when none
    /// (or several with the name ignoring case)</returns>
    private static ISymbol Find(INamedTypeSymbol type, IParameterSymbol parameter)
    {
      if (string.IsNullOrEmpty(parameter.Name))
        return null;
      ISymbol found = null;
      bool ambiguous = false;
      for (INamedTypeSymbol at = type; at != null; at = at.BaseType)
      {
        foreach (ISymbol member in at.GetMembers())
        {
          ITypeSymbol memberType;
          if (member is IPropertySymbol property && !property.IsStatic && !property.IsIndexer && property.DeclaredAccessibility == Accessibility.Public && property.GetMethod != null)
            memberType = property.Type;
          else if (member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public && Fields.IsOfState(field) && !ModelAnalyzer.IsFramework(at))
            memberType = field.Type;
          else
            continue;
          if (!SymbolEqualityComparer.Default.Equals(memberType, parameter.Type) || !string.Equals(member.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
            continue;
          if (string.Equals(member.Name, parameter.Name, StringComparison.Ordinal))
            return member;
          if (found != null && found.Name != member.Name)
            ambiguous = true;
          found = found ?? member;
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
