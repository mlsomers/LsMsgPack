using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;

namespace LsMsgPack.Analyzers
{
  /// <summary>
  /// The properties of the serialized types (<see cref="ModelTypes"/>): default values that are lost (LSMP001-003, LSMP006) and places where the data picks the type (LSMP004),
  /// the calls and actions that read into such a place (LSMP005) or into a place without property names (LSMP007), and classes of other projects that are not checked (LSMP008). See docs/Analyzers.md.
  /// <para>Reported from symbol and operation actions (not at the end of the compilation), so the IDE shows them while typing and offers the code fixes.</para>
  /// </summary>
  [DiagnosticAnalyzer(LanguageNames.CSharp)]
  public sealed class ModelAnalyzer : DiagnosticAnalyzer
  {
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
      ImmutableArray.Create(Rules.MissingDefaultValue, Rules.DefaultValueDiffers, Rules.DefaultValueType, Rules.OpenProperty, Rules.OpenRead,
        Rules.NullReadAsInitial, Rules.UntypedRead, Rules.UncheckedModel);

    public override void Initialize(AnalysisContext context)
    {
      context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
      context.EnableConcurrentExecution();
      context.RegisterCompilationStartAction(start =>
      {
        KnownSymbols known = new KnownSymbols(start.Compilation);
        if (known.IsEmpty && !ModelTypes.AllTypes(start.Options))
          return; // neither serializer is referenced, and the project of models does not ask for its classes to be checked
        Models models = new Models(start.Compilation, known, start.Options);
        OtherAssemblies others = new OtherAssemblies(start.Compilation, start.Options);
        start.RegisterSymbolAction(c => AnalyzeProperty(c, known, models, others), SymbolKind.Property);
        start.RegisterSymbolAction(c => AnalyzeBaseType(c, models, others), SymbolKind.NamedType);
        start.RegisterSymbolAction(c => AnalyzeAction(c, known, others), SymbolKind.Method);
        start.RegisterOperationAction(c => AnalyzeCall(c, known, others), OperationKind.Invocation, OperationKind.ObjectCreation);
        if (known.MinimalApiBody != null)
          start.RegisterSyntaxNodeAction(c => AnalyzeMinimalApiType(c, known, others), SyntaxKind.GenericName);
      });
    }

    /// <summary>
    /// <see cref="ModelTypes"/> of the compilation, found when the first property is analyzed (a cancelled search is not kept).
    /// </summary>
    private sealed class Models
    {
      private readonly Compilation _compilation;
      private readonly KnownSymbols _known;
      private readonly AnalyzerOptions _options;
      private readonly object _lock = new object();
      private volatile ModelTypes _types;

      public Models(Compilation compilation, KnownSymbols known, AnalyzerOptions options)
      {
        _compilation = compilation;
        _known = known;
        _options = options;
      }

      public ModelTypes Get(CancellationToken cancellation)
      {
        ModelTypes types = _types;
        if (types != null)
          return types;
        lock (_lock)
        {
          if (_types is null)
            _types = ModelTypes.Find(_compilation, _known, _options, cancellation);
          return _types;
        }
      }
    }

    #region Properties

    private static void AnalyzeProperty(SymbolAnalysisContext context, KnownSymbols known, Models models, OtherAssemblies others)
    {
      IPropertySymbol property = (IPropertySymbol)context.Symbol;
      if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is null
        || property.SetMethod is null || property.SetMethod.DeclaredAccessibility != Accessibility.Public || property.IsImplicitlyDeclared)
        return; // what FilterNonSettable and FilterStatic leave out, and properties without a setter (GetOnlyProperties: written, but never set from the data)
      if (!models.Get(context.CancellationToken).Contains(property.ContainingType) || IsIgnored(property))
        return;

      string name = string.Concat(property.ContainingType.Name, ".", property.Name);
      ITypeSymbol open = OpenPart(property.Type);
      if (open != null)
        context.ReportDiagnostic(Diagnostic.Create(Rules.OpenProperty, property.Locations[0], name, Display(property.Type), Display(open)));
      INamedTypeSymbol other = others.FindUnchecked(property.Type);
      if (other != null)
        context.ReportDiagnostic(Diagnostic.Create(Rules.UncheckedModel, property.Locations[0], Display(other), other.ContainingAssembly.Name));

