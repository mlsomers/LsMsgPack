using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Threading;

namespace LsMsgPack.Analyzers
{
  /// <summary>
  /// What the constructor sets a property to: its initializer, the initializer of the field its getter returns, or the last assignment of either in the body of the parameterless constructor
  /// (a reader gives a left out property this value).
  /// </summary>
  internal readonly struct InitialValue
  {
    /// <summary>
    /// Something sets it (a constant or not).
    /// </summary>
    public readonly bool Found;

    /// <summary>
    /// A constant, converted to the type of the property (<see cref="Value"/>).
    /// </summary>
    public readonly bool IsConstant;

    public readonly object Value;

    /// <summary>
    /// The expression as written (the argument of [DefaultValue] when it has the property's type).
    /// </summary>
    public readonly ExpressionSyntax Expression;

    public readonly SemanticModel Model;

    /// <summary>
    /// It is the initializer of the auto-property, so the code fix can replace it.
    /// </summary>
    public readonly bool CanReplace;

    /// <summary>
    /// Nothing sets it and it is an auto-property, so the code fix can add an initializer.
    /// </summary>
    public readonly bool CanAdd;

    private InitialValue(bool found, bool isConstant, object value, ExpressionSyntax expression, SemanticModel model, bool canReplace, bool canAdd)
    {
      Found = found;
      IsConstant = isConstant;
      Value = value;
      Expression = expression;
      Model = model;
      CanReplace = canReplace;
      CanAdd = canAdd;
    }

    public static InitialValue Find(IPropertySymbol property, Compilation compilation, CancellationToken cancellation)
    {
      if (property.DeclaringSyntaxReferences.Length != 1 || !(property.DeclaringSyntaxReferences[0].GetSyntax(cancellation) is PropertyDeclarationSyntax declaration))
        return default(InitialValue);
      SemanticModel model = compilation.GetSemanticModel(declaration.SyntaxTree);
      bool isAuto = IsAutoProperty(declaration);

      ExpressionSyntax value = declaration.Initializer?.Value;
      bool fromInitializer = value != null;
      IFieldSymbol field = isAuto ? null : BackingField(declaration, model, property, cancellation);
      if (value is null && field != null)
        value = FieldInitializer(field, cancellation);

      // The body of the parameterless constructor runs after the initializers: its last assignment wins
      ExpressionSyntax assigned = ConstructorAssignment(property, field, model, cancellation);
      if (assigned != null)
      {
        value = assigned;
        fromInitializer = false;
      }

      if (value is null)
        return new InitialValue(false, false, null, null, model, false, isAuto);

      SemanticModel valueModel = value.SyntaxTree == declaration.SyntaxTree ? model : compilation.GetSemanticModel(value.SyntaxTree);
      Optional<object> constant = valueModel.GetConstantValue(value, cancellation);
      if (!constant.HasValue)
        return new InitialValue(true, false, null, value, valueModel, false, false);
      object converted = ConvertedConstant(value, valueModel, cancellation) ?? constant.Value;
      return new InitialValue(true, true, converted, value, valueModel, isAuto && fromInitializer, false);
    }

    /// <summary>
    /// The constant after its implicit conversion to the type it is assigned to (the 3 of <c>long X = 3</c> is a long).
    /// </summary>
    private static object ConvertedConstant(ExpressionSyntax value, SemanticModel model, CancellationToken cancellation)
    {
      IOperation operation = model.GetOperation(value, cancellation);
      IOperation parent = operation?.Parent;
      if (parent is IConversionOperation conversion && conversion.IsImplicit && conversion.ConstantValue.HasValue)
        return conversion.ConstantValue.Value;
      return null;
    }

    private static bool IsAutoProperty(PropertyDeclarationSyntax declaration)
    {
      if (declaration.AccessorList is null)
        return false; // expression bodied: no setter
      foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
        if (accessor.Body != null || accessor.ExpressionBody != null)
          return false;
      return true;
    }

    /// <summary>
    /// The field of this type that the getter returns (<c>get =&gt; _x;</c>, <c>get { return _x; }</c>, <c>=&gt; _x</c>).
    /// </summary>
    private static IFieldSymbol BackingField(PropertyDeclarationSyntax declaration, SemanticModel model, IPropertySymbol property, CancellationToken cancellation)
    {
      ExpressionSyntax returned = declaration.ExpressionBody?.Expression;
      if (returned is null && declaration.AccessorList != null)
      {
        foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
        {
          if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
            continue;
          returned = accessor.ExpressionBody?.Expression;
          if (returned is null && accessor.Body != null && accessor.Body.Statements.Count == 1 && accessor.Body.Statements[0] is ReturnStatementSyntax ret)
            returned = ret.Expression;
        }
      }
      if (returned is null)
        return null;
      IFieldSymbol field = model.GetSymbolInfo(returned, cancellation).Symbol as IFieldSymbol;
      return field != null && !field.IsStatic && SymbolEqualityComparer.Default.Equals(field.ContainingType, property.ContainingType) ? field : null;
    }

    private static ExpressionSyntax FieldInitializer(IFieldSymbol field, CancellationToken cancellation)
    {
      if (field.DeclaringSyntaxReferences.Length != 1)
        return null;
      return (field.DeclaringSyntaxReferences[0].GetSyntax(cancellation) as VariableDeclaratorSyntax)?.Initializer?.Value;
    }

    /// <summary>
    /// The last statement of the parameterless constructor's body that assigns the property or its field (<c>X = 3;</c>, <c>this.X = 3;</c>, <c>_x = 3;</c>), null when there is none.
    /// </summary>
    private static ExpressionSyntax ConstructorAssignment(IPropertySymbol property, IFieldSymbol field, SemanticModel model, CancellationToken cancellation)
    {
      ExpressionSyntax found = null;
      foreach (IMethodSymbol constructor in property.ContainingType.InstanceConstructors)
      {
        if (constructor.Parameters.Length != 0 || constructor.DeclaringSyntaxReferences.Length != 1)
          continue;
        if (!(constructor.DeclaringSyntaxReferences[0].GetSyntax(cancellation) is ConstructorDeclarationSyntax syntax) || syntax.Body is null)
          continue;
        SemanticModel ctorModel = syntax.SyntaxTree == model.SyntaxTree ? model : model.Compilation.GetSemanticModel(syntax.SyntaxTree);
        foreach (StatementSyntax statement in syntax.Body.Statements)
        {
          if (!(statement is ExpressionStatementSyntax expression) || !(expression.Expression is AssignmentExpressionSyntax assignment) || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            continue;
          ISymbol target = ctorModel.GetSymbolInfo(assignment.Left, cancellation).Symbol;
          if (SymbolEqualityComparer.Default.Equals(target, property) || (field != null && SymbolEqualityComparer.Default.Equals(target, field)))
            found = assignment.Right;
        }
      }
      return found;
    }
  }
}
