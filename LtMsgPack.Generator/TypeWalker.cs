using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Linq;

namespace LtMsgPack.Generator
{
  /// <summary>
  /// Finds the types a context needs code for: the roots and the types they reach (property types, elements, type arguments, base classes), as models.
  /// <para>The properties are the ones the runtime looks at (FullPropertyInfo.GetProperties: public, instance, no indexers), matched there by declaring type, name and type,
  /// so a property the generator leaves out is simply handled by reflection.</para>
  /// </summary>
  internal sealed class TypeWalker
  {
    internal static readonly SymbolDisplayFormat Format = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly Compilation _compilation;
    private readonly ISymbol _within;
    private readonly string _contextName;
    private readonly INamedTypeSymbol _list;
    private readonly INamedTypeSymbol _enumerable;
    private readonly List<TypeModel> _types = new List<TypeModel>();
    private readonly HashSet<ITypeSymbol> _seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
    private readonly Queue<ITypeSymbol> _queue = new Queue<ITypeSymbol>();
    private readonly List<DiagnosticInfo> _diagnostics;

    /// <param name="within">Where the generated code is (the context class, or the assembly for the context of the assembly): what it can access</param>
    public TypeWalker(Compilation compilation, ISymbol within, string contextName, List<DiagnosticInfo> diagnostics)
    {
      _compilation = compilation;
      _within = within;
      _contextName = contextName;
      _diagnostics = diagnostics;
      _list = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
      _enumerable = compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
    }

    public List<TypeModel> Types => _types;

    /// <param name="location">Where the root is named (diagnostics), null when it was found by a marker</param>
    public void AddRoot(ITypeSymbol type, Location location)
    {
      if (type is null || type.TypeKind == TypeKind.Error)
        return;

      if ((type is INamedTypeSymbol named && named.IsUnboundGenericType) || ContainsTypeParameters(type))
      {
        if (location != null)
          _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.OpenGeneric.Id, location, type.ToDisplayString()));
        return;
      }

