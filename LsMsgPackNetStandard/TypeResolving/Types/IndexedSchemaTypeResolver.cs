using LsMsgPack.Meta;
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
    public int count { get; private set; } = 0;

    [XmlElement("T")]
    public Dictionary<long, ComplexTypeDef> ByTypeId { get; set; } = new Dictionary<long, ComplexTypeDef>();

    [IgnoreDataMember]
    public Dictionary<Type, ComplexTypeDef> ByType { get; set; } = new Dictionary<Type, ComplexTypeDef>();


    public ComplexTypeDef GetComplex(Type type, MsgPackSettings settings)
    {
      if (ByType.TryGetValue(type, out ComplexTypeDef complexSchemaBase))
        return complexSchemaBase;

      count++;

      ComplexTypeDef newEntry = new ComplexTypeDef(count, type, settings);
      ByType.Add(type, newEntry);
      ByTypeId.Add(count, newEntry);
      FullPropertyInfo[] props = MsgPackSerializer.GetSerializedProps(type, settings);
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
      if (_blockRecursionResolve)
        return null;

      long id;
      if (typeId is long) id = (long)typeId;
      else id = Convert.ToInt64(typeId);

      ComplexTypeDef def;
      if (!ByTypeId.TryGetValue(id, out def))
        return null;

      if (def.Type != null)
        return def.Type;

      _blockRecursionResolve = true;
      try
      {
        List<IMsgPackTypeResolver> resolvers = new List<IMsgPackTypeResolver>(settings.TypeResolvers);
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
      if (!ByType.TryGetValue(assignedTo.PropertyInfo.DeclaringType, out def))
      {
        if (assignedTo.AssignedToType != null)
        {
          _blockRecursionGetId = true;
          try
          {
            def = GetComplex(assignedTo.PropertyInfo.DeclaringType, settings);
          }
          finally
          {
            _blockRecursionGetId = false;
          }
        }
        else
          return null;
      }

      if (def.IdByName.TryGetValue(assignedTo.PropertyInfo.Name, out long id))
        return id;

      return null;
    }

    internal void ResolveDeserializedTypes(MsgPackSettings settings)
    {
      
      foreach (ComplexTypeDef def in ByTypeId.Values)
      {
        // string typeName = MsgPackSerializer.GetTypeName(type, (settings._addTypeName & AddTypeIdOption.FullName) > 0);

        if (def.Type is null)
          def.Type = TypeResolver.ResolveInternal(def.TypeName, null, settings.TypeResolvers);

        if (def.Type is null)
          throw new Exception($"Unable to resolve type \"{def.TypeName}\" using resolver(s): {string.Join(", ", settings.TypeResolvers.Select(r => r.GetType().Name))}");

        if (!ByType.ContainsKey(def.Type))
          ByType.Add(def.Type, def);
      }

      count = ByTypeId.Count;
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

      for (int t = 0; t < settings.TypeResolvers.Length; t++)
      {
        IMsgPackTypeResolver resolver = settings.TypeResolvers[t];
        if (resolver is IndexedSchemaTypeResolver)
          continue;

        TypeName = resolver.IdForType(type, null, settings) as string;
        if (!string.IsNullOrEmpty(TypeName))
          break;
      }

      if (string.IsNullOrEmpty(TypeName))
        TypeName = MsgPackSerializer.GetTypeName(type, (settings._addTypeName & AddTypeIdOption.FullName) > 0);
    }

    internal void ParseProps(FullPropertyInfo[] props)
    {
      for (int t = props.Length - 1; t >= 0; t--)
      {
        FullPropertyInfo prop = props[t];
        Props.Add(t, prop.PropertyInfo.Name);
      }
    }

    [IgnoreDataMember]
    public Type Type { get; set; } // for runtime only

    [XmlElement("N")]
    public string TypeName { get; set; }

    [IgnoreDataMember]
    public long TypeId { get; set; }

    // used to have PropType as value, but there is no need to preserve more info than the name.
    [XmlElement("P")]
    public Dictionary<long, string> Props { get; set; } = new Dictionary<long, string>();


    private Dictionary<string, long> _idByName;
    [IgnoreDataMember]
    public Dictionary<string, long> IdByName
    {
      get
      {
        if (_idByName != null)
          return _idByName;

        _idByName = new Dictionary<string, long>(Props.Count);
        foreach (KeyValuePair<long, string> kvp in Props)
          _idByName[kvp.Value] = kvp.Key;

        return _idByName;
      }
    }
  }
}
