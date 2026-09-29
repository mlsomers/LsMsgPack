using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Attributes;
using LsMsgPack.TypeResolving.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public ComplexTypeDef GetComplex(Type type, MsgPackSettings settings)
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

    public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackSettings settings)
    {
      return GetComplex(type, settings).TypeId;
    }

    public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
    {
      if (typeId is null || !MsgPackMeta.NumericTypes.Contains(typeId.GetType())) // e.g. a name used by another resolver
        return null;

      decimal id = Convert.ToDecimal(typeId);
      if (id < 0 || id >= ByTypeId.Count || id != decimal.Truncate(id))
        return null;

      ComplexTypeDef def = ByTypeId[(int)id];

      if (def.Type != null)
        return def.Type;

      if (IsFrozen)
        throw SchemaGrowthException.Instance;

      Type type = ResolveTypeName(def.TypeName, assignedTo, settings);
      if (type != null)
      {
        def.Type = type;
        ByType[type] = def;
      }
      return type;
    }

    /// <summary>
    /// Resolve the name (stored in the schema) using the other resolvers (they may have provided the name, see <see cref="ComplexTypeDef"/>) or the default name resolver.
    /// </summary>
    private static Type ResolveTypeName(string typeName, Type assignedTo, MsgPackSettings settings)
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

      return TypeResolver.ResolveInternal(typeName, assignedTo, resolvers);
    }

    object IMsgPackPropertyIdResolver.GetId(FullPropertyInfo assignedTo, MsgPackSettings settings)
    {
      ComplexTypeDef def;
      if (!ByType.TryGetValue(assignedTo.PropertyInfo.ReflectedType, out def))
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

    private void ResolveDeserializedTypes(MsgPackSettings settings)
    {

      foreach (ComplexTypeDef def in ByTypeId)
      {
        // string typeName = MsgPackSerializer.GetTypeName(type, (settings._addTypeName & AddTypeIdOption.FullName) > 0);

        if (def.Type is null)
          def.Type = ResolveTypeName(def.TypeName, null, settings);

        if (def.Type is null)
          throw new Exception($"Unable to resolve type \"{def.TypeName}\" using resolver(s): {string.Join(", ", settings._typeResolvers.Select(r => r.GetType().Name))}\r\nIt may help to pre-register your type like this:\r\n  MsgPackSerializer.CacheAssemblyTypes(typeof({def.TypeName}));");

        if (!ByType.ContainsKey(def.Type))
          ByType.Add(def.Type, def);
      }
    }


    // We know the fixed structure of our schema, so we can omit the oop stuff to keep it small
    public byte[] Pack()
    {
      return Pack(null);
    }

    /// <param name="settings">Settings used for the rest of the data (e.g. to use the same <see cref="MsgPackSettings.EndianAction"/>)</param>
    public byte[] Pack(MsgPackSettings settings)
    {
      ByteWriter bytes = new ByteWriter();
      WriteTo(bytes, settings ?? new MsgPackSettings());
      return bytes.ToArray();
    }

    /// <summary>
    /// A map of type names with an array of property names, written directly (the same bytes as packing them as MpMap, MpArray and MpString items)
    /// </summary>
    /// <param name="settings">Only the <see cref="MsgPackSettings.EndianAction"/> is used (for the lengths of long names and large schemas)</param>
    private void WriteTo(ByteWriter bytes, MsgPackSettings settings)
    {
      MpMap.WriteHeader(bytes, ByTypeId.Count, settings);
      for (int t = 0; t < ByTypeId.Count; t++)
      {
        ComplexTypeDef def = ByTypeId[t];
        WriteName(bytes, def.TypeName, settings);
        MpArray.WriteHeader(bytes, def.Props.Count, settings);
        for (int p = 0; p < def.Props.Count; p++)
          WriteName(bytes, def.Props[p], settings);
      }
    }

    private static void WriteName(ByteWriter bytes, string name, MsgPackSettings settings)
    {
      if (name is null)
        bytes.Write((byte)MsgPackTypeId.MpNull);
      else
        MpString.Write(bytes, name, settings);
    }

    /// <returns>null if the stream starts with nil (null is serialized without a schema)</returns>
    /// <exception cref="MsgPackException">When the stream does not start with a schema</exception>
    public static IndexedSchemaTypeResolver Unpack(System.IO.Stream bytes, MsgPackSettings settings)
    {
      if (settings is null)
        settings = new MsgPackSettings();

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
    internal static IndexedSchemaTypeResolver FromBytes(byte[] raw, MsgPackSettings lengthSettings, MsgPackSettings settings)
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
        ByType = new Dictionary<Type, ComplexTypeDef>(ByType.Count)
      };

      Dictionary<ComplexTypeDef, ComplexTypeDef> copies = new Dictionary<ComplexTypeDef, ComplexTypeDef>(ByTypeId.Count);
      for (int t = 0; t < ByTypeId.Count; t++)
      {
        ComplexTypeDef def = ByTypeId[t];
        ComplexTypeDef defCopy = new ComplexTypeDef() { Type = def.Type, TypeName = def.TypeName, TypeId = def.TypeId, Props = new List<string>(def.Props) };
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

    public ComplexTypeDef(int id, Type type, MsgPackSettings settings)
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