      if (!IsAccessible(type))
      {
        if (location != null)
          _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotAccessible.Id, location, type.ToDisplayString(), _contextName));
        return;
      }

      Enqueue(type);
    }

    public void Run()
    {
      while (_queue.Count > 0)
        Visit(_queue.Dequeue());
    }

    private void Enqueue(ITypeSymbol type)
    {
      if (type is null || type.TypeKind == TypeKind.Error || type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer
        || type.TypeKind == TypeKind.TypeParameter || type.TypeKind == TypeKind.Dynamic || type.IsRefLikeType)
        return;
      type = (ITypeSymbol)type.WithNullableAnnotation(NullableAnnotation.None);
      if (_seen.Add(type))
        _queue.Enqueue(type);
    }

    private bool IsAccessible(ISymbol symbol)
    {
      return _compilation.IsSymbolAccessibleWithin(symbol, _within);
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
          foreach (ITypeSymbol argument in named.TypeArguments)
            if (ContainsTypeParameters(argument))
              return true;
          return named.ContainingType != null && ContainsTypeParameters(named.ContainingType);
        default:
          return false;
      }
    }

    private void Visit(ITypeSymbol type)
    {
      if (IsLeaf(type) || !IsAccessible(type) || ContainsTypeParameters(type))
        return;

      string name = type.ToDisplayString(Format);
      if (type is IArrayTypeSymbol array)
      {
        Enqueue(array.ElementType);
        if (array.Rank == 1)
          Add(TypeModelKind.Array, name, array.ElementType.ToDisplayString(Format));
        else
          Add(TypeModelKind.Boxed, name);
        return;
      }

      if (!(type is INamedTypeSymbol named) || type.TypeKind == TypeKind.Delegate)
        return;

      if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
      {
        Enqueue(named.TypeArguments[0]);
        Add(TypeModelKind.Nullable, name, named.TypeArguments[0].ToDisplayString(Format));
        return;
      }

      if (named.TypeKind == TypeKind.Enum)
      {
        Add(TypeModelKind.Enum, name);
        return;
      }

      if (_list != null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, _list))
      {
        Enqueue(named.TypeArguments[0]);
        Add(TypeModelKind.List, name, named.TypeArguments[0].ToDisplayString(Format));
        return;
      }

      foreach (ITypeSymbol argument in named.TypeArguments)
        Enqueue(argument);

      if (IsCollection(named))
      {
        foreach (INamedTypeSymbol face in named.AllInterfaces)
          if (SymbolEqualityComparer.Default.Equals(face.OriginalDefinition, _enumerable))
            Enqueue(face.TypeArguments[0]);
        Add(TypeModelKind.Collection, name, null, HasCreator(named), HasCreator(named));
        return;
      }

      if (IsFramework(named))
      {
        Add(TypeModelKind.Boxed, name);
        return;
      }

      if (named.TypeKind == TypeKind.Struct)
      {
        GetProperties(named); // the types of the properties (the runtime writes and reads structs boxed)
        Add(TypeModelKind.Boxed, name);
        return;
      }

      if (named.TypeKind == TypeKind.Interface)
      {
        Add(TypeModelKind.Object, name); // the instantiations only: values are written and read as their runtime type (type ids)
        return;
      }

      if (named.TypeKind != TypeKind.Class)
        return;

      if (named.BaseType != null && named.BaseType.SpecialType != SpecialType.System_Object)
        Enqueue(named.BaseType);

      bool creator = HasCreator(named);
      List<PropertyModel> properties = GetProperties(named);
      Add(TypeModelKind.Object, name, null, creator, true, properties);
    }

    private void Add(TypeModelKind kind, string name, string argument = null, bool creator = false, bool keep = false, List<PropertyModel> properties = null)
    {
      _types.Add(new TypeModel(kind, name, argument, creator, keep, (properties ?? new List<PropertyModel>()).ToEquatable()));
    }

    /// <summary>
    /// The values the serializer writes and reads without generic instantiations (its typed handlers of these types are not generic).
    /// </summary>
    private static bool IsLeaf(ITypeSymbol type)
    {
      switch (type.SpecialType)
      {
        case SpecialType.System_Boolean:
        case SpecialType.System_Char:
        case SpecialType.System_SByte:
        case SpecialType.System_Byte:
        case SpecialType.System_Int16:
        case SpecialType.System_UInt16:
        case SpecialType.System_Int32:
        case SpecialType.System_UInt32:
        case SpecialType.System_Int64:
        case SpecialType.System_UInt64:
        case SpecialType.System_Single:
        case SpecialType.System_Double:
        case SpecialType.System_Decimal:
        case SpecialType.System_String:
        case SpecialType.System_DateTime:
          return true;
      }

      if (type is IArrayTypeSymbol array && array.Rank == 1 && (array.ElementType.SpecialType == SpecialType.System_Byte || array.ElementType.SpecialType == SpecialType.System_SByte))
        return true; // bin

      if (type is INamedTypeSymbol named && named.ContainingNamespace?.ToDisplayString() == "System" && named.ContainingType is null && !named.IsGenericType)
      {
        switch (named.Name)
        {
          case "Guid":
          case "DateTimeOffset":
          case "TimeSpan":
            return true;
        }
      }
      return false;
    }

    /// <summary>
    /// A type of the framework (as the runtime's TypeResolver.IsFrameworkAssembly decides it): written as a leaf or collection, or refused, never by its properties.
    /// </summary>
    private bool IsFramework(INamedTypeSymbol type)
    {
      IAssemblySymbol assembly = type.ContainingAssembly;
      if (assembly is null || SymbolEqualityComparer.Default.Equals(assembly, _compilation.Assembly))
        return false;
      string name = assembly.Name;
      return name == "mscorlib" || name == "netstandard" || name == "System" || name.StartsWith("System.", System.StringComparison.Ordinal) || name.StartsWith("Microsoft.", System.StringComparison.Ordinal);
    }

    private static bool IsCollection(INamedTypeSymbol type)
    {
      if (type.SpecialType == SpecialType.System_String)
        return false;
      if (type.SpecialType == SpecialType.System_Collections_IEnumerable)
        return true;
      foreach (INamedTypeSymbol face in type.AllInterfaces)
        if (face.SpecialType == SpecialType.System_Collections_IEnumerable)
          return true;
      return false;
    }

    /// <summary>
    /// A parameterless constructor the generated code can call with <c>new T()</c> (the runtime uses the parameterless constructor too, or creates the object without one).
    /// </summary>
    private bool HasCreator(INamedTypeSymbol type)
    {
      if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic)
        return false;

      bool found = false;
      foreach (IMethodSymbol constructor in type.InstanceConstructors)
      {
        if (constructor.Parameters.Length == 0 && IsAccessible(constructor) && !IsObsoleteError(constructor))
        {
          found = true;
          break;
        }
      }
      if (!found)
        return false;

      for (INamedTypeSymbol t = type; t != null; t = t.BaseType) // new T() of a type with required members does not compile
      {
        foreach (ISymbol member in t.GetMembers())
        {
          if ((member is IPropertySymbol property && property.IsRequired) || (member is IFieldSymbol field && field.IsRequired))
            return false;
        }
      }
      return true;
    }

    /// <summary>
    /// The properties the type declares that the runtime may serialize (public instance properties without parameters), the types they reach are added.
    /// </summary>
    private List<PropertyModel> GetProperties(INamedTypeSymbol type)
    {
      List<PropertyModel> properties = new List<PropertyModel>();
      foreach (ISymbol member in type.GetMembers())
      {
        if (!(member is IPropertySymbol property) || property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public
          || property.ReturnsByRef || property.ReturnsByRefReadonly || property.Type.IsRefLikeType || property.Type.TypeKind == TypeKind.Pointer
          || property.Type.TypeKind == TypeKind.FunctionPointer || IsObsoleteError(property) || !IsAccessible(property.Type))
          continue;

        Enqueue(property.Type);
        if (type.TypeKind != TypeKind.Class)
          continue;

        bool canGet = property.GetMethod != null && IsAccessible(property.GetMethod) && !IsObsoleteError(property.GetMethod);
        bool canSet = property.SetMethod != null && !property.SetMethod.IsInitOnly && IsAccessible(property.SetMethod) && !IsObsoleteError(property.SetMethod);
        if (property.SetMethod != null && !canSet)
          _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.PropertyByReflection.Id, property.Locations.FirstOrDefault(), type.ToDisplayString(), property.Name,
            property.SetMethod.IsInitOnly ? "init-only" : "not accessible to the generated code"));
        if (!canGet && !canSet)
          continue;

        properties.Add(new PropertyModel(property.Name, Escape(property.Name), property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), canGet, canSet));
      }
      return properties;
    }

    private static bool IsObsoleteError(ISymbol symbol)
    {
      foreach (AttributeData attribute in symbol.GetAttributes())
      {
        if (attribute.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute" && attribute.ConstructorArguments.Length == 2
          && attribute.ConstructorArguments[1].Value is bool error && error)
          return true;
      }
      return false;
    }

    internal static string Escape(string name)
    {
      return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
    }
  }
}