      CheckDefaults(context, property, name, known);
    }

    /// <summary>
    /// A serialized class deriving from a class of another assembly that no analyzer checked (LSMP008): the properties of the base class are written too.
    /// </summary>
    private static void AnalyzeBaseType(SymbolAnalysisContext context, Models models, OtherAssemblies others)
    {
      INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
      if (type.TypeKind != TypeKind.Class || type.BaseType is null || type.IsImplicitlyDeclared || !models.Get(context.CancellationToken).Contains(type))
        return;
      INamedTypeSymbol other = others.FindUnchecked(type.BaseType);
      if (other != null)
        context.ReportDiagnostic(Diagnostic.Create(Rules.UncheckedModel, type.Locations[0], Display(other), other.ContainingAssembly.Name));
    }

    /// <summary>
    /// The "ignore" attributes FilterIgnoredAttribute recognizes (by full name, as it does).
    /// </summary>
    private static bool IsIgnored(IPropertySymbol property)
    {
      foreach (AttributeData attribute in property.GetAttributes())
      {
        switch (attribute.AttributeClass?.ToDisplayString())
        {
          case "System.Xml.Serialization.XmlIgnoreAttribute":
          case "Newtonsoft.Json.JsonIgnoreAttribute":
          case "System.Runtime.Serialization.IgnoreDataMemberAttribute":
          case "MessagePack.IgnoreMemberAttribute":
            return true;
          case "System.Text.Json.Serialization.JsonIgnoreAttribute":
            if (!HasNamedArgument(attribute, "Condition", out TypedConstant condition) || Equals(condition.Value, 1)) // JsonIgnoreCondition.Always
              return true;
            break;
          case "PolyType.PropertyShapeAttribute":
            if (HasNamedArgument(attribute, "Ignore", out TypedConstant ignore) && Equals(ignore.Value, true))
              return true;
            break;
        }
      }
      return false;
    }

    private static bool HasNamedArgument(AttributeData attribute, string name, out TypedConstant value)
    {
      foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
      {
        if (argument.Key == name)
        {
          value = argument.Value;
          return true;
        }
      }
      value = default(TypedConstant);
      return false;
    }

    #endregion

    #region Defaults

