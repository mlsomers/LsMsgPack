#if KEEPTRACK
using LsMsgPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using static LsMsgPack.MsgPackValidation;

namespace ObjectDebugger
{
  /// <summary>
  /// Validates what <see cref="MsgPackValidation"/> can not see in a single item: the indexed schema, the type ids of the body, the shapes of
  /// framework types that have a type id, and the places where the data picks the type that a reader creates
  /// (<see cref="ValidationSeverity.SecurityVulnerability"/>, see docs/security.md).
  /// <para>A type id is only written where the type of the value differs from the declared type. A value that can not have a derived type
  /// (DateTime, Guid, an enum...) or a framework type with a type id, and a place holding values of different kinds, reveal a property
  /// declared as object (or as an interface): a reader creates whatever type the data names there.</para>
  /// </summary>
  public static class PayloadValidation
  {
    private const string SecurityAdvice = " The data can make a reader create any type it can resolve in such a place, including types that run code when they are created or filled (see docs/security.md). Declare the type you expect, or set MsgPackOptions.TypeGuard (e.g. an AllowedTypesGuard).";

    /// <param name="root">The reconstructed objects (<see cref="RootObject.Reconstruct(MsgPackItem, SchemaStore)"/>), with the item tree kept (KEEPTRACK and PreservePackages) so the issues refer to their items</param>
    /// <returns>The issues, each with the <see cref="ValidationItem.Item"/> it is about (the item of the payload when there is no better one)</returns>
    public static List<ValidationItem> Validate(RootObject root)
    {
      List<ValidationItem> issues = new List<ValidationItem>();
      if (root is null)
        return issues;

      if (root.Kind == ObjectKind.Sequence)
      {
        for (int t = 0; t < root.Members.Count; t++)
          if (root.Members[t] is RootObject payload)
            ValidatePayload(payload, issues);
      }
      else
        ValidatePayload(root, issues);
      return issues;
    }

    private static void ValidatePayload(RootObject payload, List<ValidationItem> issues)
    {
      MsgPackItem payloadItem = payload.FirstItemRef ?? payload.LastItemRef;
      if (payload.Schema != null)
        ValidateSchema(payload.Schema, payload.Schema.ItemRef ?? payloadItem, issues);
      for (int t = 0; t < payload.Warnings.Count; t++)
        issues.Add(new ValidationItem(payload.Schema?.ItemRef ?? payloadItem, ValidationSeverity.Comment, 0, payload.Warnings[t]));

      new BodyValidator(payload.Schema, issues).Validate(payload);
    }

    #region Schema

    private static void ValidateSchema(SchemaInfo schema, MsgPackItem schemaItem, List<ValidationItem> issues)
    {
      if (!schema.IsAvailable)
      {
        issues.Add(new ValidationItem(schemaItem, ValidationSeverity.Comment, 0, "The body refers to schema ", schema.Id,
          ", which is not in the data (WriteSchemaReference): a reader needs it in its SchemaStore. Without it the names of the types and properties are unknown, and the body is not checked against the schema."));
        return;
      }

      Dictionary<string, SchemaType> byName = new Dictionary<string, SchemaType>(StringComparer.Ordinal);
      for (int t = 0; t < schema.Types.Count; t++)
      {
        SchemaType type = schema.Types[t];
        MsgPackItem at = type.NameItemRef ?? schemaItem;
        if (string.IsNullOrEmpty(type.Name))
        {
          issues.Add(new ValidationItem(at, ValidationSeverity.Error, 0, "Type #", type.Index, " of the schema has no name: a reader can not resolve it."));
        }
        else
        {
          SchemaType first;
          if (byName.TryGetValue(type.Name, out first))
          {
            issues.Add(new ValidationItem(at, ValidationSeverity.Warning, 0, "The schema has the type name \"", type.Name, "\" twice (types #", first.Index, " and #", type.Index,
              "): the writer had two classes with this name (short type names of different namespaces, AddTypeIdOption.FullName writes full names). A reader resolves both to the same class, or can not choose between them."));
          }
          else
            byName.Add(type.Name, type);
          CheckTypeName(type.Name, at, "The schema", issues);
        }

        Dictionary<string, int> properties = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int p = 0; p < type.Properties.Count; p++)
        {
          string name = type.Properties[p];
          MsgPackItem propertyAt = (p < type.PropertyItemRefs.Count ? type.PropertyItemRefs[p] : null) ?? at;
          int firstIndex;
          if (name is null)
          {
            issues.Add(new ValidationItem(propertyAt, ValidationSeverity.Error, 0, "Property #", p, " of type ", type.Name, " in the schema has no name (nil): reading this type into a class fails."));
          }
          else if (properties.TryGetValue(name, out firstIndex))
          {
            issues.Add(new ValidationItem(propertyAt, ValidationSeverity.Error, 0, "Type ", type.Name, " in the schema has the property name \"", name, "\" twice (#", firstIndex, " and #", p,
              "): reading this type into a class fails."));
          }
          else
          {
            properties.Add(name, p);
            if (name.Length == 0 || name == "@")
            {
              issues.Add(new ValidationItem(propertyAt, ValidationSeverity.Warning, 0, "Property #", p, " of type ", type.Name, " in the schema is named \"", name,
                "\", which is reserved for ", name.Length == 0 ? "the type id" : "the content of a wrapped value", ": LsMsgPack never writes it, no class can have such a property."));
            }
          }
        }
      }
    }

