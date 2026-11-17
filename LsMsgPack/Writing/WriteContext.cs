using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using LsMsgPack.IO;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LsMsgPack.Writing
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
  /// <para>Public for the code of the source generator (see LsMsgPack.LsMsgPackContext): may change between releases.</para>
  [EditorBrowsable(EditorBrowsableState.Never)]
  public sealed class WriteContext
  {
    internal readonly Serializer Serializer;
    public MsgPackWriter W;

    /// <summary>
    /// The value of the Serialize call: a struct of a typed call is in a <see cref="RootSlot{T}"/> (see LsMsgPackSerializer.TypedWriter), other values as they are (boxed for untyped calls).
    /// </summary>
    internal object Root;

    /// <summary>
    /// What the calls on this thread looked up for the root type of this call (calls usually write the same root type), the context belongs to one serializer: see <see cref="Serializer.WriteRootEntry"/>.
    /// </summary>
    internal Serializer.WriteRootEntry Entry;

    /// <summary>
    /// The entries of the root types written on this thread before (<see cref="Serializer.EntryOf"/>), made when a second root type shows up: a server writing several kinds of messages in turn looks up nothing.
    /// </summary>
    internal Serializer.WriteRootEntry[] Entries;
    internal int NextEntry; // where the next root type goes when all of Entries are taken
    internal IdMode Mode;
    internal InlineSchema Inline;

    /// <summary>
    /// The settings the ids are resolved with: the options of the serializer, or in <see cref="IdMode.Session"/> a copy using the session.
    /// </summary>
    internal MsgPackOptions IdSettings;
    internal int Depth;

    /// <summary>
    /// The serialization callbacks of a write that may be repeated (a cached schema that grows): called once per object (Core's CallbackReplay, started by the entry code of those writes).
    /// </summary>
    internal readonly CallbackReplay Replay = new CallbackReplay();

    /// <summary>
    /// <see cref="ReferenceHandling.IgnoreCycles"/>: the objects with properties being written, the root first (LsMsgPack: CyclePath). Null until the first.
    /// </summary>
    private PathEntry[] _path;
    internal int PathCount;

    /// <summary>
    /// An element of the path: a struct, so a store has no covariance check (an object[] might be a string[]: CastHelpers.StelemRef per object).
    /// </summary>
    private struct PathEntry
    {
      internal object Value;
    }

    /// <returns>false when the object is already being written (a cycle: write nil instead), otherwise it is on the path until <see cref="LeaveObject"/></returns>
    internal bool EnterObject(object value)
    {
      PathEntry[] path = _path;
      int count = PathCount;
      for (int t = count - 1; t >= 0; t--)
        if (ReferenceEquals(path[t].Value, value))
          return false;
      if (path is null || count == path.Length)
        path = GrowPath();
      path[count].Value = value;
      PathCount = count + 1;
      return true;
    }

    private PathEntry[] GrowPath()
    {
      PathEntry[] path = _path;
      Array.Resize(ref path, path is null ? 16 : path.Length * 2);
      return _path = path;
    }

    internal void LeaveObject()
    {
      _path[--PathCount].Value = null; // not kept alive after the call
    }

    /// <summary>
    /// At the start of a call: a call that failed left its path.
    /// </summary>
    internal void ClearPath()
    {
      Array.Clear(_path, 0, PathCount);
      PathCount = 0;
    }

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
      return new MsgPackException($"The object graph is nested deeper than {nameof(LsMsgPackOptions)}.{nameof(LsMsgPackOptions.MaxDepth)} ({Serializer.Options._maxDepth}), it may contain a cycle.");
    }

    internal void LeaveContainer()
    {
      Depth--;
    }

    /// <summary>
    /// A writer for content that has to be written before its header is known (the elements of a collection that is wrapped with a type id: the id is only known after the elements, as in LsMsgPack).
    /// </summary>
    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/>: the objects and collections of the value being written (null without the option, made at its first write).
    /// </summary>
    internal ReferenceWriting References;

    /// <summary>
    /// <see cref="ReferenceHandling.Preserve"/> in the body of a stream head: where the value is in the body's root value, the start of its paths (<see cref="ReferenceWriting.InKey"/>
    /// inside a key), so the body stays the payload of Serialize of the root. Null for a root value.
    /// </summary>
    internal int[] ReferencePrefix;

    /// <summary>
    /// Copies the content of a rented writer (the tracked values written there move with it).
    /// </summary>
    internal void CopyRented(MsgPackWriter from, MsgPackWriter to)
    {
      References?.Copied(from, to, to.Pos);
      to.Raw(from.Buf, 0, from.Pos);
    }

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
      writer.Moves = References; // null without ReferenceHandling.Preserve
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
          return SessionIdsOfAnother(plan, out infos);
      }
    }

    /// <summary>
    /// <see cref="BeginObject"/> in another session than the plan's last one: the plan's table of sessions first (a class below several root types is written in the session of each), then the session itself.
    /// </summary>
    private byte[][] SessionIdsOfAnother(ObjectPlan plan, out FullPropertyInfo[] infos)
    {
      MsgPackOptions settings = IdSettings;
      ObjectPlan.SessionIds previous = plan.PreviousSession;
      ObjectPlan.SessionIds known = previous != null && ReferenceEquals(previous.Settings, settings) ? previous : plan.KnownSession(settings);
      if (known != null)
      {
        plan.PreviousSession = plan.LastSession; // each is checked before it is used: another thread may swap them at the same time
        plan.LastSession = known;
        infos = known.Infos;
        return known.Keys;
      }

      infos = FullPropertyInfo.GetSerializedProps(plan.Type, settings);
      if (infos.Length != plan.Props.Length)
        throw new InvalidOperationException($"The properties of {plan.Type.FullName} differ from the ones of the schema session.");
      byte[][] keys = Serializer.SessionKeys(infos);
      if (settings._schemaFrozen) // shared by the calls that use the session (see Serializer.SessionShared), a session of one call is not worth it
      {
        ObjectPlan.SessionIds ids = new ObjectPlan.SessionIds(settings, infos, keys);
        ObjectPlan.SessionIds last = plan.LastSession;
        if (last != null) // a second session: from now on they are kept
        {
          ids = plan.KeepSession(last, ids);
          plan.PreviousSession = last;
        }
        plan.LastSession = ids;
      }
      return keys;
    }

    /// <summary>
    /// The names an object keeps as extension data (ObjectPlan.WriteExtended) as their positions in its type's entry: of the inline schema, or of the session (LsMsgPackDebug's order: after the type id, before the values).
    /// </summary>
    internal List<KeptEntry> KeptIds(ObjectPlan plan, List<KeptEntry> kept)
    {
      if (Mode != IdMode.Inline)
        return ExtensionDataMember.WithSchemaIds(kept, plan.Type, IdSettings);
      for (int t = 0; t < kept.Count; t++)
      {
        int position = Inline.KeptName(plan.Schema, (string)kept[t].Key);
        kept[t] = new KeptEntry(position, position, kept[t].Bytes);
      }
      return kept;
    }

    /// <summary>
    /// <see cref="WriteTypeId(Type, FullPropertyInfo)"/> of the type of a plan (which has its schema entry: no lookup).
    /// </summary>
    internal void WriteTypeId(ObjectPlan plan, FullPropertyInfo assignedTo)
    {
      if (Mode == IdMode.Inline)
        W.Int32(Inline.Register(plan.Schema));
      else
        WriteTypeId(plan.Type, assignedTo);
    }

    /// <summary>
    /// Writes the type id (LsMsgPack: GetTypeIdentifier), which adds the type to the indexed schema.
    /// </summary>
    internal void WriteTypeId(Type type, FullPropertyInfo assignedTo)
    {
      switch (Mode)
      {
        case IdMode.Names:
          Serializer.WriteId(this, Serializer.NameTypeId(type, assignedTo));
          return;

        case IdMode.Inline:
          W.Int32(Inline.Register(Serializer.GetSchemaInfo(type)));
          return;

        default:
          Serializer.WriteId(this, SerializationRules.GetTypeIdentifier(type, IdSettings, assignedTo));
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

    /// <summary>
    /// The entry with the names its objects kept as extension data last (InlineSchema.KeptName), encoded once for those names: replaced as a whole (several threads write with the serializer).
    /// </summary>
    internal volatile KeptEntryBytes WithKept;

    internal sealed class KeptEntryBytes
    {
      internal readonly string[] Names;
      internal readonly byte[] Entry;

      internal KeptEntryBytes(string[] names, byte[] entry)
      {
        Names = names;
        Entry = entry;
      }

      internal bool Has(List<string> names)
      {
        if (names.Count != Names.Length)
          return false;
        for (int t = 0; t < Names.Length; t++)
          if (!string.Equals(Names[t], names[t], StringComparison.Ordinal))
            return false;
        return true;
      }
    }
  }

  /// <summary>
  /// The indexed schema of one Serialize call, built in the same order as LsMsgPack builds its IndexedSchemaTypeResolver, so the bytes are the same.
  /// </summary>
  internal sealed class InlineSchema
  {
    // Arrays with a count rather than lists: Clear runs once per call, List.Clear calls Array.Clear
    private SchemaTypeInfo[] _types = new SchemaTypeInfo[8];
    private List<string>[] _collectionProps = new List<string>[8]; // collections get their properties when they are resolved (ComplexTypeDef.AddProp), objects the names they keep as extension data (null for none)
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
    /// The position of a name an object of the type keeps as extension data: after the type's own names, added when it is new (as IndexedSchemaTypeResolver.IdOfKeptName adds it).
    /// </summary>
    internal int KeptName(SchemaTypeInfo info, string name)
    {
      int id = Register(info);
      string[] names = info.PropertyNames;
      for (int t = 0; t < names.Length; t++)
        if (string.Equals(names[t], name, StringComparison.Ordinal))
          return t;
      List<string> kept = _collectionProps[id] ?? (_collectionProps[id] = new List<string>());
      int at = kept.IndexOf(name);
      if (at < 0)
      {
        at = kept.Count;
        kept.Add(name);
      }
      return names.Length + at;
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
          List<string> kept = _collectionProps[t];
          if (kept is null)
          {
            writer.Raw(info.Entry ?? (info.Entry = EncodeEntry(writer, info, null)));
            continue;
          }
          SchemaTypeInfo.KeptEntryBytes withKept = info.WithKept; // with the names kept as extension data after its own ones
          if (withKept is null || !withKept.Has(kept))
            info.WithKept = withKept = new SchemaTypeInfo.KeptEntryBytes(kept.ToArray(), EncodeEntry(writer, info, kept));
          writer.Raw(withKept.Entry);
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
        if (!info.IsCollection && _collectionProps[t] != null)
          props.AddRange(_collectionProps[t]); // the names kept as extension data
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
        string[] other = collectionProps[t];
        if (props is null || other is null)
        {
          if (props is null && other is null)
            continue;
          return false;
        }
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

    /// <param name="kept">The names objects of the type kept as extension data (after its own ones), null for none</param>
    private static byte[] EncodeEntry(MsgPackWriter like, SchemaTypeInfo info, List<string> kept)
    {
      MsgPackWriter writer = like.Like(64);
      WriteName(writer, info.Name);
      writer.ArrayHeader(info.PropertyNames.Length + (kept is null ? 0 : kept.Count));
      for (int p = 0; p < info.PropertyNames.Length; p++)
        WriteName(writer, info.PropertyNames[p]);
      if (kept != null)
        for (int p = 0; p < kept.Count; p++)
          WriteName(writer, kept[p]);
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