    private static void CheckDefaults(SymbolAnalysisContext context, IPropertySymbol property, string name, KnownSymbols known)
    {
      IMethodSymbol withValues = ValuesConstructor.Of(property.ContainingType);
      if (withValues is null && !IsConstructed(property.ContainingType))
        return; // created without a constructor (ObjectCreation.ConstructorOrUninitialized): a left out property keeps the type's default, as the writer assumes
      if (withValues != null && ValuesConstructor.Takes(withValues, property))
        return; // the constructor gets the value the writer leaves out
      Compilation compilation = context.Compilation;
      ITypeSymbol type = property.Type;
      ITypeSymbol underlying = UnderlyingType(type);
      InitialValue initial = InitialValue.Find(property, compilation, context.CancellationToken, withValues);
      Location location = property.Locations[0];

      AttributeData defaultValue = null;
      foreach (AttributeData attribute in property.GetAttributes())
        if (SerializerUses.Same(attribute.AttributeClass, known.DefaultValueAttribute))
          defaultValue = attribute;

      if (defaultValue is null)
      {
        if (initial.IsConstant && !IsDefault(initial.Value, type) && !(initial.Value is string text && text.Length == 0))
        {
          string argument = Literal(initial.Value, underlying, initial.Expression, initial.Model, location);
          context.ReportDiagnostic(Diagnostic.Create(Rules.MissingDefaultValue, location, Properties(argument, null), name, argument, DefaultText(type)));
        }
        return;
      }

      if (!(defaultValue.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken) is AttributeSyntax attributeSyntax))
        return;
      Location at = attributeSyntax.GetLocation();
      ImmutableArray<TypedConstant> arguments = defaultValue.ConstructorArguments;
      if (arguments.Length == 2) // (Type, string): converted by the TypeConverter of the type when the attribute is read
      {
        ITypeSymbol of = arguments[0].Value as ITypeSymbol;
        if (of != null && !SameType(of, underlying) && type.SpecialType != SpecialType.System_Object)
        {
          context.ReportDiagnostic(Diagnostic.Create(Rules.DefaultValueType, at, NoFix, name, attributeSyntax.ArgumentList?.Arguments.ToString(), Display(type)));
          return;
        }
        if (underlying.SpecialType == SpecialType.System_Decimal && arguments[1].Value is string decimalText && initial.IsConstant
          && decimal.TryParse(decimalText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
        {
          if (!Equals(parsed, initial.Value))
          {
            string argument = Literal(initial.Value, underlying, initial.Expression, initial.Model, location);
            context.ReportDiagnostic(Diagnostic.Create(Rules.DefaultValueDiffers, at, Properties(argument, null), name, attributeSyntax.ArgumentList?.Arguments.ToString(), argument));
          }
          else if (IsNullable(type))
            ReportNullReadAsInitial(context, name, initial, underlying, location);
        }
        else if (!initial.Found && arguments[1].Value is string text && text.Length > 0)
          context.ReportDiagnostic(Diagnostic.Create(Rules.DefaultValueDiffers, at, NoFix, name, attributeSyntax.ArgumentList?.Arguments.ToString(), DefaultText(type))); // nothing sets it
        return;
      }
      if (arguments.Length != 1 || arguments[0].Kind == TypedConstantKind.Array || arguments[0].IsNull || type.SpecialType == SpecialType.System_Object)
        return; // [DefaultValue(null)] is never left out (FilterDefaultValues ignores it)

      TypedConstant value = arguments[0];
      string written = attributeSyntax.ArgumentList.Arguments[0].ToString();
      if (!SameType(value.Type, underlying))
      {
        string converted = Convert(value.Value, underlying, out object convertedValue) ? Literal(convertedValue, underlying, null, null, location) : null;
        context.ReportDiagnostic(Diagnostic.Create(Rules.DefaultValueType, at, Properties(converted, null), name, string.Concat(written, " (", Display(value.Type), ")"), Display(type)));
        return;
      }

      if (initial.IsConstant)
      {
        if (!Equals(initial.Value, value.Value))
        {
          string argument = Literal(initial.Value, underlying, initial.Expression, initial.Model, location);
          string initializer = initial.CanReplace ? written : null;
          context.ReportDiagnostic(Diagnostic.Create(Rules.DefaultValueDiffers, at, Properties(argument, initializer), name, written, argument));
        }
        else if (IsNullable(type))
          ReportNullReadAsInitial(context, name, initial, underlying, location);
      }
      else if (!initial.Found && !IsDefault(value.Value, type))
      {
        // Nothing sets it: a reader gives the left out value the type's default
        string initializer = initial.CanAdd ? written : null;
        context.ReportDiagnostic(Diagnostic.Create(Rules.DefaultValueDiffers, at, Properties(null, initializer), name, written, DefaultText(type)));
      }
    }

    /// <summary>
    /// LSMP006: [DefaultValue] equals the initial value, so the writer keeps null, but as nil in the array layout, which a reader takes as left out (ExplicitNullKeepsTheConstructorValue).
    /// </summary>
    private static void ReportNullReadAsInitial(SymbolAnalysisContext context, string name, InitialValue initial, ITypeSymbol underlying, Location location)
    {
      string value = Literal(initial.Value, underlying, initial.Expression, initial.Model, location);
      context.ReportDiagnostic(Diagnostic.Create(Rules.NullReadAsInitial, location, name, value));
    }

    /// <summary>
    /// The property can hold null: a reference type or a nullable value type.
    /// </summary>
    private static bool IsNullable(ITypeSymbol type)
    {
      return !type.IsValueType || !SameType(UnderlyingType(type), type);
    }

    private static readonly ImmutableDictionary<string, string> NoFix = ImmutableDictionary<string, string>.Empty;

    private static ImmutableDictionary<string, string> Properties(string argument, string initializer)
    {
      ImmutableDictionary<string, string> properties = NoFix;
      if (argument != null)
        properties = properties.Add(Rules.ArgumentKey, argument);
      if (initializer != null)
        properties = properties.Add(Rules.InitializerKey, initializer);
      return properties;
    }

    /// <summary>
    /// A reader creates the objects with a constructor (and so runs the initializers): a class with a parameterless constructor (of any accessibility, also the implicit one), a struct with an explicit one.
    /// </summary>
    private static bool IsConstructed(INamedTypeSymbol type)
    {
      if (type.IsAbstract)
        return true; // the classes deriving from it run its initializers
      foreach (IMethodSymbol constructor in type.InstanceConstructors)
        if (constructor.Parameters.Length == 0 && (type.TypeKind == TypeKind.Class || !constructor.IsImplicitlyDeclared))
          return true;
      return false;
    }

    private static ITypeSymbol UnderlyingType(ITypeSymbol type)
    {
      if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        return named.TypeArguments[0];
      return type;
    }

    private static bool SameType(ITypeSymbol a, ITypeSymbol b)
    {
      return SymbolEqualityComparer.Default.Equals(a, b);
    }

    /// <summary>
    /// What FilterDefaultValues leaves out without [DefaultValue]: null (references and nullables), zero, false, '\0'.
    /// </summary>
    private static bool IsDefault(object value, ITypeSymbol type)
    {
      if (value is null)
        return true;
      if (!type.IsValueType || !SameType(UnderlyingType(type), type))
        return false; // a nullable is only left out when it is null
      switch (value)
      {
        case bool b: return !b;
        case char c: return c == '\0';
        case string _: return false;
        case float f: return f == 0;
        case double d: return d == 0;
        case decimal m: return m == 0;
        case IConvertible number: return System.Convert.ToDecimal(number, CultureInfo.InvariantCulture) == 0;
        default: return false;
      }
    }

    private static string DefaultText(ITypeSymbol type)
    {
      if (!type.IsValueType || !SameType(UnderlyingType(type), type))
        return "null";
      switch (type.SpecialType)
      {
        case SpecialType.System_Boolean: return "false";
        case SpecialType.System_Char: return "'\\0'";
        default: return "0";
      }
    }

    /// <summary>
    /// The value as a constant of the type (for the code fix of LSMP003: [DefaultValue(1)] on a long becomes [DefaultValue(1L)]).
    /// </summary>
    private static bool Convert(object value, ITypeSymbol type, out object converted)
    {
      converted = null;
      Type target;
      switch (type.TypeKind == TypeKind.Enum ? ((INamedTypeSymbol)type).EnumUnderlyingType.SpecialType : type.SpecialType)
      {
        case SpecialType.System_SByte: target = typeof(sbyte); break;
        case SpecialType.System_Byte: target = typeof(byte); break;
        case SpecialType.System_Int16: target = typeof(short); break;
        case SpecialType.System_UInt16: target = typeof(ushort); break;
        case SpecialType.System_Int32: target = typeof(int); break;
        case SpecialType.System_UInt32: target = typeof(uint); break;
        case SpecialType.System_Int64: target = typeof(long); break;
        case SpecialType.System_UInt64: target = typeof(ulong); break;
        case SpecialType.System_Single: target = typeof(float); break;
        case SpecialType.System_Double: target = typeof(double); break;
        case SpecialType.System_Decimal: target = typeof(decimal); break;
        default: return false;
      }
      if (!(value is IConvertible) || value is string || value is bool || value is char)
        return false;
      try
      {
        converted = System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        return Equals(System.Convert.ChangeType(converted, value.GetType(), CultureInfo.InvariantCulture), value); // no precision lost
      }
      catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException || ex is FormatException)
      {
        return false;
      }
    }