    #endregion

    #region Type names

    /// <summary>
    /// Characters the type resolver refuses (TypeResolver.UnsafeNameChars in Core): assembly-qualified names, bracketed generic arguments, pointers and references.
    /// </summary>
    private static readonly char[] UnsafeNameChars = new[] { ',', '[', ']', '&', '*' };

    private static readonly Regex ArraySuffix = new Regex(@"(\[,*\])+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Framework types known as deserialization gadgets (ysoserial.net and others): creating them or setting their properties runs code, starts processes,
    /// touches files or deserializes other data. Only distinctive names: type names are short by default, so a common one (Task, Action...) would flag the user's own classes.
    /// </summary>
    private static readonly HashSet<string> Gadgets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "Process", "ProcessStartInfo", "ObjectDataProvider", "XmlDataProvider", "ExpandedWrapper", "AssemblyInstaller", "PSObject",
      "WindowsIdentity", "WindowsPrincipal", "WindowsClaimsIdentity", "ClaimsIdentity", "ClaimsPrincipal", "RolePrincipal",
      "SessionSecurityToken", "SessionViewStateHistoryItem", "TextFormattingRunProperties", "ActivitySurrogateSelector", "ObjectStateFormatter",
      "LosFormatter", "BinaryFormatter", "SoapFormatter", "NetDataContractSerializer", "DataSet", "DataTable", "ResourceSet", "ResourceReader",
      "ToolboxItemContainer", "TempFileCollection", "FileInfo", "DirectoryInfo", "FileStream", "WebClient", "HttpWebRequest", "XslCompiledTransform",
      "AppDomain", "RuntimeType", "MethodInfo", "PropertyInfo", "MulticastDelegate", "PeriodicTimer", "WeakReference", "SqlConnection"
    };

    /// <summary>
    /// The names in a type name as LsMsgPack writes them ("Dictionary&lt;String, List&lt;Foo&gt;&gt;[]": Dictionary, String, List, Foo), with their array suffixes.
    /// Commas outside the generic arguments (an assembly name) do not separate names.
    /// </summary>
    private static List<string> SplitNames(string typeName)
    {
      List<string> names = new List<string>();
      int angle = 0;
      int square = 0;
      int start = 0;
      for (int t = 0; t < typeName.Length; t++)
      {
        char c = typeName[t];
        if (c == '[') square++;
        else if (c == ']') square--;
        else if (c == '<' || c == '>' || (c == ',' && angle > 0 && square == 0))
        {
          AddName(names, typeName, start, t);
          start = t + 1;
          if (c == '<') angle++;
          else if (c == '>') angle--;
        }
      }
      AddName(names, typeName, start, typeName.Length);
      return names;
    }

