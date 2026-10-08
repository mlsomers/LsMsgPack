using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

      // Only the names are needed, so the property id resolvers (including this one) are not consulted
      FullPropertyInfo[] props = FullPropertyInfo.GetStaticallyIncludedProps(type, settings);
      newEntry.ParseProps(props);

      return newEntry;
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
        throw UnresolvedType(def, settings);
      def.Type = type;
      ByType[type] = def;
      return type;
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

    // The root types BindReaderTypes was called for (the last one checked without a lookup)
    private volatile Type _lastBoundRoot;
    private ConcurrentDictionary<Type, bool> _boundRoots;

    /// <summary>
    /// Reading into other classes than the writer used (e.g. a DTO into an entity): the values of an object only say which property of the writer's class they are (an index into its entry),
    /// not which class that was. The root of the payload is the first entry of the schema, and the other classes follow from the properties both sides have:
    /// the writer's class of a property is the declared type of the writer's property with the same name (also of collection elements, dictionary values and nullables).
    /// Adds an entry for each class that is read this way (the entry of the writer's class), so the values are matched by name. Does nothing when the root class is in the schema.
    /// <para>Once per root type and schema. Needs the writer's classes (resolved from their names). A class that would get two different entries gets none.</para>
    /// </summary>
    /// <exception cref="SchemaGrowthException">When a frozen schema would get an entry (a copy remembers it)</exception>
    internal void BindReaderTypes(Type root)
    {
      if (_lastBoundRoot == root)
        return;
      if (_boundRoots != null && _boundRoots.ContainsKey(root))
      {
        _lastBoundRoot = root;
        return;
      }

      Dictionary<Type, ComplexTypeDef> aliases = ReaderAliases(root);
      if (aliases.Count > 0)
      {
        if (IsFrozen)
          throw SchemaGrowthException.Instance;
        foreach (KeyValuePair<Type, ComplexTypeDef> alias in aliases)
          ByType[alias.Key] = alias.Value;
      }

      if (_boundRoots is null)
        _boundRoots = new ConcurrentDictionary<Type, bool>();
      _boundRoots.TryAdd(root, true);
      _lastBoundRoot = root;
    }

    private Dictionary<Type, ComplexTypeDef> ReaderAliases(Type root)
    {
      Dictionary<Type, ComplexTypeDef> aliases = new Dictionary<Type, ComplexTypeDef>();
      Type reader = ObjectTypeOf(root, 0);
      if (reader is null || ByTypeId.Count == 0 || HasOwnEntry(reader))
        return aliases;
      ComplexTypeDef first = ByTypeId[0]; // the writer adds the entries depth first: the first one is the class of the root (or of the first element of a root collection)
      if (first.Type is null || first.IsCollection)
        return aliases;

      HashSet<Type> conflicts = new HashSet<Type>();
      HashSet<KeyValuePair<Type, Type>> visited = new HashSet<KeyValuePair<Type, Type>>();
      Queue<KeyValuePair<Type, Type>> pairs = new Queue<KeyValuePair<Type, Type>>();
      pairs.Enqueue(new KeyValuePair<Type, Type>(reader, first.Type));
      while (pairs.Count > 0)
      {
        KeyValuePair<Type, Type> pair = pairs.Dequeue();
        Type read = pair.Key;
        Type written = pair.Value;
        if (read == written || !visited.Add(pair))
          continue;
        if (!ByType.TryGetValue(written, out ComplexTypeDef def) || def.IsCollection || HasOwnEntry(read))
          continue; // nothing was written with the writer's class, or the reader's class has its own entry (and its own properties below it)

        if (aliases.TryGetValue(read, out ComplexTypeDef earlier) && !ReferenceEquals(earlier, def))
          conflicts.Add(read);
        aliases[read] = def;

        foreach (PropertyInfo readProp in read.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
          if (readProp.GetIndexParameters().Length > 0)
            continue;
          PropertyInfo writtenProp = NonIndexedProperty(written, readProp.Name);
          if (writtenProp is null)
            continue;
          Type readValue = ObjectTypeOf(readProp.PropertyType, 0);
          Type writtenValue = ObjectTypeOf(writtenProp.PropertyType, 0);
          if (readValue != null && writtenValue != null)
            pairs.Enqueue(new KeyValuePair<Type, Type>(readValue, writtenValue));
        }
      }

      foreach (Type conflict in conflicts)
        aliases.Remove(conflict);
      return aliases;
    }

    private bool HasOwnEntry(Type type)
    {
      return ByType.ContainsKey(type) || (_unresolved > 0 && (FindUnresolved(TypeResolver.GetTypeName(type, true)) ?? FindUnresolved(TypeResolver.GetTypeName(type, false))) != null);
    }

    private static PropertyInfo NonIndexedProperty(Type type, string name)
    {
      foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        if (string.Equals(prop.Name, name, StringComparison.Ordinal) && prop.GetIndexParameters().Length == 0)
          return prop;
      return null;
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
    /// This throws rather than matching the values by position. When the differences are collected (<see cref="MsgPackOptions._differences"/>), the object is reported and skipped instead.
    /// </summary>
    /// <param name="assignedTo">The property the object is assigned to (null for the root and elements)</param>
    /// <returns>True when the object is to be skipped (left null)</returns>
    internal bool SkipIfNoEntry(Type type, MsgPackOptions settings, FullPropertyInfo assignedTo)
    {
      if (!HasNoEntry(type, settings))
        return false;
      if (settings._differences is null)
        throw NoEntry(type);
      settings._differences.UnmatchedClass(type, assignedTo);
      return true;
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

    private Exception NoEntry(Type type)
    {
      return new MsgPackException($"The data has no schema entry for {type.FullName}, so its values cannot be matched to its properties. "
        + $"The data was written with another class ({string.Join(", ", ByTypeId.Select(d => d.TypeName))}), and none of them could be paired with {type.Name} "
        + "(the root is the first class of the schema, the other classes follow from the properties both classes have, and the reader needs the writer's classes). "
        + "Deserialize with an out ReadDifferences to skip such objects and get them reported.");
    }

    private static Exception UnresolvedType(ComplexTypeDef def, MsgPackOptions settings)
    {
      return new MsgPackException($"Unable to resolve type \"{def.TypeName}\" using resolver(s): {string.Join(", ", settings._typeResolvers.Select(r => r.GetType().Name))}\r\nIt may help to pre-register your type like this:\r\n  MsgPackTypes.CacheAssemblyTypes(typeof({def.TypeName}));"
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

      if (def.IdByName.TryGetValue(assignedTo.PropertyInfo.Name, out int id))
        return id;

      if (def.IsCollection)
      {
        if (IsFrozen)
          throw SchemaGrowthException.Instance;
        return def.AddProp(assignedTo.PropertyInfo.Name);
      }

      return null;
    }

    private void ResolveDeserializedTypes(MsgPackOptions settings)
    {

      foreach (ComplexTypeDef def in ByTypeId)
      {
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
      List<ComplexTypeDef> defs = SchemaBytes.Parse(raw, lengthSettings);
      IndexedSchemaTypeResolver ret = new IndexedSchemaTypeResolver() { ByTypeId = defs, ByType = new Dictionary<Type, ComplexTypeDef>(defs.Count) };
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
        _unresolved = _unresolved
      };

      Dictionary<ComplexTypeDef, ComplexTypeDef> copies = new Dictionary<ComplexTypeDef, ComplexTypeDef>(ByTypeId.Count);
      for (int t = 0; t < ByTypeId.Count; t++)
      {
        ComplexTypeDef def = ByTypeId[t];
        ComplexTypeDef defCopy = new ComplexTypeDef() { Type = def.Type, TypeName = def.TypeName, TypeId = def.TypeId, Props = new List<string>(def.Props), ResolveError = def.ResolveError };
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

    internal void ParseProps(FullPropertyInfo[] props)
    {
      for (int t = 0; t < props.Length; t++)
        Props.Add(props[t].PropertyInfo.Name);
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
