using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using LsMsgPack.IO;
using LsMsgPack.Writing;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace LsMsgPack
{
  internal sealed partial class Serializer
  {
    private readonly ConcurrentDictionary<Type, FullPropertyInfo> _rootInfos = new ConcurrentDictionary<Type, FullPropertyInfo>();

    [ThreadStatic]
    private static WriteState _threadState;

    /// <summary>
    /// The writers and context of the calls on one thread, reused (taken while a call runs, so a nested call gets its own).
    /// </summary>
    private sealed class WriteState
    {
      internal Serializer Owner;
      internal WriteContext Context;
      internal MsgPackWriter Body = new MsgPackWriter(1024);
      internal MsgPackWriter Schema = new MsgPackWriter(256);
      internal InlineSchema Inline = new InlineSchema();
    }

    private WriteState RentState()
    {
      WriteState state = _threadState;
      _threadState = null;
      if (state is null)
        state = new WriteState();
      if (!ReferenceEquals(state.Owner, this))
      {
        state.Owner = this;
        state.Context = new WriteContext(this) { Inline = state.Inline };
      }
      return state;
    }

    private static void ReturnState(WriteState state)
    {
      if (state.Body.Buf.Length > 1024 * 1024) // do not keep a huge buffer alive
        state.Body = new MsgPackWriter(1024);
      _threadState = state;
    }

    private FullPropertyInfo RootInfo(Type type)
    {
      if (_rootInfos.TryGetValue(type, out FullPropertyInfo info))
        return info;
      return _rootInfos.GetOrAdd(type, t => new FullPropertyInfo(t));
    }

    /// <summary>
    /// Writes the payload: the value (names mode), or the schema (or a reference to it) followed by the value.
    /// </summary>
    /// <param name="value">The value, written by <paramref name="write"/> (it is <see cref="WriteContext.Root"/>)</param>
    /// <param name="write">Writes the value (typed or boxed), may run twice with a schema reference (when the cached schema grows)</param>
    /// <param name="sink">Gets the payload instead of <paramref name="target"/> or <paramref name="result"/> (the stream writer)</param>
    internal void Serialize(Type assignedTo, object value, Action<WriteContext, FullPropertyInfo> write, Stream target, out byte[] result, IPayloadSink sink = null)
    {
      result = null;
      bool isNull = value is null;
      WriteState state = RentState();
      try
      {
        WriteContext c = state.Context;
        c.Root = value;
        c.Depth = 0;
        if (c.PathCount != 0)
          c.ClearPath();
        c.ByNames = false;
        if (!ReferenceEquals(c.W, state.Body)) // the fields of the context mostly keep their values: a comparison is cheaper than storing a reference (a write barrier)
          c.W = state.Body;
        c.W.Reset(Options);
        FullPropertyInfo root = c.RootInfo;
        if (root is null || root.AssignedToType != assignedTo) // calls on a thread usually write the same root type: no lookup (the hash of a Type is a call into the runtime)
          c.RootInfo = root = RootInfo(assignedTo);

        if (!Options._useInexedSchema || isNull) // null is serialized without a schema
        {
          c.Mode = IdMode.Names;
          if (!ReferenceEquals(c.IdSettings, Options))
            c.IdSettings = Options;
          write(c, root);
          Output(null, 0, false, c.W, target, sink, out result);
          return;
        }

        if (Options._writeSchemaReference)
        {
          SchemaStore store = Options._schemaStore;
          if (store is null)
            throw new MsgPackException("MsgPackSettings.WriteSchemaReference needs a MsgPackSettings.SchemaStore to keep the schema in.");

          // The published session of the root type, shared with other calls (as SchemaStore.RunWriter, without its allocations)
          SchemaStore.SessionState writer = WriterState(store, assignedTo);
          SchemaSession current = writer.Current;
          if (current != null)
          {
            try
            {
              c.Mode = IdMode.Session;
              c.IdSettings = WriterShared(current).Settings(Options);
              write(c, root);
              Output(current.Reference, current.Reference.Length, true, c.W, target, sink, out result);
              return;
            }
            catch (SchemaGrowthException) // something the session does not have yet, RunWriter grows it
            {
            }
          }

          SchemaSession session = WriteGrowing(store, assignedTo, c, root, write);
          Output(session.Reference, session.Reference.Length, true, c.W, target, sink, out result);
          return;
        }

        MsgPackWriter schema = state.Schema;
        schema.Reset(Options);
        if (!CustomPropertyIds && Filters != FilterMode.Custom)
        {
          // The schema of this call is built while writing (the same order as LsMsgPack's session)
          c.Mode = IdMode.Inline;
          c.Inline.Clear();
          if (!ReferenceEquals(c.IdSettings, Options))
            c.IdSettings = Options;
          write(c, root);
          if (Options._schemaVersion == SchemaVersion.Described)
            schema.Raw(_described.Get(c.Inline, assignedTo, Options));
          else
            c.Inline.WriteTo(schema);
        }
        else
        {
          // Custom property ids or filters see the ids of a session, as in LsMsgPack
          IndexedSchemaTypeResolver resolver = new IndexedSchemaTypeResolver();
          SchemaSession session = new SchemaSession(resolver, Options);
          c.Mode = IdMode.Session;
          c.IdSettings = session.Apply(Options);
          write(c, root);
          schema.Raw(resolver.Pack(Options, assignedTo, Options));
        }
        Output(schema.Buf, schema.Pos, false, c.W, target, sink, out result);
      }
      finally
      {
        state.Context.Root = null;
        ReturnState(state);
      }
    }

    /// <summary>
    /// Writes with the writer session of the root type under the store's lock, which grows it (in a method of its own: a lambda that captures parameters allocates its closure where the method starts, on every call).
    /// </summary>
    private SchemaSession WriteGrowing(SchemaStore store, Type assignedTo, WriteContext c, FullPropertyInfo root, Action<WriteContext, FullPropertyInfo> write)
    {
      SchemaSession session;
      store.RunWriter(assignedTo, Options, (s, sessionSettings) =>
      {
        c.Depth = 0;
        if (c.PathCount != 0)
          c.ClearPath();
        c.ByNames = false;
        c.W.Reset(Options);
        c.Mode = IdMode.Session;
        c.IdSettings = sessionSettings;
        write(c, root);
        return true;
      }, out session);
      return session;
    }

    #region Schema heads (the stream writer)

    private MsgPackOptions _completeOptions; // the options with CompleteSchemas, for the heads
    private SchemaStore _headStore; // the schemas of the heads, when the options have no store

    /// <summary>
    /// A schema written on its own, for the body that follows it in a stream (<see cref="Streaming.MsgPackStreamWriter.WriteHead{TRoot}"/>): written in parts, each with this schema.
    /// </summary>
    internal sealed class WriterHead
    {
      internal readonly SchemaStore Store;
      internal readonly Type Root;

      /// <summary>
      /// The options with <see cref="MsgPackOptions.CompleteSchemas"/>.
      /// </summary>
      internal readonly MsgPackOptions Settings;
      internal readonly SchemaId Id;

      /// <summary>
      /// Frozen, the schema of <see cref="Id"/>: replaced by a copy with more cache entries (never more schema).
      /// </summary>
      internal SchemaSession Session;

      internal WriterHead(SchemaStore store, Type root, MsgPackOptions settings, SchemaSession session)
      {
        Store = store;
        Root = root;
        Settings = settings;
        Session = session;
        Id = session.Id;
      }
    }

    /// <summary>
    /// The head of a root type: the complete schema of the root type (<see cref="MsgPackOptions.CompleteSchemas"/>, whatever the options say), published in the options' store or a store of this serializer
    /// (<see cref="SchemaStore.GetInlineSchema"/> has it), so the body after the head has every class it can hold.
    /// </summary>
    internal WriterHead PrepareHead(Type root)
    {
      SchemaStore store = HeadStore();

      MsgPackOptions settings = Options;
      if (!settings._completeSchemas)
      {
        settings = _completeOptions;
        if (settings is null)
        {
          settings = Options.CloneOptions(); // shares the arrays, so the session key is the same every time
          settings._completeSchemas = true;
          _completeOptions = settings;
        }
      }

      store.PrepareWriter(root, settings);
      return new WriterHead(store, root, settings, store.GetWriter(root, settings).Current); // the prepared session, or a grown one of the same root type
    }

    /// <summary>
    /// Where the schemas of the heads are kept: the options' store, or one of this serializer.
    /// </summary>
    internal SchemaStore HeadStore()
    {
      SchemaStore store = Options._schemaStore;
      if (store is null)
      {
        store = _headStore;
        if (store is null)
          _headStore = store = new SchemaStore();
      }
      return store;
    }

    /// <summary>
    /// Writes a value of the body of a schema head, as <see cref="Serialize"/> writes the body of a payload with that schema.
    /// </summary>
    /// <param name="assignedTo">The declared type of the value (a type id is added when the value's type differs)</param>
    /// <param name="depth">The nesting of the value in the stream (for <see cref="MsgPackOptions.MaxDepth"/>)</param>
    /// <exception cref="InvalidOperationException">The value needs a class or property the schema of the head does not have, nothing was written</exception>
    internal void SerializeBody(WriterHead head, Type assignedTo, object value, Action<WriteContext, FullPropertyInfo> write, int depth, IPayloadSink sink)
    {
      WriteState state = RentState();
      try
      {
        WriteContext c = state.Context;
        c.Root = value;
        FullPropertyInfo root = c.RootInfo;
        if (root is null || root.AssignedToType != assignedTo)
          c.RootInfo = root = RootInfo(assignedTo);
        try
        {
          WriteBody(c, state, depth, WriterShared(head.Session).Settings(Options), write, root);
        }
        catch (SchemaGrowthException) // the frozen session needs more: cache entries (the first value of a class), or classes the head does not have
        {
          SchemaSession grown = head.Session.Thaw(head.Settings);
          WriteBody(c, state, depth, grown.Apply(head.Settings), write, root);
          byte[] schema = grown.Resolver.Pack(SchemaBytes.Canonical, head.Root, head.Settings); // as SchemaStore.PublishWriter
          if (SchemaId.Compute(schema, head.Settings._schemaVersion) != head.Id)
            throw new InvalidOperationException($"The {(value is null ? "value" : value.GetType().Name)} declared as {assignedTo.Name} needs a class the schema written in the head does not have: a value declared as object, or a class of an assembly that {head.Root.Name} does not reach (see {nameof(MsgPackOptions.CompleteSchemas)}). Nothing was written for it.");
          grown.Freeze();
          head.Session = grown;
        }
        sink.Write(null, 0, false, c.W);
      }
      finally
      {
        state.Context.Root = null;
        ReturnState(state);
      }
    }

    private static void WriteBody(WriteContext c, WriteState state, int depth, MsgPackOptions sessionSettings, Action<WriteContext, FullPropertyInfo> write, FullPropertyInfo root)
    {
      c.Depth = depth;
      if (c.PathCount != 0)
        c.ClearPath();
      c.ByNames = false;
      if (!ReferenceEquals(c.W, state.Body))
        c.W = state.Body;
      c.W.Reset(c.Serializer.Options);
      c.Mode = IdMode.Session;
      c.IdSettings = sessionSettings;
      write(c, root);
    }

    #endregion

    /// <summary>
    /// The writer sessions of the root type read last: (store, root type) to SessionState, which SchemaStore.GetWriter finds by a key of the settings, which do not change.
    /// </summary>
    private volatile WriterHit _lastWriter;

    /// <summary>
    /// The inline schemas of <see cref="SchemaVersion.Described"/>, made once per root type and sequence of types.
    /// </summary>
    private readonly DescribedInlineSchemas _described = new DescribedInlineSchemas();

    private sealed class WriterHit
    {
      internal readonly SchemaStore Store;
      internal readonly Type Root;
      internal readonly SchemaStore.SessionState State;

      internal WriterHit(SchemaStore store, Type root, SchemaStore.SessionState state)
      {
        Store = store;
        Root = root;
        State = state;
      }
    }

    private volatile SessionShared _lastWriterSession; // apart from the one of reading, a serializer may do both

    private SessionShared WriterShared(SchemaSession session)
    {
      SessionShared last = _lastWriterSession;
      if (last != null && ReferenceEquals(last.Session, session))
        return last;
      last = _sessions.GetValue(session, s => new SessionShared(s));
      _lastWriterSession = last;
      return last;
    }

    private SchemaStore.SessionState WriterState(SchemaStore store, Type root)
    {
      WriterHit last = _lastWriter;
      if (last != null && last.Root == root && ReferenceEquals(last.Store, store))
        return last.State;
      SchemaStore.SessionState state = store.GetWriter(root, Options);
      _lastWriter = new WriterHit(store, root, state);
      return state;
    }

    /// <param name="reference">The head is a reference to a schema of the store (<see cref="SchemaStore.ReferenceLength"/> bytes)</param>
    private static void Output(byte[] head, int headLength, bool reference, MsgPackWriter body, Stream target, IPayloadSink sink, out byte[] result)
    {
      if (sink != null)
      {
        result = null;
        sink.Write(head, headLength, reference, body);
        return;
      }

      if (target != null)
      {
        result = null;
        if (headLength > 0)
          target.Write(head, 0, headLength);
        body.CopyTo(target);
        return;
      }

      result = MsgPackWriter.NewBytes(headLength + body.Pos); // filled below
      if (headLength > 0)
        new ReadOnlySpan<byte>(head, 0, headLength).CopyTo(result); // Buffer.BlockCopy checks the array types first
      new ReadOnlySpan<byte>(body.Buf, 0, body.Pos).CopyTo(new Span<byte>(result, headLength, body.Pos));
    }
  }

  /// <summary>
  /// Takes the payload of a Serialize call: the head (the schema, a reference to it, or nothing) and the body (<see cref="Streaming.MsgPackStreamWriter"/>).
  /// </summary>
  internal interface IPayloadSink
  {
    void Write(byte[] head, int headLength, bool reference, MsgPackWriter body);
  }
}