    private static void AddName(List<string> names, string typeName, int start, int end)
    {
      string name = typeName.Substring(start, end - start).Trim();
      if (name.Length > 0)
        names.Add(name);
    }

    /// <summary>
    /// The name without namespace, declaring types, array suffix and generic arity.
    /// </summary>
    private static string ShortName(string name)
    {
      name = ArraySuffix.Replace(name, string.Empty);
      int separator = Math.Max(name.LastIndexOf('.'), name.LastIndexOf('+'));
      if (separator >= 0)
        name = name.Substring(separator + 1);
      int arity = name.IndexOf('`');
      return arity > 0 ? name.Substring(0, arity) : name;
    }

    /// <summary>
    /// Names a reader refuses (an assembly could be loaded) and known gadgets.
    /// </summary>
    /// <returns>False when the name was reported</returns>
    private static bool CheckTypeName(string typeName, MsgPackItem at, string where, List<ValidationItem> issues)
    {
      List<string> names = SplitNames(typeName);
      for (int t = 0; t < names.Count; t++)
      {
        if (ArraySuffix.Replace(names[t], string.Empty).IndexOfAny(UnsafeNameChars) >= 0)
        {
          issues.Add(new ValidationItem(at, ValidationSeverity.SecurityVulnerability, 0, where, " names the type \"", typeName,
            "\" with an assembly name or bracketed generic arguments. LsMsgPack and LtMsgPack never write such names and refuse to read them: Type.GetType would load the assembly it names, which runs its code (see docs/security.md). The data was made by another writer or by hand, possibly to attack a reader."));
          return false;
        }
      }

      for (int t = 0; t < names.Count; t++)
      {
        string shortName = ShortName(names[t]);
        if (Gadgets.Contains(shortName))
        {
          issues.Add(new ValidationItem(at, ValidationSeverity.SecurityVulnerability, 0, where, " names the type \"", typeName, "\": ", shortName,
            " is the name of a framework type known as a deserialization gadget (creating it or setting its properties can run code, start processes or touch files). A reader creates it where the declared type allows it (object, or an interface it implements) unless a TypeGuard refuses it (see docs/security.md): the data was possibly made to attack a reader. If it is a class of your own with that name, this does not apply."));
          return false;
        }
      }
      return true;
    }

    /// <summary>
    /// The outermost type of a name: its short name with the generic arity ("List`1"), the number of generic arguments and the rank of its array suffix (0 when it is not an array).
    /// </summary>
    private static string OuterName(string typeName, out List<string> arguments, out int rank)
    {
      arguments = new List<string>();
      rank = 0;
      string name = typeName.Trim();
      Match suffix = Regex.Match(name, @"\[(,*)\]$", RegexOptions.CultureInvariant);
      if (suffix.Success)
      {
        rank = suffix.Groups[1].Length + 1;
        return string.Concat(name.Substring(0, suffix.Index), "[]");
      }

      int open = name.IndexOf('<');
      if (open > 0 && name.EndsWith(">", StringComparison.Ordinal))
      {
        int depth = 0;
        int start = open + 1;
        for (int t = open + 1; t < name.Length - 1; t++)
        {
          char c = name[t];
          if (c == '<' || c == '[') depth++;
          else if (c == '>' || c == ']') depth--;
          else if (c == ',' && depth == 0)
          {
            arguments.Add(name.Substring(start, t - start).Trim());
            start = t + 1;
          }
        }
        arguments.Add(name.Substring(start, name.Length - 1 - start).Trim());
        return string.Concat(ShortName(name.Substring(0, open)), "`", arguments.Count.ToString(CultureInfo.InvariantCulture));
      }
      return ShortName(name);
    }

    private static HashSet<string> _frameworkNames;

