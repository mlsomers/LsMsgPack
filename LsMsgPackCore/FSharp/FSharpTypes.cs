using LsMsgPack.Converters;
using LsMsgPack.Meta;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace LsMsgPack.FSharp
{
  /// <summary>
  /// The types F# compiles (docs/FSharp.md), recognized by the full names of FSharp.Core's types and of its [CompilationMapping] attribute, so nothing references or loads FSharp.Core:
  /// <list type="bullet">
  /// <item>options and value options: written as their value, None as nil (<see cref="FSharpOptionConverter{TOption, T}"/>);</item>
  /// <item>unions: a case with fields as an object of its fields with the case name as the type id, a fieldless case as its name, a union of one case with one field as that field
  /// (<see cref="FSharpUnion"/>);</item>
  /// <item>lists and maps: filled by FSharp.Core's factories (CollectionInfo); records need nothing (a constructor taking the fields).</item>
  /// </list>
  /// Reflection only, as System.Text.Json's F# support: the source generator does not run in F# projects.
  /// </summary>
  internal static class FSharpTypes
  {
    internal const string CompilationMappingAttribute = "Microsoft.FSharp.Core.CompilationMappingAttribute";
    internal const string OptionDefinition = "Microsoft.FSharp.Core.FSharpOption`1";
    internal const string ValueOptionDefinition = "Microsoft.FSharp.Core.FSharpValueOption`1";
    internal const string ListDefinition = "Microsoft.FSharp.Collections.FSharpList`1";
    internal const string MapDefinition = "Microsoft.FSharp.Collections.FSharpMap`2";
    internal const string SetDefinition = "Microsoft.FSharp.Collections.FSharpSet`1";

    // SourceConstructFlags of FSharp.Core
    internal const int SumType = 1;
    internal const int RecordType = 2;
    internal const int Field = 4;
    internal const int UnionCase = 8;
    private const int KindMask = 31;

    private static readonly ConcurrentDictionary<Type, FSharpUnion> Unions = new ConcurrentDictionary<Type, FSharpUnion>();

    /// <summary>
    /// The union a type is, or whose case class it is (null for any other type, cached per type). Not FSharp.Core's options and collections (written their own way).
    /// </summary>
    internal static FSharpUnion UnionOf(Type type)
    {
      if (type is null || type.IsPrimitive || type == typeof(string) || type.IsArray || type.IsEnum || type.IsInterface || type == typeof(object))
        return null;
      if (!Unions.TryGetValue(type, out FSharpUnion found))
        found = Unions.GetOrAdd(type, Find);
      return found.Type is null ? null : found;
    }

    private static FSharpUnion Find(Type type)
    {
      if (IsUnionType(type))
        return FSharpUnion.Create(type);
      Type baseType = type.BaseType;
      if (baseType != null && baseType != typeof(object) && IsUnionType(baseType))
      {
        FSharpUnion union = UnionOf(baseType);
        if (union != null && union.CaseOfClass(type) != null)
          return union;
      }
      return FSharpUnion.NotAUnion;
    }

    /// <summary>
    /// A type with [CompilationMapping(SourceConstructFlags.SumType)], other than FSharp.Core's options (<see cref="IsOption"/>) and collections.
    /// </summary>
    private static bool IsUnionType(Type type)
    {
      if (!HasMapping(type, SumType))
        return false;
      if (!type.IsGenericType)
        return true;
      string definition = type.GetGenericTypeDefinition().FullName;
      return definition != OptionDefinition && definition != ValueOptionDefinition && definition != ListDefinition;
    }

    /// <summary>
    /// <c>'T option</c> (a class: None is null) or <c>'T voption</c> (a struct).
    /// </summary>
    internal static bool IsOption(Type type, out bool valueOption)
    {
      valueOption = false;
      if (!type.IsGenericType || type.IsGenericTypeDefinition)
        return false;
      string definition = type.GetGenericTypeDefinition().FullName;
      valueOption = definition == ValueOptionDefinition;
      return valueOption || definition == OptionDefinition;
    }

    /// <summary>
    /// F#'s own definition of a generic type (list, map, set): by full name, from FSharp.Core.
    /// </summary>
    internal static bool IsDefinition(Type type, string definition)
    {
      return type.IsGenericType && !type.IsGenericTypeDefinition && type.GetGenericTypeDefinition().FullName == definition;
    }

    /// <summary>
    /// The converter of an option, of a union whose values of exactly this type are a name or a single field, or of the class of a fieldless case (ConverterLookup asks after [MsgPackConverter]).
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = TrimReasons.FSharp)]
    internal static MsgPackConverter ConverterFor(Type type)
    {
      if (type.IsArray || type.IsPrimitive || type.IsEnum || type.IsInterface || type.IsAbstract || type.ContainsGenericParameters)
        return null;
      if (IsOption(type, out bool valueOption))
        return FSharpOptionConverters.Create(type, valueOption);
      FSharpUnion union = UnionOf(type);
      if (union is null)
        return null;
      if (type != union.Type) // a case class
      {
        UnionCase ofClass = union.CaseOfClass(type);
        return ofClass.Fields.Length == 0 ? FSharpCaseNameConverters.Create(type, union) : null;
      }
      if (union.Cases.Length == 1)
      {
        UnionCase single = union.Cases[0];
        if (single.Fields.Length == 1)
          return FSharpSingleCaseConverters.Create(type, single);
        return single.Fields.Length == 0 ? FSharpCaseNameConverters.Create(type, union) : null; // several fields: an object of them
      }
      if (union.ValuesAreNames)
        return FSharpCaseNameConverters.Create(type, union);
      return type.IsValueType ? FSharpAccess.Create(typeof(FSharpUnsupportedConverter<>), new[] { type }) : null;
    }

    /// <summary>
    /// The members written for a union type or a case class: the fields of its case, in their order (null for other types). A union type whose values are names or another case's class has none.
    /// </summary>
    internal static MemberInfo[] MembersOf(Type type)
    {
      FSharpUnion union = UnionOf(type);
      if (union is null)
        return null;
      UnionCase ofType = union.CaseOfClass(type);
      return ofType is null ? new MemberInfo[0] : ofType.Fields;
    }

    /// <summary>
    /// The class of a fieldless case of a union with fields (<c>Shape+_Empty</c>): written as the case's name, never with a type id (SerializationRules.IsHiddenFrameworkType).
    /// </summary>
    internal static bool IsFieldlessCaseClass(Type type)
    {
      if (type.IsVisible && !type.IsNested)
        return false;
      FSharpUnion union = UnionOf(type);
      if (union is null || type == union.Type)
        return false;
      UnionCase ofClass = union.CaseOfClass(type);
      return ofClass != null && ofClass.Fields.Length == 0;
    }

    /// <summary>
    /// The class of a case with fields of a union of several cases (<c>Shape+Circle</c>): its type id names the case, so it is written also with <see cref="AddTypeIdOption.Never"/>
    /// (plain MsgPack, MsgPackMediaTypes.ToPlain), as System.Text.Json writes the "$type" of a union.
    /// </summary>
    internal static bool IsCaseWithFields(Type type)
    {
      if (!type.IsNested || type.IsValueType)
        return false;
      FSharpUnion union = UnionOf(type);
      if (union is null || type == union.Type)
        return false;
      UnionCase ofClass = union.CaseOfClass(type);
      return ofClass != null && ofClass.Fields.Length != 0;
    }

    /// <summary>
    /// The class of the case a type id names, looked up among the cases of the declared union (a case name is not unique across unions: Ok, Error, Empty). Null when the declared type is no union
    /// or has no case class of that name.
    /// </summary>
    internal static Type CaseClassNamed(string name, Type declared)
    {
      FSharpUnion union = UnionOf(declared);
      if (union is null)
        return null;
      for (int t = 0; t < union.Cases.Length; t++)
      {
        UnionCase unionCase = union.Cases[t];
        if (unionCase.Name == name && unionCase.Fields.Length != 0 && unionCase.Class != union.Type)
          return unionCase.Class;
      }
      return null;
    }

    /// <summary>
    /// The type id of a case class: its case name (the nested class of a generic union would otherwise be named <c>Gen&lt;Int32&gt;+GA</c>). Null for other types.
    /// </summary>
    internal static string CaseNameOf(Type type)
    {
      if (!type.IsNested)
        return null;
      FSharpUnion union = UnionOf(type);
      if (union is null || type == union.Type)
        return null;
      return union.CaseOfClass(type)?.Name;
    }

    /// <summary>
    /// The constructor of a case class with fields, or of a union of one case with several fields: its parameters are the fields in their order (internal, as F# compiles it). Null for other types.
    /// </summary>
    internal static ConstructorInfo CaseConstructor(Type type, out PropertyInfo[] fields)
    {
      fields = null;
      FSharpUnion union = UnionOf(type);
      UnionCase ofClass = union?.CaseOfClass(type);
      if (ofClass is null || ofClass.Constructor is null)
        return null;
      fields = ofClass.Fields;
      return ofClass.Constructor;
    }

    /// <summary>
    /// Whether a member has [CompilationMapping] of this kind, and its numbers (the case and the field of a union field, the position of a record field).
    /// </summary>
    internal static bool HasMapping(MemberInfo member, int kind)
    {
      return TryGetMapping(member, out int flags, out _, out _) && (flags & KindMask) == kind;
    }

    internal static bool TryGetMapping(MemberInfo member, out int flags, out int first, out int second)
    {
      flags = 0;
      first = -1;
      second = -1;
      IList<CustomAttributeData> attributes;
      try
      {
        attributes = member.GetCustomAttributesData();
      }
      catch (Exception) // an attribute of an assembly that cannot be loaded
      {
        return false;
      }
      for (int t = 0; t < attributes.Count; t++)
      {
        CustomAttributeData attribute = attributes[t];
        if (attribute.AttributeType.FullName != CompilationMappingAttribute)
          continue;
        IList<CustomAttributeTypedArgument> arguments = attribute.ConstructorArguments;
        if (arguments.Count == 0 || !(arguments[0].Value is int kind))
          return false;
        flags = kind;
        if (arguments.Count > 1 && arguments[1].Value is int a)
          first = a;
        if (arguments.Count > 2 && arguments[2].Value is int b)
          second = b;
        return true;
      }
      return false;
    }
  }

  /// <summary>
  /// The cases of an F# union, by tag (FSharp.Core's FSharpType.GetUnionCases finds them the same way: [CompilationMapping(UnionCase, tag)] on the factories <c>NewX</c> and the getters <c>get_X</c>
  /// of fieldless cases, [CompilationMapping(Field, tag, position)] on the fields).
  /// <para>How F# compiles them: a reference union with fields has a nested class per case with fields (<c>Shape+Circle</c>), the fieldless cases are instances of the union type itself or of a
  /// nested class <c>_X</c>; a union of fieldless cases, a union of one case and a struct union have their fields (if any) on the union type.</para>
  /// </summary>
  internal sealed class FSharpUnion
  {
    internal static readonly FSharpUnion NotAUnion = new FSharpUnion();

    internal readonly Type Type;

    internal readonly UnionCase[] Cases;

    /// <summary>
    /// The values of exactly <see cref="Type"/> are fieldless cases (written as their names): a reference union whose cases with fields have classes of their own, or a union of fieldless cases.
    /// </summary>
    internal readonly bool ValuesAreNames;

    /// <summary>
    /// The getter of <c>Tag</c>: the case of a value.
    /// </summary>
    internal readonly MethodInfo TagGetter;

    private FSharpUnion()
    {
    }

    private FSharpUnion(Type type, UnionCase[] cases, MethodInfo tagGetter)
    {
      Type = type;
      Cases = cases;
      TagGetter = tagGetter;
      bool valuesAreNames = true;
      for (int t = 0; t < cases.Length; t++)
      {
        if (cases[t].Fields.Length != 0 && cases[t].Class == type)
          valuesAreNames = false;
      }
      ValuesAreNames = valuesAreNames;
    }

    /// <summary>
    /// The case whose values have exactly this class (null for none): the class of a case, or the union type itself for a single case or a case with fields on it.
    /// The union type of a union with several cases has no case of its own (its values are names or of struct unions any case).
    /// </summary>
    internal UnionCase CaseOfClass(Type type)
    {
      if (type == Type)
      {
        if (Cases.Length == 1)
          return Cases[0];
        return null;
      }
      for (int t = 0; t < Cases.Length; t++)
      {
        if (Cases[t].Class == type)
          return Cases[t];
      }
      return null;
    }

    /// <summary>
    /// The case with this name (exactly, then ignoring case as enum names are read), null for none.
    /// </summary>
    internal UnionCase Named(string name)
    {
      for (int t = 0; t < Cases.Length; t++)
      {
        if (string.Equals(Cases[t].Name, name, StringComparison.Ordinal))
          return Cases[t];
      }
      UnionCase found = null;
      for (int t = 0; t < Cases.Length; t++)
      {
        if (string.Equals(Cases[t].Name, name, StringComparison.OrdinalIgnoreCase))
        {
          if (found != null)
            return null; // two cases differ only in case
          found = Cases[t];
        }
      }
      return found;
    }

    /// <summary>
    /// The value of the fieldless case with this name.
    /// </summary>
    /// <exception cref="MsgPackException">No fieldless case has the name</exception>
    internal object FieldlessCaseNamed(string name)
    {
      UnionCase found = Named(name);
      if (found is null)
        throw new MsgPackException($"\"{name}\" is not a case of the F# union {Type.FullName}.");
      if (found.Fields.Length != 0)
        throw new MsgPackException($"The case {found.Name} of the F# union {Type.FullName} has fields: it is written as an object with its fields, not as its name \"{name}\".");
      return found.Value;
    }

    /// <summary>
    /// The name of the case of a value (by its Tag).
    /// </summary>
    internal string NameOf(object value)
    {
      int tag = (int)TagGetter.Invoke(value, null);
      return Cases[tag].Name;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = TrimReasons.FSharp)]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = TrimReasons.FSharp)]
    internal static FSharpUnion Create(Type type)
    {
      const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
      const BindingFlags Instances = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
      SortedDictionary<int, UnionCase> byTag = new SortedDictionary<int, UnionCase>();
      MethodInfo[] methods = type.GetMethods(Statics);
      for (int t = 0; t < methods.Length; t++)
      {
        MethodInfo method = methods[t];
        if (!FSharpTypes.TryGetMapping(method, out int flags, out int tag, out _) || (flags & 31) != FSharpTypes.UnionCase || tag < 0 || byTag.ContainsKey(tag))
          continue;
        bool fieldless = method.Name.StartsWith("get_", StringComparison.Ordinal) && method.GetParameters().Length == 0;
        string name = fieldless ? method.Name.Substring(4) : method.Name.StartsWith("New", StringComparison.Ordinal) && method.Name.Length > 3 ? method.Name.Substring(3) : method.Name;
        byTag.Add(tag, new UnionCase(name, tag, fieldless ? null : method, fieldless ? method : null));
      }
      if (byTag.Count == 0)
        return NotAUnion;

      // The classes of the cases: nested classes deriving from the union (of a generic union: nested in its definition, closed with its arguments)
      Dictionary<string, Type> nested = new Dictionary<string, Type>(StringComparer.Ordinal);
      Type[] nestedTypes = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
      for (int t = 0; t < nestedTypes.Length; t++)
      {
        Type candidate = nestedTypes[t];
        if (candidate.IsGenericTypeDefinition && type.IsGenericType)
        {
          try
          {
            candidate = candidate.MakeGenericType(type.GenericTypeArguments);
          }
          catch (ArgumentException)
          {
            continue;
          }
        }
        if (candidate.BaseType == type && !nested.ContainsKey(candidate.Name))
          nested.Add(candidate.Name, candidate);
      }

      UnionCase[] cases = new UnionCase[byTag.Count];
      int count = 0;
      foreach (UnionCase unionCase in byTag.Values)
      {
        if (unionCase.Tag != count)
          return NotAUnion; // not as F# compiles unions
        Type caseClass;
        if (!nested.TryGetValue(unionCase.Name, out caseClass) && !nested.TryGetValue("_" + unionCase.Name, out caseClass))
          caseClass = type;
        unionCase.SetClass(caseClass, FieldsOf(caseClass, unionCase.Tag, Instances), caseClass != type || byTag.Count == 1);
        cases[count++] = unionCase;
      }
      MethodInfo tagGetter = type.GetProperty("Tag", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
      if (tagGetter is null || tagGetter.ReturnType != typeof(int))
        return NotAUnion;
      return new FSharpUnion(type, cases, tagGetter);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.FSharp)]
    private static PropertyInfo[] FieldsOf(Type caseClass, int tag, BindingFlags flags)
    {
      SortedDictionary<int, PropertyInfo> byPosition = new SortedDictionary<int, PropertyInfo>();
      PropertyInfo[] properties = caseClass.GetProperties(flags);
      for (int t = 0; t < properties.Length; t++)
      {
        if (FSharpTypes.TryGetMapping(properties[t], out int flags2, out int caseTag, out int position) && (flags2 & 31) == FSharpTypes.Field && caseTag == tag && position >= 0 && !byPosition.ContainsKey(position))
          byPosition.Add(position, properties[t]);
      }
      PropertyInfo[] fields = new PropertyInfo[byPosition.Count];
      byPosition.Values.CopyTo(fields, 0);
      return fields;
    }
  }

  /// <summary>
  /// A case of an F# union.
  /// </summary>
  internal sealed class UnionCase
  {
    internal readonly string Name;
    internal readonly int Tag;

    /// <summary>
    /// The static <c>NewX</c> of a case with fields (F#'s own factory), null for a fieldless case.
    /// </summary>
    internal readonly MethodInfo Factory;

    /// <summary>
    /// The static getter of a fieldless case's value, null for a case with fields.
    /// </summary>
    private readonly MethodInfo _value;

    /// <summary>
    /// The fields in their order: properties of <see cref="Class"/>.
    /// </summary>
    internal PropertyInfo[] Fields { get; private set; }

    /// <summary>
    /// The class of its values: a nested class of the union, or the union type itself.
    /// </summary>
    internal Type Class { get; private set; }

    /// <summary>
    /// The constructor of <see cref="Class"/> taking the fields in their order (a case with fields that has a class of its own, or the case of a union of one case), otherwise null.
    /// </summary>
    internal ConstructorInfo Constructor { get; private set; }

    private object _singleton;
    private bool _hasSingleton;

    internal UnionCase(string name, int tag, MethodInfo factory, MethodInfo value)
    {
      Name = name;
      Tag = tag;
      Factory = factory;
      _value = value;
    }

    /// <param name="ownConstructor">The class is the case's own (or the union has one case): a constructor of it taking the fields is the case's (a struct union of several cases has a
    /// constructor taking the tag, which a case of one int field would match)</param>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.FSharp)]
    internal void SetClass(Type caseClass, PropertyInfo[] fields, bool ownConstructor)
    {
      Class = caseClass;
      Fields = fields;
      if (fields.Length == 0 || !ownConstructor)
        return;
      ConstructorInfo[] constructors = caseClass.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      for (int t = 0; t < constructors.Length; t++)
      {
        ParameterInfo[] parameters = constructors[t].GetParameters();
        if (parameters.Length != fields.Length)
          continue;
        bool same = true;
        for (int p = 0; p < parameters.Length && same; p++)
          same = parameters[p].ParameterType == fields[p].PropertyType;
        if (same)
        {
          Constructor = constructors[t];
          return;
        }
      }
    }

    /// <summary>
    /// The value of a fieldless case (F# keeps one instance).
    /// </summary>
    internal object Value
    {
      get
      {
        if (!_hasSingleton)
        {
          _singleton = _value?.Invoke(null, null);
          _hasSingleton = true;
        }
        return _singleton;
      }
    }

    /// <summary>
    /// The static getter of a fieldless case's value, null for a case with fields.
    /// </summary>
    internal MethodInfo ValueGetter { get { return _value; } }
  }
}
