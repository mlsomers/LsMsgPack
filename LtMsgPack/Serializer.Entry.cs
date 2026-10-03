using LsMsgPack;
using LsMsgPack.Meta;
using LsMsgPack.TypeResolving.Types;
using LtMsgPack.IO;
using LtMsgPack.Writing;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace LtMsgPack
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
        state.Context = new WriteContext(this);
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
    internal void Serialize(Type assignedTo, object value, Action<WriteContext, FullPropertyInfo> write, Stream target, out byte[] result)
    {
      result = null;
      bool isNull = value is null;
      WriteState state = RentState();
      try
      {
        WriteContext c = state.Context;
        c.Root = value;
        c.Depth = 0;
        c.W = state.Body;
        c.W.Reset(Options);
        FullPropertyInfo root = RootInfo(assignedTo);

        if (!Options._useInexedSchema || isNull) // null is serialized without a schema
        {
          c.Mode = IdMode.Names;
          c.IdSettings = Options;
          write(c, root);
          Output(null, 0, c.W, target, out result);
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
              Output(current.Reference, current.Reference.Length, c.W, target, out result);
              return;
            }
            catch (SchemaGrowthException) // something the session does not have yet, RunWriter grows it
            {
            }
          }

          SchemaSession session;
          store.RunWriter(assignedTo, Options, (s, sessionSettings) =>
          {
            c.Depth = 0;
            c.W.Reset(Options);
            c.Mode = IdMode.Session;
            c.IdSettings = sessionSettings;
            write(c, root);
            return true;
          }, out session);
          Output(session.Reference, session.Reference.Length, c.W, target, out result);
          return;
        }

        MsgPackWriter schema = state.Schema;
        schema.Reset(Options);
        if (!CustomPropertyIds && Filters != FilterMode.Custom)
        {
          // The schema of this call is built while writing (the same order as LsMsgPack's session)
          c.Mode = IdMode.Inline;
          c.Inline = state.Inline;
          c.Inline.Clear();
          c.IdSettings = Options;
          write(c, root);
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
          schema.Raw(resolver.Pack(Options));
        }
        Output(schema.Buf, schema.Pos, c.W, target, out result);
      }
      finally
      {
        state.Context.Root = null;
        ReturnState(state);
      }
    }

    /// <summary>
    /// The writer sessions of the root type read last: (store, root type) to SessionState, which SchemaStore.GetWriter finds by a key of the settings, which do not change.
    /// </summary>
    private volatile WriterHit _lastWriter;

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

    private static void Output(byte[] head, int headLength, MsgPackWriter body, Stream target, out byte[] result)
    {
      if (target != null)
      {
        result = null;
        if (headLength > 0)
          target.Write(head, 0, headLength);
        body.CopyTo(target);
        return;
      }

      result = new byte[headLength + body.Pos];
      if (headLength > 0)
        Buffer.BlockCopy(head, 0, result, 0, headLength);
      Buffer.BlockCopy(body.Buf, 0, result, headLength, body.Pos);
    }
  }
}