    /// <summary>
    /// The public types of the assemblies the type resolver searches by name (TypeResolver.NativeAssemblies in Core): the core library, the collections and numerics.
    /// Their names, short ("List`1") and full.
    /// </summary>
    private static HashSet<string> FrameworkNames
    {
      get
      {
        HashSet<string> names = _frameworkNames;
        if (names != null)
          return names;

        names = new HashSet<string>(StringComparer.Ordinal);
        List<Assembly> assemblies = new List<Assembly>()
        {
          typeof(object).Assembly,
          typeof(List<>).Assembly,
          typeof(SortedSet<>).Assembly,
          typeof(System.Collections.Concurrent.ConcurrentBag<>).Assembly,
          typeof(System.Collections.ObjectModel.ObservableCollection<>).Assembly,
          typeof(System.Numerics.BigInteger).Assembly,
          typeof(Uri).Assembly
        };
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        for (int t = 0; t < loaded.Length; t++)
          if (string.Equals(loaded[t].GetName().Name, "System.Collections.Immutable", StringComparison.Ordinal))
            assemblies.Add(loaded[t]);

        for (int a = 0; a < assemblies.Count; a++)
        {
          if (assemblies.IndexOf(assemblies[a]) != a)
            continue;
          Type[] types;
          try
          {
            types = assemblies[a].GetExportedTypes();
          }
          catch (Exception)
          {
            continue;
          }
          for (int t = 0; t < types.Length; t++)
          {
            names.Add(types[t].Name);
            if (types[t].FullName != null)
              names.Add(types[t].FullName);
          }
        }
        _frameworkNames = names;
        return names;
      }
    }

    /// <summary>
    /// A type of the framework (or an array of one): it has no derived types in the user's code, so it only gets a type id where it is assigned to object or a framework interface or base class.
    /// </summary>
    private static bool IsFrameworkType(string typeName)
    {
      List<string> arguments;
      int rank;
      string outer = OuterName(typeName, out arguments, out rank);
      if (rank > 0)
        return IsFrameworkType(typeName.Substring(0, typeName.LastIndexOf('[')));
      if (FrameworkNames.Contains(outer))
        return true;
      // A full name: the namespace is not part of OuterName
      string trimmed = typeName.Trim();
      int open = trimmed.IndexOf('<');
      string full = open > 0 ? string.Concat(trimmed.Substring(0, open), "`", arguments.Count.ToString(CultureInfo.InvariantCulture)) : trimmed;
      return FrameworkNames.Contains(full);
    }

    /// <summary>
    /// A collection whose elements (or values) are declared as object: "List&lt;Object&gt;", "Object[]", "Dictionary&lt;String, Object&gt;", ArrayList, Hashtable...
    /// </summary>
    private static bool HoldsObjects(string typeName)
    {
      List<string> arguments;
      int rank;
      string outer = OuterName(typeName, out arguments, out rank);
      if (rank > 0)
        return IsObject(typeName.Substring(0, typeName.LastIndexOf('[')));
      switch (outer)
      {
        case "ArrayList":
        case "Hashtable":
        case "Queue":
        case "Stack":
        case "SortedList":
        case "ListDictionary":
        case "HybridDictionary":
        case "OrderedDictionary":
          return true;
      }
      return arguments.Count > 0 && IsObject(arguments[arguments.Count - 1]);
    }

    private static bool IsObject(string typeName)
    {
      string name = typeName.Trim();
      return string.Equals(name, "Object", StringComparison.Ordinal) || string.Equals(name, "System.Object", StringComparison.Ordinal);
    }

    #endregion

    #region Body

    /// <summary>
    /// The evidence that a place of the object model (a property of a type, the elements of a collection, the values of a dictionary) is declared as object.
    /// </summary>
    private sealed class Place
    {
      public MsgPackItem At;
      public string Reason;
      public int Count;
      public ValueKind Kind;
      public PrimitiveObject First;
      /// <summary>The <see cref="Reason"/> is a value with a type id (counted), not values of different kinds</summary>
      public bool FromTypeIds;
    }

    private sealed class BodyValidator
    {
      private static readonly Regex Indexes = new Regex(@"\[\d+\]", RegexOptions.CultureInvariant);

      private readonly SchemaInfo _schema;
      private readonly List<ValidationItem> _issues;

