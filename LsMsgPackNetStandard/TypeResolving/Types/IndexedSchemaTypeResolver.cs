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
      FullPropertyInfo[] props = FullPropertyInfo.GetSerializedProps(type, settings);
      newEntry.ParseProps(props);

      return newEntry;
    }

    public object IdForType(Type type, FullPropertyInfo assignedTo, MsgPackSettings settings)
    {
      return GetComplex(type, settings).TypeId;
    }

    private bool _blockRecursionResolve = false;
    public Type Resolve(object typeId, Type assignedTo, FullPropertyInfo assignedToProp, Dictionary<object, object> properties, MsgPackSettings settings)
    {
      if (_blockRecursionResolve || typeId is null)
        return null;

      int id;
      if (typeId is int) id = (int)typeId;
      else id = Convert.ToInt32(typeId);

      if (id < 0 || id >= ByTypeId.Count)
        return null;

      ComplexTypeDef def = ByTypeId[id];

      if (def.Type != null)
        return def.Type;

      _blockRecursionResolve = true;
      try
      {
        List<IMsgPackTypeResolver> resolvers = new List<IMsgPackTypeResolver>(settings._typeResolvers);
        resolvers.Remove(this);
        Type type = TypeResolver.ResolveInternal(def.TypeName, assignedTo, resolvers.ToArray());
        def.Type = type;
        ByType[type] = def;
        return type;
      }
      finally
      {
        _blockRecursionResolve = false;
      }
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

      return null;
    }

    private void ResolveDeserializedTypes(MsgPackSettings settings)
    {

      foreach (ComplexTypeDef def in ByTypeId)
      {
        // string typeName = MsgPackSerializer.GetTypeName(type, (settings._addTypeName & AddTypeIdOption.FullName) > 0);

        if (def.Type is null)
          def.Type = TypeResolver.ResolveInternal(def.TypeName, null, settings._typeResolvers);

        if (def.Type is null)
          throw new Exception($"Unable to resolve type \"{def.TypeName}\" using resolver(s): {string.Join(", ", settings._typeResolvers.Select(r => r.GetType().Name))}\r\nIt may help to pre-register your type like this:\r\n  MsgPackSerializer.CacheAssemblyTypes(typeof({def.TypeName}));");

        if (!ByType.ContainsKey(def.Type))
          ByType.Add(def.Type, def);
      }
    }


    // We know the fixed structure of our schema, so we can omit the oop stuff to keep it small
    public byte[] Pack()
    {
      MsgPackSettings settings=new MsgPackSettings{_addTypeIdOptions = AddTypeIdOption.Never};

      KeyValuePair<object,object>[] items=new KeyValuePair<object, object>[ByTypeId.Count];
      if (ByTypeId.Count > 0) { 
        for (int t = ByTypeId.Count - 1; t != 0; t--) // Loop with JNZ condition
          items[t] = new KeyValuePair<object, object>(ByTypeId[t].TypeName, ByTypeId[t].Props.ToArray());
        items[0] = new KeyValuePair<object, object>(ByTypeId[0].TypeName, ByTypeId[0].Props.ToArray());
      }
      MpMap m=new MpMap(items, settings);
      return m.ToBytes();
    }

    public static IndexedSchemaTypeResolver Unpack(System.IO.Stream bytes, MsgPackSettings settings) {
      MpMap m=(MpMap)MsgPackItem.Unpack(bytes);
      KeyValuePair<object, object>[] items=m.Value as KeyValuePair<object, object>[];

      IndexedSchemaTypeResolver ret=new IndexedSchemaTypeResolver(){ ByTypeId=new List<ComplexTypeDef>(items.Length), ByType=new Dictionary<Type, ComplexTypeDef>(items.Length)};
      for (int i = 0; i < m.Count; i++) {
        KeyValuePair<object, object> typ = items[i];
        object[] props= (object[])typ.Value;

        ComplexTypeDef def= new ComplexTypeDef() { TypeId = i, TypeName = (string)typ.Key, Props = new List<string>(props.Length) };
        for (int t = 0; t < props.Length; t++)
          def.Props.Add((string)props[t]);

        ret.ByTypeId.Add(def);
      }

      ret.ResolveDeserializedTypes(settings);

      return ret;
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
