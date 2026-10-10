using LsMsgPack;
using LsMsgPack.Meta;
using LtMsgPack.IO;
using System;
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
  internal sealed class WriteContext
  {
    internal readonly Serializer Serializer;
    internal MsgPackWriter W;

    /// <summary>
    /// The value of the Serialize call.
    /// </summary>
    internal object Root;
    internal IdMode Mode;
    internal InlineSchema Inline;

    /// <summary>
    /// The settings the ids are resolved with: the options of the serializer, or in <see cref="IdMode.Session"/> a copy using the session.
    /// </summary>
    internal MsgPackOptions IdSettings;
    internal int Depth;
    private List<MsgPackWriter> _spareWriters;

    internal WriteContext(Serializer serializer)
    {
      Serializer = serializer;
    }

    internal bool UsesSchema { get { return Mode != IdMode.Names; } }

    internal void EnterContainer()
    {
      if (++Depth > Serializer.Options._maxDepth)
        throw new MsgPackException($"The object graph is nested deeper than {nameof(LtMsgPackOptions)}.{nameof(LtMsgPackOptions.MaxDepth)} ({Serializer.Options._maxDepth}), it may contain a cycle.");
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
    private readonly List<SchemaTypeInfo> _types = new List<SchemaTypeInfo>(8);
    private readonly List<List<string>> _collectionProps = new List<List<string>>(8); // collections get their properties when they are resolved (ComplexTypeDef.AddProp)

    internal void Clear()
    {
      _types.Clear();
      _collectionProps.Clear();
    }

    /// <returns>The type id: the position of the type in the schema</returns>
    internal int Register(SchemaTypeInfo info)
    {
      List<SchemaTypeInfo> types = _types;
      for (int t = 0; t < types.Count; t++) // a handful of types, a scan is faster than hashing
        if (ReferenceEquals(types[t], info))
          return t;

      types.Add(info);
      _collectionProps.Add(info.IsCollection ? new List<string>() : null);
      return types.Count - 1;
    }

    /// <summary>
    /// The property ids of the type are resolved (the first time in this call): it is added to the schema, a collection gets its property names.
    /// </summary>
    internal void PropertiesResolved(SchemaTypeInfo info)
    {
      int count = _types.Count;
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
      writer.MapHeader(_types.Count);
      for (int t = 0; t < _types.Count; t++)
      {
        SchemaTypeInfo info = _types[t];
        if (!info.IsCollection)
        {
          writer.Raw(info.Entry ?? (info.Entry = EncodeEntry(writer, info)));
          continue;
        }

        WriteName(writer, info.Name);
        IList<string> props = info.IsCollection ? (IList<string>)_collectionProps[t] : info.PropertyNames;
        writer.ArrayHeader(props.Count);
        for (int p = 0; p < props.Count; p++)
          WriteName(writer, props[p]);
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
}