      /// <summary>
      /// Per place (<see cref="PlaceKey"/>), in the order they were found.
      /// </summary>
      private readonly Dictionary<string, Place> _places = new Dictionary<string, Place>(StringComparer.Ordinal);
      private readonly List<Place> _found = new List<Place>();

      public BodyValidator(SchemaInfo schema, List<ValidationItem> issues)
      {
        _schema = schema != null && schema.IsAvailable ? schema : null;
        _issues = issues;
      }

      public void Validate(RootObject payload)
      {
        Visit(payload);
        for (int t = 0; t < _found.Count; t++)
        {
          Place place = _found[t];
          if (place.Reason is null)
            continue;
          string more = place.FromTypeIds && place.Count > 1 ? string.Concat(" This place holds ", place.Count.ToString(CultureInfo.InvariantCulture), " such values.") : string.Empty;
          _issues.Add(new ValidationItem(place.At, ValidationSeverity.SecurityVulnerability, 0, place.Reason, SecurityAdvice, more));
        }
      }

      private void Visit(PrimitiveObject obj)
      {
        if (obj.TypeIdItemRef != null)
          CheckTypeId(obj);
        else if (!(obj is RootObject))
          CheckKind(obj);

        if (obj is ComplexObject complex)
        {
          for (int t = 0; t < complex.Members.Count; t++)
            if (!(complex.Members[t] is RootObject)) // payloads of a sequence are validated on their own
              Visit(complex.Members[t]);
        }
      }

      private void CheckTypeId(PrimitiveObject obj)
      {
        MsgPackItem typeId = obj.TypeIdItemRef;
        string name = obj.Type;
        if (typeId is MpInt)
        {
          if (_schema is null)
            return; // a schema that is not available, or a custom type resolver
          long index = -1;
          try
          {
            index = Convert.ToInt64(typeId.Value, CultureInfo.InvariantCulture);
          }
          catch (OverflowException)
          {
          }
          if (index < 0 || index >= _schema.Types.Count)
          {
            _issues.Add(new ValidationItem(typeId, ValidationSeverity.Error, 0, "The type id ", typeId.Value, " of ", Where(obj), " is not in the schema (",
              _schema.Types.Count, _schema.Types.Count == 1 ? " type" : " types", "): reading it fails."));
            return;
          }
        }
        else if (typeId is MpString)
        {
          if (!CheckTypeName(name, typeId, string.Concat("The type id of ", Where(obj)), _issues))
            return;
        }
        else
        {
          _issues.Add(new ValidationItem(typeId, ValidationSeverity.Error, 0, "The type id of ", Where(obj), " is a ", MsgPackItem.GetOfficialTypeName(typeId.TypeId),
            ": type ids are names, or indexes of the schema. A reader can not resolve it."));
          return;
        }

        if (string.IsNullOrEmpty(name))
          return;
        CheckShape(obj, name);

        if (!(obj is ComplexObject))
        {
          AddEvidence(obj, typeId, string.Concat(Capitalized(Where(obj)), " holds ", WithArticle(PrimitiveObject.KindName(obj.ValueKind)), " with the type id \"", name,
            "\". A value that is not an object or a collection only gets a type id where its type differs from the declared type, and such types have no derived types: this place is declared as object (or as an interface, e.g. IComparable), unless the data was written with AddTypeIdOption.Always."));
        }
        else if (IsFrameworkType(name))
        {
          string elements = HoldsObjects(name) ? " Its elements are declared as object as well: each of them can have a type id of its own." : string.Empty;
          AddEvidence(obj, typeId, string.Concat(Capitalized(Where(obj)), " holds ", WithArticle(name), " with a type id. A framework type only gets a type id where it is assigned to object, or to a framework interface or base class (e.g. IEnumerable), or the data was written with AddTypeIdOption.Always: this place accepts types the data picks.", elements));
        }
        CheckKind(obj);
      }

