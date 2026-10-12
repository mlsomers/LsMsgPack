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
      ReportUncovered();
    }

    /// <summary>
    /// Properties whose values are of types only known at runtime (declared as object, an interface or an abstract class, also as the elements of a collection).
    /// </summary>
    private readonly List<(INamedTypeSymbol Owner, IPropertySymbol Property, ITypeSymbol Declared)> _polymorphic = new List<(INamedTypeSymbol, IPropertySymbol, ITypeSymbol)>();

    private void RecordPolymorphic(INamedTypeSymbol owner, IPropertySymbol property)
    {
      ITypeSymbol declared = ValueType(property.Type);
      if (declared.SpecialType == SpecialType.System_Object
        || (declared is INamedTypeSymbol named && !IsFramework(named) && (named.TypeKind == TypeKind.Interface || (named.TypeKind == TypeKind.Class && named.IsAbstract))))
        _polymorphic.Add((owner, property, declared));
    }

    /// <summary>
    /// The type of the values a property holds: the underlying type of a Nullable, the elements of an array or collection (the values of a dictionary).
    /// </summary>
    private ITypeSymbol ValueType(ITypeSymbol type)
    {
      for (int depth = 0; depth < 4; depth++)
      {
        if (type is IArrayTypeSymbol array)
          type = array.ElementType;
        else if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
          type = named.TypeArguments[0];
        else if (type is INamedTypeSymbol collection && type.SpecialType != SpecialType.System_String && IsCollection(collection) && Element(collection) is ITypeSymbol element)
          type = element;
        else
          return type;
      }
      return type;
    }

    /// <returns>The T of IEnumerable&lt;T&gt; (the value of a KeyValuePair), null for other collections</returns>
    private ITypeSymbol Element(INamedTypeSymbol collection)
    {
      INamedTypeSymbol enumerable = SymbolEqualityComparer.Default.Equals(collection.OriginalDefinition, _enumerable) ? collection : null;
      if (enumerable is null)
      {
        foreach (INamedTypeSymbol face in collection.AllInterfaces)
        {
          if (SymbolEqualityComparer.Default.Equals(face.OriginalDefinition, _enumerable))
          {
            enumerable = face;
            break;
          }
        }
      }
      if (enumerable is null)
        return null;
      ITypeSymbol element = enumerable.TypeArguments[0];
      if (element is INamedTypeSymbol pair && pair.IsGenericType && pair.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic" && pair.Name == "KeyValuePair")
        return pair.TypeArguments[1];
      return element;
    }

    /// <summary>
    /// LTMP010 (reported when the project is trimmed or compiled ahead of time): a property of a type only known at runtime that no class of the context fits.
    /// Those classes get no generated code: trimming may remove their properties, NativeAOT may fail to create their handlers.
    /// </summary>
    private void ReportUncovered()
    {
      HashSet<string> reported = new HashSet<string>();
      foreach ((INamedTypeSymbol owner, IPropertySymbol property, ITypeSymbol declared) in _polymorphic)
      {
        if (!reported.Add(owner.ToDisplayString() + "." + property.Name) || IsCovered(declared))
          continue;
        _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.Uncovered.Id, property.Locations.FirstOrDefault(), owner.ToDisplayString(), property.Name, declared.ToDisplayString()));
      }
    }

    /// <summary>
    /// A concrete class of the context fits the type. Never for object: what it holds cannot be told from the context (suppress LTMP010 where it only holds primitives).
    /// </summary>
    private bool IsCovered(ITypeSymbol declared)
    {
      if (declared.SpecialType == SpecialType.System_Object)
        return false;
      foreach (ITypeSymbol seen in _seen)
      {
        if (!(seen is INamedTypeSymbol candidate) || candidate.IsAbstract || (candidate.TypeKind != TypeKind.Class && candidate.TypeKind != TypeKind.Struct) || IsFramework(candidate))
          continue;
        if (declared.TypeKind == TypeKind.Interface)
        {
          foreach (INamedTypeSymbol face in candidate.AllInterfaces)
            if (SymbolEqualityComparer.Default.Equals(face, declared))
              return true;
        }
        else
        {
          for (INamedTypeSymbol t = candidate.BaseType; t != null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, declared))
              return true;
        }
      }
      return false;
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
      Add(TypeModelKind.Object, name, null, creator, true, properties, named.IsAbstract ? null : GetMembers(named));
    }

    private void Add(TypeModelKind kind, string name, string argument = null, bool creator = false, bool keep = false, List<PropertyModel> properties = null, List<MemberModel> members = null)
    {
      _types.Add(new TypeModel(kind, name, argument, creator, keep, (properties ?? new List<PropertyModel>()).ToEquatable(), (members ?? new List<MemberModel>()).ToEquatable()));
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
        RecordPolymorphic(type, property);
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

    /// <summary>
    /// The properties the runtime sees on the class (FullPropertyInfo.GetProperties: public instance properties without parameters, not hidden by a property of the same name
    /// in a derived class), in the order Type.GetProperties() returns them as far as the generator can tell (the class's own first, then those of its base classes, each in declaration order).
    /// </summary>
    private List<MemberModel> GetMembers(INamedTypeSymbol type)
    {
      List<MemberModel> members = new List<MemberModel>();
      HashSet<string> names = new HashSet<string>();
      for (INamedTypeSymbol t = type; t != null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
      {
        foreach (ISymbol member in t.GetMembers())
        {
          if (!(member is IPropertySymbol property) || property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public)
            continue;
          if (!names.Add(property.Name)) // hidden by (or overridden in) a derived class
            continue;
          if (property.ReturnsByRef || property.ReturnsByRefReadonly || property.Type.IsRefLikeType || property.Type.TypeKind == TypeKind.Pointer
            || property.Type.TypeKind == TypeKind.FunctionPointer || IsObsoleteError(property) || !IsAccessible(property.Type))
            continue;

          // [DefaultValue]: the runtime's filter compares the value with the attribute's (boxed), the generated writer leaves it to the runtime
          bool canGet = property.GetMethod != null && IsAccessible(property.GetMethod) && !IsObsoleteError(property.GetMethod) && !HasAttribute(property, "System.ComponentModel.DefaultValueAttribute");
          bool canSet = property.SetMethod != null && !property.SetMethod.IsInitOnly && IsAccessible(property.SetMethod) && !IsObsoleteError(property.SetMethod);
          bool unrolled = property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public && !HasIgnoreAttribute(property); // FilterNonSettable, FilterIgnoredAttribute

          ITypeSymbol valueType = property.Type;
          bool nullable = false;
          if (valueType is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
          {
            valueType = named.TypeArguments[0];
            nullable = true;
          }
          string kind = KindOf(valueType, out string underlying);
          if (kind == "None")
            nullable = false;
          members.Add(new MemberModel(t.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), property.Name, Escape(property.Name),
            property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), kind, nullable,
            valueType.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), underlying, canGet, canSet, unrolled));
        }
      }
      return members;
    }

    /// <returns>The LtMsgPack.Generated.GeneratedKind the generated code writes and reads itself, "None" for the others</returns>
    private static string KindOf(ITypeSymbol type, out string enumUnderlying)
    {
      enumUnderlying = null;
      if (type is INamedTypeSymbol named && named.TypeKind == TypeKind.Enum && named.EnumUnderlyingType != null)
      {
        enumUnderlying = Keyword(named.EnumUnderlyingType.SpecialType);
        return enumUnderlying is null ? "None" : "Enum";
      }

      switch (type.SpecialType)
      {
        case SpecialType.System_String: return "String";
        case SpecialType.System_Boolean: return "Boolean";
        case SpecialType.System_SByte: return "SByte";
        case SpecialType.System_Int16: return "Int16";
        case SpecialType.System_Int32: return "Int32";
        case SpecialType.System_Int64: return "Int64";
        case SpecialType.System_Byte: return "Byte";
        case SpecialType.System_UInt16: return "UInt16";
        case SpecialType.System_UInt32: return "UInt32";
        case SpecialType.System_UInt64: return "UInt64";
        case SpecialType.System_Single: return "Single";
        case SpecialType.System_Double: return "Double";
        case SpecialType.System_Char: return "Char";
        case SpecialType.System_Decimal: return "Decimal";
        case SpecialType.System_DateTime: return "DateTime";
      }

      if (type is INamedTypeSymbol system && system.ContainingNamespace?.ToDisplayString() == "System" && system.ContainingType is null && !system.IsGenericType)
      {
        if (system.Name == "Guid")
          return "Guid";
        if (system.Name == "TimeSpan")
          return "TimeSpan";
      }
      return "None";
    }

    private static string Keyword(SpecialType type)
    {
      switch (type)
      {
        case SpecialType.System_SByte: return "sbyte";
        case SpecialType.System_Int16: return "short";
        case SpecialType.System_Int32: return "int";
        case SpecialType.System_Int64: return "long";
        case SpecialType.System_Byte: return "byte";
        case SpecialType.System_UInt16: return "ushort";
        case SpecialType.System_UInt32: return "uint";
        case SpecialType.System_UInt64: return "ulong";
        default: return null;
      }
    }

    /// <summary>
    /// An attribute the default FilterIgnoredAttribute leaves the property out for (any attribute with "Ignore" in its name, on the property).
    /// A wrong guess only costs the unrolled writer: the runtime compares the properties it keeps.
    /// </summary>
    private static bool HasIgnoreAttribute(IPropertySymbol property)
    {
      foreach (AttributeData attribute in property.GetAttributes())
        if (attribute.AttributeClass != null && attribute.AttributeClass.Name.IndexOf("Ignore", System.StringComparison.Ordinal) >= 0)
          return true;
      return false;
    }

    private static bool HasAttribute(ISymbol symbol, string name)
    {
      foreach (AttributeData attribute in symbol.GetAttributes())
        if (attribute.AttributeClass?.ToDisplayString() == name)
          return true;
      return false;
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
