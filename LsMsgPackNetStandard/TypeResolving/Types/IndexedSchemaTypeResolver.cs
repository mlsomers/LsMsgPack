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


    public ComplexTypeDef GetComplex(Type type, MsgPackSettings settings)
    {
      if (ByType.TryGetValue(type, out ComplexTypeDef complexSchemaBase))
        return complexSchemaBase;

      ComplexTypeDef newEntry = new ComplexTypeDef(count, type, settings);
      ByType.Add(type, newEntry);
      ByTypeId.Add(newEntry);

      // The properties of a collection are only serialized when asked for (SerializeEnumerableAttribute.SerializeProperties, on the type or the property), so they are added once they are (see GetId)
      if (newEntry.IsCollection)
        return newEntry;

      // Only the names are needed here, the ids of these props are not final (not yet in the schema) so keep them out of the session cache
      FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(type, settings, false);
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

    private bool _blockRecursionGetId = false;
    object IMsgPackPropertyIdResolver.GetId(FullPropertyInfo assignedTo, MsgPackSettings settings)
    {
      if (_blockRecursionGetId)
        return null;

      ComplexTypeDef def;
      if (!ByType.TryGetValue(assignedTo.PropertyInfo.ReflectedType, out def))
      {
        if (assignedTo.AssignedToType != null)
        {
          _blockRecursionGetId = true;
          try
          {
            def = GetComplex(assignedTo.PropertyInfo.ReflectedType, settings);
          }
          finally
          {
            _blockRecursionGetId = false;
          }
        }
        else
          return null;
      }

      if (def.IdByName.TryGetValue(assignedTo.PropertyInfo.Name, out int id))
        return id;

      if (def.IsCollection)
        return def.AddProp(assignedTo.PropertyInfo.Name);

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
    public static IndexedSchemaTypeResolver Unpack(System.IO.Stream bytes, MsgPackSettings settings) {
      MsgPackItem item = MsgPackItem.Unpack(bytes, settings ?? new MsgPackSettings());
      if (item is MpNull)
        return null;

      KeyValuePair<object, object>[] items = (item as MpMap)?.Value as KeyValuePair<object, object>[];
      if (items is null)
        throw new MsgPackException($"Expected the data to start with an indexed schema (a map) but found {item.TypeId}. Was it serialized with {nameof(MsgPackSettings)}.{nameof(MsgPackSettings.UseInexedSchema)} = false?", 0, item.TypeId);

      IndexedSchemaTypeResolver ret=new IndexedSchemaTypeResolver(){ ByTypeId=new List<ComplexTypeDef>(items.Length), ByType=new Dictionary<Type, ComplexTypeDef>(items.Length)};
      for (int i = 0; i < items.Length; i++) {
        KeyValuePair<object, object> typ = items[i];
        string typeName = typ.Key as string;
        object[] props = typ.Value as object[];
        if (typeName is null || props is null)
          throw InvalidSchema();

        ComplexTypeDef def= new ComplexTypeDef() { TypeId = i, TypeName = typeName, Props = new List<string>(props.Length) };
        for (int t = 0; t < props.Length; t++)
        {
          if (!(props[t] is string propName))
            throw InvalidSchema();
          def.Props.Add(propName);
        }

        ret.ByTypeId.Add(def);
      }

      ret.ResolveDeserializedTypes(settings);

      return ret;
    }

    private static MsgPackException InvalidSchema()
    {
      return new MsgPackException($"Invalid indexed schema, expected a map of type names with an array of property names. Was the data serialized with {nameof(MsgPackSettings)}.{nameof(MsgPackSettings.UseInexedSchema)} = false?");
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
