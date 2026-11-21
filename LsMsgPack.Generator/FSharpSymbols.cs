using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;

namespace LsMsgPack.Generator
{
  /// <summary>
  /// The F# types the runtime writes its own way (Core's FSharp/FSharpTypes.cs, docs/FSharp.md), recognized from their metadata as FSharp.Core compiles them:
  /// [CompilationMapping(SumType)] on a union, [CompilationMapping(UnionCase, tag)] on its factories <c>NewX</c> and the getters <c>get_X</c> of fieldless cases,
  /// [CompilationMapping(Field, tag, position)] on the fields. Keep it equal to the runtime: <c>GeneratedFieldsTests.Members_AsTheRuntimeSeesThem</c> compares them.
  /// </summary>
  internal static class FSharpSymbols
  {
    private const string CompilationMapping = "Microsoft.FSharp.Core.CompilationMappingAttribute";
    private const int SumType = 1;
    private const int Field = 4;
    private const int UnionCase = 8;
    private const int KindMask = 31;

    /// <summary>
    /// A type the runtime writes through a converter of its own: an option (as its value), a union whose values of exactly this type are names or one field, the class of a fieldless case,
    /// a struct union with fields (refused). Its members are not described, the surrogate is walked instead (null: nothing to walk).
    /// </summary>
    internal static bool IsConverted(INamedTypeSymbol type, out ITypeSymbol surrogate)
    {
      surrogate = null;
      if (type.IsAbstract || type.TypeKind == TypeKind.Interface || type.TypeKind == TypeKind.Enum)
        return false;
      if (IsOption(type))
      {
        surrogate = type.TypeArguments[0];
        return true;
      }
      Union union = UnionOf(type);
      if (union is null)
        return false;
      if (!SymbolEqualityComparer.Default.Equals(type, union.Type)) // a case class
        return union.CaseOfClass(type)?.Fields.Count == 0;
      if (union.Cases.Count == 1)
      {
        Case single = union.Cases[0];
        if (single.Fields.Count == 1)
          surrogate = single.Fields[0].Type;
        return single.Fields.Count <= 1;
      }
      return union.ValuesAreNames || type.TypeKind == TypeKind.Struct;
    }

    /// <summary>
    /// The members the runtime writes for a union type or a case class: the fields of its case in their order (empty for a union type of several cases), null for other types.
    /// </summary>
    internal static List<IPropertySymbol> FieldsOf(INamedTypeSymbol type)
    {
      Union union = UnionOf(type);
      if (union is null)
        return null;
      return union.CaseOfClass(type)?.Fields ?? new List<IPropertySymbol>();
    }

    private static bool IsOption(INamedTypeSymbol type)
    {
      if (!type.IsGenericType || type.TypeArguments.Length != 1)
        return false;
      string name = type.OriginalDefinition.MetadataName;
      return (name == "FSharpOption`1" || name == "FSharpValueOption`1") && type.ContainingNamespace?.ToDisplayString() == "Microsoft.FSharp.Core";
    }

    private static bool IsFSharpCoreCollection(INamedTypeSymbol type)
    {
      return type.OriginalDefinition.MetadataName == "FSharpList`1" && type.ContainingNamespace?.ToDisplayString() == "Microsoft.FSharp.Collections";
    }

    private static Union UnionOf(INamedTypeSymbol type)
    {
      if (IsUnionType(type))
        return Union.Create(type);
      INamedTypeSymbol baseType = type.BaseType;
      if (baseType != null && IsUnionType(baseType))
      {
        Union union = Union.Create(baseType);
        if (union?.CaseOfClass(type) != null)
          return union;
      }
      return null;
    }

    private static bool IsUnionType(INamedTypeSymbol type)
    {
      return TryGetMapping(type, out int flags, out _, out _) && (flags & KindMask) == SumType && !IsOption(type) && !IsFSharpCoreCollection(type);
    }

