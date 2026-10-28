using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using LtMsgPack.IO;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LtMsgPack.Writing
{
  /// <summary>
  /// Where property ids and type ids come from.
  /// </summary>
  internal enum IdMode
  {
    /// <summary>
    /// No schema: property names (or the ids of custom property id resolvers) and type names.
    /// </summary>
    Names,

    /// <summary>
    /// The indexed schema of this call is built while writing (<see cref="InlineSchema"/>): property ids are the positions of the properties, type ids the positions of the types.
    /// </summary>
    Inline,

    /// <summary>
    /// A schema session of LsMsgPack.Core decides the ids (a cached schema of a <see cref="SchemaStore"/>, or a session per call when custom resolvers or filters are involved).
    /// </summary>
    Session
  }

  /// <summary>
  /// The state of one Serialize call.
  /// </summary>
  /// <para>Public for the code of the source generator (see LtMsgPack.LtMsgPackContext): may change between releases.</para>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class WriteContext
  {
    internal readonly Serializer Serializer;
    public MsgPackWriter W;

    /// <summary>
    /// The value of the Serialize call.
    /// </summary>
    internal object Root;

    /// <summary>
    /// What the previous call on this thread looked up for its root (calls usually write the same root type), the context belongs to one serializer:
    /// the info of the declared type, the handler of the typed writer (a ValueHandler&lt;T&gt;, see LtMsgPackSerializer.TypedWriter) and the type of the boxed one (Serializer.WriteRoot).
    /// </summary>
    internal FullPropertyInfo RootInfo;
    internal object RootHandler;
    internal WriteTypeInfo RootTypeInfo;
    internal IdMode Mode;
    internal InlineSchema Inline;

    /// <summary>
    /// The settings the ids are resolved with: the options of the serializer, or in <see cref="IdMode.Session"/> a copy using the session.
    /// </summary>
    internal MsgPackOptions IdSettings;
    internal int Depth;

    /// <summary>
    /// Below an object with an anonymous type, or a value with one that is not written as its declared type: objects with anonymous types are written by names (LsMsgPack: the byNames parameter of SerializeObject, see SerializationRules.WritesByNames).
    /// </summary>
    internal bool ByNames;
    private List<MsgPackWriter> _spareWriters;

    internal WriteContext(Serializer serializer)
    {
      Serializer = serializer;
    }

    internal bool UsesSchema { get { return Mode != IdMode.Names; } }

    internal void EnterContainer()
    {
      if (++Depth > Serializer.Options._maxDepth)
        throw TooDeep();
    }

    private MsgPackException TooDeep() // apart, so EnterContainer is inlined
    {
      return new MsgPackException($"The object graph is nested deeper than {nameof(LtMsgPackOptions)}.{nameof(LtMsgPackOptions.MaxDepth)} ({Serializer.Options._maxDepth}), it may contain a cycle.");
    }

    internal void LeaveContainer()
    {
      Depth--;
    }

    /// <summary>
    /// A writer for content that has to be written before its header is known (the elements of a collection that is wrapped with a type id: the id is only known after the elements, as in LsMsgPack).
    /// </summary>
    internal MsgPackWriter RentWriter()
    {
      MsgPackWriter writer;
      if (_spareWriters != null && _spareWriters.Count > 0)
      {
        writer = _spareWriters[_spareWriters.Count - 1];
        _spareWriters.RemoveAt(_spareWriters.Count - 1);
      }
      else
        writer = new MsgPackWriter(256);
      writer.Reset(Serializer.Options);
      return writer;
    }

    internal void ReturnWriter(MsgPackWriter writer)
    {
      if (_spareWriters is null)
        _spareWriters = new List<MsgPackWriter>();
      _spareWriters.Add(writer);
    }

    #region Ids

    /// <summary>
    /// The point where LsMsgPack resolves the property ids of a type (FullPropertyInfo.GetSerializedProps), which adds the type to the indexed schema when it has properties.
    /// </summary>
    /// <param name="infos">The properties as LsMsgPack sees them at this point (their ids, for the filters and resolvers)</param>
    /// <returns>The encoded property ids, in the order of <see cref="ObjectPlan.Props"/></returns>
    internal byte[][] BeginObject(ObjectPlan plan, out FullPropertyInfo[] infos)
    {
      switch (Mode)
      {
        case IdMode.Names:
          infos = plan.Infos;
          return plan.NameKeys;

        case IdMode.Inline:
          if (plan.Props.Length > 0)
            Inline.PropertiesResolved(plan.Schema);
          infos = plan.Infos;
          return plan.IndexKeys;

        default:
          ObjectPlan.SessionIds last = plan.LastSession;
          if (last != null && ReferenceEquals(last.Settings, IdSettings)) // the same frozen session: the same answer as below
          {
            infos = last.Infos;
            return last.Keys;
          }

          infos = FullPropertyInfo.GetSerializedProps(plan.Type, IdSettings);
          if (infos.Length != plan.Props.Length)
            throw new InvalidOperationException($"The properties of {plan.Type.FullName} differ from the ones of the schema session.");
          byte[][] keys = Serializer.SessionKeys(infos);
          if (IdSettings._schemaFrozen) // shared by the calls that use the session (see Serializer.SessionShared), a session of one call is not worth it
            plan.LastSession = new ObjectPlan.SessionIds(IdSettings, infos, keys);
          return keys;
      }
    }

    /// <summary>
    /// Writes the type id (LsMsgPack: GetTypeIdentifier), which adds the type to the indexed schema.
    /// </summary>
    internal void WriteTypeId(Type type, FullPropertyInfo assignedTo)
    {
      switch (Mode)
      {
        case IdMode.Names:
          Serializer.WriteBoxed(this, Serializer.NameTypeId(type, assignedTo), null);
          return;

        case IdMode.Inline:
          W.Int32(Inline.Register(Serializer.GetSchemaInfo(type)));
          return;

        default:
          Serializer.WriteBoxed(this, SerializationRules.GetTypeIdentifier(type, IdSettings, assignedTo), null);
          return;
      }
    }

    #endregion
  }

  /// <summary>
  /// A type as it appears in the indexed schema (LsMsgPack: ComplexTypeDef): its name and, for other types than collections, the names of its properties.
  /// </summary>
  internal sealed class SchemaTypeInfo
  {
    internal Type Type;
    internal string Name;
    internal bool IsCollection;
    internal string[] PropertyNames;

    /// <summary>
    /// Not for collections: the entry of the type in the schema (its name and the array of its property names), encoded when it is first written. The settings of the serializer do not change.
    /// </summary>
    internal byte[] Entry;
  }

  /// <summary>
  /// The indexed schema of one Serialize call, built in the same order as LsMsgPack builds its IndexedSchemaTypeResolver, so the bytes are the same.
  /// </summary>
  internal sealed class InlineSchema
  {
    // Arrays with a count rather than lists: Clear runs once per call, List.Clear calls Array.Clear
    private SchemaTypeInfo[] _types = new SchemaTypeInfo[8];
    private List<string>[] _collectionProps = new List<string>[8]; // collections get their properties when they are resolved (ComplexTypeDef.AddProp)
    private int _count;

    internal void Clear()
    {
      for (int t = _count - 1; t >= 0; t--)
        _collectionProps[t] = null; // the types are kept by the plans anyway
      _count = 0;
    }

    /// <returns>The type id: the position of the type in the schema</returns>
    internal int Register(SchemaTypeInfo info)
    {
      SchemaTypeInfo[] types = _types;
      int count = _count;
      for (int t = 0; t < count; t++) // a handful of types, a scan is faster than hashing
        if (ReferenceEquals(types[t], info))
          return t;

      if (count == types.Length)
      {
        Array.Resize(ref _types, count * 2);
        Array.Resize(ref _collectionProps, count * 2);
      }
      _types[count] = info;
      _collectionProps[count] = info.IsCollection ? new List<string>() : null;
      _count = count + 1;
      return count;
    }

    /// <summary>
    /// The property ids of the type are resolved (the first time in this call): it is added to the schema, a collection gets its property names.
    /// </summary>
    internal void PropertiesResolved(SchemaTypeInfo info)
    {
      int count = _count;
      int id = Register(info);
      if (!info.IsCollection)
        return;

      List<string> props = _collectionProps[id];
      if (props.Count == 0 || id == count) // AddProp only adds names that are not there yet
      {
        string[] names = info.PropertyNames;
        for (int t = 0; t < names.Length; t++)
          if (!props.Contains(names[t]))
            props.Add(names[t]);
      }
    }

    /// <summary>
    /// The schema as IndexedSchemaTypeResolver.Pack writes it: a map of type names with an array of property names.
    /// </summary>
    internal void WriteTo(MsgPackWriter writer)
    {
      writer.MapHeader(_count);
      for (int t = 0; t < _count; t++)
      {
        SchemaTypeInfo info = _types[t];
        if (!info.IsCollection)
        {
          writer.Raw(info.Entry ?? (info.Entry = EncodeEntry(writer, info)));
          continue;
        }

        WriteName(writer, info.Name);
        List<string> props = _collectionProps[t];
        writer.ArrayHeader(props.Count);
        for (int p = 0; p < props.Count; p++)
          WriteName(writer, props[p]);
      }
    }

    /// <summary>
    /// The entries as LsMsgPack's IndexedSchemaTypeResolver has them (for the described schema), the names are shared.
    /// </summary>
    internal List<ComplexTypeDef> ToDefs()
    {
      List<ComplexTypeDef> defs = new List<ComplexTypeDef>(_count);
      for (int t = 0; t < _count; t++)
      {
        SchemaTypeInfo info = _types[t];
        List<string> props = info.IsCollection ? _collectionProps[t] : new List<string>(info.PropertyNames);
        defs.Add(new ComplexTypeDef() { Type = info.Type, TypeName = info.Name, TypeId = t, Props = props });
      }
      return defs;
    }

    /// <summary>
    /// Same types (and properties of collections) in the same order: the same schema.
    /// </summary>
    internal bool SameAs(SchemaTypeInfo[] types, string[][] collectionProps)
    {
      if (types.Length != _count)
        return false;
      for (int t = 0; t < types.Length; t++)
      {
        if (!ReferenceEquals(types[t], _types[t]))
          return false;
        List<string> props = _collectionProps[t];
        if (props is null)
          continue;
        string[] other = collectionProps[t];
        if (other.Length != props.Count)
          return false;
        for (int p = 0; p < other.Length; p++)
          if (!string.Equals(other[p], props[p], StringComparison.Ordinal))
            return false;
      }
      return true;
    }

    internal SchemaTypeInfo[] Types()
    {
      SchemaTypeInfo[] types = new SchemaTypeInfo[_count];
      Array.Copy(_types, types, _count);
      return types;
    }

    internal string[][] CollectionProps()
    {
      string[][] props = new string[_count][];
      for (int t = 0; t < props.Length; t++)
        props[t] = _collectionProps[t]?.ToArray();
      return props;
    }

    internal int TypesHash()
    {
      unchecked
      {
        int hash = _count;
        for (int t = 0; t < _count; t++)
          hash = hash * 31 + RuntimeHelpers.GetHashCode(_types[t]);
        return hash;
      }
    }

    private static byte[] EncodeEntry(MsgPackWriter like, SchemaTypeInfo info)
    {
      MsgPackWriter writer = like.Like(64);
      WriteName(writer, info.Name);
      writer.ArrayHeader(info.PropertyNames.Length);
      for (int p = 0; p < info.PropertyNames.Length; p++)
        WriteName(writer, info.PropertyNames[p]);
      return writer.ToArray();
    }

    private static void WriteName(MsgPackWriter writer, string name)
    {
      if (name is null)
        writer.Nil();
      else
        writer.String(name);
    }
  }

  /// <summary>
  /// The described schemas (<see cref="SchemaVersion.Described"/>) of a serializer's inline schemas, made once per root type and sequence of types (a few per root type) instead of per call.
  /// </summary>
  internal sealed class DescribedInlineSchemas
  {
    private const int MaxCount = 256; // beyond this (endless variations of types) they are made per call

    private readonly object _lock = new object();
    private volatile Described[] _all = new Described[0];

    private sealed class Described
    {
      internal Type Root;
      internal int Hash;
      internal SchemaTypeInfo[] Types;
      internal string[][] CollectionProps;
      internal byte[] Bytes;
    }

    internal byte[] Get(InlineSchema inline, Type root, MsgPackOptions options)
    {
      int hash = inline.TypesHash();
      Described[] all = _all;
      for (int t = 0; t < all.Length; t++)
      {
        Described known = all[t];
        if (known.Hash == hash && known.Root == root && inline.SameAs(known.Types, known.CollectionProps))
          return known.Bytes;
      }

      ByteWriter bytes = new ByteWriter();
      DescribedSchema.Write(bytes, inline.ToDefs(), root, options, options);
      byte[] schema = bytes.ToArray();
      if (all.Length < MaxCount)
      {
        Described made = new Described() { Root = root, Hash = hash, Types = inline.Types(), CollectionProps = inline.CollectionProps(), Bytes = schema };
        lock (_lock)
        {
          Described[] grown = new Described[_all.Length + 1];
          Array.Copy(_all, grown, _all.Length);
          grown[grown.Length - 1] = made;
          _all = grown;
        }
      }
      return schema;
    }
  }
}