    /// <summary>
    /// The argument of [DefaultValue] for the value: the initializer as it is written when it has the property's type, otherwise a literal of that type
    /// (decimal as <c>typeof(decimal), "1.5"</c>: an attribute cannot take a decimal).
    /// </summary>
    private static string Literal(object value, ITypeSymbol type, ExpressionSyntax written, SemanticModel model, Location location)
    {
      if (written != null && model != null && type.SpecialType != SpecialType.System_Decimal && SameType(model.GetTypeInfo(written).Type, type))
        return written.ToString();

      if (type.TypeKind == TypeKind.Enum)
      {
        foreach (ISymbol member in type.GetMembers())
          if (member is IFieldSymbol field && field.HasConstantValue && Equals(field.ConstantValue, value))
            return string.Concat(Display(type), ".", field.Name);
        return string.Concat("(", Display(type), ")", System.Convert.ToString(value, CultureInfo.InvariantCulture));
      }

      switch (value)
      {
        case string s: return SyntaxFactory.Literal(s).Text;
        case char c: return SyntaxFactory.Literal(c).Text;
        case bool b: return b ? "true" : "false";
        case int i: return SyntaxFactory.Literal(i).Text;
        case long l: return SyntaxFactory.Literal(l).Text;
        case uint u: return SyntaxFactory.Literal(u).Text;
        case ulong ul: return SyntaxFactory.Literal(ul).Text;
        case float f: return SyntaxFactory.Literal(f).Text;
        case double d: return SyntaxFactory.Literal(d).Text;
        case decimal m: return string.Concat("typeof(decimal), \"", m.ToString(CultureInfo.InvariantCulture), "\"");
        case byte _:
        case sbyte _:
        case short _:
        case ushort _:
          return string.Concat("(", Display(type), ")", System.Convert.ToString(value, CultureInfo.InvariantCulture));
        default: return System.Convert.ToString(value, CultureInfo.InvariantCulture);
      }
    }

