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
    /// <param name="write">Writes the value (typed or boxed), may run twice with a schema reference (when the cached schema grows)</param>
    internal void Serialize(Type assignedTo, bool isNull, Action<WriteContext, FullPropertyInfo> write, Stream target, out byte[] result)
    {
      result = null;
      WriteState state = RentState();
      try
      {
        WriteContext c = state.Context;
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
        ReturnState(state);
      }
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
