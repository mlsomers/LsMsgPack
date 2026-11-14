using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Linq;

namespace LsMsgPack.Generator
{
  /// <summary>
  /// Finds the types a context needs code for: the roots and the types they reach (property types, elements, type arguments, base classes), as models.
  /// <para>The members are the ones the runtime looks at (FullPropertyInfo.GetMembers: public instance properties without parameters, the non-public ones of GenerateOptions.NonPublic,
  /// then the fields of GenerateOptions.Fields), matched there by declaring type, name and type, so a member the generator leaves out is simply handled by reflection.</para>
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
    private readonly int _nonPublic;
    private readonly int _fields;
    private readonly bool _unsafeAccessors;

    /// <param name="within">Where the generated code is (the context class, or the assembly for the context of the assembly): what it can access</param>
    public TypeWalker(Compilation compilation, ISymbol within, string contextName, List<DiagnosticInfo> diagnostics, GenerateOptions options)
    {
      _compilation = compilation;
      _within = within;
      _contextName = contextName;
      _diagnostics = diagnostics;
      _nonPublic = options.NonPublic;
      _fields = options.Fields;
      _unsafeAccessors = options.UnsafeAccessors;
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
      for (int i = _bulkArrays.Count - 1; i >= 0; i--)
        if (!_direct.Contains(_bulkArrays[i].Array))
          _types[_bulkArrays[i].Index] = _types[_bulkArrays[i].Index] with { Bulk = false };
      ReportUncovered();
    }

    /// <summary>
    /// The arrays described in bulk (<see cref="TypeModel.Bulk"/>), and the arrays reached other than as the elements of a collection (<see cref="AddImplied"/>): an array only reached that way
    /// is for Core's conversions, which never use the handlers in bulk, so it is described without them (NativeAOT compiles every instantiation a description makes).
    /// </summary>
    private readonly List<(ITypeSymbol Array, int Index)> _bulkArrays = new List<(ITypeSymbol, int)>();
    private readonly HashSet<ITypeSymbol> _direct = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

    /// <summary>
    /// Whether the runtime writes and reads collections of the type in bulk where the options write the type as such (Serializer.InBulk; an sbyte[] is bin, a leaf).
    /// </summary>
    private static bool IsBulkElement(ITypeSymbol element)
    {
      switch (element.SpecialType)
      {
        case SpecialType.System_Int32:
        case SpecialType.System_Int64:
        case SpecialType.System_Single:
        case SpecialType.System_Double:
        case SpecialType.System_SByte:
        case SpecialType.System_Int16:
        case SpecialType.System_UInt16:
        case SpecialType.System_UInt32:
        case SpecialType.System_UInt64:
        case SpecialType.System_Boolean:
        case SpecialType.System_Decimal:
          return true;
      }
      return element.MetadataName == "Guid" && element.ContainingType is null && element.ContainingNamespace?.ToDisplayString() == "System";
    }

    /// <summary>
    /// Properties and fields whose values are of types only known at runtime (declared as object, an interface or an abstract class, also as the elements of a collection).
    /// </summary>
    private readonly List<(INamedTypeSymbol Owner, ISymbol Property, ITypeSymbol Declared)> _polymorphic = new List<(INamedTypeSymbol, ISymbol, ITypeSymbol)>();

