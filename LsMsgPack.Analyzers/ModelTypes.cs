using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace LsMsgPack.Analyzers
{
  /// <summary>
  /// The types a project serializes: those reachable from what it hands to the serializers (the roots, see <see cref="SerializerUses"/>), through the types of their properties,
  /// the elements of collections, generic arguments, base classes and the classes of the project that derive from them (a type id can name those).
  /// <para>Made once per compilation, when the first property is analyzed. The rules only look at these types, so classes that are never serialized (e.g. options classes) are left alone.
  /// With the MSBuild property <c>LsMsgPackAnalyze</c> = <c>AllTypes</c> every public class and struct of the project is one (for a project of models that does not call the serializers itself).</para>
  /// </summary>
  internal sealed class ModelTypes
  {
    private readonly HashSet<ITypeSymbol> _reached = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> _definitions = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
    private readonly List<INamedTypeSymbol> _sourceTypes = new List<INamedTypeSymbol>();
    private readonly Queue<ITypeSymbol> _pending = new Queue<ITypeSymbol>();

    /// <summary>
    /// A serialized type of this project (the definition of a generic type counts for all its constructed types).
    /// </summary>
    public bool Contains(INamedTypeSymbol type)
    {
      return _definitions.Contains(type.OriginalDefinition);
    }

    public static ModelTypes Find(Compilation compilation, KnownSymbols known, AnalyzerOptions options, CancellationToken cancellation)
    {
      ModelTypes types = new ModelTypes();
      types.CollectSourceTypes(compilation.Assembly.GlobalNamespace, cancellation);

      if (AllTypes(options))
        for (int t = 0; t < types._sourceTypes.Count; t++)
        {
          INamedTypeSymbol type = types._sourceTypes[t];
          if (type.DeclaredAccessibility == Accessibility.Public && (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct) && !type.IsStatic)
            types.Add(type);
        }

      List<SerializedUse> uses = types.CollectUses(compilation, known, cancellation);
      for (int t = 0; t < uses.Count; t++)
        if (!(uses[t].Type is ITypeParameterSymbol))
          types.Add(uses[t].Type);
      types.Walk(cancellation);
      return types;
    }

    /// <summary>
    /// The MSBuild property <c>LsMsgPackAnalyze</c> = <c>AllTypes</c>: every public class and struct of the project is checked, also when it references only LsMsgPack.Core.
    /// </summary>
    public static bool AllTypes(AnalyzerOptions options)
    {
      return options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.LsMsgPackAnalyze", out string analyze)
        && string.Equals(analyze?.Trim(), "AllTypes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The roots: what the project hands to the serializers and the formatters (also through its generic wrappers of them). Also used by the TypeScript generator (lsmsgpack-ts --solution).
    /// </summary>
    public static List<SerializedUse> FindUses(Compilation compilation, KnownSymbols known, CancellationToken cancellation)
    {
      ModelTypes types = new ModelTypes();
      types.CollectSourceTypes(compilation.Assembly.GlobalNamespace, cancellation);
      return types.CollectUses(compilation, known, cancellation);
    }

    private List<SerializedUse> CollectUses(Compilation compilation, KnownSymbols known, CancellationToken cancellation)
    {
      List<SerializedUse> uses = new List<SerializedUse>();
      for (int t = 0; t < _sourceTypes.Count; t++)
      {
        SerializerUses.FromAttributes(_sourceTypes[t], known, uses);
        SerializerUses.FromController(_sourceTypes[t], known, uses);
      }

      Scan(compilation, node => SerializerUses.IsCandidate(node), operation => SerializerUses.FromOperation(operation, known, uses), cancellation);
      Wrappers wrappers = new Wrappers();
      for (int scanned = 0; wrappers.Learn(uses, ref scanned);) // until no new wrapper is found
        Scan(compilation, wrappers.IsCandidate, operation => wrappers.FromOperation(operation, uses), cancellation);
      return uses;
    }

    /// <summary>
    /// The operations of the nodes that may be uses (the trees are only bound where a node is a candidate).
    /// </summary>
    private static void Scan(Compilation compilation, Func<SyntaxNode, bool> isCandidate, Action<IOperation> found, CancellationToken cancellation)
    {
      foreach (SyntaxTree tree in compilation.SyntaxTrees)
      {
        cancellation.ThrowIfCancellationRequested();
        SemanticModel model = null;
        foreach (SyntaxNode node in tree.GetRoot(cancellation).DescendantNodes())
        {
          if (!isCandidate(node))
            continue;
          if (model is null)
            model = compilation.GetSemanticModel(tree);
          IOperation operation = model.GetOperation(node, cancellation);
          if (operation != null)
            found(operation);
        }
      }
    }

    /// <summary>
    /// Generic methods of the project that hand their type parameter to a serializer (<c>Save&lt;T&gt;(T item) =&gt; MsgPackSerializer.Serialize(item)</c>), and the interface methods they implement:
    /// the type arguments of their callers are serialized too.
    /// </summary>
    private sealed class Wrappers
    {
      private readonly Dictionary<IMethodSymbol, HashSet<int>> _methods = new Dictionary<IMethodSymbol, HashSet<int>>(SymbolEqualityComparer.Default);
      private readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);

      /// <summary>
      /// Adds the wrappers the uses from <paramref name="scanned"/> on reveal.
      /// </summary>
      /// <returns>A new wrapper was found (its callers are to be scanned)</returns>
      public bool Learn(List<SerializedUse> uses, ref int scanned)
      {
        bool added = false;
        for (; scanned < uses.Count; scanned++)
        {
          if (!(uses[scanned].Type is ITypeParameterSymbol parameter) || parameter.TypeParameterKind != TypeParameterKind.Method)
            continue;
          IMethodSymbol method = parameter.DeclaringMethod?.OriginalDefinition;
          if (method is null || !ModelTypes.IsInSource(method))
            continue;
          added |= Add(method, parameter.Ordinal);
          foreach (INamedTypeSymbol i in method.ContainingType.AllInterfaces)
            foreach (ISymbol member in i.GetMembers(method.Name))
              if (member is IMethodSymbol declared && SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(declared), method))
                added |= Add(declared.OriginalDefinition, parameter.Ordinal);
        }
        return added;
      }

      private bool Add(IMethodSymbol method, int ordinal)
      {
        if (!_methods.TryGetValue(method, out HashSet<int> ordinals))
          _methods.Add(method, ordinals = new HashSet<int>());
        _names.Add(method.Name);
        return ordinals.Add(ordinal);
      }

      public bool IsCandidate(SyntaxNode node)
      {
        if (!(node is InvocationExpressionSyntax invocation))
          return false;
        ExpressionSyntax expression = invocation.Expression;
        SimpleNameSyntax name = expression is MemberAccessExpressionSyntax member ? member.Name : expression as SimpleNameSyntax;
        return name != null && _names.Contains(name.Identifier.ValueText);
      }

      public void FromOperation(IOperation operation, List<SerializedUse> uses)
      {
        if (!(operation is IInvocationOperation invocation) || !_methods.TryGetValue(invocation.TargetMethod.OriginalDefinition, out HashSet<int> ordinals))
          return;
        foreach (int ordinal in ordinals)
          if (ordinal < invocation.TargetMethod.TypeArguments.Length)
            uses.Add(new SerializedUse(invocation.TargetMethod.TypeArguments[ordinal], false, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name));
      }
    }

    private void CollectSourceTypes(INamespaceOrTypeSymbol container, CancellationToken cancellation)
    {
      foreach (ISymbol member in container.GetMembers())
      {
        cancellation.ThrowIfCancellationRequested();
        if (member is INamespaceSymbol ns)
          CollectSourceTypes(ns, cancellation);
        else if (member is INamedTypeSymbol type && !type.IsImplicitlyDeclared && type.Locations.Length > 0 && type.Locations[0].IsInSource)
        {
          _sourceTypes.Add(type);
          CollectSourceTypes(type, cancellation);
        }
      }
    }

    private void Add(ITypeSymbol type)
    {
      if (type != null && _reached.Add(type))
        _pending.Enqueue(type);
    }

    private void Walk(CancellationToken cancellation)
    {
      while (_pending.Count > 0)
      {
        cancellation.ThrowIfCancellationRequested();
        ITypeSymbol type = _pending.Dequeue();
        if (type is IArrayTypeSymbol array)
        {
          Add(array.ElementType);
          continue;
        }
        if (!(type is INamedTypeSymbol named) || named.TypeKind == TypeKind.Error)
          continue;

        for (int t = 0; t < named.TypeArguments.Length; t++)
          Add(named.TypeArguments[t]);
        if (!IsInSource(named))
          continue; // the framework's and other assemblies' types: only what they hold (List<Invoice>, Task<Invoice>)

        _definitions.Add(named.OriginalDefinition);
        Add(named.BaseType);
        foreach (ISymbol member in named.GetMembers())
        {
          if (member is IPropertySymbol property && !property.IsStatic && !property.IsIndexer && property.DeclaredAccessibility == Accessibility.Public && property.GetMethod != null)
            Add(property.Type);
        }

        // A value of a derived class has a type id where the base class (or interface) is declared
        for (int t = 0; t < _sourceTypes.Count; t++)
          if (Derives(_sourceTypes[t], named.OriginalDefinition))
            Add(_sourceTypes[t]);
      }
    }

    private static bool Derives(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
      if (SymbolEqualityComparer.Default.Equals(type, baseType))
        return false;
      for (INamedTypeSymbol b = type.BaseType; b != null; b = b.BaseType)
        if (SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, baseType))
          return true;
      if (baseType.TypeKind == TypeKind.Interface)
        foreach (INamedTypeSymbol i in type.AllInterfaces)
          if (SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, baseType))
            return true;
      return false;
    }

    internal static bool IsInSource(ISymbol symbol)
    {
      return symbol.Locations.Length > 0 && symbol.Locations[0].IsInSource;
    }
  }

  /// <summary>
  /// A type the project writes or reads: a type argument or <c>typeof</c> of a call of the serializers, a type an HttpClient formatter call reads, the result and parameters of a controller action...
  /// </summary>
  internal readonly struct SerializedUse
  {
    public readonly ITypeSymbol Type;

    /// <summary>
    /// Read from data (Deserialize, the parameters of an action): where the data picks types (LSMP005).
    /// </summary>
    public readonly bool Reads;

    /// <summary>
    /// Where the type is named (the type argument, typeof, the parameter), for the diagnostic.
    /// </summary>
    public readonly Location Location;

    /// <summary>
    /// What reads it, for the message (e.g. "MsgPackSerializer.Deserialize").
    /// </summary>
    public readonly string What;

    public SerializedUse(ITypeSymbol type, bool reads, Location location, string what)
    {
      Type = type;
      Reads = reads;
      Location = location;
      What = what;
    }
  }

  /// <summary>
  /// The types of this compilation that the analyzers recognize (null when the compilation does not reference them).
  /// </summary>
  internal sealed class KnownSymbols
  {
    public readonly INamedTypeSymbol LsSerializer;
    public readonly INamedTypeSymbol LtSerializer;
    public readonly INamedTypeSymbol HttpSerializer;
    public readonly INamedTypeSymbol SerializableAttribute;
    public readonly INamedTypeSymbol DefaultValueAttribute;
    public readonly INamedTypeSymbol SystemType;

    // The web formatters: only with a reference to them (a controller does not read msgpack otherwise)
    public readonly INamedTypeSymbol AspNetCoreController;
    public readonly INamedTypeSymbol AspNetCoreObjectResult;
    public readonly INamedTypeSymbol WebApiController;
    public readonly INamedTypeSymbol MvcController;
    public readonly INamedTypeSymbol MvcResult;
    public readonly INamedTypeSymbol HttpContentExtensions;
    public readonly INamedTypeSymbol HttpClientExtensions;
    public readonly INamedTypeSymbol ObjectContent;

    public KnownSymbols(Compilation compilation)
    {
      LsSerializer = compilation.GetTypeByMetadataName("LsMsgPack.MsgPackSerializer");
      LtSerializer = compilation.GetTypeByMetadataName("LtMsgPack.LtMsgPackSerializer");
      HttpSerializer = compilation.GetTypeByMetadataName("LtMsgPack.Http.LtMsgPackHttpSerializer");
      SerializableAttribute = compilation.GetTypeByMetadataName("LtMsgPack.LtMsgPackSerializableAttribute");
      DefaultValueAttribute = compilation.GetTypeByMetadataName("System.ComponentModel.DefaultValueAttribute");
      SystemType = compilation.GetTypeByMetadataName("System.Type");

      bool aspNetCore = false, webApi = false, mvc = false;
      foreach (AssemblyIdentity reference in compilation.ReferencedAssemblyNames)
      {
        aspNetCore |= reference.Name == "LsMsgPackFormatters";
        webApi |= reference.Name == "LsMsgPackWebApiFormatters";
        mvc |= reference.Name == "LsMsgPackMvc";
      }
      if (aspNetCore)
      {
        AspNetCoreController = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase");
        AspNetCoreObjectResult = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ObjectResult");
      }
      if (webApi)
      {
        WebApiController = compilation.GetTypeByMetadataName("System.Web.Http.ApiController");
        HttpContentExtensions = compilation.GetTypeByMetadataName("System.Net.Http.HttpContentExtensions");
        HttpClientExtensions = compilation.GetTypeByMetadataName("System.Net.Http.HttpClientExtensions");
        ObjectContent = compilation.GetTypeByMetadataName("System.Net.Http.ObjectContent`1");
      }
      if (mvc)
      {
        MvcController = compilation.GetTypeByMetadataName("System.Web.Mvc.Controller");
        MvcResult = compilation.GetTypeByMetadataName("LsMsgPackMvc.LsMsgPackResult");
      }
    }

    /// <summary>
    /// Nothing to analyze: the compilation references neither serializer.
    /// </summary>
    public bool IsEmpty
    {
      get { return LsSerializer is null && LtSerializer is null && HttpSerializer is null; }
    }
  }

  /// <summary>
  /// Recognizes where a project hands types to the serializers.
  /// </summary>
  internal static class SerializerUses
  {
    /// <summary>
    /// A syntax node that may be a use (checked without the semantic model, so a tree is only bound where it may matter).
    /// </summary>
    public static bool IsCandidate(SyntaxNode node)
    {
      if (node is InvocationExpressionSyntax invocation)
      {
        string name = MethodName(invocation.Expression);
        return name == "Serialize" || name == "Deserialize" || name == "DeserializeBody" || name == "SerializeObject" || name == "ReadAsAsync" || name == "PostAsync" || name == "PutAsync"
          || (name != null && ResultMethods.Contains(name));
      }
      return node is ObjectCreationExpressionSyntax || node is ImplicitObjectCreationExpressionSyntax;
    }

    /// <summary>
    /// The methods of ASP.NET Core's ControllerBase that take the value of the response as object (<c>Ok(value)</c>, <c>CreatedAtAction(..., value)</c>, <c>BadRequest(error)</c>...).
    /// </summary>
    private static readonly HashSet<string> ResultMethods = new HashSet<string>(StringComparer.Ordinal)
    {
      "Ok", "Created", "CreatedAtAction", "CreatedAtRoute", "Accepted", "AcceptedAtAction", "AcceptedAtRoute", "StatusCode",
      "NotFound", "BadRequest", "Conflict", "UnprocessableEntity", "Unauthorized",
    };

    private static string MethodName(ExpressionSyntax expression)
    {
      switch (expression)
      {
        case MemberAccessExpressionSyntax member: return member.Name.Identifier.ValueText;
        case MemberBindingExpressionSyntax binding: return binding.Name.Identifier.ValueText;
        case SimpleNameSyntax name: return name.Identifier.ValueText;
        default: return null;
      }
    }

    public static void FromOperation(IOperation operation, KnownSymbols known, List<SerializedUse> uses)
    {
      if (operation is IInvocationOperation invocation)
        FromInvocation(invocation, known, uses);
      else if (operation is IObjectCreationOperation creation)
        FromCreation(creation, known, uses);
    }

    public static void FromInvocation(IInvocationOperation invocation, KnownSymbols known, List<SerializedUse> uses)
    {
      IMethodSymbol method = invocation.TargetMethod;
      INamedTypeSymbol container = method.ContainingType?.OriginalDefinition;
      if (container is null)
        return;

      if (Same(container, known.LsSerializer) || Same(container, known.LtSerializer) || Same(container, known.HttpSerializer))
      {
        bool reads = method.Name.StartsWith("Deserialize", StringComparison.Ordinal);
        if (!reads && method.Name != "Serialize" && method.Name != "SerializeObject")
          return;
        string what = string.Concat(container.Name, ".", method.Name);
        if (method.IsGenericMethod)
        {
          uses.Add(new SerializedUse(method.TypeArguments[0], reads, TypeArgumentLocation(invocation.Syntax) ?? invocation.Syntax.GetLocation(), what));
          return;
        }
        for (int t = 0; t < invocation.Arguments.Length; t++)
        {
          IArgumentOperation argument = invocation.Arguments[t];
          if (Same(argument.Parameter?.Type, known.SystemType))
          {
            if (Unwrap(argument.Value) is ITypeOfOperation typeOf)
              uses.Add(new SerializedUse(typeOf.TypeOperand, reads, typeOf.Syntax.GetLocation(), what));
            return;
          }
        }
        if (!reads && invocation.Arguments.Length > 0) // SerializeObject(object): the type of the value
          AddValue(invocation.Arguments[0].Value, what, uses);
        return;
      }

      if (Same(container, known.AspNetCoreController))
      {
        if (ResultMethods.Contains(method.Name))
          AddResultValue(invocation.Arguments, string.Concat("ControllerBase.", method.Name), uses);
        return;
      }

      if (method.IsGenericMethod && ((Same(container, known.HttpContentExtensions) && method.Name == "ReadAsAsync")
        || (Same(container, known.HttpClientExtensions) && (method.Name == "PostAsync" || method.Name == "PutAsync"))))
      {
        bool reads = method.Name == "ReadAsAsync";
        uses.Add(new SerializedUse(method.TypeArguments[0], reads, TypeArgumentLocation(invocation.Syntax) ?? invocation.Syntax.GetLocation(), string.Concat(container.Name, ".", method.Name)));
      }
    }

    private static void FromCreation(IObjectCreationOperation creation, KnownSymbols known, List<SerializedUse> uses)
    {
      INamedTypeSymbol type = creation.Type as INamedTypeSymbol;
      if (type is null)
        return;
      if (Same(type.OriginalDefinition, known.ObjectContent) && type.TypeArguments.Length == 1)
        uses.Add(new SerializedUse(type.TypeArguments[0], false, creation.Syntax.GetLocation(), "ObjectContent"));
      else if (Same(type, known.MvcResult) && creation.Arguments.Length > 0)
        AddValue(creation.Arguments[0].Value, "LsMsgPackResult", uses);
      else if (known.AspNetCoreObjectResult != null && DerivesFrom(type, known.AspNetCoreObjectResult))
        AddResultValue(creation.Arguments, type.Name, uses);
    }

    /// <summary>
    /// The value of an ASP.NET Core result (<c>Ok(value)</c>, <c>new OkObjectResult(value)</c>): the argument of the parameter <c>value</c> (<c>error</c> for BadRequest...) declared as object,
    /// which the output formatters write. Its static type is the root, as for a value passed to <c>Serialize</c>.
    /// </summary>
    private static void AddResultValue(ImmutableArray<IArgumentOperation> arguments, string what, List<SerializedUse> uses)
    {
      for (int t = 0; t < arguments.Length; t++)
      {
        IParameterSymbol parameter = arguments[t].Parameter;
        if (parameter != null && parameter.Type.SpecialType == SpecialType.System_Object && (parameter.Name == "value" || parameter.Name == "error"))
          AddValue(arguments[t].Value, what, uses);
      }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
      for (INamedTypeSymbol b = type; b != null; b = b.BaseType)
        if (Same(b, baseType))
          return true;
      return false;
    }

    /// <summary>
    /// The type of a value passed as object (the static type of the expression before its conversion to object).
    /// </summary>
    private static void AddValue(IOperation value, string what, List<SerializedUse> uses)
    {
      ITypeSymbol type = Unwrap(value)?.Type;
      if (type != null && type.SpecialType != SpecialType.System_Object)
        uses.Add(new SerializedUse(type, false, value.Syntax.GetLocation(), what));
    }

    private static IOperation Unwrap(IOperation value)
    {
      while (value is IConversionOperation conversion && conversion.IsImplicit)
        value = conversion.Operand;
      return value;
    }

    private static Location TypeArgumentLocation(SyntaxNode invocation)
    {
      ExpressionSyntax expression = (invocation as InvocationExpressionSyntax)?.Expression;
      SimpleNameSyntax name = expression is MemberAccessExpressionSyntax member ? member.Name : expression as SimpleNameSyntax;
      if (name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count > 0)
        return generic.TypeArgumentList.Arguments[0].GetLocation();
      return null;
    }

    /// <summary>
    /// <c>[LtMsgPackSerializable(typeof(T))]</c> on a context of LtMsgPack's source generator.
    /// </summary>
    public static void FromAttributes(INamedTypeSymbol type, KnownSymbols known, List<SerializedUse> uses)
    {
      if (known.SerializableAttribute is null)
        return;
      foreach (AttributeData attribute in type.GetAttributes())
      {
        if (!Same(attribute.AttributeClass, known.SerializableAttribute) || attribute.ConstructorArguments.Length == 0)
          continue;
        if (attribute.ConstructorArguments[0].Value is ITypeSymbol listed)
          uses.Add(new SerializedUse(listed, false, attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation(), "LtMsgPackSerializable"));
      }
    }

    /// <summary>
    /// The results and parameters of the actions of a controller, with a reference to the web formatter of its framework.
    /// </summary>
    public static void FromController(INamedTypeSymbol type, KnownSymbols known, List<SerializedUse> uses)
    {
      if (!IsController(type, known))
        return;
      foreach (ISymbol member in type.GetMembers())
        if (member is IMethodSymbol method)
          FromAction(method, known, uses);
    }

    public static bool IsController(INamedTypeSymbol type, KnownSymbols known)
    {
      if (type.TypeKind != TypeKind.Class || type.IsAbstract || (known.AspNetCoreController is null && known.WebApiController is null && known.MvcController is null))
        return false;
      for (INamedTypeSymbol b = type.BaseType; b != null; b = b.BaseType)
        if (Same(b, known.AspNetCoreController) || Same(b, known.WebApiController) || Same(b, known.MvcController))
          return true;
      return false;
    }

    /// <summary>
    /// The result type (Task&lt;ActionResult&lt;T&gt;&gt; holds T) and the parameters of an action of a controller (see <see cref="IsController"/>).
    /// </summary>
    public static void FromAction(IMethodSymbol method, KnownSymbols known, List<SerializedUse> uses)
    {
      if (method.MethodKind != MethodKind.Ordinary || method.IsStatic || method.DeclaredAccessibility != Accessibility.Public || method.IsGenericMethod)
        return;
      foreach (AttributeData attribute in method.GetAttributes())
        if (attribute.AttributeClass?.Name == "NonActionAttribute")
          return;

      string what = string.Concat("The action ", method.ContainingType.Name, ".", method.Name);
      if (!method.ReturnsVoid)
        uses.Add(new SerializedUse(method.ReturnType, false, method.Locations[0], what));
      foreach (IParameterSymbol parameter in method.Parameters)
      {
        bool fromServices = false;
        foreach (AttributeData attribute in parameter.GetAttributes())
          fromServices |= attribute.AttributeClass?.Name == "FromServicesAttribute";
        if (!fromServices)
          uses.Add(new SerializedUse(parameter.Type, true, parameter.Locations[0], what));
      }
    }

    internal static bool Same(ISymbol a, ISymbol b)
    {
      return a != null && b != null && SymbolEqualityComparer.Default.Equals(a, b);
    }
  }
}
