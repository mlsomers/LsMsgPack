using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Threading;

namespace LsMsgPack.CodeAnalysis
{
  /// <summary>
  /// The converters a project registers in <c>MsgPackOptions.Converters</c>, as far as its code shows them (the options are a runtime value): the converter classes it declares,
  /// unless a <c>[MsgPackConverter(typeof(...))]</c> names them (they are that attribute's), and the converters it creates with <c>new</c> (also of other assemblies: <c>new BlitArrayConverter&lt;Vector3&gt;()</c>).
  /// A converter found here that the options don't have only costs the generator some code and the analyzers some warnings, it never changes what is written.
  /// <para>Shared by the source generator (<c>TypeWalker</c>) and the analyzers (<c>ModelTypes</c>): the file is linked into both projects.</para>
  /// </summary>
  internal static class OptionsConverters
  {
    internal const int Declared = 1;
    internal const int Created = 2;
    internal const int Named = 3;

    private const int MaxChain = 32;

    /// <summary>
    /// A node that may declare, create or name a converter: class declarations with a base list, object creations, <c>[MsgPackConverter(typeof(...))]</c>.
    /// </summary>
    internal static bool IsCandidate(SyntaxNode node)
    {
      if (node is ObjectCreationExpressionSyntax || node is ImplicitObjectCreationExpressionSyntax)
        return true;
      if (node is ClassDeclarationSyntax declaration)
        return declaration.BaseList != null;
      return node is AttributeSyntax attribute && IsConverterAttribute(attribute) != null;
    }

    /// <summary>
    /// The type in <c>[MsgPackConverter(typeof(T))]</c>, by the attribute's name (an alias is not recognized); null for another attribute.
    /// </summary>
    private static TypeSyntax IsConverterAttribute(AttributeSyntax attribute)
    {
      SimpleNameSyntax name = attribute.Name is QualifiedNameSyntax qualified ? qualified.Right : attribute.Name as SimpleNameSyntax;
      if (name is null || (name.Identifier.ValueText != "MsgPackConverter" && name.Identifier.ValueText != "MsgPackConverterAttribute"))
        return null;
      if (attribute.ArgumentList is null || attribute.ArgumentList.Arguments.Count != 1)
        return null;
      return (attribute.ArgumentList.Arguments[0].Expression as TypeOfExpressionSyntax)?.Type;
    }

    /// <summary>
    /// The closed converter class the node declares, creates or names (<see cref="IsCandidate"/>), null for none.
    /// Only type names are bound (speculatively, at their position), never the method bodies the creations are in.
    /// </summary>
    /// <param name="kind"><see cref="Declared"/>, <see cref="Created"/> or <see cref="Named"/></param>
    internal static INamedTypeSymbol Find(SyntaxNode node, SemanticModel model, CancellationToken cancel, out int kind)
    {
      ITypeSymbol type = null;
      kind = 0;
      switch (node)
      {
        case ClassDeclarationSyntax declaration:
          kind = Declared;
          type = model.GetDeclaredSymbol(declaration, cancel) as ITypeSymbol;
          break;
        case ObjectCreationExpressionSyntax creation:
          kind = Created;
          type = TypeOf(creation.Type, model);
          break;
        case ImplicitObjectCreationExpressionSyntax implicitCreation: // new() only where the declaration names the type
          kind = Created;
          TypeSyntax target = TargetType(implicitCreation);
          type = target is null ? null : TypeOf(target, model);
          break;
        case AttributeSyntax attribute:
          kind = Named;
          TypeSyntax named = IsConverterAttribute(attribute);
          type = named is null ? null : TypeOf(named, model);
          break;
      }
      return type is INamedTypeSymbol converter && ConverterOf(converter, out ITypeSymbol unused, out ITypeSymbol unusedSurrogate) ? converter : null;
    }

