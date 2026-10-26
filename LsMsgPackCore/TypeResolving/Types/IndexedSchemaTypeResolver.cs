using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace LsMsgPack.TypeResolving.Types
{

  /// <summary>
  /// <para>Gives each included complex type a unique number as ID</para>
  /// <para>When deserializing, will lookup the name by the ID.</para>
  /// <para>Depends on other resolvers to resolve the Type by it's name (e.g. <see cref="WildGooseChaseResolver"/>)</para>
  /// <para>This should be the first resolver followed by one that can resolve by name.</para>
  /// <para>this is also a PropertyIdResolver, note that the same instance should be used.</para>
  /// </summary>
  public class IndexedSchemaTypeResolver : IMsgPackTypeResolver, IMsgPackPropertyIdResolver
  {
    [IgnoreDataMember]
    public int count { get { return ByTypeId.Count; } }

    [XmlElement("T")]
    [SerializeEnumerable(typeof(ComplexTypeDef), false)]
    public List<ComplexTypeDef> ByTypeId { get; set; } = new List<ComplexTypeDef>();

    [IgnoreDataMember]
    public Dictionary<Type, ComplexTypeDef> ByType { get; set; } = new Dictionary<Type, ComplexTypeDef>();


    /// <summary>
    /// A frozen schema is shared by the calls of a cached schema session (see <see cref="SchemaStore"/>) and must not change: adding to it throws <see cref="SchemaGrowthException"/>, the caller then extends a copy.
    /// </summary>
    internal bool IsFrozen { get; private set; }

    public ComplexTypeDef GetComplex(Type type, MsgPackOptions settings)
    {
      if (ByType.TryGetValue(type, out ComplexTypeDef complexSchemaBase))
        return complexSchemaBase;

      if (IsFrozen)
        throw SchemaGrowthException.Instance;

      ComplexTypeDef newEntry = new ComplexTypeDef(count, type, settings);
      ByType.Add(type, newEntry);
      ByTypeId.Add(newEntry);

      // The properties of a collection are only serialized when asked for (SerializeEnumerableAttribute.SerializeProperties, on the type or the property), so they are added once they are (see GetId)
      if (newEntry.IsCollection)
        return newEntry;

      // Only the names are needed: the property id resolvers (not this one) only for the names they give (see FullPropertyInfo.SchemaName)
      FullPropertyInfo[] props = FullPropertyInfo.GetStaticallyIncludedProps(type, settings);
      newEntry.ParseProps(props, settings);

      return newEntry;
    }

    /// <summary>
    /// Sets the ids of the properties of a type (as <see cref="IMsgPackPropertyIdResolver.GetId"/> would) when its entry has the same names in the same order, so the id of each is its position:
    /// the entry was made from these properties (writing), or the writer had the same ones (reading).
    /// <para>For a session without other property id resolvers, see FullPropertyInfo.GetSerializedProps. False (nothing set) otherwise, or when two properties have the same name.</para>
    /// </summary>
    internal bool TryAssignIds(Type type, FullPropertyInfo[] props, MsgPackOptions settings)
    {
      if (props.Length == 0)
        return false;
      for (int t = 0; t < props.Length; t++)
        if (!props[t]._uniqueName)
          return false;

      if (!TryGetDef(type, out ComplexTypeDef def))
        def = GetComplex(type, settings); // as GetId does for the first property
      if (!ReferenceEquals(def.ParsedFrom, props))
      {
        // The same names (unique, as the properties' are), so the lookup GetId uses would give each its position
        List<string> names = def.Props;
        if (def.IsCollection || names.Count != props.Length)
          return false;
        for (int t = 0; t < props.Length; t++)
          if (!string.Equals(names[t], props[t].PropertyInfo.Name, StringComparison.Ordinal))
            return false;
      }

      for (int t = 0; t < props.Length; t++)
        props[t].PropertyId = BoxedId(t);
      return true;
    }

    private static readonly object[] BoxedIds = CreateBoxedIds(256);

    private static object[] CreateBoxedIds(int count)
    {
      object[] boxed = new object[count];
      for (int t = 0; t < count; t++)
        boxed[t] = t;
      return boxed;
    }

    /// <summary>
    /// The index as an object, the small ones boxed once.
    /// </summary>
    internal static object BoxedId(int id)
    {
      return (uint)id < (uint)BoxedIds.Length ? BoxedIds[id] : id;
    }

    public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackOptions settings)
    {
      return GetComplex(type, settings).TypeId;
    }

    public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackOptions settings)
    {
      if (typeId is null || !NumericTypes.All.Contains(typeId.GetType())) // e.g. a name used by another resolver
        return null;

      decimal id = Convert.ToDecimal(typeId);
      if (id < 0 || id >= ByTypeId.Count || id != decimal.Truncate(id))
        return null;

      ComplexTypeDef def = ByTypeId[(int)id];

      if (def.Type != null)
        return def.Type;

      if (IsFrozen)
        throw SchemaGrowthException.Instance;

      // Not resolved when the schema was read (another assembly, or a name of several types): now the declared type helps
      Type type = ResolveTypeName(def.TypeName, assignedTo, settings);
      if (type is null)
        return PairedWith(def, assignedTo) ?? throw UnresolvedType(def, settings);
      def.Type = type;
      ByType[type] = def;
      return type;
    }

    /// <summary>
    /// A type id of a class the reader does not have: the one reader class that <see cref="BindReaderTypes"/> paired with its entry and fits where it goes, null when there is none.
    /// </summary>
    private Type PairedWith(ComplexTypeDef def, Type assignedTo)
    {
      Type target = assignedTo is null ? null : Nullable.GetUnderlyingType(assignedTo) ?? assignedTo;
      Type found = null;
      foreach (KeyValuePair<Type, ComplexTypeDef> entry in ByType)
      {
        if (!ReferenceEquals(entry.Value, def) || entry.Key.IsAbstract || entry.Key.IsInterface || (target != null && !target.IsAssignableFrom(entry.Key)))
          continue;
        if (found != null)
          return null;
        found = entry.Key;
      }
      return found;
    }

    /// <summary>
    /// The entry of the schema of a type that is read: the one its name was resolved to when the schema was read, otherwise an entry with its name (short or full) whose name was not resolved,
    /// e.g. a type of an assembly that was not known yet.
    /// </summary>
    /// <exception cref="SchemaGrowthException">When the entry is found by name in a frozen schema (a copy remembers it)</exception>
    internal bool TryGetDef(Type type, out ComplexTypeDef def)
    {
      if (ByType.TryGetValue(type, out def))
        return true;
      if (_unresolved == 0)
        return false;

      def = FindUnresolved(TypeResolver.GetTypeName(type, true)) ?? FindUnresolved(TypeResolver.GetTypeName(type, false));
      if (def is null)
        return false;

      if (IsFrozen)
        throw SchemaGrowthException.Instance;
      def.Type = type;
      ByType[type] = def;
      _unresolved--;
      return true;
    }

    private ComplexTypeDef FindUnresolved(string name)
    {
      ComplexTypeDef found = null;
      for (int t = 0; t < ByTypeId.Count; t++)
      {
        ComplexTypeDef def = ByTypeId[t];
        if (def.Type is null && string.Equals(def.TypeName, name, StringComparison.Ordinal))
        {
          if (found != null)
            return null; // two types of the writer with this (short) name: not this way
          found = def;
        }
      }
      return found;
    }

    /// <summary>
    /// The entries whose names were not resolved when the schema was read.
    /// </summary>
    private int _unresolved;

    /// <summary>
    /// A schema of version 2 read from the data: the entry of the objects the root holds (the root's class, or the elements' of a root collection), -1 when not known.
    /// </summary>
    internal int RootEntry = -1;

    // The root types BindReaderTypes was called for (the last one checked without a lookup)
    private volatile Type _lastBoundRoot;
    private ConcurrentDictionary<Type, bool> _boundRoots;

    /// <summary>
    /// The reader classes BindReaderTypes found for two or more of the writer's classes (so for none), with those writer classes: reported with the objects that are skipped (<see cref="SkipIfNoEntry"/>).
    /// </summary>
    private Dictionary<Type, Type[]> _ambiguous;

    /// <summary>
    /// Reading into other classes than the writer used (e.g. a DTO into an entity): the values of an object only say which property of the writer's class they are (an index into its entry),
    /// not which class that was. The root of the payload is the first entry of the schema, and the other classes follow from the properties both sides have:
    /// the writer's class of a property is the declared type of the writer's property with the same name (also of collection elements, dictionary values and nullables).
    /// Adds an entry for each class that is read this way (the entry of the writer's class), so the values are matched by name. Does nothing when the root class is in the schema.
    /// <para>Without the writer's classes (their names are not resolved, e.g. a client of a server) the types of a schema of version 2 say which entries the properties hold (<see cref="ComplexTypeDef.ValueEntries"/>), and the root is its root entry;
    /// a schema of version 1 only pairs the root (the first entry).</para>
    /// <para>Once per root type and schema. A class that would get two different entries gets none (it is remembered for the report, see <see cref="SkipIfNoEntry"/>).</para>
    /// </summary>
    /// <param name="ignoreCase"><see cref="MsgPackOptions.PropertyNameCaseInsensitive"/>: a property of the reader without the exact name of one of the writer's follows the one whose name equals its name ignoring case</param>
    /// <exception cref="SchemaGrowthException">When a frozen schema would get an entry (a copy remembers it)</exception>
    internal void BindReaderTypes(Type root, bool ignoreCase)
    {
      if (_lastBoundRoot == root)
        return;
      if (_boundRoots != null && _boundRoots.ContainsKey(root))
      {
        _lastBoundRoot = root;
        return;
      }

      Dictionary<Type, ComplexTypeDef> aliases = ReaderAliases(root, ignoreCase, out Dictionary<Type, Type[]> ambiguous);
      bool newAmbiguous = false;
      foreach (Type read in ambiguous.Keys)
        newAmbiguous |= _ambiguous is null || !_ambiguous.ContainsKey(read);
      if (aliases.Count > 0 || newAmbiguous)
      {
        if (IsFrozen)
          throw SchemaGrowthException.Instance;
        foreach (KeyValuePair<Type, ComplexTypeDef> alias in aliases)
          ByType[alias.Key] = alias.Value;
        foreach (KeyValuePair<Type, Type[]> conflict in ambiguous)
        {
          if (_ambiguous is null)
            _ambiguous = new Dictionary<Type, Type[]>();
          _ambiguous[conflict.Key] = conflict.Value;
        }
      }

      if (_boundRoots is null)
        _boundRoots = new ConcurrentDictionary<Type, bool>();
      _boundRoots.TryAdd(root, true);
      _lastBoundRoot = root;
    }

    /// <param name="ambiguous">The reader classes found for more than one writer class, with those writer classes</param>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = TrimReasons.ModelTypes)]
    private Dictionary<Type, ComplexTypeDef> ReaderAliases(Type root, bool ignoreCase, out Dictionary<Type, Type[]> ambiguous)
    {
      Dictionary<Type, ComplexTypeDef> aliases = new Dictionary<Type, ComplexTypeDef>();
      ambiguous = new Dictionary<Type, Type[]>();
      Type reader = ObjectTypeOf(root, 0);
      if (reader is null || ByTypeId.Count == 0 || HasOwnEntry(reader))
        return aliases;
      ComplexTypeDef start = RootDef();
      if (start is null)
        return aliases;

      Dictionary<Type, List<ComplexTypeDef>> conflicts = new Dictionary<Type, List<ComplexTypeDef>>();
      HashSet<KeyValuePair<Type, ComplexTypeDef>> visited = new HashSet<KeyValuePair<Type, ComplexTypeDef>>();
      Queue<KeyValuePair<Type, ComplexTypeDef>> pairs = new Queue<KeyValuePair<Type, ComplexTypeDef>>();
      pairs.Enqueue(new KeyValuePair<Type, ComplexTypeDef>(reader, start));
      while (pairs.Count > 0)
      {
        KeyValuePair<Type, ComplexTypeDef> pair = pairs.Dequeue();
        Type read = pair.Key;
        ComplexTypeDef def = pair.Value;
        if (read == def.Type || !visited.Add(pair))
          continue;
        if (def.IsCollection || HasOwnEntry(read))
          continue; // the reader's class has its own entry (and its own properties below it)

        if (aliases.TryGetValue(read, out ComplexTypeDef earlier) && !ReferenceEquals(earlier, def))
        {
          if (!conflicts.TryGetValue(read, out List<ComplexTypeDef> writers))
          {
            writers = new List<ComplexTypeDef>() { earlier };
            conflicts.Add(read, writers);
          }
          if (!writers.Contains(def))
            writers.Add(def);
        }
        aliases[read] = def;

        PropertyInfo[] readProps = read.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        for (int t = 0; t < readProps.Length; t++)
        {
          PropertyInfo readProp = readProps[t];
          if (readProp.GetIndexParameters().Length > 0)
            continue;
          Type readValue = ObjectTypeOf(readProp.PropertyType, 0);
          ComplexTypeDef next = readValue is null ? null : ValueDef(def, readProp.Name, ignoreCase);
          if (next != null)
            pairs.Enqueue(new KeyValuePair<Type, ComplexTypeDef>(readValue, next));
        }
      }

      foreach (KeyValuePair<Type, List<ComplexTypeDef>> conflict in conflicts)
      {
        aliases.Remove(conflict.Key);
        ambiguous.Add(conflict.Key, conflict.Value.Where(d => d.Type != null).Select(d => d.Type).ToArray());
      }
      return aliases;
    }

    /// <summary>
    /// The entry of the root's class: the first entry (the writer adds the entries depth first: the class of the root, or of the first element of a root collection) when its class was found,
    /// otherwise the root of a schema of version 2, otherwise the first entry when it has properties (the reader has none of the writer's classes).
    /// </summary>
    private ComplexTypeDef RootDef()
    {
      ComplexTypeDef first = ByTypeId[0];
      if (first.Type != null)
        return first.IsCollection ? null : first;
      if (RootEntry >= 0)
        return RootEntry < ByTypeId.Count ? ByTypeId[RootEntry] : null;
      return first.Props.Count > 0 ? first : null;
    }

    /// <summary>
    /// The entry of the objects the property <paramref name="name"/> of the writer's class of <paramref name="def"/> holds: by the declared type of the writer's property (its class was found),
    /// or by the type a schema of version 2 describes. Null when there is none.
    /// </summary>
    private ComplexTypeDef ValueDef(ComplexTypeDef def, string name, bool ignoreCase)
    {
      if (def.Type != null)
      {
        PropertyInfo writtenProp = NonIndexedProperty(def.Type, name, ignoreCase);
        Type writtenValue = writtenProp is null ? null : ObjectTypeOf(writtenProp.PropertyType, 0);
        return writtenValue != null && ByType.TryGetValue(writtenValue, out ComplexTypeDef next) ? next : null;
      }
      if (def.ValueEntries is null)
        return null;
      int position = PositionOf(def, name, ignoreCase);
      int entry = position < 0 || position >= def.ValueEntries.Length ? -1 : def.ValueEntries[position];
      return entry >= 0 && entry < ByTypeId.Count ? ByTypeId[entry] : null;
    }

    /// <returns>The position of the name in the entry, or with <paramref name="ignoreCase"/> of the one name that equals it ignoring case, -1 when none</returns>
    private static int PositionOf(ComplexTypeDef def, string name, bool ignoreCase)
    {
      if (def.IdByName.TryGetValue(name, out int position))
        return position;
      if (!ignoreCase)
        return -1;
      position = -1;
      for (int t = 0; t < def.Props.Count; t++)
      {
        if (!string.Equals(def.Props[t], name, StringComparison.OrdinalIgnoreCase))
          continue;
        if (position >= 0)
          return -1; // several names (as IdsIgnoringCase)
        position = t;
      }
      return position;
    }

    private bool HasOwnEntry(Type type)
    {
      return ByType.ContainsKey(type) || (_unresolved > 0 && (FindUnresolved(TypeResolver.GetTypeName(type, true)) ?? FindUnresolved(TypeResolver.GetTypeName(type, false))) != null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimReasons.ModelTypes)]
    private static PropertyInfo NonIndexedProperty(Type type, string name, bool ignoreCase)
    {
      PropertyInfo[] props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
      PropertyInfo found = null;
      int matches = 0;
      for (int t = 0; t < props.Length; t++)
      {
        if (props[t].GetIndexParameters().Length > 0)
          continue;
        if (string.Equals(props[t].Name, name, StringComparison.Ordinal))
          return props[t];
        if (ignoreCase && string.Equals(props[t].Name, name, StringComparison.OrdinalIgnoreCase))
        {
          found = props[t];
          matches++;
        }
      }
      return matches == 1 ? found : null; // as IdsIgnoringCase: none when several names equal it
    }

    /// <summary>
    /// The class of the objects a value of this type holds: itself, the element type of a collection, the value type of a dictionary, the type of a nullable. Null for values that are not objects (numbers, strings...).
    /// </summary>
    private static Type ObjectTypeOf(Type type, int depth)
    {
      if (type is null || depth > 8)
        return null;
      type = Nullable.GetUnderlyingType(type) ?? type;
      if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(object) || type.IsPointer || type.ContainsGenericParameters)
        return null;
      if (ValueConverter.IsCollection(type))
      {
        CollectionInfo collection = CollectionInfo.Get(type);
        return ObjectTypeOf(collection.IsDictionary ? collection.ValueType : collection.ElementType, depth + 1);
      }
      return type;
    }

    /// <summary>
    /// Reading an object (with values) of a class that has no entry: its values are indexes into an entry of the writer's class, but which one is not known.
    /// This throws rather than matching the values by position. When the differences are collected (<see cref="MsgPackOptions._differences"/>), the object is reported, and skipped unless <see cref="MsgPackOptions.ReadErrors"/> is FailFast.
    /// </summary>
    /// <param name="assignedTo">The property the object is assigned to (null for the root and elements)</param>
    /// <returns>True when the object is to be skipped (left null)</returns>
    internal bool SkipIfNoEntry(Type type, MsgPackOptions settings, FullPropertyInfo assignedTo)
    {
      if (!HasNoEntry(type, settings))
        return false;
      Type[] writers = null;
      if (_ambiguous != null)
        _ambiguous.TryGetValue(type, out writers);
      if (settings._differences is null)
        throw NoEntry(type, writers);
      Exception error = NoEntry(type, writers);
      if (settings._differences.UnmatchedClass(type, assignedTo, writers, error))
        return true;
      throw error;
    }

    private bool HasNoEntry(Type type, MsgPackOptions settings)
    {
      if (TryGetDef(type, out ComplexTypeDef _))
        return false;
      if (FullPropertyInfo.GetStaticallyIncludedProps(type, settings).Length == 0)
        return false; // not an object with properties (e.g. a DateTimeOffset written as an array)
      for (int t = 0; t < settings._propertyNameResolvers.Length; t++)
        if (!(settings._propertyNameResolvers[t] is IndexedSchemaTypeResolver))
          return false; // custom property ids are not indexes into the schema
      return true;
    }

    /// <param name="writers">The writer's classes the class was paired with (more than one, so none was used), null when it was not paired</param>
    private Exception NoEntry(Type type, Type[] writers)
    {
      string pairing = writers is null
        ? $"The data was written with another class ({string.Join(", ", ByTypeId.Select(d => d.TypeName))}), and none of them could be paired with {type.Name} "
          + "(the root is the first class of the schema, the other classes follow from the properties both classes have: by the types of the writer's classes, or without them by the types schema version 2 describes). "
        : $"{type.Name} is read where the writer had different classes ({string.Join(", ", writers.Select(w => w.Name))}), so it could not be paired with one of them. ";
      return new MsgPackException($"The data has no schema entry for {type.FullName}, so its values cannot be matched to its properties. "
        + pairing + "Deserialize with an out ReadDifferences to skip such objects and get them reported.");
    }

    private static Exception UnresolvedType(ComplexTypeDef def, MsgPackOptions settings)
    {
      return new UnresolvedTypeException(def.TypeName, $"Unable to resolve type \"{def.TypeName}\" using resolver(s): {string.Join(", ", settings._typeResolvers.Select(r => r.GetType().Name))}\r\nIt may help to pre-register your type like this:\r\n  MsgPackTypes.CacheAssemblyTypes(typeof({def.TypeName}));"
        + (def.ResolveError is null ? "" : string.Concat("\r\n", def.ResolveError)));
    }

    /// <summary>
    /// Resolve the name (stored in the schema) using the other resolvers (they may have provided the name, see <see cref="ComplexTypeDef"/>) or the default name resolver.
    /// </summary>
    private static Type ResolveTypeName(string typeName, Type assignedTo, MsgPackOptions settings)
    {
      IMsgPackTypeResolver[] resolvers = settings._typeResolvers;
      Dictionary<object, object> noProperties = new Dictionary<object, object>(0);
      for (int t = resolvers.Length - 1; t >= 0; t--)
      {
        if (resolvers[t] is IndexedSchemaTypeResolver)
          continue;

        Type type = resolvers[t].Resolve(typeName, assignedTo, null, noProperties, settings);
        if (type != null && !type.ContainsGenericParameters)
          return type;
      }

      return TypeResolver.ResolveInternal(typeName, assignedTo, resolvers, false);
    }

    object IMsgPackPropertyIdResolver.GetId(FullPropertyInfo assignedTo, MsgPackOptions settings)
    {
      ComplexTypeDef def;
      if (!TryGetDef(assignedTo.PropertyInfo.ReflectedType, out def))
      {
        if (assignedTo.AssignedToType is null)
          return null;

        def = GetComplex(assignedTo.PropertyInfo.ReflectedType, settings); // does not resolve property ids, so no recursion
      }

      object found = IdIn(def, assignedTo.PropertyInfo.Name);
      if (found is null && settings._propertyNameCaseInsensitive && IdIgnoringCase(def, assignedTo, settings, out int id))
        return BoxedId(id);
      return found;
    }

    /// <summary>
    /// The id of a property that a property id resolver gave the string id <paramref name="name"/>: the index of that name in the entry (see <see cref="FullPropertyInfo"/>.SchemaName),
    /// null when the entry (of the data) does not have it.
    /// </summary>
    internal object IdOfSchemaName(FullPropertyInfo assignedTo, string name, MsgPackOptions settings)
    {
      ComplexTypeDef def;
      if (!TryGetDef(assignedTo.PropertyInfo.ReflectedType, out def))
      {
        if (assignedTo.AssignedToType is null)
          return null;
        def = GetComplex(assignedTo.PropertyInfo.ReflectedType, settings);
      }
      return IdIn(def, name);
    }

    private object IdIn(ComplexTypeDef def, string name)
    {
      if (def.IdByName.TryGetValue(name, out int id))
        return BoxedId(id);

      if (def.IsCollection)
      {
        if (IsFrozen)
          throw SchemaGrowthException.Instance;
        return def.AddProp(name);
      }
      return null;
    }

    /// <summary>
    /// <see cref="MsgPackOptions.PropertyNameCaseInsensitive"/>: the position of the name in the entry that equals the property's name ignoring case (see <see cref="ComplexTypeDef.IdsIgnoringCase"/>).
    /// </summary>
    private static bool IdIgnoringCase(ComplexTypeDef def, FullPropertyInfo assignedTo, MsgPackOptions settings, out int id)
    {
      Type reader = assignedTo.PropertyInfo.ReflectedType;
      ComplexTypeDef.CaseInsensitiveIds known = def.IgnoringCase;
      if (known is null || known.Reader != reader)
      {
        FullPropertyInfo[] props = FullPropertyInfo.GetStaticallyIncludedProps(reader, settings);
        string[] names = new string[props.Length];
        for (int t = 0; t < props.Length; t++)
          names[t] = props[t].PropertyInfo.Name; // GetId is only asked for the properties without a name of another resolver
        def.IgnoringCase = known = new ComplexTypeDef.CaseInsensitiveIds(reader, def.IdsIgnoringCase(names));
      }
      return known.Ids.TryGetValue(assignedTo.PropertyInfo.Name, out id);
    }

    /// <summary>
    /// The id <see cref="IMsgPackPropertyIdResolver.GetId"/> gives the name of the property when the entry has it, null otherwise; adds no entries or names (see FullPropertyInfo.FormerId).
    /// </summary>
    internal object IdOfName(FullPropertyInfo assignedTo)
    {
      if (TryGetDef(assignedTo.PropertyInfo.ReflectedType, out ComplexTypeDef def) && def.IdByName.TryGetValue(assignedTo.PropertyInfo.Name, out int id))
        return BoxedId(id);
      return null;
    }

    private void ResolveDeserializedTypes(MsgPackOptions settings)
    {

      for (int t = 0; t < ByTypeId.Count; t++)
      {
        ComplexTypeDef def = ByTypeId[t];
        if (def.Type is null)
        {
          // Without the declared type (not known yet) a name may not resolve: another assembly, or a short name of several types. That is decided where the entry is used (Resolve, TryGetDef)
          try
          {
            def.Type = ResolveTypeName(def.TypeName, null, settings);
          }
          catch (MsgPackException)
          {
            throw; // e.g. a name that could load an assembly
          }
          catch (Exception ex)
          {
            def.ResolveError = ex.Message;
          }
        }

        if (def.Type is null)
          _unresolved++;
        else if (!ByType.ContainsKey(def.Type))
          ByType.Add(def.Type, def);
      }
    }


    // We know the fixed structure of our schema, so we can omit the oop stuff to keep it small
    public byte[] Pack()
    {
      return Pack(null);
    }

    /// <param name="settings">Settings used for the rest of the data (e.g. to use the same <see cref="MsgPackSettings.EndianAction"/>)</param>
    public byte[] Pack(MsgPackOptions settings)
    {
      ByteWriter bytes = new ByteWriter();
      WriteTo(bytes, settings ?? new DefaultOptions());
      return bytes.ToArray();
    }

    /// <summary>
    /// The schema in the <see cref="MsgPackOptions.SchemaVersion"/> of the settings.
    /// </summary>
    /// <param name="byteOrder">The <see cref="MsgPackSettings.EndianAction"/> of the bytes</param>
    /// <param name="root">The declared type of the root (version 2 describes it)</param>
    /// <param name="settings">The settings the body is written with</param>
    internal byte[] Pack(MsgPackOptions byteOrder, Type root, MsgPackOptions settings)
    {
      if (settings._schemaVersion != SchemaVersion.Described)
        return Pack(byteOrder);
      ByteWriter bytes = new ByteWriter();
      DescribedSchema.Write(bytes, ByTypeId, root, settings, byteOrder);
      return bytes.ToArray();
    }

    /// <summary>
    /// A map of type names with an array of property names, written directly (the same bytes as packing them as MpMap, MpArray and MpString items)
    /// </summary>
    /// <param name="settings">Only the <see cref="MsgPackSettings.EndianAction"/> is used (for the lengths of long names and large schemas)</param>
    private void WriteTo(ByteWriter bytes, MsgPackOptions settings)
    {
      bytes.WriteMapHeader(ByTypeId.Count, settings);
      for (int t = 0; t < ByTypeId.Count; t++)
      {
        ComplexTypeDef def = ByTypeId[t];
        WriteName(bytes, def.TypeName, settings);
        bytes.WriteArrayHeader(def.Props.Count, settings);
        for (int p = 0; p < def.Props.Count; p++)
          WriteName(bytes, def.Props[p], settings);
      }
    }

    private static void WriteName(ByteWriter bytes, string name, MsgPackOptions settings)
    {
      if (name is null)
        bytes.Write((byte)MsgPackTypeId.MpNull);
      else
        bytes.WriteString(name, settings);
    }

    /// <returns>null if the stream starts with nil (null is serialized without a schema)</returns>
    /// <exception cref="MsgPackException">When the stream does not start with a schema</exception>
    public static IndexedSchemaTypeResolver Unpack(System.IO.Stream bytes, MsgPackOptions settings)
    {
      if (settings is null)
        settings = new DefaultOptions();

      int first = bytes.ReadByte();
      if (first < 0)
        throw new MsgPackException("Unexpected end of data.", 0, MsgPackTypeId.NeverUsed);
      if (first == (int)MsgPackTypeId.MpNull)
        return null;
      if (!SchemaBytes.IsMap(first))
        throw SchemaBytes.NotASchema(first);

      return FromBytes(SchemaBytes.ReadRaw(bytes, first, settings), settings, settings);
    }

    /// <summary>
    /// The schema of the given bytes (see <see cref="SchemaBytes.ReadRaw"/>), with the types resolved.
    /// </summary>
    /// <param name="lengthSettings">The <see cref="MsgPackSettings.EndianAction"/> the bytes were read with, null for the byte order of the specification (<see cref="SchemaBytes.Canonical"/>)</param>
    /// <param name="settings">Resolves the types</param>
    internal static IndexedSchemaTypeResolver FromBytes(byte[] raw, MsgPackOptions lengthSettings, MsgPackOptions settings)
    {
      List<ComplexTypeDef> defs = SchemaBytes.Parse(raw, lengthSettings, out SchemaVersion _, out int rootEntry);
      IndexedSchemaTypeResolver ret = new IndexedSchemaTypeResolver() { ByTypeId = defs, ByType = new Dictionary<Type, ComplexTypeDef>(defs.Count), RootEntry = rootEntry };
      ret.ResolveDeserializedTypes(settings);
      return ret;
    }

    /// <summary>
    /// A copy that can be extended (not frozen), the types and properties keep their ids.
    /// </summary>
    internal IndexedSchemaTypeResolver Copy()
    {
      IndexedSchemaTypeResolver copy = new IndexedSchemaTypeResolver()
      {
        ByTypeId = new List<ComplexTypeDef>(ByTypeId.Count),
        ByType = new Dictionary<Type, ComplexTypeDef>(ByType.Count),
        _unresolved = _unresolved,
        _ambiguous = _ambiguous is null ? null : new Dictionary<Type, Type[]>(_ambiguous),
        RootEntry = RootEntry
      };

      Dictionary<ComplexTypeDef, ComplexTypeDef> copies = new Dictionary<ComplexTypeDef, ComplexTypeDef>(ByTypeId.Count);
      for (int t = 0; t < ByTypeId.Count; t++)
      {
        ComplexTypeDef def = ByTypeId[t];
        ComplexTypeDef defCopy = new ComplexTypeDef() { Type = def.Type, TypeName = def.TypeName, TypeId = def.TypeId, Props = new List<string>(def.Props), ResolveError = def.ResolveError, ValueEntries = def.ValueEntries, ContentEntry = def.ContentEntry };
        copy.ByTypeId.Add(defCopy);
        copies.Add(def, defCopy);
      }

      foreach (KeyValuePair<Type, ComplexTypeDef> entry in ByType)
        copy.ByType.Add(entry.Key, copies.TryGetValue(entry.Value, out ComplexTypeDef defCopy) ? defCopy : entry.Value);

      return copy;
    }

    /// <summary>
    /// From now on the schema is only read (by several threads), the lookups that are otherwise built when first used are built now.
    /// </summary>
    internal void Freeze()
    {
      for (int t = 0; t < ByTypeId.Count; t++)
      {
        Dictionary<string, int> built = ByTypeId[t].IdByName;
      }
      IsFrozen = true;
    }
  }

  public class ComplexTypeDef
  {
    public ComplexTypeDef() { }

    public ComplexTypeDef(int id, Type type, MsgPackOptions settings)
    {
      Type = type;
      TypeId = id;

      // Get type name from downstream resolvers

      for (int t = 0; t < settings._typeResolvers.Length; t++)
      {
        IMsgPackTypeResolver resolver = settings._typeResolvers[t];
        if (resolver is IndexedSchemaTypeResolver)
          continue;

        TypeName = resolver.IdForType(type, null, settings) as string;
        if (!string.IsNullOrEmpty(TypeName))
          break;
      }

      if (string.IsNullOrEmpty(TypeName))
        TypeName = TypeResolver.GetTypeName(type, (settings._addTypeIdOptions & AddTypeIdOption.FullName) > 0);
    }

    /// <exception cref="MsgPackException">When property id resolvers give two properties the same name (as FullPropertyInfo checks the ids)</exception>
    internal void ParseProps(FullPropertyInfo[] props, MsgPackOptions settings)
    {
      bool renamed = false;
      for (int t = 0; t < props.Length; t++)
      {
        string name = FullPropertyInfo.SchemaNameOf(props[t], settings);
        renamed |= props[t].SchemaName != null;
        if (renamed) // the .NET names are unique (see FullPropertyInfo._uniqueName), a resolver's may equal another name
        {
          int other = Props.IndexOf(name);
          if (other >= 0)
            throw new MsgPackException($"The properties {Type.Name}.{props[other].PropertyInfo.Name} and {Type.Name}.{props[t].PropertyInfo.Name} have the same id \"{name}\", the keys of a map must be unique.");
        }
        Props.Add(name);
      }
      ParsedFrom = props;
    }

    /// <summary>
    /// A schema of version 2 read from the data: per property (the positions of <see cref="Props"/>) the entry of the objects its values hold (-1 for none), null for version 1 or an entry made here.
    /// For readers without the writer's classes (see IndexedSchemaTypeResolver.BindReaderTypes).
    /// </summary>
    internal int[] ValueEntries;

    /// <summary>
    /// A schema of version 2 read from the data: the entry of the objects the content of this entry holds (<c>["@", type]</c>: the elements of a collection), -1 for none.
    /// </summary>
    internal int ContentEntry = -1;

    /// <summary>
    /// The properties (of the session) <see cref="Props"/> was made of, null for an entry read from the data or copied: see <see cref="IndexedSchemaTypeResolver.TryAssignIds"/>.
    /// </summary>
    internal FullPropertyInfo[] ParsedFrom;

    /// <summary>
    /// The properties of the reader at the positions of <see cref="Props"/>, kept for the properties they were found for (see ValueConverter.PropertiesByPosition).
    /// <para>One reference, replaced as a whole: a frozen schema is read by several threads.</para>
    /// </summary>
    internal volatile PositionBinding ByPosition;

    internal sealed class PositionBinding
    {
      internal readonly FullPropertyInfo[] Props;
      internal readonly int Count;
      internal readonly FullPropertyInfo[] Properties;

      internal PositionBinding(FullPropertyInfo[] props, int count, FullPropertyInfo[] properties)
      {
        Props = props;
        Count = count;
        Properties = properties;
      }
    }

    /// <summary>
    /// <see cref="MsgPackOptions.PropertyNameCaseInsensitive"/> with the indexed schema: the positions of the names of this entry that a property of the reader has only ignoring case (camelCase writers), by the name of the property.
    /// A property that is not named in the entry gets the one name that equals its name ignoring case, none when several names of the entry or several properties equal each other ignoring case (as keys of maps are matched).
    /// </summary>
    /// <param name="readerNames">The names of the reader's properties</param>
    internal Dictionary<string, int> IdsIgnoringCase(IList<string> readerNames)
    {
      Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
      Dictionary<string, int> byName = new Dictionary<string, int>(Props.Count, StringComparer.OrdinalIgnoreCase); // the position, -1 for several names
      for (int t = 0; t < Props.Count; t++)
        if (Props[t] != null)
          byName[Props[t]] = byName.ContainsKey(Props[t]) ? -1 : t;

      Dictionary<string, int> readers = new Dictionary<string, int>(readerNames.Count, StringComparer.OrdinalIgnoreCase); // how many properties have the name
      for (int t = 0; t < readerNames.Count; t++)
        readers[readerNames[t]] = readers.TryGetValue(readerNames[t], out int count) ? count + 1 : 1;

      for (int t = 0; t < readerNames.Count; t++)
      {
        string name = readerNames[t];
        if (byName.TryGetValue(name, out int position) && position >= 0 && readers[name] == 1 && !string.Equals(Props[position], name, StringComparison.Ordinal))
          result[name] = position;
      }
      return result;
    }

    /// <summary>
    /// <see cref="IdsIgnoringCase"/> for the properties of the last reader class IndexedSchemaTypeResolver.GetId asked for, replaced as a whole: a frozen schema is read by several threads.
    /// </summary>
    internal volatile CaseInsensitiveIds IgnoringCase;

    internal sealed class CaseInsensitiveIds
    {
      internal readonly Type Reader;
      internal readonly Dictionary<string, int> Ids;

      internal CaseInsensitiveIds(Type reader, Dictionary<string, int> ids)
      {
        Reader = reader;
        Ids = ids;
      }
    }

    internal int AddProp(string name)
    {
      int id = Props.Count;
      IdByName.Add(name, id); // before Props.Add, the getter may build the lookup from Props
      Props.Add(name);
      return id;
    }

    /// <summary>
    /// Collections (except strings) are serialized as their elements, their properties only when asked for.
    /// </summary>
    internal bool IsCollection
    {
      get { return Type != null && Type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(Type); }
    }

    [IgnoreDataMember]
    public Type Type { get; set; } // for runtime only

    [XmlElement("N")]
    public string TypeName { get; set; }

    [IgnoreDataMember]
    public int TypeId { get; set; }

    // used to have PropType as value, but there is no need to preserve more info than the name.
    [XmlElement("P")]
    public List<string> Props { get; set; } = new List<string>();

    /// <summary>
    /// Why the name was not resolved when the schema was read (for the message when it is needed).
    /// </summary>
    internal string ResolveError;


    private Dictionary<string, int> _idByName;
    [IgnoreDataMember]
    public Dictionary<string, int> IdByName
    {
      get
      {
        if (_idByName != null)
          return _idByName;

        _idByName = new Dictionary<string, int>(Props.Count);
        if(Props.Count <= 0)
          return _idByName;

        for (int t = Props.Count - 1; t != 0; t--)
          _idByName.Add(Props[t], t);
        _idByName.Add(Props[0], 0);

        return _idByName;
      }
    }
  }
}
