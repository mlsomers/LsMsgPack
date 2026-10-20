using LsMsgPack;
using LsMsgPack.Meta;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LsMsgPackTypeScript
{
  /// <summary>
  /// The .NET types the TypeScript models describe: the classes and structs (with their base classes), interfaces and enums of the user's assemblies that the roots reach,
  /// through the serialized properties (the static filters and <see cref="PropertyOrder"/> of the server's settings), the elements of collections, dictionary values, pairs, tuples and nullables,
  /// and the classes deriving from a reached class or interface (a type id names them). The framework's types are kinds (<see cref="Spec"/>), never models.
  /// </summary>
  internal sealed class ModelSet
  {
    private readonly MsgPackOptions _settings;
    private readonly List<Type> _candidates;
    private readonly HashSet<Type> _visited = new HashSet<Type>();
    private readonly Dictionary<Type, ModelClass> _classes = new Dictionary<Type, ModelClass>();
    private readonly List<ModelClass> _order = new List<ModelClass>();
    private readonly List<ModelInterface> _interfaces = new List<ModelInterface>();
    private readonly List<Type> _enums = new List<Type>();
    private readonly HashSet<string> _tsNames = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _enumNames = new Dictionary<Type, string>();

    /// <param name="settings">The settings of the server's application/x-lsmsgpack (its static filters, property order and type names)</param>
    /// <param name="candidates">The types a derived class is looked for in (the user's assemblies)</param>
    internal ModelSet(MsgPackOptions settings, List<Type> candidates)
    {
      _settings = settings;
      _candidates = candidates.Where(t => !t.ContainsGenericParameters && t.FullName != null && t.Name.IndexOf('<') < 0).OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
    }

    internal MsgPackOptions Settings { get { return _settings; } }

    internal bool FullTypeNames { get { return (_settings.AddTypeIdOptions & AddTypeIdOption.FullName) != 0; } }

    /// <summary>
    /// The classes, base classes before the classes deriving from them (a JavaScript class must exist before one extends it), otherwise in the order they were reached.
    /// </summary>
    internal List<ModelClass> Classes
    {
      get
      {
        List<ModelClass> sorted = new List<ModelClass>(_order.Count);
        HashSet<ModelClass> done = new HashSet<ModelClass>();
        foreach (ModelClass model in _order)
          AddWithBases(model, sorted, done);
        return sorted;
      }
    }

    private static void AddWithBases(ModelClass model, List<ModelClass> sorted, HashSet<ModelClass> done)
    {
      if (model.Base != null)
        AddWithBases(model.Base, sorted, done);
      if (done.Add(model))
        sorted.Add(model);
    }

    internal List<ModelInterface> Interfaces { get { return _interfaces; } }

    internal List<Type> Enums { get { return _enums; } }

    internal string EnumName(Type type) { return _enumNames[type]; }

    internal ModelClass ClassOf(Type type)
    {
      return _classes.TryGetValue(type, out ModelClass model) ? model : null;
    }

    internal void Add(Type root)
    {
      Visit(root, 0);
    }

    private void Visit(Type type, int depth)
    {
      if (type is null || type.IsGenericParameter || type.ContainsGenericParameters || type.IsPointer || type.IsByRef || depth > 64)
        return;
      type = Nullable.GetUnderlyingType(type) ?? type;
      if (type.IsArray)
      {
        Visit(type.GetElementType(), depth + 1);
        return;
      }
      if (type.IsEnum)
      {
        if (IsUsers(type) && _visited.Add(type))
        {
          _enums.Add(type);
          _enumNames.Add(type, UniqueTsName(type));
        }
        return;
      }
      if (type.IsPrimitive || type == typeof(string) || type == typeof(object) || type == typeof(decimal))
        return;
      if (FrameworkTypeInfo.IsKeyValuePair(type) || FrameworkTypeInfo.IsTuple(type))
      {
        foreach (Type arg in type.GetGenericArguments())
          Visit(arg, depth + 1);
        return;
      }
      if (_settings.HasCustomExtension(type) || FrameworkTypeInfo.GetPlainForm(type) != PlainForm.None)
        return;
      if (typeof(IEnumerable).IsAssignableFrom(type))
      {
        if (!_visited.Add(type))
          return;
        CollectionInfo info = CollectionInfo.Get(type);
        if (info.IsDictionary || !(info.PairKey is null))
        {
          Visit(info.PairKey?.PropertyType ?? info.KeyType, depth + 1);
          Visit(info.PairValue?.PropertyType ?? info.ValueType, depth + 1);
        }
        else
          Visit(info.ElementType, depth + 1);
        return;
      }
      if (!DescribedSchema.IsObject(type, _settings) || !IsUsers(type) || !_visited.Add(type))
        return;

      if (type.IsInterface)
      {
        ModelInterface model = new ModelInterface(type, UniqueTsName(type));
        _interfaces.Add(model);
        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod != null))
        {
          model.Props.Add(new ModelProperty(prop));
          Visit(prop.PropertyType, depth + 1);
        }
      }
      else
      {
        ModelClass model = new ModelClass(type, UniqueTsName(type), TypeResolver.GetTypeName(type, FullTypeNames));
        _classes.Add(type, model);
        _order.Add(model);
        if (type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType))
        {
          Visit(type.BaseType, depth + 1);
          model.Base = ClassOf(type.BaseType);
        }
        FullPropertyInfo[] props = FullPropertyInfo.GetStaticallyIncludedProps(type, _settings);
        HashSet<string> inherited = model.Base is null ? new HashSet<string>() : new HashSet<string>(model.Base.AllProps.Select(p => p.Name), StringComparer.Ordinal);
        foreach (FullPropertyInfo prop in props)
        {
          ModelProperty property = new ModelProperty(prop.PropertyInfo);
          model.AllProps.Add(property);
          if (!inherited.Contains(property.Name))
            model.OwnProps.Add(property);
        }
        foreach (FullPropertyInfo prop in props)
          Visit(prop.PropertyInfo.PropertyType, depth + 1);
      }

      if (!type.IsSealed && !type.IsValueType)
        foreach (Type derived in _candidates.Where(c => c != type && type.IsAssignableFrom(c) && !c.IsInterface))
          Visit(derived, depth + 1);
    }

    private static bool IsUsers(Type type)
    {
      return ModelAssemblies.IsUsers(type.Assembly) && !TypeResolver.IsFrameworkAssembly(type.Assembly);
    }

    /// <summary>
    /// A TypeScript identifier for the type: its name (generic arguments joined with _), with a number when another type has it.
    /// </summary>
    private string UniqueTsName(Type type)
    {
      string name = Identifier(type);
      string unique = name;
      for (int t = 2; !_tsNames.Add(unique) || TypeScriptWriter.IsReserved(unique); t++)
        unique = string.Concat(name, "_", t.ToString(System.Globalization.CultureInfo.InvariantCulture));
      return unique;
    }

    private static string Identifier(Type type)
    {
      string name = type.Name;
      int tick = name.IndexOf('`');
      if (tick >= 0)
        name = name.Substring(0, tick);
      if (type.IsGenericType)
        name = string.Concat(name, "_", string.Join("_", type.GetGenericArguments().Select(Identifier)));
      if (type.IsArray)
        name = string.Concat(Identifier(type.GetElementType()), "Array");
      char[] chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '$' ? c : '_').ToArray();
      name = new string(chars);
      return char.IsDigit(name[0]) ? "_" + name : name;
    }

    /// <summary>
    /// The declared type of a value as the described schema has it (DescribedSchema.WriteType, in its order), with the models for its classes.
    /// </summary>
    internal Spec SpecOf(Type type)
    {
      Type nullable = Nullable.GetUnderlyingType(type);
      if (nullable != null)
        return Spec.Of("?", type, SpecOf(nullable));
      if (type == typeof(decimal))
        return Spec.Kind("decimal", type);
      if (type.IsArray && type.GetArrayRank() > 1)
        return Spec.Kind("any", type);
      if (type == typeof(bool))
        return Spec.Kind("bool", type);
      if (type == typeof(sbyte) || type == typeof(short) || type == typeof(int) || type == typeof(byte) || type == typeof(ushort) || type == typeof(uint))
        return Spec.Kind("int", type);
      if (type == typeof(long))
        return Spec.Kind("long", type);
      if (type == typeof(ulong))
        return Spec.Kind("ulong", type);
      if (type == typeof(float) || type == typeof(double))
        return Spec.Kind("float", type);
      if (type == typeof(string))
        return Spec.Kind("string", type);
      if (type == typeof(byte[]) || type == typeof(sbyte[]))
        return Spec.Kind("bin", type);
      if (type == typeof(Guid))
        return Spec.Kind("guid", type);
      if (type.IsArray && !type.GetElementType().IsValueType)
        return Spec.Of("list", type, SpecOf(type.GetElementType()));
      if (type == typeof(DateTime))
        return Spec.Kind("datetime", type);
      if (type == typeof(DateTimeOffset))
        return Spec.Kind(_settings.DateTimeOffsetFormat == DateTimeOffsetFormat.ClockTimeAndOffset ? "dto-clock" : "dto", type);
      if (_settings.HasCustomExtension(type))
        return Spec.Kind("any", type);

      switch (FrameworkTypeInfo.GetPlainForm(type))
      {
        case PlainForm.None: break;
        case PlainForm.Half: return Spec.Kind("float", type);
        case PlainForm.Text: return Spec.Kind("string", type);
        case PlainForm.Rune: return Spec.Kind("int", type);
        case PlainForm.NInt: return Spec.Kind("long", type);
        case PlainForm.NUInt: return Spec.Kind("ulong", type);
        case PlainForm.Binary: return Spec.Kind("bin", type);
        case PlainForm.Complex: return Spec.Of("tuple", type, Spec.Kind("float", typeof(double)), Spec.Kind("float", typeof(double)));
        case PlainForm.BigInteger: return Spec.Kind("bigint", type);
        default: return Spec.Kind("any", type);
      }

      if (type.IsEnum)
      {
        Spec underlying = SpecOf(Enum.GetUnderlyingType(type));
        underlying.Enum = type;
        underlying.Type = type;
        return underlying;
      }
      if (FrameworkTypeInfo.IsKeyValuePair(type))
        return Spec.Of("pair", type, type.GetGenericArguments().Select(SpecOf).ToArray());
      if (FrameworkTypeInfo.IsTuple(type))
        return Spec.Of("tuple", type, type.GetGenericArguments().Select(SpecOf).ToArray());
      if (typeof(IEnumerable).IsAssignableFrom(type))
      {
        CollectionInfo info = CollectionInfo.Get(type);
        if (info.IsDictionary || !(info.PairKey is null) || typeof(IDictionary).IsAssignableFrom(type))
          return Spec.Of("dict", type, SpecOf(info.PairKey?.PropertyType ?? info.KeyType ?? typeof(object)), SpecOf(info.PairValue?.PropertyType ?? info.ValueType ?? typeof(object)));
        return Spec.Of("list", type, SpecOf((type.IsArray ? type.GetElementType() : info.ElementType) ?? typeof(object)));
      }
      if (type == typeof(char))
        return Spec.Kind("char", type);
      if (type == typeof(TimeSpan))
        return Spec.Kind("timespan", type);
      if (typeof(Uri).IsAssignableFrom(type))
        return Spec.Kind("uri", type);
      if (type == typeof(DateOnly))
        return Spec.Kind("dateonly", type);
      if (type == typeof(TimeOnly))
        return Spec.Kind("timeonly", type);

      ModelClass model = ClassOf(type);
      if (model != null && !type.IsInterface)
        return new Spec() { Name = type.IsValueType ? "struct" : "class", Type = type, Class = model };
      if (type.IsInterface && _interfaces.Any(i => i.Type == type))
        return new Spec() { Name = "any", Type = type, Interface = _interfaces.First(i => i.Type == type) };
      return Spec.Kind("any", type);
    }
  }

  internal sealed class ModelClass
  {
    internal readonly Type Type;
    internal readonly string TsName;

    /// <summary>
    /// The type id the server writes for it (the name of its schema entry): the short name, or the full name with AddTypeIdOption.FullName.
    /// </summary>
    internal readonly string TypeName;
    internal ModelClass Base;

    /// <summary>
    /// The serialized properties, in the order of the server's settings (inherited ones too).
    /// </summary>
    internal readonly List<ModelProperty> AllProps = new List<ModelProperty>();

    /// <summary>
    /// The ones the base model does not have.
    /// </summary>
    internal readonly List<ModelProperty> OwnProps = new List<ModelProperty>();

    internal ModelClass(Type type, string tsName, string typeName)
    {
      Type = type;
      TsName = tsName;
      TypeName = typeName;
    }

    internal bool IsAbstract { get { return Type.IsAbstract; } }
  }

  internal sealed class ModelInterface
  {
    internal readonly Type Type;
    internal readonly string TsName;
    internal readonly List<ModelProperty> Props = new List<ModelProperty>();

    internal ModelInterface(Type type, string tsName)
    {
      Type = type;
      TsName = tsName;
    }
  }

  internal sealed class ModelProperty
  {
    private static readonly NullabilityInfoContext NullabilityContext = new NullabilityInfoContext();

    internal readonly string Name;
    internal readonly Type Type;

    /// <summary>
    /// The nullable annotations of the property (string? or string), Unknown without them (null when they cannot be read).
    /// </summary>
    internal readonly NullabilityInfo Nullability;

    internal ModelProperty(PropertyInfo property)
    {
      Name = property.Name;
      Type = property.PropertyType;
      try
      {
        lock (NullabilityContext) // not thread-safe
          Nullability = NullabilityContext.Create(property);
      }
      catch (Exception)
      {
        Nullability = null;
      }
    }
  }

  /// <summary>
  /// A declared type: a kind (<c>string</c>, <c>int</c>, <c>datetime</c>... <c>any</c>), or <c>?</c>, <c>list</c>, <c>dict</c>, <c>pair</c>, <c>tuple</c> with their items, or <c>class</c>/<c>struct</c> of a model.
  /// </summary>
  internal sealed class Spec
  {
    internal string Name;
    internal Type Type;
    internal Spec[] Items = Array.Empty<Spec>();
    internal ModelClass Class;
    internal ModelInterface Interface;

    /// <summary>
    /// An enum (the kind is its underlying integer).
    /// </summary>
    internal Type Enum;

    internal static Spec Kind(string kind, Type type)
    {
      return new Spec() { Name = kind, Type = type };
    }

    internal static Spec Of(string name, Type type, params Spec[] items)
    {
      return new Spec() { Name = name, Type = type, Items = items };
    }
  }
}
