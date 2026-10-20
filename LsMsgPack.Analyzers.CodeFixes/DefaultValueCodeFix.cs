using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;
using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPack.Analyzers
{
  /// <summary>
  /// LSMP001: adds <c>[DefaultValue(x)]</c> with the initial value. LSMP002 and LSMP003: writes the argument of <c>[DefaultValue]</c> (the initial value, or the value in the property's type),
  /// or (LSMP002) the initializer of an auto-property with the value of the attribute. The analyzer computes the text (<see cref="Rules.ArgumentKey"/>, <see cref="Rules.InitializerKey"/>).
  /// </summary>
  [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DefaultValueCodeFix)), Shared]
  public sealed class DefaultValueCodeFix : CodeFixProvider
  {
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Rules.MissingDefaultValue.Id, Rules.DefaultValueDiffers.Id, Rules.DefaultValueType.Id);

    public override FixAllProvider GetFixAllProvider()
    {
      return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
      SyntaxNode root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
      foreach (Diagnostic diagnostic in context.Diagnostics)
      {
        SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        PropertyDeclarationSyntax property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property is null)
          continue;
        diagnostic.Properties.TryGetValue(Rules.ArgumentKey, out string argument);
        diagnostic.Properties.TryGetValue(Rules.InitializerKey, out string initializer);

        if (diagnostic.Id == Rules.MissingDefaultValue.Id && argument != null)
        {
          string title = string.Concat("Add [DefaultValue(", argument, ")]");
          context.RegisterCodeFix(CodeAction.Create(title, c => AddAttribute(context.Document, property, argument, c), title), diagnostic);
          continue;
        }

        AttributeSyntax attribute = node.FirstAncestorOrSelf<AttributeSyntax>();
        if (attribute != null && argument != null)
        {
          string title = string.Concat("Use [DefaultValue(", argument, ")]");
          context.RegisterCodeFix(CodeAction.Create(title, c => ReplaceArgument(context.Document, attribute, argument, c), title), diagnostic);
        }
        if (initializer != null)
        {
          string title = string.Concat("Initialize ", property.Identifier.ValueText, " to ", initializer);
          context.RegisterCodeFix(CodeAction.Create(title, c => SetInitializer(context.Document, property, initializer, c), title + " (initializer)"), diagnostic);
        }
      }
    }

    private static async Task<Document> AddAttribute(Document document, PropertyDeclarationSyntax property, string argument, CancellationToken cancellation)
    {
      SyntaxNode root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
      SyntaxTrivia newLine = NewLine(root);
      NameSyntax name = SyntaxFactory.ParseName("System.ComponentModel.DefaultValue").WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation);
      // Tokens without elastic trivia: the formatter of the IDE leaves the rest of the declaration as it is
      AttributeListSyntax list = SyntaxFactory.AttributeList(Token(SyntaxKind.OpenBracketToken), null,
        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Attribute(name, SyntaxFactory.ParseAttributeArgumentList("(" + argument + ")"))), Token(SyntaxKind.CloseBracketToken));

      PropertyDeclarationSyntax updated;
      if (property.AttributeLists.Count == 0)
      {
        // The attribute takes the comments and indentation of the property, the property keeps its indentation on the next line
        SyntaxToken first = property.GetFirstToken();
        SyntaxTriviaList leading = first.LeadingTrivia;
        updated = property.ReplaceToken(first, first.WithLeadingTrivia(Indentation(leading)))
          .WithAttributeLists(SyntaxFactory.SingletonList(list.WithLeadingTrivia(leading).WithTrailingTrivia(newLine)));
      }
      else
      {
        SyntaxToken next = property.AttributeLists.Last().GetLastToken().GetNextToken();
        updated = property.WithAttributeLists(property.AttributeLists.Add(list.WithLeadingTrivia(Indentation(next.LeadingTrivia)).WithTrailingTrivia(newLine)));
      }

      document = document.WithSyntaxRoot(root.ReplaceNode(property, updated));
      document = await ImportAdder.AddImportsAsync(document, Simplifier.AddImportsAnnotation, null, cancellation).ConfigureAwait(false);
      return await Simplifier.ReduceAsync(document, Simplifier.Annotation, null, cancellation).ConfigureAwait(false);
    }

    private static async Task<Document> ReplaceArgument(Document document, AttributeSyntax attribute, string argument, CancellationToken cancellation)
    {
      SyntaxNode root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
      AttributeArgumentListSyntax arguments = SyntaxFactory.ParseAttributeArgumentList("(" + argument + ")");
      if (attribute.ArgumentList != null)
        arguments = arguments.WithTriviaFrom(attribute.ArgumentList);
      return document.WithSyntaxRoot(root.ReplaceNode(attribute, attribute.WithArgumentList(arguments)));
    }

    private static async Task<Document> SetInitializer(Document document, PropertyDeclarationSyntax property, string value, CancellationToken cancellation)
    {
      SyntaxNode root = await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false);
      ExpressionSyntax expression = SyntaxFactory.ParseExpression(value);
      PropertyDeclarationSyntax updated;
      if (property.Initializer != null)
        updated = property.WithInitializer(property.Initializer.WithValue(expression.WithTriviaFrom(property.Initializer.Value)));
      else
      {
        // { get; set; } = value;  (the trailing trivia of the accessors moves behind the semicolon)
        AccessorListSyntax accessors = property.AccessorList;
        updated = property.WithAccessorList(accessors.WithoutTrailingTrivia())
          .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.Token(SyntaxFactory.TriviaList(SyntaxFactory.Space), SyntaxKind.EqualsToken, SyntaxFactory.TriviaList(SyntaxFactory.Space)), expression))
          .WithSemicolonToken(SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.SemicolonToken, accessors.GetTrailingTrivia()));
      }
      return document.WithSyntaxRoot(root.ReplaceNode(property, updated));
    }

    private static SyntaxToken Token(SyntaxKind kind)
    {
      return SyntaxFactory.Token(SyntaxFactory.TriviaList(), kind, SyntaxFactory.TriviaList());
    }

    /// <summary>
    /// The whitespace after the last line break (the indentation of the line).
    /// </summary>
    private static SyntaxTriviaList Indentation(SyntaxTriviaList leading)
    {
      if (leading.Count > 0 && leading[leading.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia))
        return SyntaxFactory.TriviaList(leading[leading.Count - 1]);
      return SyntaxFactory.TriviaList();
    }

    /// <summary>
    /// The line break the file uses.
    /// </summary>
    private static SyntaxTrivia NewLine(SyntaxNode root)
    {
      foreach (SyntaxTrivia trivia in root.DescendantTrivia())
        if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
          return trivia;
      return SyntaxFactory.EndOfLine(Environment.NewLine);
    }
  }
}