      /// <summary>
      /// A place where values of different kinds were found (e.g. an integer and a string): only object (or an interface both implement) holds both.
      /// <para>Only for the properties of objects with a type id and the values of a dictionary with string keys (with the schema the keys of objects are indexes).
      /// An inferred type may be wrong (and the inference rules out types whose values differ in kind), without the schema objects of different types share paths,
      /// and an array is also a tuple, a KeyValuePair or a DateTimeOffset (values of different kinds).</para>
      /// </summary>
      private void CheckKind(PrimitiveObject obj)
      {
        ComplexObject parent = obj.Parent;
        if (parent is null)
          return;
        bool typed = parent.Kind == ObjectKind.Object && parent.TypeIdItemRef != null && !parent.TypeIsGuess;
        bool dictionary = parent.Kind == ObjectKind.Dictionary && _schema != null && obj.FirstItemRef is MpString;
        if (!typed && !dictionary)
          return;

        ValueKind kind = obj is ComplexObject ? ValueKind.Map : obj.ValueKind; // objects and collections count as one kind: a property declared as a base class holds both
        if (kind == ValueKind.Nil || kind == ValueKind.Error)
          return;

        Place place = GetPlace(obj);
        if (place.First is null)
        {
          place.First = obj;
          place.Kind = kind;
          return;
        }
        if (place.Kind == kind || place.Reason != null)
          return;

        place.At = obj.LastItemRef ?? obj.FirstItemRef;
        place.Reason = string.Concat(Capitalized(Where(obj)), " holds ", WithArticle(Describe(obj, kind)), ", ", Where(place.First), " ", WithArticle(Describe(place.First, place.Kind)),
          ": only a place declared as object (or as an interface both implement) holds both.");
      }

      private static string Describe(PrimitiveObject obj, ValueKind kind)
      {
        if (kind != ValueKind.Map)
          return PrimitiveObject.KindName(kind);
        ComplexObject complex = obj as ComplexObject;
        return complex != null && complex.IsCollection ? "collection" : "object";
      }

      private void AddEvidence(PrimitiveObject obj, MsgPackItem at, string reason)
      {
        Place place = GetPlace(obj);
        if (place.Reason is null)
        {
          place.Reason = reason;
          place.At = at;
          place.FromTypeIds = true;
        }
        if (place.FromTypeIds)
          place.Count++;
      }

      private Place GetPlace(PrimitiveObject obj)
      {
        string key = PlaceKey(obj);
        Place place;
        if (!_places.TryGetValue(key, out place))
        {
          place = new Place();
          _places.Add(key, place);
          _found.Add(place);
        }
        return place;
      }

      /// <summary>
      /// Identifies the declared type of a value: a property of a type (of the schema when known), the elements of a collection or the values of a dictionary (by their path without indexes).
      /// </summary>
      private static string PlaceKey(PrimitiveObject obj)
      {
        ComplexObject parent = obj.Parent;
        if (parent is null || parent is RootObject && parent.Kind == ObjectKind.Value)
          return string.Empty;
        switch (parent.Kind)
        {
          case ObjectKind.Object:
            if (parent.SchemaType != null)
              return string.Concat("#", parent.SchemaType.Index.ToString(CultureInfo.InvariantCulture), ".", obj.Name);
            if (parent.Type != null && !parent.TypeIsGuess)
              return string.Concat(parent.Type, ".", obj.Name);
            return string.Concat(Indexes.Replace(parent.Path, "[]"), ".", obj.Name);
          case ObjectKind.Dictionary:
            return Indexes.Replace(parent.Path, "[]") + "{}";
          case ObjectKind.Entry:
            return string.Concat(Indexes.Replace(parent.Path, "[]"), ".", obj.Name);
          default:
            return Indexes.Replace(parent.Path, "[]") + "[]";
        }
      }