    #endregion

    #region Reading into any type

    /// <summary>
    /// <c>Deserialize&lt;object&gt;</c>, <c>ReadAsAsync&lt;List&lt;object&gt;&gt;</c>, <c>Ok(value)</c>, <c>new ObjectContent&lt;T&gt;</c>...
    /// </summary>
    private static void AnalyzeCall(OperationAnalysisContext context, KnownSymbols known, OtherAssemblies others)
    {
      List<SerializedUse> uses = new List<SerializedUse>(1);
      SerializerUses.FromOperation(context.Operation, known, uses);
      Report(context.ReportDiagnostic, uses, others);
    }

    /// <summary>
    /// <c>MsgPackBody&lt;object&gt;</c>, <c>MsgPackResult&lt;T&gt;</c> where a minimal API names them.
    /// </summary>
    private static void AnalyzeMinimalApiType(SyntaxNodeAnalysisContext context, KnownSymbols known, OtherAssemblies others)
    {
      GenericNameSyntax name = (GenericNameSyntax)context.Node;
      if (!SerializerUses.IsMinimalApiTypeName(name))
        return;
      List<SerializedUse> uses = new List<SerializedUse>(1);
      SerializerUses.FromTypeName(name, context.SemanticModel, known, uses, context.CancellationToken);
      Report(context.ReportDiagnostic, uses, others);
    }

    /// <summary>
    /// The result and parameters of a controller action, with a reference to the web formatter of its framework.
    /// </summary>
    private static void AnalyzeAction(SymbolAnalysisContext context, KnownSymbols known, OtherAssemblies others)
    {
      IMethodSymbol method = (IMethodSymbol)context.Symbol;
      if (!SerializerUses.IsController(method.ContainingType, known))
        return;
      List<SerializedUse> uses = new List<SerializedUse>();
      SerializerUses.FromAction(method, known, uses);
      Report(context.ReportDiagnostic, uses, others);
    }

    private static void Report(Action<Diagnostic> report, List<SerializedUse> uses, OtherAssemblies others)
    {
      for (int t = 0; t < uses.Count; t++)
      {
        SerializedUse use = uses[t];
        if (use.Location is null || !use.Location.IsInSource)
          continue;
        INamedTypeSymbol other = others.FindUnchecked(use.Type);
        if (other != null)
          report(Diagnostic.Create(Rules.UncheckedModel, use.Location, Display(other), other.ContainingAssembly.Name));
        if (!use.Reads)
          continue;
        ITypeSymbol open = OpenPart(use.Type);
        if (open != null)
          report(Diagnostic.Create(Rules.OpenRead, use.Location, use.What, Display(use.Type), Display(open)));
        string untyped = WithoutNames(use.Type);
        if (untyped != null)
          report(Diagnostic.Create(Rules.UntypedRead, use.Location, use.What, Display(use.Type), untyped));
      }
    }