    private void RecordPolymorphic(INamedTypeSymbol owner, ISymbol property, ITypeSymbol type)
    {
      ITypeSymbol declared = ValueType(type);
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
    /// LSMP110 (reported when the project is trimmed or compiled ahead of time): a property or field of a type only known at runtime that no class of the context fits.
    /// Those classes get no generated code: trimming may remove their properties, NativeAOT may fail to create their handlers.
    /// </summary>
    private void ReportUncovered()
    {
      HashSet<string> reported = new HashSet<string>();
      foreach ((INamedTypeSymbol owner, ISymbol property, ITypeSymbol declared) in _polymorphic)
      {
        if (!reported.Add(owner.ToDisplayString() + "." + property.Name) || IsCovered(declared))
          continue;
        _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.Uncovered.Id, property.Locations.FirstOrDefault(), owner.ToDisplayString(), property.Name, declared.ToDisplayString()));
      }
    }

    /// <summary>
    /// A concrete class of the context fits the type. Never for object: what it holds cannot be told from the context (suppress LSMP110 where it only holds primitives).
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

    /// <param name="implied">The element array of a collection (<see cref="AddImplied"/>)</param>
    private void Enqueue(ITypeSymbol type, bool implied = false)
    {
      if (type is null || type.TypeKind == TypeKind.Error || type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer
        || type.TypeKind == TypeKind.TypeParameter || type.TypeKind == TypeKind.Dynamic || type.IsRefLikeType)
        return;
      type = (ITypeSymbol)type.WithNullableAnnotation(NullableAnnotation.None);
      if (!implied && type is IArrayTypeSymbol)
        _direct.Add(type);
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
        {
          bool bulk = IsBulkElement(array.ElementType);
          if (bulk)
            _bulkArrays.Add((type, _types.Count));
          Add(TypeModelKind.Array, name, array.ElementType.ToDisplayString(Format), bulk: bulk);
        }
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
        Add(TypeModelKind.List, name, named.TypeArguments[0].ToDisplayString(Format), bulk: IsBulkElement(named.TypeArguments[0]));
        return;
      }

      foreach (ITypeSymbol argument in named.TypeArguments)
        Enqueue(argument);

      if (IsCollection(named))
      {
        ITypeSymbol element = SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, _enumerable) ? named.TypeArguments[0] : null;
        foreach (INamedTypeSymbol face in named.AllInterfaces)
        {
          if (SymbolEqualityComparer.Default.Equals(face.OriginalDefinition, _enumerable))
          {
            Enqueue(face.TypeArguments[0]);
            if (element is null)
              element = face.TypeArguments[0];
          }
        }
        string factory = element is null ? null : FactoryCall(named, element);
        if (IsDefinition(named, "HashSet", 1))
          Add(TypeModelKind.HashSet, name, named.TypeArguments[0].ToDisplayString(Format), bulk: IsBulkElement(named.TypeArguments[0]));
        else if (IsDefinition(named, "Dictionary", 2))
          Add(TypeModelKind.Dictionary, name, named.TypeArguments[0].ToDisplayString(Format) + ", " + named.TypeArguments[1].ToDisplayString(Format));
        else if (HasTypedConcrete(named, element))
          Add(TypeModelKind.CollectionInterface, name);
        else
          Add(TypeModelKind.Collection, name, factory is null ? null : element.ToDisplayString(Format), HasCreator(named), HasCreator(named), factory: factory);
        AddImplied(named, element);
        return;
      }

      bool fieldStruct = IsFieldStruct(named);
      if (IsFramework(named) && !fieldStruct)
      {
        Add(TypeModelKind.Boxed, name);
        return;
      }

      if (named.TypeKind == TypeKind.Struct) // written and read by reference (the runtime's struct plans), with the accessors and the code generated here
      {
        List<PropertyModel> accessors = GetProperties(named, fieldStruct, out Kept kept);
        string isDefault = DefaultCheck(named, out List<PropertyModel> defaultAccessors);
        Add(TypeModelKind.Struct, name, null, HasCreator(named), true, accessors, GetMembers(named, fieldStruct), kept: kept, isDefault: isDefault, defaultAccessors: defaultAccessors,
          keptMethods: fieldStruct ? null : ShouldSerializeMethods(named));
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
      List<PropertyModel> properties = GetProperties(named, false, out Kept found);
      Add(TypeModelKind.Object, name, null, creator, true, properties, named.IsAbstract ? null : GetMembers(named, false), kept: found, keptMethods: ShouldSerializeMethods(named));
    }

    /// <summary>
    /// The <c>ShouldSerialize{Name}()</c> methods the type declares, as LsMsgPack.Core's ShouldSerializeMethod finds them by name (public, parameterless, returning bool, not generic):
    /// FilterShouldSerialize calls them by reflection, so the trimmer has to keep them. Any such name, the runtime decides which members they belong to.
    /// </summary>
    /// <returns>null when there are none</returns>
    private static List<StringValue> ShouldSerializeMethods(INamedTypeSymbol type)
    {
      List<StringValue> names = null;
      foreach (ISymbol member in type.GetMembers())
      {
        if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary && method.DeclaredAccessibility == Accessibility.Public && method.Parameters.Length == 0
          && !method.IsGenericMethod && method.ReturnType.SpecialType == SpecialType.System_Boolean && method.Name.Length > 15 && method.Name.StartsWith("ShouldSerialize", System.StringComparison.Ordinal))
          (names ?? (names = new List<StringValue>())).Add(new StringValue(method.Name));
      }
      return names;
    }

    /// <returns>Whether the type is exactly this generic class of System.Collections.Generic</returns>
    private static bool IsDefinition(INamedTypeSymbol type, string name, int arity)
    {
      return type.Name == name && type.Arity == arity && type.TypeKind == TypeKind.Class && type.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
    }

    /// <returns>A generic interface the runtime reads into a class with a typed reader (Serializer.InterfaceReader)</returns>
    private bool HasTypedConcrete(INamedTypeSymbol type, ITypeSymbol element)
    {
      if (element is null || type.TypeKind != TypeKind.Interface || !type.IsGenericType)
        return false;
      INamedTypeSymbol concrete = ConcreteOf(type, element);
      return concrete != null && (IsDefinition(concrete, "List", 1) || IsDefinition(concrete, "HashSet", 1) || IsDefinition(concrete, "Dictionary", 2));
    }

    /// <summary>
    /// The types the runtime makes for a collection it fills (LsMsgPack.Core's CollectionInfo, ValueConverter): the array of the elements (it reads them into one first),
    /// the class it creates for an interface or abstract class, the static factory class of an immutable or frozen collection.
    /// They are described (or kept) too, so NativeAOT has their instantiations and trimming keeps what is found by reflection.
    /// </summary>
    /// <param name="element">The T of IEnumerable&lt;T&gt;, null for a non-generic collection</param>
    private void AddImplied(INamedTypeSymbol collection, ITypeSymbol element)
    {
      if (element is null)
        return;

      string ns = collection.ContainingNamespace?.ToDisplayString();
      bool immutable = collection.IsGenericType && (ns == "System.Collections.Immutable" || ns == "System.Collections.Frozen");
      if (immutable || !IsDictionary(collection)) // a dictionary is filled by its Add, the factories take the pairs as an array
        Enqueue(_compilation.CreateArrayTypeSymbol(element), true);

      if (collection.TypeKind == TypeKind.Interface || collection.IsAbstract)
      {
        INamedTypeSymbol concrete = ConcreteOf(collection, element);
        if (concrete != null)
        {
          Enqueue(concrete);
          ns = concrete.ContainingNamespace?.ToDisplayString();
          immutable = concrete.IsGenericType && (ns == "System.Collections.Immutable" || ns == "System.Collections.Frozen");
          collection = concrete;
        }
      }

      if (immutable && collection.TypeKind != TypeKind.Interface)
      {
        INamedTypeSymbol factory = _compilation.GetTypeByMetadataName(ns + "." + collection.Name); // the static class of the same name
        if (factory != null && factory.IsStatic && _seen.Add(factory))
          Add(TypeModelKind.Factory, factory.ToDisplayString(Format));
      }
    }

    /// <summary>
    /// The factory the runtime uses for an immutable or frozen collection (CollectionInfo.FindFactory): a public static generic method of the static class with the same name,
    /// with the collection's type arguments, that takes IEnumerable&lt;T&gt; (and optional parameters) and returns the collection. CreateRange when there is one, otherwise the first.
    /// </summary>
    /// <returns>The method with its type arguments (to call with the elements), null when the collection has none</returns>
    private string FactoryCall(INamedTypeSymbol collection, ITypeSymbol element)
    {
      string ns = collection.ContainingNamespace?.ToDisplayString();
      if (!collection.IsGenericType || collection.TypeKind == TypeKind.Interface || (ns != "System.Collections.Immutable" && ns != "System.Collections.Frozen"))
        return null;
      INamedTypeSymbol factoryClass = _compilation.GetTypeByMetadataName(ns + "." + collection.Name);
      if (factoryClass is null)
        return null;

      INamedTypeSymbol enumerable = _enumerable.Construct(element);
      IMethodSymbol found = null;
      foreach (ISymbol member in factoryClass.GetMembers())
      {
        if (!(member is IMethodSymbol method) || !method.IsStatic || method.DeclaredAccessibility != Accessibility.Public || method.TypeParameters.Length != collection.TypeArguments.Length || method.Parameters.Length == 0)
          continue;
        IMethodSymbol constructed = method.Construct(collection.TypeArguments.ToArray());
        if (!SymbolEqualityComparer.Default.Equals(constructed.Parameters[0].Type, enumerable) || !_compilation.HasImplicitConversion(constructed.ReturnType, collection))
          continue;
        bool optional = true;
        for (int t = 1; t < constructed.Parameters.Length; t++)
          optional &= constructed.Parameters[t].IsOptional;
        if (!optional || !IsAccessible(method))
          continue;
        if (found is null || method.Name == "CreateRange")
          found = method;
      }
      if (found is null)
        return null;
      return factoryClass.ToDisplayString(Format) + "." + found.Name + "<" + string.Join(", ", collection.TypeArguments.Select(a => a.ToDisplayString(Format))) + ">";
    }

    private static bool IsDictionary(INamedTypeSymbol type)
    {
      if (IsDictionaryInterface(type))
        return true;
      foreach (INamedTypeSymbol face in type.AllInterfaces)
        if (IsDictionaryInterface(face))
          return true;
      return false;
    }

    private static bool IsDictionaryInterface(INamedTypeSymbol type)
    {
      if (type.ContainingNamespace?.ToDisplayString() == "System.Collections")
        return type.Name == "IDictionary";
      return type.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic" && (type.Name == "IDictionary" || type.Name == "IReadOnlyDictionary") && type.Arity == 2;
    }

    /// <summary>
    /// The class the runtime creates for an interface or abstract collection type (as CollectionInfo.GetConcreteType): the implementation of an IImmutable... interface,
    /// otherwise the first of Dictionary&lt;TKey, TValue&gt; (pairs), object[] (object), List&lt;T&gt;, HashSet&lt;T&gt; and T[] that can be assigned to it.
    /// </summary>
    /// <returns>null when none fits (the runtime fails to create it)</returns>
    private INamedTypeSymbol ConcreteOf(INamedTypeSymbol type, ITypeSymbol element)
    {
      if (type.TypeKind == TypeKind.Interface && type.IsGenericType && type.ContainingNamespace?.ToDisplayString() == "System.Collections.Immutable" && type.Name.StartsWith("IImmutable", System.StringComparison.Ordinal))
      {
        string name = type.Name == "IImmutableSet" ? "ImmutableHashSet" : type.Name.Substring(1);
        INamedTypeSymbol definition = _compilation.GetTypeByMetadataName("System.Collections.Immutable." + name + "`" + type.Arity);
        INamedTypeSymbol implementation = definition?.Construct(type.TypeArguments.ToArray());
        return implementation != null && _compilation.HasImplicitConversion(implementation, type) ? implementation : null;
      }

      List<ITypeSymbol> candidates = new List<ITypeSymbol>();
      if (element is INamedTypeSymbol pair && pair.IsGenericType && pair.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic" && pair.Name == "KeyValuePair")
        candidates.Add(_compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2")?.Construct(pair.TypeArguments[0], pair.TypeArguments[1]));
      else
      {
        candidates.Add(_list?.Construct(element));
        candidates.Add(_compilation.GetTypeByMetadataName("System.Collections.Generic.HashSet`1")?.Construct(element));
      }
      for (int t = 0; t < candidates.Count; t++)
        if (candidates[t] is INamedTypeSymbol candidate && _compilation.HasImplicitConversion(candidate, type))
          return candidate;
      return null; // object[] and T[] are enqueued anyway
    }

    private void Add(TypeModelKind kind, string name, string argument = null, bool creator = false, bool keep = false, List<PropertyModel> properties = null, List<MemberModel> members = null, string factory = null,
      Kept kept = Kept.None, string isDefault = null, List<PropertyModel> defaultAccessors = null, List<StringValue> keptMethods = null, bool bulk = false)
    {
      _types.Add(new TypeModel(kind, name, argument, creator, keep, (properties ?? new List<PropertyModel>()).ToEquatable(), (members ?? new List<MemberModel>()).ToEquatable(), factory,
        (kept & Kept.NonPublicProperties) != 0, isDefault, (kept & Kept.PublicFields) != 0, (kept & Kept.NonPublicFields) != 0,
        isDefault is null || defaultAccessors is null ? EquatableArray<PropertyModel>.Empty : defaultAccessors.ToEquatable(),
        keptMethods is null ? EquatableArray<StringValue>.Empty : keptMethods.ToEquatable(), bulk));
    }

    /// <summary>
    /// The members of a type the runtime finds by reflection besides its public properties (kept by the trimmer: DynamicDependency).
    /// </summary>
    [System.Flags]
    private enum Kept
    {
      None = 0,
      NonPublicProperties = 1,
      PublicFields = 2,
      NonPublicFields = 4
    }

    #region The default check of a struct (LsMsgPack's StructDefault)

    /// <summary>
    /// The default check of FilterDefaultValues for the struct, as LsMsgPack's StructDefault compiles it (LsMsgPack.Core: <c>Activator.CreateInstance(type).Equals(value)</c>, ValueType.Equals):
    /// each field against its default with the Equals of its type. A record struct by its own Equals (what its Equals(object) calls). Null where the generated code cannot see or read
    /// every field the same way (fields of other assemblies, explicit layouts, own Equals, a declared parameterless constructor, inaccessible members without an UnsafeAccessor): the runtime compares then.
    /// </summary>
    /// <param name="accessors">The struct's own members read through an UnsafeAccessor (private fields, private getters of auto-properties): the expression has the placeholder <c>\u0001n\u0001</c> for the n-th, the emitter writes the call</param>
    /// <returns>A C# expression over <c>v</c> (the struct by reference), or null</returns>
    private string DefaultCheck(INamedTypeSymbol type, out List<PropertyModel> accessors)
    {
      accessors = new List<PropertyModel>();
      _defaultAccessors = accessors;
      foreach (IMethodSymbol constructor in type.InstanceConstructors)
        if (constructor.Parameters.Length == 0 && !constructor.IsImplicitlyDeclared) // Core's default is what it makes
          return null;
      string name = type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format);
      if (OwnEquals(type)) // a record's Equals(object) calls its Equals(T), so do the framework's structs (Vector3...), which StructDefault compares typed too
        return type.IsRecord || (IsFramework(type) && Implements(type, "System.IEquatable<T>")) ? "default(" + name + ").Equals(v)" : null;
      return FieldsAreDefault(type, "v", 0);
    }

    private List<PropertyModel> _defaultAccessors;

    /// <summary>
    /// The placeholder of a member of the struct (on <c>v</c>) read through an UnsafeAccessor (see <see cref="DefaultCheck"/>).
    /// </summary>
    private string DefaultAccessor(string name, ITypeSymbol type, bool field)
    {
      _defaultAccessors.Add(new PropertyModel(name, Escape(name), type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), true, false, true, false, field));
      return "\u0001" + (_defaultAccessors.Count - 1) + "\u0001";
    }

    private string FieldsAreDefault(INamedTypeSymbol type, string value, int depth)
    {
      if (depth > 16 || type.Locations.Length == 0 || !type.Locations[0].IsInSource || IsExplicitLayout(type)) // the fields of another assembly are not all visible
        return null;
      List<string> checks = new List<string>();
      foreach (ISymbol member in type.GetMembers())
      {
        if (member.IsStatic)
          continue;
        string check;
        switch (member)
        {
          case IFieldSymbol field:
            if (field.IsConst)
              continue;
            if (field.AssociatedSymbol is IPropertySymbol) // the backing field of an auto-property: compared through the property
              continue;
            if (field.IsImplicitlyDeclared || field.IsFixedSizeBuffer)
              return null;
            if (IsAccessible(field))
              check = FieldIsDefault(value + "." + Escape(field.Name), field.Type, depth);
            else if (depth == 0 && CanUseAccessor(type)) // a private field of the struct itself: by reference through an UnsafeAccessor
              check = FieldIsDefault(DefaultAccessor(field.Name, field.Type, true), field.Type, depth);
            else
              return null;
            break;
          case IPropertySymbol property:
            bool? auto = IsAutoProperty(property);
            if (auto is null)
              return null;
            if (!auto.Value) // computed: no field
              continue;
            if (property.GetMethod is null)
              return null;
            if (IsAccessible(property.GetMethod))
              check = FieldIsDefault(value + "." + Escape(property.Name), property.Type, depth);
            else if (depth == 0 && CanUseAccessor(type, property)) // a private getter of the struct itself: through an UnsafeAccessor
              check = FieldIsDefault(DefaultAccessor(property.Name, property.Type, false), property.Type, depth);
            else
              return null;
            break;
          case IEventSymbol _: // a field-like event has a field
            return null;
          default:
            continue;
        }
        if (check is null)
          return null;
        checks.Add(check);
      }
      return checks.Count == 0 ? "true" : string.Join(" && ", checks);
    }

    /// <summary>
    /// The field (of the default instance, zero) equals the value as StructDefault.FieldIsDefault compares it.
    /// </summary>
    private string FieldIsDefault(string value, ITypeSymbol type, int depth)
    {
      if (type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer || type.IsRefLikeType || type.TypeKind == TypeKind.TypeParameter)
        return null;
      if (!type.IsValueType) // a null field
        return "(object)" + value + " == null";
      if (type.TypeKind == TypeKind.Enum)
        return value + " == 0";
      string name = type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format);
      switch (type.SpecialType)
      {
        case SpecialType.System_Boolean:
        case SpecialType.System_Char:
        case SpecialType.System_SByte:
        case SpecialType.System_Int16:
        case SpecialType.System_Int32:
        case SpecialType.System_Int64:
        case SpecialType.System_Byte:
        case SpecialType.System_UInt16:
        case SpecialType.System_UInt32:
        case SpecialType.System_UInt64:
        case SpecialType.System_Single: // == 0: -0.0 is a default, NaN is not (as 0f.Equals)
        case SpecialType.System_Double:
        case SpecialType.System_IntPtr:
        case SpecialType.System_UIntPtr:
          return value + " == default(" + name + ")";
        case SpecialType.System_Decimal: // decimal.Equals: 0.00m equals 0m
          return value + " == 0m";
      }
      if (type is INamedTypeSymbol named)
      {
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) // a Nullable without a value is boxed as null
          return "!" + value + ".HasValue";
        if (OwnEquals(named))
          return named.IsRecord || (IsFramework(named) && Implements(named, "System.IEquatable<T>")) ? "default(" + name + ").Equals(" + value + ")" : null; // their Equals(T) is their Equals(object)
        return FieldsAreDefault(named, value, depth + 1);
      }
      return null;
    }

    /// <summary>
    /// The struct overrides Equals(object) (otherwise it is ValueType.Equals).
    /// </summary>
    private static bool OwnEquals(INamedTypeSymbol type)
    {
      foreach (ISymbol member in type.GetMembers("Equals"))
        if (member is IMethodSymbol method && !method.IsStatic && method.IsOverride && method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Object)
          return true;
      return false;
    }

    /// <param name="metadataName">The generic interface as ToDisplayString writes its definition (System.IEquatable&lt;T&gt;)</param>
    private static bool Implements(INamedTypeSymbol type, string metadataName)
    {
      foreach (INamedTypeSymbol face in type.AllInterfaces)
        if (face.OriginalDefinition.ToDisplayString() == metadataName && face.TypeArguments.Length == 1 && SymbolEqualityComparer.Default.Equals(face.TypeArguments[0], type))
          return true;
      return false;
    }

    private static bool IsExplicitLayout(INamedTypeSymbol type)
    {
      foreach (AttributeData attribute in type.GetAttributes())
        if (attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute" && attribute.ConstructorArguments.Length > 0
          && attribute.ConstructorArguments[0].Value is int kind && kind == 2) // LayoutKind.Explicit
          return true;
      return false;
    }

    /// <summary>
    /// The property reads a field the compiler declared for it (an auto-property, or a positional property of a record): true; computed (no field of its own): false;
    /// null when it cannot be told (an accessor using the field keyword, a property of another assembly).
    /// </summary>
    private static bool? IsAutoProperty(IPropertySymbol property)
    {
      if (property.IsIndexer)
        return false;
      if (property.DeclaringSyntaxReferences.Length != 1) // another assembly's, or a partial property (its definition has no bodies)
        return null;
      foreach (SyntaxReference reference in property.DeclaringSyntaxReferences)
      {
        SyntaxNode node = reference.GetSyntax();
        if (node is Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax) // a positional property of a record
          return true;
        if (!(node is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax declaration))
          return null;
        if (declaration.ExpressionBody != null)
          return UsesField(declaration.ExpressionBody) ? (bool?)null : false;
        if (declaration.AccessorList is null || declaration.AccessorList.Accessors.Count == 0)
          return null;
        int bodies = 0;
        foreach (Microsoft.CodeAnalysis.CSharp.Syntax.AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
        {
          if (accessor.Body is null && accessor.ExpressionBody is null)
            continue;
          if (UsesField(accessor))
            return null;
          bodies++;
        }
        if (bodies == 0)
          return true; // { get; set; }
        if (bodies == declaration.AccessorList.Accessors.Count)
          return false; // computed
        return null; // { get; set { ... } }: a field read through code (C# 14 semi-auto)
      }
      return null;
    }

    /// <summary>
    /// The field keyword (C# 14): the property has a field, read through code the generator does not follow.
    /// </summary>
    private static bool UsesField(SyntaxNode node)
    {
      foreach (SyntaxToken token in node.DescendantTokens())
        if (token.IsKind(SyntaxKind.FieldKeyword) || (token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == "field"))
          return true;
      return false;
    }

    #endregion

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
    /// A struct only with a declared one (C# 10): otherwise it is read into its default, as the runtime does.
    /// </summary>
    private bool HasCreator(INamedTypeSymbol type)
    {
      if ((type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct) || type.IsAbstract || type.IsStatic)
        return false;

      bool found = false;
      foreach (IMethodSymbol constructor in type.InstanceConstructors)
      {
        if (constructor.Parameters.Length == 0 && IsAccessible(constructor) && !IsObsoleteError(constructor) && (type.TypeKind == TypeKind.Class || !constructor.IsImplicitlyDeclared))
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
    /// The properties and fields the type declares that the runtime may serialize (public instance properties without parameters, the non-public ones of <see cref="IsNonPublicCandidate"/>,
    /// the fields of <see cref="IsFieldCandidate"/>; only the public fields of a framework struct whose state they are), the types they reach are added.
    /// </summary>
    /// <param name="kept">The members the runtime finds by reflection besides the public properties (the trimmer keeps them)</param>
    private List<PropertyModel> GetProperties(INamedTypeSymbol type, bool fieldStruct, out Kept kept)
    {
      kept = Kept.None;
      List<PropertyModel> properties = new List<PropertyModel>();
      foreach (ISymbol member in type.GetMembers())
      {
        if (member is IFieldSymbol field)
        {
          if (field.DeclaredAccessibility == Accessibility.Public && (fieldStruct || !IsFramework(type)) && IsFieldOfState(field))
            kept |= Kept.PublicFields; // as public properties: kept whatever the mode, so a runtime serializing them (SerializedFields.Public) still finds them when trimmed
          if (!(fieldStruct ? IsFieldStructMember(field) : !IsFramework(type) && IsFieldCandidate(field, _fields)))
            continue;
          if (field.DeclaredAccessibility != Accessibility.Public)
            kept |= Kept.NonPublicFields;
          if (IsObsoleteError(field) || !IsAccessible(field.Type))
            continue;
          Enqueue(field.Type);
          RecordPolymorphic(type, field, field.Type);
          bool canGetField = CanGet(type, field, out bool unsafeField);
          bool canSetField = CanSet(type, field);
          if (!canGetField && !canSetField)
            continue;
          properties.Add(new PropertyModel(field.Name, Escape(field.Name), field.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), canGetField, canSetField, unsafeField, canSetField && unsafeField, true));
          continue;
        }

        if (fieldStruct || !(member is IPropertySymbol property) || property.IsStatic || property.IsIndexer || (property.DeclaredAccessibility != Accessibility.Public && !IsNonPublicCandidate(property, _nonPublic))
          || property.ReturnsByRef || property.ReturnsByRefReadonly || property.Type.IsRefLikeType || property.Type.TypeKind == TypeKind.Pointer
          || property.Type.TypeKind == TypeKind.FunctionPointer || IsObsoleteError(property) || !IsAccessible(property.Type))
          continue;

        if (property.DeclaredAccessibility != Accessibility.Public)
          kept |= Kept.NonPublicProperties;
        Enqueue(property.Type);
        RecordPolymorphic(type, property, property.Type);

        bool canGet = CanGet(type, property, out bool unsafeGet);
        bool canSet = CanSet(type, property, out bool unsafeSet);
        if (property.SetMethod != null && !canSet)
          _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.PropertyByReflection.Id, property.Locations.FirstOrDefault(), type.ToDisplayString(), property.Name,
            property.SetMethod.IsInitOnly ? "init-only" : "not accessible to the generated code"));
        if (!canGet && !canSet)
          continue;

        properties.Add(new PropertyModel(property.Name, Escape(property.Name), property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), canGet, canSet, unsafeGet, unsafeSet));
      }
      return properties;
    }

    /// <summary>
    /// The generated code can read the field: it is accessible, or an UnsafeAccessor returns it by reference (<paramref name="viaAccessor"/>; the generator only describes the fields the runtime may serialize).
    /// </summary>
    private bool CanGet(INamedTypeSymbol declaring, IFieldSymbol field, out bool viaAccessor)
    {
      viaAccessor = false;
      if (IsAccessible(field))
        return true;
      return viaAccessor = CanUseAccessor(declaring);
    }

    /// <summary>
    /// The generated code can set the field: not readonly (decision of the runtime: a readonly field is only set by a constructor, never behind the type's back), accessible or through the UnsafeAccessor of <see cref="CanGet(INamedTypeSymbol, IFieldSymbol, out bool)"/>.
    /// </summary>
    private bool CanSet(INamedTypeSymbol declaring, IFieldSymbol field)
    {
      return !field.IsReadOnly && (IsAccessible(field) || CanUseAccessor(declaring));
    }

    /// <summary>
    /// The generated code can call the getter: it is accessible, or an UnsafeAccessor can call it (<paramref name="viaAccessor"/>, see <see cref="ViaAccessor"/>).
    /// </summary>
    private bool CanGet(INamedTypeSymbol declaring, IPropertySymbol property, out bool viaAccessor)
    {
      viaAccessor = false;
      IMethodSymbol get = property.GetMethod;
      if (get is null || IsObsoleteError(get))
        return false;
      if (IsAccessible(get))
        return true;
      return viaAccessor = ViaAccessor(declaring, property);
    }

    /// <summary>
    /// The generated code can call the setter: it is accessible, or an UnsafeAccessor can call it (<paramref name="viaAccessor"/>, see <see cref="ViaAccessor"/>).
    /// An init-only setter only compiles in an object initializer, so an accessible one takes an UnsafeAccessor too, whatever <see cref="GenerateOptions.NonPublic"/> says (the runtime sets it with every option).
    /// </summary>
    private bool CanSet(INamedTypeSymbol declaring, IPropertySymbol property, out bool viaAccessor)
    {
      viaAccessor = false;
      IMethodSymbol set = property.SetMethod;
      if (set is null || IsObsoleteError(set))
        return false;
      if (!IsAccessible(set))
        return viaAccessor = ViaAccessor(declaring, property);
      if (set.IsInitOnly)
        return viaAccessor = CanUseAccessor(declaring, property);
      return true;
    }

    /// <summary>
    /// A non-public accessor the generated code calls through an UnsafeAccessor (.NET 8): only of a property the runtime may serialize with its non-public accessors
    /// (<see cref="GenerateOptions.NonPublic"/>: all, or the ones with an opt-in attribute). Otherwise the runtime uses reflection.
    /// </summary>
    private bool ViaAccessor(INamedTypeSymbol declaring, IPropertySymbol property)
    {
      return CanUseAccessor(declaring, property) && (_nonPublic == LsMsgPackGenerator.NonPublicAll || (_nonPublic == LsMsgPackGenerator.NonPublicAttributed && OptsIn(property)));
    }

    /// <summary>
    /// An UnsafeAccessor can call the property's accessors: declared by a class that is not generic (UnsafeAccessor supports generics from .NET 9), not virtual (UnsafeAccessor would call this declaration,
    /// the runtime calls the override). A struct's accessor takes it by reference.
    /// </summary>
    private bool CanUseAccessor(INamedTypeSymbol declaring, IPropertySymbol property)
    {
      return !property.IsVirtual && !property.IsOverride && !property.IsAbstract && CanUseAccessor(declaring);
    }

    /// <summary>
    /// An UnsafeAccessor can reach the members of the type: a class or struct that is not generic (UnsafeAccessor supports generics from .NET 9) and accessible to the generated code.
    /// </summary>
    private bool CanUseAccessor(INamedTypeSymbol declaring)
    {
      if (!_unsafeAccessors || (declaring.TypeKind != TypeKind.Class && declaring.TypeKind != TypeKind.Struct) || !IsAccessible(declaring))
        return false;
      for (INamedTypeSymbol t = declaring; t != null; t = t.ContainingType)
        if (t.IsGenericType)
          return false;
      return true;
    }

    /// <summary>
    /// A non-public property the runtime serializes with FilterNonSettable's NonPublicProperties (FullPropertyInfo.NonPublicCandidates): an instance property without parameters,
    /// not an explicit interface implementation, not generated by the compiler (a record's EqualityContract), with <paramref name="nonPublic"/> Attributed only with an opt-in attribute.
    /// </summary>
    internal static bool IsNonPublicCandidate(IPropertySymbol property, int nonPublic)
    {
      if (nonPublic == LsMsgPackGenerator.NonPublicNone || property.DeclaredAccessibility == Accessibility.Public || property.IsStatic || property.IsIndexer
        || property.ExplicitInterfaceImplementations.Length > 0 || property.IsImplicitlyDeclared)
        return false;
      return nonPublic == LsMsgPackGenerator.NonPublicAll || OptsIn(property);
    }

    /// <summary>
    /// A field the runtime may serialize with FilterNonSettable's SerializedFields <paramref name="fields"/> (FullPropertyInfo.AddFieldCandidates, IsFieldCandidate): an instance field,
    /// not const, not generated by the compiler (backing fields of auto-properties and events: implicitly declared here, names with &lt; or VB's $ (static locals, anonymous types) in other assemblies), not a fixed-size buffer,
    /// not of a pointer or ref struct type; with Attributed only with an opt-in attribute, with Public also the public ones, with All all. Framework-declared fields are left out by the caller.
    /// </summary>
    internal static bool IsFieldCandidate(IFieldSymbol field, int fields)
    {
      if (fields == LsMsgPackGenerator.FieldsNone || !IsFieldOfState(field))
        return false;
      switch (fields)
      {
        case LsMsgPackGenerator.FieldsAll: return true;
        case LsMsgPackGenerator.FieldsPublic: return field.DeclaredAccessibility == Accessibility.Public || OptsIn(field);
        default: return OptsIn(field);
      }
    }

    /// <summary>
    /// A field that may be serialized at all (the first checks of FullPropertyInfo.IsFieldCandidate), whatever the mode.
    /// </summary>
    private static bool IsFieldOfState(IFieldSymbol field)
    {
      if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared || field.AssociatedSymbol != null || field.Name.IndexOf('<') >= 0 || field.Name.IndexOf('$') >= 0 || field.IsFixedSizeBuffer
        || HasAttribute(field, "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
        return false;
      ITypeSymbol type = field.Type;
      if (type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer || type.IsRefLikeType)
        return false;
      if (type.TypeKind == TypeKind.Delegate) // the field of a field-like event of another assembly (FullPropertyInfo.IsEventField)
        foreach (ISymbol other in field.ContainingType.GetMembers(field.Name))
          if (other is IEventSymbol)
            return false;
      return true;
    }

    /// <summary>
    /// A framework struct whose state is its public fields (SerializationRules.IsFieldStruct: System.Numerics.Vector3, Quaternion, Matrix4x4...): a value type of the framework with a public instance field that is not readonly,
    /// not a tuple, KeyValuePair or Nullable. The runtime writes it by its public fields only, whatever the fields mode.
    /// </summary>
    private bool IsFieldStruct(INamedTypeSymbol type)
    {
      if (type.TypeKind != TypeKind.Struct || !IsFramework(type) || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T || type.IsTupleType)
        return false;
      string ns = type.ContainingNamespace?.ToDisplayString();
      if ((ns == "System" && (type.Name == "ValueTuple" || type.Name == "Tuple")) || (ns == "System.Collections.Generic" && type.Name == "KeyValuePair"))
        return false;
      foreach (ISymbol member in type.GetMembers())
        if (member is IFieldSymbol field && !field.IsStatic && !field.IsConst && field.DeclaredAccessibility == Accessibility.Public && !field.IsReadOnly)
          return true;
      return false;
    }

    /// <summary>
    /// A member of a framework struct whose state is its fields (<see cref="IsFieldStruct"/>): its public fields (FullPropertyInfo.AddFieldCandidates with SerializedFields.Public, no attributes there).
    /// </summary>
    private static bool IsFieldStructMember(IFieldSymbol field)
    {
      return field.DeclaredAccessibility == Accessibility.Public && IsFieldOfState(field);
    }

    /// <summary>
    /// An attribute that opts the property or field in (as LsMsgPack.Core's IncludeAttributes decides): [JsonInclude], Json.NET's [JsonProperty], [DataMember], [Key] (MessagePack-CSharp, Nerdbank.MessagePack),
    /// [PropertyShape] (not with Ignore = true), any other attribute with "Include" in its name.
    /// </summary>
    internal static bool OptsIn(ISymbol property)
    {
      foreach (AttributeData attribute in property.GetAttributes())
      {
        INamedTypeSymbol type = attribute.AttributeClass;
        if (type is null)
          continue;
        switch (type.ToDisplayString())
        {
          case "System.Text.Json.Serialization.JsonIncludeAttribute":
          case "Newtonsoft.Json.JsonPropertyAttribute":
          case "System.Runtime.Serialization.DataMemberAttribute":
          case "MessagePack.KeyAttribute":
          case "Nerdbank.MessagePack.KeyAttribute":
            return true;
          case "PolyType.PropertyShapeAttribute":
            bool ignored = false;
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
              ignored |= argument.Key == "Ignore" && argument.Value.Value is bool ignore && ignore;
            if (!ignored)
              return true;
            break;
          default:
            if (type.Name.IndexOf("Include", System.StringComparison.Ordinal) >= 0)
              return true;
            break;
        }
      }
      return false;
    }

    /// <summary>
    /// The members the runtime sees on the class (FullPropertyInfo.GetMembers, Reflection order) as far as the generator can tell: the public instance properties without parameters
    /// (the class's own first, then those of its base classes, each in declaration order), the non-public ones of <see cref="IsNonPublicCandidate"/> (never kept by the default static filters),
    /// then the public fields and the non-public fields of <see cref="IsFieldCandidate"/> in the same order (a framework struct whose state is its fields: only its public fields).
    /// A member is left out when a candidate of the same name, property or field, is declared by a derived class ("new", an override): decided over all candidates before any is added, as the runtime does.
    /// </summary>
    private List<MemberModel> GetMembers(INamedTypeSymbol type, bool fieldStruct)
    {
      // The candidates in the runtime's order, with the depth of their class (0: the type itself)
      List<(ISymbol Member, INamedTypeSymbol Declaring, int Depth, bool PublicPass)> candidates = new List<(ISymbol, INamedTypeSymbol, int, bool)>();
      for (int pass = 0; pass < 4; pass++)
      {
        bool fieldPass = pass >= 2;
        bool publicPass = pass == 0 || pass == 2;
        if (fieldPass ? (!fieldStruct && _fields == LsMsgPackGenerator.FieldsNone) || (fieldStruct && !publicPass) : fieldStruct || (!publicPass && _nonPublic == LsMsgPackGenerator.NonPublicNone))
          continue;
        int depth = 0;
        for (INamedTypeSymbol t = type; t != null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType, depth++)
        {
          if ((!publicPass || fieldPass) && IsFramework(t) && !fieldStruct) // the runtime leaves out the framework's non-public properties and its fields
            continue;
          foreach (ISymbol member in t.GetMembers())
          {
            bool candidate;
            if (fieldPass)
              candidate = member is IFieldSymbol field && (field.DeclaredAccessibility == Accessibility.Public) == publicPass
                && (fieldStruct ? IsFieldStructMember(field) : IsFieldCandidate(field, _fields));
            else
              candidate = member is IPropertySymbol property && !property.IsStatic && !property.IsIndexer
                && (publicPass ? property.DeclaredAccessibility == Accessibility.Public : IsNonPublicCandidate(property, _nonPublic));
            if (candidate)
              candidates.Add((member, t, depth, publicPass));
          }
        }
      }

      List<MemberModel> members = new List<MemberModel>();
      for (int i = 0; i < candidates.Count; i++)
      {
        (ISymbol member, INamedTypeSymbol t, int depth, bool publicPass) = candidates[i];
        bool hidden = false;
        for (int j = 0; j < candidates.Count && !hidden; j++)
          hidden = candidates[j].Depth < depth && candidates[j].Member.Name == member.Name; // hidden by (or overridden in) a derived class
        if (hidden)
          continue;

        MemberModel model = member is IFieldSymbol field ? FieldMember(t, field, fieldStruct) : PropertyMember(t, (IPropertySymbol)member, publicPass);
        if (model != null)
          members.Add(depth > 0 ? model with { Inherited = true } : model);
      }
      return members;
    }

    private MemberModel PropertyMember(INamedTypeSymbol t, IPropertySymbol property, bool publicPass)
    {
      if (property.ReturnsByRef || property.ReturnsByRefReadonly || property.Type.IsRefLikeType || property.Type.TypeKind == TypeKind.Pointer
        || property.Type.TypeKind == TypeKind.FunctionPointer || IsObsoleteError(property) || !IsAccessible(property.Type))
        return null;

      // [DefaultValue]: the runtime's filter compares the value with the attribute's (boxed), the generated writer leaves it to the runtime
      bool hasDefaultValue = HasAttribute(property, "System.ComponentModel.DefaultValueAttribute");
      bool canGet = CanGet(t, property, out bool unsafeGet) && !hasDefaultValue;
      bool canSet = CanSet(t, property, out bool unsafeSet);
      bool unrolled = publicPass && KeptByDefault(t, property) && !HasIgnoreAttribute(property); // FilterNonSettable, FilterIgnoredAttribute
      return Member(t, property, property.Type, hasDefaultValue, canGet, canSet, unrolled, canGet && unsafeGet, unsafeSet, false);
    }

    private MemberModel FieldMember(INamedTypeSymbol t, IFieldSymbol field, bool fieldStruct)
    {
      if (IsObsoleteError(field) || !IsAccessible(field.Type))
        return null;

      bool hasDefaultValue = HasAttribute(field, "System.ComponentModel.DefaultValueAttribute");
      bool canGet = CanGet(t, field, out bool viaAccessor) && !hasDefaultValue;
      bool canSet = CanSet(t, field);
      bool unrolled = KeptByDefault(field, fieldStruct);
      return Member(t, field, field.Type, hasDefaultValue, canGet, canSet, unrolled, canGet && viaAccessor, canSet && viaAccessor, true);
    }

    private static MemberModel Member(INamedTypeSymbol t, ISymbol member, ITypeSymbol type, bool hasDefaultValue, bool canGet, bool canSet, bool unrolled, bool unsafeGet, bool unsafeSet, bool field)
    {
      ITypeSymbol valueType = type;
      bool nullable = false;
      if (valueType is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
      {
        valueType = named.TypeArguments[0];
        nullable = true;
      }
      string kind = KindOf(valueType, out string underlying);
      if (kind == "None")
        nullable = false;
      return new MemberModel(t.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), member.Name, Escape(member.Name),
        type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), kind, nullable,
        valueType.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Format), underlying, canGet, canSet, unrolled, hasDefaultValue, unsafeGet, unsafeSet,
        t.TypeKind == TypeKind.Struct, field);
    }

    /// <returns>The LsMsgPack.Generated.GeneratedKind the generated code writes and reads itself, "None" for the others</returns>
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
    /// The default FilterNonSettable keeps the property: a public setter, or no setter (computed properties and get-only collections, GetOnlyProperties.All) on a type that is not the framework's
    /// (TypeResolver.IsFrameworkAssembly) and not of a type that is never written (SerializationRules.IsNeverWritten). Not predicted: a non-public setter a constructor takes (immutable classes).
    /// A wrong guess only costs the unrolled writer.
    /// </summary>
    private static bool KeptByDefault(INamedTypeSymbol declaring, IPropertySymbol property)
    {
      if (property.SetMethod != null)
        return property.SetMethod.DeclaredAccessibility == Accessibility.Public;
      if (property.GetMethod is null || property.GetMethod.DeclaredAccessibility != Accessibility.Public || IsNeverWritten(property.Type))
        return false;
      string assembly = declaring.ContainingAssembly?.Name ?? string.Empty;
      return !(assembly == "mscorlib" || assembly == "netstandard" || assembly == "System" || assembly == "System.Private.CoreLib"
        || assembly.StartsWith("System.", System.StringComparison.Ordinal) || assembly.StartsWith("Microsoft.", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// The default static filters keep the field: SerializedFields.Attributed selects the fields with an opt-in attribute (all public fields of a framework struct whose state they are),
    /// FilterNonSettable keeps a readonly field as a property without a setter (not of a type that is never written), FilterIgnoredAttribute leaves out the ignored ones ([NonSerialized] too).
    /// A wrong guess only costs the unrolled writer.
    /// </summary>
    private static bool KeptByDefault(IFieldSymbol field, bool fieldStruct)
    {
      if ((!fieldStruct && !OptsIn(field)) || HasIgnoreAttribute(field))
        return false;
      return !field.IsReadOnly || !IsNeverWritten(field.Type);
    }

    /// <summary>
    /// SerializationRules.IsNeverWritten: System.Type, MemberInfo (and the classes deriving from them) and delegates, also as the elements of an array.
    /// </summary>
    private static bool IsNeverWritten(ITypeSymbol type)
    {
      while (type is IArrayTypeSymbol array)
        type = array.ElementType;
      if (type.TypeKind == TypeKind.Delegate)
        return true;
      for (ITypeSymbol t = type; t != null; t = t.BaseType)
      {
        string name = t.ToDisplayString();
        if (name == "System.Type" || name == "System.Reflection.MemberInfo")
          return true;
      }
      return false;
    }

    /// <summary>
    /// An attribute the default FilterIgnoredAttribute leaves the property or field out for (any attribute with "Ignore" in its name, on the member; [NonSerialized] on a field).
    /// A wrong guess only costs the unrolled writer: the runtime compares the properties it keeps.
    /// </summary>
    private static bool HasIgnoreAttribute(ISymbol property)
    {
      foreach (AttributeData attribute in property.GetAttributes())
        if (attribute.AttributeClass != null && (attribute.AttributeClass.Name.IndexOf("Ignore", System.StringComparison.Ordinal) >= 0 || attribute.AttributeClass.ToDisplayString() == "System.NonSerializedAttribute"))
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