      /// <summary>
      /// The shapes of the framework types a reader converts from arrays: it fails on another shape.
      /// </summary>
      private void CheckShape(PrimitiveObject obj, string typeName)
      {
        List<string> arguments;
        int rank;
        string outer = OuterName(typeName, out arguments, out rank);
        ComplexObject complex = obj as ComplexObject;
        List<PrimitiveObject> items = complex != null && complex.Kind == ObjectKind.Collection ? complex.Members : null;
        string problem = null;

        if (rank > 1)
          problem = CheckMultidimensional(items, rank);
        else if (outer == "DateTimeOffset")
        {
          if (complex is null)
          {
            if (obj.ValueKind != ValueKind.Timestamp)
              problem = "it is written as a timestamp, or as [timestamp, offset in minutes]";
          }
          else if (items is null || items.Count != 2 || items[0].ValueKind != ValueKind.Timestamp && items[0].ValueKind != ValueKind.Nil || items[1].ValueKind != ValueKind.Int)
            problem = "it is written as a timestamp, or as [timestamp, offset in minutes]";
          else
          {
            long minutes = Convert.ToInt64(items[1].Value, CultureInfo.InvariantCulture);
            if (minutes < -14 * 60 || minutes > 14 * 60)
              problem = string.Concat("its offset of ", minutes.ToString(CultureInfo.InvariantCulture), " minutes is beyond the 14 hours a DateTimeOffset allows");
          }
        }
        else if (outer == "Complex")
        {
          if (items is null || items.Count != 2 || !IsNumber(items[0]) || !IsNumber(items[1]))
            problem = "it is written as [real, imaginary]";
        }
        else if (outer == "KeyValuePair`2")
        {
          if (items is null || items.Count != 2)
            problem = "it is written as [key, value]";
        }
        else if (outer.StartsWith("Tuple`", StringComparison.Ordinal) || outer.StartsWith("ValueTuple`", StringComparison.Ordinal))
        {
          int expected = Math.Min(arguments.Count, 8); // the 8th item (Rest) is a nested tuple
          if (items is null || items.Count != expected)
            problem = string.Concat("it is written as an array of its ", expected.ToString(CultureInfo.InvariantCulture), " items");
        }

        if (problem != null)
        {
          _issues.Add(new ValidationItem(obj.LastItemRef ?? obj.FirstItemRef, ValidationSeverity.Error, 0, Capitalized(Where(obj)), " has the type id \"", typeName,
            "\", but does not have its shape (", problem, "): reading it as that type fails."));
        }
      }

      private static string CheckMultidimensional(List<PrimitiveObject> items, int rank)
      {
        string shape = string.Concat("it is written as [", rank.ToString(CultureInfo.InvariantCulture), " lengths, [items]]");
        if (items is null || items.Count != rank + 1)
          return shape;
        long product = 1;
        for (int t = 0; t < rank; t++)
        {
          if (items[t].ValueKind != ValueKind.Int)
            return shape;
          long length;
          try
          {
            length = Convert.ToInt64(items[t].Value, CultureInfo.InvariantCulture);
          }
          catch (OverflowException)
          {
            return "a length does not fit in an integer";
          }
          if (length < 0 || length > int.MaxValue)
            return string.Concat("the length ", length.ToString(CultureInfo.InvariantCulture), " is not a valid array length");
          product = Math.Min(product * length, (long)int.MaxValue + 1);
        }
        ComplexObject values = items[rank] as ComplexObject;
        if (values is null || values.Kind != ObjectKind.Collection)
          return shape;
        if (values.Members.Count != product)
          return string.Concat("the lengths make ", product.ToString(CultureInfo.InvariantCulture), " items, the data has ", values.Members.Count.ToString(CultureInfo.InvariantCulture));
        return null;
      }

      private static bool IsNumber(PrimitiveObject obj)
      {
        return obj.ValueKind == ValueKind.Float || obj.ValueKind == ValueKind.Int;
      }

      private static string Where(PrimitiveObject obj)
      {
        string path = obj.Path;
        if (string.IsNullOrEmpty(path) || obj.Parent is RootObject && obj.Parent.Kind == ObjectKind.Value)
          return "the root";
        return path;
      }

      private static string WithArticle(string noun)
      {
        return string.Concat("AEIOUaeiou".IndexOf(noun[0]) >= 0 ? "an " : "a ", noun);
      }

      private static string Capitalized(string text)
      {
        return text == "the root" ? "The root" : text;
      }
    }

    #endregion
  }
}
#endif