    /// <summary>
    /// What a read into an untyped place makes of an object written as an array of its values (LSMP007): object, or a dictionary with string keys and values of any type. Null for other types.
    /// </summary>
    private static string WithoutNames(ITypeSymbol type)
    {
      if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic)
        return "as an object[] of its values";
      if (!(type is INamedTypeSymbol named))
        return null;
      if (IsUntypedDictionary(named))
        return "and a dictionary cannot be filled from it (MsgPackException)";
      foreach (INamedTypeSymbol i in named.AllInterfaces)
        if (IsUntypedDictionary(i))
          return "and a dictionary cannot be filled from it (MsgPackException)";
      return null;
    }

    private static bool IsUntypedDictionary(INamedTypeSymbol type)
    {
      if (type.TypeArguments.Length != 2 || type.TypeArguments[0].SpecialType != SpecialType.System_String)
        return false;
      ITypeSymbol value = type.TypeArguments[1];
      if (value.SpecialType != SpecialType.System_Object && value.TypeKind != TypeKind.Dynamic)
        return false;
      string name = type.OriginalDefinition.ToDisplayString();
      return name == "System.Collections.Generic.IDictionary<TKey, TValue>" || name == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
    }

    /// <summary>
    /// The part of a declared type where the data picks the type a reader creates: object (or dynamic), an interface or abstract class of the framework (IDisposable, Stream...),
    /// a collection of the framework without an element type (ArrayList, Hashtable), also as the element or generic argument of the type. Null when there is none.
    /// <para>The framework's interfaces and abstract classes are implemented by types that run code when they are created or filled, the project's own ones only by the project's (and its dependencies') classes.</para>
    /// </summary>
    internal static ITypeSymbol OpenPart(ITypeSymbol type)
    {
      return OpenPart(type, 0);
    }

    private static ITypeSymbol OpenPart(ITypeSymbol type, int depth)
    {
      if (type is null || depth > 16)
        return null;
      if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic)
        return type;
      if (type is IArrayTypeSymbol array)
        return OpenPart(array.ElementType, depth + 1);
      if (!(type is INamedTypeSymbol named) || named.TypeKind == TypeKind.Error || named.TypeKind == TypeKind.TypeParameter)
        return null;

      if (named.IsGenericType)
      {
        foreach (ITypeSymbol argument in named.TypeArguments)
        {
          ITypeSymbol open = OpenPart(argument, depth + 1);
          if (open != null)
            return open;
        }
        return null;
      }

      if (ModelTypes.IsInSource(named) || !IsFramework(named))
        return null;
      if (named.TypeKind == TypeKind.Interface || (named.TypeKind == TypeKind.Class && named.IsAbstract))
        return IsRefusedAnyway(named) ? null : named;
      return IsUntypedCollection(named) ? named : null;
    }

    internal static bool IsFramework(INamedTypeSymbol type)
    {
      INamespaceSymbol ns = type.ContainingNamespace;
      while (ns != null && !ns.IsGlobalNamespace && !ns.ContainingNamespace.IsGlobalNamespace)
        ns = ns.ContainingNamespace;
      return ns != null && (ns.Name == "System" || ns.Name == "Microsoft");
    }

    /// <summary>
    /// The serializers refuse Type and MemberInfo whatever the data says.
    /// </summary>
    private static bool IsRefusedAnyway(INamedTypeSymbol type)
    {
      string name = type.ToDisplayString();
      return name == "System.Type" || name.StartsWith("System.Reflection.", StringComparison.Ordinal);
    }

    /// <summary>
    /// A collection without an element type (its elements are object), e.g. ArrayList, Hashtable, Queue.
    /// </summary>
    private static bool IsUntypedCollection(INamedTypeSymbol type)
    {
      if (type.SpecialType == SpecialType.System_String)
        return false;
      bool enumerable = false;
      foreach (INamedTypeSymbol i in type.AllInterfaces)
      {
        if (i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
          return false;
        enumerable |= i.SpecialType == SpecialType.System_Collections_IEnumerable;
      }
      switch (type.ToDisplayString())
      {
        case "System.Collections.BitArray":
        case "System.Collections.Specialized.StringCollection":
        case "System.Collections.Specialized.StringDictionary":
        case "System.Collections.Specialized.NameValueCollection":
          return false; // their elements have a type
      }
      return enumerable;
    }

    private static string Display(ITypeSymbol type)
    {
      return type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
    }

    #endregion
  }
}
