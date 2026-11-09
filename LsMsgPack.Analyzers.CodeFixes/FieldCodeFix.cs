using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPack.Analyzers
{
  /// <summary>
  /// LSMP010: adds <c>[DataMember]</c> to the public field (System.Runtime.Serialization; outside a <c>[DataContract]</c> it only opts the field in), or makes it an auto-property
  /// (<c>{ get; set; }</c>, <c>{ get; }</c> for a readonly field; the initializer and attributes stay, attributes that cannot be on a property get the <c>field:</c> target).
  /// Making it a property breaks code that takes the field by <c>ref</c> or changes a struct it holds in place: the fix is offered, the developer decides.
  /// </summary>
  [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FieldCodeFix)), Shared]
  public sealed class FieldCodeFix : CodeFixProvider
  {
    private const string DataMember = "System.Runtime.Serialization.DataMemberAttribute";

    // AttributeTargets
    private const int PropertyTarget = 128;
    private const int AllTargets = 32767;

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Rules.FieldLeftOut.Id);

    public override FixAllProvider GetFixAllProvider()
    {
      return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
      SyntaxNode root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
      SemanticModel model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
      foreach (Diagnostic diagnostic in context.Diagnostics)
      {
        VariableDeclaratorSyntax variable = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        FieldDeclarationSyntax field = variable?.FirstAncestorOrSelf<FieldDeclarationSyntax>();
        if (field is null)
          continue;

        if (model.Compilation.GetTypeByMetadataName(DataMember) != null)
        {
          const string title = "Add [DataMember]";
          context.RegisterCodeFix(CodeAction.Create(title, c => DefaultValueCodeFix.AddAttribute(context.Document, field, "System.Runtime.Serialization.DataMember", null, c), title), diagnostic);
        }
        if (CanBeProperty(field))
        {
          string title = string.Concat("Make '", variable.Identifier.ValueText, "' a property");
          context.RegisterCodeFix(CodeAction.Create(title, c => MakeProperty(context.Document, field, variable, c), "Make it a property"), diagnostic);
        }
      }
    }

    /// <summary>
    /// Modifiers a property cannot have: volatile, fixed (fixed-size buffers are never serialized anyway), const and static (neither are reported).
    /// </summary>
    private static bool CanBeProperty(FieldDeclarationSyntax field)
    {
      foreach (SyntaxToken modifier in field.Modifiers)
        if (modifier.IsKind(SyntaxKind.VolatileKeyword) || modifier.IsKind(SyntaxKind.FixedKeyword) || modifier.IsKind(SyntaxKind.ConstKeyword) || modifier.IsKind(SyntaxKind.StaticKeyword))
          return false;
      return true;
    }

    private static async Task<Document> MakeProperty(Document document, FieldDeclarationSyntax field, VariableDeclaratorSyntax variable, CancellationToken cancellation)
    {
      SyntaxNode root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
      SemanticModel model = await document.GetSemanticModelAsync(cancellation).ConfigureAwait(false);

      bool readOnly = false;
      List<SyntaxToken> modifiers = new List<SyntaxToken>(field.Modifiers.Count);
      foreach (SyntaxToken modifier in field.Modifiers)
      {
        if (modifier.IsKind(SyntaxKind.ReadOnlyKeyword))
          readOnly = true;
        else
          modifiers.Add(modifier);
      }

      List<AttributeListSyntax> attributes = new List<AttributeListSyntax>(field.AttributeLists.Count);
      foreach (AttributeListSyntax list in field.AttributeLists)
        attributes.Add(list.Target is null && !AllowedOnProperties(list, model, cancellation)
          ? list.WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Identifier("field"), SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.ColonToken, SyntaxFactory.TriviaList(SyntaxFactory.Space))))
          : list);

      AccessorListSyntax accessors = ((PropertyDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(readOnly ? "int X { get; }" : "int X { get; set; }")).AccessorList.WithoutTrivia();
      EqualsValueClauseSyntax initializer = variable.Initializer;
      if (initializer != null)
        accessors = accessors.WithTrailingTrivia(SyntaxFactory.Space);
      TypeSyntax type = field.Declaration.Type;
      SyntaxToken semicolon = field.SemicolonToken;
      SyntaxToken identifier = variable.Identifier.WithTrailingTrivia(SyntaxFactory.Space);

      bool single = field.Declaration.Variables.Count == 1;
      PropertyDeclarationSyntax property = SyntaxFactory.PropertyDeclaration(SyntaxFactory.List(attributes), SyntaxFactory.TokenList(modifiers), type, null, identifier, accessors, null,
        initializer?.WithoutLeadingTrivia(), initializer is null ? default(SyntaxToken) : semicolon);
      if (initializer is null)
        property = property.WithAccessorList(accessors.WithTrailingTrivia(semicolon.TrailingTrivia));

      if (single) // the comments and indentation of the declaration (on its first token, which may have been readonly)
        return document.WithSyntaxRoot(root.ReplaceNode(field, property.WithLeadingTrivia(field.GetLeadingTrivia())));

      // int A, B: B becomes a property after the declaration of the others (with its indentation, not its comments)
      FieldDeclarationSyntax rest = field.WithDeclaration(field.Declaration.WithVariables(field.Declaration.Variables.Remove(variable)));
      property = property.WithLeadingTrivia(DefaultValueCodeFix.Indentation(field.GetLeadingTrivia())).WithTrailingTrivia(semicolon.TrailingTrivia);
      return document.WithSyntaxRoot(root.ReplaceNode(field, new SyntaxNode[] { rest, property }));
    }

    /// <summary>
    /// Every attribute of the list may be on a property (its AttributeUsage); otherwise the list gets the target <c>field:</c> (allowed on auto-properties).
    /// </summary>
    private static bool AllowedOnProperties(AttributeListSyntax list, SemanticModel model, CancellationToken cancellation)
    {
      foreach (AttributeSyntax attribute in list.Attributes)
      {
        INamedTypeSymbol type = (model.GetSymbolInfo(attribute, cancellation).Symbol as IMethodSymbol)?.ContainingType;
        if (type != null && (ValidOn(type) & PropertyTarget) == 0)
          return false;
      }
      return true;
    }

    private static int ValidOn(INamedTypeSymbol attribute)
    {
      for (INamedTypeSymbol t = attribute; t != null; t = t.BaseType)
        foreach (AttributeData usage in t.GetAttributes())
          if (usage.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute" && usage.ConstructorArguments.Length > 0 && usage.ConstructorArguments[0].Value is int targets)
            return targets;
      return AllTargets;
    }
  }
}