    private static bool TryGetMapping(ISymbol symbol, out int flags, out int first, out int second)
    {
      flags = 0;
      first = -1;
      second = -1;
      foreach (AttributeData attribute in symbol.GetAttributes())
      {
        if (attribute.AttributeClass?.ToDisplayString() != CompilationMapping || attribute.ConstructorArguments.Length == 0 || !(attribute.ConstructorArguments[0].Value is int kind))
          continue;
        flags = kind;
        if (attribute.ConstructorArguments.Length > 1 && attribute.ConstructorArguments[1].Value is int a)
          first = a;
        if (attribute.ConstructorArguments.Length > 2 && attribute.ConstructorArguments[2].Value is int b)
          second = b;
        return true;
      }
      return false;
    }

    private sealed class Case
    {
      internal string Name;
      internal int Tag;
      internal INamedTypeSymbol Class;
      internal List<IPropertySymbol> Fields;
    }

    private sealed class Union
    {
      internal INamedTypeSymbol Type;
      internal List<Case> Cases;
      internal bool ValuesAreNames;

      internal Case CaseOfClass(INamedTypeSymbol type)
      {
        if (SymbolEqualityComparer.Default.Equals(type, Type))
          return Cases.Count == 1 ? Cases[0] : null;
        foreach (Case unionCase in Cases)
        {
          if (SymbolEqualityComparer.Default.Equals(unionCase.Class, type))
            return unionCase;
        }
        return null;
      }

      internal static Union Create(INamedTypeSymbol type)
      {
        SortedDictionary<int, Case> byTag = new SortedDictionary<int, Case>();
        foreach (ISymbol member in type.GetMembers())
        {
          if (!(member is IMethodSymbol method) || !method.IsStatic || !TryGetMapping(method, out int flags, out int tag, out _) || (flags & KindMask) != UnionCase || tag < 0 || byTag.ContainsKey(tag))
            continue;
          string name = method.MetadataName;
          bool fieldless = name.StartsWith("get_", StringComparison.Ordinal) && method.Parameters.Length == 0;
          byTag.Add(tag, new Case { Tag = tag, Name = fieldless ? name.Substring(4) : name.StartsWith("New", StringComparison.Ordinal) && name.Length > 3 ? name.Substring(3) : name });
        }
        if (byTag.Count == 0)
          return null;

        Dictionary<string, INamedTypeSymbol> nested = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (INamedTypeSymbol candidate in type.GetTypeMembers())
        {
          if (SymbolEqualityComparer.Default.Equals(candidate.BaseType, type) && !nested.ContainsKey(candidate.Name))
            nested.Add(candidate.Name, candidate);
        }

        Union union = new Union { Type = type, Cases = new List<Case>() };
        bool valuesAreNames = true;
        int count = 0;
        foreach (Case unionCase in byTag.Values)
        {
          if (unionCase.Tag != count++)
            return null;
          if (!nested.TryGetValue(unionCase.Name, out INamedTypeSymbol caseClass) && !nested.TryGetValue("_" + unionCase.Name, out caseClass))
            caseClass = type;
          unionCase.Class = caseClass;
          unionCase.Fields = FieldsOf(caseClass, unionCase.Tag);
          if (unionCase.Fields.Count != 0 && SymbolEqualityComparer.Default.Equals(caseClass, type))
            valuesAreNames = false;
          union.Cases.Add(unionCase);
        }
        union.ValuesAreNames = valuesAreNames;
        return union;
      }

      private static List<IPropertySymbol> FieldsOf(INamedTypeSymbol caseClass, int tag)
      {
        SortedDictionary<int, IPropertySymbol> byPosition = new SortedDictionary<int, IPropertySymbol>();
        foreach (ISymbol member in caseClass.GetMembers())
        {
          if (member is IPropertySymbol property && !property.IsStatic && TryGetMapping(property, out int flags, out int caseTag, out int position)
            && (flags & KindMask) == Field && caseTag == tag && position >= 0 && !byPosition.ContainsKey(position))
            byPosition.Add(position, property);
        }
        return new List<IPropertySymbol>(byPosition.Values);
      }
    }
  }
}