    private static ITypeSymbol TypeOf(TypeSyntax syntax, SemanticModel model)
    {
      return model.GetSpeculativeTypeInfo(syntax.SpanStart, syntax, SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
    }

    /// <summary>
    /// The declared type a <c>new()</c> creates: of a local or field (<c>X x = new();</c>) or a property (<c>X P { get; } = new();</c>), null otherwise.
    /// </summary>
    private static TypeSyntax TargetType(ImplicitObjectCreationExpressionSyntax creation)
    {
      if (!(creation.Parent is EqualsValueClauseSyntax equals))
        return null;
      if (equals.Parent is PropertyDeclarationSyntax property)
        return property.Type;
      if (equals.Parent is VariableDeclaratorSyntax && equals.Parent.Parent is VariableDeclarationSyntax variable && !variable.Type.IsVar)
        return variable.Type;
      return null;
    }

    /// <summary>
    /// A converter class that can be created (not abstract, no open type parameters in what it converts): the T of its <c>MsgPackConverter&lt;T, TSurrogate&gt;</c> with the surrogate,
    /// or of its <c>MsgPackBinaryConverter&lt;T&gt;</c> (surrogate null: binary data, nothing to follow).
    /// </summary>
    internal static bool ConverterOf(INamedTypeSymbol converter, out ITypeSymbol converted, out ITypeSymbol surrogate)
    {
      converted = null;
      surrogate = null;
      if (converter.IsAbstract || converter.TypeKind != TypeKind.Class)
        return false;
      for (INamedTypeSymbol t = converter.BaseType; t != null; t = t.BaseType)
      {
        if (t.ContainingNamespace?.ToDisplayString() != "LsMsgPack.Converters")
          continue;
        bool surrogateKind = t.Name == "MsgPackConverter" && t.TypeArguments.Length == 2;
        bool binaryKind = t.Name == "MsgPackBinaryConverter" && t.TypeArguments.Length == 1;
        if (!surrogateKind && !binaryKind)
          continue;
        if (ContainsTypeParameters(t.TypeArguments[0]) || (surrogateKind && ContainsTypeParameters(t.TypeArguments[1])))
          return false; // the definition of a generic converter: its constructed types are found where they are created
        converted = t.TypeArguments[0];
        surrogate = surrogateKind ? t.TypeArguments[1] : null;
        return true;
      }
      return false;
    }

    private static bool ContainsTypeParameters(ITypeSymbol type)
    {
      switch (type)
      {
        case ITypeParameterSymbol _:
          return true;
        case IArrayTypeSymbol array:
          return ContainsTypeParameters(array.ElementType);
        case INamedTypeSymbol named:
          for (int t = 0; t < named.TypeArguments.Length; t++)
            if (ContainsTypeParameters(named.TypeArguments[t]))
              return true;
          return named.ContainingType != null && ContainsTypeParameters(named.ContainingType);
        default:
          return false;
      }
    }

    /// <summary>
    /// The converters counted as the options': created ones, and declared ones no attribute names.
    /// </summary>
    /// <param name="found">Each converter found with its kind (<see cref="Find"/>), by any key that identifies the type</param>
    internal static List<TKey> Registered<TKey>(IEnumerable<KeyValuePair<TKey, int>> found, IEqualityComparer<TKey> comparer)
    {
      HashSet<TKey> named = new HashSet<TKey>(comparer);
      HashSet<TKey> registered = new HashSet<TKey>(comparer);
      List<TKey> result = new List<TKey>();
      foreach (KeyValuePair<TKey, int> converter in found)
        if (converter.Value == Named)
          named.Add(converter.Key);
      foreach (KeyValuePair<TKey, int> converter in found)
        if ((converter.Value == Created || (converter.Value == Declared && !named.Contains(converter.Key))) && registered.Add(converter.Key))
          result.Add(converter.Key);
      return result;
    }

    /// <summary>
    /// The surrogates per converted type (a null surrogate for a binary converter). A type converted by several converters (other options) has them all.
    /// </summary>
    internal static Dictionary<ITypeSymbol, List<ITypeSymbol>> Map(IEnumerable<INamedTypeSymbol> converters)
    {
      Dictionary<ITypeSymbol, List<ITypeSymbol>> map = new Dictionary<ITypeSymbol, List<ITypeSymbol>>(SymbolEqualityComparer.Default);
      foreach (INamedTypeSymbol converter in converters)
      {
        if (!ConverterOf(converter, out ITypeSymbol converted, out ITypeSymbol surrogate))
          continue;
        converted = (ITypeSymbol)converted.WithNullableAnnotation(NullableAnnotation.None);
        if (!map.TryGetValue(converted, out List<ITypeSymbol> surrogates))
          map.Add(converted, surrogates = new List<ITypeSymbol>());
        surrogate = surrogate is null ? null : (ITypeSymbol)surrogate.WithNullableAnnotation(NullableAnnotation.None);
        bool known = false;
        for (int s = 0; s < surrogates.Count; s++)
          known |= SymbolEqualityComparer.Default.Equals(surrogates[s], surrogate);
        if (!known)
          surrogates.Add(surrogate);
      }
      return map;
    }

    /// <summary>
    /// The type as the options' converters write it: converted parts (also elements and type arguments) replaced by their surrogate, a binary converter's by <c>byte[]</c>.
    /// The first surrogate when several converters convert one type. For checks of what is written (the analyzers' type rules).
    /// </summary>
    internal static ITypeSymbol AsWritten(ITypeSymbol type, Dictionary<ITypeSymbol, List<ITypeSymbol>> map, Compilation compilation)
    {
      return map.Count == 0 || type is null ? type : AsWritten(type, map, compilation, 0);
    }

    private static ITypeSymbol AsWritten(ITypeSymbol type, Dictionary<ITypeSymbol, List<ITypeSymbol>> map, Compilation compilation, int depth)
    {
      if (depth > MaxChain)
        return type; // a chain of converters back to a converted type (the runtime refuses it)
      if (map.TryGetValue((ITypeSymbol)type.WithNullableAnnotation(NullableAnnotation.None), out List<ITypeSymbol> surrogates))
      {
        ITypeSymbol surrogate = surrogates[0];
        return surrogate is null ? compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_Byte)) : AsWritten(surrogate, map, compilation, depth + 1);
      }
      if (type is IArrayTypeSymbol array)
      {
        ITypeSymbol element = AsWritten(array.ElementType, map, compilation, depth + 1);
        return ReferenceEquals(element, array.ElementType) ? type : compilation.CreateArrayTypeSymbol(element, array.Rank);
      }
      if (!(type is INamedTypeSymbol named) || named.TypeArguments.Length == 0)
        return type;
      ITypeSymbol[] arguments = null;
      for (int t = 0; t < named.TypeArguments.Length; t++)
      {
        ITypeSymbol argument = AsWritten(named.TypeArguments[t], map, compilation, depth + 1);
        if (ReferenceEquals(argument, named.TypeArguments[t]))
          continue;
        if (arguments is null)
        {
          arguments = new ITypeSymbol[named.TypeArguments.Length];
          for (int a = 0; a < arguments.Length; a++)
            arguments[a] = named.TypeArguments[a];
        }
        arguments[t] = argument;
      }
      return arguments is null ? type : named.ConstructedFrom.Construct(arguments);
    }
  }
}
